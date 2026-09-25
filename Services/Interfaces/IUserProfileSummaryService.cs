using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>
    /// Assembles the user-management admin module's composite player-profile view
    /// (docs/specs/user-management/DESIGN.md §2, IMPLEMENTATION_PLAN.md Phase 1) from the same
    /// per-concern services their own endpoints already use.
    /// </summary>
    public interface IUserProfileSummaryService
    {
        /// <summary>Null if no user with this id exists.</summary>
        Task<UserProfileSummaryDto?> GetAsync(int userId);
    }
}
