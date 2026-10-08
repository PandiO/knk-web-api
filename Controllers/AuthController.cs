using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        public const string RefreshTokenCookieName = "refreshToken";

        /// <summary>The refresh cookie is only sent to the auth endpoints (closed-alpha WP4).</summary>
        public const string RefreshCookiePath = "/api/Auth";

        private readonly IAuthService _authService;
        private readonly ITokenService _tokenService;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<AuthController> _logger;
        private readonly IUserSessionStateCache _sessionState;
        private readonly SecuritySettings _security;

        public AuthController(
            IAuthService authService,
            ITokenService tokenService,
            IWebHostEnvironment environment,
            ILogger<AuthController> logger,
            IUserSessionStateCache sessionState,
            IOptions<SecuritySettings> security)
        {
            _authService = authService;
            _tokenService = tokenService;
            _environment = environment;
            _logger = logger;
            _sessionState = sessionState;
            _security = security.Value;
        }

        private string? ClientIp => HttpContext?.Connection.RemoteIpAddress?.ToString();
        private string? UserAgent => HttpContext?.Request.Headers.UserAgent.ToString();

        [HttpPost("login")]
        [EnableRateLimiting(knkwebapi_v2.Configuration.RateLimitingSetup.AuthPolicy)]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] AuthLoginRequestDto request)
        {
            if (request == null)
            {
                return BadRequest(new { error = "InvalidRequest", message = "Login payload is required." });
            }

            var login = string.IsNullOrWhiteSpace(request.Login) ? request.Email : request.Login;
            var (ok, result, error, lockedFor) = await _authService.LoginAsync(login ?? string.Empty, request.Password, request.RememberMe, ClientIp, UserAgent);
            if (lockedFor.HasValue)
            {
                // Closed-alpha WP6.2: the message is shown to players verbatim.
                Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(lockedFor.Value.TotalSeconds)).ToString();
                return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "TooManyAttempts", message = error });
            }
            if (!ok || result == null)
            {
                return Unauthorized(new { error = "InvalidCredentials", message = error ?? AuthService.InvalidCredentials });
            }

            SetRefreshTokenCookie(result.Session);
            result.RefreshToken = null;
            _logger.LogInformation("Login succeeded for user {UserId}", result.User?.Id);

            return Ok(result);
        }

        /// <summary>
        /// Web registration for a Minecraft account (closed-alpha WP5, D1): the player runs
        /// /account link in game, enters the code here with an email and password, and is logged
        /// in (no "remember me"). 400 for a bad code, email or password, 409 AlreadyRegistered or
        /// DuplicateEmail, 403 RegistrationNeedsCode without a code.
        /// </summary>
        [HttpPost("register")]
        [EnableRateLimiting(knkwebapi_v2.Configuration.RateLimitingSetup.AuthPolicy)]
        [AllowAnonymous]
        public async Task<IActionResult> Register([FromBody] AuthRegisterRequestDto request)
        {
            var outcome = await _authService.RegisterAsync(request, ClientIp, UserAgent);
            if (!outcome.Ok || outcome.Result == null)
            {
                _logger.LogInformation("Registration refused: {Error}", outcome.Error);
                var body = new { error = outcome.Error, message = outcome.Message };
                return outcome.Error switch
                {
                    "AlreadyRegistered" or "DuplicateEmail" => Conflict(body),
                    AuthRegisterOutcome.RegistrationNeedsCode => StatusCode(StatusCodes.Status403Forbidden, body),
                    _ => BadRequest(body)
                };
            }

            SetRefreshTokenCookie(outcome.Result.Session);
            outcome.Result.RefreshToken = null;
            return Ok(outcome.Result);
        }

        [HttpPost("refresh")]
        [EnableRateLimiting(knkwebapi_v2.Configuration.RateLimitingSetup.AuthPolicy)]
        [AllowAnonymous]
        public async Task<IActionResult> Refresh([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AuthRefreshRequestDto? request)
        {
            // The cookie carries the token; a token in the body only counts in Development
            // (closed-alpha WP4), so a token leaked into page script can't be replayed here.
            var refreshToken = ReadRefreshTokenCookie();
            if (string.IsNullOrWhiteSpace(refreshToken) && _environment.IsDevelopment())
            {
                refreshToken = request?.RefreshToken;
            }

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return Unauthorized(new { error = "RefreshTokenRequired", message = "Refresh token is required." });
            }

            var (ok, result, error) = await _authService.RefreshAsync(refreshToken, ClientIp, UserAgent);
            if (!ok || result == null)
            {
                _logger.LogWarning("Refresh failed: {Reason}", error ?? "Invalid or expired refresh token");
                ClearRefreshTokenCookie();
                return Unauthorized(new { error = "InvalidRefreshToken", message = error ?? "Invalid or expired refresh token." });
            }

            SetRefreshTokenCookie(result.Session);
            result.RefreshToken = null;
            _logger.LogInformation("Refresh succeeded for token");

            return Ok(result);
        }

        [HttpPost("logout")]
        [AllowAnonymous]
        public async Task<IActionResult> Logout([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AuthRefreshRequestDto? request)
        {
            // Revoking needs no secrecy, so a body token is accepted here (it can only end a session).
            var refreshToken = ReadRefreshTokenCookie();
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                refreshToken = request?.RefreshToken;
            }

            await _authService.LogoutAsync(refreshToken);
            ClearRefreshTokenCookie();
            _logger.LogInformation("Logout completed (token provided: {HasToken})", !string.IsNullOrWhiteSpace(refreshToken));

            return NoContent();
        }

        /// <summary>"Sign out everywhere": ends every session of the logged-in user (closed-alpha WP4).</summary>
        [HttpPost("logout-all")]
        [Authorize]
        public async Task<IActionResult> LogoutAll()
        {
            var userId = GetUserIdFromClaims(User);
            if (!userId.HasValue)
            {
                return Unauthorized(new { error = "InvalidToken", message = "User claim missing." });
            }

            await _authService.RevokeAllSessionsAsync(userId.Value, "sign out everywhere");
            ClearRefreshTokenCookie();
            return NoContent();
        }

        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> Me()
        {
            var userId = GetUserIdFromClaims(User);
            if (!userId.HasValue)
            {
                return Unauthorized(new { error = "InvalidToken", message = "User claim missing." });
            }

            var user = await _authService.GetCurrentUserAsync(userId.Value);
            if (user == null)
            {
                return Unauthorized(new { error = "UserNotFound", message = "User not found or inactive." });
            }

            return Ok(user);
        }

        [HttpPut("update")]
        [Authorize]
        public async Task<IActionResult> Update([FromBody] AuthUpdateRequestDto request)
        {
            var userId = GetUserIdFromClaims(User);
            if (!userId.HasValue)
            {
                return Unauthorized(new { error = "InvalidToken", message = "User claim missing." });
            }

            if (request == null)
            {
                return BadRequest(new { error = "InvalidRequest", message = "Update payload is required." });
            }

            var (ok, result, error) = await _authService.UpdateUserAsync(userId.Value, request, ReadRefreshTokenCookie(), ClientIp, UserAgent);
            if (!ok || result == null)
            {
                _logger.LogWarning("Update failed for user {UserId}: {Reason}", userId, error ?? "Unknown error");
                return BadRequest(new { error = "UpdateFailed", message = error ?? "Failed to update user account." });
            }

            _logger.LogInformation("User account updated for user {UserId}", userId);
            SetRefreshTokenCookie(result.Session);
            return Ok(result);
        }

        [HttpPost("validate-token")]
        [EnableRateLimiting(knkwebapi_v2.Configuration.RateLimitingSetup.AuthPolicy)]
        [AllowAnonymous]
        public async Task<IActionResult> ValidateToken([FromBody] AuthValidateTokenRequestDto request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Token))
            {
                return BadRequest(new { error = "InvalidRequest", message = "Token is required." });
            }

            var principal = await _tokenService.ValidateAccessTokenAsync(request.Token);
            if (principal != null && await AccessTokenSessionCheck.FindProblemAsync(principal, _sessionState, HttpContext.RequestAborted) != null)
            {
                principal = null; // a revoked session or a refresh JWT is not a valid access token
            }
            var expiresAt = await _tokenService.ExtractExpirationAsync(request.Token);

            return Ok(new AuthValidateTokenResponseDto
            {
                Valid = principal != null,
                ExpiresAt = principal != null ? expiresAt : null
            });
        }

        [HttpPost("forgot-password")]
        [EnableRateLimiting(knkwebapi_v2.Configuration.RateLimitingSetup.AuthPolicy)]
        [AllowAnonymous]
        public async Task<IActionResult> ForgotPassword([FromBody] AuthForgotPasswordRequestDto request)
        {
            // Intentionally generic to avoid account enumeration.
            var email = request?.Email ?? string.Empty;
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = HttpContext.Request.Headers.UserAgent.ToString();

            // Closed-alpha WP6.7: the debug token only for a developer on this machine.
            var remote = HttpContext.Connection.RemoteIpAddress;
            var allowDebug = _environment.IsDevelopment() && remote != null && IPAddress.IsLoopback(remote);
            var response = await _authService.RequestPasswordResetAsync(
                email,
                clientIp,
                userAgent,
                allowDebug);

            return Ok(response);
        }

        [HttpPost("reset-password")]
        [EnableRateLimiting(knkwebapi_v2.Configuration.RateLimitingSetup.AuthPolicy)]
        [AllowAnonymous]
        public async Task<IActionResult> ResetPassword([FromBody] AuthResetPasswordRequestDto request)
        {
            var (ok, error) = await _authService.ResetPasswordAsync(request);
            if (!ok)
            {
                return BadRequest(new { error = "InvalidResetToken", message = error ?? "Reset token is invalid or expired." });
            }

            return Ok(new AuthResetPasswordResponseDto
            {
                Message = "Password has been reset successfully."
            });
        }

        private string? ReadRefreshTokenCookie()
        {
            return Request.Cookies.TryGetValue(RefreshTokenCookieName, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;
        }

        /// <summary>
        /// HttpOnly, Secure outside Development, SameSite from Security:RefreshCookie:SameSite,
        /// Path /api/Auth. Without "remember me" it is a session cookie (no Expires); the server
        /// still ends it after Security:Jwt:SessionRefreshHours.
        /// </summary>
        private void SetRefreshTokenCookie(IssuedRefreshToken? session)
        {
            if (session == null)
            {
                return;
            }

            var options = BuildRefreshCookieOptions();
            if (session.RememberMe)
            {
                options.Expires = session.ExpiresAt;
            }

            DeleteLegacyRootCookie();
            Response.Cookies.Append(RefreshTokenCookieName, session.Token, options);
        }

        private void ClearRefreshTokenCookie()
        {
            Response.Cookies.Delete(RefreshTokenCookieName, BuildRefreshCookieOptions());
            DeleteLegacyRootCookie();
        }

        /// <summary>Before closed-alpha WP4 the cookie lived at Path=/ and held a refresh JWT.</summary>
        private void DeleteLegacyRootCookie()
        {
            var legacy = BuildRefreshCookieOptions();
            legacy.Path = "/";
            Response.Cookies.Delete(RefreshTokenCookieName, legacy);
        }

        private CookieOptions BuildRefreshCookieOptions()
        {
            return new CookieOptions
            {
                HttpOnly = true,
                Secure = !_environment.IsDevelopment(),
                SameSite = ParseSameSite(_security.RefreshCookie.SameSite),
                Path = RefreshCookiePath,
                IsEssential = true
            };
        }

        private static SameSiteMode ParseSameSite(string? value) =>
            Enum.TryParse<SameSiteMode>(value, ignoreCase: true, out var mode) && mode != SameSiteMode.Unspecified
                ? mode
                : SameSiteMode.Lax;

        private int? GetUserIdFromClaims(ClaimsPrincipal principal)
        {
            var userIdClaim = principal.FindFirst("uid")
                ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)
                ?? principal.FindFirst(ClaimTypes.NameIdentifier);

            if (userIdClaim == null)
            {
                return null;
            }

            return int.TryParse(userIdClaim.Value, out var userId) ? userId : (int?)null;
        }
    }
}
