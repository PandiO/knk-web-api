using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>
    /// Resolves a user's title bracket from their ExperiencePoints total
    /// (docs/specs/user-features/IMPLEMENTATION_PLAN.md §4). Purely a function of the current XP
    /// value — jumps straight to the target bracket on any XP change (DESIGN.md §3), so the same
    /// resolution logic covers both promotion and demotion without special-casing direction.
    /// </summary>
    public interface ITitleService
    {
        Task<TitleResolutionDto> ResolveAsync(int experiencePoints, Models.Gender? gender = null);

        /// <summary>The full ordered bracket list — used by UserService.AdjustBalancesAsync's
        /// tier-crossing consolidation loop, which needs to walk every bracket between the
        /// previous and new XP totals, not just the single resolved one.</summary>
        Task<List<Models.TitleBracket>> GetAllOrderedAsync();
    }
}
