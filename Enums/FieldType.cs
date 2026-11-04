namespace knkwebapi_v2.Enums
{
    public enum FieldType
    {
        String = 0,
        Integer = 1,
        Boolean = 2,
        DateTime = 3,
        Decimal = 4,
        Enum = 5,
        Object = 6,
        List = 7,
        HybridMinecraftMaterialRefPicker = 8,
        HybridMinecraftEnchantmentRefPicker = 9, // Custom picker for enchantment references
        // PandiO/knk-workspace#232: an ordered list of banner pattern layers (pattern key + dye colour), the shield
        // banner on ItemBlueprint.ShieldBannerLayers. The value is the layer array itself, not a related entity.
        BannerLayers = 10
    }
}
