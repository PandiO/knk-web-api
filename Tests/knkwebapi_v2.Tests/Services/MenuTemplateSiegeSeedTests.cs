using System.Text.Json;
using System.Text.Json.Serialization;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Siege Phase 8b (docs/specs/siege-minigame/MENU_TEMPLATES.md Part C): the create-only siege menu
/// seeds - each passes the API's create-path validation, the key wiring matches the plugin's
/// SiegeMenuFeature ids, and the export feeds knk-api-client's SiegeMenuSeedContractTest.
/// </summary>
public class MenuTemplateSiegeSeedTests
{
    private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<MenuMappingProfile>()).CreateMapper();

    private static readonly string[] SiegeKeys =
    {
        MenuTemplateSeed.SiegeOverviewMenuKey,
        MenuTemplateSeed.SiegeInformationMenuKey,
        MenuTemplateSeed.SiegeSpawnpointMenuKey,
    };

    private static async Task<(KnKDbContext Context, List<MenuTemplate> Seeded)> SeedTwiceAsync()
    {
        var context = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase($"MenuSiegeSeed_{Guid.NewGuid()}").Options);
        await MenuTemplateSeed.SeedCanonicalAsync(context);
        await MenuTemplateSeed.SeedCanonicalAsync(context);
        var seeded = await context.MenuTemplates
            .Include(t => t.Sections).ThenInclude(s => s.Items).ThenInclude(i => i.VariableBindings)
            .Include(t => t.Sections).ThenInclude(s => s.Items).ThenInclude(i => i.Actions).ThenInclude(a => a.Conditions)
            .Include(t => t.Sections).ThenInclude(s => s.Items).ThenInclude(i => i.Conditions)
            .Include(t => t.Sections).ThenInclude(s => s.VariableBindings)
            .ToListAsync();
        return (context, seeded);
    }

    public static TheoryData<string> Keys()
    {
        var data = new TheoryData<string>();
        foreach (var key in SiegeKeys) data.Add(key);
        return data;
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task Seed_CreatesEachSiegeTemplateOnce_AndItPassesServiceValidation(string key)
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;

        Assert.Single(seeded, t => t.Key == key);

        var repo = new Mock<IMenuTemplateRepository>();
        repo.Setup(r => r.GetByKeyAsync(It.IsAny<string>())).ReturnsAsync((MenuTemplate?)null);
        repo.Setup(r => r.AddAsync(It.IsAny<MenuTemplate>())).Returns(Task.CompletedTask);
        var service = new MenuTemplateService(repo.Object, new Mock<IMinecraftMaterialRefRepository>().Object, _mapper);

        var created = await service.CreateAsync(_mapper.Map<MenuTemplateDto>(seeded.Single(t => t.Key == key)));

        Assert.Equal(key, created.Key);
    }

    [Fact]
    public async Task Overview_ShrinksToItsContent_AndLobbiesInCooldownDontOpen()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var overview = seeded.Single(t => t.Key == MenuTemplateSeed.SiegeOverviewMenuKey);

        Assert.Equal((MenuGrowthMode.Dynamic, 2), (overview.Growth, overview.MinHeight));
        var row = overview.Sections.Single(s => s.Name == "Sieges").Items.Single(i => i.IsRowTemplate);
        var guard = Assert.Single(row.Conditions, c => c.ConditionTypeId == "siege.lobby-open");
        Assert.Equal(MenuConditionPhase.Click, guard.Phase);
        Assert.Contains("$row.getLobbyId$", guard.ParamsJson);
    }

    [Fact]
    public async Task Information_WiresTheLobbyContext_JoinLeaveAndThePhaseAwareBody()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var info = seeded.Single(t => t.Key == MenuTemplateSeed.SiegeInformationMenuKey);

        Assert.Equal(6, info.Height);
        Assert.Equal((MenuGrowthMode.Dynamic, 3), (info.Growth, info.MinHeight)); // shrinks to its content
        Assert.Equal(20, info.AutoRefreshTicks);
        var body = info.Sections.Single(s => s.Name == "Body");
        Assert.Equal("siege.body", body.ContentSourceId);
        Assert.Equal("{\"lobbyId\":\"$ctx.lobbyId$\"}", body.ContentSourceParamsJson);
        Assert.Single(body.Items, i => i.IsRowTemplate);

        var joinLeave = info.Sections.SelectMany(s => s.Items).Single(i => i.SlotOverride == 14);
        Assert.Equal(new[] { "siege.join", "menu.confirm.doubleclick" }, joinLeave.Actions.OrderBy(a => a.SortOrder).Select(a => a.ActionTypeId));
        var join = joinLeave.Actions.Single(a => a.ActionTypeId == "siege.join");
        Assert.Contains(join.Conditions, c => c.ConditionTypeId == "siege.join-eligible" && c.Phase == MenuConditionPhase.Click);
        Assert.Contains(joinLeave.Conditions, c => c.ConditionTypeId == "siege.phase" && c.Phase == MenuConditionPhase.Render);

        var votes = info.Sections.Single(s => s.Name == "Votes");
        Assert.Equal("siege.vote-candidates", votes.ContentSourceId);
        var vote = Assert.Single(votes.Items).Actions.Single();
        Assert.Equal("siege.vote", vote.ActionTypeId);
        Assert.Contains("$row.getChoiceKey$", vote.ParamsJson);
    }

    [Fact]
    public async Task Overview_RowsOpenTheirInformation_WithTheLobbyAsContext()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;
        var overview = seeded.Single(t => t.Key == MenuTemplateSeed.SiegeOverviewMenuKey);

        var grid = overview.Sections.Single(s => s.Name == "Sieges");
        Assert.Equal("siege.lobbies", grid.ContentSourceId);
        var open = grid.Items.Single(i => i.IsRowTemplate).Actions.Single();
        Assert.Equal("menu.open", open.ActionTypeId);
        Assert.Equal("{\"key\":\"siege.information\",\"ctx.lobbyId\":\"$row.getLobbyId$\"}", open.ParamsJson);
        var empty = grid.Items.Single(i => i.SlotOverride == 22);
        Assert.Equal("siege.lobbies-empty", Assert.Single(empty.Conditions).ConditionTypeId);
    }

    /// <summary>
    /// Writes the siege templates as <c>GET /api/MenuTemplates/by-key/{key}</c> returns them (the
    /// fixture of knk-api-client's <c>SiegeMenuSeedContractTest</c>). Always checks the round trip;
    /// writes the file only when <c>KNK_SIEGE_MENU_SEED_EXPORT</c> names a path (regenerate after a
    /// change: <c>KNK_SIEGE_MENU_SEED_EXPORT=../knk-plugin/knk-api-client/src/test/resources/menu/siege-seeds.json
    /// dotnet test ... --filter SiegeSeeds_ExportAsApiJson</c>).
    /// </summary>
    [Fact]
    public async Task SiegeSeeds_ExportAsApiJson()
    {
        var (context, seeded) = await SeedTwiceAsync();
        await using var _ = context;

        var dtos = SiegeKeys.Select(key => _mapper.Map<MenuTemplateDto>(seeded.Single(t => t.Key == key))).ToList();
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        var json = JsonSerializer.Serialize(dtos, options);
        var roundTripped = JsonSerializer.Deserialize<List<MenuTemplateDto>>(json, options)!;
        Assert.Equal(SiegeKeys, roundTripped.Select(t => t.Key));

        var exportPath = Environment.GetEnvironmentVariable("KNK_SIEGE_MENU_SEED_EXPORT");
        if (!string.IsNullOrWhiteSpace(exportPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(exportPath))!);
            await File.WriteAllTextAsync(exportPath, json + "\n");
        }
    }
}
