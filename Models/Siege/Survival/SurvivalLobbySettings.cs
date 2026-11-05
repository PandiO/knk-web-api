using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

// Siege Survival phase 1 (DESIGN §3a, §10 "Survival lobby override"; PandiO/knk-workspace#41): a survival lobby's
// override of "end when all objectives are captured", Inherit / On / Off, resolved lobby → scenario per match
// (navigation-compass DESIGN C9 precedent). 1:1 with SiegeLobby (unique SiegeLobbyId), cascades with the lobby.
// A lobby without a row behaves as Inherit. Its own entity rather than a SiegeLobby column, so the lobby model,
// its DTOs and its form seed stay untouched in this link.
[FormConfigurableEntity("SurvivalLobbySettings")]
public class SurvivalLobbySettings
{
    public int Id { get; set; }

    [NavigationPair(nameof(SiegeLobby))]
    [RelatedEntityField(typeof(SiegeLobby))]
    public int SiegeLobbyId { get; set; }
    [RelatedEntityField(typeof(SiegeLobby))]
    public SiegeLobby SiegeLobby { get; set; } = null!;

    public SurvivalAllCapturedOverride AllCapturedOverride { get; set; } = SurvivalAllCapturedOverride.Inherit;
}
