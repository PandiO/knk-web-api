using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Service for generating and managing link codes for account linking.
    /// </summary>
    public interface ILinkCodeService
    {
        /// <summary>
        /// Generates a cryptographically secure 8-character alphanumeric code.
        /// Format: ABC12XYZ
        /// </summary>
        /// <returns>8-character alphanumeric code</returns>
        Task<string> GenerateCodeAsync();

        /// <summary>
        /// Generates and persists a new link code for a user.
        /// </summary>
        /// <param name="userId">Optional user ID to associate with code (null for web-first flow)</param>
        /// <returns>Link code response with code and expiration</returns>
        Task<LinkCodeResponseDto> GenerateLinkCodeAsync(int? userId);

        /// <summary>
        /// Validates a link code without consuming it.
        /// </summary>
        /// <param name="code">Link code to validate</param>
        /// <returns>Validation result with link code data if valid</returns>
        Task<(bool IsValid, LinkCode? LinkCode, string? Error)> ValidateLinkCodeAsync(string code);

        /// <summary>
        /// Validates and consumes a link code, marking it as used.
        /// </summary>
        /// <param name="code">Link code to consume</param>
        /// <returns>Consumption result with link code data if successful</returns>
        Task<(bool Success, LinkCode? LinkCode, string? Error)> ConsumeLinkCodeAsync(string code);

        /// <summary>
        /// Gets all expired link codes for cleanup.
        /// </summary>
        /// <returns>Collection of expired link codes</returns>
        Task<IEnumerable<LinkCode>> GetExpiredCodesAsync();

        /// <summary>
        /// Marks active codes past their ExpiresAt as Expired and returns how many it changed.
        /// </summary>
        Task<int> CleanupExpiredCodesAsync();

        /// <summary>
        /// Deletes codes that were used or expired before <paramref name="before"/> (KNG-208,
        /// run hourly by LinkCodeCleanupService). Active codes are never deleted.
        /// </summary>
        /// <returns>Number of rows deleted</returns>
        Task<int> DeleteFinishedCodesAsync(DateTime before);

        /// <summary>
        /// The admission gate's claim code for a Mojang-verified player (KNG-214, DESIGN § 5.2):
        /// 8 Crockford base32 characters, <see cref="LinkCodePurpose.Claim"/>, no user, bound to
        /// the UUID and name. One active code per UUID: an unexpired one is returned again
        /// instead of a new one, so a player who rejoins sees the same code.
        /// </summary>
        Task<LinkCode> CreateClaimCodeAsync(string minecraftUuid, string minecraftName, TimeSpan timeToLive);
    }
}
