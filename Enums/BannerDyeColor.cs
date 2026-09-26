namespace knkwebapi_v2.Enums
{
    // Mirrors org.bukkit.DyeColor 1:1 by name (docs/specs/siege-minigame/DESIGN.md §3.1), so the
    // plugin maps with DyeColor.valueOf(name) and no translation table. Stored as a string column.
    public enum BannerDyeColor
    {
        WHITE,
        ORANGE,
        MAGENTA,
        LIGHT_BLUE,
        YELLOW,
        LIME,
        PINK,
        GRAY,
        LIGHT_GRAY,
        CYAN,
        PURPLE,
        BLUE,
        BROWN,
        GREEN,
        RED,
        BLACK
    }
}
