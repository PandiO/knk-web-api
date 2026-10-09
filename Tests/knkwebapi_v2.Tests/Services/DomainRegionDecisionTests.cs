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
            repo.Setup(r => r.GetByWgRegionNameAsync(d.WgRegionId)).ReturnsAsync(d);
        }
        return new DomainService(repo.Object, Mapper(), new Mock<IDomainRegionNameFinalizer>().Object,
            NullLogger<DomainService>.Instance);
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
