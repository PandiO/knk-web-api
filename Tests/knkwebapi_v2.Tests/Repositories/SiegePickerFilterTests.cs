using KnKWebAPI.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Tests.Services;

namespace knkwebapi_v2.Tests.Repositories;

/// <summary>
/// Siege Phase 3 picker filters used by the scenario/team/objective FormConfigurations
/// (docs/specs/siege-minigame/PHASE_3_FORMCONFIGS.md): GateStructure search by townId and by
/// siegeScenarioId, Clan search preferTownId ordering, and the read-only TitleBrackets lookup.
/// </summary>
public class SiegePickerFilterTests : IDisposable
{
    private readonly KnKDbContext _context;

    public SiegePickerFilterTests()
    {
        _context = SiegeTestData.NewContext("SiegePickerFilters");
    }

    public void Dispose() => _context.Dispose();

    private async Task SeedAsync()
    {
        await SiegeTestData.SeedValidScenarioAsync(_context);

        // A second town with its own district and gate, plus a gate in town 1 that the scenario
        // did not select.
        _context.Towns.Add(new Town { Id = 2, Name = "Other", Description = "o", WgRegionId = "other" });
        _context.Districts.Add(new District { Id = 12, Name = "Elsewhere", Description = "d", WgRegionId = "other_d", TownId = 2 });
        _context.GateStructures.Add(new GateStructure
        {
            Id = 401, Name = "Side gate", Description = "g", WgRegionId = "cinix_side",
            DistrictId = 10, StreetId = 1, LocationId = 1003
        });
        _context.GateStructures.Add(new GateStructure
        {
            Id = 402, Name = "Far gate", Description = "g", WgRegionId = "other_gate",
            DistrictId = 12, StreetId = 1, LocationId = 1003
        });
        _context.Clans.Add(new Clan { Id = 6, Name = "Another Garrison", ChatColor = "BLUE", BannerDesignId = 51, DefaultForTownId = 1 });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    private static PagedQuery Query(params (string Key, string Value)[] filters) => new()
    {
        PageNumber = 1,
        PageSize = 50,
        Filters = filters.ToDictionary(f => f.Key, f => f.Value)
    };

    [Fact]
    public async Task GateSearch_TownFilter_ListsOnlyThatTownsGates()
    {
        await SeedAsync();
        var repo = new GateStructureRepository(_context);

        var result = await repo.SearchAsync(Query(("townId", "1")));

        Assert.Equal(new[] { 400, 401 }, result.Items.Select(g => g.Id).OrderBy(id => id));
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task GateSearch_ScenarioFilter_ListsOnlyTheScenariosSelectedGates()
    {
        await SeedAsync();
        var repo = new GateStructureRepository(_context);

        var result = await repo.SearchAsync(Query(("siegeScenarioId", "100")));

        Assert.Equal(new[] { 400 }, result.Items.Select(g => g.Id));
    }

    [Fact]
    public async Task GateSearch_WithoutSiegeFilters_IsUnchanged()
    {
        await SeedAsync();
        var repo = new GateStructureRepository(_context);

        var result = await repo.SearchAsync(Query());

        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    public async Task ClanSearch_PreferTownId_ListsTheTownsDefaultClanFirst()
    {
        await SeedAsync();
        var repo = new ClanRepository(_context);

        var preferred = await repo.SearchAsync(Query(("preferTownId", "1")));
        var plain = await repo.SearchAsync(Query());

        Assert.Equal(new[] { 6, 5 }, preferred.Items.Select(c => c.Id));
        Assert.Equal(new[] { 5, 6 }, plain.Items.Select(c => c.Id));
        Assert.Equal(2, preferred.TotalCount);
    }

    [Fact]
    public async Task ClanSearch_PreferTownId_KeepsTheRequestedSortAfterThePreferredClan()
    {
        await SeedAsync();
        _context.Clans.Add(new Clan { Id = 7, Name = "Aardvark Company", ChatColor = "GRAY", BannerDesignId = 51 });
        await _context.SaveChangesAsync();
        var repo = new ClanRepository(_context);

        var query = Query(("preferTownId", "1"));
        query.SortBy = "name";
        var result = await repo.SearchAsync(query);

        Assert.Equal(new[] { 6, 7, 5 }, result.Items.Select(c => c.Id));
    }

    private static TitleBracketsController TitleController()
    {
        var service = new Mock<ITitleService>();
        service.Setup(s => s.GetAllOrderedAsync()).ReturnsAsync(new List<TitleBracket>
        {
            new() { Id = 1, MaleName = "Peasant", FemaleName = "Peasant", MinExperience = 0 },
            new() { Id = 2, MaleName = "Squire", FemaleName = "Squiress", MinExperience = 50 },
            new() { Id = 3, MaleName = "Knight", FemaleName = "Dame", MinExperience = 200 }
        });
        return new TitleBracketsController(service.Object);
    }

    [Fact]
    public async Task TitleBracketSearch_MatchesEitherNameAndPages()
    {
        var controller = TitleController();

        var byFemaleName = (OkObjectResult)await controller.Search(new PagedQueryDto { SearchTerm = "dame" });
        var page = (PagedResultDto<TitleBracketDto>)byFemaleName.Value!;
        Assert.Equal(new[] { 3 }, page.Items.Select(b => b.Id));
        Assert.Equal("Knight", page.Items[0].Name);

        var paged = (OkObjectResult)await controller.Search(new PagedQueryDto { PageNumber = 2, PageSize = 2 });
        var second = (PagedResultDto<TitleBracketDto>)paged.Value!;
        Assert.Equal(3, second.TotalCount);
        Assert.Equal(new[] { 3 }, second.Items.Select(b => b.Id));
    }

    [Fact]
    public async Task TitleBracketGetById_UnknownId_IsNotFound()
    {
        var controller = TitleController();

        Assert.IsType<NotFoundResult>(await controller.GetById(99));
        var found = (OkObjectResult)await controller.GetById(2);
        Assert.Equal("Squire", ((TitleBracketDto)found.Value!).Name);
    }
}
