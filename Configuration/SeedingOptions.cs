using Microsoft.Extensions.Configuration;

namespace knkwebapi_v2.Configuration;

/// <summary>
/// The create-only startup seeders (end of Program.cs), bound from the "Seeding" section of appsettings.
/// </summary>
public class SeedingOptions
{
    public const string SectionName = "Seeding";

    /// <summary>
    /// With false the seeders skip their example/demo rows: the nine <c>example.*</c> menu templates
    /// (<see cref="Models.MenuTemplateSeed.ExampleKeyPrefix"/>), the disabled example siege lobby, the 18 trade-good
    /// blueprints of <see cref="Models.ItemBlueprintExampleCatalogSeed"/> and the <c>Test</c> tag of
    /// <see cref="Models.KitSeed"/>. The real content (hub and the other live menus, kits, V1 items, enchantment
    /// books, lootboxes) is seeded either way. The closed alpha runs with <c>Seeding__IncludeExamples=false</c>;
    /// example rows an earlier start already created stay until knk-workspace
    /// <c>scripts/maintenance/remove-seeded-examples.sql</c> removes them.
    /// </summary>
    public bool IncludeExamples { get; set; } = true;

    public static SeedingOptions From(IConfiguration configuration) =>
        configuration.GetSection(SectionName).Get<SeedingOptions>() ?? new SeedingOptions();
}
