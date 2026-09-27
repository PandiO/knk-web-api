namespace knkwebapi_v2.Models;

/// <summary>
/// A standing rule that issues lootbox tokens (IMPLEMENTATION_PLAN.md Phase 5 issue hooks): <see cref="Quantity"/>
/// tokens of <see cref="LootboxTypeId"/> at <see cref="BoxStars"/> (null = rolled from the type's box-grade weights at
/// issue) whenever a player becomes a member of a premium tier (<see cref="PermissionGroupId"/>, v1's donator rank
/// boxes: Noble 2 rare, Royal 1 legendary, Dragonblood 2 legendary) or is granted a kit (<see cref="KitId"/>). Exactly
/// one of the two is set. Edited on the web app's lootbox page; not form-configurable.
/// </summary>
public class LootboxTokenGrant
{
    public int Id { get; set; }

    public int LootboxTypeId { get; set; }
    public LootboxType LootboxType { get; set; } = null!;

    // 1-5, null = rolled per token.
    public int? BoxStars { get; set; }

    public int Quantity { get; set; } = 1;

    public int? PermissionGroupId { get; set; }
    public PermissionGroup? PermissionGroup { get; set; }

    public int? KitId { get; set; }
    public Kit? Kit { get; set; }

    public bool Enabled { get; set; } = true;
}
