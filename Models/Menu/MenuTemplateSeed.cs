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

        // A second seed, alongside (not replacing) example.placeholder above: that one's
        // single-item HIDE-overflow section can't demonstrate pagination at all. This one
        // exists purely so InventoryMenu Phase 2's pagination (IMPLEMENTATION_PLAN.md -
        // "Overflow/pagination implemented on the base class") has something real to show on
        // a live server: a 3x2 (capacity 6) SCROLL section with 10 items spans two pages.
        yield return new MenuTemplate
        {
            Key = "example.pagination",
            Name = "Example Pagination Menu",
            Description = "Phase 2 smoke-test seed exercising SCROLL overflow/pagination - not real menu content.",
            Height = 3,
            Growth = MenuGrowthMode.Static,
            Sections =
            {
                new MenuSectionTemplate
                {
                    Name = "Content",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 0,
                    DisplaySlot = 0,
                    Width = 3,
                    Height = 2,
                    PositionMode = MenuPositionMode.Static,
                    AlignVertical = MenuAlignVertical.Top,
                    AlignHorizontal = MenuAlignHorizontal.Left,
                    Overflow = MenuOverflowMode.Scroll,
                    ListMode = MenuListMode.Grid,
                    Priority = MenuRenderPriority.Medium,
                    Items = PaginationDemoItems(10),
                },
            },
        };

        // A third seed, alongside the two above: exercises IMPLEMENTATION_PLAN.md Phase 4's
        // visibilityPermission/actionPermission gating, closing reconciliation gap #10 for
        // real (v2's debug-only Caches button had no permission gate at all). "Debug Tools"
        // below literally recreates that item behind a whole-section VisibilityPermission;
        // "Content" demonstrates DESIGN_REVIEW.md §2.4's other case - an item visible to
        // everyone but only actionable by permission holders - via ActionPermission alone.
        //
        // Both permission nodes are deliberately left unregistered in plugin.yml, so Bukkit's
        // own fallback for an unregistered permission string applies: true for ops, false for
        // everyone else. That makes /op the live-test toggle on a dev server with no
        // permissions plugin installed, consistent with DESIGN_REVIEW.md's PermissionsEx-
        // independence rationale (no hard dependency on a specific permission plugin either
        // way).
        yield return new MenuTemplate
        {
            Key = "example.permissions",
            Name = "Example Permissions Menu",
            Description = "Phase 4 smoke-test seed exercising visibilityPermission/actionPermission gating - not real menu content.",
            Height = 3,
            Growth = MenuGrowthMode.Static,
            Sections =
            {
                new MenuSectionTemplate
                {
                    Name = "Content",
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
                    Items =
                    {
                        new MenuItemTemplate
                        {
                            SortOrder = 0,
                            Amount = 1,
                            DisplayMode = MenuDisplayMode.Normal,
                            ActionPermission = "knk.menu.example.permissions.act",
                            VariableBindings =
                            {
                                new VariableBinding
                                {
                                    TargetProperty = "Name",
                                    SortOrder = 0,
                                    Expression = "Preview (everyone sees this, only permission holders can act)",
                                    RefreshPolicy = VariableRefreshPolicy.Static,
                                },
                            },
                        },
                    },
                },
                new MenuSectionTemplate
                {
                    Name = "Debug Tools",
                    Kind = MenuSectionKind.StaticButtons,
                    SortOrder = 1,
                    DisplaySlot = 9,
                    Width = 9,
                    Height = 1,
                    PositionMode = MenuPositionMode.Static,
                    AlignVertical = MenuAlignVertical.Top,
                    AlignHorizontal = MenuAlignHorizontal.Left,
                    Overflow = MenuOverflowMode.Hide,
                    ListMode = MenuListMode.Default,
                    Priority = MenuRenderPriority.Medium,
                    VisibilityPermission = "knk.menu.example.permissions.debug",
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
                                    Expression = "Caches",
                                    RefreshPolicy = VariableRefreshPolicy.Static,
                                },
                            },
                        },
                    },
                },
            },
        };

        // A fourth seed: exercises IMPLEMENTATION_PLAN.md Phase 5's search/filter
        // mechanism - a Searchable SCROLL section small enough (2x2, capacity 4) that
        // an 8-item catalog spans two pages, so search-narrows-before-paginate is
        // actually observable, not just "search returns everything on one page".
        // Each item also carries a "Category" VariableBinding: DESIGN_REVIEW.md §2.3
        // describes filters faceting on real structured item fields (Category/Grade/
        // Tag) that don't exist on MenuItemTemplate's actual schema and are out of
        // this phase's authority to add (see ACTIVE_SESSIONS.md's Phase 5 entry,
        // open question 4) - so this demonstrates faceted filtering today via the
        // already-generic, already-persisted VariableBinding.TargetProperty
        // mechanism instead of inventing new columns. A "Fruit"/"Berry" category
        // split gives /knk menu filter Content Category Berry something real to
        // narrow against.
        yield return new MenuTemplate
        {
            Key = "example.search",
            Name = "Example Search & Filter Menu",
            Description = "Phase 5 smoke-test seed exercising searchable content + FilterBar-style facet filtering - not real menu content.",
            Height = 3,
            Growth = MenuGrowthMode.Static,
            Sections =
            {
                new MenuSectionTemplate
                {
                    Name = "Instructions",
                    Kind = MenuSectionKind.SearchBar,
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
                                    Expression = "Try: /knk menu search Content | /knk menu filter Content Category Berry",
                                    RefreshPolicy = VariableRefreshPolicy.Static,
                                },
                            },
                        },
                    },
                },
                new MenuSectionTemplate
                {
                    Name = "Content",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 1,
                    DisplaySlot = 9,
                    Width = 2,
                    Height = 2,
                    PositionMode = MenuPositionMode.Static,
                    AlignVertical = MenuAlignVertical.Top,
                    AlignHorizontal = MenuAlignHorizontal.Left,
                    Overflow = MenuOverflowMode.Scroll,
                    ListMode = MenuListMode.Grid,
                    Priority = MenuRenderPriority.Medium,
                    Searchable = true,
                    Items = SearchDemoItems(),
                },
            },
        };

        // Phase 6 smoke-test seed (IMPLEMENTATION_PLAN.md, DESIGN_REVIEW.md §2.2):
        // demonstrates the item-level-vs-action-level condition composition decided
        // for this phase (see ACTIVE_SESSIONS.md's Phase 6 entry, open question 3).
        // Both demo permission nodes are deliberately left unregistered in plugin.yml,
        // reusing Phase 4's example.permissions convention of relying on Bukkit's own
        // unregistered-permission-defaults-to-op fallback (no permissions plugin is
        // installed on the dev server) - a non-op player is denied, an op player is
        // allowed, with no seed/config change needed to demonstrate both outcomes.
        yield return new MenuTemplate
        {
            Key = "example.conditions",
            Name = "Example Conditional Actions Menu",
            Description = "Phase 6 smoke-test seed exercising click-time ActionRegistry/ConditionRegistry evaluation - not real menu content.",
            Height = 3,
            Growth = MenuGrowthMode.Static,
            Sections =
            {
                new MenuSectionTemplate
                {
                    Name = "Content",
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
                    Items =
                    {
                        // Item-level gate: the condition sits on the item itself
                        // (ActionBindingId null), so failing it blocks every action
                        // on the item - the menu.close action never runs and the
                        // menu stays open. Re-evaluated fresh against the live
                        // player on every click (never a render-time cached value),
                        // so granting/revoking the node between opening the menu
                        // and clicking is reflected immediately - the staleness fix
                        // DESIGN_REVIEW.md §2.2 is about.
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
                                    Expression = "Item-level gate demo",
                                    RefreshPolicy = VariableRefreshPolicy.Static,
                                },
                                new VariableBinding
                                {
                                    TargetProperty = "Lore",
                                    SortOrder = 0,
                                    Expression = "Needs knk.menu.example.conditions.itemgate to close the menu",
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
                                    ConditionTypeId = "permission-node",
                                    ParamsJson = "{\"node\":\"knk.menu.example.conditions.itemgate\"}",
                                    SortOrder = 0,
                                },
                            },
                        },
                        // Action-level gate: no item-level condition, but the first
                        // action carries its own condition (ActionBindingId set) -
                        // failing it skips just that action, not the item's other
                        // action. Clicking without the node still closes the menu
                        // (the second action's "always" condition passes) while
                        // showing the first action's denial message - proof that
                        // per-action conditions are independent, not all-or-nothing
                        // for the whole click.
                        new MenuItemTemplate
                        {
                            SortOrder = 1,
                            Amount = 1,
                            DisplayMode = MenuDisplayMode.Normal,
                            VariableBindings =
                            {
                                new VariableBinding
                                {
                                    TargetProperty = "Name",
                                    SortOrder = 0,
                                    Expression = "Action-level gate demo",
                                    RefreshPolicy = VariableRefreshPolicy.Static,
                                },
                                new VariableBinding
                                {
                                    TargetProperty = "Lore",
                                    SortOrder = 0,
                                    Expression = "Always closes; needs knk.menu.example.conditions.actiongate for the gated action to also run",
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
                                    Conditions =
                                    {
                                        new ConditionBinding
                                        {
                                            ConditionTypeId = "permission-node",
                                            ParamsJson = "{\"node\":\"knk.menu.example.conditions.actiongate\"}",
                                            SortOrder = 0,
                                        },
                                    },
                                },
                                new ActionBinding
                                {
                                    ActionTypeId = "menu.close",
                                    ParamsJson = "{}",
                                    SortOrder = 1,
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
                },
            },
        };
    }

    private static List<MenuItemTemplate> SearchDemoItems()
    {
        (string Name, string Category)[] catalog =
        {
            ("Red Apple", "Fruit"),
            ("Green Apple", "Fruit"),
            ("Banana", "Fruit"),
            ("Cherry", "Berry"),
            ("Blueberry", "Berry"),
            ("Strawberry", "Berry"),
            ("Fig", "Fruit"),
            ("Grape", "Fruit"),
        };

        var items = new List<MenuItemTemplate>();
        for (var i = 0; i < catalog.Length; i++)
        {
            items.Add(new MenuItemTemplate
            {
                SortOrder = i,
                Amount = 1,
                DisplayMode = MenuDisplayMode.Normal,
                VariableBindings =
                {
                    new VariableBinding
                    {
                        TargetProperty = "Name",
                        SortOrder = 0,
                        Expression = catalog[i].Name,
                        RefreshPolicy = VariableRefreshPolicy.Static,
                    },
                    new VariableBinding
                    {
                        TargetProperty = "Category",
                        SortOrder = 0,
                        Expression = catalog[i].Category,
                        RefreshPolicy = VariableRefreshPolicy.Static,
                    },
                },
            });
        }
        return items;
    }

    private static List<MenuItemTemplate> PaginationDemoItems(int count)
    {
        var items = new List<MenuItemTemplate>();
        for (var i = 0; i < count; i++)
        {
            items.Add(new MenuItemTemplate
            {
                SortOrder = i,
                Amount = 1,
                DisplayMode = MenuDisplayMode.Normal,
                VariableBindings =
                {
                    new VariableBinding
                    {
                        TargetProperty = "Name",
                        SortOrder = 0,
                        Expression = $"Item {i + 1}",
                        RefreshPolicy = VariableRefreshPolicy.Static,
                    },
                },
            });
        }
        return items;
    }
}
