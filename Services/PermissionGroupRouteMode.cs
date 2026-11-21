using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// A permission group's default /navigate route mode (PandiO/knk-workspace#852): the <see cref="RouteMode"/> the
    /// plugin uses for members who type no mode word. Null = no default.
    /// </summary>
    /// <remarks>
    /// On write, <see cref="PermissionGroupDto.DefaultRouteMode"/> is read like this: a body without the property
    /// (<see cref="PermissionGroupDto.DefaultRouteModeSent"/> false) keeps the stored value, so a client or an imported
    /// form configuration that predates the field can't wipe it; null or "" (the form's "None" option) clears it; a
    /// mode name, in any case, sets it. Another word never gets here: the JSON binding answers 400. A number that is no
    /// mode (the enum converter also reads numbers) is refused with a 400 by <see cref="Validate"/>.
    /// An enum since 2026-11-21 (developer decision): it was a string checked here, which made the form field a String
    /// field with a value list; as an enum the form field is an Enum field.
    /// </remarks>
    public static class PermissionGroupRouteMode
    {
        /// <summary>The accepted names, in the order the plugin and the form list them.</summary>
        public static readonly IReadOnlyList<string> Values = Enum.GetNames<RouteMode>();

        /// <summary>Throws <see cref="ArgumentException"/> when the DTO's value is set but no defined mode.</summary>
        public static void Validate(PermissionGroupDto dto)
        {
            if (dto.DefaultRouteMode is { } mode && !Enum.IsDefined(mode))
            {
                throw new ArgumentException(
                    $"DefaultRouteMode must be one of {string.Join(", ", Values)}, or empty for no default.", nameof(dto));
            }
        }

        /// <summary>Applies the DTO's value to the group (see the class remarks); call <see cref="Validate"/> first.</summary>
        public static void Apply(PermissionGroup group, PermissionGroupDto dto)
        {
            if (!dto.DefaultRouteModeSent) return;
            group.DefaultRouteMode = dto.DefaultRouteMode;
        }
    }
}
