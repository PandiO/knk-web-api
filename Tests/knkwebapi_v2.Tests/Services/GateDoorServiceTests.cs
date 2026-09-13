using System;
using System.Threading.Tasks;
using AutoMapper;
using Moq;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

// Covers item 6.3's new UpdateRegionDataAsync (GATESTRUCTURE_QOL_IMPLEMENTATION_PLAN.md) - the
// narrow write path the plugin's region-capture command uses to persist a WorldEdit-drawn
// region's vertex JSON onto one of a GateDoor's two region slots.
public class GateDoorServiceTests
{
    private readonly Mock<IGateDoorRepository> _repo;
    private readonly Mock<IGateStructureRepository> _structureRepo;
    private readonly Mock<ILocationRepository> _locationRepo;
    private readonly Mock<ILocationService> _locationService;
    private readonly Mock<IMapper> _mapper;
    private readonly GateDoorService _service;

    public GateDoorServiceTests()
    {
        _repo = new Mock<IGateDoorRepository>();
        _structureRepo = new Mock<IGateStructureRepository>();
        _locationRepo = new Mock<ILocationRepository>();
        _locationService = new Mock<ILocationService>();
        _mapper = new Mock<IMapper>();

        _service = new GateDoorService(
            _repo.Object,
            _structureRepo.Object,
            _locationRepo.Object,
            _locationService.Object,
            _mapper.Object);
    }

    [Fact]
    public async Task UpdateRegionDataAsync_WithInvalidId_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.UpdateRegionDataAsync(0, isOpenedRegion: false, regionData: "{}"));

        _repo.Verify(r => r.UpdateRegionDataAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UpdateRegionDataAsync_WhenDoorDoesNotExist_ThrowsKeyNotFoundException()
    {
        _repo.Setup(r => r.GetByIdAsync(42)).ReturnsAsync((GateDoor?)null);

        await Assert.ThrowsAsync<System.Collections.Generic.KeyNotFoundException>(
            () => _service.UpdateRegionDataAsync(42, isOpenedRegion: true, regionData: "{}"));

        _repo.Verify(r => r.UpdateRegionDataAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UpdateRegionDataAsync_ForClosedRegion_DelegatesToRepositoryWithIsOpenedRegionFalse()
    {
        _repo.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(new GateDoor { Id = 7 });

        var regionJson = "{\"type\":\"POLYGON2D\",\"worldName\":\"world\",\"points\":[{\"x\":1,\"z\":2}],\"minY\":60,\"maxY\":65}";
        await _service.UpdateRegionDataAsync(7, isOpenedRegion: false, regionData: regionJson);

        _repo.Verify(r => r.UpdateRegionDataAsync(7, false, regionJson), Times.Once);
    }

    [Fact]
    public async Task UpdateRegionDataAsync_ForOpenedRegion_DelegatesToRepositoryWithIsOpenedRegionTrue()
    {
        _repo.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(new GateDoor { Id = 7 });

        var regionJson = "{\"type\":\"CUBOID\",\"worldName\":\"world\",\"pos1\":{\"x\":1,\"y\":60,\"z\":2},\"pos2\":{\"x\":5,\"y\":65,\"z\":6}}";
        await _service.UpdateRegionDataAsync(7, isOpenedRegion: true, regionData: regionJson);

        _repo.Verify(r => r.UpdateRegionDataAsync(7, true, regionJson), Times.Once);
    }

    [Fact]
    public async Task UpdateRegionDataAsync_WithNullRegionData_PassesEmptyStringToRepository()
    {
        _repo.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(new GateDoor { Id = 7 });

        await _service.UpdateRegionDataAsync(7, isOpenedRegion: false, regionData: null!);

        _repo.Verify(r => r.UpdateRegionDataAsync(7, false, string.Empty), Times.Once);
    }
}
