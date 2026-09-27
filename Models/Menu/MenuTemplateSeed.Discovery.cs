using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// Domain discovery (docs/specs/domain-discovery/DESIGN.md §3.7, KNG-20): <c>discoveries.main</c>,
/// opened by <c>/discoveries</c> and the hub's Discoveries tile (slot 20). New content - v1 only
/// had a dead "Knowledge" BOOK tile on the Personal Menu (§1.1.5), whose copy the header reuses.
/// Create-only like every other MenuTemplateSeed entry. The plugin side is knk-paper's
/// <c>DiscoveriesMenuFeature</c>: root <c>discoveries</c> (the viewer's summary) and row source
/// <c>discoveries.rows</c> (every discoverable place with the viewer's state; the engine's page and
/// the DomainType/Status filters are forwarded to <c>POST api/users/{id}/discoveries/progress</c>).
/// </summary>
public static partial class MenuTemplateSeed
{
    public const string DiscoveriesMenuKey = "discoveries.main";

    /// <summary>Values of the Type filter button - the API's discoverable domain types.</summary>
    public const string DiscoveryTypeFilterValues = "Town,District,Structure,GateStructure";

    private static IEnumerable<MenuTemplate> DiscoveryTemplates()
    {
        yield return DiscoveriesTemplate();
    }

    /// <summary>
    /// Header: the viewer's head with per-type counts, the Knowledge book (latest discovery,
    /// lifetime rewards), Back. Rows 2-4: the places, discovered ones by type material and the
    /// latest HIGHLIGHTed, undiscovered ones DISABLED gray dye - Towns by name, the rest masked
    /// "???" with their town (D6). Row 5: pager, Type and Status filters, Clear filters.
    /// Read-only: rows have no click action.
    /// </summary>
    private static MenuTemplate DiscoveriesTemplate()
    {
        var typeFilter = DemoItem(47, 147,
            Bind("Material", "HOPPER", VariableRefreshPolicy.Static),
            Bind("Name", "&eType", VariableRefreshPolicy.Static),
            Lore(0, "&7Click to show the next type"),
            Lore(1, "&7(after the last one: every type again)"));
        typeFilter.Actions.Add(new ActionBinding
        {
            ActionTypeId = "menu.filter.cycle",
            ParamsJson = "{\"facetKey\":\"DomainType\",\"values\":\"" + DiscoveryTypeFilterValues + "\"}",
            SortOrder = 0,
        });

        var statusFilter = DemoItem(49, 149,
            Bind("Material", "ENDER_EYE", VariableRefreshPolicy.Static),
            Bind("Name", "&eShow", VariableRefreshPolicy.Static),
            Lore(0, "&7Click to show only discovered,"),
            Lore(1, "&7then only undiscovered places"));
        statusFilter.Actions.Add(new ActionBinding
        {
            ActionTypeId = "menu.filter.cycle",
            ParamsJson = "{\"facetKey\":\"Status\",\"values\":\"Discovered,Undiscovered\"}",
            SortOrder = 0,
        });

        var clear = DemoItem(51, 151,
            Bind("Material", "BARRIER", VariableRefreshPolicy.Static),
            Bind("Name", "&cClear filters", VariableRefreshPolicy.Static));
        clear.Actions.Add(new ActionBinding { ActionTypeId = "menu.filter.clear", ParamsJson = "{\"facetKey\":\"DomainType\"}", SortOrder = 0 });
        clear.Actions.Add(new ActionBinding { ActionTypeId = "menu.filter.clear", ParamsJson = "{\"facetKey\":\"Status\"}", SortOrder = 1 });

        return new MenuTemplate
        {
            Key = DiscoveriesMenuKey,
            Name = "&8Discoveries",
            Description = "Every discoverable town, district, structure and gate with the viewer's progress (discoveries.rows). Domain discovery KNG-20.",
            Height = 6,
            MinHeight = 3,
            Growth = MenuGrowthMode.Dynamic,
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
                    Overflow = MenuOverflowMode.Hide,
                    Items =
                    {
                        DemoItem(0, 0,
                            Bind("Material", "PLAYER_HEAD", VariableRefreshPolicy.Static),
                            Bind("SkullOwner", "$player.getName$", VariableRefreshPolicy.OnDirty),
                            Bind("Name", "&f$player.getName$", VariableRefreshPolicy.OnDirty),
                            Lore(0, "$discoveries.getSummaryLines$", VariableRefreshPolicy.OnDirty)),
                        DemoItem(4, 1,
                            Bind("Material", "BOOK", VariableRefreshPolicy.Static),
                            Bind("Name", "&eKnowledge", VariableRefreshPolicy.Static),
                            Lore(0, "&7See all discovered places"),
                            Lore(1, "&7Latest discovered: &f$discoveries.getLatestName$", VariableRefreshPolicy.OnDirty),
                            Lore(2, "$discoveries.getRewardsLine$", VariableRefreshPolicy.OnDirty)),
                        BackButton(8, 2),
                    },
                },
                new MenuSectionTemplate
                {
                    Name = "Places",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 1,
                    DisplaySlot = 18,
                    Width = 9,
                    Height = 3,
                    MinHeight = 1,
                    Overflow = MenuOverflowMode.Scroll,
                    ListMode = MenuListMode.Grid,
                    // Filters only reach a content source on a searchable section.
                    Searchable = true,
                    ContentSourceId = "discoveries.rows",
                    ContentSourceParamsJson = "{}",
                    Items =
                    {
                        new MenuItemTemplate
                        {
                            SortOrder = 0,
                            Amount = 1,
                            IsRowTemplate = true,
                            DisplayMode = MenuDisplayMode.Normal,
                            VariableBindings =
                            {
                                Bind("Material", "$row.getMaterial$", VariableRefreshPolicy.OnDirty),
                                Bind("DisplayMode", "$row.getDisplayMode$", VariableRefreshPolicy.OnDirty),
                                Bind("Name", "$row.getName$", VariableRefreshPolicy.OnDirty),
                                Lore(0, "$row.getLoreLines$", VariableRefreshPolicy.OnDirty),
                            },
                        },
                        PagerButton(45, "&aPrevious page", "menu.page.prev"),
                        typeFilter,
                        statusFilter,
                        clear,
                        PagerButton(53, "&aNext page", "menu.page.next"),
                    },
                },
            },
        };
    }
}
