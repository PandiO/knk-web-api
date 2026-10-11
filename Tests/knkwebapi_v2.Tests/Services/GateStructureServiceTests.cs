using System;
using System.Threading.Tasks;
using AutoMapper;
using Moq;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

// KNG-78: 'here' is the plugin's gate-command keyword (/gate toggle here), so it can't be used as a
// gate structure name on create or rename (GateNameRules).
public class GateStructureServiceTests
{
    private readonly Mock<IGateStructureRepository> _repo;
    private readonly Mock<ILocationRepository> _locationRepo;
    private readonly Mock<ILocationService> _locationService;
    private readonly Mock<IMapper> _mapper;
    private readonly Mock<IDomainRegionNameFinalizer> _regionNames;
    private readonly GateStructureService _service;

    public GateStructureServiceTests()
    {
        _repo = new Mock<IGateStructureRepository>();
        _locationRepo = new Mock<ILocationRepository>();
        _locationService = new Mock<ILocationService>();
        _mapper = new Mock<IMapper>();
        _regionNames = new Mock<IDomainRegionNameFinalizer>();

        _service = new GateStructureService(
            _repo.Object,
            _locationRepo.Object,
            _locationService.Object,
            _mapper.Object,
            _regionNames.Object);
    }

    private static GateStructureDto Dto(string name) =>
        new GateStructureDto { Name = name, StreetId = 1, DistrictId = 2 };

    [Theory]
    [InlineData("here")]
    [InlineData(" HERE ")]
    [InlineData("Here")]
    public async Task CreateAsync_WithReservedName_ThrowsArgumentExceptionAndDoesNotSave(string name)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(Dto(name)));

        Assert.Contains("reserved", ex.Message);
        _repo.Verify(r => r.AddGateStructureAsync(It.IsAny<GateStructure>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithNameContainingHere_IsAllowed()
    {
        var dto = Dto("Herewood");
        var gate = new GateStructure { Name = "Herewood" };
        _mapper.Setup(m => m.Map<GateStructure>(dto)).Returns(gate);
        _mapper.Setup(m => m.Map<GateStructureDto>(gate)).Returns(Dto("Herewood"));

        var result = await _service.CreateAsync(dto);

        Assert.Equal("Herewood", result.Name);
        _repo.Verify(r => r.AddGateStructureAsync(gate), Times.Once);
    }

    [Theory]
    [InlineData("here")]
    [InlineData(" HERE ")]
    public async Task UpdateAsync_RenamingToReservedName_ThrowsArgumentExceptionAndDoesNotSave(string name)
    {
        _repo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new GateStructure { Id = 5, Name = "North Gate" });

        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(5, Dto(name)));

        _repo.Verify(r => r.UpdateGateStructureAsync(It.IsAny<GateStructure>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_RenamingToNameContainingHere_IsAllowed()
    {
        var existing = new GateStructure { Id = 5, Name = "North Gate" };
        var dto = Dto("Herewood");
        _repo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(existing);

        await _service.UpdateAsync(5, dto);

        _mapper.Verify(m => m.Map(dto, existing), Times.Once);
        _repo.Verify(r => r.UpdateGateStructureAsync(existing), Times.Once);
    }

    [Theory]
    [InlineData("here", true)]
    [InlineData("  hErE\t", true)]
    [InlineData("Herewood", false)]
    [InlineData("there", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void GateNameRules_IsReserved_MatchesTrimmedCaseInsensitiveKeywordOnly(string? name, bool expected)
    {
        Assert.Equal(expected, GateNameRules.IsReserved(name));
    }
}
