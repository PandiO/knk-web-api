using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

[FormConfigurableEntity("Grade")]
public class Grade
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Stars { get; set; }

    /// <summary>
    /// Percent chance (0-100) that an item of this grade drops; null when not set. Grades 1-3 are v1's
    /// <c>Product.gradeChance()</c> values (Linear KNG-6, knk-workspace <c>docs/specs/items/GRADE_DROPCHANCE.md</c>).
    /// </summary>
    public decimal? DropChance { get; set; }

    /// <summary>
    /// v1's enchant-book level cap divisor (Linear KNG-6): an item of this grade may hold an enchantment only up
    /// to <c>definitionMaxLevel / EnchantLevelCapDivisor</c> (integer division). Grades 1-5 use 5, 4, 3, 2, 1 -
    /// v1's <c>6 - grade</c>. Null means uncapped (grades 6-10). Applied by knk-plugin's enchantment books.
    /// </summary>
    public int? EnchantLevelCapDivisor { get; set; }
}
