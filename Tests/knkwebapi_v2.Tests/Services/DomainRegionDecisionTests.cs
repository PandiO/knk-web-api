using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// POST api/Domains/search-region-decisions: the game server's region → domain lookup (navigation, walk paths,
/// the region tracker). Live test 2026-10-09: a GateStructure region (Keep Gate, the gates) answered nothing, so
/// the navigator never saw its AllowEntry.
/// </summary>
public class DomainRegionDecisionTests
{
    private static IMapper Mapper() => new MapperConfiguration(cfg =>
    {
        cfg.AddProfile<DomainMappingProfile>();
        cfg.AddProfile<PagedQueryMappingProfile>();
    }).CreateMapper();

    private static DomainService Service(params Domain[] domains)
    {
        var repo = new Mock<IDomainRepository>();
        foreach (var d in domains)
        {
            repo.Setup(r => r.GetByAnyRegionAsync(d.WgRegionId, null)).ReturnsAsync(new DomainRegionMatch(d, null));
            // KNG-254: a sub-region answers its domain.
            foreach (var sub in d.SubRegions)
            {
                repo.Setup(r => r.GetByAnyRegionAsync(sub.WgRegionId, null)).ReturnsAsync(new DomainRegionMatch(d, sub.Id));
            }
        }
        return new DomainService(repo.Object, Mapper(), new Mock<IDomainRegionNameFinalizer>().Object,
            NullLogger<DomainService>.Instance, FixedWorldResolver.Instance);
    }

    [Fact]
    public async Task ASubRegionAnswersItsDomainWithAllItsRegions()
    {
        var gate = new GateStructure { Id = 11, Name = "Garrison", WgRegionId = "domain_11", AllowExit = false };
        gate.SubRegions.Add(new DomainSubRegion { Id = 4, DomainId = 11, WgRegionId = "domain_11_sub_4" });
        gate.SubRegions.Add(new DomainSubRegion { Id = 2, DomainId = 11, WgRegionId = "domain_11_sub_2" });

        var result = await Service(gate).SearchDomainRegionDecisionAsync(
            new DomainRegionQueryDto { WgRegionIds = new List<string> { "domain_11_sub_4" }, TopDownHierarchy = true });

        var decision = Assert.Single(result).Value;
        Assert.Equal((11, "domain_11", 4, false), (decision.Id, decision.WgRegionId, decision.MatchedSubRegionId, decision.AllowExit));
        Assert.Equal(new[] { "domain_11_sub_2", "domain_11_sub_4" }, decision.SubRegionIds);
    }

    [Fact]
    public async Task TheMainRegionAnswersWithoutAMatchedSubRegion()
    {
        var town = new Town { Id = 3, Name = "Oakhaven", WgRegionId = "domain_3" };
        town.SubRegions.Add(new DomainSubRegion { Id = 9, DomainId = 3, WgRegionId = "domain_3_sub_9" });

        var decision = await Service(town).GetByWgRegionNameAsync("domain_3");

        Assert.NotNull(decision);
        Assert.Null(decision!.MatchedSubRegionId);
        Assert.Equal(new[] { "domain_3_sub_9" }, decision.SubRegionIds);
    }

    [Fact]
    public async Task AGateStructureRegionAnswersItsGate()
    {
        var gate = new GateStructure { Id = 11, Name = "Keep Gate", WgRegionId = "domain_11", AllowEntry = false };

        var result = await Service(gate).SearchDomainRegionDecisionAsync(
            new DomainRegionQueryDto { WgRegionIds = new List<string> { "domain_11" }, TopDownHierarchy = true });

        var decision = Assert.Single(result).Value;
        Assert.Equal(("Keep Gate", "GateStructure", false), (decision.Name, decision.DomainType, decision.AllowEntry));
    }

    [Fact]
    public async Task AGateStructureTakesTheStructurePlaceInTheHierarchy()
    {
        var district = new District { Id = 8, Name = "The Keep", WgRegionId = "district_1000006" };
        var gate = new GateStructure { Id = 11, Name = "Keep Gate", WgRegionId = "domain_11" };

        var result = await Service(district, gate).SearchDomainRegionDecisionAsync(new DomainRegionQueryDto
        {
            WgRegionIds = new List<string> { "district_1000006", "domain_11" },
            TopDownHierarchy = true
        });

        Assert.Equal(new[] { "The Keep", "Keep Gate" }, result.OrderBy(e => e.Key).Select(e => e.Value.Name));
    }
}
