using System;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

// KNG-78: PUT /api/Domains/{id} and PUT /api/Structures/{id} can also rename a GateStructure, so they
// reject the gate-command keyword 'here' for gates. Other domains and structures may still use it.
public class DomainServiceGateNameTests
{
    private readonly Mock<IDomainRepository> _repo = new();
    private readonly DomainService _service;

    public DomainServiceGateNameTests()
    {
        _service = new DomainService(_repo.Object, new Mock<IMapper>().Object,
            new Mock<IDomainRegionNameFinalizer>().Object, NullLogger<DomainService>.Instance);
    }

    [Theory]
    [InlineData("here")]
    [InlineData(" HERE ")]
    public async Task UpdateAsync_RenamingAGateStructureToReservedName_ThrowsArgumentExceptionAndDoesNotSave(string name)
    {
        _repo.Setup(r => r.GetByIdAsync(9)).ReturnsAsync(new GateStructure { Id = 9, Name = "North Gate" });

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _service.UpdateAsync(9, new Domain { Name = name }));

        Assert.Contains("reserved", ex.Message);
        _repo.Verify(r => r.UpdateDomainAsync(It.IsAny<Domain>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_RenamingANonGateDomainToHere_IsAllowed()
    {
        var existing = new Town { Id = 3, Name = "Oakhaven" };
        _repo.Setup(r => r.GetByIdAsync(3)).ReturnsAsync(existing);

        await _service.UpdateAsync(3, new Domain { Name = "here" });

        Assert.Equal("here", existing.Name);
        _repo.Verify(r => r.UpdateDomainAsync(existing), Times.Once);
    }
}

public class StructureServiceGateNameTests
{
    private readonly Mock<IStructureRepository> _repo = new();
    private readonly StructureService _service;

    public StructureServiceGateNameTests()
    {
        _service = new StructureService(
            _repo.Object,
            new Mock<IStreetRepository>().Object,
            new Mock<IDistrictRepository>().Object,
            new Mock<ILocationRepository>().Object,
            new Mock<IMapper>().Object,
            new Mock<ITeleportDestinationService>().Object,
            new Mock<IDomainRegionNameFinalizer>().Object);
    }

    private static StructureDto Dto(string name) => new StructureDto
    {
        Name = name,
        WgRegionId = "domain_9",
        StreetId = 1,
        DistrictId = 2,
        HouseNumber = 4,
    };

    [Theory]
    [InlineData("here")]
    [InlineData(" HERE ")]
    public async Task UpdateAsync_RenamingAGateStructureToReservedName_ThrowsArgumentExceptionAndDoesNotSave(string name)
    {
        _repo.Setup(r => r.GetByIdAsync(9)).ReturnsAsync(
            new GateStructure { Id = 9, Name = "North Gate", StreetId = 1, DistrictId = 2, HouseNumber = 4 });

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(9, Dto(name)));

        Assert.Contains("reserved", ex.Message);
        _repo.Verify(r => r.UpdateStructureAsync(It.IsAny<Structure>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_RenamingAPlainStructureToHere_IsAllowed()
    {
        var existing = new Structure { Id = 5, Name = "Smithy", StreetId = 1, DistrictId = 2, HouseNumber = 4 };
        _repo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(existing);

        await _service.UpdateAsync(5, Dto("here"));

        Assert.Equal("here", existing.Name);
        _repo.Verify(r => r.UpdateStructureAsync(existing), Times.Once);
    }
}
