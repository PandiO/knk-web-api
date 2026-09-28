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
/// Teleport menu (docs/specs/teleport/DESIGN.md §3.8, KNG-17 Phase 6): the
/// <c>teleport.destinations</c> seed and the hub's Teleport tile. The plugin-side contract (root,
/// row source, actions, row getters) is checked in knk-paper against the exported content-seeds.json.
/// </summary>
public class MenuTemplateTeleportSeedTests
{
    private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<MenuMappingProfile>()).CreateMapper();

    private static async Task<(KnKDbContext Context, List<MenuTemplate> Seeded)> SeedTwiceAsync()
    {
        var options = new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase($"MenuTeleportSeed_{Guid.NewGuid()}")
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

    [Fact]
    public async Task Seed_CreatesTheMenuOnce_AndTheApiWouldAcceptIt()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var template = Assert.Single(seeded, t => t.Key == MenuTemplateSeed.TeleportMenuKey);

        var repo = new Mock<IMenuTemplateRepository>();
        repo.Setup(r => r.GetByKeyAsync(It.IsAny<string>())).ReturnsAsync((MenuTemplate?)null);
        repo.Setup(r => r.AddAsync(It.IsAny<MenuTemplate>())).Returns(Task.CompletedTask);
        var service = new MenuTemplateService(repo.Object, new Mock<IMinecraftMaterialRefRepository>().Object, _mapper);

        var created = await service.CreateAsync(_mapper.Map<MenuTemplateDto>(template));

        Assert.Equal(MenuTemplateSeed.TeleportMenuKey, created.Key);
        Assert.Equal((6, 3, MenuGrowthMode.Dynamic), (template.Height, template.MinHeight!.Value, template.Growth));
    }

    [Fact]
    public async Task Header_HasSpawnRequestsTheInfoCompassWithTheViewersWarmupAndBack()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var menu = seeded.Single(t => t.Key == MenuTemplateSeed.TeleportMenuKey);

        var spawn = ItemAt(menu, 0);
        var toSpawn = Assert.Single(spawn.Actions);
        Assert.Equal(("teleport.spawn", "{}"), (toSpawn.ActionTypeId, toSpawn.ParamsJson));
        Assert.Equal(new[] { "$teleport.getSpawnDisplayMode$" }, Expressions(spawn, "DisplayMode"));

        var requests = ItemAt(menu, 2);
        Assert.Equal("teleport.requests", Assert.Single(requests.Actions).ActionTypeId);
        Assert.Equal(new[] { "$teleport.getRequestLines$" }, Expressions(requests, "Lore"));
        Assert.Equal(new[] { "$teleport.getRequestsDisplayMode$" }, Expressions(requests, "DisplayMode"));

        var info = ItemAt(menu, 4);
        Assert.Equal(new[] { "COMPASS" }, Expressions(info, "Material"));
        Assert.Equal(new[] { "$teleport.getWarmupLine$", "&7The prices are paid in gems!", "&7Moving or taking damage cancels it" },
            Expressions(info, "Lore"));
        Assert.Empty(info.Actions);

        Assert.Equal("menu.back", Assert.Single(ItemAt(menu, 8).Actions).ActionTypeId);
    }

    [Fact]
    public async Task Destinations_ArePagedRowsWhoseClickWarpsByDomainId()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var menu = seeded.Single(t => t.Key == MenuTemplateSeed.TeleportMenuKey);
        var grid = menu.Sections.Single(s => s.Name == "Destinations");

        Assert.Equal("teleport.destinations", grid.ContentSourceId);
        Assert.Equal((9, 9, 4), (grid.DisplaySlot, grid.Width, grid.Height));
        Assert.Equal(MenuOverflowMode.Scroll, grid.Overflow);

        var row = Assert.Single(grid.Items, i => i.IsRowTemplate);
        Assert.Equal(new[] { "$row.getMaterial$" }, Expressions(row, "Material"));
        Assert.Equal(new[] { "$row.getDisplayMode$" }, Expressions(row, "DisplayMode"));
        Assert.Equal(new[] { "$row.getName$" }, Expressions(row, "Name"));
        Assert.Equal(new[] { "$row.getLoreLines$" }, Expressions(row, "Lore"));
        var warp = Assert.Single(row.Actions);
        Assert.Equal(("teleport.warp", "{\"domainId\":\"$row.getDomainId$\"}"), (warp.ActionTypeId, warp.ParamsJson));
        Assert.Empty(row.Conditions);

        var pinned = grid.Items.Where(i => !i.IsRowTemplate).OrderBy(i => i.SlotOverride).Select(i => i.SlotOverride!.Value);
        Assert.Equal(new[] { 45, 53 }, pinned);
        Assert.Equal("menu.page.prev", Assert.Single(ItemAt(menu, 45).Actions).ActionTypeId);
        Assert.Equal("menu.page.next", Assert.Single(ItemAt(menu, 53).Actions).ActionTypeId);
    }

    [Fact]
    public async Task Hub_HasTheTeleportTileInSlot24BehindMenuAvailable()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var hub = seeded.Single(t => t.Key == MenuTemplateSeed.HubMenuKey);
        var tile = ItemAt(hub, 24);

        Assert.Equal(new[] { "COMPASS" }, Expressions(tile, "Material"));
        Assert.Equal(new[] { "&aTeleport to points on the map" }, Expressions(tile, "Name"));
        Assert.Equal(new[] { "&7Teleport to important points in the world", "&7Price will be paid in gems!" },
            Expressions(tile, "Lore"));
        var open = Assert.Single(tile.Actions);
        Assert.Equal(("menu.open", "{\"key\":\"teleport.destinations\"}"), (open.ActionTypeId, open.ParamsJson));
        var available = Assert.Single(tile.Conditions);
        Assert.Equal(("menu-available", MenuConditionPhase.Render, "{\"key\":\"teleport.destinations\"}"),
            (available.ConditionTypeId, available.Phase, available.ParamsJson));
        Assert.Null(tile.VisibilityPermission);

        // Slot 20 stays Discoveries; every hub tile has its own slot.
        Assert.Equal("{\"key\":\"discoveries.main\"}", Assert.Single(ItemAt(hub, 20).Actions).ParamsJson);
        var slots = hub.Sections.SelectMany(s => s.Items).Select(i => i.SlotOverride).ToList();
        Assert.Equal(slots.Count, slots.Distinct().Count());
    }
}
