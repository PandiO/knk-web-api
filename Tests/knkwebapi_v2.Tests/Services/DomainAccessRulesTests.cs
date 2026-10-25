using AutoMapper;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// KNG-56: the game server writes every domain's AllowEntry/AllowExit onto its WorldGuard region, from one list.
/// </summary>
public class DomainAccessRulesTests
{
    private readonly Mock<IDomainRepository> _repo = new();
    private readonly DomainService _service;

    public DomainAccessRulesTests()
    {
        _service = new DomainService(_repo.Object, new Mock<IMapper>().Object,
            new Mock<IDomainRegionNameFinalizer>().Object, NullLogger<DomainService>.Instance, FixedWorldResolver.Instance);
        _repo.Setup(r => r.GetSubRegionIdsByDomainAsync())
            .ReturnsAsync(new Dictionary<int, IReadOnlyList<string>>());
    }

    [Fact]
    public async Task EveryDomainWithARegionIsListedWithItsRulesAndType()
    {
        _repo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Domain>
        {
            new District { Id = 7, Name = "Old Quarter", WgRegionId = "domain_7", AllowEntry = false, AllowExit = true },
            new Town { Id = 3, Name = "Oakhaven", WgRegionId = "domain_3", AllowEntry = true, AllowExit = false },
            new GateStructure { Id = 9, Name = "North Gate", WgRegionId = "domain_9" },
        });

        var rules = await _service.GetAccessRulesAsync();

        Assert.Equal(new[] { 3, 7, 9 }, rules.Select(r => r.Id));
        var town = rules[0];
        Assert.Equal(("Oakhaven", "domain_3", true, false, "Town"),
            (town.Name, town.WgRegionId, town.AllowEntry, town.AllowExit, town.DomainType));
        Assert.False(rules[1].AllowEntry);
        Assert.Equal("District", rules[1].DomainType);
        Assert.Equal("GateStructure", rules[2].DomainType);
    }

    [Fact]
    public async Task DomainsWithoutARegionAreSkipped()
    {
        _repo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Domain>
        {
            new Structure { Id = 1, Name = "Unplaced", WgRegionId = "" },
            new Structure { Id = 2, Name = "Placed", WgRegionId = "domain_2" },
        });

        var rules = await _service.GetAccessRulesAsync();

        Assert.Equal(2, Assert.Single(rules).Id);
    }

    [Fact]
    public async Task ADomainListsItsSubRegions_OthersAnEmptyList()
    {
        // KNG-254: the sub-regions carry the same rule, and a domain_<id>_sub_<n> region no row lists is an orphan.
        _repo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Domain>
        {
            new GateStructure { Id = 9, Name = "Garrison", WgRegionId = "domain_9", AllowExit = false },
            new Town { Id = 3, Name = "Oakhaven", WgRegionId = "domain_3" },
        });
        _repo.Setup(r => r.GetSubRegionIdsByDomainAsync()).ReturnsAsync(new Dictionary<int, IReadOnlyList<string>>
        {
            [9] = new[] { "domain_9_sub_1", "domain_9_sub_2" }
        });

        var rules = await _service.GetAccessRulesAsync();

        Assert.Empty(rules[0].SubRegionIds);
        Assert.Equal(new[] { "domain_9_sub_1", "domain_9_sub_2" }, rules[1].SubRegionIds);
        Assert.False(rules[1].AllowExit);
    }
}
