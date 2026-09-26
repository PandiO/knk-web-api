using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// Team identity resolution (docs/specs/siege-minigame/DESIGN.md §3.4, vision §7.3): each of
/// name / chat colour / banner is the team's own value when set, else its Clan's. Ad-hoc teams (no
/// Clan) must set all three - a null here means an incomplete team (§3.9 readiness error).
/// One implementation shared by the read DTOs, readiness and the plugin's runtime-config.
/// </summary>
public static class SiegeTeamIdentity
{
    public static string? ResolveName(SiegeTeam team) =>
        NullIfBlank(team.Name) ?? NullIfBlank(team.Clan?.Name);

    public static string? ResolveChatColor(SiegeTeam team) =>
        NullIfBlank(team.ChatColor) ?? NullIfBlank(team.Clan?.ChatColor);

    public static int? ResolveBannerDesignId(SiegeTeam team) =>
        team.BannerDesignId ?? team.Clan?.BannerDesignId;

    // The banner entity itself (for runtime-config) - needs Team.BannerDesign or Clan.BannerDesign loaded.
    public static BannerDesign? ResolveBannerDesign(SiegeTeam team) =>
        team.BannerDesignId.HasValue ? team.BannerDesign : team.Clan?.BannerDesign;

    /// <summary>
    /// The default initial objective holder / gate owner: the scenario's first Defender team by
    /// (SortOrder, Id) - DESIGN §3.6, §3.7, §7.1. Null when the scenario has no Defender.
    /// </summary>
    public static SiegeTeam? FirstDefender(IEnumerable<SiegeTeam> teams) =>
        teams.Where(t => t.Role == SiegeTeamRole.Defender)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
            .FirstOrDefault();

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
