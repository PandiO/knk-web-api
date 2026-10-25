using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// Arena duels Phase 6 (docs/specs/arena-duels/DESIGN.md §9.2-§9.3, KNG-220): the duel menus -
/// <c>duel_matches</c> (<c>/duels</c>: live matches of every arena, two heads per match),
/// <c>duel_match_detail</c> (one match, <c>ctx.token</c>: both duelists' cards with their statistics as
/// the API shows them to the viewer, their kit, coins and real stake items, and the spectate
/// teleport), <c>arena_main</c> (<c>/arena</c>, <c>ctx.arena</c> = the arena id or empty for the arena the
/// viewer stands in: info, rules, queue, live matches, [Spectate], [Challenge a player], [My claims])
/// and its <c>duel_challenge</c> picker of online players. Create-only like every MenuTemplateSeed
/// entry. The plugin side is knk-paper's <c>DuelMenuFeature</c> (roots <c>duelMatch</c>, <c>arena</c>,
/// <c>duels</c>; row sources <c>duel.matches</c>, <c>duel.match-side</c>, <c>duel.stakes</c>,
/// <c>duel.challenge-candidates</c>; actions <c>duel.spectate</c> - arena duels rev. 6: an ARENA engine
/// teleport to the spectator area, after its minimum title and gem price - <c>duel.challenge</c> and
/// <c>duel.claim</c>; conditions <c>duel.matches-empty</c>, <c>duel.in-arena</c>), checked against the
/// exported content-seeds.json.
/// </summary>
public static partial class MenuTemplateSeed
{
    public const string DuelMatchesMenuKey = "duel_matches";
    public const string DuelMatchDetailMenuKey = "duel_match_detail";
    public const string ArenaMainMenuKey = "arena_main";
    public const string DuelChallengeMenuKey = "duel_challenge";

    private static IEnumerable<MenuTemplate> DuelTemplates()
    {
        yield return DuelMatchesTemplate();
        yield return DuelMatchDetailTemplate();
        yield return ArenaMainTemplate();
        yield return DuelChallengeTemplate();
    }

    /// <summary>One head of a live match (two per match, eight wide so a pair stays on one line); opens the detail.</summary>
    private static MenuItemTemplate DuelMatchRow() => new()
    {
        SortOrder = 0,
        Amount = 1,
        IsRowTemplate = true,
        DisplayMode = MenuDisplayMode.Normal,
        VariableBindings =
        {
            Bind("Material", "$row.getMaterial$", VariableRefreshPolicy.OnDirty),
            Bind("SkullOwner", "$row.getSkullOwner$", VariableRefreshPolicy.OnDirty),
            Bind("Name", "$row.getName$", VariableRefreshPolicy.OnDirty),
            Bind("Lore", "$row.getLines$", VariableRefreshPolicy.Ttl, 0, 20),
        },
        Actions =
        {
            new ActionBinding
            {
                ActionTypeId = "menu.open",
                ParamsJson = "{\"key\":\"" + DuelMatchDetailMenuKey + "\",\"ctx.token\":\"$row.getToken$\"}",
                SortOrder = 0,
            },
        },
    };

    private static MenuItemTemplate DuelPane(int slot, int sortOrder) => DemoItem(slot, sortOrder,
        Bind("Material", "BLACK_STAINED_GLASS_PANE", VariableRefreshPolicy.Static),
        Bind("Name", " ", VariableRefreshPolicy.Static));

    private static MenuItemTemplate CloseButton(int slot, int sortOrder)
    {
        var close = DemoItem(slot, sortOrder,
            Bind("Material", "BARRIER", VariableRefreshPolicy.Static),
            Bind("Name", "&cClose", VariableRefreshPolicy.Static));
        close.Actions.Add(new ActionBinding { ActionTypeId = "menu.close", ParamsJson = "{}", SortOrder = 0 });
        return close;
    }

