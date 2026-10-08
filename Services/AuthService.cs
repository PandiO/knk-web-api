using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AutoMapper;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Service for authentication workflows: login, refresh, logout, and current-user retrieval.
    /// </summary>
    public class AuthService : IAuthService
    {
        private readonly IUserRepository _userRepository;
        private readonly ITokenService _tokenService;
        private readonly IPasswordService _passwordService;
        private readonly IMapper _mapper;
        private readonly ILinkCodeRepository _linkCodeRepository;
        private readonly IPasswordResetDeliveryService _passwordResetDeliveryService;
        private readonly IMemoryCache _memoryCache;
        private readonly SecuritySettings _securitySettings;
        private readonly ILogger<AuthService> _logger;
        private readonly IRefreshTokenRepository _refreshTokens;
        private readonly ISessionRevocationService _sessionRevocation;
        private readonly ILinkCodeService _linkCodeService;

        public AuthService(
            IUserRepository userRepository,
            ITokenService tokenService,
            IPasswordService passwordService,
            IMapper mapper,
            ILinkCodeRepository linkCodeRepository,
            IPasswordResetDeliveryService passwordResetDeliveryService,
            IMemoryCache memoryCache,
            IOptions<SecuritySettings> securitySettings,
            ILogger<AuthService> logger,
            IRefreshTokenRepository refreshTokens,
            ISessionRevocationService sessionRevocation,
            ILinkCodeService linkCodeService)
        {
            _linkCodeService = linkCodeService;
            _refreshTokens = refreshTokens;
            _sessionRevocation = sessionRevocation;
            _userRepository = userRepository;
            _tokenService = tokenService;
            _passwordService = passwordService;
            _mapper = mapper;
            _linkCodeRepository = linkCodeRepository;
            _passwordResetDeliveryService = passwordResetDeliveryService;
            _memoryCache = memoryCache;
            _securitySettings = securitySettings.Value;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<(bool Ok, AuthLoginResponseDto? Result, string? Error)> LoginAsync(string login, string password, bool rememberMe, string? clientIp = null, string? userAgent = null)
        {
            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            {
                return (false, null, "Email or Minecraft name and password are required.");
            }

            // Closed-alpha WP5 (D3): log in with the email or the Minecraft name, case-insensitive.
            var normalizedEmail = login.Trim().ToLowerInvariant();
            var user = await FindByLoginAsync(login);

            if (user == null || string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                _logger.LogWarning("Login failed for {Email}: user not found or missing password hash", normalizedEmail);
                return (false, null, "Invalid credentials.");
            }

            if (!user.IsActive || user.DeletedAt.HasValue)
            {
                _logger.LogWarning("Login blocked for {Email}: inactive or deleted", normalizedEmail);
                return (false, null, "Account is inactive or deleted.");
            }

            var passwordValid = await _passwordService.VerifyPasswordAsync(password, user.PasswordHash);
            if (!passwordValid)
            {
                _logger.LogWarning("Login failed for {Email}: invalid password", normalizedEmail);
                return (false, null, "Invalid credentials.");
            }

            var (accessToken, expiresIn, session) = await IssueSessionAsync(user, rememberMe, null, clientIp, userAgent);

            var response = new AuthLoginResponseDto
            {
                AccessToken = accessToken,
                RefreshToken = null,
                ExpiresIn = expiresIn,
                User = _mapper.Map<UserDto>(user),
                Session = session
            };

            _logger.LogInformation("Login succeeded for user {UserId} ({Email})", user.Id, normalizedEmail);
            return (true, response, null);
        }

        /// <inheritdoc/>
        public async Task<(bool Ok, AuthRefreshResponseDto? Result, string? Error)> RefreshAsync(string refreshToken, string? clientIp = null, string? userAgent = null)
        {
            const string invalid = "Invalid or expired refresh token.";
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return (false, null, "Refresh token is required.");
            }

            var now = DateTime.UtcNow;
            var stored = await _refreshTokens.GetByHashAsync(_tokenService.HashRefreshToken(refreshToken.Trim()));
            if (stored == null)
            {
                _logger.LogWarning("Refresh failed: unknown refresh token");
                return (false, null, invalid);
            }

            if (stored.RevokedAt.HasValue)
            {
                // Two tabs refreshing with the same cookie at once: the loser presents a token rotated
                // a moment ago. While its replacement is still live, give it a sibling in the same
                // family instead of treating it as theft.
                var grace = TimeSpan.FromSeconds(Math.Max(0, _securitySettings.Jwt.RefreshReuseGraceSeconds));
                if (stored.ReplacedByHash != null && now - stored.RevokedAt.Value <= grace)
                {
                    // Follow the rotation chain (three tabs at once rotate it more than once).
                    var replacement = await _refreshTokens.GetByHashAsync(stored.ReplacedByHash);
                    for (var hop = 0; hop < 5 && replacement?.RevokedAt != null && replacement.ReplacedByHash != null
                         && now - replacement.RevokedAt.Value <= grace; hop++)
                    {
                        replacement = await _refreshTokens.GetByHashAsync(replacement.ReplacedByHash);
                    }
                    if (replacement != null && replacement.RevokedAt == null && replacement.ExpiresAt > now)
                    {
                        _logger.LogInformation("Refresh for user {UserId} with a token rotated {Seconds:0.0}s ago (concurrent refresh): issued a sibling token",
                            stored.UserId, (now - stored.RevokedAt.Value).TotalSeconds);
                        return await IssueRotatedAsync(stored, replacement, now, clientIp, userAgent, markSpent: false);
                    }
                }

                // Reuse of a rotated or revoked token: someone else may hold this family. End it.
                var revoked = await _refreshTokens.RevokeFamilyAsync(stored.FamilyId, now);
                _logger.LogWarning("Refresh token reuse detected for user {UserId}: revoked session family ({Count} tokens)",
                    stored.UserId, revoked);
                return (false, null, invalid);
            }

            if (stored.ExpiresAt <= now)
            {
                _logger.LogInformation("Refresh failed for user {UserId}: refresh token expired", stored.UserId);
                return (false, null, invalid);
            }

            return await IssueRotatedAsync(stored, stored, now, clientIp, userAgent, markSpent: true);
        }

        /// <summary>
        /// Issues the next refresh token of <paramref name="presented"/>'s family (and an access
        /// token). With <paramref name="markSpent"/> the presented token is revoked and linked to its
        /// successor (normal rotation); without it (the concurrent-refresh grace) nothing is revoked.
        /// </summary>
        private async Task<(bool Ok, AuthRefreshResponseDto? Result, string? Error)> IssueRotatedAsync(
            RefreshToken presented, RefreshToken lifetimeSource, DateTime now, string? clientIp, string? userAgent, bool markSpent)
        {
            var user = await _userRepository.GetByIdAsync(presented.UserId);
            if (user == null || !user.IsActive || user.DeletedAt.HasValue)
            {
                await _refreshTokens.RevokeFamilyAsync(presented.FamilyId, now);
                _logger.LogWarning("Refresh failed for user {UserId}: not found or inactive", presented.UserId);
                return (false, null, "Invalid or expired refresh token.");
            }

            var next = NewRefreshToken(user.Id, presented.FamilyId, lifetimeSource.RememberMe, clientIp, userAgent, out var raw);
            if (markSpent)
            {
                presented.RevokedAt = now;
                presented.ReplacedByHash = next.TokenHash;
            }
            await _refreshTokens.AddAsync(next);

            var accessToken = await _tokenService.GenerateAccessTokenAsync(user, presented.FamilyId);
            var response = new AuthRefreshResponseDto
            {
                AccessToken = accessToken,
                RefreshToken = null,
                ExpiresIn = await CalculateExpiresInSecondsAsync(accessToken),
                Session = new IssuedRefreshToken(raw, next.ExpiresAt, next.RememberMe)
            };

            _logger.LogInformation("Refresh succeeded for user {UserId}", user.Id);
            return (true, response, null);
        }

        /// <inheritdoc/>
        public async Task LogoutAsync(string? refreshToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                _logger.LogInformation("Logout without a refresh token");
                return;
            }

            var stored = await _refreshTokens.GetByHashAsync(_tokenService.HashRefreshToken(refreshToken.Trim()));
            if (stored == null)
            {
                _logger.LogInformation("Logout with an unknown refresh token");
                return;
            }

            var revoked = await _refreshTokens.RevokeFamilyAsync(stored.FamilyId, DateTime.UtcNow);
            _logger.LogInformation("Logout for user {UserId}: revoked session family ({Count} tokens)", stored.UserId, revoked);
        }

        /// <inheritdoc/>
        public Task RevokeAllSessionsAsync(int userId, string reason) =>
            _sessionRevocation.RevokeAllSessionsAsync(userId, reason);

        /// <inheritdoc/>
        public async Task<AuthRegisterOutcome> RegisterAsync(AuthRegisterRequestDto request, string? clientIp = null, string? userAgent = null)
        {
            if (request == null)
            {
                return AuthRegisterOutcome.Fail("InvalidRequest", "Registration payload is required.");
            }

            // D1/D2: a code from the game server (/account link) is the only way in during the alpha.
            if (string.IsNullOrWhiteSpace(request.LinkCode))
            {
                return _securitySettings.Registration.AllowWebFirst
                    ? AuthRegisterOutcome.Fail("WebFirstUnavailable", "Web-only sign-up isn't available yet. Join the server and run /account link to get your registration code.")
                    : AuthRegisterOutcome.Fail(AuthRegisterOutcome.RegistrationNeedsCode, "Join the server and run /account link to get your registration code.");
            }

            var (codeValid, linkCode, _) = await _linkCodeService.ValidateLinkCodeAsync(request.LinkCode);
            if (!codeValid || linkCode?.UserId == null)
            {
                return AuthRegisterOutcome.Fail("InvalidLinkCode", "This code is invalid or has expired. Run /account link in game for a new one.");
            }

            var user = await _userRepository.GetByIdAsync(linkCode.UserId.Value);
            if (user == null || string.IsNullOrWhiteSpace(user.Uuid) || !user.IsActive || user.DeletedAt.HasValue)
            {
                // Codes from the web (generate-link-code for a web-only account) don't name a Minecraft account.
                return AuthRegisterOutcome.Fail("InvalidLinkCode", "This code doesn't belong to a Minecraft account. Run /account link in game for a new one.");
            }

            if (!string.IsNullOrEmpty(user.PasswordHash))
            {
                return AuthRegisterOutcome.Fail("AlreadyRegistered", "This Minecraft account already has a web login. Log in or reset your password.");
            }

            var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
            if (!IsValidEmail(email))
            {
                return AuthRegisterOutcome.Fail("InvalidEmail", "Enter a valid email address.");
            }
            if (await _userRepository.IsEmailTakenAsync(email, user.Id))
            {
                return AuthRegisterOutcome.Fail("DuplicateEmail", "This email is already in use by another account.");
            }

            if (request.Password != request.PasswordConfirmation)
            {
                return AuthRegisterOutcome.Fail("PasswordMismatch", "Password and confirmation do not match.");
            }
            var (passwordValid, passwordError) = await _passwordService.ValidatePasswordAsync(request.Password ?? string.Empty);
            if (!passwordValid)
            {
                return AuthRegisterOutcome.Fail("InvalidPassword", passwordError ?? "Password does not meet the requirements.");
            }

            var (consumed, _, _) = await _linkCodeService.ConsumeLinkCodeAsync(request.LinkCode);
            if (!consumed)
            {
                return AuthRegisterOutcome.Fail("InvalidLinkCode", "This code is invalid or has expired. Run /account link in game for a new one.");
            }

            // AccountCreatedVia stays as it was (MinecraftServer): the account came from the game.
            user.Email = email;
            user.EmailVerified = false;
            user.PasswordHash = await _passwordService.HashPasswordAsync(request.Password!);
            user.LastPasswordChangeAt = DateTime.UtcNow;
            await _userRepository.UpdateUserAsync(user);

            var (accessToken, expiresIn, session) = await IssueSessionAsync(user, false, null, clientIp, userAgent);
            _logger.LogInformation("Web login registered for Minecraft account {UserId} ({Username})", user.Id, user.Username);
            return AuthRegisterOutcome.Success(new AuthLoginResponseDto
            {
                AccessToken = accessToken,
                RefreshToken = null,
                ExpiresIn = expiresIn,
                User = _mapper.Map<UserDto>(user),
                Session = session
            });
        }

        private static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email) || email.Length > 256)
            {
                return false;
            }
            try
            {
                return new System.Net.Mail.MailAddress(email).Address == email;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public async Task<UserDto?> GetCurrentUserAsync(int userId)
        {
            if (userId <= 0)
            {
                return null;
            }

            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null || !user.IsActive || user.DeletedAt.HasValue)
            {
                return null;
            }

            return _mapper.Map<UserDto>(user);
        }

        /// <inheritdoc/>
        public async Task<(bool Ok, AuthUpdateResponseDto? Result, string? Error)> UpdateUserAsync(int userId, AuthUpdateRequestDto request, string? currentRefreshToken = null, string? clientIp = null, string? userAgent = null)
        {
            if (userId <= 0)
            {
                return (false, null, "Invalid user ID.");
            }

            if (request == null)
            {
                return (false, null, "Update request is required.");
            }

            // Validate that at least one field is being updated
            var hasEmailUpdate = !string.IsNullOrWhiteSpace(request.Email);
            var hasPasswordUpdate = !string.IsNullOrWhiteSpace(request.NewPassword);

            if (!hasEmailUpdate && !hasPasswordUpdate)
            {
                return (false, null, "At least one field (email or password) must be provided for update.");
            }

            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null || !user.IsActive || user.DeletedAt.HasValue)
            {
                _logger.LogWarning("Update failed for user {UserId}: not found or inactive", userId);
                return (false, null, "User not found or inactive.");
            }

            // Handle password update
            if (hasPasswordUpdate)
            {
                var newPassword = request.NewPassword!;

                if (string.IsNullOrWhiteSpace(request.CurrentPassword))
                {
                    return (false, null, "Current password is required to change password.");
                }

                if (string.IsNullOrWhiteSpace(user.PasswordHash))
                {
                    return (false, null, "Cannot update password for account without existing password.");
                }

                // Verify current password
                var passwordValid = await _passwordService.VerifyPasswordAsync(request.CurrentPassword, user.PasswordHash);
                if (!passwordValid)
                {
                    _logger.LogWarning("Password update failed for user {UserId}: incorrect current password", userId);
                    return (false, null, "Current password is incorrect.");
                }

                // Validate new password
                if (newPassword.Length < 8)
                {
                    return (false, null, "New password must be at least 8 characters long.");
                }

                // Hash and update password
                var newPasswordHash = await _passwordService.HashPasswordAsync(newPassword);
                user.PasswordHash = newPasswordHash;

                _logger.LogInformation("Password updated for user {UserId}", userId);
            }

            // Handle email update
            if (hasEmailUpdate)
            {
                var normalizedEmail = request.Email!.Trim().ToLowerInvariant();

                // Check if email is already in use by another user
                var existingUser = await _userRepository.GetByEmailAsync(normalizedEmail);
                if (existingUser != null && existingUser.Id != userId)
                {
                    return (false, null, "Email is already in use by another account.");
                }

                user.Email = normalizedEmail;
                user.EmailVerified = false; // Reset verification status when email changes

                _logger.LogInformation("Email updated for user {UserId}", userId);
            }

            // Save changes
            await _userRepository.UpdateUserAsync(user);

            // A password or email change ends every session (closed-alpha WP4); this tab gets a
            // fresh one so it stays logged in, keeping its "remember me" choice.
            var current = string.IsNullOrWhiteSpace(currentRefreshToken)
                ? null
                : await _refreshTokens.GetByHashAsync(_tokenService.HashRefreshToken(currentRefreshToken.Trim()));
            var rememberMe = current != null && current.UserId == user.Id && current.RememberMe;
            await RevokeAllSessionsAsync(user.Id, hasPasswordUpdate ? "password changed" : "email changed");
            var (accessToken, expiresIn, session) = await IssueSessionAsync(user, rememberMe, null, clientIp, userAgent);

            return (true, new AuthUpdateResponseDto
            {
                User = _mapper.Map<UserDto>(user),
                Message = "Account updated successfully.",
                AccessToken = accessToken,
                ExpiresIn = expiresIn,
                Session = session
            }, null);
        }

        /// <inheritdoc/>
        public async Task<AuthForgotPasswordResponseDto> RequestPasswordResetAsync(string email, string? clientIp, string? userAgent, bool allowDebugPayload)
        {
            const string genericMessage = "If an account with that email exists, we have sent password reset instructions.";

            if (string.IsNullOrWhiteSpace(email))
            {
                return new AuthForgotPasswordResponseDto { Message = genericMessage };
            }

            var normalizedEmail = email.Trim().ToLowerInvariant();
            if (IsForgotPasswordThrottled(normalizedEmail, clientIp))
            {
                _logger.LogWarning("Password reset request throttled for {Email} from {Ip}", normalizedEmail, clientIp ?? "unknown");
                return new AuthForgotPasswordResponseDto { Message = genericMessage };
            }

            var user = await _userRepository.GetByEmailAsync(normalizedEmail);
            if (user == null || string.IsNullOrWhiteSpace(user.PasswordHash) || !user.IsActive || user.DeletedAt.HasValue)
            {
                _logger.LogInformation("Password reset requested for unknown/ineligible email {Email} from {Ip}", normalizedEmail, clientIp ?? "unknown");
                return new AuthForgotPasswordResponseDto { Message = genericMessage };
            }

            await _linkCodeRepository.InvalidateActivePasswordResetTokensAsync(user.Id);

            var rawToken = GenerateRawResetToken();
            var tokenHash = HashToken(rawToken);
            var expiresAt = DateTime.UtcNow.AddMinutes(_securitySettings.PasswordResetTokenExpirationMinutes);

            await _linkCodeRepository.CreateAsync(new LinkCode
            {
                UserId = user.Id,
                Code = tokenHash,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt,
                Status = LinkCodeStatus.Active
            });

            var resetUrl = BuildResetUrl(rawToken);
            try
            {
                await _passwordResetDeliveryService.SendPasswordResetAsync(user.Email!, user.Username, resetUrl);
            }
            catch (Exception ex)
            {
                // Let this bubble up as a 500 so the frontend reports a real failure instead of a
                // false "reset instructions sent" message - the token is already persisted above,
                // but the user was never notified, so masking the failure would be misleading.
                _logger.LogError(ex, "Failed to deliver password reset email for user {UserId} ({Email})", user.Id, normalizedEmail);
                throw;
            }

            _logger.LogInformation(
                "Password reset token issued for user {UserId} from {Ip} ({UserAgent})",
                user.Id,
                clientIp ?? "unknown",
                string.IsNullOrWhiteSpace(userAgent) ? "unknown" : userAgent);

            var includeDebug = allowDebugPayload && _securitySettings.PasswordResetExposeTokenInDevelopment;
            return new AuthForgotPasswordResponseDto
            {
                Message = genericMessage,
                DebugResetToken = includeDebug ? rawToken : null,
                DebugResetUrl = includeDebug ? resetUrl : null
            };
        }

        /// <inheritdoc/>
        public async Task<(bool Ok, string? Error)> ResetPasswordAsync(AuthResetPasswordRequestDto request)
        {
            if (request == null)
            {
                return (false, "Reset payload is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Token))
            {
                return (false, "Reset token is required.");
            }

            if (string.IsNullOrWhiteSpace(request.NewPassword))
            {
                return (false, "New password is required.");
            }

            if (request.NewPassword != request.PasswordConfirmation)
            {
                return (false, "Password and confirmation do not match.");
            }

            var (passwordValid, passwordError) = await _passwordService.ValidatePasswordAsync(request.NewPassword);
            if (!passwordValid)
            {
                return (false, passwordError ?? "Password does not meet policy requirements.");
            }

            var tokenHash = HashToken(request.Token.Trim());
            var resetToken = await _linkCodeRepository.GetActivePasswordResetTokenAsync(tokenHash);
            if (resetToken == null || resetToken.User == null)
            {
                return (false, "Reset token is invalid or expired.");
            }

            if (!resetToken.User.IsActive || resetToken.User.DeletedAt.HasValue)
            {
                return (false, "Account is inactive.");
            }

            var newPasswordHash = await _passwordService.HashPasswordAsync(request.NewPassword);
            await _userRepository.UpdatePasswordHashAsync(resetToken.User.Id, newPasswordHash);

            await _linkCodeRepository.UpdateLinkCodeStatusAsync(resetToken.Id, LinkCodeStatus.Used);
            await _linkCodeRepository.InvalidateActivePasswordResetTokensAsync(resetToken.User.Id, resetToken.Id);
            await RevokeAllSessionsAsync(resetToken.User.Id, "password reset");

            _logger.LogInformation("Password reset completed for user {UserId}", resetToken.User.Id);
            return (true, null);
        }

        private async Task<int> CalculateExpiresInSecondsAsync(string accessToken)
        {
            var expiresAt = await _tokenService.ExtractExpirationAsync(accessToken);
            if (!expiresAt.HasValue)
            {
                return 0;
            }

            var remaining = expiresAt.Value - DateTime.UtcNow;
            return remaining <= TimeSpan.Zero ? 0 : (int)Math.Round(remaining.TotalSeconds);
        }

        /// <summary>An identifier with "@" is an email, anything else a username (case-insensitive).</summary>
        private Task<User?> FindByLoginAsync(string login)
        {
            var trimmed = login.Trim();
            return trimmed.Contains('@')
                ? _userRepository.GetByEmailAsync(trimmed.ToLowerInvariant())
                : _userRepository.GetByUsernameIgnoreCaseAsync(trimmed);
        }

        /// <summary>A new login session: a refresh token (new family unless given) and an access token
        /// carrying the family as "sid".</summary>
        private async Task<(string AccessToken, int ExpiresIn, IssuedRefreshToken Session)> IssueSessionAsync(
            User user, bool rememberMe, string? familyId, string? clientIp, string? userAgent)
        {
            var family = familyId ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var token = NewRefreshToken(user.Id, family, rememberMe, clientIp, userAgent, out var raw);
            await _refreshTokens.AddAsync(token);

            var accessToken = await _tokenService.GenerateAccessTokenAsync(user, family);
            var expiresIn = await CalculateExpiresInSecondsAsync(accessToken);
            return (accessToken, expiresIn, new IssuedRefreshToken(raw, token.ExpiresAt, rememberMe));
        }

        private RefreshToken NewRefreshToken(int userId, string familyId, bool rememberMe, string? clientIp, string? userAgent, out string raw)
        {
            raw = _tokenService.GenerateRefreshToken();
            var now = DateTime.UtcNow;
            var jwt = _securitySettings.Jwt;
            return new RefreshToken
            {
                UserId = userId,
                TokenHash = _tokenService.HashRefreshToken(raw),
                FamilyId = familyId,
                CreatedAt = now,
                ExpiresAt = rememberMe
                    ? now.AddDays(Math.Max(1, jwt.RefreshTokenDays))
                    : now.AddHours(Math.Max(1, jwt.SessionRefreshHours)),
                RememberMe = rememberMe,
                CreatedByIp = Truncate(clientIp, 64),
                UserAgent = Truncate(userAgent, 256)
            };
        }

        private static string? Truncate(string? value, int max) =>
            string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];

        private bool IsForgotPasswordThrottled(string normalizedEmail, string? clientIp)
        {
            var cooldown = TimeSpan.FromSeconds(Math.Max(5, _securitySettings.PasswordResetRequestCooldownSeconds));
            var key = $"pwdreset:{normalizedEmail}:{clientIp ?? "unknown"}";
            if (_memoryCache.TryGetValue(key, out _))
            {
                return true;
            }

            _memoryCache.Set(key, true, cooldown);
            return false;
        }

        private string BuildResetUrl(string rawToken)
        {
            var baseUrl = string.IsNullOrWhiteSpace(_securitySettings.PasswordResetFrontendBaseUrl)
                ? "http://localhost:3000"
                : _securitySettings.PasswordResetFrontendBaseUrl.TrimEnd('/');

            return $"{baseUrl}/auth/reset-password?token={Uri.EscapeDataString(rawToken)}";
        }

        private static string GenerateRawResetToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static string HashToken(string rawToken)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
