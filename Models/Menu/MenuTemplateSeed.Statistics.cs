using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// Player statistics privacy (KNG-34, knk-workspace docs/specs/player-statistics/IMPLEMENTATION_PLAN.md
/// §5.3): <c>statistics.visibility</c>, opened by <c>/stats settings</c> and the profile's
/// "Statistics privacy" tile (<c>profile.main</c> header slot 6). Create-only like every other
/// MenuTemplateSeed entry - an existing database gets it after the content-menu reset. The plugin side
/// is knk-paper's <c>StatisticsVisibilityMenuFeature</c>: root <c>statsvis</c> (selected group, counts,
/// the pending group action's preview), row source <c>statistics.visibility.rows</c> (the selected
/// group's settings from <c>GET api/statistics/users/{id}/visibility</c>, each followed by its context
/// rows), actions <c>statistics.visibility.select-group</c> / <c>.cycle</c> / <c>.group</c> /
/// <c>.apply-group</c> and condition <c>statistics.visibility.pending</c>.
/// <para>
/// Link 5 read surfaces: <c>statistics.main</c> (the viewer's or <c>ctx.target</c>'s statistics as the
/// API shows them to the viewer; knk-paper <c>StatisticsMenuFeature</c>: root <c>stats</c>, row source
/// <c>statistics.main.title-history</c>, action <c>statistics.main.period</c>), opened by the profile's
/// "Statistics" tile (<c>profile.main</c> header slot 5) and from leaderboard entries;
/// <c>statistics.leaderboards</c> / <c>statistics.leaderboard</c> (knk-paper
/// <c>LeaderboardsMenuFeature</c>: row sources <c>statistics.leaderboards.boards</c> and
/// <c>statistics.leaderboard.entries</c>, root <c>lb</c>, action <c>statistics.leaderboard.period</c>),
/// opened by <c>/leaderboard</c> and from <c>statistics.main</c>.
/// </para>
/// </summary>
public static partial class MenuTemplateSeed
{
    public const string StatisticsVisibilityMenuKey = "statistics.visibility";
    public const string StatisticsMainMenuKey = "statistics.main";
    public const string LeaderboardsMenuKey = "statistics.leaderboards";
    public const string LeaderboardMenuKey = "statistics.leaderboard";

    private const string StatisticsPendingCondition = "statistics.visibility.pending";

    private static IEnumerable<MenuTemplate> StatisticsTemplates()
    {
        yield return StatisticsVisibilityTemplate();
        yield return StatisticsMainTemplate();
        yield return LeaderboardsTemplate();
        yield return LeaderboardTemplate();
    }

