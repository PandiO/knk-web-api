using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// InventoryMenu Phase 9 ("domain integration", E1-E9 -
/// docs/specs/inventory-menu/IMPLEMENTATION_PLAN.md "Phase 9") demo seeds. Create-only
/// like every other MenuTemplateSeed entry. Exercises every extension without any
/// Siege code; the plugin-side counterparts (content source <c>example.rows</c>,
/// variable roots <c>exampleClock</c>/<c>exampleSelected</c>) are registered by
/// knk-paper's <c>ExampleDomainMenuFeature</c>, and <c>value-equals</c> is an engine
/// condition. If those registrations are missing the plugin's startup validation
/// blocks these two menus (and only these two).
///
/// <list type="bullet">
/// <item><c>example.domain</c> - AutoRefreshTicks 20 (E4) with a TTL clock line; a row
/// template over <c>example.rows</c> (E3) using Material/Amount/BannerPatterns/
/// SkullOwner/DisplayMode bindings (E6), inline colours (E7), null/list lore (E8) and a
/// per-row Render condition (E5); a pager whose lore reads <c>$section$</c> and a
/// Back/Exit button driven by <c>$menu$</c> + <c>menu.back</c> (E9); rows open the
/// detail menu with <c>ctx.rowId</c> (E1).</item>
/// <item><c>example.domain.detail</c> - reads <c>$ctx.rowId$</c> and the
/// ctx-driven <c>exampleSelected</c> root (E1/E2), passes <c>$ctx.rowId$</c> into its own
/// content source's params (E3 param interpolation), and nests further detail menus
/// so <c>menu.back</c> has a real stack to pop.</item>
/// </list>
/// </summary>
public static partial class MenuTemplateSeed
{
    private const string ExampleRowsSource = "example.rows";
    private const string DetailMenuKey = "example.domain.detail";

