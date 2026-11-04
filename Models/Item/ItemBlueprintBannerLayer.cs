using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// One banner pattern layer of a shield (or banner) blueprint (PandiO/knk-workspace#232). Same shape as the clan
/// <see cref="BannerLayer"/>: rendered bottom to top by (<see cref="SortOrder"/>, <see cref="Id"/>), the pattern as a
/// Bukkit registry key validated against <see cref="BannerPatternKeys"/>, the dye colour as its Bukkit name. Owned
/// by the blueprint: replaced as a whole on every blueprint create/update, cascades with it. The shield's own base
/// colour is <see cref="ItemBlueprint.ShieldBannerBaseColor"/>.
/// </summary>
public class ItemBlueprintBannerLayer
{
    public int Id { get; set; }

    public int ItemBlueprintId { get; set; }
    public ItemBlueprint ItemBlueprint { get; set; } = null!;

    public int SortOrder { get; set; }

    public string PatternKey { get; set; } = null!;

    public BannerDyeColor Color { get; set; } = BannerDyeColor.BLACK;
}
