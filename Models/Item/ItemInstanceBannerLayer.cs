using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// One banner pattern layer of a minted shield (PandiO/knk-workspace#232), copied from the blueprint's
/// <see cref="ItemBlueprintBannerLayer"/> rows at mint and frozen there, like the instance's grade. Rendered bottom to
/// top by (<see cref="SortOrder"/>, <see cref="Id"/>); cascades with its instance.
/// </summary>
public class ItemInstanceBannerLayer
{
    public long Id { get; set; }

    public long ItemInstanceId { get; set; }
    public ItemInstance ItemInstance { get; set; } = null!;

    public int SortOrder { get; set; }

    public string PatternKey { get; set; } = null!;

    public BannerDyeColor Color { get; set; } = BannerDyeColor.BLACK;
}
