using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories
{
    /// <summary>
    /// Repository for managing LinkCode entities.
    /// </summary>
    public interface ILinkCodeRepository
    {
        /// <summary>
        /// Creates a new link code in the database.
        /// </summary>
        /// <param name="linkCode">Link code entity to create</param>
        /// <returns>Created link code with ID populated</returns>
        Task<LinkCode> CreateAsync(LinkCode linkCode);

        /// <summary>
        /// Retrieves a link code by its unique code string.
        /// </summary>
        /// <param name="code">8-character alphanumeric code</param>
        /// <returns>Link code if found, null otherwise</returns>
        Task<LinkCode?> GetByCodeAsync(string code);

        /// <summary>
        /// Retrieves a link code by its unique code string (alias for GetByCodeAsync).
        /// </summary>
        /// <param name="code">8-character alphanumeric code</param>
        /// <returns>Link code if found, null otherwise</returns>
        Task<LinkCode?> GetLinkCodeByCodeAsync(string code);

        /// <summary>
        /// Retrieves a link code by its ID.
        /// </summary>
        /// <param name="id">Link code ID</param>
        /// <returns>Link code if found, null otherwise</returns>
        Task<LinkCode?> GetByIdAsync(int id);

        /// <summary>
        /// Updates an existing link code.
        /// </summary>
        /// <param name="linkCode">Link code with updated values</param>
        Task UpdateAsync(LinkCode linkCode);

        /// <summary>
        /// Updates the status of a link code.
        /// </summary>
        /// <param name="id">Link code ID</param>
        /// <param name="status">New status</param>
        Task UpdateLinkCodeStatusAsync(int id, LinkCodeStatus status);

        /// <summary>Marks the code Used only if it is still Active, in one conditional write. Returns
        /// false when another request already used (or expired) it, so two concurrent registrations
        /// with the same code can't both succeed.</summary>
        Task<bool> TryMarkUsedAsync(int id);

        /// <summary>
        /// Deletes a link code by ID.
        /// </summary>
        /// <param name="id">Link code ID</param>
        Task DeleteAsync(int id);

        /// <summary>
        /// Retrieves all expired link codes for cleanup operations.
        /// </summary>
        /// <returns>Collection of expired link codes</returns>
        Task<IEnumerable<LinkCode>> GetExpiredAsync();

        /// <summary>
        /// Retrieves all expired link codes for cleanup operations (alias for GetExpiredAsync).
        /// </summary>
        /// <returns>Collection of expired link codes</returns>
        Task<IEnumerable<LinkCode>> GetExpiredLinkCodesAsync();

        /// <summary>
        /// Gets an active password reset token by its hashed token value.
        /// Password reset tokens are persisted as SHA-256 hashes in Code.
        /// </summary>
        Task<LinkCode?> GetActivePasswordResetTokenAsync(string hashedToken);

        /// <summary>
        /// Invalidates all active password-reset tokens for a user.
        /// Optionally keeps a single token active.
        /// </summary>
        Task InvalidateActivePasswordResetTokensAsync(int userId, int? excludeLinkCodeId = null);
    }
}
