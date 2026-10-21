using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AutoMapper;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services.Email;
using knkwebapi_v2.Services.Invitations;
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
        private readonly IEmailComposer _email;
        private readonly string _publicBaseUrl;
        private readonly ILoginAttemptLimiter _loginLimiter;
        private readonly IMemoryCache _memoryCache;
        private readonly SecuritySettings _securitySettings;
        private readonly ILogger<AuthService> _logger;
        private readonly IRefreshTokenRepository _refreshTokens;
        private readonly ISessionRevocationService _sessionRevocation;
        private readonly ILinkCodeService _linkCodeService;
        private readonly IInvitationService? _invitations;
        private readonly IUserService? _users;

        public AuthService(
            IUserRepository userRepository,
            ITokenService tokenService,
            IPasswordService passwordService,
            IMapper mapper,
            ILinkCodeRepository linkCodeRepository,
            IEmailComposer email,
            IMemoryCache memoryCache,
            IOptions<SecuritySettings> securitySettings,
            ILogger<AuthService> logger,
            IRefreshTokenRepository refreshTokens,
            ISessionRevocationService sessionRevocation,
            ILinkCodeService linkCodeService,
            ILoginAttemptLimiter loginLimiter,
            IOptions<AppSettings>? app = null,
            IInvitationService? invitations = null,
            IUserService? users = null)
        {
            // KNG-214: register with a claim code links through IUserService (case A / B).
            _users = users;
            _invitations = invitations;
            _loginLimiter = loginLimiter;
            _linkCodeService = linkCodeService;
            _refreshTokens = refreshTokens;
            _sessionRevocation = sessionRevocation;
            _userRepository = userRepository;
            _tokenService = tokenService;
            _passwordService = passwordService;
            _mapper = mapper;
            _linkCodeRepository = linkCodeRepository;
            _email = email;
            _memoryCache = memoryCache;
            _securitySettings = securitySettings.Value;
            _logger = logger;
            // KNG-206: App:PublicBaseUrl, with the reset-specific setting as the fallback.
            _publicBaseUrl = AppSettings.ResolveBaseUrl(app?.Value.PublicBaseUrl, _securitySettings.PasswordResetFrontendBaseUrl);
        }

        /// <summary>Same text for every failed login (closed-alpha WP6.1): unknown name, wrong password,
        /// no web login, inactive or deleted all look alike.</summary>
        public const string InvalidCredentials = "Invalid credentials.";

        /// <summary>A bcrypt hash of a random value, verified against for unknown identifiers so they
        /// take as long as a wrong password (closed-alpha WP6.1).</summary>
        private static readonly Lazy<string> TimingDummyHash = new(() =>
            BCrypt.Net.BCrypt.HashPassword(Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)), 10));

        /// <inheritdoc/>
        public async Task<(bool Ok, AuthLoginResponseDto? Result, string? Error, TimeSpan? LockedFor)> LoginAsync(string login, string password, bool rememberMe, string? clientIp = null, string? userAgent = null)
        {
            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            {
                return (false, null, "Email or Minecraft name and password are required.", null);
            }

            // Closed-alpha WP5 (D3): log in with the email or the Minecraft name, case-insensitive.
            var user = await FindByLoginAsync(login);
            var lockKey = user != null ? $"user:{user.Id}" : $"login:{login.Trim().ToLowerInvariant()}";

            // WP6.2: while locked, the password isn't even checked.
            if (_loginLimiter.IsLocked(lockKey, out var remaining))
            {
                _logger.LogWarning("Login refused for {LockKey}: locked for {Minutes:0.#} more minutes", lockKey, remaining.TotalMinutes);
                return (false, null, LoginAttemptLimiter.LockedMessage(remaining), remaining);
            }

            // WP6.1: verify the password first, so inactive accounts and unknown names look exactly
            // like a wrong password (and take as long).
            bool passwordValid;
            if (user == null || string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                await _passwordService.VerifyPasswordAsync(password, TimingDummyHash.Value);
                passwordValid = false;
            }
            else
            {
                passwordValid = await _passwordService.VerifyPasswordAsync(password, user.PasswordHash);
            }

            if (!passwordValid || user == null || !user.IsActive || user.DeletedAt.HasValue)
            {
                var locked = _loginLimiter.RecordFailure(lockKey);
                _logger.LogWarning("Login failed for {LockKey} ({Reason}){Locked}", lockKey,
                    user == null ? "unknown" : !passwordValid ? "wrong password or no web login" : "inactive or deleted",
                    locked ? ": account locked" : string.Empty);
                return (false, null, InvalidCredentials, null);
            }

            _loginLimiter.Reset(lockKey);
            var (accessToken, expiresIn, session) = await IssueSessionAsync(user, rememberMe, null, clientIp, userAgent);

            var response = new AuthLoginResponseDto
            {
                AccessToken = accessToken,
                RefreshToken = null,
                ExpiresIn = expiresIn,
                User = _mapper.Map<UserDto>(user),
                Session = session
            };

            _logger.LogInformation("Login succeeded for user {UserId}", user.Id);
            return (true, response, null, null);
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

            var hasInvite = !string.IsNullOrWhiteSpace(request.InviteToken) || !string.IsNullOrWhiteSpace(request.InviteCode);
            var inviteMode = _securitySettings.Registration.Mode != RegistrationMode.LinkCode;

            // KNG-214 (game-first, DESIGN § 5.3): a claim code from the disconnect screen comes with an
            // invitation; the account is created and linked in one step.
            if (!string.IsNullOrWhiteSpace(request.ClaimCode))
            {
                if (!string.IsNullOrWhiteSpace(request.LinkCode))
                {
                    return AuthRegisterOutcome.Fail("InvalidRequest", "Use either a claim code or a link code, not both.");
                }
                if (!inviteMode)
                {
                    return AuthRegisterOutcome.Fail(AuthRegisterOutcome.InvitesDisabled, "Sign-up with an invitation isn't open yet. Join the server and run /account link to get your registration code.");
                }
                if (!hasInvite)
                {
                    return AuthRegisterOutcome.Fail(AuthRegisterOutcome.InviteRequired, "You need an invitation to create an account.");
                }
            }
            if (hasInvite)
            {
                if (!inviteMode)
                {
                    // Mode=LinkCode (today's alpha): invites don't exist for sign-up yet.
                    return AuthRegisterOutcome.Fail(AuthRegisterOutcome.InvitesDisabled, "Sign-up with an invitation isn't open yet. Join the server and run /account link to get your registration code.");
                }
                if (!string.IsNullOrWhiteSpace(request.LinkCode))
                {
                    return AuthRegisterOutcome.Fail("InvalidRequest", "Use either an invitation or a link code, not both.");
                }
                return await RegisterWithInviteAsync(request, clientIp, userAgent);
            }

            // D1/D2: a code from the game server (/account link) is the only way in during the alpha.
            if (string.IsNullOrWhiteSpace(request.LinkCode))
            {
                if (inviteMode)
                {
                    return AuthRegisterOutcome.Fail(AuthRegisterOutcome.InviteRequired, "You need an invitation to create an account.");
                }
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

            // Mode=Invite: a link code alone only works for an account that is already admitted
            // (grandfathered, or admitted by staff), so it can't be used to sign up without an invite.
            if (inviteMode && !user.AdmittedAt.HasValue)
            {
                return AuthRegisterOutcome.Fail(AuthRegisterOutcome.InviteRequired, "You need an invitation to create an account.");
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

        /// <summary>
        /// Mode=Invite with an invite (KNG-212, plan § 5.1): check the invite, the email (a personal
        /// invite's address must match and is then verified), the password and duplicates, then take the
        /// use and create the web shell account in one transaction (InvitationService.RedeemAsync) and
        /// log it in.
        /// </summary>
        private async Task<AuthRegisterOutcome> RegisterWithInviteAsync(AuthRegisterRequestDto request, string? clientIp, string? userAgent)
        {
            const string invalidInvite = "This invitation is invalid or has expired. Ask whoever invited you for a new one.";
            if (_invitations == null)
            {
                return AuthRegisterOutcome.Fail(AuthRegisterOutcome.InvitesDisabled, "Sign-up with an invitation isn't available.");
            }

            // A token is a personal invite, a code a shared one; the other field is ignored.
            var invitation = !string.IsNullOrWhiteSpace(request.InviteToken)
                ? await _invitations.FindUsableAsync(request.InviteToken, null)
                : await _invitations.FindUsableAsync(null, request.InviteCode);
            if (invitation == null)
            {
                return AuthRegisterOutcome.Fail(AuthRegisterOutcome.InvalidInvite, invalidInvite);
            }

            var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
            var personal = invitation.Kind == Models.InvitationKind.Personal;
            if (personal && !string.Equals(email, invitation.Email, StringComparison.OrdinalIgnoreCase))
            {
                return AuthRegisterOutcome.Fail(AuthRegisterOutcome.InviteEmailMismatch, "This invitation was sent to a different email address. Use the address it was sent to.");
            }
            if (!IsValidEmail(email))
            {
                return AuthRegisterOutcome.Fail("InvalidEmail", "Enter a valid email address.");
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
            if (await _userRepository.IsEmailTakenAsync(email))
            {
                return AuthRegisterOutcome.Fail("DuplicateEmail", "This email is already in use by another account.");
            }

            var passwordHash = await _passwordService.HashPasswordAsync(request.Password!);
            var signup = new InviteSignup(invitation, email, passwordHash, EmailVerified: personal);
            if (!string.IsNullOrWhiteSpace(request.ClaimCode))
            {
                return await RegisterWithInviteAndClaimAsync(signup, request.ClaimCode!, clientIp, userAgent);
            }

            var user = await _invitations.RedeemAsync(signup);
            if (user == null)
            {
                // Another sign-up took the last use, or the invite was paused or revoked meanwhile.
                return AuthRegisterOutcome.Fail(AuthRegisterOutcome.InvalidInvite, invalidInvite);
            }

            var (accessToken, expiresIn, session) = await IssueSessionAsync(user, false, null, clientIp, userAgent);
            return AuthRegisterOutcome.Success(new AuthLoginResponseDto
            {
                AccessToken = accessToken,
                RefreshToken = null,
                ExpiresIn = expiresIn,
                User = _mapper.Map<UserDto>(user),
                Session = session
            });
        }

        private const string InvalidClaimMessage = "This code from the server is invalid or has expired. Join the server again for a new one.";

        /// <summary>
        /// Game-first sign-up (KNG-214, plan § 6.1): the claim code is checked before the invite's use
        /// is taken, then the shell account is created (InvitationService.RedeemAsync) and linked
        /// (UserService.LinkMinecraftAccountAsync: case A, or the fold of case B) in one transaction.
        /// Any failure rolls all of it back, so there is never a new account without its Minecraft
        /// identity, nor a used invite without an account.
        /// </summary>
        private async Task<AuthRegisterOutcome> RegisterWithInviteAndClaimAsync(InviteSignup signup, string claimCode, string? clientIp, string? userAgent)
        {
            if (_users == null)
            {
                return AuthRegisterOutcome.Fail(AuthRegisterOutcome.InvalidClaimCode, InvalidClaimMessage);
            }

            var (valid, code, _) = await _linkCodeService.ValidateLinkCodeAsync(claimCode);
            if (!valid || code == null || code.Purpose != LinkCodePurpose.Claim || string.IsNullOrWhiteSpace(code.MinecraftUuid))
            {
                return AuthRegisterOutcome.Fail(AuthRegisterOutcome.InvalidClaimCode, InvalidClaimMessage);
            }
            var holder = await _userRepository.GetByUuidAsync(code.MinecraftUuid);
            if (holder != null && (!holder.IsActive || holder.DeletedAt.HasValue))
            {
                return AuthRegisterOutcome.Fail(AuthRegisterOutcome.InvalidClaimCode, InvalidClaimMessage);
            }
            if (holder != null && !string.IsNullOrEmpty(holder.PasswordHash))
            {
                return AuthRegisterOutcome.Fail("AlreadyRegistered", "This Minecraft account already has a web account. Log in with it, or use Forgot password.");
            }

            LinkMinecraftAccountResult? link = null;
            var outcome = await _userRepository.RunInTransactionAsync(async () =>
            {
                var shell = await _invitations!.RedeemAsync(signup);
                if (shell == null)
                {
                    return AuthRegisterOutcome.Fail(AuthRegisterOutcome.InvalidInvite, "This invitation is invalid or has expired. Ask whoever invited you for a new one.");
                }
                try
                {
                    link = await _users.LinkMinecraftAccountAsync(shell.Id, claimCode);
                }
                catch (LinkConflictException ex)
                {
                    return AuthRegisterOutcome.Fail(ex.Code, ex.Message);
                }
                catch (InvalidOperationException)
                {
                    // Spent or expired between the check above and the link.
                    return AuthRegisterOutcome.Fail(AuthRegisterOutcome.InvalidClaimCode, InvalidClaimMessage);
                }
                return AuthRegisterOutcome.Pending;
            }, result => result.Ok);
            if (!outcome.Ok || link == null)
            {
                return outcome.Ok ? AuthRegisterOutcome.Fail(AuthRegisterOutcome.InvalidClaimCode, InvalidClaimMessage) : outcome;
            }

            // The account the login now belongs to: the shell itself (case A) or the Minecraft account it was folded into (case B).
            var user = await _userRepository.GetByIdAsync(link.User.Id);
            if (user == null)
            {
                return AuthRegisterOutcome.Fail(AuthRegisterOutcome.InvalidClaimCode, InvalidClaimMessage);
            }
            var (accessToken, expiresIn, session) = await IssueSessionAsync(user, false, null, clientIp, userAgent);
            _logger.LogInformation("Web account {UserId} created with an invitation and linked to Minecraft {Username} (merged: {Merged})", user.Id, user.Username, link.Merged);
            return AuthRegisterOutcome.Success(new AuthLoginResponseDto
            {
                AccessToken = accessToken,
                RefreshToken = null,
                ExpiresIn = expiresIn,
                User = _mapper.Map<UserDto>(user),
                Session = session,
                Linked = true,
                MinecraftName = user.Username
            });
        }

        /// <inheritdoc/>
        public async Task<AuthLoginResponseDto?> StartSessionAsync(int userId, string? clientIp = null, string? userAgent = null)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null || !user.IsActive || user.DeletedAt.HasValue)
            {
                return null;
            }

            var (accessToken, expiresIn, session) = await IssueSessionAsync(user, false, null, clientIp, userAgent);
            return new AuthLoginResponseDto
            {
                AccessToken = accessToken,
                RefreshToken = null,
                ExpiresIn = expiresIn,
                User = _mapper.Map<UserDto>(user),
                Session = session
            };
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

            // Closed-alpha WP6.5: both changes need the current password (D8: no email
            // verification during the alpha, so this is what stops a stolen session taking the account).
            if (string.IsNullOrWhiteSpace(request.CurrentPassword))
            {
                return (false, null, "Enter your current password to change your email or password.");
            }
            if (string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                return (false, null, "This account has no web password yet.");
            }
            if (!await _passwordService.VerifyPasswordAsync(request.CurrentPassword, user.PasswordHash))
            {
                _logger.LogWarning("Update failed for user {UserId}: incorrect current password", userId);
                return (false, null, "Current password is incorrect.");
            }

            string? newEmail = null;
            if (hasEmailUpdate)
            {
                newEmail = request.Email!.Trim().ToLowerInvariant();
                if (!IsValidEmail(newEmail))
                {
                    return (false, null, "Enter a valid email address.");
                }
                if (string.Equals(newEmail, user.Email, StringComparison.OrdinalIgnoreCase))
                {
                    newEmail = null; // unchanged
                }
                else if (await _userRepository.IsEmailTakenAsync(newEmail, userId))
                {
                    return (false, null, "Email is already in use by another account.");
                }
            }

            if (hasPasswordUpdate)
            {
                // WP6.6: the one shared password policy.
                var (valid, policyError) = await _passwordService.ValidatePasswordAsync(request.NewPassword!);
                if (!valid)
                {
                    return (false, null, policyError ?? "Password does not meet the requirements.");
                }
                user.PasswordHash = await _passwordService.HashPasswordAsync(request.NewPassword!);
                user.LastPasswordChangeAt = DateTime.UtcNow;
                _logger.LogInformation("Password updated for user {UserId}", userId);
            }

            var previousEmail = user.Email;
            if (newEmail != null)
            {
                user.Email = newEmail;
                user.EmailVerified = false;
                user.LastEmailChangeAt = DateTime.UtcNow;
                _logger.LogInformation("Email updated for user {UserId}", userId);
            }

            if (!hasPasswordUpdate && newEmail == null)
            {
                return (true, new AuthUpdateResponseDto { User = _mapper.Map<UserDto>(user), Message = "Nothing changed." }, null);
            }

            await _userRepository.UpdateUserAsync(user);

            // A password or email change ends every session (closed-alpha WP4); this tab gets a
            // fresh one so it stays logged in, keeping its "remember me" choice.
            var current = string.IsNullOrWhiteSpace(currentRefreshToken)
                ? null
                : await _refreshTokens.GetByHashAsync(_tokenService.HashRefreshToken(currentRefreshToken.Trim()));
            var rememberMe = current != null && current.UserId == user.Id && current.RememberMe;
            await RevokeAllSessionsAsync(user.Id, hasPasswordUpdate ? "password changed" : "email changed");
            var (accessToken, expiresIn, session) = await IssueSessionAsync(user, rememberMe, null, clientIp, userAgent);

            // WP6.5: tell the old address, best effort (outbox, KNG-206; logged when it fails).
            if (newEmail != null && !string.IsNullOrWhiteSpace(previousEmail))
            {
                await QueueEmailChangedNoticeAsync(user.Id, user.Username, previousEmail, newEmail);
            }

            return (true, new AuthUpdateResponseDto
            {
                User = _mapper.Map<UserDto>(user),
                Message = "Account updated successfully.",
                AccessToken = accessToken,
                ExpiresIn = expiresIn,
                Session = session
            }, null);
        }

        /// <summary>"st***@example.com": enough for the old owner to recognise, not a full address.</summary>
        public static string MaskEmail(string email) => EmailMask.Mask(email);

        /// <summary>
        /// Queues the "your email was changed" notice to <paramref name="previousEmail"/>. Used by
        /// Auth/update and by staff update-email (KNG-206). Never throws: the change itself is done.
        /// </summary>
        public static async Task QueueEmailChangedNoticeAsync(IEmailComposer email, ILogger logger, int userId, string? username,
            string previousEmail, string newEmail)
        {
            try
            {
                await email.QueueAsync(EmailKind.EmailChanged, previousEmail,
                    new { username = GreetingName(username), newEmailHint = MaskEmail(newEmail) },
                    $"email-changed:{userId}:{DateTime.UtcNow.Ticks}", userId: userId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not queue the email-changed notice for user {UserId}", userId);
            }
        }

        private Task QueueEmailChangedNoticeAsync(int userId, string? username, string previousEmail, string newEmail) =>
            QueueEmailChangedNoticeAsync(_email, _logger, userId, username, previousEmail, newEmail);

        private static string GreetingName(string? username) => string.IsNullOrWhiteSpace(username) ? "there" : username;

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

            var token = await _linkCodeRepository.CreateAsync(new LinkCode
            {
                UserId = user.Id,
                Code = tokenHash,
                Purpose = LinkCodePurpose.PasswordReset,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt,
                Status = LinkCodeStatus.Active
            });

            var resetUrl = BuildResetUrl(rawToken);
            // Closed-alpha WP6.7 (SEC-13): queued, so the answer is the same whether or not SMTP
            // works (a 500 only for existing accounts told an attacker which emails exist).
            // KNG-206: the outbox row expires with the token; EmailDispatcher logs delivery failures.
            try
            {
                await _email.QueueAsync(EmailKind.PasswordReset, user.Email!,
                    new { username = GreetingName(user.Username), resetUrl, expiresAt },
                    $"reset:{token.Id}", expiresAt, user.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not queue the password reset mail for user {UserId}", user.Id);
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
            return $"{_publicBaseUrl}/auth/reset-password?token={Uri.EscapeDataString(rawToken)}";
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
