using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// The one parent walk of every domain type (landmark-domain LD23, PandiO/knk-workspace#983) and the CrownLand loop of
/// PandiO/knk-workspace#981: <c>DomainMappingProfile.GetParentDomainDecisions</c> used to loop forever for a type other
/// than Town, District and Structure. Every walk here runs under a time limit, so a loop fails the test instead of
/// hanging it. Covers the mapping (A05: a landmark, a landmark under a landmark, a crown land, a bare domain, a cycle),
/// and <c>search-region-decisions</c> and <c>by-region</c> through the real repository's parent loading.
/// </summary>
public class DomainParentWalkTests : IDisposable
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    private static IMapper Mapper() => new MapperConfiguration(cfg =>
    {
        cfg.AddProfile<DomainMappingProfile>();
        cfg.AddProfile<PagedQueryMappingProfile>();
    }).CreateMapper();

    private readonly KnKDbContext _context;
    private readonly DomainService _service;

    public DomainParentWalkTests()
    {
        _context = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _service = new DomainService(new DomainRepository(_context), Mapper(), new Mock<IDomainRegionNameFinalizer>().Object,
            NullLogger<DomainService>.Instance, FixedWorldResolver.Instance);

        _context.Kingdoms.Add(new Kingdom { Id = 40, Name = "Aster" });
        _context.Towns.Add(new Town { Id = 1, Name = "Cinix", Description = "", WgRegionId = "domain_1", WorldName = "world" });
        _context.Districts.Add(new District { Id = 2, Name = "Old town", Description = "", WgRegionId = "domain_2", TownId = 1, WorldName = "world" });
        _context.Structures.Add(new Structure { Id = 3, Name = "Keep", Description = "", WgRegionId = "domain_3", DistrictId = 2, StreetId = 1, WorldName = "world" });
        _context.Structures.Add(new Structure { Id = 4, Name = "Farm", Description = "", WgRegionId = "domain_4", TownId = 1, WorldName = "world" });
        _context.CrownLands.Add(new CrownLand { Id = 5, Name = "Royal forest", Description = "", WgRegionId = "domain_5", KingdomId = 40, WorldName = "world" });
        _context.Landmarks.Add(new Landmark { Id = 6, Name = "Throne room", Description = "", WgRegionId = "domain_6", AreaKind = LandmarkAreaKind.OwnRegion, ParentDomainId = 3, WorldName = "world" });
        _context.Landmarks.Add(new Landmark { Id = 7, Name = "Dais", Description = "", WgRegionId = "domain_7", AreaKind = LandmarkAreaKind.OwnRegion, ParentDomainId = 6, WorldName = "world" });
        _context.Landmarks.Add(new Landmark { Id = 8, Name = "Standing stone", Description = "", WgRegionId = "domain_8", AreaKind = LandmarkAreaKind.OwnRegion, WorldName = "world" });
        _context.Landmarks.Add(new Landmark { Id = 9, Name = "Well", Description = "", AreaKind = LandmarkAreaKind.None, ParentDomainId = 1, WorldName = "world" });
        _context.DomainSubRegions.Add(new DomainSubRegion { Id = 20, DomainId = 6, Name = "Tower", WgRegionId = "domain_6_sub_20" });
        _context.DomainSubRegions.Add(new DomainSubRegion { Id = 21, DomainId = 5, Name = "Glade", WgRegionId = "domain_5_sub_21" });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();
    }

    public void Dispose() => _context.Dispose();

    private static Task<T> Ends<T>(Func<T> walk) => Task.Run(walk).WaitAsync(Limit);

    private static Task<T> Ends<T>(Func<Task<T>> walk) => Task.Run(walk).WaitAsync(Limit);

    private static string[] Chain(DomainRegionDecisionDto decision) =>
        decision.ParentDomainDecisions.Select(p => $"{p.DomainType} {p.Id}").ToArray();

    // ===== #981: the mapping ends for every type =====

    [Fact]
    public async Task ACrownLand_MapsWithoutParents_AndTheWalkEnds()
    {
        var decision = await Ends(() => Mapper().Map<DomainRegionDecisionDto>(
            new CrownLand { Id = 5, Name = "Royal forest", WgRegionId = "domain_5", KingdomId = 40 }));

        Assert.Equal(("CrownLand", 5), (decision.DomainType, decision.Id));
        Assert.Empty(decision.ParentDomainDecisions);
    }

    [Fact]
    public async Task ABareDomain_MapsWithoutParents_AndTheWalkEnds()
    {
        var decision = await Ends(() => Mapper().Map<DomainRegionDecisionDto>(new Domain { Id = 12, Name = "Plain", WgRegionId = "domain_12" }));

        Assert.Equal("Domain", decision.DomainType);
        Assert.Empty(decision.ParentDomainDecisions);
    }

    [Fact]
    public async Task ARuralStructure_HasNoParent_AsBefore()
    {
        var town = new Town { Id = 1, Name = "Cinix", WgRegionId = "domain_1" };
        var decision = await Ends(() => Mapper().Map<DomainRegionDecisionDto>(
            new Structure { Id = 4, Name = "Farm", WgRegionId = "domain_4", TownId = 1, Town = town }));

        Assert.Empty(decision.ParentDomainDecisions);
    }

    [Fact]
    public async Task AStructure_StillWalksUpToItsDistrictAndTown()
    {
        var town = new Town { Id = 1, Name = "Cinix", WgRegionId = "domain_1" };
        var district = new District { Id = 2, Name = "Old town", WgRegionId = "domain_2", TownId = 1, Town = town };
        var decision = await Ends(() => Mapper().Map<DomainRegionDecisionDto>(
            new Structure { Id = 3, Name = "Keep", WgRegionId = "domain_3", DistrictId = 2, District = district }));

        Assert.Equal(new[] { "District 2", "Town 1" }, Chain(decision));
    }

    [Fact]
    public async Task ALandmarkUnderALandmark_WalksTheWholeChain_NearestFirst()
    {
        var town = new Town { Id = 1, Name = "Cinix", WgRegionId = "domain_1" };
        var district = new District { Id = 2, Name = "Old town", WgRegionId = "domain_2", TownId = 1, Town = town };
        var keep = new Structure { Id = 3, Name = "Keep", WgRegionId = "domain_3", DistrictId = 2, District = district };
        var throne = new Landmark { Id = 6, Name = "Throne room", WgRegionId = "domain_6", ParentDomainId = 3, ParentDomain = keep };
        var dais = new Landmark { Id = 7, Name = "Dais", WgRegionId = "domain_7", ParentDomainId = 6, ParentDomain = throne };

        var decision = await Ends(() => Mapper().Map<DomainRegionDecisionDto>(dais));

        Assert.Equal("Landmark", decision.DomainType);
        Assert.Equal(new[] { "Landmark 6", "Structure 3", "District 2", "Town 1" }, Chain(decision));
    }

    [Fact]
    public async Task ACycleOfLandmarks_Ends_AndNeverListsTheDomainItself()
    {
        var a = new Landmark { Id = 30, Name = "A", WgRegionId = "a" };
        var b = new Landmark { Id = 31, Name = "B", WgRegionId = "b", ParentDomainId = 30, ParentDomain = a };
        a.ParentDomainId = 31;
        a.ParentDomain = b;

        var decision = await Ends(() => Mapper().Map<DomainRegionDecisionDto>(a));

        Assert.Equal(new[] { "Landmark 31" }, Chain(decision));
    }

    [Fact]
    public void TheIdWalk_StopsAtACycle_AndAtTheDepthLimit()
    {
        var parents = new Dictionary<int, int?> { [1] = 2, [2] = 3, [3] = 1 };
        Assert.Equal(new[] { 2, 3 }, DomainParents.AncestorIds(1, id => parents.TryGetValue(id, out var p) ? p : null));

        // A chain longer than any real hierarchy stops at the limit.
        Assert.Equal(DomainParents.MaxDepth, DomainParents.AncestorIds(0, id => id + 1).Count);
    }

    [Fact]
    public void EveryTypesParent_IsNamedInOnePlace()
    {
        Assert.Null(DomainParents.ParentIdOf(new Town { Id = 1 }));
        Assert.Equal(1, DomainParents.ParentIdOf(new District { Id = 2, TownId = 1 }));
        Assert.Equal(2, DomainParents.ParentIdOf(new Structure { Id = 3, DistrictId = 2 }));
        Assert.Equal(2, DomainParents.ParentIdOf(new GateStructure { Id = 10, DistrictId = 2 }));
        Assert.Null(DomainParents.ParentIdOf(new Structure { Id = 4, TownId = 1 }));
        Assert.Null(DomainParents.ParentIdOf(new CrownLand { Id = 5 }));
        Assert.Null(DomainParents.ParentIdOf(new Domain { Id = 12 }));
        Assert.Equal(3, DomainParents.ParentIdOf(new Landmark { Id = 6, ParentDomainId = 3 }));
        Assert.Null(DomainParents.ParentIdOf(new Landmark { Id = 8 }));
    }

    // ===== A05: search-region-decisions and by-region through the repository =====

    [Fact]
    public async Task ByRegion_ALandmarkRegion_ResolvesWithItsWholeParentChain()
    {
        var decision = await Ends(() => _service.GetByWgRegionNameAsync("domain_7", "world"));

        Assert.NotNull(decision);
        Assert.Equal(("Landmark", 7), (decision!.DomainType, decision.Id));
        Assert.Equal(new[] { "Landmark 6", "Structure 3", "District 2", "Town 1" }, Chain(decision));
    }

    [Fact]
    public async Task ByRegion_ALandmarkSubRegion_ResolvesToTheLandmark()
    {
        var decision = await Ends(() => _service.GetByWgRegionNameAsync("domain_6_sub_20"));

        Assert.Equal((6, 20), (decision!.Id, decision.MatchedSubRegionId!.Value));
        Assert.Equal(new[] { "domain_6_sub_20" }, decision.SubRegionIds);
        Assert.Equal(new[] { "Structure 3", "District 2", "Town 1" }, Chain(decision));
    }

    [Theory]
    [InlineData("domain_5")]
    [InlineData("domain_5_sub_21")]
    public async Task ByRegion_ACrownLandRegion_ResolvesWithoutParents_AndEnds(string region)
    {
        var decision = await Ends(() => _service.GetByWgRegionNameAsync(region, "world"));

        Assert.Equal(("CrownLand", 5), (decision!.DomainType, decision.Id));
        Assert.Empty(decision.ParentDomainDecisions);
    }

    [Fact]
    public async Task ByRegion_ATopLevelLandmark_HasNoParents()
    {
        var decision = await Ends(() => _service.GetByWgRegionNameAsync("domain_8"));

        Assert.Equal(("Landmark", 8), (decision!.DomainType, decision.Id));
        Assert.Empty(decision.ParentDomainDecisions);
    }

    [Fact]
    public async Task SearchRegionDecisions_ACrownLandRegion_Ends_AndAnswersNothingAsBefore()
    {
        // The search keeps one town, district, structure and (since #983) landmark; a crown land is none of them.
        var result = await Ends(() => _service.SearchDomainRegionDecisionAsync(
            new DomainRegionQueryDto { WgRegionIds = new List<string> { "domain_5", "domain_5_sub_21" }, TopDownHierarchy = true }));

        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchRegionDecisions_TopDown_PutsTheLandmarkInnermost_WithItsParentChain()
    {
        var result = await Ends(() => _service.SearchDomainRegionDecisionAsync(new DomainRegionQueryDto
        {
            WgRegionIds = new List<string> { "domain_1", "domain_2", "domain_3", "domain_6_sub_20" }, TopDownHierarchy = true, WorldName = "world"
        }));

        Assert.Equal(new[] { (0, "Town"), (1, "District"), (2, "Structure"), (3, "Landmark") },
            result.OrderBy(r => r.Key).Select(r => (r.Key, r.Value.DomainType)).ToArray());
        Assert.Equal(20, result[3].MatchedSubRegionId);
        Assert.Equal(new[] { "Structure 3", "District 2", "Town 1" }, Chain(result[3]));
    }

    [Fact]
    public async Task SearchRegionDecisions_BottomUp_GivesTheLandmarkTheLowestKey_AndKeepsTheOldKeysWithoutOne()
    {
        var withLandmark = await Ends(() => _service.SearchDomainRegionDecisionAsync(new DomainRegionQueryDto
        {
            WgRegionIds = new List<string> { "domain_1", "domain_2", "domain_3", "domain_6" }, TopDownHierarchy = false
        }));
        Assert.Equal(new[] { (0, "Landmark"), (1, "Structure"), (2, "District"), (3, "Town") },
            withLandmark.OrderBy(r => r.Key).Select(r => (r.Key, r.Value.DomainType)).ToArray());

        var without = await Ends(() => _service.SearchDomainRegionDecisionAsync(new DomainRegionQueryDto
        {
            WgRegionIds = new List<string> { "domain_1", "domain_2", "domain_3" }, TopDownHierarchy = false
        }));
        Assert.Equal(new[] { (0, "Structure"), (1, "District"), (2, "Town") },
            without.OrderBy(r => r.Key).Select(r => (r.Key, r.Value.DomainType)).ToArray());
    }

    [Fact]
    public async Task SearchRegionDecisions_APointLandmarkUnderATown_IsNeverMatched_ByARegion()
    {
        // The point landmark (9) owns no region: the town's region answers the town only.
        var result = await Ends(() => _service.SearchDomainRegionDecisionAsync(new DomainRegionQueryDto
        {
            WgRegionIds = new List<string> { "domain_1" }, TopDownHierarchy = true
        }));

        Assert.Equal("Town", Assert.Single(result).Value.DomainType);
    }

    [Fact]
    public async Task TheRepositorysParentLoading_EndsOnACycleInTheDatabase()
    {
        // A cycle no service would save (the landmark rules refuse it): 8 under 7, 7 under 6, 6 under 8.
        var standingStone = await _context.Landmarks.SingleAsync(l => l.Id == 8);
        standingStone.ParentDomainId = 7;
        var throne = await _context.Landmarks.SingleAsync(l => l.Id == 6);
        throne.ParentDomainId = 8;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var decision = await Ends(() => _service.GetByWgRegionNameAsync("domain_8"));

        Assert.Equal(new[] { "Landmark 7", "Landmark 6" }, Chain(decision!));
    }
}
