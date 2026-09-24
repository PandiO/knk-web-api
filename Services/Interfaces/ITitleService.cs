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
        Task<TitleResolutionDto> ResolveAsync(int experiencePoints);
    }
}