    /// <summary>§9.2 List: header (live count), one row per match head, an empty notice, pager.</summary>
    private static MenuTemplate DuelMatchesTemplate()
    {
        var empty = WithConditions(DemoItem(22, 50,
                Bind("Material", "BARRIER", VariableRefreshPolicy.Static),
                Bind("Name", "&cNo duels right now", VariableRefreshPolicy.Static),
                Lore(0, "&7Nobody is fighting at the moment."),
                Lore(1, "&7Challenge someone in an arena: &f/duel <player>")),
            RenderCondition("duel.matches-empty", "{}"));

        return new MenuTemplate
        {
            Key = DuelMatchesMenuKey,
            Name = "&8Live duels",
            Description = "Live duels of every arena (duel.matches), two heads per match. Arena duels P6, DESIGN §9.2.",
            Height = 6,
            Growth = MenuGrowthMode.Dynamic,
            MinHeight = 3,
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
                            Bind("Material", "IRON_SWORD", VariableRefreshPolicy.Static),
                            Bind("Amount", "$duels.getLiveCountOrOne$", VariableRefreshPolicy.Ttl, 0, 20),
                            Bind("Name", "&6Live duels", VariableRefreshPolicy.Static),
                            Bind("Lore", "$duels.getLines$", VariableRefreshPolicy.Ttl, 0, 20)),
                        BackButton(8, 1),
                    },
                },
                new MenuSectionTemplate
                {
                    Name = "Duels",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 1,
                    DisplaySlot = 9,
                    Width = 8,
                    Height = 4,
                    Overflow = MenuOverflowMode.Scroll,
                    ContentSourceId = "duel.matches",
                    ContentSourceParamsJson = "{}",
                    Items =
                    {
                        PagerButton(45, "&aPrevious page", "menu.page.prev"),
                        PagerButton(53, "&aNext page", "menu.page.next"),
                        empty,
                        DuelMatchRow(),
                    },
                },
            },
        };
    }

    /// <summary>
    /// §9.2 Detail (<c>ctx.token</c>): challenger card (slot 0), VS / stage / timer / gear tile (4), opponent card (8);
    /// under each card their kit, coins and stake items; Go to the spectator area, Back, Close.
    /// </summary>
    private static MenuTemplate DuelMatchDetailTemplate()
    {
        const string tokenParam = "\"token\":\"$ctx.token$\"";

        MenuSectionTemplate Card(string name, int slot, string side, int sortOrder) => new()
        {
            Name = name,
            Kind = MenuSectionKind.ContentGrid,
            SortOrder = sortOrder,
            DisplaySlot = slot,
            Width = 1,
            Height = 1,
            Overflow = MenuOverflowMode.Hide,
            ContentSourceId = "duel.match-side",
            ContentSourceParamsJson = "{" + tokenParam + ",\"side\":\"" + side + "\"}",
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
                        Bind("SkullOwner", "$row.getSkullOwner$", VariableRefreshPolicy.OnDirty),
                        Bind("DisplayMode", "$row.getDisplayMode$", VariableRefreshPolicy.Ttl, 0, 20),
                        Bind("Name", "$row.getTitle$", VariableRefreshPolicy.Ttl, 0, 20),
                        Bind("Lore", "$row.getLines$", VariableRefreshPolicy.Ttl, 0, 20),
                    },
                },
            },
        };

        MenuSectionTemplate Stakes(string name, int slot, string side, int sortOrder) => new()
        {
            Name = name,
            Kind = MenuSectionKind.ContentGrid,
            SortOrder = sortOrder,
            DisplaySlot = slot,
            Width = 4,
            Height = 4,
            Overflow = MenuOverflowMode.Hide,
            ContentSourceId = "duel.stakes",
            ContentSourceParamsJson = "{" + tokenParam + ",\"side\":\"" + side + "\"}",
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
                        Bind("Amount", "$row.getAmount$", VariableRefreshPolicy.OnDirty),
                        Bind("Name", "$row.getName$", VariableRefreshPolicy.OnDirty),
                        Bind("Lore", "$row.getLines$", VariableRefreshPolicy.OnDirty),
                    },
                },
            },
        };

        var vs = DemoItem(4, 0,
            Bind("Material", "$duelMatch.getVsMaterial$", VariableRefreshPolicy.Ttl, 0, 20),
            Bind("Name", "$duelMatch.getVsTitle$", VariableRefreshPolicy.Ttl, 0, 20),
            Bind("Lore", "$duelMatch.getVsLines$", VariableRefreshPolicy.Ttl, 0, 20));

        var divider = new MenuSectionTemplate
        {
            Name = "Divider",
            Kind = MenuSectionKind.StaticButtons,
            SortOrder = 4,
            DisplaySlot = 13,
            Width = 1,
            Height = 4,
            Overflow = MenuOverflowMode.Hide,
        };
        foreach (var slot in new[] { 13, 22, 31, 40 })
            divider.Items.Add(DuelPane(slot, slot));

        var spectate = WithConditions(DemoItem(45, 0,
                Bind("Material", "ENDER_EYE", VariableRefreshPolicy.Static),
                Bind("Name", "&aGo to the spectator area", VariableRefreshPolicy.Static),
                Lore(0, "&7Teleports you to this arena's"),
                Lore(1, "&7spectator area: its minimum"),
                Lore(2, "&7title and gem price apply"),
                Lore(3, "&7(or walk in for free).")),
            RenderCondition("value-equals", "{\"value\":\"$duelMatch.getKnown$\",\"expected\":\"true\"}"));
        spectate.Actions.Add(new ActionBinding { ActionTypeId = "duel.spectate", ParamsJson = "{" + tokenParam + "}", SortOrder = 0 });

        return new MenuTemplate
        {
            Key = DuelMatchDetailMenuKey,
            Name = "&8Duel",
            Description = "One live duel (ctx.token): both duelists, their stakes and statistics. Arena duels P6, DESIGN §9.2.",
            Height = 6,
            Growth = MenuGrowthMode.Static,
            AutoRefreshTicks = 20,
            Sections =
            {
                Card("Challenger", 0, "CHALLENGER", 0),
                new MenuSectionTemplate
                {
                    Name = "Versus",
                    Kind = MenuSectionKind.StaticButtons,
                    SortOrder = 1,
                    DisplaySlot = 1,
                    Width = 7,
                    Height = 1,
                    Overflow = MenuOverflowMode.Hide,
                    Items = { vs },
                },
                Card("Opponent", 8, "OPPONENT", 2),
                Stakes("Challenger stakes", 9, "CHALLENGER", 3),
                divider,
                Stakes("Opponent stakes", 14, "OPPONENT", 5),
                new MenuSectionTemplate
                {
                    Name = "Footer",
                    Kind = MenuSectionKind.StaticButtons,
                    SortOrder = 6,
                    DisplaySlot = 45,
                    Width = 9,
                    Height = 1,
                    Overflow = MenuOverflowMode.Hide,
                    Items = { spectate, BackButton(49, 1), CloseButton(53, 2) },
                },
            },
        };
    }

    /// <summary>
    /// §9.3 Arena menu (<c>ctx.arena</c>): info, rules, Spectate, queue, Challenge a player (only inside the arena),
    /// My claims, Back; then the arena's live duels.
    /// </summary>
    private static MenuTemplate ArenaMainTemplate()
    {
        const string arenaParam = "{\"arenaId\":\"$ctx.arena$\"}";

        var info = DemoItem(0, 0,
            Bind("Material", "SHIELD", VariableRefreshPolicy.Static),
            Bind("Name", "$arena.getName$", VariableRefreshPolicy.Ttl, 0, 20),
            Bind("Lore", "$arena.getInfoLines$", VariableRefreshPolicy.Ttl, 0, 20));

        var rules = DemoItem(1, 1,
            Bind("Material", "BOOK", VariableRefreshPolicy.Static),
            Bind("Name", "&eRules", VariableRefreshPolicy.Static),
            Bind("Lore", "$arena.getRulesLines$", VariableRefreshPolicy.Ttl, 0, 100));

        var spectate = DemoItem(2, 2,
            Bind("Material", "ENDER_EYE", VariableRefreshPolicy.Static),
            Bind("Name", "&aSpectate", VariableRefreshPolicy.Static),
            Bind("Lore", "$arena.getSpectateLines$", VariableRefreshPolicy.Ttl, 0, 20),
            Bind("DisplayMode", "$arena.getSpectateDisplayMode$", VariableRefreshPolicy.Ttl, 0, 20));
        spectate.Actions.Add(new ActionBinding { ActionTypeId = "duel.spectate", ParamsJson = arenaParam, SortOrder = 0 });

        var queue = DemoItem(4, 3,
            Bind("Material", "CLOCK", VariableRefreshPolicy.Static),
            Bind("Amount", "$arena.getQueueAmount$", VariableRefreshPolicy.Ttl, 0, 20),
            Bind("Name", "&eQueue", VariableRefreshPolicy.Static),
            Bind("Lore", "$arena.getQueueLines$", VariableRefreshPolicy.Ttl, 0, 20));

        var challenge = DemoItem(6, 4,
            Bind("Material", "IRON_SWORD", VariableRefreshPolicy.Static),
            Bind("Name", "&aChallenge a player", VariableRefreshPolicy.Static),
            Bind("Lore", "$arena.getChallengeLines$", VariableRefreshPolicy.Ttl, 0, 20),
            Bind("DisplayMode", "$arena.getChallengeDisplayMode$", VariableRefreshPolicy.Ttl, 0, 20));
        challenge.Actions.Add(new ActionBinding
        {
            ActionTypeId = "menu.open",
            ParamsJson = "{\"key\":\"" + DuelChallengeMenuKey + "\",\"ctx.arena\":\"$ctx.arena$\"}",
            SortOrder = 0,
        });
        challenge.Conditions.Add(new ConditionBinding
        {
            ConditionTypeId = "duel.in-arena",
            ParamsJson = arenaParam,
            SortOrder = 0,
            Phase = MenuConditionPhase.Click,
        });

        var claims = DemoItem(7, 5,
            Bind("Material", "CHEST", VariableRefreshPolicy.Static),
            Bind("Name", "&6My claims", VariableRefreshPolicy.Static),
            Bind("Lore", "$arena.getClaimsLines$", VariableRefreshPolicy.Ttl, 0, 20),
            Bind("DisplayMode", "$arena.getClaimsDisplayMode$", VariableRefreshPolicy.Ttl, 0, 20));
        claims.Actions.Add(new ActionBinding { ActionTypeId = "duel.claim", ParamsJson = "{}", SortOrder = 0 });

        var divider = new MenuSectionTemplate
        {
            Name = "Divider",
            Kind = MenuSectionKind.StaticButtons,
            SortOrder = 1,
            DisplaySlot = 9,
            Width = 9,
            Height = 1,
            Overflow = MenuOverflowMode.Hide,
        };
        foreach (var slot in new[] { 9, 10, 11, 12, 14, 15, 16, 17 })
            divider.Items.Add(DuelPane(slot, slot));
        divider.Items.Add(DemoItem(13, 0,
            Bind("Material", "IRON_SWORD", VariableRefreshPolicy.Static),
            Bind("Name", "&6Live duels here", VariableRefreshPolicy.Static),
            Lore(0, "&7Click a duelist for the stakes"),
            Lore(1, "&7and both players' statistics.")));

        var empty = WithConditions(DemoItem(31, 50,
                Bind("Material", "BARRIER", VariableRefreshPolicy.Static),
                Bind("Name", "&cNo duels here right now", VariableRefreshPolicy.Static),
                Lore(0, "&7Challenge someone: &f/duel <player>")),
            RenderCondition("duel.matches-empty", arenaParam));

        return new MenuTemplate
        {
            Key = ArenaMainMenuKey,
            Name = "&8Arena",
            Description = "One arena (ctx.arena, else the viewer's): info, rules, queue, live duels, spectate, challenge, claims. Arena duels P6, DESIGN §9.3.",
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
                    Items = { info, rules, spectate, queue, challenge, claims, BackButton(8, 6) },
                },
                divider,
                new MenuSectionTemplate
                {
                    Name = "Live duels",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 2,
                    DisplaySlot = 18,
                    Width = 8,
                    Height = 3,
                    Overflow = MenuOverflowMode.Scroll,
                    ContentSourceId = "duel.matches",
                    ContentSourceParamsJson = arenaParam,
                    Items =
                    {
                        PagerButton(45, "&aPrevious page", "menu.page.prev"),
                        PagerButton(53, "&aNext page", "menu.page.next"),
                        empty,
                        DuelMatchRow(),
                    },
                },
            },
        };
    }

    /// <summary>§9.3 "Challenge a player": online players; a click runs /duel &lt;name&gt; (the proposal editor).</summary>
    private static MenuTemplate DuelChallengeTemplate()
    {
        const string arenaParam = "{\"arenaId\":\"$ctx.arena$\"}";

        var row = new MenuItemTemplate
        {
            SortOrder = 0,
            Amount = 1,
            IsRowTemplate = true,
            DisplayMode = MenuDisplayMode.Normal,
            VariableBindings =
            {
                Bind("Material", "$row.getMaterial$", VariableRefreshPolicy.OnDirty),
                Bind("SkullOwner", "$row.getSkullOwner$", VariableRefreshPolicy.OnDirty),
                Bind("DisplayMode", "$row.getDisplayMode$", VariableRefreshPolicy.Ttl, 0, 20),
                Bind("Name", "$row.getTitle$", VariableRefreshPolicy.Ttl, 0, 20),
                Bind("Lore", "$row.getLines$", VariableRefreshPolicy.Ttl, 0, 20),
            },
            Actions =
            {
                new ActionBinding { ActionTypeId = "duel.challenge", ParamsJson = "{\"name\":\"$row.getName$\"}", SortOrder = 0 },
            },
            Conditions =
            {
                new ConditionBinding
                {
                    ConditionTypeId = "duel.in-arena",
                    ParamsJson = arenaParam,
                    SortOrder = 0,
                    Phase = MenuConditionPhase.Click,
                },
            },
        };

        return new MenuTemplate
        {
            Key = DuelChallengeMenuKey,
            Name = "&8Challenge a player",
            Description = "Online players to challenge from the arena menu (ctx.arena); a pick opens the proposal editor (/duel <name>). Arena duels P6, DESIGN §9.3.",
            Height = 6,
            Growth = MenuGrowthMode.Dynamic,
            MinHeight = 3,
            AutoRefreshTicks = 40,
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
                            Bind("Material", "IRON_SWORD", VariableRefreshPolicy.Static),
                            Bind("Name", "&aChallenge a player", VariableRefreshPolicy.Static),
                            Lore(0, "&7Pick who to duel; you set the"),
                            Lore(1, "&7stakes and gear in the next step.")),
                        BackButton(8, 1),
                    },
                },
                new MenuSectionTemplate
                {
                    Name = "Players",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 1,
                    DisplaySlot = 9,
                    Width = 9,
                    Height = 4,
                    Overflow = MenuOverflowMode.Scroll,
                    ContentSourceId = "duel.challenge-candidates",
                    ContentSourceParamsJson = arenaParam,
                    Items =
                    {
                        PagerButton(45, "&aPrevious page", "menu.page.prev"),
                        PagerButton(53, "&aNext page", "menu.page.next"),
                        row,
                    },
                },
            },
        };
    }
}
