using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using knkwebapi_v2.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Access tokens are JWTs signed with HS256 (Security:Jwt:{Issuer, Audience, Secret,
    /// AccessTokenMinutes}). Refresh tokens are opaque random values (closed-alpha hardening WP4):
    /// the JWT refresh tokens of before are gone, and AccessTokenSessionCheck refuses any token
    /// that still carries their "token_type" claim.
    /// </summary>
    public class TokenService : ITokenService
    {
        private readonly string _issuer;
        private readonly string _audience;
        private readonly int _accessTokenMinutes;
        private readonly SymmetricSecurityKey _key;
        private readonly JwtSecurityTokenHandler _tokenHandler;

        public TokenService(IConfiguration config)
        {
            var jwtSection = config.GetSection("Security:Jwt");
            _issuer = jwtSection["Issuer"] ?? "knk-api";
            _audience = jwtSection["Audience"] ?? "knk-app";
            var secret = jwtSection["Secret"];
            if (string.IsNullOrEmpty(secret))
            {
                throw new InvalidOperationException("Security:Jwt:Secret is required (see CLAUDE.md for the dev user-secrets setup).");
            }
            if (secret.Length < 32)
            {
                throw new InvalidOperationException("JWT secret must be at least 32 characters long for HS256.");
            }
            _accessTokenMinutes = int.TryParse(jwtSection["AccessTokenMinutes"], out var atm) ? atm : 30;

            _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            _tokenHandler = new JwtSecurityTokenHandler();
        }

        /// <inheritdoc/>
        public Task<string> GenerateAccessTokenAsync(User user, string? sessionFamilyId = null)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString(), ClaimValueTypes.String),
                new Claim(JwtRegisteredClaimNames.Email, user.Email ?? "", ClaimValueTypes.String),
                new Claim("uid", user.Id.ToString(), ClaimValueTypes.String),
                new Claim("username", user.Username, ClaimValueTypes.String),
                new Claim(AccessTokenSessionCheck.TokenVersionClaim, user.TokenVersion.ToString(), ClaimValueTypes.Integer32),
            };
            if (!string.IsNullOrEmpty(sessionFamilyId))
            {
                claims.Add(new Claim(AccessTokenSessionCheck.SessionIdClaim, sessionFamilyId, ClaimValueTypes.String));
            }

            var token = new JwtSecurityToken(
                issuer: _issuer,
                audience: _audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(_accessTokenMinutes),
                signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256Signature)
            );

            return Task.FromResult(_tokenHandler.WriteToken(token));
        }

        /// <inheritdoc/>
        public string GenerateRefreshToken()
        {
            return Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        }

        /// <inheritdoc/>
        public string HashRefreshToken(string refreshToken) => Sha256Hex(refreshToken);

        /// <summary>Lower-case hex SHA-256 of a UTF-8 string.</summary>
        public static string Sha256Hex(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

        /// <inheritdoc/>
        public Task<ClaimsPrincipal?> ValidateAccessTokenAsync(string token)
        {
            if (string.IsNullOrEmpty(token))
                return Task.FromResult<ClaimsPrincipal?>(null);

            try
            {
                var principal = _tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = _key,
                    ValidateIssuer = true,
                    ValidIssuer = _issuer,
                    ValidateAudience = true,
                    ValidAudience = _audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(60),
                }, out _);

                return Task.FromResult<ClaimsPrincipal?>(principal);
            }
            catch
            {
                // Invalid signature, expired, malformed, ...
                return Task.FromResult<ClaimsPrincipal?>(null);
            }
        }

        /// <inheritdoc/>
        public Task<int?> ExtractUserIdFromPrincipalAsync(ClaimsPrincipal principal)
        {
            var uidClaim = principal?.FindFirst("uid") ?? principal?.FindFirst(JwtRegisteredClaimNames.Sub);
            return Task.FromResult(uidClaim != null && int.TryParse(uidClaim.Value, out var userId) ? userId : (int?)null);
        }

        /// <inheritdoc/>
        public Task<DateTime?> ExtractExpirationAsync(string token)
        {
            if (string.IsNullOrEmpty(token) || !_tokenHandler.CanReadToken(token))
                return Task.FromResult<DateTime?>(null);

            try
            {
                var validTo = _tokenHandler.ReadJwtToken(token).ValidTo;
                return Task.FromResult<DateTime?>(validTo == DateTime.MinValue ? null : DateTime.SpecifyKind(validTo, DateTimeKind.Utc));
            }
            catch
            {
                return Task.FromResult<DateTime?>(null);
            }
        }

        /// <inheritdoc/>
        public async Task<bool> IsTokenExpiredAsync(string token)
        {
            var expiresAt = await ExtractExpirationAsync(token);
            return !expiresAt.HasValue || DateTime.UtcNow >= expiresAt.Value;
        }
    }
}
