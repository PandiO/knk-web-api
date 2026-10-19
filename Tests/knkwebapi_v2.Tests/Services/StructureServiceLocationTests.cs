using System;
using System.Threading.Tasks;
using AutoMapper;
using Moq;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

// KNG-195: a spawn point captured in game (or created on the form) arrives as an embedded Location and
// used to be dropped, because StructureDto had only locationId.
public class StructureServiceLocationTests
{
    private readonly Mock<IStructureRepository> _repo = new();
    private readonly Mock<IStreetRepository> _streets = new();
    private readonly Mock<IDistrictRepository> _districts = new();
    private readonly Mock<ILocationRepository> _locationRepo = new();
    private readonly Mock<ILocationService> _locations = new();
    private readonly StructureService _service;

    public StructureServiceLocationTests()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<StructureMappingProfile>();
            cfg.AddProfile<LocationMappingProfile>();
        }).CreateMapper();
        _streets.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new Street { Id = 1, Name = "Main" });
        _districts.Setup(r => r.GetByIdAsync(2)).ReturnsAsync(new District { Id = 2, Name = "Centre" });
        _service = new StructureService(
            _repo.Object,
            _streets.Object,
            _districts.Object,
            _locationRepo.Object,
            _locations.Object,
            mapper,
            new Mock<ITeleportDestinationService>().Object,
            new Mock<IDomainRegionNameFinalizer>().Object,
            FixedWorldResolver.Instance);
    }

    private static StructureDto Dto(LocationDto? location = null, int? locationId = null) => new()
    {
        Name = "Smithy",
        Description = "",
        WgRegionId = "domain_9",
        StreetId = 1,
        DistrictId = 2,
        HouseNumber = 4,
        Location = location,
        LocationId = locationId,
    };

    private static LocationDto Captured(int? id = null) => new()
    {
        Id = id,
        Name = "Smithy spawn",
        X = 10,
        Y = 64,
        Z = -3,
        World = "world",
    };

    [Fact]
    public async Task CreateAsync_WithCapturedLocation_CreatesItAndStoresItsId()
    {
        var captured = Captured();
        _locations.Setup(s => s.CreateAsync(captured)).ReturnsAsync(new LocationDto { Id = 41 });
        Structure? saved = null;
        _repo.Setup(r => r.AddStructureAsync(It.IsAny<Structure>())).Callback<Structure>(s => saved = s);

        await _service.CreateAsync(Dto(captured));

        _locations.Verify(s => s.CreateAsync(captured), Times.Once);
        Assert.NotNull(saved);
        Assert.Equal(41, saved!.LocationId);
    }

    [Fact]
    public async Task UpdateAsync_WithCapturedLocation_CreatesItAndStoresItsId()
    {
        var existing = new Structure { Id = 5, Name = "Smithy", StreetId = 1, DistrictId = 2, HouseNumber = 4, WgRegionId = "domain_9", LocationId = 7 };
        _repo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(existing);
        var captured = Captured();
        _locations.Setup(s => s.CreateAsync(captured)).ReturnsAsync(new LocationDto { Id = 42 });

        await _service.UpdateAsync(5, Dto(captured));

        Assert.Equal(42, existing.LocationId);
        _repo.Verify(r => r.UpdateStructureAsync(existing), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WithEditedExistingLocation_UpdatesItInPlace()
    {
        var existing = new Structure { Id = 5, Name = "Smithy", StreetId = 1, DistrictId = 2, HouseNumber = 4, WgRegionId = "domain_9", LocationId = 7 };
        _repo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(existing);
        var edited = Captured(7);

        await _service.UpdateAsync(5, Dto(edited));

        _locations.Verify(s => s.UpdateAsync(7, edited), Times.Once);
        _locations.Verify(s => s.CreateAsync(It.IsAny<LocationDto>()), Times.Never);
        Assert.Equal(7, existing.LocationId);
    }

    [Fact]
    public async Task CreateAsync_WithPickedLocationId_KeepsIt()
    {
        _locationRepo.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(new Location { Id = 7 });
        Structure? saved = null;
        _repo.Setup(r => r.AddStructureAsync(It.IsAny<Structure>())).Callback<Structure>(s => saved = s);

        await _service.CreateAsync(Dto(locationId: 7));

        Assert.Equal(7, saved!.LocationId);
        _locations.Verify(s => s.CreateAsync(It.IsAny<LocationDto>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithUnknownLocationId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(Dto(locationId: 99)));
        _repo.Verify(r => r.AddStructureAsync(It.IsAny<Structure>()), Times.Never);
    }
}
