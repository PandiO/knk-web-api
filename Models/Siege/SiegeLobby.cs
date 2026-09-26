using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

// A configurable siege lobby (docs/specs/siege-minigame/DESIGN.md §3.8, D1): timings plus a
// weighted scenario rotation. Several enabled lobbies may run concurrently; the plugin's runtime
// locks (§5.5) stop two lobbies running the same scenario or two scenarios in one town at once.
// The plugin reads enabled lobbies through GET /api/siege-lobbies/runtime-config.
[FormConfigurableEntity("SiegeLobby")]
public class SiegeLobby
{
    public int Id { get; set; }

    // Shown in menus ("Siege — Cinix").
    public string Name { get; set; } = null!;

    // /siege join <key>; unique, stored lowercase.
    public string Key { get; set; } = null!;

    public bool IsEnabled { get; set; }

    public SiegeLobbyMode Mode { get; set; } = SiegeLobbyMode.Continuous;

    public int MatchmakingSeconds { get; set; } = 300;
    public int CooldownSeconds { get; set; } = 900;

    // 1–3 (menu layout cap, MENU_TEMPLATES.md C.3).
    public int VoteCandidateCount { get; set; } = 2;
    public bool AllowRandomVote { get; set; } = true;

    // Reserved for Scheduled mode (Phase 10); null for Continuous.
    public string? ScheduleJson { get; set; }

    // M2M join with Weight; join rows cascade with the lobby.
    [RelatedEntityField(typeof(SiegeLobbyScenario))]
    public ICollection<SiegeLobbyScenario> Rotation { get; set; } = new List<SiegeLobbyScenario>();
}
