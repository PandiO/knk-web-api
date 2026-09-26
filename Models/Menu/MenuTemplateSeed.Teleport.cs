using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// Teleport menu (docs/specs/teleport/DESIGN.md §3.8, KNG-17 Phase 6): <c>teleport.destinations</c>,
/// opened by a bare <c>/warp</c> and the hub's Teleport tile (slot 24). v1's Personal menu COMPASS
/// "Teleport to points on the map" and its "Teleport to spawnpoints" list (§1.2), with the viewer's
/// real warmup in the info tile. Create-only like every other MenuTemplateSeed entry. The plugin side
/// is knk-paper's <c>TeleportMenuFeature</c>: root <c>teleport</c> (warmup line, pending /tpa
/// requests), row source <c>teleport.destinations</c> (the viewer's GET api/teleport-destinations
/// list - the same data as <c>/warps</c> - with the plugin's bypass nodes applied) and the actions
/// <c>teleport.warp</c> / <c>teleport.spawn</c> / <c>teleport.requests</c>, which close the menu and
/// run the same teleport-engine path as <c>/warp</c>, <c>/spawn</c> and <c>/tpaccept</c> - so every
/// engine guard (siege, freeze, combat tag, closed domain) applies even though <c>/menu</c> passes
/// the siege command filter.
/// </summary>
public static partial class MenuTemplateSeed
{
    public const string TeleportMenuKey = "teleport.destinations";

    private static IEnumerable<MenuTemplate> TeleportTemplates()
    {
        yield return TeleportDestinationsTemplate();
    }

    /// <summary>
    /// Header: Spawn, pending teleport requests, the info COMPASS (v1 copy, the viewer's warmup),
    /// Back. Rows 2-5: one tile per destination - its type's material, price, requirements and
    /// "Available! Click here to teleport" or "Locked! &lt;reason&gt;" (DISABLED, so it can't be
    /// clicked). Row 6: pager.
    /// </summary>
    private static MenuTemplate TeleportDestinationsTemplate()
    {
        var spawn = DemoItem(0, 0,
            Bind("Material", "RED_BED", VariableRefreshPolicy.Static),
            Bind("Name", "&aSpawn", VariableRefreshPolicy.Static),
            Lore(0, "&7Teleport to the server spawn"),
            Lore(1, "&7Price: &afree"),
            Bind("DisplayMode", "$teleport.getSpawnDisplayMode$", VariableRefreshPolicy.OnDirty));
        spawn.Actions.Add(new ActionBinding { ActionTypeId = "teleport.spawn", ParamsJson = "{}", SortOrder = 0 });

        var requests = DemoItem(2, 1,
            Bind("Material", "ENDER_PEARL", VariableRefreshPolicy.Static),
            Bind("Name", "&eTeleport requests", VariableRefreshPolicy.Static),
            Lore(0, "$teleport.getRequestLines$", VariableRefreshPolicy.OnDirty),
            Bind("DisplayMode", "$teleport.getRequestsDisplayMode$", VariableRefreshPolicy.OnDirty));
        requests.Actions.Add(new ActionBinding { ActionTypeId = "teleport.requests", ParamsJson = "{}", SortOrder = 0 });

        var row = new MenuItemTemplate
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
        };
        row.Actions.Add(new ActionBinding
        {
            ActionTypeId = "teleport.warp",
            ParamsJson = "{\"domainId\":\"$row.getDomainId$\"}",
            SortOrder = 0,
        });

        return new MenuTemplate
        {
            Key = TeleportMenuKey,
            Name = "&8Teleport to spawnpoints",
            Description = "The viewer's warp destinations (teleport.destinations), spawn and pending /tpa requests. Teleport KNG-17 Phase 6.",
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
                        spawn,
                        requests,
                        DemoItem(4, 2,
                            Bind("Material", "COMPASS", VariableRefreshPolicy.Static),
                            Bind("Name", "&aTeleport to points on the map", VariableRefreshPolicy.Static),
                            Lore(0, "$teleport.getWarmupLine$", VariableRefreshPolicy.OnDirty),
                            Lore(1, "&7The prices are paid in gems!"),
                            Lore(2, "&7Moving or taking damage cancels it")),
                        BackButton(8, 3),
                    },
                },
                new MenuSectionTemplate
                {
                    Name = "Destinations",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 1,
                    DisplaySlot = 9,
                    Width = 9,
                    Height = 4,
                    MinHeight = 1,
                    Overflow = MenuOverflowMode.Scroll,
                    ListMode = MenuListMode.Grid,
                    ContentSourceId = "teleport.destinations",
                    ContentSourceParamsJson = "{}",
                    Items =
                    {
                        row,
                        PagerButton(45, "&aPrevious page", "menu.page.prev"),
                        PagerButton(53, "&aNext page", "menu.page.next"),
                    },
                },
            },
        };
    }
}
