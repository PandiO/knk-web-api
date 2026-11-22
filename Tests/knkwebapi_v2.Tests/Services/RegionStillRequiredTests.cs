using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Landmark-domain A01 and LD11 (PandiO/knk-workspace#983): <c>Domain.WgRegionId</c> is nullable for landmarks only; a town
/// without a region is still refused and nothing is saved (the other types keep their own checks, charter § 3.2).
/// </summary>
public class RegionStillRequiredTests
{
    private readonly Mock<ITownRepository> _repo = new();
    private readonly TownService _service;

    public RegionStillRequiredTests()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<TownMappingProfile>();
            cfg.AddProfile<LocationMappingProfile>();
        }).CreateMapper();
        _service = new TownService(_repo.Object, new Mock<ILocationRepository>().Object, new Mock<ILocationService>().Object, mapper,
            new Mock<ITeleportDestinationService>().Object, new Mock<IDomainRegionNameFinalizer>().Object, FixedWorldResolver.Instance);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task ATownWithoutARegion_IsRefused_AndNothingIsSaved(string? region)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAsync(new TownDto { Name = "Cinix", Description = "", WgRegionId = region! }));

        Assert.Contains("WgRegionId is required", ex.Message);
        _repo.Verify(r => r.AddTownAsync(It.IsAny<Town>()), Times.Never);
    }
}
