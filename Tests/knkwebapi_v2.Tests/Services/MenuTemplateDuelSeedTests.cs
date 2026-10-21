using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Moq;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Arena duels Phase 6 (DESIGN §9.2-§9.3, KNG-220): the create-only duel menu seeds - each passes the API's
/// create-path validation and wires the ids knk-paper's DuelMenuFeature registers. The plugin-side contract (roots,
/// row sources, actions, conditions, row getters) is checked in knk-paper against the exported content-seeds.json.
/// </summary>
public class MenuTemplateDuelSeedTests
{
    private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<MenuMappingProfile>()).CreateMapper();

    private static readonly string[] DuelKeys =
    {
        MenuTemplateSeed.DuelMatchesMenuKey,
        MenuTemplateSeed.DuelMatchDetailMenuKey,
        MenuTemplateSeed.ArenaMainMenuKey,
        MenuTemplateSeed.DuelChallengeMenuKey,
    };

    public static TheoryData<string> Keys()
    {
        var data = new TheoryData<string>();
        foreach (var key in DuelKeys) data.Add(key);
        return data;
    }

    private static async Task<(KnKDbContext Context, List<MenuTemplate> Seeded)> SeedTwiceAsync()
    {
        var context = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase($"MenuDuelSeed_{Guid.NewGuid()}").Options);
        await MenuTemplateSeed.SeedCanonicalAsync(context);
        await MenuTemplateSeed.SeedCanonicalAsync(context);
        var seeded = await context.MenuTemplates
            .Include(t => t.Sections).ThenInclude(s => s.Items).ThenInclude(i => i.VariableBindings)
            .Include(t => t.Sections).ThenInclude(s => s.Items).ThenInclude(i => i.Actions).ThenInclude(a => a.Conditions)
            .Include(t => t.Sections).ThenInclude(s => s.Items).ThenInclude(i => i.Conditions)
            .ToListAsync();
        return (context, seeded);
    }

    private static MenuItemTemplate ItemAt(MenuTemplate template, int slot) =>
        template.Sections.SelectMany(s => s.Items).Single(i => i.SlotOverride == slot);

    private static MenuItemTemplate RowOf(MenuSectionTemplate section) => Assert.Single(section.Items, i => i.IsRowTemplate);

    private static List<string> Expressions(MenuItemTemplate item, string property) =>
        item.VariableBindings.Where(b => b.TargetProperty == property).OrderBy(b => b.SortOrder).Select(b => b.Expression).ToList();

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task Seed_CreatesEachDuelMenuOnce_AndItPassesServiceValidation(string key)
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var template = Assert.Single(seeded, t => t.Key == key);

        var repo = new Mock<IMenuTemplateRepository>();
        repo.Setup(r => r.GetByKeyAsync(It.IsAny<string>())).ReturnsAsync((MenuTemplate?)null);
        repo.Setup(r => r.AddAsync(It.IsAny<MenuTemplate>())).Returns(Task.CompletedTask);
        var service = new MenuTemplateService(repo.Object, new Mock<IMinecraftMaterialRefRepository>().Object, _mapper);

        var created = await service.CreateAsync(_mapper.Map<MenuTemplateDto>(template));

        Assert.Equal(key, created.Key);
        Assert.True(template.AutoRefreshTicks > 0, "live menus repaint on their own");
    }

    [Fact]
    public async Task Matches_ListsTwoHeadsPerMatchEightWide_AndAHeadOpensTheDetailByToken()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var menu = seeded.Single(t => t.Key == MenuTemplateSeed.DuelMatchesMenuKey);
        var grid = menu.Sections.Single(s => s.Name == "Duels");

        Assert.Equal(("duel.matches", "{}"), (grid.ContentSourceId, grid.ContentSourceParamsJson));
        Assert.Equal((9, 8, 4), (grid.DisplaySlot, grid.Width, grid.Height));
        var row = RowOf(grid);
        Assert.Equal(new[] { "$row.getSkullOwner$" }, Expressions(row, "SkullOwner"));
        Assert.Equal(new[] { "$row.getLines$" }, Expressions(row, "Lore"));
        var open = Assert.Single(row.Actions);
        Assert.Equal(("menu.open", "{\"key\":\"duel_match_detail\",\"ctx.token\":\"$row.getToken$\"}"),
            (open.ActionTypeId, open.ParamsJson));

        var empty = ItemAt(menu, 22);
        var none = Assert.Single(empty.Conditions);
        Assert.Equal(("duel.matches-empty", MenuConditionPhase.Render), (none.ConditionTypeId, none.Phase));
        Assert.Equal(new[] { "$duels.getLines$" }, Expressions(ItemAt(menu, 4), "Lore"));
        Assert.Equal("menu.back", Assert.Single(ItemAt(menu, 8).Actions).ActionTypeId);
    }

    [Fact]
    public async Task Detail_HasBothCardsWithStats_TheirStakes_AndTheSpectatorWarpButton()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var menu = seeded.Single(t => t.Key == MenuTemplateSeed.DuelMatchDetailMenuKey);

        foreach (var (name, slot, side) in new[] { ("Challenger", 0, "CHALLENGER"), ("Opponent", 8, "OPPONENT") })
        {
            var card = menu.Sections.Single(s => s.Name == name);
            Assert.Equal(("duel.match-side", slot), (card.ContentSourceId, card.DisplaySlot));
            Assert.Equal("{\"token\":\"$ctx.token$\",\"side\":\"" + side + "\"}", card.ContentSourceParamsJson);
            Assert.Equal(new[] { "$row.getSkullOwner$" }, Expressions(RowOf(card), "SkullOwner"));

            var stakes = menu.Sections.Single(s => s.Name == name + " stakes");
            Assert.Equal("duel.stakes", stakes.ContentSourceId);
            Assert.Equal("{\"token\":\"$ctx.token$\",\"side\":\"" + side + "\"}", stakes.ContentSourceParamsJson);
            Assert.Equal(new[] { "$row.getAmount$" }, Expressions(RowOf(stakes), "Amount"));
        }

        Assert.Equal(new[] { "$duelMatch.getVsLines$" }, Expressions(ItemAt(menu, 4), "Lore"));
        var spectate = ItemAt(menu, 45);
        var warp = Assert.Single(spectate.Actions);
        Assert.Equal(("duel.spectate", "{\"token\":\"$ctx.token$\"}"), (warp.ActionTypeId, warp.ParamsJson));
        Assert.Equal("value-equals", Assert.Single(spectate.Conditions).ConditionTypeId);
        Assert.Equal("menu.back", Assert.Single(ItemAt(menu, 49).Actions).ActionTypeId);
        Assert.Equal("menu.close", Assert.Single(ItemAt(menu, 53).Actions).ActionTypeId);

        // No two sections claim the same slot.
        var slots = menu.Sections.SelectMany(s => Enumerable.Range(0, s.Height)
            .SelectMany(r => Enumerable.Range(0, s.Width).Select(c => s.DisplaySlot + r * 9 + c))).ToList();
        Assert.Equal(slots.Count, slots.Distinct().Count());
    }

    [Fact]
    public async Task Arena_HasInfoRulesSpectateQueueChallengeClaims_AndItsLiveDuels()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var menu = seeded.Single(t => t.Key == MenuTemplateSeed.ArenaMainMenuKey);
        const string arenaParam = "{\"arenaId\":\"$ctx.arena$\"}";

        Assert.Equal(new[] { "$arena.getInfoLines$" }, Expressions(ItemAt(menu, 0), "Lore"));
        Assert.Equal(new[] { "$arena.getRulesLines$" }, Expressions(ItemAt(menu, 1), "Lore"));

        var spectate = ItemAt(menu, 2);
        Assert.Equal(("duel.spectate", arenaParam), (spectate.Actions.Single().ActionTypeId, spectate.Actions.Single().ParamsJson));
        Assert.Equal(new[] { "$arena.getSpectateDisplayMode$" }, Expressions(spectate, "DisplayMode"));

        Assert.Equal(new[] { "$arena.getQueueLines$" }, Expressions(ItemAt(menu, 4), "Lore"));

        var challenge = ItemAt(menu, 6);
        Assert.Equal(("menu.open", "{\"key\":\"duel_challenge\",\"ctx.arena\":\"$ctx.arena$\"}"),
            (challenge.Actions.Single().ActionTypeId, challenge.Actions.Single().ParamsJson));
        var inside = Assert.Single(challenge.Conditions);
        Assert.Equal(("duel.in-arena", MenuConditionPhase.Click, arenaParam), (inside.ConditionTypeId, inside.Phase, inside.ParamsJson));

        Assert.Equal("duel.claim", Assert.Single(ItemAt(menu, 7).Actions).ActionTypeId);
        Assert.Equal("menu.back", Assert.Single(ItemAt(menu, 8).Actions).ActionTypeId);

        var grid = menu.Sections.Single(s => s.Name == "Live duels");
        Assert.Equal(("duel.matches", arenaParam, 8), (grid.ContentSourceId, grid.ContentSourceParamsJson, grid.Width));
        Assert.Equal("duel.matches-empty", Assert.Single(ItemAt(menu, 31).Conditions).ConditionTypeId);
    }

    [Fact]
    public async Task Challenge_PicksAnOnlinePlayer_AndRunsTheDuelCommandOnlyInsideTheArena()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var menu = seeded.Single(t => t.Key == MenuTemplateSeed.DuelChallengeMenuKey);
        var grid = menu.Sections.Single(s => s.Name == "Players");

        Assert.Equal("duel.challenge-candidates", grid.ContentSourceId);
        var row = RowOf(grid);
        var pick = Assert.Single(row.Actions);
        Assert.Equal(("duel.challenge", "{\"name\":\"$row.getName$\"}"), (pick.ActionTypeId, pick.ParamsJson));
        Assert.Equal(("duel.in-arena", MenuConditionPhase.Click), (row.Conditions.Single().ConditionTypeId, row.Conditions.Single().Phase));
    }
}
