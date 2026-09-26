using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

// A reusable, multi-layer banner that mirrors Minecraft's own banner data (base colour + ordered
// pattern layers). Shared: referenced by Clan now, by ad-hoc SiegeTeams later (Siege Phase 2).
// See docs/specs/siege-minigame/DESIGN.md §3.1.
//
// Layers are an owned child collection (GateStructure -> GateDoor pattern): they have their own
// endpoints (POST /api/BannerDesigns/{id}/layers, /api/BannerLayers/{id}) and BannerDesign's own
// create/update never touches them - that is what the web-app's ownedChildCollection List field
// expects.
[FormConfigurableEntity("BannerDesign")]
public class BannerDesign
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public BannerDyeColor BaseColor { get; set; } = BannerDyeColor.WHITE;

    [RelatedEntityField(typeof(BannerLayer))]
    public ICollection<BannerLayer> Layers { get; set; } = new List<BannerLayer>();
}
