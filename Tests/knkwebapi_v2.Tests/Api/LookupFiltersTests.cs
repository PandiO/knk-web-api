using System.Reflection;
using System.Text.Json;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Tests.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// PandiO/knk-workspace#288 (developer decision 2026-11-15): <c>POST api/Lookup/{type}/search</c> passes the relation
/// field's narrowing <c>pickerFilters</c> to the type's own search where the type's lookup takes them, so a picker in
/// lookup mode gets the same narrowed list as the full search. Results stay <c>{ id, label, detail? }</c>. Any other
/// filter, or a value the search would skip, answers 400 rather than a wider list; <c>prefer…</c> keys only order.
/// </summary>
public class LookupFiltersTests : IAsyncLifetime
{
    private KnKDbContext _context = null!;
    private readonly Mock<IUserService> _users = new();
    private readonly Mock<IClanService> _clans = new();
    private readonly Mock<IDistrictService> _districts = new();
    private readonly Mock<IPermissionGroupService> _groups = new();

    public async Task InitializeAsync()
    {
        _context = SiegeTestData.NewContext("LookupFilters");
        await SiegeTestData.SeedValidScenarioAsync(_context);
        // A second scenario with its own team: a siege team picker scoped to scenario 100 must not offer it.
        _context.SiegeScenarios.Add(new SiegeScenario
        {
            Id = 101, Name = "Siege of Cinix II", TownId = 1, HubLocationId = 1000,
            Teams = { new SiegeTeam { Id = 203, SortOrder = 0, Role = SiegeTeamRole.Attacker, AllianceGroup = 2, Name = "Rebels" } }
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    private LookupController Controller()
    {
        var siege = new SiegeScenarioService(new SiegeScenarioRepository(_context), new Mock<ILocationService>().Object,
            SiegeTestData.NoRules(), SiegeTestData.Mapper());
        var services = new ServiceCollection()
            .AddSingleton<ISiegeScenarioService>(siege)
            .AddSingleton(_users.Object).AddSingleton(_clans.Object).AddSingleton(_districts.Object).AddSingleton(_groups.Object)
            .BuildServiceProvider();
        return new LookupController(new LookupService(services));
    }

    private static LookupSearchRequestDto Request(params (string Key, string? Value)[] filters) => new()
    {
        Filters = filters.ToDictionary(f => f.Key, f => f.Value),
    };

    private static JsonElement Ok(IActionResult result) =>
        JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(result).Value);

    private static JsonElement BadRequest(IActionResult result) =>
        JsonSerializer.SerializeToElement(Assert.IsType<BadRequestObjectResult>(result).Value);

    private static int[] Ids(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt32()).ToArray();

    private static PagedResultDto<T> Empty<T>() => new() { Items = new(), TotalCount = 0 };

    [Fact]
    public async Task SiegeTeams_NarrowedBySiegeScenarioId_ListOnlyThatScenariosTeams()
    {
        var all = Ok(await Controller().Search("SiegeTeam", new LookupSearchRequestDto()));
        var scoped = Ok(await Controller().Search("SiegeTeam", Request(("siegeScenarioId", "100"))));

        Assert.Equal(new[] { 201, 202, 203 }, Ids(all));
        Assert.Equal(new[] { 201, 202 }, Ids(scoped));
        Assert.Equal(2, scoped.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task FilteredResults_StayLabelsOnly()
    {
        var scoped = Ok(await Controller().Search("SiegeTeam", Request(("siegeScenarioId", "100"))));

        Assert.Equal(new[] { "items", "total" }, scoped.EnumerateObject().Select(p => p.Name).ToArray());
        foreach (var item in scoped.GetProperty("items").EnumerateArray())
        {
            Assert.All(item.EnumerateObject(), p => Assert.Contains(p.Name, new[] { "id", "label", "detail" }));
        }
        var raiders = scoped.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetInt32() == 202);
        Assert.Equal("Raiders", raiders.GetProperty("label").GetString());
        Assert.Equal("Attacker", raiders.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task FilterKeys_MatchWithoutCase_AndReachTheSearchInItsOwnSpelling()
    {
        PagedQueryDto? sent = null;
        _groups.Setup(s => s.SearchAsync(It.IsAny<PagedQueryDto>()))
            .Callback<PagedQueryDto>(q => sent = q)
            .ReturnsAsync(Empty<PermissionGroupListDto>());

        Ok(await Controller().Search("PermissionGroup", Request(("ISPREMIUMTIER", " True "), ("ParentGroupId", "07"))));

        Assert.Equal(new Dictionary<string, string> { ["isPremiumTier"] = "true", ["parentGroupId"] = "7" }, sent!.Filters);

        var scoped = Ok(await Controller().Search("SiegeTeam", Request(("SIEGESCENARIOID", "101"))));
        Assert.Equal(new[] { 203 }, Ids(scoped));
    }

    [Fact]
    public async Task AFilterTheLookupDoesNotTake_Is400_AndTheSearchDoesNotRun()
    {
        // A user's lookup takes no filters: an email filter would let a lookup find a user by email (never allowed).
        var email = BadRequest(await Controller().Search("User", Request(("email", "steve@example.com"))));
        Assert.Equal(LookupFilterException.NotAllowed, email.GetProperty("error").GetString());
        Assert.Equal(new[] { "email" }, email.GetProperty("filters").EnumerateArray().Select(f => f.GetString()).ToArray());
        Assert.Contains("only the full User search", email.GetProperty("message").GetString());
        _users.Verify(s => s.SearchAsync(It.IsAny<PagedQueryDto>()), Times.Never);

        // A key the type's own search doesn't read at all, next to one it does: the whole request is refused.
        var unknown = BadRequest(await Controller().Search("SiegeTeam", Request(("siegeScenarioId", "100"), ("townId", "1"))));
        Assert.Equal(new[] { "townId" }, unknown.GetProperty("filters").EnumerateArray().Select(f => f.GetString()).ToArray());
    }

    [Fact]
    public async Task ExcludeIds_IsNotALookupFilter()
    {
        // The picker keeps applying excludeIds in the browser (agent decision, #288).
        var refused = BadRequest(await Controller().Search("District", Request(("excludeIds", "3,4"))));
        Assert.Equal(LookupFilterException.NotAllowed, refused.GetProperty("error").GetString());
        _districts.Verify(s => s.SearchAsync(It.IsAny<PagedQueryDto>()), Times.Never);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1.5")]
    public async Task AnIdFilterThatIsNotAPositiveNumber_Is400_NeverTheWholeList(string? value)
    {
        // The searches skip a non-positive or non-numeric id, so passing it on would list every team.
        var refused = BadRequest(await Controller().Search("SiegeTeam", Request(("siegeScenarioId", value))));

        Assert.Equal(LookupFilterException.Invalid, refused.GetProperty("error").GetString());
        Assert.Equal(new[] { "siegeScenarioId" }, refused.GetProperty("filters").EnumerateArray().Select(f => f.GetString()).ToArray());
        Assert.Contains("record id", refused.GetProperty("message").GetString());
    }

    [Fact]
    public async Task AFlagFilterThatIsNotTrueOrFalse_Is400()
    {
        var refused = BadRequest(await Controller().Search("PermissionGroup", Request(("isPremiumTier", "yes"))));

        Assert.Equal(LookupFilterException.Invalid, refused.GetProperty("error").GetString());
        Assert.Contains("true or false", refused.GetProperty("message").GetString());
        _groups.Verify(s => s.SearchAsync(It.IsAny<PagedQueryDto>()), Times.Never);
    }

    [Fact]
    public async Task PreferFilters_OrderWhereTheSearchTakesThem_AndAreLeftOutOtherwise()
    {
        var clanQueries = new List<PagedQueryDto>();
        _clans.Setup(s => s.SearchAsync(It.IsAny<PagedQueryDto>()))
            .Callback<PagedQueryDto>(q => clanQueries.Add(q))
            .ReturnsAsync(Empty<ClanListDto>());
        PagedQueryDto? districtQuery = null;
        _districts.Setup(s => s.SearchAsync(It.IsAny<PagedQueryDto>()))
            .Callback<PagedQueryDto>(q => districtQuery = q)
            .ReturnsAsync(Empty<DistrictListDto>());

        Ok(await Controller().Search("Clan", Request(("preferTownId", "5"))));
        Ok(await Controller().Search("Clan", Request(("preferTownId", "-1"))));
        Ok(await Controller().Search("District", Request(("townId", "1"), ("preferStreetId", "9"))));

        Assert.Equal("5", clanQueries[0].Filters!["preferTownId"]);
        Assert.Null(clanQueries[1].Filters);
        Assert.Equal(new Dictionary<string, string> { ["townId"] = "1" }, districtQuery!.Filters);
    }

    [Fact]
    public async Task UsersStillSearchByUsernameOnly_WithNoFilters()
    {
        PagedQueryDto? sent = null;
        _users.Setup(s => s.SearchAsync(It.IsAny<PagedQueryDto>()))
            .Callback<PagedQueryDto>(q => sent = q)
            .ReturnsAsync(Empty<UserListDto>());

        Ok(await Controller().Search("User", new LookupSearchRequestDto { SearchTerm = "ste", Filters = new() }));

        Assert.Equal(new Dictionary<string, string> { ["username"] = "ste" }, sent!.Filters);
    }

    /// <summary>
    /// The lookup filters per type, pinned so a change is deliberate. Each key is one the type's own search reads
    /// (its repository's <c>Filters</c> lookup, same spelling) and narrows by a parent or a flag.
    /// </summary>
    [Fact]
    public void LookupFilters_ArePinnedPerType_AndNeverOnUsers()
    {
        var expected = new Dictionary<string, string>
        {
            ["Category"] = "parentCategoryId:Id",
            ["ItemBlueprint"] = "CategoryId:Id",
            ["Clan"] = "preferTownId:Prefer",
            ["CrownLand"] = "kingdomId:Id",
            ["District"] = "townId:Id",
            ["Structure"] = "streetId:Id, districtId:Id, townId:Id",
            ["Warehouse"] = "districtId:Id, townId:Id, acceptsDeliveries:Flag", // production world P2 (#91)
            ["ProductionStructure"] = "districtId:Id, townId:Id, productionKindId:Id",
            ["ProductionKindLevel"] = "productionKindId:Id", // production engine P3 (#86)
            ["ProductionLine"] = "productionStructureId:Id, itemBlueprintId:Id",
            ["ProductionArea"] = "productionStructureId:Id",
            ["ProductionCycleRecord"] = "productionStructureId:Id, productionLineId:Id", // #838
            ["TransportOrder"] = "sourceStructureId:Id, structureId:Id", // transport core P5 (#79)
            ["TransportLane"] = "structureId:Id",
            ["LogisticsPolicy"] = "townId:Id, warehouseId:Id", // town logistics P9 (#127)
            ["LogisticsRule"] = "logisticsPolicyId:Id",
            ["ConsumerReplenishment"] = "structureId:Id, storageId:Id",
            ["LogisticsReservation"] = "townId:Id, consumerStructureId:Id",
            ["LogisticsPlannerRun"] = "townId:Id",
            ["ShopTypeLevel"] = "shopTypeId:Id", // shops P10 (#248)
            ["Shop"] = "townId:Id, districtId:Id, shopTypeId:Id",
            ["ShopCatalogueEntry"] = "shopId:Id, itemBlueprintId:Id",
            ["ShopTradeStat"] = "shopId:Id, itemBlueprintId:Id",
            ["TownAccount"] = "townId:Id",
            ["StorageContainer"] = "storageId:Id",
            ["ProductionYieldOverride"] = "productionStructureId:Id, itemBlueprintId:Id",
            ["HarvestRule"] = "productionKindId:Id", // player harvesting P8a (#685)
            ["ResourcePool"] = "productionStructureId:Id, productionAreaId:Id",
            ["PlayerHarvest"] = "productionStructureId:Id, harvestRuleId:Id",
            ["GateStructure"] = "streetId:Id, districtId:Id, townId:Id, siegeScenarioId:Id, isActive:Flag, isOpened:Flag",
            ["GateDoor"] = "gateStructureId:Id, isActive:Flag",
            ["Landmark"] = "domainId:Id",
            ["Province"] = "kingdomId:Id",
            ["Storage"] = "domainId:Id, userId:Id, clanId:Id", // stock core P1 (#97)
            ["SiegeScenario"] = "townId:Id",
            ["SiegeTeam"] = "siegeScenarioId:Id",
            ["SiegeShopEntry"] = "siegeScenarioId:Id, categoryId:Id, defaultsOnly:Flag", // siege shop L2 (#867)
            ["PermissionGroup"] = "parentGroupId:Id, isPremiumTier:Flag",
        };
        var actual = DashboardAccess.All
            .Where(t => t.Lookup is { Filters.Count: > 0 })
            .ToDictionary(t => t.TypeName, t => string.Join(", ", t.Lookup!.Filters.Select(f => $"{f.Key}:{f.Kind}")));

        Assert.Equal(expected.OrderBy(e => e.Key), actual.OrderBy(e => e.Key));
        Assert.Empty(DashboardAccess.Find("User")!.Lookup!.Filters);
        // Every type with filters has a lookup search to apply them to.
        Assert.All(actual.Keys, name => Assert.True(DashboardAccess.Find(name)!.LookupSearchable, name));
    }

    [Fact]
    public void LookupSearch_KeepsItsAccessRule()
    {
        // Any area node or the plugin key, as before #288 (ReadAccessRulesTests pins the category "L" too).
        var search = typeof(LookupController).GetMethod(nameof(LookupController.Search))!;
        var rule = Assert.Single(search.GetCustomAttributes<RequireServiceOrAnyAreaNodeAttribute>());
        Assert.Equal(ReadCategory.Lookup, rule.Use);
        Assert.Empty(search.GetCustomAttributes<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>());
    }
}
