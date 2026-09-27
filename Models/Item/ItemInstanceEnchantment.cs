namespace knkwebapi_v2.Models;

/// <summary>
/// An enchantment on one <see cref="ItemInstance"/> - the normalized child table vision §9.1 asks for. Composite
/// key (ItemInstanceId, EnchantmentDefinitionId): an item holds each enchantment once. Cascades from its own
/// instance; Restrict to the definition.
/// </summary>
public class ItemInstanceEnchantment
{
    public long ItemInstanceId { get; set; }
    public ItemInstance ItemInstance { get; set; } = null!;

    public int EnchantmentDefinitionId { get; set; }
    public EnchantmentDefinition EnchantmentDefinition { get; set; } = null!;

    public int Level { get; set; } = 1;
}