    private static IEnumerable<MenuTemplate> DomainIntegrationTemplates()
    {
        yield return new MenuTemplate
        {
            Key = "example.domain",
            Name = "&8Example Domain Menu",
            Description = "InventoryMenu Phase 9 demo exercising E1-E9 (ctx params, feature roots, row templates, live repaint, render conditions, item-meta bindings, inline colour, lore omission/expansion, menu.back) - not real menu content.",
            Height = 3,
            Growth = MenuGrowthMode.Static,
            AutoRefreshTicks = 20,
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
                        // E4 + E2: a feature-registered root re-resolved every second by a
                        // TTL binding while the menu stays open (AutoRefreshTicks = 20).
                        DemoItem(0, 0,
                            Bind("Material", "CLOCK", VariableRefreshPolicy.Static),
                            Bind("Name", "&eServer clock", VariableRefreshPolicy.Static),
                            Bind("Lore", "&7Seconds since enable: &a$exampleClock.getSeconds$", VariableRefreshPolicy.Ttl, 0, 20),
                            Bind("Lore", "&7Time: &f$exampleClock.getTime$", VariableRefreshPolicy.Ttl, 1, 20),
                            Bind("Lore", "&8Static line - resolved once", VariableRefreshPolicy.Static, 2)),

                        // E6 SkullOwner (viewer's own head) + E7 precedence: the whole-line
                        // ChatColorDescription (GRAY) applies first, inline codes override after.
                        WithDescriptionColor("GRAY", DemoItem(4, 1,
                            Bind("Material", "PLAYER_HEAD", VariableRefreshPolicy.Static),
                            Bind("SkullOwner", "$player.getUniqueId$", VariableRefreshPolicy.OnDirty),
                            Bind("Name", "&a$player.getName$&7's head", VariableRefreshPolicy.OnDirty),
                            Bind("Lore", "Prefix colour (grey) first, &bthen inline aqua", VariableRefreshPolicy.Static),
                            Bind("Lore", "&cred &r&oreset to default + italic", VariableRefreshPolicy.Static, 1))),

                        // E9: Back/Exit label + hint from the engine root $menu$, menu.back action.
                        BackButton(8, 2),
                    },
                },
                new MenuSectionTemplate
                {
                    Name = "Rows",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 1,
                    DisplaySlot = 9,
                    Width = 9,
                    Height = 1,
                    Overflow = MenuOverflowMode.Scroll,
                    ContentSourceId = ExampleRowsSource,
                    ContentSourceParamsJson = "{}",
                    Items =
                    {
                        // E9 $section$ pager (absolute SlotOverrides 9 and 17 - the section's
                        // first and last cell - leaving 7 auto slots for rows).
                        PagerButton(9, "&aPrevious page", "menu.page.prev"),
                        PagerButton(17, "&aNext page", "menu.page.next"),
                        ExampleRowTemplate(),
                    },
                },
                new MenuSectionTemplate
                {
                    Name = "Conditions",
                    Kind = MenuSectionKind.StaticButtons,
                    SortOrder = 2,
                    DisplaySlot = 18,
                    Width = 9,
                    Height = 1,
                    Overflow = MenuOverflowMode.Hide,
                    Items =
                    {
                        // E5 item-level Render conditions: exactly one of these two shows.
                        WithConditions(DemoItem(20, 0,
                                Bind("Material", "LIME_CONCRETE", VariableRefreshPolicy.Static),
                                Bind("Name", "&aYou are op", VariableRefreshPolicy.Static),
                                Bind("Lore", "&7Render condition: value-equals $player.isOp$ = true", VariableRefreshPolicy.Static)),
                            RenderCondition("value-equals", "{\"value\":\"$player.isOp$\",\"expected\":\"true\"}")),
                        WithConditions(DemoItem(21, 1,
                                Bind("Material", "RED_CONCRETE", VariableRefreshPolicy.Static),
                                Bind("Name", "&cYou are not op", VariableRefreshPolicy.Static),
                                Bind("Lore", "&7Render condition: value-equals $player.isOp$ = true, negated", VariableRefreshPolicy.Static)),
                            RenderCondition("value-equals", "{\"value\":\"$player.isOp$\",\"expected\":\"true\",\"negate\":\"true\"}")),

                        // E5 action-level Render conditions: ops open the detail menu for row 1,
                        // everyone else gets the close action - one item, two mutually
                        // exclusive actions, no denial chat spam.
                        ActionLevelRenderDemo(23, 2),

                        // E8 omission: a whole-expression binding that resolves to null drops
                        // its lore line entirely; an empty string stays a blank line.
                        DemoItem(25, 3,
                            Bind("Material", "WRITABLE_BOOK", VariableRefreshPolicy.Static),
                            Bind("Name", "&eLore omission / expansion", VariableRefreshPolicy.Static),
                            Bind("Lore", "&7Line above a dropped null line", VariableRefreshPolicy.Static, 0),
                            Bind("Lore", "$exampleSelected.getNote$", VariableRefreshPolicy.OnDirty, 1),
                            Bind("Lore", "", VariableRefreshPolicy.Static, 2),
                            Bind("Lore", "&7Blank line above is an empty string", VariableRefreshPolicy.Static, 3)),
                    },
                },
            },
        };

        yield return new MenuTemplate
        {
            Key = DetailMenuKey,
            Name = "&8Example Detail",
            Description = "InventoryMenu Phase 9 demo detail menu - opened with ctx.rowId (E1), reads the ctx-driven exampleSelected root (E2) and nests itself so menu.back pops a real stack (E9) - not real menu content.",
            Height = 3,
            Growth = MenuGrowthMode.Static,
            AutoRefreshTicks = 20,
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
                            Bind("Material", "COMPASS", VariableRefreshPolicy.Static),
                            Bind("Name", "&7Menu context", VariableRefreshPolicy.Static),
                            Bind("Lore", "&7ctx.rowId = &a$ctx.rowId$", VariableRefreshPolicy.OnDirty),
                            Bind("Lore", "&7Previous menu: &f$menu.getPreviousTitle$", VariableRefreshPolicy.OnDirty, 1),
                            Bind("Lore", "&7Clock: &f$exampleClock.getTime$", VariableRefreshPolicy.Ttl, 2, 20)),

                        // E1 + E2 + E6: every property comes from the ctx-selected row.
                        DemoItem(4, 1,
                            Bind("Material", "$exampleSelected.getMaterialKey$", VariableRefreshPolicy.OnDirty),
                            Bind("Amount", "$exampleSelected.getCount$", VariableRefreshPolicy.OnDirty),
                            Bind("BannerPatterns", "$exampleSelected.getBannerPatterns$", VariableRefreshPolicy.OnDirty),
                            Bind("SkullOwner", "$exampleSelected.getSkullOwner$", VariableRefreshPolicy.OnDirty),
                            Bind("Name", "&e$exampleSelected.getName$ &7(row $ctx.rowId$)", VariableRefreshPolicy.OnDirty),
                            Bind("Lore", "$exampleSelected.getNote$", VariableRefreshPolicy.OnDirty, 0),
                            Bind("Lore", "$exampleSelected.getDetailLines$", VariableRefreshPolicy.OnDirty, 1)),

                        BackButton(8, 2),
                    },
                },
                new MenuSectionTemplate
                {
                    Name = "Others",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 1,
                    DisplaySlot = 9,
                    Width = 9,
                    Height = 2,
                    Overflow = MenuOverflowMode.Scroll,
                    ContentSourceId = ExampleRowsSource,
                    // E3: $...$ in ContentSourceParamsJson values, resolved per render.
                    ContentSourceParamsJson = "{\"excludeId\":\"$ctx.rowId$\"}",
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
                                Bind("Material", "$row.getMaterialKey$", VariableRefreshPolicy.OnDirty),
                                Bind("Name", "&7$row.getName$", VariableRefreshPolicy.OnDirty),
                                Bind("Lore", "&8Every row except ctx.rowId $ctx.rowId$", VariableRefreshPolicy.OnDirty),
                                Bind("Lore", "&8Click to open (pushes onto the back stack)", VariableRefreshPolicy.Static, 1),
                            },
                            Actions =
                            {
                                new ActionBinding
                                {
                                    ActionTypeId = "menu.open",
                                    ParamsJson = "{\"key\":\"" + DetailMenuKey + "\",\"ctx.rowId\":\"$row.getId$\"}",
                                    SortOrder = 0,
                                },
                            },
                        },
                    },
                },
            },
        };
    }

    /// <summary>
    /// The <c>example.rows</c> row template: E6 item-meta bindings, E7 inline colour,
    /// E8 null/list lore, an E5 per-row Render condition (hides rows whose
    /// <c>isHidden</c> is true) and an E1 ctx-carrying <c>menu.open</c>.
    /// </summary>
    private static MenuItemTemplate ExampleRowTemplate()
    {
        return new MenuItemTemplate
        {
            SortOrder = 0,
            Amount = 1,
            IsRowTemplate = true,
            DisplayMode = MenuDisplayMode.Normal,
            VariableBindings =
            {
                Bind("Material", "$row.getMaterialKey$", VariableRefreshPolicy.OnDirty),
                Bind("Amount", "$row.getCount$", VariableRefreshPolicy.OnDirty),
                Bind("BannerPatterns", "$row.getBannerPatterns$", VariableRefreshPolicy.OnDirty),
                Bind("SkullOwner", "$row.getSkullOwner$", VariableRefreshPolicy.OnDirty),
                Bind("DisplayMode", "$row.getDisplayMode$", VariableRefreshPolicy.OnDirty),
                Bind("Name", "&f$row.getName$", VariableRefreshPolicy.OnDirty),
                Bind("Lore", "&7Tag: &a$row.getTag$ &7- amount &a$row.getCount$", VariableRefreshPolicy.OnDirty, 0),
                Bind("Lore", "$row.getNote$", VariableRefreshPolicy.OnDirty, 1),
                Bind("Lore", "", VariableRefreshPolicy.Static, 2),
                Bind("Lore", "$row.getDetailLines$", VariableRefreshPolicy.OnDirty, 3),
                Bind("Lore", "&8Click: open details (ctx.rowId=$row.getId$)", VariableRefreshPolicy.OnDirty, 4),
            },
            Actions =
            {
                new ActionBinding
                {
                    ActionTypeId = "menu.open",
                    ParamsJson = "{\"key\":\"" + DetailMenuKey + "\",\"ctx.rowId\":\"$row.getId$\"}",
                    SortOrder = 0,
                },
            },
            Conditions =
            {
                RenderCondition("value-equals", "{\"value\":\"$row.isHidden$\",\"expected\":\"false\"}"),
            },
        };
    }

    private static MenuItemTemplate ActionLevelRenderDemo(int slot, int sortOrder)
    {
        var openDetail = new ActionBinding
        {
            ActionTypeId = "menu.open",
            ParamsJson = "{\"key\":\"" + DetailMenuKey + "\",\"ctx.rowId\":\"1\"}",
            SortOrder = 0,
        };
        var close = new ActionBinding
        {
            ActionTypeId = "menu.close",
            ParamsJson = "{}",
            SortOrder = 1,
        };

        var item = DemoItem(slot, sortOrder,
            Bind("Material", "LEVER", VariableRefreshPolicy.Static),
            Bind("Name", "&eAction-level render conditions", VariableRefreshPolicy.Static),
            Bind("Lore", "&7Op: opens the detail menu for row 1", VariableRefreshPolicy.Static, 0),
            Bind("Lore", "&7Non-op: closes the menu", VariableRefreshPolicy.Static, 1));

        item.Actions.Add(openDetail);
        item.Actions.Add(close);

        // Action-scoped conditions must also be reachable through the item's own
        // Conditions navigation (ConditionBinding.MenuItemTemplateId is a required FK) -
        // same arrangement MenuTemplateService.BuildItemAsync uses.
        var opOnly = RenderCondition("value-equals", "{\"value\":\"$player.isOp$\",\"expected\":\"true\"}");
        var nonOpOnly = RenderCondition("value-equals", "{\"value\":\"$player.isOp$\",\"expected\":\"true\",\"negate\":\"true\"}");
        openDetail.Conditions.Add(opOnly);
        close.Conditions.Add(nonOpOnly);
        item.Conditions.Add(opOnly);
        item.Conditions.Add(nonOpOnly);
        return item;
    }

    private static MenuItemTemplate BackButton(int slot, int sortOrder)
    {
        var item = DemoItem(slot, sortOrder,
            Bind("Material", "BARRIER", VariableRefreshPolicy.Static),
            Bind("Name", "&c$menu.getBackLabel$", VariableRefreshPolicy.OnDirty),
            Bind("Lore", "&7$menu.getBackHint$", VariableRefreshPolicy.OnDirty));
        item.Actions.Add(new ActionBinding { ActionTypeId = "menu.back", ParamsJson = "{}", SortOrder = 0 });
        return item;
    }

    private static MenuItemTemplate PagerButton(int slot, string name, string actionTypeId)
    {
        var item = DemoItem(slot, 100 + slot,
            Bind("Material", "ARROW", VariableRefreshPolicy.Static),
            Bind("Name", name, VariableRefreshPolicy.Static),
            Bind("Lore", "&7Page &a$section.getPage$&7/&a$section.getPageCount$", VariableRefreshPolicy.OnDirty));
        item.Actions.Add(new ActionBinding { ActionTypeId = actionTypeId, ParamsJson = "{}", SortOrder = 0 });
        return item;
    }

    private static MenuItemTemplate DemoItem(int slot, int sortOrder, params VariableBinding[] bindings)
    {
        var item = new MenuItemTemplate
        {
            SortOrder = sortOrder,
            SlotOverride = slot,
            Amount = 1,
            DisplayMode = MenuDisplayMode.Normal,
        };
        item.VariableBindings.AddRange(bindings);
        return item;
    }

    private static MenuItemTemplate WithDescriptionColor(string chatColor, MenuItemTemplate item)
    {
        item.ChatColorDescription = chatColor;
        return item;
    }

    private static MenuItemTemplate WithConditions(MenuItemTemplate item, params ConditionBinding[] conditions)
    {
        item.Conditions.AddRange(conditions);
        return item;
    }

    private static ConditionBinding RenderCondition(string conditionTypeId, string paramsJson)
    {
        return new ConditionBinding
        {
            ConditionTypeId = conditionTypeId,
            ParamsJson = paramsJson,
            SortOrder = 0,
            Phase = MenuConditionPhase.Render,
        };
    }

    private static VariableBinding Bind(string targetProperty, string expression, VariableRefreshPolicy policy,
        int sortOrder = 0, int? ttlTicks = null)
    {
        return new VariableBinding
        {
            TargetProperty = targetProperty,
            SortOrder = sortOrder,
            Expression = expression,
            RefreshPolicy = policy,
            TtlTicks = policy == VariableRefreshPolicy.Ttl ? ttlTicks ?? 20 : null,
        };
    }
}
