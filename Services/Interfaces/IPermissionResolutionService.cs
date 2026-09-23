using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Resolves a User's effective permissions per docs/specs/user-features/DESIGN.md §2.2:
    /// direct grants first (always wins), then each PermissionGroup membership ordered by
    /// weight (highest first), then that group's own single-parent inheritance chain, with
    /// wildcard/longest-prefix matching and expiry exclusion at every level. Undeclared nodes
    /// fail closed (deny).
    /// </summary>
    public interface IPermissionResolutionService
    {
        /// <summary>Null if no user with this id exists.</summary>
        Task<PermissionCheckResponseDto?> CheckAsync(int userId, string node);

        /// <summary>Null if no user with this id exists.</summary>
        Task<PermissionEffectiveResponseDto?> GetEffectiveAsync(int userId);
    }
}
