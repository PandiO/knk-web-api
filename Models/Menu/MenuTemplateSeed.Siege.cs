using knkwebapi_v2.Enums;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Models;

/// <summary>
/// Siege Phase 8b (docs/specs/siege-minigame/MENU_TEMPLATES.md Part C, DESIGN §10): the siege
/// menus - <c>siege.overview</c> (lobbies), <c>siege.information</c> (one lobby, per phase; opened
/// with <c>ctx.lobbyId</c>) and <c>siege.spawnpoint</c> (respawn picker). Create-only like every
/// MenuTemplateSeed entry. The plugin side (roots <c>siege</c>/<c>siegeViewer</c>/<c>siegeServer</c>,
/// row sources, actions, conditions) is knk-paper's <c>SiegeMenuFeature</c> over knk-core's
/// <c>core.siege.menu</c> views; the seed ↔ plugin contract test lives in knk-api-client
/// (<c>SiegeMenuSeedContractTest</c>, fixture exported by <c>MenuTemplateSiegeSeedTests</c>).
///
/// Departures from Part C (all cheap to edit via the CRUD API): multi-line lore that depends on
/// the row kind uses one list getter (<c>$row.getLines$</c>) instead of per-line bindings; the vote
/// row uses one <c>siege.vote</c> action whose <c>scenarioId</c> is "random" for the Random row; the
/// hub's Siege tile (C.1, content port CP1) is left as it is.
/// </summary>
public static partial class MenuTemplateSeed
{
    public const string SiegeInformationMenuKey = "siege.information";
    public const string SiegeSpawnpointMenuKey = "siege.spawnpoint";

    private static IEnumerable<MenuTemplate> SiegeTemplates()
    {
        yield return SiegeOverviewTemplate();
        yield return SiegeInformationTemplate();
        yield return SiegeSpawnpointTemplate();
    }

    /// <summary>
    /// Seeding is create-only, so a database seeded before a layout fix keeps the old layout for good unless
    /// the row is deleted. This brings the three siege menus' <em>layout</em> fields (growth, minimum height)
    /// and the Information menu's "Change spawnpoint" phases up to the current seed, in place, without
    /// touching anything an admin may have edited (names, lore, slots, the other conditions). Idempotent:
    /// returns how many templates changed. Playtest 2026-10-24.
    /// </summary>
    public static async Task<int> RepairSiegeLayoutAsync(KnKDbContext context, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        var changed = 0;
        var wanted = SiegeTemplates().ToDictionary(t => t.Key);
        var existing = await context.MenuTemplates
            .Include(t => t.Sections).ThenInclude(s => s.Items).ThenInclude(i => i.Actions)
            .Include(t => t.Sections).ThenInclude(s => s.Items).ThenInclude(i => i.Conditions)
            .Where(t => t.Key == SiegeOverviewMenuKey || t.Key == SiegeInformationMenuKey || t.Key == SiegeSpawnpointMenuKey)
            .ToListAsync(cancellationToken);

        foreach (var template in existing)
        {
            if (!wanted.TryGetValue(template.Key, out var seed)) continue;
            var touched = false;
            if (template.Growth != seed.Growth || template.MinHeight != seed.MinHeight)
            {
                template.Growth = seed.Growth;
                template.MinHeight = seed.MinHeight;
                touched = true;
            }
            if (template.Key == SiegeInformationMenuKey)
            {
                // The "Change spawnpoint" button: the item whose menu.open action opens siege.spawnpoint.
                var phaseConditions = template.Sections
                    .SelectMany(s => s.Items)
                    .Where(i => i.Actions.Any(a => a.ActionTypeId == "menu.open"
                        && a.ParamsJson != null && a.ParamsJson.Contains("\"" + SiegeSpawnpointMenuKey + "\"")))
                    .SelectMany(i => i.Conditions)
                    .Where(c => c.ConditionTypeId == "siege.phase" && c.ParamsJson != null
                        && c.ParamsJson.Contains("\"phases\":\"IN_PROGRESS\""));
                foreach (var condition in phaseConditions)
                {
                    condition.ParamsJson = condition.ParamsJson!.Replace("\"phases\":\"IN_PROGRESS\"", "\"phases\":\"HUB,IN_PROGRESS\"");
                    touched = true;
                }
            }
            if (touched) changed++;
        }

        if (changed > 0)
            await context.SaveChangesAsync(cancellationToken);
        logger?.LogInformation("Siege menu layout repair complete. Templates updated: {Changed}", changed);
        return changed;
    }

