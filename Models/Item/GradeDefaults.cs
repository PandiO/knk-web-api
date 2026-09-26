namespace knkwebapi_v2.Models;

/// <summary>
/// The 10 item grades (Linear KNG-6, knk-workspace <c>docs/specs/items/GRADE_DROPCHANCE.md</c>): the rows
/// <see cref="KitSeed"/> and <see cref="ItemBlueprintV1Seed"/> create when missing. Grades 1-5 are v1's star
/// scale; 6-10 are new tiers above it and deliberately uncapped for enchantment books. The names of 6-10 are
/// placeholders pending a rename.
/// <para>
/// Both seeds are create-only, so changing a value here does not touch an existing row - the
/// <c>AddGradeDropChanceAndEnchantCap</c> migration backfilled grades 1-5 once; retune later in the web app.
/// </para>
/// </summary>
public static class GradeDefaults
{
    public sealed record Spec(string Name, int Stars, decimal DropChance, int? EnchantLevelCapDivisor)
    {
        public Grade ToGrade() => new()
        {
            Name = Name,
            Stars = Stars,
            DropChance = DropChance,
            EnchantLevelCapDivisor = EnchantLevelCapDivisor,
        };
    }

    public static readonly IReadOnlyList<Spec> All = new Spec[]
    {
        new("Common", 1, 70m, 5),
        new("Uncommon", 2, 60m, 4),
        new("Rare", 3, 40m, 3),
        new("Epic", 4, 25m, 2),
        new("Legendary", 5, 15m, 1),
        new("Mythic", 6, 8m, null),
        new("Ascended", 7, 5m, null),
        new("Relic", 8, 1m, null),
        new("Exalted", 9, 0.5m, null),
        new("Divine", 10, 0.05m, null),
    };
}
