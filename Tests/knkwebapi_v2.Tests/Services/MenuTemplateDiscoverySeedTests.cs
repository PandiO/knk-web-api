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
/// Domain discovery (docs/specs/domain-discovery/DESIGN.md §3.7): the <c>discoveries.main</c>
/// seed and the hub's Discoveries tile. The plugin-side contract (root, row source, row getters)
/// is checked in knk-paper against the exported content-seeds.json.
/// </summary>
public class MenuTemplateDiscoverySeedTests
{
    private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<MenuMappingProfile>()).CreateMapper();

    private static async Task<(KnKDbContext Context, List<MenuTemplate> Seeded)> SeedTwiceAsync()
    {
        var options = new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase($"MenuDiscoverySeed_{Guid.NewGuid()}")
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
        var template = Assert.Single(seeded, t => t.Key == MenuTemplateSeed.DiscoveriesMenuKey);

        var repo = new Mock<IMenuTemplateRepository>();
        repo.Setup(r => r.GetByKeyAsync(It.IsAny<string>())).ReturnsAsync((MenuTemplate?)null);
        repo.Setup(r => r.AddAsync(It.IsAny<MenuTemplate>())).Returns(Task.CompletedTask);
        var service = new MenuTemplateService(repo.Object, new Mock<IMinecraftMaterialRefRepository>().Object, _mapper);

        var created = await service.CreateAsync(_mapper.Map<MenuTemplateDto>(template));

        Assert.Equal(MenuTemplateSeed.DiscoveriesMenuKey, created.Key);
        Assert.Equal((6, 3, MenuGrowthMode.Dynamic), (template.Height, template.MinHeight!.Value, template.Growth));
    }

    [Fact]
    public async Task Header_ShowsTheViewersCountsTheKnowledgeBookAndBack()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var menu = seeded.Single(t => t.Key == MenuTemplateSeed.DiscoveriesMenuKey);

        var head = ItemAt(menu, 0);
        Assert.Equal(new[] { "$player.getName$" }, Expressions(head, "SkullOwner"));
        Assert.Equal(new[] { "$discoveries.getSummaryLines$" }, Expressions(head, "Lore"));

        var book = ItemAt(menu, 4);
        Assert.Equal(new[] { "BOOK" }, Expressions(book, "Material"));
        Assert.Equal(new[] { "&eKnowledge" }, Expressions(book, "Name"));
        Assert.Equal(new[] { "&7See all discovered places", "&7Latest discovered: &f$discoveries.getLatestName$", "$discoveries.getRewardsLine$" },
            Expressions(book, "Lore"));
        Assert.Empty(book.Actions);

        Assert.Equal("menu.back", Assert.Single(ItemAt(menu, 8).Actions).ActionTypeId);
    }

    [Fact]
    public async Task Places_AreReadOnlyRowsFromTheDiscoveriesSource()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var grid = seeded.Single(t => t.Key == MenuTemplateSeed.DiscoveriesMenuKey).Sections.Single(s => s.Name == "Places");

        Assert.Equal("discoveries.rows", grid.ContentSourceId);
        Assert.Equal((18, 9, 3), (grid.DisplaySlot, grid.Width, grid.Height));
        Assert.True(grid.Searchable, "filters only reach a content source on a searchable section");
        Assert.Equal(MenuOverflowMode.Scroll, grid.Overflow);

        var row = Assert.Single(grid.Items, i => i.IsRowTemplate);
        Assert.Empty(row.Actions);
        Assert.Equal(new[] { "$row.getMaterial$" }, Expressions(row, "Material"));
        Assert.Equal(new[] { "$row.getDisplayMode$" }, Expressions(row, "DisplayMode"));
        Assert.Equal(new[] { "$row.getName$" }, Expressions(row, "Name"));
        Assert.Equal(new[] { "$row.getLoreLines$" }, Expressions(row, "Lore"));
    }

    [Fact]
    public async Task Controls_PageAndFilterByTypeAndStatus()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var menu = seeded.Single(t => t.Key == MenuTemplateSeed.DiscoveriesMenuKey);
        var grid = menu.Sections.Single(s => s.Name == "Places");

        var pinned = grid.Items.Where(i => !i.IsRowTemplate).OrderBy(i => i.SlotOverride).Select(i => i.SlotOverride!.Value);
        Assert.Equal(new[] { 45, 47, 49, 51, 53 }, pinned);
        Assert.Equal("menu.page.prev", Assert.Single(ItemAt(menu, 45).Actions).ActionTypeId);
        Assert.Equal("menu.page.next", Assert.Single(ItemAt(menu, 53).Actions).ActionTypeId);

        using (var type = JsonDocument.Parse(Assert.Single(ItemAt(menu, 47).Actions, a => a.ActionTypeId == "menu.filter.cycle").ParamsJson))
        {
            Assert.Equal("DomainType", type.RootElement.GetProperty("facetKey").GetString());
            Assert.Equal("Town,District,Structure,GateStructure", type.RootElement.GetProperty("values").GetString());
        }
        using (var status = JsonDocument.Parse(Assert.Single(ItemAt(menu, 49).Actions, a => a.ActionTypeId == "menu.filter.cycle").ParamsJson))
        {
            Assert.Equal("Status", status.RootElement.GetProperty("facetKey").GetString());
            Assert.Equal("Discovered,Undiscovered", status.RootElement.GetProperty("values").GetString());
        }

        var clears = ItemAt(menu, 51).Actions.OrderBy(a => a.SortOrder).ToList();
        Assert.All(clears, a => Assert.Equal("menu.filter.clear", a.ActionTypeId));
        Assert.Equal(new[] { "{\"facetKey\":\"DomainType\"}", "{\"facetKey\":\"Status\"}" }, clears.Select(a => a.ParamsJson));
    }

    [Fact]
    public async Task Hub_HasTheDiscoveriesTileInSlot20BehindMenuAvailable()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var tile = ItemAt(seeded.Single(t => t.Key == MenuTemplateSeed.HubMenuKey), 20);

        Assert.Equal(new[] { "BOOK" }, Expressions(tile, "Material"));
        Assert.Equal(new[] { "&eDiscoveries" }, Expressions(tile, "Name"));
        Assert.Equal(new[] { "&7See all discovered places", "$discoveries.getCountLine$" }, Expressions(tile, "Lore"));
        var open = Assert.Single(tile.Actions);
        Assert.Equal(("menu.open", "{\"key\":\"discoveries.main\"}"), (open.ActionTypeId, open.ParamsJson));
        var available = Assert.Single(tile.Conditions);
        Assert.Equal(("menu-available", MenuConditionPhase.Render, "{\"key\":\"discoveries.main\"}"),
            (available.ConditionTypeId, available.Phase, available.ParamsJson));
        Assert.Null(tile.VisibilityPermission);
    }
}
