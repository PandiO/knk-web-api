using knkwebapi_v2.Enums;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Models;

/// <summary>
/// Basic seed-data mechanism for InventoryMenu templates (IMPLEMENTATION_PLAN.md
/// Phase 1: "templates are authored directly for now" - no FormConfig UI exists
/// yet). Seeds are create-only (skip if a template with the same Key already
/// exists) rather than diffed/upserted like AbilityDefinition.SeedCanonicalAsync,
/// since templates are expected to be hand-edited via the CRUD API afterwards
/// and a diffing seed would silently clobber those edits on every restart.
///
/// The one seeded template here is a structural smoke test exercising every
/// entity in the schema, not real game content - porting v1's screens and the
/// live Kits/Sieges menus is explicitly separate, later work per
/// IMPLEMENTATION_PLAN.md's "explicitly out of scope for this plan" section.
/// </summary>
public static class MenuTemplateSeed
{
    public static async Task SeedCanonicalAsync(KnKDbContext context, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        var created = 0;

        foreach (var template in CanonicalTemplates())
        {
            var exists = await context.MenuTemplates.AnyAsync(m => m.Key == template.Key, cancellationToken);
            if (exists) continue;

            await context.MenuTemplates.AddAsync(template, cancellationToken);
            created++;
        }

        if (created > 0)
            await context.SaveChangesAsync(cancellationToken);

        logger?.LogInformation("MenuTemplate seed complete. Templates created: {Created}", created);
    }

    private static IEnumerable<MenuTemplate> CanonicalTemplates()
    {
        yield return new MenuTemplate
        {
            Key = "example.placeholder",
            Name = "Example Placeholder Menu",
            Description = "Phase 1 smoke-test seed exercising every entity in the schema - not real menu content.",
            Height = 3,
            Growth = MenuGrowthMode.Static,
            Sections =
            {
                new MenuSectionTemplate
                {
                    Name = "Header",
                    Kind = MenuSectionKind.StaticButtons,
                    SortOrder = 0,
                    DisplaySlot = 0,
                    Width = 9,
                    Height = 1,
                    PositionMode = MenuPositionMode.Static,
                    AlignVertical = MenuAlignVertical.Top,
                    AlignHorizontal = MenuAlignHorizontal.Left,
                    Overflow = MenuOverflowMode.Hide,
                    ListMode = MenuListMode.Default,
                    Priority = MenuRenderPriority.Medium,
                    VariableBindings =
                    {
                        new VariableBinding
                        {
                            TargetProperty = "Name",
                            SortOrder = 0,
                            Expression = "Example Placeholder",
                            RefreshPolicy = VariableRefreshPolicy.Static,
                        },
                    },
                    Items =
                    {
                        new MenuItemTemplate
                        {
                            SortOrder = 0,
                            Amount = 1,
                            DisplayMode = MenuDisplayMode.Normal,
                            VariableBindings =
                            {
                                new VariableBinding
                                {
                                    TargetProperty = "Name",
                                    SortOrder = 0,
                                    Expression = "$player.getName$",
                                    RefreshPolicy = VariableRefreshPolicy.OnDirty,
                                },
                                new VariableBinding
                                {
                                    TargetProperty = "Lore",
                                    SortOrder = 0,
                                    Expression = "Click to close",
                                    RefreshPolicy = VariableRefreshPolicy.Static,
                                },
                            },
                            Actions =
                            {
                                new ActionBinding
                                {
                                    ActionTypeId = "menu.close",
                                    ParamsJson = "{}",
                                    SortOrder = 0,
                                },
                            },
                            Conditions =
                            {
                                new ConditionBinding
                                {
                                    ConditionTypeId = "always",
                                    ParamsJson = "{}",
                                    SortOrder = 0,
                                },
                            },
                        },
                    },
                },
            },
        };
    }
}
