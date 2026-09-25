using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories
{
    public interface IUserRepository
    {
        // ===== EXISTING METHODS =====
        Task<IEnumerable<User>> GetAllAsync();
        Task<User?> GetByIdAsync(int id);
        Task<User?> GetByUuidAsync(string uuid);
        Task<User?> GetByUsernameAsync(string username);
        Task AddUserAsync(User user);
        Task UpdateUserAsync(User user);
        Task UpdateUserCoinsAsync(int id, int coins);
        Task UpdateUserCoinsByUuidAsync(string uuid, int coins);
        Task UpdateGatePassThroughMethodAsync(int id, GatePassThroughMethod method);
        Task UpdateActiveModeAsync(int id, ActiveMode mode);
        Task DeleteUserAsync(int id);
        Task<PagedResult<User>> SearchAsync(PagedQuery query);

        /// <summary>Sets IsOnline and stamps LastSeenAt=UtcNow (docs/specs/user-management/IMPLEMENTATION_PLAN.md Phase 3).</summary>
        Task UpdatePresenceAsync(int id, bool isOnline);

        /// <summary>Moderation search: users in a given PermissionGroup, optionally narrowed to currently-online ones.</summary>
        Task<IEnumerable<User>> SearchByGroupAsync(int groupId, bool? onlineOnly = null);

        // ===== NEW METHODS: UNIQUE CONSTRAINT CHECKS =====
        /// <summary>
        /// Check if a username is already taken (case-insensitive).
        /// </summary>
        /// <param name="username">Username to check</param>
        /// <param name="excludeUserId">Optional user ID to exclude from check (for updates)</param>
        /// <returns>True if taken, false otherwise</returns>
        Task<bool> IsUsernameTakenAsync(string username, int? excludeUserId = null);

        /// <summary>
        /// Check if an email is already taken (case-insensitive).
        /// </summary>
        /// <param name="email">Email to check</param>
        /// <param name="excludeUserId">Optional user ID to exclude from check (for updates)</param>
        /// <returns>True if taken, false otherwise</returns>
        Task<bool> IsEmailTakenAsync(string email, int? excludeUserId = null);

        /// <summary>
        /// Check if a UUID is already taken.
        /// </summary>
        /// <param name="uuid">UUID to check</param>
        /// <param name="excludeUserId">Optional user ID to exclude from check (for updates)</param>
        /// <returns>True if taken, false otherwise</returns>
        Task<bool> IsUuidTakenAsync(string uuid, int? excludeUserId = null);

        // ===== NEW METHODS: FIND BY MULTIPLE CRITERIA =====
        /// <summary>
        /// Get user by email (case-insensitive).
        /// </summary>
        Task<User?> GetByEmailAsync(string email);

        /// <summary>
        /// Get user by UUID and Username together.
        /// Used for duplicate detection (both must match).
        /// </summary>
        Task<User?> GetByUuidAndUsernameAsync(string uuid, string username);

        // ===== NEW METHODS: CREDENTIALS & EMAIL UPDATES =====
        /// <summary>
        /// Update only the password hash for a user.
        /// </summary>
        Task UpdatePasswordHashAsync(int id, string passwordHash);

        /// <summary>
        /// Update only the email for a user.
        /// </summary>
        Task UpdateEmailAsync(int id, string email);

        // ===== NEW METHODS: MERGE & CONFLICT RESOLUTION =====
        /// <summary>
        /// Find a duplicate user account matching UUID and Username.
        /// Used for conflict detection before merge.
        /// </summary>
        Task<User?> FindDuplicateAsync(string uuid, string username);

        /// <summary>
        /// Merge two user accounts.
        /// Consolidates data, soft-deletes secondary account, keeps primary account intact.
        /// </summary>
        /// <param name="primaryUserId">Account to keep</param>
        /// <param name="secondaryUserId">Account to delete</param>
        Task MergeUsersAsync(int primaryUserId, int secondaryUserId);

        // ===== NEW METHODS: LINK CODE OPERATIONS =====
        /// <summary>
        /// Create a new link code in the database.
        /// </summary>
        Task<LinkCode> CreateLinkCodeAsync(LinkCode linkCode);

        /// <summary>
        /// Get a link code by its code string.
        /// </summary>
        Task<LinkCode?> GetLinkCodeByCodeAsync(string code);

        /// <summary>
        /// Update the status of a link code (Active → Used → Expired).
        /// </summary>
        Task UpdateLinkCodeStatusAsync(int linkCodeId, LinkCodeStatus status);

        /// <summary>
        /// Get all expired link codes (ExpiresAt < UtcNow).
        /// Used for cleanup operations.
        /// </summary>
        Task<IEnumerable<LinkCode>> GetExpiredLinkCodesAsync();
    }
}

