using System.Text.RegularExpressions;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Validates a Minecraft text style made of legacy formatting codes only — the same "&amp;"
    /// codes the FormWizard's "Minecraft text coloring" field setting previews and the plugin's
    /// DisplayTextFormatter renders: colors &amp;0-&amp;9/&amp;a-&amp;f, formats &amp;k-&amp;o,
    /// reset &amp;r, and hex colors as &amp;x&amp;r&amp;r&amp;g&amp;g&amp;b&amp;b. Used for
    /// PermissionGroup's KNG-7 display fields, which hold a style (e.g. "&amp;e", "&amp;6&amp;l"),
    /// not text.
    /// </summary>
    public static class MinecraftTextStyle
    {
        private static readonly Regex CodesOnly = new(
            "^(?:&x(?:&[0-9a-f]){6}|&[0-9a-fk-or])+$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Blank → null; otherwise the codes in canonical form ("§" accepted as "&amp;", lower-case).
        /// Throws ArgumentException for anything that isn't purely formatting codes.
        /// </summary>
        public static string? Normalize(string? value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var canonical = value.Trim().Replace('§', '&').ToLowerInvariant();
            if (!CodesOnly.IsMatch(canonical))
                throw new ArgumentException(
                    $"{fieldName} '{value}' must contain only Minecraft formatting codes, e.g. \"&e\", \"&6&l\" or \"&x&f&f&a&a&0&0\".",
                    fieldName);
            return canonical;
        }
    }
}
