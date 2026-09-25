using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// InventoryMenu content port (docs/specs/inventory-menu/CONTENT_PORT_PLAN.md, CP1-CP8): the
/// first real menu content - hub, Kits, Profile, Item catalogue, Premium tiers, Player manager.
/// Create-only like every other MenuTemplateSeed entry, so each template here must be complete
/// when first seeded: a later change to an already-seeded template is a CRUD-API edit, not a
/// seed edit (plan §2). The plugin-side counterparts (feature roots, row sources, actions,
/// conditions) live in knk-paper's <c>menu/content/</c> package; if one of them is missing the
/// plugin's startup validation blocks only the menu that uses it.
///
/// Slots are absolute (engine Phase 9 J16). Colours use inline <c>&amp;</c>-codes (E7):
/// good <c>&amp;a</c>, bad <c>&amp;c</c>, label <c>&amp;7</c>, value <c>&amp;f</c>.
/// </summary>
public static partial class MenuTemplateSeed
{
    public const string HubMenuKey = "main";
    public const string KitsOverviewMenuKey = "kits.overview";
    public const string ProfileMenuKey = "profile.main";
    public const string ItemsCatalogMenuKey = "items.catalog";
    public const string PremiumTiersMenuKey = "premium.tiers";
    public const string SiegeOverviewMenuKey = "siege.overview";
    public const string UserManagerMenuKey = "users.manager";
    public const string UserManagerEditMenuKey = "users.manager.edit";
    public const string UserManagerGroupsMenuKey = "users.manager.groups";
    public const string UserManagerTitlesMenuKey = "users.manager.titles";

    /// <summary>Permission node that opens the Player manager (and shows its hub tile).</summary>
    public const string UserManagePermission = "knk.admin.user.manage";

    private static IEnumerable<MenuTemplate> ContentTemplates()
    {
        yield return HubTemplate();
        yield return KitsOverviewTemplate();
        yield return ProfileTemplate();
        yield return ItemsCatalogTemplate();
    }

