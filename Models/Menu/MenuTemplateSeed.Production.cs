using knkwebapi_v2.Enums;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Models;

/// <summary>
/// In-game production menus (P6a, PandiO/knk-workspace#683, production DESIGN § 11.2, decision record § 15.1 M1-M5),
/// seeded as menu content so they open on a fresh database and the developer can reshape them in the web app menu
/// editor (A27). Create-only like every other seed; <see cref="RepairProductionHubTileAsync"/> adds the hub's
/// "Structures" tile to a hub seeded before it.
/// <list type="number">
///   <item><c>structures.overview</c> (M2): the "Structures" tile of the main menu. V2's overview pattern (header item at
///     slot 4 explaining the list, one item per object, click opens it) on the searchable content section with
///     FilterBar facets Town, Kind and Status. Rows come from the plugin's <c>structures.overview</c> source
///     (<c>POST api/StructureMenus/search</c>); the plugin pre-sets the Town facet from the player's position
///     (territory lookup, #689), empty outside any town's land.</item>
///   <item><c>structures.structure</c> (M1, M3, M5): V2 <c>ProductionStructureMenu</c>: a details header, then
///     "Productions" (rates), "Workers", "Warnings" and the transport orders entry for members of the town and staff
///     (<c>structure.getSeesDetails</c>, from <c>GET api/StructureMenus/{id}</c>), and V2 <c>StorageOverview</c>'s
///     storage list ("Amount: N" per item, source <c>structures.storages</c>). Staff buttons (M4) in the bottom row,
///     behind <c>knk.admin.world</c>: survey now, dry run, show the area outline. Players get no action buttons.</item>
///   <item><c>structures.orders</c> (M1, M3): open and recent orders of one structure (<c>GET
///     api/StructureMenus/{id}/orders</c>): cargo, from, to, state, expected arrival. Staff can cancel an open order or
///     retry a waiting, returned or cancelled one (with a confirm).</item>
/// </list>
/// The plugin side (feature, roots <c>structures</c>/<c>structure</c>/<c>structureOrders</c>, sources, actions) is
/// link L18 (PandiO/knk-workspace#74).
/// </summary>
public static partial class MenuTemplateSeed
{
    public const string StructuresOverviewMenuKey = "structures.overview";
    public const string StructureMenuKey = "structures.structure";
    public const string StructureOrdersMenuKey = "structures.orders";

    /// <summary>Hub slot of the "Structures" tile (the first free slot of the hub's bottom row).</summary>
    public const int StructuresHubSlot = 18;

    /// <summary>Staff node of the menus' action buttons (M4).</summary>
    public const string StructureStaffPermission = "knk.admin.world";

    private const string StructureCtx = "{\"structureId\":\"$ctx.structureId$\"}";

    private static IEnumerable<MenuTemplate> ProductionTemplates()
    {
        yield return StructuresOverviewTemplate();
        yield return StructureMenuTemplate();
        yield return StructureOrdersTemplate();
    }

    /// <summary>The hub's "Structures" tile (M2), shown while <c>structures.overview</c> is available.</summary>
    private static MenuItemTemplate StructuresHubTile() =>
        OpenTile(StructuresHubSlot, 9, StructuresOverviewMenuKey,
            Bind("Material", "CRAFTING_TABLE", VariableRefreshPolicy.Static),
            Bind("Name", "&6Structures", VariableRefreshPolicy.Static),
            Lore(0, "&7Farms, mines, workshops and warehouses"),
            Lore(1, "&7See what they produce and how they are doing"));

    /// <summary>
    /// Adds the "Structures" tile to a hub seeded before it (create-only seeding never would). Skips a hub that already
    /// opens <c>structures.overview</c> from any item, and one whose tile slot an admin has used. Idempotent; returns
    /// whether the hub changed.
    /// </summary>
    public static async Task<bool> RepairProductionHubTileAsync(KnKDbContext context, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        var hub = await context.MenuTemplates
            .Include(t => t.Sections).ThenInclude(s => s.Items).ThenInclude(i => i.Actions)
            .FirstOrDefaultAsync(t => t.Key == HubMenuKey, cancellationToken);
        if (hub == null) return false;
        var items = hub.Sections.SelectMany(s => s.Items).ToList();
        bool hasTile = items.Any(i => i.Actions.Any(a => a.ActionTypeId == "menu.open" && a.ParamsJson != null
            && a.ParamsJson.Contains("\"" + StructuresOverviewMenuKey + "\"")));
        var tiles = hub.Sections.FirstOrDefault(s => s.Name == "Tiles");
        if (hasTile || tiles == null || items.Any(i => i.SlotOverride == StructuresHubSlot)) return false;
        tiles.Items.Add(StructuresHubTile());
        await context.SaveChangesAsync(cancellationToken);
        logger?.LogInformation("Added the Structures tile to the main menu (slot {Slot}).", StructuresHubSlot);
        return true;
    }

