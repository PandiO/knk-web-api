using System.Text.Json;
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
/// Player statistics privacy (KNG-34, IMPLEMENTATION_PLAN.md §5.3): the <c>statistics.visibility</c>
/// seed and the profile's "Statistics privacy" tile. The plugin-side contract (root, row source,
/// actions, condition, row getters) is checked in knk-paper against the exported content-seeds.json.
/// </summary>
public class MenuTemplateStatisticsSeedTests
{
    private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<MenuMappingProfile>()).CreateMapper();

    private static async Task<(KnKDbContext Context, List<MenuTemplate> Seeded)> SeedTwiceAsync()
    {
        var options = new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase($"MenuStatisticsSeed_{Guid.NewGuid()}")
            .Options;
        var context = new KnKDbContext(options);
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

    private static List<string> Expressions(MenuItemTemplate item, string property) =>
        item.VariableBindings.Where(b => b.TargetProperty == property).OrderBy(b => b.SortOrder).Select(b => b.Expression).ToList();

    private static string Param(ActionBinding action, string name)
    {
        using var json = JsonDocument.Parse(action.ParamsJson);
        return json.RootElement.GetProperty(name).GetString()!;
    }

    [Fact]
    public async Task Seed_CreatesTheMenuOnce_AndTheApiWouldAcceptIt()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var template = Assert.Single(seeded, t => t.Key == MenuTemplateSeed.StatisticsVisibilityMenuKey);

        var repo = new Mock<IMenuTemplateRepository>();
        repo.Setup(r => r.GetByKeyAsync(It.IsAny<string>())).ReturnsAsync((MenuTemplate?)null);
        repo.Setup(r => r.AddAsync(It.IsAny<MenuTemplate>())).Returns(Task.CompletedTask);
        var service = new MenuTemplateService(repo.Object, new Mock<IMinecraftMaterialRefRepository>().Object, _mapper);

        var created = await service.CreateAsync(_mapper.Map<MenuTemplateDto>(template));

        Assert.Equal(MenuTemplateSeed.StatisticsVisibilityMenuKey, created.Key);
        Assert.Equal((6, 3, MenuGrowthMode.Dynamic), (template.Height, template.MinHeight!.Value, template.Growth));
    }

    [Fact]
    public async Task Header_HasTheFiveGroupSelectorsTheThreeGroupActionsAndBack()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var menu = seeded.Single(t => t.Key == MenuTemplateSeed.StatisticsVisibilityMenuKey);

        var groups = new[] { "activity", "combat", "minigames", "exploration", "progression" };
        var modes = new[] { "Activity", "Combat", "Minigames", "Exploration", "Progression" };
        for (var slot = 0; slot < 5; slot++)
        {
            var selector = ItemAt(menu, slot);
            var action = Assert.Single(selector.Actions);
            Assert.Equal("statistics.visibility.select-group", action.ActionTypeId);
            Assert.Equal(groups[slot], Param(action, "group"));
            Assert.Equal(new[] { $"$statsvis.get{modes[slot]}Mode$" }, Expressions(selector, "DisplayMode"));
        }

        var values = new[] { "Nobody", "Friends", "Everyone" };
        for (var i = 0; i < 3; i++)
        {
            var action = Assert.Single(ItemAt(menu, 5 + i).Actions);
            Assert.Equal("statistics.visibility.group", action.ActionTypeId);
            Assert.Equal(values[i], Param(action, "value"));
        }
        Assert.Contains("&8Friends-only shows nothing until the friends system exists", Expressions(ItemAt(menu, 6), "Lore"));

        Assert.Equal("menu.back", Assert.Single(ItemAt(menu, 8).Actions).ActionTypeId);
        Assert.Equal(new[] { "$statsvis.getGroupSummaryLines$", "", "&7Click a setting to change it.",
            "&7Active/AFK time, XP and your title", "&7are always public." }, Expressions(ItemAt(menu, 13), "Lore"));
    }

    [Fact]
    public async Task Settings_RowsCycleThroughTheClickedSetting()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var grid = seeded.Single(t => t.Key == MenuTemplateSeed.StatisticsVisibilityMenuKey).Sections.Single(s => s.Name == "Settings");

        Assert.Equal("statistics.visibility.rows", grid.ContentSourceId);
        Assert.Equal((18, 9, 3), (grid.DisplaySlot, grid.Width, grid.Height));
        Assert.Equal(MenuOverflowMode.Scroll, grid.Overflow);

        var row = Assert.Single(grid.Items, i => i.IsRowTemplate);
        var cycle = Assert.Single(row.Actions);
        Assert.Equal("statistics.visibility.cycle", cycle.ActionTypeId);
        Assert.Equal("$row.getSettingKey$", Param(cycle, "settingKey"));
        Assert.Equal("$row.getContext$", Param(cycle, "context"));
        Assert.Equal("$row.getVisibility$", Param(cycle, "expected"));
        Assert.Equal(new[] { "$row.getMaterial$" }, Expressions(row, "Material"));
        Assert.Equal(new[] { "$row.getName$" }, Expressions(row, "Name"));
        Assert.Equal(new[] { "$row.getLoreLines$" }, Expressions(row, "Lore"));
    }

    [Fact]
    public async Task Controls_PageAndConfirmOrCancelAPendingGroupAction()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var menu = seeded.Single(t => t.Key == MenuTemplateSeed.StatisticsVisibilityMenuKey);
        var grid = menu.Sections.Single(s => s.Name == "Settings");

        var pinned = grid.Items.Where(i => !i.IsRowTemplate).OrderBy(i => i.SlotOverride).Select(i => i.SlotOverride!.Value);
        Assert.Equal(new[] { 45, 48, 50, 53 }, pinned);
        Assert.Equal("menu.page.prev", Assert.Single(ItemAt(menu, 45).Actions).ActionTypeId);
        Assert.Equal("menu.page.next", Assert.Single(ItemAt(menu, 53).Actions).ActionTypeId);

        var confirm = ItemAt(menu, 48);
        Assert.Equal("menu.confirm.accept", Assert.Single(confirm.Actions).ActionTypeId);
        Assert.Equal("statistics.visibility.pending", Assert.Single(confirm.Conditions).ConditionTypeId);
        Assert.Equal(new[] { "$statsvis.getPreviewLines$" }, Expressions(confirm, "Lore"));

        var cancel = ItemAt(menu, 50);
        Assert.Equal("menu.confirm.cancel", Assert.Single(cancel.Actions).ActionTypeId);
        Assert.Equal("statistics.visibility.pending", Assert.Single(cancel.Conditions).ConditionTypeId);
    }

    [Fact]
    public async Task Profile_HasTheStatisticsPrivacyTileInHeaderSlot6BehindMenuAvailable()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var tile = ItemAt(seeded.Single(t => t.Key == MenuTemplateSeed.ProfileMenuKey), 6);

        var open = Assert.Single(tile.Actions);
        Assert.Equal("menu.open", open.ActionTypeId);
        Assert.Equal(MenuTemplateSeed.StatisticsVisibilityMenuKey, Param(open, "key"));
        var available = Assert.Single(tile.Conditions);
        Assert.Equal("menu-available", available.ConditionTypeId);
        Assert.Equal(new[] { "&dStatistics privacy" }, Expressions(tile, "Name"));
    }

    // ---- link 5: read surfaces ----

    [Fact]
    public async Task Profile_HasTheStatisticsTileInHeaderSlot5BehindMenuAvailable()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var tile = ItemAt(seeded.Single(t => t.Key == MenuTemplateSeed.ProfileMenuKey), 5);

        var open = Assert.Single(tile.Actions);
        Assert.Equal(("menu.open", MenuTemplateSeed.StatisticsMainMenuKey), (open.ActionTypeId, Param(open, "key")));
        Assert.Equal("menu-available", Assert.Single(tile.Conditions).ConditionTypeId);
        Assert.Equal(new[] { "&eStatistics" }, Expressions(tile, "Name"));
    }

    [Fact]
    public async Task StatisticsMain_ShowsTheGroupsFromTheStatsRoot_CyclesThePeriod_AndListsTheTitleHistory()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var menu = Assert.Single(seeded, t => t.Key == MenuTemplateSeed.StatisticsMainMenuKey);

        Assert.Equal(new[] { "$stats.getTargetName$" }, Expressions(ItemAt(menu, 0), "SkullOwner"));
        Assert.Equal(new[] { "$stats.getProfileLines$" }, Expressions(ItemAt(menu, 0), "Lore"));
        var groups = new[] { "Activity", "Combat", "Minigames", "Exploration", "Progression" };
        for (var i = 0; i < groups.Length; i++)
        {
            Assert.Equal(new[] { $"$stats.get{groups[i]}Lines$" }, Expressions(ItemAt(menu, 1 + i), "Lore"));
        }
        Assert.Equal("statistics.main.period", Assert.Single(ItemAt(menu, 6).Actions).ActionTypeId);
        Assert.Equal(MenuTemplateSeed.LeaderboardsMenuKey, Param(Assert.Single(ItemAt(menu, 7).Actions), "key"));
        Assert.Equal("menu.back", Assert.Single(ItemAt(menu, 8).Actions).ActionTypeId);

        var grid = menu.Sections.Single(s => s.Name == "TitleHistory");
        Assert.Equal("statistics.main.title-history", grid.ContentSourceId);
        var row = Assert.Single(grid.Items, i => i.IsRowTemplate);
        Assert.Empty(row.Actions); // read-only
        Assert.Equal(new[] { "$row.getLoreLines$" }, Expressions(row, "Lore"));
    }

    [Fact]
    public async Task Leaderboards_OpenTheClickedBoard_AndBoardEntriesOpenThatPlayersStatistics()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;

        var list = Assert.Single(seeded, t => t.Key == MenuTemplateSeed.LeaderboardsMenuKey);
        var boards = list.Sections.Single(s => s.Name == "Boards");
        Assert.Equal("statistics.leaderboards.boards", boards.ContentSourceId);
        var open = Assert.Single(Assert.Single(boards.Items, i => i.IsRowTemplate).Actions);
        Assert.Equal((MenuTemplateSeed.LeaderboardMenuKey, "$row.getBoardKey$"), (Param(open, "key"), Param(open, "ctx.board")));

        var board = Assert.Single(seeded, t => t.Key == MenuTemplateSeed.LeaderboardMenuKey);
        Assert.Equal("statistics.leaderboard.period", Assert.Single(ItemAt(board, 4).Actions).ActionTypeId);
        Assert.Equal(new[] { "$lb.getViewerLines$" }, Expressions(ItemAt(board, 6), "Lore"));
        var ranking = board.Sections.Single(s => s.Name == "Ranking");
        Assert.Equal(("statistics.leaderboard.entries", 18), (ranking.ContentSourceId, ranking.Width * ranking.Height));
        var entry = Assert.Single(ranking.Items, i => i.IsRowTemplate);
        var stats = Assert.Single(entry.Actions);
        Assert.Equal((MenuTemplateSeed.StatisticsMainMenuKey, "$row.getUserId$", "$row.getUsername$"),
            (Param(stats, "key"), Param(stats, "ctx.target"), Param(stats, "ctx.name")));
        Assert.Equal(new[] { "$row.getDisplayMode$" }, Expressions(entry, "DisplayMode"));
    }
}
