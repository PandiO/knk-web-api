using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// A permission group's default /navigate route mode (PandiO/knk-workspace#852). The values are the plugin's mode
    /// words of the route modes (#707, knk-core <c>RouteMode</c>), stored lowercase. Null = no default.
    /// </summary>
    /// <remarks>
    /// On write, <see cref="PermissionGroupDto.DefaultRouteMode"/> is read like this: null (omitted) keeps the stored
    /// value, so a client or an imported form configuration that predates the field can't wipe it; an empty or blank
    /// string (the form's "None" option) clears it; one of the three words, in any case and with surrounding blanks,
    /// sets it; anything else is refused with a 400.
    /// </remarks>
    public static class PermissionGroupRouteMode
    {
        public const string Shortest = "shortest";
        public const string Efficient = "efficient";
        public const string Preferred = "preferred";

        /// <summary>The accepted values, in the order the plugin and the form list them.</summary>
        public static readonly IReadOnlyList<string> Values = new[] { Shortest, Efficient, Preferred };

        /// <summary>Throws <see cref="ArgumentException"/> when the DTO's value is neither null, blank nor a mode word.</summary>
        public static void Validate(PermissionGroupDto dto)
        {
            var value = dto.DefaultRouteMode;
            if (value == null || string.IsNullOrWhiteSpace(value)) return;
            if (!Values.Contains(value.Trim().ToLowerInvariant()))
            {
                throw new ArgumentException(
                    $"DefaultRouteMode must be one of {string.Join(", ", Values)}, or empty for no default.", nameof(dto));
            }
        }

        /// <summary>Applies the DTO's value to the group (see the class remarks); call <see cref="Validate"/> first.</summary>
        public static void Apply(PermissionGroup group, PermissionGroupDto dto)
        {
            var value = dto.DefaultRouteMode;
            if (value == null) return;
            group.DefaultRouteMode = string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
        }
    }
}