    private static MenuTemplate StructuresOverviewTemplate()
    {
        var row = new MenuItemTemplate
        {
            SortOrder = 0,
            Amount = 1,
            IsRowTemplate = true,
            DisplayMode = MenuDisplayMode.Normal,
            VariableBindings =
            {
                Bind("Material", "$row.getMaterial$", VariableRefreshPolicy.OnDirty),
                Bind("Name", "&f$row.getName$", VariableRefreshPolicy.OnDirty),
                Lore(0, "$row.getLoreLines$", VariableRefreshPolicy.OnDirty),
                Lore(1, ""),
                Lore(2, "&eClick to open"),
            },
            Actions =
            {
                new ActionBinding
                {
                    ActionTypeId = "menu.open",
                    ParamsJson = "{\"key\":\"" + StructureMenuKey + "\",\"ctx.structureId\":\"$row.getId$\",\"ctx.name\":\"$row.getName$\"}",
                    SortOrder = 0,
                },
            },
        };

        return new MenuTemplate
        {
            Key = StructuresOverviewMenuKey,
            Name = "&8Structures",
            Description = "Every production structure and warehouse with search and Town, Kind and Status filters (structures.overview); the Town filter starts on the town the player stands in. Production P6 (M2).",
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
                        DemoItem(4, 0,
                            Bind("Material", "CRAFTING_TABLE", VariableRefreshPolicy.Static),
                            Bind("Name", "&6Structures", VariableRefreshPolicy.Static),
                            Lore(0, "&7Every production structure and warehouse"),
                            Lore(1, "&7Click one to see what it produces"),
                            Lore(2, "$structures.getTownFilterLine$", VariableRefreshPolicy.OnDirty)),
                        BackButton(8, 1),
                    },
                },
                new MenuSectionTemplate
                {
                    Name = "Structures",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 1,
                    DisplaySlot = 9,
                    Width = 9,
                    Height = 4,
                    MinHeight = 1,
                    Overflow = MenuOverflowMode.Scroll,
                    ListMode = MenuListMode.Grid,
                    Searchable = true,
                    ContentSourceId = "structures.overview",
                    ContentSourceParamsJson = "{}",
                    Items =
                    {
                        row,
                        PagerButton(45, "&aPrevious page", "menu.page.prev"),
                        FacetButton(46, "Town", "COMPASS", "&eTown", "$structures.getTownValues$", "$structures.getTownFilterLine$"),
                        FacetButton(47, "Kind", "HOPPER", "&eKind", "$structures.getKindValues$", "$structures.getKindFilterLine$"),
                        SearchButton(49),
                        FacetButton(50, "Status", "REDSTONE_TORCH", "&eStatus", "Working,Stalled,NoRoad,Full", "$structures.getStatusFilterLine$"),
                        ClearFacetsButton(51),
                        PagerButton(53, "&aNext page", "menu.page.next"),
                    },
                },
            },
        };
    }

    private static MenuTemplate StructureMenuTemplate()
    {
        var storageRow = new MenuItemTemplate
        {
            SortOrder = 0,
            Amount = 1,
            IsRowTemplate = true,
            DisplayMode = MenuDisplayMode.Normal,
            VariableBindings =
            {
                Bind("Material", "CHEST", VariableRefreshPolicy.Static),
                Bind("Name", "&6Contents of $row.getName$", VariableRefreshPolicy.OnDirty),
                Lore(0, "$row.getFillLine$", VariableRefreshPolicy.OnDirty),
                Lore(1, "$row.getContentLines$", VariableRefreshPolicy.OnDirty),
            },
        };

        return new MenuTemplate
        {
            Key = StructureMenuKey,
            Name = "&8Structure",
            Description = "One production structure or warehouse (ctx.structureId): public details for everyone; rates, workers, warnings, storages and the orders entry for town members and staff; staff buttons (survey now, dry run, area outline). Production P6 (M1, M3-M5).",
            Height = 6,
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
                    Height = 3,
                    Overflow = MenuOverflowMode.Hide,
                    Items =
                    {
                        DemoItem(4, 0,
                            Bind("Material", "$structure.getMaterial$", VariableRefreshPolicy.OnDirty),
                            Bind("Name", "&f$structure.getName$", VariableRefreshPolicy.OnDirty),
                            Lore(0, "$structure.getSummaryLines$", VariableRefreshPolicy.OnDirty)),
                        BackButton(8, 1),
                        Detail(DemoItem(19, 2,
                            Bind("Material", "FURNACE", VariableRefreshPolicy.Static),
                            Bind("Name", "&eProductions", VariableRefreshPolicy.Static),
                            Lore(0, "$structure.getRateLines$", VariableRefreshPolicy.OnDirty))),
                        Detail(DemoItem(21, 3,
                            Bind("Material", "IRON_PICKAXE", VariableRefreshPolicy.Static),
                            Bind("Name", "&eWorkers", VariableRefreshPolicy.Static),
                            Lore(0, "$structure.getWorkerLines$", VariableRefreshPolicy.OnDirty))),
                        Detail(DemoItem(23, 4,
                            Bind("Material", "REDSTONE_TORCH", VariableRefreshPolicy.Static),
                            Bind("Name", "&cWarnings", VariableRefreshPolicy.Static),
                            Lore(0, "$structure.getWarningLines$", VariableRefreshPolicy.OnDirty))),
                        Detail(OpenStructureMenu(25, 5, StructureOrdersMenuKey,
                            Bind("Material", "MINECART", VariableRefreshPolicy.Static),
                            Bind("Name", "&eTransport orders", VariableRefreshPolicy.Static),
                            Lore(0, "$structure.getOrdersLine$", VariableRefreshPolicy.OnDirty),
                            Lore(1, "&7Click to see open and recent orders"))),
                    },
                },
                new MenuSectionTemplate
                {
                    Name = "Storages",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 1,
                    DisplaySlot = 27,
                    Width = 9,
                    Height = 2,
                    Overflow = MenuOverflowMode.Scroll,
                    ListMode = MenuListMode.Grid,
                    ContentSourceId = "structures.storages",
                    ContentSourceParamsJson = StructureCtx,
                    Items = { storageRow },
                },
                new MenuSectionTemplate
                {
                    Name = "Staff",
                    Kind = MenuSectionKind.StaticButtons,
                    SortOrder = 2,
                    DisplaySlot = 45,
                    Width = 9,
                    Height = 1,
                    Overflow = MenuOverflowMode.Hide,
                    VisibilityPermission = StructureStaffPermission,
                    Items =
                    {
                        StaffAction(45, 10, "structures.survey", "SPYGLASS", "&bSurvey now",
                            "&7Asks the game server to survey the areas now"),
                        StaffAction(46, 11, "structures.dry-run", "PAPER", "&bTransport dry run",
                            "&7Shows what the automatic transport would do"),
                        StaffAction(47, 12, "structures.outline", "GLOWSTONE_DUST", "&bShow area outline",
                            "&7Shows the production areas' outlines for a while"),
                    },
                },
            },
        };
    }

    private static MenuTemplate StructureOrdersTemplate()
    {
        const string orderParams = "{\\\"orderId\\\":\\\"$row.getId$\\\"}";
        var cancel = new ActionBinding
        {
            ActionTypeId = "menu.confirm.request",
            ParamsJson = "{\"actionTypeId\":\"structures.order-cancel\",\"actionParamsJson\":\"" + orderParams + "\","
                + "\"prompt\":\"Cancel transport #$row.getId$? Click Confirm or Cancel.\"}",
            SortOrder = 0,
        };
        var retry = new ActionBinding
        {
            ActionTypeId = "menu.confirm.request",
            ParamsJson = "{\"actionTypeId\":\"structures.order-retry\",\"actionParamsJson\":\"" + orderParams + "\","
                + "\"prompt\":\"Retry transport #$row.getId$? Click Confirm or Cancel.\"}",
            SortOrder = 1,
        };
        var row = new MenuItemTemplate
        {
            SortOrder = 0,
            Amount = 1,
            IsRowTemplate = true,
            DisplayMode = MenuDisplayMode.Normal,
            ActionPermission = StructureStaffPermission,
            VariableBindings =
            {
                Bind("Material", "$row.getMaterial$", VariableRefreshPolicy.OnDirty),
                Bind("Name", "&fTransport #$row.getId$: $row.getStatus$", VariableRefreshPolicy.OnDirty),
                Lore(0, "$row.getLoreLines$", VariableRefreshPolicy.OnDirty),
                Lore(1, "$row.getStaffHintLine$", VariableRefreshPolicy.OnDirty),
            },
        };
        row.Actions.Add(cancel);
        row.Actions.Add(retry);
        AddActionCondition(row, cancel, RenderCondition("value-equals", "{\"value\":\"$row.getCanCancel$\",\"expected\":\"true\"}"));
        AddActionCondition(row, retry, RenderCondition("value-equals", "{\"value\":\"$row.getCanRetry$\",\"expected\":\"true\"}"));

        return new MenuTemplate
        {
            Key = StructureOrdersMenuKey,
            Name = "&8Transport orders",
            Description = "Open and recent transport orders of one structure (ctx.structureId): cargo, from, to, state and expected arrival; staff can cancel or retry (with a confirm). Town members and staff only. Production P6 (M1, M3, M4).",
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
                        DemoItem(4, 0,
                            Bind("Material", "MINECART", VariableRefreshPolicy.Static),
                            Bind("Name", "&eTransport orders of $ctx.name$", VariableRefreshPolicy.OnDirty),
                            Lore(0, "&7Open and recent orders, newest first"),
                            Lore(1, "$structureOrders.getCountLine$", VariableRefreshPolicy.OnDirty)),
                        BackButton(8, 1),
                    },
                },
                new MenuSectionTemplate
                {
                    Name = "Orders",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 1,
                    DisplaySlot = 9,
                    Width = 9,
                    Height = 4,
                    MinHeight = 1,
                    Overflow = MenuOverflowMode.Scroll,
                    ListMode = MenuListMode.Grid,
                    ContentSourceId = "structures.orders",
                    ContentSourceParamsJson = StructureCtx,
                    Items =
                    {
                        row,
                        PagerButton(45, "&aPrevious page", "menu.page.prev"),
                        ConfirmButton(48, "structures.pending", "&7Confirm the action you picked (see chat)"),
                        CancelButton(50, "structures.pending"),
                        PagerButton(53, "&aNext page", "menu.page.next"),
                    },
                },
            },
        };
    }

    // ---- Helpers ----

    /// <summary>Shown only to members of the structure's town and staff (the API's M3 filter says so).</summary>
    private static MenuItemTemplate Detail(MenuItemTemplate item)
    {
        item.Conditions.Add(RenderCondition("value-equals", "{\"value\":\"$structure.getSeesDetails$\",\"expected\":\"true\"}"));
        return item;
    }

    private static MenuItemTemplate OpenStructureMenu(int slot, int sortOrder, string key, params VariableBinding[] bindings)
    {
        var item = DemoItem(slot, sortOrder, bindings);
        item.Actions.Add(new ActionBinding
        {
            ActionTypeId = "menu.open",
            ParamsJson = "{\"key\":\"" + key + "\",\"ctx.structureId\":\"$ctx.structureId$\",\"ctx.name\":\"$ctx.name$\"}",
            SortOrder = 0,
        });
        return item;
    }

    private static MenuItemTemplate StaffAction(int slot, int sortOrder, string actionTypeId, string material, string name, string hint)
    {
        var item = DemoItem(slot, sortOrder,
            Bind("Material", material, VariableRefreshPolicy.Static),
            Bind("Name", name, VariableRefreshPolicy.Static),
            Lore(0, hint),
            Lore(1, "&8Staff only"));
        item.Actions.Add(new ActionBinding { ActionTypeId = actionTypeId, ParamsJson = StructureCtx, SortOrder = 0 });
        return WithPermissions(StructureStaffPermission, item);
    }

    private static MenuItemTemplate FacetButton(int slot, string facetKey, string material, string name, string values, string currentLine)
    {
        var item = DemoItem(slot, 100 + slot,
            Bind("Material", material, VariableRefreshPolicy.Static),
            Bind("Name", name, VariableRefreshPolicy.Static),
            Lore(0, currentLine, VariableRefreshPolicy.OnDirty),
            Lore(1, "&7Click to show the next one"),
            Lore(2, "&7(after the last one: all again)"));
        item.Actions.Add(new ActionBinding
        {
            ActionTypeId = "menu.filter.cycle",
            ParamsJson = "{\"facetKey\":\"" + facetKey + "\",\"values\":\"" + values + "\"}",
            SortOrder = 0,
        });
        return item;
    }

    private static MenuItemTemplate ClearFacetsButton(int slot)
    {
        var item = DemoItem(slot, 100 + slot,
            Bind("Material", "BARRIER", VariableRefreshPolicy.Static),
            Bind("Name", "&cClear filters", VariableRefreshPolicy.Static),
            Lore(0, "&7Shows every structure again"));
        foreach (var (facet, order) in new[] { ("Town", 0), ("Kind", 1), ("Status", 2) })
            item.Actions.Add(new ActionBinding { ActionTypeId = "menu.filter.clear", ParamsJson = "{\"facetKey\":\"" + facet + "\"}", SortOrder = order });
        return item;
    }
}