    /// <summary>
    /// <c>statistics.main</c>: header = the player's head with the always-public profile, one item per
    /// catalogue group listing the visible metrics, the period cycle (lifetime → day → week → month),
    /// the leaderboards and Back. Row 1 explains the title history below; rows 2-4 list it (newest
    /// first). Target = <c>ctx.target</c> (user id; default the viewer), shown name <c>ctx.name</c>.
    /// </summary>
    private static MenuTemplate StatisticsMainTemplate()
    {
        var period = DemoItem(6, 6,
            Bind("Material", "SUNFLOWER", VariableRefreshPolicy.Static),
            Bind("Name", "&ePeriod", VariableRefreshPolicy.Static),
            Lore(0, "$stats.getPeriodLine$", VariableRefreshPolicy.OnDirty),
            Lore(1, "$stats.getNextPeriodLine$", VariableRefreshPolicy.OnDirty));
        period.Actions.Add(new ActionBinding { ActionTypeId = "statistics.main.period", ParamsJson = "{}", SortOrder = 0 });

        var row = new MenuItemTemplate
        {
            SortOrder = 0,
            Amount = 1,
            IsRowTemplate = true,
            DisplayMode = MenuDisplayMode.Normal,
            VariableBindings =
            {
                Bind("Material", "$row.getMaterial$", VariableRefreshPolicy.OnDirty),
                Bind("Name", "$row.getName$", VariableRefreshPolicy.OnDirty),
                Lore(0, "$row.getLoreLines$", VariableRefreshPolicy.OnDirty),
            },
        };

        return new MenuTemplate
        {
            Key = StatisticsMainMenuKey,
            Name = "&8Statistics",
            Description = "A player's statistics (ctx.target, default the viewer) as the API shows them to the viewer, per group and period, plus the title history (statistics.main.title-history). Player statistics KNG-34.",
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
                            Bind("SkullOwner", "$stats.getTargetName$", VariableRefreshPolicy.OnDirty),
                            Bind("Name", "$stats.getTitle$", VariableRefreshPolicy.OnDirty),
                            Lore(0, "$stats.getProfileLines$", VariableRefreshPolicy.OnDirty)),
                        StatisticsGroupItem(1, 1, "CLOCK", "&fActivity", "$stats.getActivityLines$"),
                        StatisticsGroupItem(2, 2, "IRON_SWORD", "&fCombat", "$stats.getCombatLines$"),
                        StatisticsGroupItem(3, 3, "WHITE_BANNER", "&fMinigames", "$stats.getMinigamesLines$"),
                        StatisticsGroupItem(4, 4, "COMPASS", "&fExploration", "$stats.getExplorationLines$"),
                        StatisticsGroupItem(5, 5, "EXPERIENCE_BOTTLE", "&fProgression", "$stats.getProgressionLines$"),
                        period,
                        OpenTile(7, 7, LeaderboardsMenuKey,
                            Bind("Material", "GOLDEN_HELMET", VariableRefreshPolicy.Static),
                            Bind("Name", "&6Leaderboards", VariableRefreshPolicy.Static),
                            Lore(0, "&7Weekly, monthly and all-time rankings"),
                            Lore(1, "&7(also &f/leaderboard&7)")),
                        BackButton(8, 8),
                    },
                },
                new MenuSectionTemplate
                {
                    Name = "Info",
                    Kind = MenuSectionKind.StaticButtons,
                    SortOrder = 1,
                    DisplaySlot = 9,
                    Width = 9,
                    Height = 1,
                    Overflow = MenuOverflowMode.Hide,
                    Items =
                    {
                        DemoItem(13, 9,
                            Bind("Material", "BOOK", VariableRefreshPolicy.Static),
                            Bind("Name", "&eTitle history", VariableRefreshPolicy.Static),
                            Lore(0, "$stats.getTitleHistoryLine$", VariableRefreshPolicy.OnDirty),
                            Lore(1, ""),
                            Lore(2, "&7Players choose who may see their"),
                            Lore(3, "&7statistics: &f/stats settings")),
                    },
                },
                new MenuSectionTemplate
                {
                    Name = "TitleHistory",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 2,
                    DisplaySlot = 18,
                    Width = 9,
                    Height = 3,
                    MinHeight = 1,
                    Overflow = MenuOverflowMode.Scroll,
                    ListMode = MenuListMode.Grid,
                    ContentSourceId = "statistics.main.title-history",
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

    /// <summary>
    /// <c>statistics.leaderboards</c>: every board (<c>GET api/leaderboards</c>); a click opens
    /// <c>statistics.leaderboard</c> with <c>ctx.board</c>.
    /// </summary>
    private static MenuTemplate LeaderboardsTemplate()
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
                Bind("Name", "$row.getName$", VariableRefreshPolicy.OnDirty),
                Lore(0, "$row.getLoreLines$", VariableRefreshPolicy.OnDirty),
            },
            Actions =
            {
                new ActionBinding
                {
                    ActionTypeId = "menu.open",
                    ParamsJson = "{\"key\":\"" + LeaderboardMenuKey + "\",\"ctx.board\":\"$row.getBoardKey$\"}",
                    SortOrder = 0,
                },
            },
        };

        return new MenuTemplate
        {
            Key = LeaderboardsMenuKey,
            Name = "&8Leaderboards",
            Description = "Every leaderboard (statistics.leaderboards.boards); a click opens it. Player statistics KNG-34.",
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
                            Bind("Material", "GOLDEN_HELMET", VariableRefreshPolicy.Static),
                            Bind("Name", "&6Leaderboards", VariableRefreshPolicy.Static),
                            Lore(0, "&7Weekly, monthly and all-time rankings,"),
                            Lore(1, "&7updated every few minutes."),
                            Lore(2, ""),
                            Lore(3, "&7You appear on a board when you show"),
                            Lore(4, "&7that statistic to everyone"),
                            Lore(5, "&7(&f/stats settings&7). Playtime and XP"),
                            Lore(6, "&7always rank.")),
                        BackButton(8, 1),
                    },
                },
                new MenuSectionTemplate
                {
                    Name = "Boards",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 1,
                    DisplaySlot = 9,
                    Width = 9,
                    Height = 4,
                    MinHeight = 1,
                    Overflow = MenuOverflowMode.Scroll,
                    ListMode = MenuListMode.Grid,
                    ContentSourceId = "statistics.leaderboards.boards",
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

    /// <summary>
    /// <c>statistics.leaderboard</c>: one board (<c>ctx.board</c>) - the top 10 as player heads (the
    /// viewer HIGHLIGHTed; a click opens that player's <c>statistics.main</c>), the viewer's own rank,
    /// the period cycle (weekly → monthly → lifetime) and Back.
    /// </summary>
    private static MenuTemplate LeaderboardTemplate()
    {
        var period = DemoItem(4, 1,
            Bind("Material", "CLOCK", VariableRefreshPolicy.Static),
            Bind("Name", "&e$lb.getPeriodName$", VariableRefreshPolicy.OnDirty),
            Lore(0, "$lb.getNextPeriodLine$", VariableRefreshPolicy.OnDirty));
        period.Actions.Add(new ActionBinding { ActionTypeId = "statistics.leaderboard.period", ParamsJson = "{}", SortOrder = 0 });

        var row = new MenuItemTemplate
        {
            SortOrder = 0,
            Amount = 1,
            IsRowTemplate = true,
            DisplayMode = MenuDisplayMode.Normal,
            VariableBindings =
            {
                Bind("Material", "PLAYER_HEAD", VariableRefreshPolicy.Static),
                Bind("SkullOwner", "$row.getUsername$", VariableRefreshPolicy.OnDirty),
                Bind("DisplayMode", "$row.getDisplayMode$", VariableRefreshPolicy.OnDirty),
                Bind("Name", "$row.getName$", VariableRefreshPolicy.OnDirty),
                Lore(0, "$row.getLoreLines$", VariableRefreshPolicy.OnDirty),
            },
            Actions =
            {
                new ActionBinding
                {
                    ActionTypeId = "menu.open",
                    ParamsJson = "{\"key\":\"" + StatisticsMainMenuKey + "\",\"ctx.target\":\"$row.getUserId$\",\"ctx.name\":\"$row.getUsername$\"}",
                    SortOrder = 0,
                },
            },
        };

        return new MenuTemplate
        {
            Key = LeaderboardMenuKey,
            Name = "&8Leaderboard",
            Description = "One leaderboard (ctx.board): top 10 and the viewer's rank per period (statistics.leaderboard.entries). Player statistics KNG-34.",
            Height = 4,
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
                            Bind("Material", "GOLDEN_HELMET", VariableRefreshPolicy.Static),
                            Bind("Name", "$lb.getTitle$", VariableRefreshPolicy.OnDirty),
                            Lore(0, "&7$lb.getPeriodName$", VariableRefreshPolicy.OnDirty),
                            Lore(1, "&7Top 10, updated every few minutes")),
                        period,
                        DemoItem(6, 2,
                            Bind("Material", "PLAYER_HEAD", VariableRefreshPolicy.Static),
                            Bind("SkullOwner", "$player.getName$", VariableRefreshPolicy.OnDirty),
                            Bind("Name", "&fYour position", VariableRefreshPolicy.Static),
                            Lore(0, "$lb.getViewerLines$", VariableRefreshPolicy.OnDirty)),
                        BackButton(8, 3),
                    },
                },
                new MenuSectionTemplate
                {
                    Name = "Ranking",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 1,
                    DisplaySlot = 9,
                    Width = 9,
                    Height = 2,
                    MinHeight = 1,
                    Overflow = MenuOverflowMode.Hide,
                    ListMode = MenuListMode.Grid,
                    ContentSourceId = "statistics.leaderboard.entries",
                    ContentSourceParamsJson = "{}",
                    Items =
                    {
                        row,
                    },
                },
            },
        };
    }

    private static MenuItemTemplate StatisticsGroupItem(int slot, int sortOrder, string material, string name, string linesExpression) =>
        DemoItem(slot, sortOrder,
            Bind("Material", material, VariableRefreshPolicy.Static),
            Bind("Name", name, VariableRefreshPolicy.Static),
            Lore(0, linesExpression, VariableRefreshPolicy.OnDirty));

    /// <summary>
    /// Header: the five group selectors (the selected one HIGHLIGHTed), the three group actions
    /// ("Set all listed to Nobody/Friends/Everyone" - previewed, then confirmed) and Back. Row 1: what
    /// the selected group shows now. Rows 2-4: one row per setting of the group plus one per context
    /// with an override or with data; a click cycles Nobody → Friends → Everyone. Row 5: pager and the
    /// Confirm (with the preview) / Cancel pair shown while a group action waits.
    /// </summary>
    private static MenuTemplate StatisticsVisibilityTemplate()
    {
        var confirm = DemoItem(48, 148,
            Bind("Material", "LIME_CONCRETE", VariableRefreshPolicy.Static),
            Bind("Name", "&aConfirm", VariableRefreshPolicy.Static),
            Lore(0, "$statsvis.getPreviewLines$", VariableRefreshPolicy.OnDirty));
        confirm.Actions.Add(new ActionBinding { ActionTypeId = "menu.confirm.accept", ParamsJson = "{}", SortOrder = 0 });
        confirm.Conditions.Add(RenderCondition(StatisticsPendingCondition, "{}"));

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
            ActionTypeId = "statistics.visibility.cycle",
            ParamsJson = "{\"settingKey\":\"$row.getSettingKey$\",\"context\":\"$row.getContext$\",\"expected\":\"$row.getVisibility$\"}",
            SortOrder = 0,
        });

        return new MenuTemplate
        {
            Key = StatisticsVisibilityMenuKey,
            Name = "&8Statistics privacy",
            Description = "Who may see each of the viewer's statistics, per group, with a confirmed group action (statistics.visibility.rows). Player statistics KNG-34.",
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
                        StatisticsGroupSelector(0, 0, "activity", "&fActivity", "CLOCK", "$statsvis.getActivityMode$", "&7Logins"),
                        StatisticsGroupSelector(1, 1, "combat", "&fCombat", "IRON_SWORD", "$statsvis.getCombatMode$",
                            "&7Kills, deaths, damage, arrows, headshots, killstreak"),
                        StatisticsGroupSelector(2, 2, "minigames", "&fMinigames", "WHITE_BANNER", "$statsvis.getMinigamesMode$",
                            "&7Wins, losses, draws, objectives, gate damage"),
                        StatisticsGroupSelector(3, 3, "exploration", "&fExploration", "COMPASS", "$statsvis.getExplorationMode$",
                            "&7Distance, highest fall, discoveries"),
                        StatisticsGroupSelector(4, 4, "progression", "&fProgression", "EXPERIENCE_BOTTLE", "$statsvis.getProgressionMode$",
                            "&7Title history, coins and gems"),
                        StatisticsGroupAction(5, 5, "Nobody", "&cSet all listed to Nobody", "RED_DYE"),
                        StatisticsGroupAction(6, 6, "Friends", "&bSet all listed to Friends", "LIGHT_BLUE_DYE",
                            "&8Friends-only shows nothing until the friends system exists"),
                        StatisticsGroupAction(7, 7, "Everyone", "&aSet all listed to Everyone", "LIME_DYE"),
                        BackButton(8, 8),
                    },
                },
                new MenuSectionTemplate
                {
                    Name = "Info",
                    Kind = MenuSectionKind.StaticButtons,
                    SortOrder = 1,
                    DisplaySlot = 9,
                    Width = 9,
                    Height = 1,
                    Overflow = MenuOverflowMode.Hide,
                    Items =
                    {
                        DemoItem(13, 9,
                            Bind("Material", "SPYGLASS", VariableRefreshPolicy.Static),
                            Bind("Name", "&eWho may see your statistics", VariableRefreshPolicy.Static),
                            Lore(0, "$statsvis.getGroupSummaryLines$", VariableRefreshPolicy.OnDirty),
                            Lore(1, ""),
                            Lore(2, "&7Click a setting to change it."),
                            Lore(3, "&7Active/AFK time, XP and your title"),
                            Lore(4, "&7are always public.")),
                    },
                },
                new MenuSectionTemplate
                {
                    Name = "Settings",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 2,
                    DisplaySlot = 18,
                    Width = 9,
                    Height = 3,
                    MinHeight = 1,
                    Overflow = MenuOverflowMode.Scroll,
                    ListMode = MenuListMode.Grid,
                    ContentSourceId = "statistics.visibility.rows",
                    ContentSourceParamsJson = "{}",
                    Items =
                    {
                        row,
                        PagerButton(45, "&aPrevious page", "menu.page.prev"),
                        confirm,
                        CancelButton(50, StatisticsPendingCondition),
                        PagerButton(53, "&aNext page", "menu.page.next"),
                    },
                },
            },
        };
    }

    private static MenuItemTemplate StatisticsGroupSelector(int slot, int sortOrder, string group, string name, string material,
        string displayModeExpression, string description)
    {
        var item = DemoItem(slot, sortOrder,
            Bind("Material", material, VariableRefreshPolicy.Static),
            Bind("DisplayMode", displayModeExpression, VariableRefreshPolicy.OnDirty),
            Bind("Name", name, VariableRefreshPolicy.Static),
            Lore(0, description),
            Lore(1, "&eClick: &7show these settings"));
        item.Actions.Add(new ActionBinding
        {
            ActionTypeId = "statistics.visibility.select-group",
            ParamsJson = "{\"group\":\"" + group + "\"}",
            SortOrder = 0,
        });
        return item;
    }

    private static MenuItemTemplate StatisticsGroupAction(int slot, int sortOrder, string value, string name, string material,
        string? note = null)
    {
        var item = DemoItem(slot, sortOrder,
            Bind("Material", material, VariableRefreshPolicy.Static),
            Bind("Name", name, VariableRefreshPolicy.Static),
            Lore(0, "&7Every setting of the shown group"),
            Lore(1, "&7(context overrides stay as they are)"),
            Lore(2, "&eClick: &7preview, then Confirm"));
        if (note != null)
        {
            item.VariableBindings.Add(Lore(3, note));
        }
        item.Actions.Add(new ActionBinding
        {
            ActionTypeId = "statistics.visibility.group",
            ParamsJson = "{\"value\":\"" + value + "\"}",
            SortOrder = 0,
        });
        return item;
    }
}
