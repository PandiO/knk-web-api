using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>
    /// Authoring path for User ↔ PermissionGroup memberships, and premium-tier resolution
    /// (docs/specs/user-features/IMPLEMENTATION_PLAN.md §5). Premium tiers are ordinary
    /// PermissionGroups flagged IsPremiumTier; a temporary tier is just a membership with an
    /// ExpiresAt, and "restore on expiry" falls out of the resolution engine ignoring expired
    /// memberships — no separate restore bookkeeping.
    /// </summary>
    public interface IUserPermissionGroupService
    {
        Task<List<UserPermissionGroupDto>> GetByUserAsync(int userId);
        Task<List<UserPermissionGroupDto>> GetByGroupAsync(int permissionGroupId);

        /// <summary>Creates the membership, or replaces its ExpiresAt if the user already holds
        /// the group. Throws KeyNotFoundException for an unknown user/group, ArgumentException
        /// for an ExpiresAt that isn't in the future.</summary>
        Task<UserPermissionGroupDto> UpsertAsync(UpsertUserPermissionGroupDto dto, int? actorUserId = null);

        /// <summary>Throws KeyNotFoundException if the user doesn't hold the group.</summary>
        Task DeleteAsync(int userId, int permissionGroupId, int? actorUserId = null);

        /// <summary>The user's current premium tier: their highest-Weight active membership in a
        /// group flagged IsPremiumTier, or null if they hold none.</summary>
        Task<UserPermissionGroupDto?> GetActivePremiumTierAsync(int userId);

        /// <summary>Products of SalaryMultiplier, GemBonusMultiplier and ExpBonusMultiplier across
        /// the user's currently-active memberships (1.0 each if none).</summary>
        Task<RankMultipliersDto> GetActiveRankMultipliersAsync(int userId);

        /// <summary>
        /// Background sweep (RankExpirySweepService): puts users whose temporary rank expired back
        /// on Default, and queues a RankChanged notification for everyone whose rank expired in
        /// (<paramref name="after"/>, <paramref name="asOf"/>] or who was put back on Default.
        /// </summary>
        /// <returns>How many users were notified.</returns>
        Task<int> SweepExpiredRanksAsync(DateTime after, DateTime asOf);
    }
}
