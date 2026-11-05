using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services;

/// <summary>
/// PandiO/knk-workspace#260 (developer decision 2026-11-05): a player's chat and tab-list styles (KNG-7) come from their
/// groups in <see cref="PermissionGroupPrecedence"/> order, the order the permission check, Game Settings overrides
/// and teleport fees use (hierarchy first: a held group before the parents it inherits from; then the highest Weight).
/// Each field on its own: the first group that sets it wins; the "Default" group is the last fallback. A premium tier
/// has no special place any more (before #260 only the tier or Default counted, so e.g. a Staff group's colours were
/// never drawn).
/// </summary>
public static class GroupDisplayStyles
{
    /// <param name="NameColorGroupId">
    /// The group whose <c>NameColor</c> won, so the plugin can give it its own tab-list team; null when it came from the
    /// Default group or nothing set one (the plugin then uses its owner/staff/default teams).
    /// </param>
    public sealed record Result(string? ChatPrimaryColor, string? ChatSecondaryColor, string? NameColor, int? NameColorGroupId);

    /// <param name="ordered">The player's effective groups in precedence order (first wins).</param>
    /// <param name="defaultGroup">The "Default" group (by name), or null.</param>
    public static Result Resolve(IReadOnlyList<PermissionGroup> ordered, PermissionGroup? defaultGroup)
    {
        var chain = ordered.Where(g => defaultGroup == null || g.Id != defaultGroup.Id).ToList();
        if (defaultGroup != null) chain.Add(defaultGroup);

        static string? Value(string? style) => string.IsNullOrWhiteSpace(style) ? null : style;

        var nameFrom = chain.FirstOrDefault(g => Value(g.NameColor) != null);
        return new Result(
            chain.Select(g => Value(g.ChatPrimaryColor)).FirstOrDefault(v => v != null),
            chain.Select(g => Value(g.ChatSecondaryColor)).FirstOrDefault(v => v != null),
            nameFrom?.NameColor,
            nameFrom == null || (defaultGroup != null && nameFrom.Id == defaultGroup.Id) ? null : nameFrom.Id);
    }
}
