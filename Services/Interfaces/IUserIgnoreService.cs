using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>Outcome of <see cref="IUserIgnoreService.AddAsync"/>; the controller maps each to a status code.</summary>
    public enum UserIgnoreAddResult
    {
        /// <summary>Now ignored (or already was - adding is idempotent).</summary>
        Ignored,
        UserNotFound,
        SelfIgnore,
        /// <summary>The target holds knk.msg.unignorable (staff), so moderation contact stays possible.</summary>
        CannotIgnoreStaff,
        IgnoreLimitReached
    }

    /// <summary>
    /// A player's ignore list (KNG-18 Phase 2, docs/specs/private-messages/DESIGN.md §3.1/§3.2).
    /// The plugin keeps a copy per online player and drops the ignored player's private messages
    /// and public chat; this service is the source of truth and enforces the rules: no
    /// self-ignore, at most <see cref="UserIgnoreService.MaxIgnoresPerUser"/> entries, and staff
    /// holding <see cref="UserIgnoreService.UnignorableNode"/> can't be ignored.
    /// </summary>
    public interface IUserIgnoreService
    {
        /// <summary>Everyone the user ignores, oldest first; null if the user doesn't exist.</summary>
        Task<List<UserIgnoreDto>?> GetAsync(int userId);

        Task<UserIgnoreAddResult> AddAsync(int userId, int ignoredUserId);

        /// <summary>Idempotent: removing someone who isn't ignored (or an unknown id) is a no-op.</summary>
        Task RemoveAsync(int userId, int ignoredUserId);
    }
}
