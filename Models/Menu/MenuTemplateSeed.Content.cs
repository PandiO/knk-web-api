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
        yield return PremiumTiersTemplate();
        yield return UserManagerTemplate();
        yield return UserManagerEditTemplate();
        yield return UserManagerTitlesTemplate();
        yield return UserManagerGroupsTemplate();
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
                        // The viewer's own head -> Profile & titles. Menu follow-up 2026-09-26:
                        // quick stats (title, rank, progress, balances, premium) in the lore.
                        OpenTile(4, 0, ProfileMenuKey,
                            Bind("Material", "PLAYER_HEAD", VariableRefreshPolicy.Static),
                            Bind("SkullOwner", "$player.getName$", VariableRefreshPolicy.OnDirty),
                            Bind("Name", "&f$player.getName$", VariableRefreshPolicy.OnDirty),
                            Lore(0, "$profile.getQuickStatsLines$", VariableRefreshPolicy.OnDirty),
                            Lore(1, ""),
                            Lore(2, "&eClick to view your profile")),

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
                            Bind("Lore", "&7Manage players (staff)", VariableRefreshPolicy.Static))),
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
            MinHeight = 3,
            Growth = MenuGrowthMode.Dynamic,
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
                        ConfirmButton(2, "kits.purchase-pending", "&7Buy the kit you picked (see chat)"),
                        CancelButton(6, "kits.purchase-pending"),
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
    /// page; the pager (45/53) stays for safety. Read-only. Menu follow-up 2026-09-26: rows are
    /// coloured by state (lime reached / golden helmet current / gray ahead), marked in the name
    /// and numbered by stack Amount; the head shows quick stats, the progress item a bar.
    /// </summary>
    private static MenuTemplate ProfileTemplate()
    {
        return new MenuTemplate
        {
            Key = ProfileMenuKey,
            Name = "&8Your profile",
            Description = "The viewer's balances, title progress, premium tier and every title bracket. Content port CP3.",
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
                            Lore(0, "$profile.getQuickStatsLines$", VariableRefreshPolicy.OnDirty)),
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
                            Lore(0, "$profile.getProgressLines$", VariableRefreshPolicy.OnDirty),
                            Lore(1, "$profile.getTitleRankLine$", VariableRefreshPolicy.OnDirty),
                            Lore(2, "$profile.getProgressBar$", VariableRefreshPolicy.OnDirty)),
                        DemoItem(4, 3,
                            Bind("Material", "BOOK", VariableRefreshPolicy.Static),
                            Bind("Name", "&eTitles", VariableRefreshPolicy.Static),
                            Lore(0, "&7There are &f$profile.getTitleCount$ &7titles", VariableRefreshPolicy.OnDirty),
                            Lore(1, "&7Earn experience to climb them"),
                            Lore(2, ""),
                            Lore(3, "&a✔ &7reached  &6» &7current  &8■ &7ahead")),
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
                                Bind("Material", "$row.getMaterial$", VariableRefreshPolicy.OnDirty),
                                Bind("Amount", "$row.getOrder$", VariableRefreshPolicy.OnDirty),
                                Bind("DisplayMode", "$row.getDisplayMode$", VariableRefreshPolicy.OnDirty),
                                Bind("Name", "$row.getMarkedName$", VariableRefreshPolicy.OnDirty),
                                Lore(0, "$row.getLoreLines$", VariableRefreshPolicy.OnDirty),
                            },
                        },
                    },
                },
            },
        };
    }

    /// <summary>
    /// CP4 - <c>items.catalog</c> (v1 List of items §3.11). Menu follow-up 2026-09-26: rows come
    /// from the plugin's <c>items.catalog</c> source, which renders each blueprint as the exact
    /// ItemStack a player is granted (name, lore, enchantments, material); the row template's
    /// bindings are only the fallback when that item can't be built. Search (shift-click clears),
    /// a Category filter (cycles <c>$itemsCatalog.getCategoryValues$</c>, the live category list;
    /// includes subcategories - <c>ItemBlueprintRepository.SearchAsync</c>) and paging. The menu
    /// shrinks to fit a short result (Growth Dynamic, MinHeight 3). Read-only.
    /// </summary>
    private static MenuTemplate ItemsCatalogTemplate()
    {
        var filter = DemoItem(47, 147,
            Bind("Material", "HOPPER", VariableRefreshPolicy.Static),
            Bind("Name", "&eFilter by category", VariableRefreshPolicy.Static),
            Lore(0, "&7Click to show the next category"),
            Lore(1, "&7(after the last one: every category again)"));
        filter.Actions.Add(new ActionBinding
        {
            ActionTypeId = "menu.filter.cycle",
            ParamsJson = "{\"facetKey\":\"Category\",\"values\":\"$itemsCatalog.getCategoryValues$\"}",
            SortOrder = 0,
        });
        var clear = DemoItem(51, 151,
            Bind("Material", "BARRIER", VariableRefreshPolicy.Static),
            Bind("Name", "&cClear category filter", VariableRefreshPolicy.Static));
        clear.Actions.Add(new ActionBinding { ActionTypeId = "menu.filter.clear", ParamsJson = "{\"facetKey\":\"Category\"}", SortOrder = 0 });

        return new MenuTemplate
        {
            Key = ItemsCatalogMenuKey,
            Name = "&8Item catalogue",
            Description = "Every item blueprint as granted, searchable, filterable by category and paged (items.catalog). Content port CP4.",
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
                    MinHeight = 1,
                    Overflow = MenuOverflowMode.Scroll,
                    ListMode = MenuListMode.Grid,
                    Searchable = true,
                    ContentSourceId = "items.catalog",
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
                                Bind("Material", "PAPER", VariableRefreshPolicy.Static),
                                Bind("Name", "&f$row.getName$", VariableRefreshPolicy.OnDirty),
                            },
                        },
                        PagerButton(45, "&aPrevious page", "menu.page.prev"),
                        filter,
                        SearchButton(49),
                        clear,
                        PagerButton(53, "&aNext page", "menu.page.next"),
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
    /// CP5 - <c>premium.tiers</c> (v1 Donator-ranks information §3.4, rebuilt from today's v3 data,
    /// not its 2017 content): the premium-tier permission groups by weight with their salary
    /// multiplier, the viewer's own tier HIGHLIGHTed; header shows the viewer's tier + expiry
    /// (<c>premium</c> root). Read-only. The perk redesign (vision §5.3) changes the data, not this
    /// screen.
    /// </summary>
    private static MenuTemplate PremiumTiersTemplate()
    {
        return new MenuTemplate
        {
            Key = PremiumTiersMenuKey,
            Name = "&8Premium tiers",
            Description = "Every premium tier (premium permission groups by weight) and the viewer's own. Content port CP5.",
            Height = 3,
            MinHeight = 2,
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
                            Bind("Material", "GOLD_BLOCK", VariableRefreshPolicy.Static),
                            Bind("Name", "&6Premium tiers", VariableRefreshPolicy.Static),
                            Lore(0, "$premium.getTierLine$", VariableRefreshPolicy.OnDirty)),
                        BackButton(8, 1),
                    },
                },
                new MenuSectionTemplate
                {
                    Name = "Tiers",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 1,
                    DisplaySlot = 9,
                    Width = 9,
                    Height = 1,
                    Overflow = MenuOverflowMode.Scroll,
                    ContentSourceId = "premium.tiers",
                    ContentSourceParamsJson = "{}",
                    Items =
                    {
                        PagerButton(18, "&aPrevious page", "menu.page.prev"),
                        PagerButton(26, "&aNext page", "menu.page.next"),
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
                                Bind("Name", "&6$row.getName$", VariableRefreshPolicy.OnDirty),
                                Lore(0, "$row.getLoreLines$", VariableRefreshPolicy.OnDirty),
                            },
                        },
                    },
                },
            },
        };
    }

    // ===== CP8 - Player manager (v1 Online-players Manager + Edit statistics §3.14) =====
    //
    // Every section carries VisibilityPermission knk.admin.user.manage (the engine skips a section
    // the viewer lacks it for); each edit also has its own knk.admin.user.<property>
    // ActionPermission and the Click condition users.outranks-target, and the plugin's
    // UserAdminService re-checks both. Step sizes live in session state (engine G1): the online
    // list opens the editor with state.pm.* defaults, the value items cycle them.

    private const string TargetParams = "{\"userId\":\"$ctx.userId$\",\"name\":\"$ctx.name$\"}";
    private const string CtxUserIdJson = "\"userId\":\"$ctx.userId$\"";

    private static MenuTemplate UserManagerTemplate()
    {
        return new MenuTemplate
        {
            Key = UserManagerMenuKey,
            Name = "&8Player manager",
            Description = "Online players the viewer outranks (users.online; with knk.admin.user.manage.all every player, online and offline, self included) -> the editor. Content port CP8.",
            Height = 6,
            MinHeight = 3,
            Growth = MenuGrowthMode.Dynamic,
            Sections =
            {
                ManagerSection(new MenuSectionTemplate
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
                            Bind("Material", "PLAYER_HEAD", VariableRefreshPolicy.Static),
                            Bind("Name", "&cPlayers", VariableRefreshPolicy.Static),
                            Lore(0, "&7Online now: &f$usersManager.getOnlineCount$", VariableRefreshPolicy.OnDirty),
                            Lore(1, "&7Staff: online players ranked below you"),
                            Lore(2, "&7Owners: every player, online and offline")),
                        BackButton(8, 1),
                    },
                }),
                ManagerSection(new MenuSectionTemplate
                {
                    Name = "Players",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 1,
                    DisplaySlot = 9,
                    Width = 9,
                    Height = 4,
                    Overflow = MenuOverflowMode.Scroll,
                    MinHeight = 1,
                    Searchable = true,
                    ContentSourceId = "users.online",
                    ContentSourceParamsJson = "{}",
                    Items =
                    {
                        PagerButton(45, "&aPrevious page", "menu.page.prev"),
                        SearchButton(49),
                        PagerButton(53, "&aNext page", "menu.page.next"),
                        new MenuItemTemplate
                        {
                            SortOrder = 0,
                            Amount = 1,
                            IsRowTemplate = true,
                            DisplayMode = MenuDisplayMode.Normal,
                            VariableBindings =
                            {
                                Bind("Material", "$row.getMaterial$", VariableRefreshPolicy.OnDirty),
                                Bind("SkullOwner", "$row.getUuid$", VariableRefreshPolicy.OnDirty),
                                Bind("DisplayMode", "$row.getDisplayMode$", VariableRefreshPolicy.OnDirty),
                                Bind("Name", "$row.getDisplayName$", VariableRefreshPolicy.OnDirty),
                                Lore(0, "$row.getLoreLines$", VariableRefreshPolicy.OnDirty),
                            },
                            Actions =
                            {
                                new ActionBinding
                                {
                                    ActionTypeId = "menu.open",
                                    ParamsJson = "{\"key\":\"" + UserManagerEditMenuKey + "\",\"ctx.userId\":\"$row.getUserId$\","
                                        + "\"ctx.name\":\"$row.getName$\",\"state.pm.coinStep\":\"100\","
                                        + "\"state.pm.gemStep\":\"10\",\"state.pm.xpStep\":\"100\"}",
                                    SortOrder = 0,
                                },
                            },
                        },
                    },
                }),
            },
        };
    }

    private static MenuTemplate UserManagerEditTemplate()
    {
        return new MenuTemplate
        {
            Key = UserManagerEditMenuKey,
            Name = "&8Edit player",
            Description = "Steppers for coins/gems/XP, title, groups, mode, salary, freeze, kick, ban for the player in ctx.userId/ctx.name. Content port CP8.",
            Height = 6,
            Growth = MenuGrowthMode.Static,
            Sections =
            {
                ManagerSection(new MenuSectionTemplate
                {
                    Name = "Target",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 0,
                    DisplaySlot = 0,
                    Width = 1,
                    Height = 1,
                    Overflow = MenuOverflowMode.Hide,
                    ContentSourceId = "users.target",
                    ContentSourceParamsJson = TargetParams,
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
                                Bind("Material", "PLAYER_HEAD", VariableRefreshPolicy.Static),
                                Bind("SkullOwner", "$row.getName$", VariableRefreshPolicy.OnDirty),
                                Bind("Name", "&f$row.getName$", VariableRefreshPolicy.OnDirty),
                                Lore(0, "$row.getSummaryLines$", VariableRefreshPolicy.OnDirty),
                            },
                        },
                    },
                }),
                ManagerSection(new MenuSectionTemplate
                {
                    Name = "Header",
                    Kind = MenuSectionKind.StaticButtons,
                    SortOrder = 1,
                    DisplaySlot = 1,
                    Width = 8,
                    Height = 1,
                    Overflow = MenuOverflowMode.Hide,
                    Items = { BackButton(8, 0) },
                }),
                ManagerSection(new MenuSectionTemplate
                {
                    Name = "Controls",
                    Kind = MenuSectionKind.StaticButtons,
                    SortOrder = 2,
                    DisplaySlot = 9,
                    Width = 9,
                    Height = 5,
                    Overflow = MenuOverflowMode.Hide,
                    Items =
                    {
                        StepperMinus(10, "coins", "pm.coinStep", "coins"),
                        StepperValue(11, "GOLD_INGOT", "&6Coins: &f$target.getCoins$", "pm.coinStep", "1,10,100,1000,10000"),
                        StepperPlus(12, "coins", "pm.coinStep", "coins"),
                        StepperMinus(19, "gems", "pm.gemStep", "gems"),
                        StepperValue(20, "EMERALD", "&aGems: &f$target.getGems$", "pm.gemStep", "1,10,100,1000"),
                        StepperPlus(21, "gems", "pm.gemStep", "gems"),
                        StepperMinus(28, "xp", "pm.xpStep", "XP"),
                        StepperValue(29, "EXPERIENCE_BOTTLE", "&bExperience: &f$target.getExperience$", "pm.xpStep", "10,100,1000,10000"),
                        StepperPlus(30, "xp", "pm.xpStep", "XP"),

                        WithActionPermission("knk.admin.user.xp", OpenWithTarget(14, 110, UserManagerTitlesMenuKey,
                            Bind("Material", "IRON_HELMET", VariableRefreshPolicy.Static),
                            Bind("Name", "&bTitle: &f$target.getTitleName$", VariableRefreshPolicy.OnDirty),
                            Lore(0, "&8Click to set a title"))),
                        WithActionPermission("knk.admin.user.group", OpenWithTarget(15, 111, UserManagerGroupsMenuKey,
                            Bind("Material", "BOOK", VariableRefreshPolicy.Static),
                            Bind("Name", "&eGroups", VariableRefreshPolicy.Static),
                            Lore(0, "&7Premium tier: &f$target.getPremiumTierName$", VariableRefreshPolicy.OnDirty),
                            Lore(1, "&8Click to add or remove groups"))),
                        ManagerEdit(16, 112, "knk.admin.user.mode", "users.mode",
                            "{" + CtxUserIdJson + ",\"mode\":\"$target.getNextMode$\"}",
                            Bind("Material", "ENDER_EYE", VariableRefreshPolicy.Static),
                            Bind("Name", "&dMode: &f$target.getMode$", VariableRefreshPolicy.OnDirty),
                            Lore(0, "&8Click to switch to $target.getNextModeName$", VariableRefreshPolicy.OnDirty)),
                        ManagerEdit(23, 113, "knk.admin.user.salary", "users.salary-payout", "{" + CtxUserIdJson + "}",
                            Bind("Material", "CLOCK", VariableRefreshPolicy.Static),
                            Bind("Name", "&6Pay out salary", VariableRefreshPolicy.Static),
                            Lore(0, "&7Pays the salary owed since the last payout")),
                        FreezeToggle(24, 114),
                        ManagerConfirmed(32, 115, "users.kick", "Kick $target.getName$? Click Confirm or Cancel.",
                            Bind("Material", "IRON_HORSE_ARMOR", VariableRefreshPolicy.Static),
                            Bind("Name", "&cKick", VariableRefreshPolicy.Static),
                            Lore(0, "&7Runs /kick as you (vanilla permission)")),
                        ManagerConfirmed(33, 116, "users.ban", "Ban $target.getName$? Click Confirm or Cancel.",
                            Bind("Material", "NETHERITE_AXE", VariableRefreshPolicy.Static),
                            Bind("Name", "&4Ban", VariableRefreshPolicy.Static),
                            Lore(0, "&7Runs /ban as you (vanilla permission)")),
                        ConfirmButton(48, "users.pending", "&7Confirm the action you picked (see chat)"),
                        CancelButton(50, "users.pending"),
                    },
                }),
            },
        };
    }

    private static MenuTemplate UserManagerTitlesTemplate()
    {
        var row = new MenuItemTemplate
        {
            SortOrder = 0,
            Amount = 1,
            IsRowTemplate = true,
            DisplayMode = MenuDisplayMode.Normal,
            ActionPermission = "knk.admin.user.xp",
            VariableBindings =
            {
                Bind("Material", "IRON_HELMET", VariableRefreshPolicy.Static),
                Bind("DisplayMode", "$row.getPickerDisplayMode$", VariableRefreshPolicy.OnDirty),
                Bind("Name", "&f$row.getName$", VariableRefreshPolicy.OnDirty),
                Lore(0, "$row.getLoreLines$", VariableRefreshPolicy.OnDirty),
                Lore(1, "&8Click to set this title", VariableRefreshPolicy.Static),
            },
            Actions =
            {
                ConfirmRequest("users.set-title", "{" + CtxUserIdJson + ",\"bracketId\":\"$row.getBracketId$\"}",
                    "Set $ctx.name$'s title to $row.getName$ (XP becomes $row.getMinExperience$)? Click Confirm or Cancel.", 0),
            },
        };
        row.Conditions.Add(OutranksTarget());
        return new MenuTemplate
        {
            Key = UserManagerTitlesMenuKey,
            Name = "&8Set title",
            Description = "Pick a title bracket for the player in ctx.userId (users.titles); sets their XP to its minimum. Content port CP8.",
            Height = 4,
            MinHeight = 2,
            Growth = MenuGrowthMode.Dynamic,
            Sections =
            {
                ManagerSection(new MenuSectionTemplate
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
                            Bind("Material", "IRON_HELMET", VariableRefreshPolicy.Static),
                            Bind("Name", "&bTitle for &f$target.getName$", VariableRefreshPolicy.OnDirty),
                            Lore(0, "&7Current: &f$target.getTitleName$", VariableRefreshPolicy.OnDirty),
                            Lore(1, "&7Experience: &f$target.getExperience$", VariableRefreshPolicy.OnDirty)),
                        ConfirmButton(2, "users.pending", "&7Set the title you picked (see chat)"),
                        CancelButton(6, "users.pending"),
                        BackButton(8, 1),
                    },
                }),
                ManagerSection(new MenuSectionTemplate
                {
                    Name = "Titles",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 1,
                    DisplaySlot = 9,
                    Width = 9,
                    Height = 2,
                    Overflow = MenuOverflowMode.Scroll,
                    ContentSourceId = "users.titles",
                    ContentSourceParamsJson = TargetParams,
                    Items =
                    {
                        PagerButton(27, "&aPrevious page", "menu.page.prev"),
                        PagerButton(35, "&aNext page", "menu.page.next"),
                        row,
                    },
                }),
            },
        };
    }

    private static MenuTemplate UserManagerGroupsTemplate()
    {
        var add = new ActionBinding
        {
            ActionTypeId = "users.group",
            ParamsJson = "{" + CtxUserIdJson + ",\"groupId\":\"$row.getGroupId$\",\"op\":\"add\"}",
            SortOrder = 0,
        };
        var remove = ConfirmRequest("users.group", "{" + CtxUserIdJson + ",\"groupId\":\"$row.getGroupId$\",\"op\":\"remove\"}",
            "Remove $ctx.name$ from $row.getName$? Click Confirm or Cancel.", 1);
        var row = new MenuItemTemplate
        {
            SortOrder = 0,
            Amount = 1,
            IsRowTemplate = true,
            DisplayMode = MenuDisplayMode.Normal,
            ActionPermission = "knk.admin.user.group",
            VariableBindings =
            {
                Bind("Material", "$row.getMaterial$", VariableRefreshPolicy.OnDirty),
                Bind("DisplayMode", "$row.getDisplayMode$", VariableRefreshPolicy.OnDirty),
                Bind("Name", "&f$row.getName$", VariableRefreshPolicy.OnDirty),
                Lore(0, "$row.getLoreLines$", VariableRefreshPolicy.OnDirty),
            },
        };
        row.Actions.Add(add);
        row.Actions.Add(remove);
        AddActionCondition(row, add,
            RenderCondition("value-equals", "{\"value\":\"$row.getIsMember$\",\"expected\":\"false\"}"));
        AddActionCondition(row, remove,
            RenderCondition("value-equals", "{\"value\":\"$row.getIsMember$\",\"expected\":\"true\"}"));
        row.Conditions.Add(OutranksTarget());
        return new MenuTemplate
        {
            Key = UserManagerGroupsMenuKey,
            Name = "&8Groups",
            Description = "Every permission group; click to add the player in ctx.userId or (confirmed) remove them (users.groups). A premium tier click is a confirmed switch - the plugin replaces their other premium tiers (one tier per player here). Content port CP8.",
            Height = 6,
            MinHeight = 3,
            Growth = MenuGrowthMode.Dynamic,
            Sections =
            {
                ManagerSection(new MenuSectionTemplate
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
                            Bind("Material", "BOOK", VariableRefreshPolicy.Static),
                            Bind("Name", "&eGroups of &f$target.getName$", VariableRefreshPolicy.OnDirty),
                            Lore(0, "&7Highlighted: current memberships"),
                            Lore(1, "&7Removing a group or switching premium tier"),
                            Lore(2, "&7asks for confirmation")),
                        ConfirmButton(2, "users.pending", "&7Confirm the removal you picked (see chat)"),
                        CancelButton(6, "users.pending"),
                        BackButton(8, 1),
                    },
                }),
                ManagerSection(new MenuSectionTemplate
                {
                    Name = "Groups",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 1,
                    DisplaySlot = 9,
                    Width = 9,
                    Height = 4,
                    Overflow = MenuOverflowMode.Scroll,
                    ContentSourceId = "users.groups",
                    ContentSourceParamsJson = TargetParams,
                    Items =
                    {
                        PagerButton(45, "&aPrevious page", "menu.page.prev"),
                        PagerButton(53, "&aNext page", "menu.page.next"),
                        row,
                    },
                }),
            },
        };
    }

    private static MenuSectionTemplate ManagerSection(MenuSectionTemplate section)
    {
        section.VisibilityPermission = UserManagePermission;
        return section;
    }

    private static readonly System.Text.Json.JsonSerializerOptions RelaxedJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static ConditionBinding OutranksTarget() => new()
    {
        ConditionTypeId = "users.outranks-target",
        ParamsJson = "{" + CtxUserIdJson + "}",
        SortOrder = 0,
        Phase = MenuConditionPhase.Click,
    };

    private static ActionBinding ConfirmRequest(string actionTypeId, string actionParamsJson, string prompt, int sortOrder) => new()
    {
        ActionTypeId = "menu.confirm.request",
        ParamsJson = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["actionTypeId"] = actionTypeId,
            ["actionParamsJson"] = actionParamsJson,
            ["prompt"] = prompt,
        }, RelaxedJson),
        SortOrder = sortOrder,
    };

    /// <summary>A pinned Player-manager edit: one action, its ActionPermission, the outranks Click condition.</summary>
    private static MenuItemTemplate ManagerEdit(int slot, int sortOrder, string? permission, string actionTypeId,
        string paramsJson, params VariableBinding[] bindings)
    {
        var item = DemoItem(slot, sortOrder, bindings);
        item.ActionPermission = permission;
        item.Actions.Add(new ActionBinding { ActionTypeId = actionTypeId, ParamsJson = paramsJson, SortOrder = 0 });
        item.Conditions.Add(OutranksTarget());
        return item;
    }

    /// <summary>A pinned edit that asks for confirmation first (kick, ban).</summary>
    private static MenuItemTemplate ManagerConfirmed(int slot, int sortOrder, string actionTypeId, string prompt,
        params VariableBinding[] bindings)
    {
        var item = DemoItem(slot, sortOrder, bindings);
        item.Actions.Add(ConfirmRequest(actionTypeId, "{" + CtxUserIdJson + "}", prompt, 0));
        item.Conditions.Add(OutranksTarget());
        return item;
    }

    private static MenuItemTemplate StepperMinus(int slot, string field, string stateKey, string unit) =>
        ManagerEdit(slot, 100 + slot, "knk.admin.user." + field, "users.adjust",
            "{" + CtxUserIdJson + ",\"field\":\"" + field + "\",\"delta\":\"-$state." + stateKey + "$\"}",
            Bind("Material", "RED_STAINED_GLASS_PANE", VariableRefreshPolicy.Static),
            Bind("Name", "&c-$state." + stateKey + "$ " + unit, VariableRefreshPolicy.OnDirty));

    private static MenuItemTemplate StepperPlus(int slot, string field, string stateKey, string unit) =>
        ManagerEdit(slot, 100 + slot, "knk.admin.user." + field, "users.adjust",
            "{" + CtxUserIdJson + ",\"field\":\"" + field + "\",\"delta\":\"$state." + stateKey + "$\"}",
            Bind("Material", "LIME_STAINED_GLASS_PANE", VariableRefreshPolicy.Static),
            Bind("Name", "&a+$state." + stateKey + "$ " + unit, VariableRefreshPolicy.OnDirty));

    /// <summary>The value item of a stepper: shows the amount and step; clicking cycles the step (G1 menu.state.cycle).</summary>
    private static MenuItemTemplate StepperValue(int slot, string material, string name, string stateKey, string steps)
    {
        var item = DemoItem(slot, 100 + slot,
            Bind("Material", material, VariableRefreshPolicy.Static),
            Bind("Name", name, VariableRefreshPolicy.OnDirty),
            Lore(0, "&7Step: &f$state." + stateKey + "$", VariableRefreshPolicy.OnDirty),
            Lore(1, "&8Click to change the step (" + steps.Replace(",", "/") + ")"));
        item.Actions.Add(new ActionBinding
        {
            ActionTypeId = "menu.state.cycle",
            ParamsJson = "{\"key\":\"" + stateKey + "\",\"values\":\"" + steps + "\"}",
            SortOrder = 0,
        });
        return item;
    }

    /// <summary>Freeze/unfreeze in one slot: action-level Render conditions on $target.getIsFrozen$, Click-phase node checks.</summary>
    private static MenuItemTemplate FreezeToggle(int slot, int sortOrder)
    {
        var item = DemoItem(slot, sortOrder,
            Bind("Material", "$target.getFreezeMaterial$", VariableRefreshPolicy.OnDirty),
            Bind("Name", "$target.getFreezeName$", VariableRefreshPolicy.OnDirty),
            Lore(0, "&7Frozen players can't move, chat or use commands"));
        var freeze = new ActionBinding
        {
            ActionTypeId = "users.freeze",
            ParamsJson = "{" + CtxUserIdJson + ",\"op\":\"freeze\"}",
            SortOrder = 0,
        };
        var unfreeze = new ActionBinding
        {
            ActionTypeId = "users.freeze",
            ParamsJson = "{" + CtxUserIdJson + ",\"op\":\"unfreeze\"}",
            SortOrder = 1,
        };
        item.Actions.Add(freeze);
        item.Actions.Add(unfreeze);
        AddActionCondition(item, freeze,
            RenderCondition("value-equals", "{\"value\":\"$target.getIsFrozen$\",\"expected\":\"false\"}"));
        AddActionCondition(item, freeze, new ConditionBinding
        {
            ConditionTypeId = "permission-node", ParamsJson = "{\"node\":\"knk.freeze\"}", SortOrder = 1,
            Phase = MenuConditionPhase.Click,
        });
        AddActionCondition(item, unfreeze,
            RenderCondition("value-equals", "{\"value\":\"$target.getIsFrozen$\",\"expected\":\"true\"}"));
        AddActionCondition(item, unfreeze, new ConditionBinding
        {
            ConditionTypeId = "permission-node", ParamsJson = "{\"node\":\"knk.unfreeze\"}", SortOrder = 1,
            Phase = MenuConditionPhase.Click,
        });
        item.Conditions.Add(OutranksTarget());
        return item;
    }

    /// <summary>Opens a Player-manager sub-menu for the same target (ctx.userId + ctx.name carried along).</summary>
    private static MenuItemTemplate OpenWithTarget(int slot, int sortOrder, string key, params VariableBinding[] bindings)
    {
        var item = DemoItem(slot, sortOrder, bindings);
        item.Actions.Add(new ActionBinding
        {
            ActionTypeId = "menu.open",
            ParamsJson = "{\"key\":\"" + key + "\",\"ctx.userId\":\"$ctx.userId$\",\"ctx.name\":\"$ctx.name$\"}",
            SortOrder = 0,
        });
        return item;
    }

    private static MenuItemTemplate WithActionPermission(string node, MenuItemTemplate item)
    {
        item.ActionPermission = node;
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