    private const string LobbyParam = "{\"lobbyId\":\"$ctx.lobbyId$\"}";

    /// <summary>C.2 - every lobby, matchmaking first (fixes N5), clicking one opens its Information.</summary>
    private static MenuTemplate SiegeOverviewTemplate()
    {
        var row = new MenuItemTemplate
        {
            SortOrder = 0,
            Amount = 1,
            IsRowTemplate = true,
            DisplayMode = MenuDisplayMode.Normal,
            VariableBindings =
            {
                Bind("Material", "$row.getBannerMaterial$", VariableRefreshPolicy.OnDirty),
                Bind("Amount", "$row.getMemberCountOrOne$", VariableRefreshPolicy.Ttl, 0, 20),
                Bind("Name", "&7$row.getName$", VariableRefreshPolicy.OnDirty),
                Bind("Lore", "&7Scenario: $row.getScenarioLabel$", VariableRefreshPolicy.Ttl, 0, 20),
                Bind("Lore", "&7Joined players: $row.getMemberCountLabel$", VariableRefreshPolicy.Ttl, 1, 20),
                Bind("Lore", "&7Current stage: &a$row.getPhaseLabel$", VariableRefreshPolicy.Ttl, 2, 20),
                Bind("Lore", "$row.getEntryRequirementLine$", VariableRefreshPolicy.Ttl, 3, 20),
                Lore(4, ""),
                Bind("Lore", "&7$row.getTimerLabel$", VariableRefreshPolicy.Ttl, 5, 20),
                Bind("Lore", "&a$row.getTimeRemaining$", VariableRefreshPolicy.Ttl, 6, 20),
                Lore(7, ""),
                Bind("Lore", "$row.getJoinHintLines$", VariableRefreshPolicy.Ttl, 8, 20),
            },
            Actions =
            {
                new ActionBinding
                {
                    ActionTypeId = "menu.open",
                    ParamsJson = "{\"key\":\"" + SiegeInformationMenuKey + "\",\"ctx.lobbyId\":\"$row.getLobbyId$\"}",
                    SortOrder = 0,
                },
            },
            Conditions =
            {
                // Smoke test 2026-09-26: a lobby in cooldown (or disabled) is listed, not opened.
                new ConditionBinding
                {
                    ConditionTypeId = "siege.lobby-open",
                    ParamsJson = "{\"lobbyId\":\"$row.getLobbyId$\"}",
                    SortOrder = 0,
                    Phase = MenuConditionPhase.Click,
                },
            },
        };

        var empty = WithConditions(DemoItem(22, 50,
                Bind("Material", "BARRIER", VariableRefreshPolicy.Static),
                Bind("Name", "&cNo Sieges", VariableRefreshPolicy.Static),
                Lore(0, "&7There are currently no active Sieges."),
                Lore(1, "&7Sieges start automatically - check back soon.")),
            RenderCondition("siege.lobbies-empty", "{}"));

        return new MenuTemplate
        {
            Key = SiegeOverviewMenuKey,
            Name = "&8Siege",
            Description = "Every siege lobby (siege.lobbies), matchmaking first. Siege Phase 8b, MENU_TEMPLATES.md C.2.",
            Height = 5,
            // Smoke test 2026-09-26: shrinks to its content (header + the lobby rows in use).
            Growth = MenuGrowthMode.Dynamic,
            MinHeight = 2,
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
                            Bind("Material", "WHITE_BANNER", VariableRefreshPolicy.Static),
                            Bind("Name", "&aSiege", VariableRefreshPolicy.Static),
                            Lore(0, "&7Check the status of"),
                            Lore(1, "&7active sieges or join"),
                            Lore(2, "&7a siege"),
                            Lore(3, ""),
                            Bind("Lore", "&7Active sieges: &a$siegeServer.getLobbyCount$", VariableRefreshPolicy.Ttl, 4, 20),
                            Bind("Lore", "&7Players playing Siege: &a$siegeServer.getPlayingCount$", VariableRefreshPolicy.Ttl, 5, 20),
                            Bind("Lore", "$siegeServer.getEntryHintLine$", VariableRefreshPolicy.Ttl, 6, 20)),
                        BackButton(8, 1),
                    },
                },
                new MenuSectionTemplate
                {
                    Name = "Sieges",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 1,
                    DisplaySlot = 9,
                    Width = 9,
                    Height = 4,
                    Overflow = MenuOverflowMode.Scroll,
                    ContentSourceId = "siege.lobbies",
                    ContentSourceParamsJson = "{}",
                    Items =
                    {
                        PagerButton(36, "&aPrevious page", "menu.page.prev"),
                        PagerButton(44, "&aNext page", "menu.page.next"),
                        empty,
                        row,
                    },
                },
            },
        };
    }

    /// <summary>
    /// C.3 - one lobby (<c>ctx.lobbyId</c>): header, votes (matchmaking), join/leave (matchmaking),
    /// players/teams divider and the phase-aware body (members before the match, objectives during it).
    /// </summary>
    private static MenuTemplate SiegeInformationTemplate()
    {
        var changeSpawn = DemoItem(2, 0,
            Bind("Material", "COMPASS", VariableRefreshPolicy.Static),
            Bind("Name", "&aChange spawnpoint", VariableRefreshPolicy.Static),
            Lore(0, "&7Click to choose where you"),
            Lore(1, "&7respawn when killed"),
            Lore(2, ""),
            Bind("Lore", "&7Current: &a$siegeViewer.getCurrentSpawnName$", VariableRefreshPolicy.Ttl, 3, 20));
        changeSpawn.Actions.Add(new ActionBinding
        {
            ActionTypeId = "menu.open",
            ParamsJson = "{\"key\":\"" + SiegeSpawnpointMenuKey + "\"}",
            SortOrder = 0,
        });
        // Playtest 2026-10-24: the spawn picker opens at the team split (HUB), so the choice is changeable from then on.
        changeSpawn.Conditions.Add(RenderCondition("siege.phase", "{\"lobbyId\":\"$ctx.lobbyId$\",\"phases\":\"HUB,IN_PROGRESS\"}"));
        changeSpawn.Conditions.Add(RenderCondition("siege.participating", "{\"lobbyId\":\"$ctx.lobbyId$\",\"expected\":\"true\"}"));

        var status = DemoItem(4, 1,
            Bind("Material", "$siege.getBannerMaterial$", VariableRefreshPolicy.OnDirty),
            Bind("Name", "&7$siege.getName$", VariableRefreshPolicy.OnDirty),
            Bind("Lore", "&7Scenario: $siege.getScenarioLabel$", VariableRefreshPolicy.Ttl, 0, 20),
            Bind("Lore", "&7Joined players: $siege.getMemberCountLabel$", VariableRefreshPolicy.Ttl, 1, 20),
            Bind("Lore", "&7Current stage: &a$siege.getPhaseLabel$", VariableRefreshPolicy.Ttl, 2, 20),
            Bind("Lore", "$siege.getEntryRequirementLine$", VariableRefreshPolicy.Ttl, 3, 20),
            Lore(4, ""),
            Bind("Lore", "&7$siege.getTimerLabel$", VariableRefreshPolicy.Ttl, 5, 20),
            Bind("Lore", "&a$siege.getTimeRemaining$", VariableRefreshPolicy.Ttl, 6, 20),
            Bind("Lore", "$siegeViewer.getTeamLine$", VariableRefreshPolicy.Ttl, 7, 20));

        var help = WithConditions(DemoItem(6, 2,
                Bind("Material", "COMPASS", VariableRefreshPolicy.Static),
                Bind("Name", "&7Objective information", VariableRefreshPolicy.Static),
                Lore(0, "&6How to play"),
                Lore(1, "&7Objectives are marked by a banner."),
                Lore(2, "&7Stand inside an objective's circle"),
                Lore(3, "&7to capture (attackers) or defend it."),
                Lore(4, "&7Capturing a \"&aWin&7\" objective ends the game."),
                Lore(5, "&7Held objectives are extra spawnpoints."),
                Lore(6, "&7Selected gates can be opened and closed"),
                Lore(7, "&7by the team that owns them."),
                Bind("Lore", "$siege.getRecaptureLine$", VariableRefreshPolicy.OnDirty, 8),
                Lore(9, "&7Enchantment books appear near objectives;"),
                Lore(10, "&7click one onto an item to apply it."),
                Lore(11, "&8Siege enchantments are removed afterwards."),
                Lore(12, ""),
                Bind("Lore", "&7Objectives: &a$siege.getObjectiveCount$", VariableRefreshPolicy.OnDirty, 13),
                Bind("Lore", "&7Objectives with a gate: &a$siege.getGateObjectiveCount$", VariableRefreshPolicy.OnDirty, 14)),
            RenderCondition("value-equals", "{\"value\":\"$siege.getScenarioKnown$\",\"expected\":\"true\"}"));

        // Join or leave (matchmaking only): which action runs is picked by action-level Render
        // conditions on the viewer's membership; joining is also click-checked (siege.join-eligible).
        var joinLeave = DemoItem(14, 0,
            Bind("Material", "$siegeViewer.getJoinLeaveMaterial$", VariableRefreshPolicy.OnDirty),
            Bind("Name", "$siegeViewer.getJoinLeaveName$", VariableRefreshPolicy.OnDirty),
            Bind("Lore", "$siegeViewer.getParticipationLine$", VariableRefreshPolicy.OnDirty, 0),
            Bind("Lore", "$siegeViewer.getJoinDenialLine$", VariableRefreshPolicy.Ttl, 1, 20));
        var join = new ActionBinding { ActionTypeId = "siege.join", ParamsJson = LobbyParam, SortOrder = 0 };
        var leave = new ActionBinding
        {
            ActionTypeId = "menu.confirm.doubleclick",
            ParamsJson = "{\"actionTypeId\":\"siege.leave\",\"actionParamsJson\":\"{}\",\"windowTicks\":\"60\","
                + "\"armMessage\":\"Click again within 3s to leave the siege.\"}",
            SortOrder = 1,
        };
        joinLeave.Actions.Add(join);
        joinLeave.Actions.Add(leave);
        AddActionCondition(joinLeave, join,
            RenderCondition("value-equals", "{\"value\":\"$siegeViewer.getIsMember$\",\"expected\":\"false\"}"));
        AddActionCondition(joinLeave, join, new ConditionBinding
        {
            ConditionTypeId = "siege.join-eligible",
            ParamsJson = LobbyParam,
            SortOrder = 1,
            Phase = MenuConditionPhase.Click,
        });
        AddActionCondition(joinLeave, leave,
            RenderCondition("value-equals", "{\"value\":\"$siegeViewer.getIsMember$\",\"expected\":\"true\"}"));
        joinLeave.Conditions.Add(RenderCondition("siege.phase", "{\"lobbyId\":\"$ctx.lobbyId$\",\"phases\":\"MATCHMAKING\"}"));

        var voteRow = new MenuItemTemplate
        {
            SortOrder = 0,
            Amount = 1,
            IsRowTemplate = true,
            DisplayMode = MenuDisplayMode.Normal,
            VariableBindings =
            {
                Bind("Material", "$row.getMaterial$", VariableRefreshPolicy.OnDirty),
                Bind("Amount", "$row.getVoteCountOrOne$", VariableRefreshPolicy.OnDirty),
                Bind("DisplayMode", "$row.getDisplayMode$", VariableRefreshPolicy.OnDirty),
                Bind("Name", "$row.getTitle$", VariableRefreshPolicy.OnDirty),
                Bind("Lore", "$row.getLines$", VariableRefreshPolicy.OnDirty),
            },
            Actions =
            {
                new ActionBinding
                {
                    ActionTypeId = "siege.vote",
                    ParamsJson = "{\"lobbyId\":\"$ctx.lobbyId$\",\"scenarioId\":\"$row.getChoiceKey$\"}",
                    SortOrder = 0,
                },
            },
            Conditions =
            {
                new ConditionBinding
                {
                    ConditionTypeId = "siege.participating",
                    ParamsJson = "{\"lobbyId\":\"$ctx.lobbyId$\",\"expected\":\"true\"}",
                    SortOrder = 0,
                    Phase = MenuConditionPhase.Click,
                },
                new ConditionBinding
                {
                    ConditionTypeId = "siege.vote-open",
                    ParamsJson = LobbyParam,
                    SortOrder = 1,
                    Phase = MenuConditionPhase.Click,
                },
            },
        };

        var divider = new MenuSectionTemplate
        {
            Name = "Divider",
            Kind = MenuSectionKind.StaticButtons,
            SortOrder = 3,
            DisplaySlot = 18,
            Width = 9,
            Height = 1,
            Overflow = MenuOverflowMode.Hide,
        };
        foreach (var slot in new[] { 18, 19, 20, 21, 23, 24, 25, 26 })
        {
            divider.Items.Add(DemoItem(slot, slot,
                Bind("Material", "BLACK_STAINED_GLASS_PANE", VariableRefreshPolicy.Static),
                Bind("Name", " ", VariableRefreshPolicy.Static)));
        }
        divider.Items.Add(DemoItem(22, 0,
            Bind("Material", "SKELETON_SKULL", VariableRefreshPolicy.Static),
            Bind("Amount", "$siege.getMemberCountOrOne$", VariableRefreshPolicy.Ttl, 0, 20),
            Bind("Name", "&aJoined players", VariableRefreshPolicy.Static),
            Bind("Lore", "&7Joined players: $siege.getMemberCountLabel$", VariableRefreshPolicy.Ttl, 0, 20),
            Bind("Lore", "$siege.getPlayersMinLine$", VariableRefreshPolicy.OnDirty, 1),
            Bind("Lore", "$siege.getPlayersMaxLine$", VariableRefreshPolicy.OnDirty, 2),
            Bind("Lore", "$siege.getTeamSummaryLines$", VariableRefreshPolicy.Ttl, 3, 20)));

        var bodyRow = new MenuItemTemplate
        {
            SortOrder = 0,
            Amount = 1,
            IsRowTemplate = true,
            DisplayMode = MenuDisplayMode.Normal,
            VariableBindings =
            {
                Bind("Material", "$row.getMaterial$", VariableRefreshPolicy.OnDirty),
                Bind("SkullOwner", "$row.getSkullOwner$", VariableRefreshPolicy.OnDirty),
                Bind("BannerPatterns", "$row.getBannerPatterns$", VariableRefreshPolicy.Ttl, 0, 20),
                Bind("DisplayMode", "$row.getDisplayMode$", VariableRefreshPolicy.Ttl, 0, 20),
                Bind("Name", "$row.getTitle$", VariableRefreshPolicy.OnDirty),
                Bind("Lore", "$row.getLines$", VariableRefreshPolicy.Ttl, 0, 20),
            },
        };

        return new MenuTemplate
        {
            Key = SiegeInformationMenuKey,
            Name = "&8Siege information",
            Description = "One siege lobby (ctx.lobbyId), per phase. Siege Phase 8b, MENU_TEMPLATES.md C.3.",
            Height = 6,
            // Smoke test 2026-09-26: shrinks to its content (the votes row outside matchmaking and unused
            // body rows drop out; header + divider + one body row stay).
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
                    Items = { changeSpawn, status, help, BackButton(8, 3) },
                },
                new MenuSectionTemplate
                {
                    Name = "Votes",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 1,
                    DisplaySlot = 9,
                    Width = 4,
                    Height = 1,
                    Overflow = MenuOverflowMode.Hide,
                    ContentSourceId = "siege.vote-candidates",
                    ContentSourceParamsJson = LobbyParam,
                    Items = { voteRow },
                },
                new MenuSectionTemplate
                {
                    Name = "Actions",
                    Kind = MenuSectionKind.StaticButtons,
                    SortOrder = 2,
                    DisplaySlot = 13,
                    Width = 5,
                    Height = 1,
                    Overflow = MenuOverflowMode.Hide,
                    Items = { joinLeave },
                },
                divider,
                new MenuSectionTemplate
                {
                    Name = "Body",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 4,
                    DisplaySlot = 27,
                    Width = 9,
                    Height = 3,
                    Overflow = MenuOverflowMode.Scroll,
                    ContentSourceId = "siege.body",
                    ContentSourceParamsJson = LobbyParam,
                    Items =
                    {
                        PagerButton(45, "&aPrevious page", "menu.page.prev"),
                        PagerButton(53, "&aNext page", "menu.page.next"),
                        bodyRow,
                    },
                },
            },
        };
    }

    /// <summary>C.4 - the viewer's respawn options (held objectives first, then team spawnpoints).</summary>
    private static MenuTemplate SiegeSpawnpointTemplate()
    {
        var close = DemoItem(8, 1,
            Bind("Material", "BARRIER", VariableRefreshPolicy.Static),
            Bind("Name", "&cClose", VariableRefreshPolicy.Static));
        close.Actions.Add(new ActionBinding { ActionTypeId = "menu.close", ParamsJson = "{}", SortOrder = 0 });

        var option = new MenuItemTemplate
        {
            SortOrder = 0,
            Amount = 1,
            IsRowTemplate = true,
            DisplayMode = MenuDisplayMode.Normal,
            VariableBindings =
            {
                Bind("Material", "$row.getMaterial$", VariableRefreshPolicy.OnDirty),
                Bind("BannerPatterns", "$row.getBannerPatterns$", VariableRefreshPolicy.Ttl, 0, 20),
                Bind("DisplayMode", "$row.getDisplayMode$", VariableRefreshPolicy.Ttl, 0, 20),
                Bind("Name", "$row.getName$", VariableRefreshPolicy.OnDirty),
                Bind("Lore", "$row.getStatusLines$", VariableRefreshPolicy.Ttl, 0, 20),
            },
            Actions =
            {
                new ActionBinding { ActionTypeId = "siege.spawn", ParamsJson = "{\"option\":\"$row.getOption$\"}", SortOrder = 0 },
                new ActionBinding { ActionTypeId = "menu.close", ParamsJson = "{}", SortOrder = 1 },
            },
            Conditions =
            {
                new ConditionBinding
                {
                    ConditionTypeId = "siege.spawn-available",
                    ParamsJson = "{\"option\":\"$row.getOption$\"}",
                    SortOrder = 0,
                    Phase = MenuConditionPhase.Click,
                },
            },
        };

        return new MenuTemplate
        {
            Key = SiegeSpawnpointMenuKey,
            Name = "&8Choose a spawnpoint",
            Description = "The viewer's spawn options in their siege, from the team split on. Siege Phase 8b, MENU_TEMPLATES.md C.4.",
            Height = 3,
            // Playtest 2026-10-24: shrinks to its options (header + the option rows in use).
            Growth = MenuGrowthMode.Dynamic,
            MinHeight = 2,
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
                            Bind("Material", "COMPASS", VariableRefreshPolicy.Static),
                            Bind("Name", "&aChoose a place to spawn", VariableRefreshPolicy.Static),
                            Bind("Lore", "&7Current: &a$siegeViewer.getCurrentSpawnName$", VariableRefreshPolicy.Ttl, 0, 20),
                            Lore(1, "&7Close this menu to keep it."),
                            Lore(2, "&7Change it later from the siege menu.")),
                        close,
                    },
                },
                new MenuSectionTemplate
                {
                    Name = "Options",
                    Kind = MenuSectionKind.ContentGrid,
                    SortOrder = 1,
                    DisplaySlot = 9,
                    Width = 9,
                    Height = 2,
                    Overflow = MenuOverflowMode.Scroll,
                    ContentSourceId = "siege.spawn-options",
                    ContentSourceParamsJson = "{}",
                    Items =
                    {
                        PagerButton(18, "&aPrevious page", "menu.page.prev"),
                        PagerButton(26, "&aNext page", "menu.page.next"),
                        option,
                    },
                },
            },
        };
    }
}
