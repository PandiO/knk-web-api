using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

// One entry of a SiegeLobby's rotation (docs/specs/siege-minigame/DESIGN.md §3.8): M2M join with a
// draw Weight, composite key. Cascades from both sides - deleting a scenario just drops it from
// every rotation (a rotation row isn't worth refusing a delete over).
[FormConfigurableEntity("SiegeLobbyScenario")]
public class SiegeLobbyScenario
{
    [NavigationPair(nameof(SiegeLobby))]
    [RelatedEntityField(typeof(SiegeLobby))]
    public int SiegeLobbyId { get; set; }
    [RelatedEntityField(typeof(SiegeLobby))]
    public SiegeLobby SiegeLobby { get; set; } = null!;

    [NavigationPair(nameof(SiegeScenario))]
    [RelatedEntityField(typeof(SiegeScenario))]
    public int SiegeScenarioId { get; set; }
    [RelatedEntityField(typeof(SiegeScenario))]
    public SiegeScenario SiegeScenario { get; set; } = null!;

    public int Weight { get; set; } = 1;
}
