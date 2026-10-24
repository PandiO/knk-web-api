using knkwebapi_v2.Dtos;
using Microsoft.AspNetCore.Http;

namespace knkwebapi_v2.Configuration
{
    /// <summary>
    /// The HttpOnly refresh-token cookie (closed-alpha WP4): Secure outside Development, SameSite
    /// from Security:RefreshCookie:SameSite, Path /api/Auth. Without "remember me" it is a session
    /// cookie (no Expires); the server still ends it after Security:Jwt:SessionRefreshHours.
    /// Shared by AuthController and the one Users endpoint that logs the caller into another
    /// account (link-minecraft-account case B, KNG-207).
    /// </summary>
    public static class RefreshTokenCookie
    {
        public const string Name = "refreshToken";
        public const string Path = "/api/Auth";

        public static void Set(HttpResponse response, IssuedRefreshToken? session, bool isDevelopment, SecuritySettings security)
        {
            if (session == null)
            {
                return;
            }

            var options = BuildOptions(isDevelopment, security);
            if (session.RememberMe)
            {
                options.Expires = session.ExpiresAt;
            }

            DeleteLegacyRootCookie(response, isDevelopment, security);
            response.Cookies.Append(Name, session.Token, options);
        }

        public static void Clear(HttpResponse response, bool isDevelopment, SecuritySettings security)
        {
            response.Cookies.Delete(Name, BuildOptions(isDevelopment, security));
            DeleteLegacyRootCookie(response, isDevelopment, security);
        }

        /// <summary>Before closed-alpha WP4 the cookie lived at Path=/ and held a refresh JWT.</summary>
        private static void DeleteLegacyRootCookie(HttpResponse response, bool isDevelopment, SecuritySettings security)
        {
            var legacy = BuildOptions(isDevelopment, security);
            legacy.Path = "/";
            response.Cookies.Delete(Name, legacy);
        }

        public static CookieOptions BuildOptions(bool isDevelopment, SecuritySettings security)
        {
            return new CookieOptions
            {
                HttpOnly = true,
                Secure = !isDevelopment,
                SameSite = ParseSameSite(security.RefreshCookie.SameSite),
                Path = Path,
                IsEssential = true
            };
        }

        private static SameSiteMode ParseSameSite(string? value) =>
            Enum.TryParse<SameSiteMode>(value, ignoreCase: true, out var mode) && mode != SameSiteMode.Unspecified
                ? mode
                : SameSiteMode.Lax;
    }
}