    /// <summary>
    /// CP1 - the hub (<c>main</c>, opened by <c>/menu</c>). Grows from v2's sparse main menu
    /// (catalogue §4.1): one tile per ported feature, each shown by a <c>menu-available</c>
    /// Render condition so the hub can ship every planned tile now and a tile appears once its
    /// target menu exists and passed the plugin's startup validation (Siege's
    /// <c>siege.overview</c> arrives with Siege Phase 8b). Hidden tiles leave gaps (J7).
    /// </summary>
    private static MenuTemplate HubTemplate()
    {
        return new MenuTemplate
        {
            Key = HubMenuKey,
            Name = "&8Knights and Kings",
            Description = "Main menu (/menu): one tile per ported feature. Content port CP1.",
            Height = 3,
            Growth = MenuGrowthMode.Static,
            Sections =
            {
                new MenuSectionTemplate
                {
                    Name = "Tiles",
                    Kind = MenuSectionKind.StaticButtons,
                    SortOrder = 0,
                    DisplaySlot = 0,
                    Width = 9,
                    Height = 3,
                    Overflow = MenuOverflowMode.Hide,
                    Items =
                    {
                        // The viewer's own head -> Profile & titles.
                        OpenTile(4, 0, ProfileMenuKey,
                            Bind("Material", "PLAYER_HEAD", VariableRefreshPolicy.Static),
                            Bind("SkullOwner", "$player.getName$", VariableRefreshPolicy.OnDirty),
                            Bind("Name", "&f$player.getName$", VariableRefreshPolicy.OnDirty),
                            Bind("Lore", "&7Click to view your profile", VariableRefreshPolicy.Static)),

                        BackButton(8, 1),

                        OpenTile(10, 2, KitsOverviewMenuKey,
                            Bind("Material", "ARMOR_STAND", VariableRefreshPolicy.Static),
                            Bind("Name", "&aKits", VariableRefreshPolicy.Static),
                            Bind("Lore", "&7See all kits and claim one", VariableRefreshPolicy.Static)),

                        // MENU_TEMPLATES.md C.1 copy (static lines only - the T20 "you are in
                        // Siege N" line and the siege.open-own action need Siege 8b's
                        // siegeServer root / action, which don't exist yet; see CP1 status).
                        OpenTile(12, 3, SiegeOverviewMenuKey,
                            Bind("Material", "WHITE_BANNER", VariableRefreshPolicy.Static),
                            Bind("Name", "&aSiege Minigame", VariableRefreshPolicy.Static),
                            Lore(0, "&7Click here to see"),
                            Lore(1, "&7and join active Siege games"),
                            Lore(2, ""),
                            Lore(3, "&eDescription"),
                            Lore(4, "&7Siege is a minigame where"),
                            Lore(5, "&7teams fight against each other."),
                            Lore(6, "&7The defending team defends"),
                            Lore(7, "&7a main objective and multiple"),
                            Lore(8, "&7side objectives against the attackers."),
                            Lore(9, "&6The goal:"),
                            Lore(10, "&6- Attackers: capture the objectives"),
                            Lore(11, "&6- Defenders: defend the objectives")),

                        OpenTile(14, 4, ItemsCatalogMenuKey,
                            Bind("Material", "DIAMOND_SWORD", VariableRefreshPolicy.Static),
                            Bind("Name", "&bItem catalogue", VariableRefreshPolicy.Static),
                            Bind("Lore", "&7A list of all items in the game", VariableRefreshPolicy.Static)),

                        OpenTile(16, 5, PremiumTiersMenuKey,
                            Bind("Material", "GOLD_BLOCK", VariableRefreshPolicy.Static),
                            Bind("Name", "&6Premium tiers", VariableRefreshPolicy.Static),
                            Bind("Lore", "&7See every premium tier and your own", VariableRefreshPolicy.Static)),

                        WithPermissions(UserManagePermission, OpenTile(22, 6, UserManagerMenuKey,
                            Bind("Material", "PLAYER_HEAD", VariableRefreshPolicy.Static),
                            Bind("Name", "&cPlayer manager", VariableRefreshPolicy.Static),
                            Bind("Lore", "&7Manage online players (staff)", VariableRefreshPolicy.Static))),
                    },
                },
            },
        };
    }

