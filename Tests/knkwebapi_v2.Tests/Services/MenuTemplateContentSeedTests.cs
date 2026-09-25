using System.Text.Json;
using System.Text.Json.Serialization;
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
/// InventoryMenu content port (docs/specs/inventory-menu/CONTENT_PORT_PLAN.md CP1-CP8):
/// the content seeds are create-only and every one of them must be a template the CRUD API
/// itself would accept - same pattern as <see cref="MenuTemplateServicePhase9Tests"/>.
/// </summary>
public class MenuTemplateContentSeedTests
{
    private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<MenuMappingProfile>()).CreateMapper();

    private static async Task<(KnKDbContext Context, List<MenuTemplate> Seeded)> SeedTwiceAsync()
    {
        var options = new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase($"MenuContentSeed_{Guid.NewGuid()}")
            .Options;
        var context = new KnKDbContext(options);

        await MenuTemplateSeed.SeedCanonicalAsync(context);
        // Create-only: a second run must not duplicate anything.
        await MenuTemplateSeed.SeedCanonicalAsync(context);

        var seeded = await context.MenuTemplates
            .Include(t => t.Sections).ThenInclude(s => s.Items).ThenInclude(i => i.VariableBindings)
            .Include(t => t.Sections).ThenInclude(s => s.Items).ThenInclude(i => i.Actions).ThenInclude(a => a.Conditions)
            .Include(t => t.Sections).ThenInclude(s => s.Items).ThenInclude(i => i.Conditions)
            .Include(t => t.Sections).ThenInclude(s => s.VariableBindings)
            .ToListAsync();
        return (context, seeded);
    }

    private static MenuTemplate Single(List<MenuTemplate> seeded, string key) => seeded.Single(t => t.Key == key);

    private static MenuItemTemplate ItemAt(MenuTemplate template, int slot) =>
        template.Sections.SelectMany(s => s.Items).Single(i => i.SlotOverride == slot);

    private static string? Binding(MenuItemTemplate item, string property) =>
        item.VariableBindings.FirstOrDefault(b => b.TargetProperty == property)?.Expression;

    /// <summary>Every content-port template key, in seed order. Add each new template here.</summary>
    private static readonly string[] ContentKeyList =
    {
        MenuTemplateSeed.HubMenuKey,
    };

    public static TheoryData<string> ContentKeys()
    {
        var data = new TheoryData<string>();
        foreach (var key in ContentKeyList) data.Add(key);
        return data;
    }

    [Theory]
    [MemberData(nameof(ContentKeys))]
    public async Task Seed_CreatesEachContentTemplateOnce_AndItPassesServiceValidation(string key)
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;

        Assert.Single(seeded, t => t.Key == key);

        // The seed bypasses the service, so round-trip the template through the real mapping
        // profile and the service's create-path validation - proves the API would accept it.
        var repo = new Mock<IMenuTemplateRepository>();
        repo.Setup(r => r.GetByKeyAsync(It.IsAny<string>())).ReturnsAsync((MenuTemplate?)null);
        repo.Setup(r => r.AddAsync(It.IsAny<MenuTemplate>())).Returns(Task.CompletedTask);
        var service = new MenuTemplateService(repo.Object, new Mock<IMinecraftMaterialRefRepository>().Object, _mapper);

        var dto = _mapper.Map<MenuTemplateDto>(Single(seeded, key));
        var created = await service.CreateAsync(dto);

        Assert.Equal(key, created.Key);
    }

    [Fact]
    public async Task Hub_ShipsEveryTileBehindMenuAvailable_AndTheManagerTileBehindItsPermission()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var hub = Single(seeded, MenuTemplateSeed.HubMenuKey);

        Assert.Equal(3, hub.Height);
        var expectedTargets = new Dictionary<int, string>
        {
            [4] = MenuTemplateSeed.ProfileMenuKey,
            [10] = MenuTemplateSeed.KitsOverviewMenuKey,
            [12] = MenuTemplateSeed.SiegeOverviewMenuKey,
            [14] = MenuTemplateSeed.ItemsCatalogMenuKey,
            [16] = MenuTemplateSeed.PremiumTiersMenuKey,
            [22] = MenuTemplateSeed.UserManagerMenuKey,
        };
        foreach (var (slot, target) in expectedTargets)
        {
            var tile = ItemAt(hub, slot);
            var open = Assert.Single(tile.Actions);
            Assert.Equal("menu.open", open.ActionTypeId);
            Assert.Equal("{\"key\":\"" + target + "\"}", open.ParamsJson);
            var available = Assert.Single(tile.Conditions);
            Assert.Equal("menu-available", available.ConditionTypeId);
            Assert.Equal(MenuConditionPhase.Render, available.Phase);
            Assert.Equal("{\"key\":\"" + target + "\"}", available.ParamsJson);
        }

        var manager = ItemAt(hub, 22);
        Assert.Equal(MenuTemplateSeed.UserManagePermission, manager.VisibilityPermission);
        Assert.Equal(MenuTemplateSeed.UserManagePermission, manager.ActionPermission);
        Assert.All(expectedTargets.Keys.Where(s => s != 22), s => Assert.Null(ItemAt(hub, s).VisibilityPermission));

        var head = ItemAt(hub, 4);
        Assert.Equal("$player.getName$", Binding(head, "SkullOwner"));

        var back = ItemAt(hub, 8);
        Assert.Equal("menu.back", Assert.Single(back.Actions).ActionTypeId);
        Assert.Equal("&c$menu.getBackLabel$", Binding(back, "Name"));
    }

    /// <summary>
    /// Writes every content template exactly as <c>GET /api/MenuTemplates/by-key/{key}</c> would
    /// return it (mapping profile + the API's JSON options) - the fixture knk-plugin's
    /// <c>ContentSeedContractTest</c> validates against the registered menu features. Always
    /// checks the export round-trips; writes the file only when
    /// <c>KNK_MENU_CONTENT_SEED_EXPORT</c> names a path (regenerate after changing a content seed:
    /// <c>KNK_MENU_CONTENT_SEED_EXPORT=../knk-plugin/knk-paper/src/test/resources/menu/content-seeds.json
    /// dotnet test ... --filter ContentSeeds_ExportAsApiJson</c>).
    /// </summary>
    [Fact]
    public async Task ContentSeeds_ExportAsApiJson()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;

        var keys = ContentKeyList.ToList();
        var dtos = keys.Select(key => _mapper.Map<MenuTemplateDto>(Single(seeded, key))).ToList();
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        var json = JsonSerializer.Serialize(dtos, options);

        var roundTripped = JsonSerializer.Deserialize<List<MenuTemplateDto>>(json, options)!;
        Assert.Equal(keys, roundTripped.Select(t => t.Key).ToList());

        var exportPath = Environment.GetEnvironmentVariable("KNK_MENU_CONTENT_SEED_EXPORT");
        if (!string.IsNullOrWhiteSpace(exportPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(exportPath))!);
            await File.WriteAllTextAsync(exportPath, json + "\n");
        }
    }
}
