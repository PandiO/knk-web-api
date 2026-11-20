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
/// Production menus (P6a, PandiO/knk-workspace#683, decision record § 15.1 M1-M5, A27): the structures overview, the
/// structure menu and the transport orders list are seeded as menu content, open on a fresh database (they pass the
/// menu service's validation), and the hub gets a "Structures" tile (also added to a hub seeded before it).
/// </summary>
public class MenuTemplateProductionSeedTests
{
    private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<MenuMappingProfile>()).CreateMapper();

    private static KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase($"MenuProductionSeed_{Guid.NewGuid()}").Options);

    private static Task<List<MenuTemplate>> LoadAsync(KnKDbContext context) => context.MenuTemplates
        .Include(t => t.Sections).ThenInclude(s => s.Items).ThenInclude(i => i.VariableBindings)
        .Include(t => t.Sections).ThenInclude(s => s.Items).ThenInclude(i => i.Actions).ThenInclude(a => a.Conditions)
        .Include(t => t.Sections).ThenInclude(s => s.Items).ThenInclude(i => i.Conditions)
        .ToListAsync();

    private static async Task<(KnKDbContext Context, List<MenuTemplate> Seeded)> SeedTwiceAsync()
    {
        var context = NewContext();
        await MenuTemplateSeed.SeedCanonicalAsync(context);
        await MenuTemplateSeed.SeedCanonicalAsync(context);
        return (context, await LoadAsync(context));
    }

    private static MenuItemTemplate ItemAt(MenuTemplate template, int slot) =>
        template.Sections.SelectMany(s => s.Items).Single(i => i.SlotOverride == slot);

    private static List<string> Expressions(MenuItemTemplate item, string property) =>
        item.VariableBindings.Where(b => b.TargetProperty == property).OrderBy(b => b.SortOrder).Select(b => b.Expression).ToList();

    public static IEnumerable<object[]> Keys() => new[]
    {
        new object[] { MenuTemplateSeed.StructuresOverviewMenuKey },
        new object[] { MenuTemplateSeed.StructureMenuKey },
        new object[] { MenuTemplateSeed.StructureOrdersMenuKey },
    };

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task EachMenu_IsSeededOnce_AndTheApiWouldAcceptIt(string key)
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
        var slots = template.Sections.SelectMany(s => s.Items).Where(i => i.SlotOverride.HasValue).Select(i => i.SlotOverride).ToList();
        Assert.Equal(slots.Count, slots.Distinct().Count());
    }

    [Fact]
    public async Task Overview_IsSearchable_FiltersByTownKindAndStatus_AndARowOpensTheStructureMenu()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var menu = seeded.Single(t => t.Key == MenuTemplateSeed.StructuresOverviewMenuKey);
        var grid = menu.Sections.Single(s => s.Name == "Structures");
        Assert.True(grid.Searchable);
        Assert.Equal("structures.overview", grid.ContentSourceId);

        var row = Assert.Single(grid.Items, i => i.IsRowTemplate);
        var open = Assert.Single(row.Actions);
        Assert.Equal("menu.open", open.ActionTypeId);
        Assert.Contains("\"key\":\"structures.structure\"", open.ParamsJson);
        Assert.Contains("\"ctx.structureId\":\"$row.getId$\"", open.ParamsJson);

        var facets = grid.Items.SelectMany(i => i.Actions).Where(a => a.ActionTypeId == "menu.filter.cycle").Select(a => a.ParamsJson).ToList();
        Assert.Contains(facets, p => p.Contains("\"facetKey\":\"Town\""));
        Assert.Contains(facets, p => p.Contains("\"facetKey\":\"Kind\""));
        Assert.Contains(facets, p => p.Contains("\"facetKey\":\"Status\"") && p.Contains("Working,Stalled,NoRoad,Full"));
        Assert.Equal("menu.search.prompt", Assert.Single(ItemAt(menu, 49).Actions).ActionTypeId);
        // V2 overview pattern: header item at slot 4 explaining the list.
        Assert.Equal(new[] { "&6Structures" }, Expressions(ItemAt(menu, 4), "Name"));
    }

    [Fact]
    public async Task StructureMenu_ShowsDetailsOnlyWhenTheApiSaysSo_AndStaffButtonsBehindTheWorldNode()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var menu = seeded.Single(t => t.Key == MenuTemplateSeed.StructureMenuKey);

        Assert.Equal(new[] { "$structure.getSummaryLines$" }, Expressions(ItemAt(menu, 4), "Lore"));
        Assert.Empty(ItemAt(menu, 4).Conditions);
        foreach (var slot in new[] { 19, 21, 23, 25 })
        {
            var condition = Assert.Single(ItemAt(menu, slot).Conditions);
            Assert.Equal(("value-equals", MenuConditionPhase.Render), (condition.ConditionTypeId, condition.Phase));
            Assert.Contains("$structure.getSeesDetails$", condition.ParamsJson);
        }
        Assert.Contains("\"key\":\"structures.orders\"", Assert.Single(ItemAt(menu, 25).Actions).ParamsJson);

        var storages = menu.Sections.Single(s => s.Name == "Storages");
        Assert.Equal("structures.storages", storages.ContentSourceId);

        var staff = menu.Sections.Single(s => s.Name == "Staff");
        Assert.Equal("knk.admin.world", staff.VisibilityPermission);
        Assert.Equal(new[] { "structures.survey", "structures.dry-run", "structures.outline" },
            staff.Items.OrderBy(i => i.SlotOverride).Select(i => Assert.Single(i.Actions).ActionTypeId));
        Assert.All(staff.Items, i => Assert.Equal("knk.admin.world", i.ActionPermission));
        // Players have no action buttons: every other item only opens menus or goes back.
        var playerActions = menu.Sections.Where(s => s.Name != "Staff").SelectMany(s => s.Items).SelectMany(i => i.Actions)
            .Select(a => a.ActionTypeId).Distinct().OrderBy(a => a);
        Assert.Equal(new[] { "menu.back", "menu.open" }, playerActions);
    }

    [Fact]
    public async Task OrdersList_StaffCanCancelOrRetryWithAConfirm()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var menu = seeded.Single(t => t.Key == MenuTemplateSeed.StructureOrdersMenuKey);
        var grid = menu.Sections.Single(s => s.Name == "Orders");
        Assert.Equal("structures.orders", grid.ContentSourceId);
        var row = Assert.Single(grid.Items, i => i.IsRowTemplate);
        Assert.Equal("knk.admin.world", row.ActionPermission);
        Assert.All(row.Actions, a => Assert.Equal("menu.confirm.request", a.ActionTypeId));
        Assert.Contains(row.Actions, a => a.ParamsJson.Contains("structures.order-cancel") && a.Conditions.Single().ParamsJson.Contains("getCanCancel"));
        Assert.Contains(row.Actions, a => a.ParamsJson.Contains("structures.order-retry") && a.Conditions.Single().ParamsJson.Contains("getCanRetry"));
    }

    [Fact]
    public async Task Hub_HasTheStructuresTile_BehindMenuAvailable()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var hub = seeded.Single(t => t.Key == MenuTemplateSeed.HubMenuKey);
        var tile = ItemAt(hub, MenuTemplateSeed.StructuresHubSlot);
        Assert.Equal(("menu.open", "{\"key\":\"structures.overview\"}"), (Assert.Single(tile.Actions).ActionTypeId, tile.Actions[0].ParamsJson));
        Assert.Equal("menu-available", Assert.Single(tile.Conditions).ConditionTypeId);
        var slots = hub.Sections.SelectMany(s => s.Items).Select(i => i.SlotOverride).ToList();
        Assert.Equal(slots.Count, slots.Distinct().Count());
    }

    [Fact]
    public async Task AHubSeededBefore_GetsTheTileOnce()
    {
        await using var context = NewContext();
        await MenuTemplateSeed.SeedCanonicalAsync(context);
        var hub = (await LoadAsync(context)).Single(t => t.Key == MenuTemplateSeed.HubMenuKey);
        var tile = hub.Sections.SelectMany(s => s.Items).Single(i => i.SlotOverride == MenuTemplateSeed.StructuresHubSlot);
        hub.Sections.Single(s => s.Items.Contains(tile)).Items.Remove(tile);
        context.Remove(tile);
        await context.SaveChangesAsync();

        Assert.True(await MenuTemplateSeed.RepairProductionHubTileAsync(context));
        Assert.False(await MenuTemplateSeed.RepairProductionHubTileAsync(context));
        context.ChangeTracker.Clear();
        var repaired = (await LoadAsync(context)).Single(t => t.Key == MenuTemplateSeed.HubMenuKey);
        Assert.Single(repaired.Sections.SelectMany(s => s.Items), i => i.SlotOverride == MenuTemplateSeed.StructuresHubSlot);
    }
}