    /// <summary>
    /// CP2 - <c>kits.overview</c> (v2 <c>KitOverview</c>/<c>KitSelectItem</c>, catalogue §4.1).
    /// Rows come from the plugin's <c>kits.available</c> source (the viewer's availability list;
    /// a single disabled "No kits available right now" row when empty). A row's click is either
    /// <c>kits.claim</c> or - for a not-yet-bought single-purchase premium kit - a
    /// <c>menu.confirm.request</c> wrapping <c>kits.purchase</c>; which one is picked per row by
    /// action-level Render conditions on <c>$row.getIsPurchase$</c>. Confirm/Cancel (row 5) only
    /// show while a kit purchase is pending (<c>kits.purchase-pending</c>). Paging works on the
    /// base engine (fixes v2 bug B28); the server's ClaimKitAsync does every permission,
    /// cooldown and cost check (fixes kits.md bug #3). AutoRefreshTicks 20 so the cooldown line
    /// (a TTL binding) counts down.
    /// </summary>
    private static MenuTemplate KitsOverviewTemplate()
    {
        return new MenuTemplate
        {
            Key = KitsOverviewMenuKey,
            Name = "&8Kits",
            Description = "Every kit the viewer can see, claim or buy (kits.available). Content port CP2.",
            Height = 6,
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
                        DemoItem(4, 0,
                            Bind("Material", "ARMOR_STAND", VariableRefreshPolicy.Static),
                            Bind("Name", "&aKits", VariableRefreshPolicy.Static),
                            Lore(0, "&7Click a kit to claim it"),
                            Lore(1, "&7Premium kits are bought once, then claimed")),
                        BackButton(8, 1),
                    },
                },
                new MenuSectionTemplate
                {
                    Name = "Kits",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 1,
                    DisplaySlot = 9,
                    Width = 9,
                    Height = 4,
                    Overflow = MenuOverflowMode.Scroll,
                    ContentSourceId = "kits.available",
                    ContentSourceParamsJson = "{}",
                    Items =
                    {
                        // Pinned below the grid (absolute slots, J16): pager 45/53, confirm 48/50.
                        PagerButton(45, "&aPrevious page", "menu.page.prev"),
                        PagerButton(53, "&aNext page", "menu.page.next"),
                        ConfirmButton(48, "kits.purchase-pending", "&7Buy the kit you picked (see chat)"),
                        CancelButton(50, "kits.purchase-pending"),
                        KitRowTemplate(),
                    },
                },
            },
        };
    }

    private static MenuItemTemplate KitRowTemplate()
    {
        var claim = new ActionBinding
        {
            ActionTypeId = "kits.claim",
            ParamsJson = "{\"kitId\":\"$row.getKitId$\"}",
            SortOrder = 0,
        };
        var purchase = new ActionBinding
        {
            ActionTypeId = "menu.confirm.request",
            ParamsJson = "{\"actionTypeId\":\"kits.purchase\",\"actionParamsJson\":\"{\\\"kitId\\\":\\\"$row.getKitId$\\\"}\","
                + "\"prompt\":\"$row.getPurchasePrompt$\"}",
            SortOrder = 1,
        };
        var item = new MenuItemTemplate
        {
            SortOrder = 0,
            Amount = 1,
            IsRowTemplate = true,
            DisplayMode = MenuDisplayMode.Normal,
            VariableBindings =
            {
                Bind("Material", "$row.getMaterial$", VariableRefreshPolicy.OnDirty),
                Bind("DisplayMode", "$row.getDisplayMode$", VariableRefreshPolicy.OnDirty),
                Bind("Name", "&f$row.getName$", VariableRefreshPolicy.OnDirty),
                Lore(0, "$row.getLoreLines$", VariableRefreshPolicy.OnDirty),
                Bind("Lore", "$row.getCooldownText$", VariableRefreshPolicy.Ttl, 1, 20),
            },
        };
        item.Actions.Add(claim);
        item.Actions.Add(purchase);
        AddActionCondition(item, claim,
            RenderCondition("value-equals", "{\"value\":\"$row.getIsPurchase$\",\"expected\":\"false\"}"));
        AddActionCondition(item, purchase,
            RenderCondition("value-equals", "{\"value\":\"$row.getIsPurchase$\",\"expected\":\"true\"}"));
        return item;
    }

    /// <summary>
    /// Action-scoped conditions also belong to the item's own Conditions (required FK) - the
    /// arrangement <c>MenuTemplateService.BuildItemAsync</c> uses (see ActionLevelRenderDemo).
    /// </summary>
    private static void AddActionCondition(MenuItemTemplate item, ActionBinding action, ConditionBinding condition)
    {
        action.Conditions.Add(condition);
        item.Conditions.Add(condition);
    }

    /// <summary>Confirm button of a <c>menu.confirm.request</c>; shown only while <paramref name="pendingCondition"/> allows.</summary>
    private static MenuItemTemplate ConfirmButton(int slot, string pendingCondition, string hint)
    {
        var item = DemoItem(slot, 100 + slot,
            Bind("Material", "LIME_CONCRETE", VariableRefreshPolicy.Static),
            Bind("Name", "&aConfirm", VariableRefreshPolicy.Static),
            Lore(0, hint));
        item.Actions.Add(new ActionBinding { ActionTypeId = "menu.confirm.accept", ParamsJson = "{}", SortOrder = 0 });
        item.Conditions.Add(RenderCondition(pendingCondition, "{}"));
        return item;
    }

    private static MenuItemTemplate CancelButton(int slot, string pendingCondition)
    {
        var item = DemoItem(slot, 100 + slot,
            Bind("Material", "RED_CONCRETE", VariableRefreshPolicy.Static),
            Bind("Name", "&cCancel", VariableRefreshPolicy.Static));
        item.Actions.Add(new ActionBinding { ActionTypeId = "menu.confirm.cancel", ParamsJson = "{}", SortOrder = 0 });
        item.Conditions.Add(RenderCondition(pendingCondition, "{}"));
        return item;
    }

    /// <summary>
    /// CP3 - <c>profile.main</c> (v1 Title information §3.3 + Personal Menu profile/Titles/
    /// Financial tiles §3.1). Row 0 reads the plugin's <c>profile</c> root (a fresh read of the
    /// viewer); rows 2-5 list every title bracket (<c>titles.brackets</c>) with the viewer's
    /// current one HIGHLIGHTed, passed ones NORMAL and the rest DISABLED. 19 brackets fit on one
    /// page; the pager (45/53) stays for safety. Read-only.
    /// </summary>
    private static MenuTemplate ProfileTemplate()
    {
        return new MenuTemplate
        {
            Key = ProfileMenuKey,
            Name = "&8Your profile",
            Description = "The viewer's balances, title progress, premium tier and every title bracket. Content port CP3.",
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
                    Height = 1,
                    Overflow = MenuOverflowMode.Hide,
                    Items =
                    {
                        DemoItem(0, 0,
                            Bind("Material", "PLAYER_HEAD", VariableRefreshPolicy.Static),
                            Bind("SkullOwner", "$player.getName$", VariableRefreshPolicy.OnDirty),
                            Bind("Name", "&f$player.getName$", VariableRefreshPolicy.OnDirty),
                            Lore(0, "$profile.getTitleLine$", VariableRefreshPolicy.OnDirty),
                            Lore(1, "$profile.getPremiumLine$", VariableRefreshPolicy.OnDirty)),
                        DemoItem(1, 1,
                            Bind("Material", "GOLD_INGOT", VariableRefreshPolicy.Static),
                            Bind("Name", "&6Balances", VariableRefreshPolicy.Static),
                            Lore(0, "&7Coins: &f$profile.getCoins$", VariableRefreshPolicy.OnDirty),
                            Lore(1, "&7Gems: &f$profile.getGems$", VariableRefreshPolicy.OnDirty),
                            Lore(2, "&7Experience: &f$profile.getExperience$", VariableRefreshPolicy.OnDirty),
                            Lore(3, "$profile.getPrestigeLine$", VariableRefreshPolicy.OnDirty)),
                        DemoItem(2, 2,
                            Bind("Material", "IRON_HELMET", VariableRefreshPolicy.Static),
                            Bind("Name", "&bTitle progress", VariableRefreshPolicy.Static),
                            Lore(0, "$profile.getProgressLines$", VariableRefreshPolicy.OnDirty)),
                        DemoItem(4, 3,
                            Bind("Material", "BOOK", VariableRefreshPolicy.Static),
                            Bind("Name", "&eTitles", VariableRefreshPolicy.Static),
                            Lore(0, "&7There are &f$profile.getTitleCount$ &7titles", VariableRefreshPolicy.OnDirty),
                            Lore(1, "&7Earn experience to climb them")),
                        BackButton(8, 4),
                    },
                },
                new MenuSectionTemplate
                {
                    Name = "Titles",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 1,
                    DisplaySlot = 18,
                    Width = 9,
                    Height = 4,
                    Overflow = MenuOverflowMode.Scroll,
                    ContentSourceId = "titles.brackets",
                    ContentSourceParamsJson = "{}",
                    Items =
                    {
                        PagerButton(45, "&aPrevious page", "menu.page.prev"),
                        PagerButton(53, "&aNext page", "menu.page.next"),
                        new MenuItemTemplate
                        {
                            SortOrder = 0,
                            Amount = 1,
                            IsRowTemplate = true,
                            DisplayMode = MenuDisplayMode.Normal,
                            VariableBindings =
                            {
                                Bind("Material", "IRON_HELMET", VariableRefreshPolicy.Static),
                                Bind("DisplayMode", "$row.getDisplayMode$", VariableRefreshPolicy.OnDirty),
                                Bind("Name", "&f$row.getName$", VariableRefreshPolicy.OnDirty),
                                Lore(0, "$row.getLoreLines$", VariableRefreshPolicy.OnDirty),
                            },
                        },
                    },
                },
            },
        };
    }

    /// <summary>
    /// CP4 - <c>items.catalog</c> (v1 List of items §3.11), built from the <c>example.catalog</c>
    /// seed: the engine's <c>catalog.itemblueprints</c> source with search (shift-click clears) and
    /// paging. No filter buttons: the source forwards filters to
    /// <c>ItemBlueprintRepository.SearchAsync</c>, which applies none (only the search term), so a
    /// facet would do nothing. The header's total comes from the plugin's <c>itemsCatalog</c> root.
    /// Read-only - catalogue items carry no actions.
    /// </summary>
    private static MenuTemplate ItemsCatalogTemplate()
    {
        return new MenuTemplate
        {
            Key = ItemsCatalogMenuKey,
            Name = "&8Item catalogue",
            Description = "Every item blueprint, searchable and paged (catalog.itemblueprints). Content port CP4.",
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
                    Height = 1,
                    Overflow = MenuOverflowMode.Hide,
                    Items =
                    {
                        DemoItem(4, 0,
                            Bind("Material", "DIAMOND_SWORD", VariableRefreshPolicy.Static),
                            Bind("Name", "&bItem catalogue", VariableRefreshPolicy.Static),
                            Lore(0, "&7A list of all items in the game"),
                            Lore(1, "$itemsCatalog.getTotalLine$", VariableRefreshPolicy.OnDirty)),
                        BackButton(8, 1),
                    },
                },
                new MenuSectionTemplate
                {
                    Name = "Items",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 1,
                    DisplaySlot = 9,
                    Width = 9,
                    Height = 4,
                    Overflow = MenuOverflowMode.Scroll,
                    ListMode = MenuListMode.Grid,
                    Searchable = true,
                    ContentSourceId = "catalog.itemblueprints",
                    ContentSourceParamsJson = "{}",
                    Items =
                    {
                        PagerButton(45, "&aPrevious page", "menu.page.prev"),
                        PagerButton(53, "&aNext page", "menu.page.next"),
                        SearchButton(49),
                    },
                },
            },
        };
    }

    private static MenuItemTemplate SearchButton(int slot)
    {
        var item = DemoItem(slot, 100 + slot,
            Bind("Material", "OAK_SIGN", VariableRefreshPolicy.Static),
            Bind("Name", "&eSearch", VariableRefreshPolicy.Static),
            Lore(0, "&7Click to search by name"),
            Lore(1, "&7Shift-click to clear the search"));
        item.Actions.Add(new ActionBinding { ActionTypeId = "menu.search.prompt", ParamsJson = "{}", SortOrder = 0 });
        return item;
    }

    /// <summary>
    /// A pinned tile that opens <paramref name="targetKey"/> and is only rendered while that
    /// menu is available (<c>menu-available</c> Render condition, CP1).
    /// </summary>
    private static MenuItemTemplate OpenTile(int slot, int sortOrder, string targetKey, params VariableBinding[] bindings)
    {
        var item = DemoItem(slot, sortOrder, bindings);
        item.Actions.Add(new ActionBinding
        {
            ActionTypeId = "menu.open",
            ParamsJson = "{\"key\":\"" + targetKey + "\"}",
            SortOrder = 0,
        });
        item.Conditions.Add(MenuAvailable(targetKey));
        return item;
    }

    private static ConditionBinding MenuAvailable(string key) =>
        RenderCondition("menu-available", "{\"key\":\"" + key + "\"}");

    /// <summary>Same node as both <c>VisibilityPermission</c> and <c>ActionPermission</c> (Phase 4).</summary>
    private static MenuItemTemplate WithPermissions(string node, MenuItemTemplate item)
    {
        item.VisibilityPermission = node;
        item.ActionPermission = node;
        return item;
    }

    private static VariableBinding Lore(int sortOrder, string expression,
        VariableRefreshPolicy policy = VariableRefreshPolicy.Static) =>
        Bind("Lore", expression, policy, sortOrder);
}
