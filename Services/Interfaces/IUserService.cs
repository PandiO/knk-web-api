using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services
{
    public interface IUserService
    {
        // ===== EXISTING METHODS =====
        Task<IEnumerable<UserDto>> GetAllAsync();
        Task<UserDto?> GetByIdAsync(int id);
        Task<UserDto?> GetByUuidAsync(string uuid);
        Task<UserDto?> GetByUsernameAsync(string username);
        Task<UserDto> CreateAsync(UserCreateDto user);
        Task UpdateAsync(int id, UserDto user, int? actorUserId = null);

        /// <summary>
        /// Sets a user's personal salary / gem-bonus / XP-bonus multipliers (null = keep). Their
        /// own route since KNG-22 took them out of the generic user edit; each must be within
        /// 0..<see cref="Services.UserService.MaxPersonalMultiplier"/>. Audit-logged.
        /// </summary>
        Task SetPersonalMultipliersAsync(int id, UpdatePersonalMultipliersDto request, int? actorUserId = null);
        Task UpdateGatePassThroughMethodAsync(int id, GatePassThroughMethod method);
        Task UpdateActiveModeAsync(int id, ActiveMode mode, int? actorUserId = null);

        /// <summary>Sets IsOnline and stamps LastSeenAt=UtcNow (docs/specs/user-management/IMPLEMENTATION_PLAN.md Phase 3).</summary>
        Task UpdatePresenceAsync(int id, bool isOnline);

        /// <summary>Moderation search: users in a given PermissionGroup, optionally narrowed to currently-online ones.</summary>
        Task<IEnumerable<UserListDto>> SearchByGroupAsync(int groupId, bool? onlineOnly = null);
        Task DeleteAsync(int id);
        Task<PagedResultDto<UserListDto>> SearchAsync(PagedQueryDto query);

        // ===== NEW METHODS: VALIDATION =====
        /// <summary>
        /// Validates user creation DTO (username, email, password, etc.).
        /// Checks uniqueness constraints and password policy.
        /// </summary>
        /// <param name="dto">User creation data to validate</param>
        /// <param name="minecraftOnlyAccountIdBeingLinked">If provided, skips username uniqueness check (for linking minecraft-only accounts)</param>
        Task<(bool IsValid, string? ErrorMessage)> ValidateUserCreationAsync(UserCreateDto dto, int? minecraftOnlyAccountIdBeingLinked = null);

        Task<(bool IsValid, UserDto? User)> ValidateLinkCodeAsync(string code);

        /// <summary>
        /// Consume a link code (single-use) and return associated user if valid.
        /// </summary>
        /// <summary>
        /// Validates a password against security policy (length, weak password list, etc.).
        /// </summary>
        Task<(bool IsValid, string? ErrorMessage)> ValidatePasswordAsync(string password);

        // ===== NEW METHODS: UNIQUE CONSTRAINT CHECKS =====
        /// <summary>
        /// Check if a username is already taken.
        /// Returns conflicting user ID if taken.
        /// </summary>
        Task<(bool IsTaken, int? ConflictingUserId)> CheckUsernameTakenAsync(string username, int? excludeUserId = null);

        /// <summary>
        /// Check if an email is already taken.
        /// Returns conflicting user ID if taken.
        /// </summary>
        Task<(bool IsTaken, int? ConflictingUserId)> CheckEmailTakenAsync(string email, int? excludeUserId = null);

        /// <summary>
        /// Check if a UUID is already taken.
        /// Returns conflicting user ID if taken.
        /// </summary>
        Task<(bool IsTaken, int? ConflictingUserId)> CheckUuidTakenAsync(string uuid, int? excludeUserId = null);

        // ===== NEW METHODS: CREDENTIALS MANAGEMENT =====
        /// <summary>
        /// Change user password.
        /// Verifies current password before updating.
        /// </summary>
        Task ChangePasswordAsync(int userId, string currentPassword, string newPassword, string passwordConfirmation);

        /// <summary>
        /// Verify a plain text password against a stored hash.
        /// </summary>
        Task<bool> VerifyPasswordAsync(string plainPassword, string? passwordHash);

        /// <summary>
        /// Update user email (with optional current password verification).
        /// </summary>
        Task UpdateEmailAsync(int userId, string newEmail, string? currentPassword = null);

        // ===== NEW METHODS: BALANCES (COINS, GEMS, XP) =====
        /// <summary>
        /// Staff changes to a user's Coins, Gems and/or ExperiencePoints, each posted to the
        /// currency ledger as ADMIN_GRANT / ADMIN_TAKE / ADMIN_SET (currency-payments Phase 2):
        /// Add/Remove by an amount, or Set to a target the server applies under the row lock.
        /// All changes, any title-promotion bonuses (ITitleProgressionService) and the
        /// BalanceAdjusted audit entry commit in one transaction. Refusals throw
        /// CurrencyException (insufficient funds, cap, stale expectedCurrent, key reuse).
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="changes">At most one change per currency</param>
        /// <param name="ctx">
        /// Who and why: <c>CurrencyContext.ForCaller(...)</c> with the client's Idempotency-Key
        /// (≤ <see cref="CurrencyClientKeys.MaxLength"/> characters) and the required reason
        /// text. Each change is posted under the key plus ":coins" / ":gems" / ":xp"; the reason
        /// code is set per change.
        /// </param>
        /// <param name="metadata">Optional metadata for the audit trail</param>
        /// <param name="notifyPlayer">Queue a resulting title change for the plugin to show in-game (see AdjustBalancesDto.NotifyPlayer)</param>
        Task<BalanceAdjustmentResultDto> AdjustBalancesAsync(int userId, IReadOnlyList<BalanceChangeDto> changes, CurrencyContext ctx, string? metadata = null, bool notifyPlayer = true);

        /// <summary>Rebuilds v1's FreezeCommands (a dead no-op stub in v1 — see /freeze command
        /// javadoc in knk-plugin). Works on offline targets: writes through immediately, and the
        /// plugin restores/enforces the state on the target's next join.</summary>
        Task SetFrozenAsync(int userId, bool frozen, string? reason, int? actorUserId = null);

        // ===== NEW METHODS: LINK CODES =====
        /// <summary>
        /// Generate a link code for a user (or null for web-first flow).
        /// </summary>
        Task<LinkCodeResponseDto> GenerateLinkCodeAsync(int? userId);

        /// <summary>
        /// Consume a link code and return associated user.
        /// Marks code as used.
        /// </summary>
        Task<(bool IsValid, UserDto? User)> ConsumeLinkCodeAsync(string code);

        /// <summary>
        /// Get all expired link codes.
        /// </summary>
        Task<IEnumerable<LinkCode>> GetExpiredLinkCodesAsync();

        /// <summary>
        /// Clean up expired link codes and return count of cleaned codes.
        /// </summary>
        Task<int> CleanupExpiredLinksAsync();

        // ===== NEW METHODS: MERGING & LINKING =====
        /// <summary>
        /// Check for duplicate accounts (same UUID + Username).
        /// Returns secondary user ID if conflict exists.
        /// </summary>
        Task<(bool HasConflict, int? SecondaryUserId)> CheckForDuplicateAsync(string uuid, string username);

        /// <summary>
        /// Merge two user accounts.
        /// Keeps primary account intact, soft-deletes secondary account. The secondary account's
        /// coins and gems are forfeited as a MERGE_FORFEIT ledger posting (currency DESIGN.md §5
        /// Q6), in the same transaction as the soft delete.
        /// </summary>
        Task<UserDto> MergeAccountsAsync(int primaryUserId, int secondaryUserId);

        /// <summary>
        /// Link an authenticated web app user to a Minecraft account using a link code.
        /// Used in the web-app-first flow where user already has email/password.
        /// Handles duplicate detection and optional merge if account already exists.
        /// </summary>
        /// <param name="userId">Authenticated user ID (from JWT)</param>
        /// <param name="linkCode">Link code from Minecraft</param>
        /// <returns>Updated user with linked UUID</returns>
        Task<UserDto> LinkMinecraftAccountAsync(int userId, string linkCode);
    }
}
