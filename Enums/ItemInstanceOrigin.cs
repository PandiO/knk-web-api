namespace knkwebapi_v2.Enums;

/// <summary>
/// Where an <see cref="knkwebapi_v2.Models.ItemInstance"/> was minted (vision §9.1; knk-workspace
/// <c>docs/specs/lootboxes/DESIGN.md</c> §3.2). Stored as its string name. Only <see cref="Lootbox"/> is
/// written today; kits, admin gives and shops adopt instances later.
/// </summary>
public enum ItemInstanceOrigin
{
    Unknown = 0,
    Lootbox = 1,
    Kit = 2,
    Admin = 3,
    Shop = 4
}
