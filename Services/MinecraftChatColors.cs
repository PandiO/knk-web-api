namespace knkwebapi_v2.Services
{
    /// <summary>
    /// The 16 named Minecraft chat colors, as stored on PermissionGroup's KNG-7 color fields.
    /// Names match Bukkit's ChatColor constants (and, lower-cased, Adventure's NamedTextColor
    /// names), which is what the plugin parses them with.
    /// </summary>
    public static class MinecraftChatColors
    {
        public static readonly IReadOnlyList<string> Names = new[]
        {
            "BLACK", "DARK_BLUE", "DARK_GREEN", "DARK_AQUA", "DARK_RED", "DARK_PURPLE", "GOLD", "GRAY",
            "DARK_GRAY", "BLUE", "GREEN", "AQUA", "RED", "LIGHT_PURPLE", "YELLOW", "WHITE"
        };

        /// <summary>
        /// Blank → null; otherwise the canonical upper-case name. Case-insensitive, and accepts
        /// spaces for underscores ("dark red"). Throws ArgumentException for anything else.
        /// </summary>
        public static string? Normalize(string? value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var canonical = value.Trim().Replace(' ', '_').ToUpperInvariant();
            if (!Names.Contains(canonical))
                throw new ArgumentException(
                    $"{fieldName} '{value}' is not a Minecraft color name. Expected one of: {string.Join(", ", Names)}.",
                    fieldName);
            return canonical;
        }
    }
}
