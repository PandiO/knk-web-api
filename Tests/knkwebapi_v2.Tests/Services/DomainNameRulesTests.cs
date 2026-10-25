using System;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
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

/// <summary>
/// The dev database held "Residential District\n" (the form's textarea let a newline through) and the action bar
/// rendered it as a missing glyph. Every domain create/update now trims Name and Description (DomainNameRules) and
/// refuses a name that is empty after trimming.
/// </summary>
public class DomainNameRulesTests
{
    [Theory]
    [InlineData("Foo\n", "Foo")]
    [InlineData("  Residential District \r\n", "Residential District")]
    [InlineData("\tMerchant's District\t", "Merchant's District")]
    [InlineData("Already clean", "Already clean")]
    [InlineData("Two\nlines", "Two\nlines")]
    public void Normalize_TrimsTheNameAtBothEnds_AndKeepsTheInside(string name, string expected)
    {
        var dto = new TownDto { Name = name, Description = " keep\ninner \n" };

        DomainNameRules.Normalize(dto, "Town", "dto");

        Assert.Equal(expected, dto.Name);
        Assert.Equal("keep\ninner", dto.Description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\r\n\t")]
    public void Normalize_RejectsANameThatIsEmptyAfterTrimming(string? name)
    {
        var dto = new DistrictDto { Name = name!, Description = "d" };

        var ex = Assert.Throws<ArgumentException>(() => DomainNameRules.Normalize(dto, "District", "districtDto"));

        Assert.Equal("District name is required.", ex.Message.Split(" (Parameter")[0]);
        Assert.Equal("districtDto", ex.ParamName);
    }

    [Fact]
    public void Normalize_LeavesANullOrEmptyDescriptionAlone()
    {
        var withNull = new StructureDto { Name = "Smithy\n", Description = null! };
        var withEmpty = new StructureDto { Name = "Smithy", Description = "" };

        DomainNameRules.Normalize(withNull, "Structure", "dto");
        DomainNameRules.Normalize(withEmpty, "Structure", "dto");

        Assert.Null(withNull.Description);
        Assert.Equal("", withEmpty.Description);
    }

    [Fact]
    public void Normalize_Domain_TrimsLikeTheDtos()
    {
        var domain = new Town { Name = " Oakhaven\n", Description = "\nA town\n" };

        DomainNameRules.Normalize(domain, "domain");

        Assert.Equal("Oakhaven", domain.Name);
        Assert.Equal("A town", domain.Description);
        Assert.Throws<ArgumentException>(() => DomainNameRules.Normalize(new Town { Name = "\n" }, "domain"));
    }
}

public class TownServiceNameTrimmingTests
{
    private readonly Mock<ITownRepository> _repo = new();
    private readonly TownService _service;

    public TownServiceNameTrimmingTests()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<TownMappingProfile>();
            cfg.AddProfile<LocationMappingProfile>();
        }).CreateMapper();
        _service = new TownService(
            _repo.Object,
            new Mock<ILocationRepository>().Object,
            new Mock<ILocationService>().Object,
            mapper,
            new Mock<ITeleportDestinationService>().Object,
            new Mock<IDomainRegionNameFinalizer>().Object,
            FixedWorldResolver.Instance);
    }

    private static TownDto Dto(string name, string description = "") => new()
    {
        Name = name,
        Description = description,
        WgRegionId = "domain_1",
    };

    [Fact]
    public async Task CreateAsync_StoresTheTrimmedNameAndDescription()
    {
        Town? saved = null;
        _repo.Setup(r => r.AddTownAsync(It.IsAny<Town>())).Callback<Town>(t => saved = t);

        var result = await _service.CreateAsync(Dto("Cinix\n", " The capital \r\n"));

        Assert.NotNull(saved);
        Assert.Equal("Cinix", saved!.Name);
        Assert.Equal("The capital", saved.Description);
        Assert.Equal("Cinix", result.Name);
    }

    [Fact]
    public async Task UpdateAsync_StoresTheTrimmedNameAndDescription()
    {
        var existing = new Town { Id = 3, Name = "Cinix", Description = "old", WgRegionId = "domain_3" };
        _repo.Setup(r => r.GetByIdAsync(3)).ReturnsAsync(existing);

        await _service.UpdateAsync(3, Dto("  Cinix Prime\n", "New\n"));

        Assert.Equal("Cinix Prime", existing.Name);
        Assert.Equal("New", existing.Description);
        _repo.Verify(r => r.UpdateTownAsync(existing), Times.Once);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData(" \r\n\t")]
    public async Task CreateAndUpdate_RejectAWhitespaceOnlyName(string name)
    {
        _repo.Setup(r => r.GetByIdAsync(3)).ReturnsAsync(new Town { Id = 3, Name = "Cinix" });

        var created = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(Dto(name)));
        var updated = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(3, Dto(name)));

        Assert.StartsWith("Town name is required.", created.Message);
        Assert.StartsWith("Town name is required.", updated.Message);
        _repo.Verify(r => r.AddTownAsync(It.IsAny<Town>()), Times.Never);
        _repo.Verify(r => r.UpdateTownAsync(It.IsAny<Town>()), Times.Never);
    }
}

public class DistrictServiceNameTrimmingTests
{
    private readonly Mock<IDistrictRepository> _repo = new();
    private readonly Mock<ITownRepository> _towns = new();
    private readonly DistrictService _service;

    public DistrictServiceNameTrimmingTests()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<DistrictMappingProfile>();
            cfg.AddProfile<LocationMappingProfile>();
        }).CreateMapper();
        _towns.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new Town { Id = 1, Name = "Cinix", WorldName = FixedWorldResolver.World });
        _service = new DistrictService(
            _repo.Object,
            _towns.Object,
            new Mock<ILocationRepository>().Object,
            new Mock<IStreetRepository>().Object,
            mapper,
            new Mock<IDomainRegionNameFinalizer>().Object,
            NullLogger<DistrictService>.Instance,
            new Mock<ITeleportDestinationService>().Object,
            FixedWorldResolver.Instance);
    }

    private static DistrictDto Dto(string name) => new()
    {
        Name = name,
        Description = "",
        WgRegionId = "domain_2",
        TownId = 1,
    };

    [Fact]
    public async Task CreateAsync_StoresTheTrimmedName()
    {
        District? saved = null;
        _repo.Setup(r => r.AddDistrictAsync(It.IsAny<District>())).Callback<District>(d => saved = d);

        await _service.CreateAsync(Dto("Residential District\n"));

        Assert.NotNull(saved);
        Assert.Equal("Residential District", saved!.Name);
    }

    [Fact]
    public async Task UpdateAsync_StoresTheTrimmedName()
    {
        var existing = new District { Id = 2, Name = "Residential District\n", TownId = 1, WgRegionId = "domain_2" };
        _repo.Setup(r => r.GetByIdAsync(2)).ReturnsAsync(existing);

        await _service.UpdateAsync(2, Dto("Merchant's District\n"));

        Assert.Equal("Merchant's District", existing.Name);
        _repo.Verify(r => r.UpdateDistrictAsync(existing), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_RejectsAWhitespaceOnlyName()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(Dto("\n")));

        Assert.StartsWith("District name is required.", ex.Message);
        _repo.Verify(r => r.AddDistrictAsync(It.IsAny<District>()), Times.Never);
    }
}

public class StructureServiceNameTrimmingTests
{
    private readonly Mock<IStructureRepository> _repo = new();
    private readonly Mock<IStreetRepository> _streets = new();
    private readonly Mock<IDistrictRepository> _districts = new();
    private readonly StructureService _service;

    public StructureServiceNameTrimmingTests()
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
            new Mock<ILocationRepository>().Object,
            new Mock<ILocationService>().Object,
            mapper,
            new Mock<ITeleportDestinationService>().Object,
            new Mock<IDomainRegionNameFinalizer>().Object,
            FixedWorldResolver.Instance);
    }

    private static StructureDto Dto(string name) => new()
    {
        Name = name,
        Description = "",
        WgRegionId = "domain_9",
        StreetId = 1,
        DistrictId = 2,
        HouseNumber = 4,
    };

    [Fact]
    public async Task CreateAsync_StoresTheTrimmedName()
    {
        Structure? saved = null;
        _repo.Setup(r => r.AddStructureAsync(It.IsAny<Structure>())).Callback<Structure>(s => saved = s);

        await _service.CreateAsync(Dto(" Smithy\n"));

        Assert.NotNull(saved);
        Assert.Equal("Smithy", saved!.Name);
    }

    [Fact]
    public async Task UpdateAsync_StoresTheTrimmedName()
    {
        var existing = new Structure { Id = 9, Name = "Smithy", StreetId = 1, DistrictId = 2, WgRegionId = "domain_9" };
        _repo.Setup(r => r.GetByIdAsync(9)).ReturnsAsync(existing);

        await _service.UpdateAsync(9, Dto("Forge\r\n"));

        Assert.Equal("Forge", existing.Name);
        _repo.Verify(r => r.UpdateStructureAsync(existing), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_RejectsAWhitespaceOnlyName()
    {
        _repo.Setup(r => r.GetByIdAsync(9)).ReturnsAsync(new Structure { Id = 9, Name = "Smithy" });

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(9, Dto(" \n ")));

        Assert.StartsWith("Structure name is required.", ex.Message);
        _repo.Verify(r => r.UpdateStructureAsync(It.IsAny<Structure>()), Times.Never);
    }
}

public class GateStructureServiceNameTrimmingTests
{
    private readonly Mock<IGateStructureRepository> _repo = new();
    private readonly GateStructureService _service;

    public GateStructureServiceNameTrimmingTests()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<GateStructureMappingProfile>();
            cfg.AddProfile<LocationMappingProfile>();
        }).CreateMapper();
        _service = new GateStructureService(
            _repo.Object,
            new Mock<ILocationRepository>().Object,
            new Mock<ILocationService>().Object,
            mapper,
            new Mock<ITeleportDestinationService>().Object,
            new Mock<IDomainRegionNameFinalizer>().Object,
            FixedWorldResolver.Instance);
    }

    private static GateStructureDto Dto(string name) => new()
    {
        Name = name,
        Description = "",
        WgRegionId = "domain_12",
        StreetId = 1,
        DistrictId = 2,
    };

    [Fact]
    public async Task CreateAsync_StoresTheTrimmedName()
    {
        GateStructure? saved = null;
        _repo.Setup(r => r.AddGateStructureAsync(It.IsAny<GateStructure>())).Callback<GateStructure>(g => saved = g);

        await _service.CreateAsync(Dto("North Gate\n"));

        Assert.NotNull(saved);
        Assert.Equal("North Gate", saved!.Name);
    }

    [Fact]
    public async Task UpdateAsync_StoresTheTrimmedName()
    {
        var existing = new GateStructure { Id = 12, Name = "North Gate", StreetId = 1, DistrictId = 2, WgRegionId = "domain_12" };
        _repo.Setup(r => r.GetByIdAsync(12)).ReturnsAsync(existing);

        await _service.UpdateAsync(12, Dto("\tSouth Gate \n"));

        Assert.Equal("South Gate", existing.Name);
        _repo.Verify(r => r.UpdateGateStructureAsync(existing), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_RejectsAWhitespaceOnlyName_BeforeTheReservedNameRule()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(Dto("\r\n")));

        Assert.StartsWith("GateStructure name is required.", ex.Message);
        _repo.Verify(r => r.AddGateStructureAsync(It.IsAny<GateStructure>()), Times.Never);
    }
}

/// <summary>PUT api/Domains/{id} and POST api/Domains take a Domain, not a typed DTO.</summary>
public class DomainServiceNameTrimmingTests
{
    private readonly Mock<IDomainRepository> _repo = new();
    private readonly DomainService _service;

    public DomainServiceNameTrimmingTests()
    {
        _service = new DomainService(_repo.Object, new Mock<IMapper>().Object,
            new Mock<IDomainRegionNameFinalizer>().Object, NullLogger<DomainService>.Instance, FixedWorldResolver.Instance);
    }

    [Fact]
    public async Task CreateAsync_StoresTheTrimmedNameAndDescription()
    {
        var domain = new Town { Name = "Cinix\n", Description = " capital\n", WgRegionId = "domain_1" };

        var created = await _service.CreateAsync(domain);

        Assert.Equal("Cinix", created.Name);
        Assert.Equal("capital", created.Description);
        _repo.Verify(r => r.AddDomainAsync(domain), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_StoresTheTrimmedNameAndDescription()
    {
        var existing = new District { Id = 5, Name = "Residential District\n", Description = "old", WgRegionId = "domain_5" };
        _repo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(existing);

        await _service.UpdateAsync(5, new Domain { Name = "Residential District\n", Description = "Homes\r\n", WgRegionId = "domain_5" });

        Assert.Equal("Residential District", existing.Name);
        Assert.Equal("Homes", existing.Description);
        _repo.Verify(r => r.UpdateDomainAsync(existing), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_RejectsAWhitespaceOnlyName()
    {
        _repo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new District { Id = 5, Name = "Residential District" });

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(5, new Domain { Name = "\n" }));

        Assert.StartsWith("Domain name is required.", ex.Message);
        _repo.Verify(r => r.UpdateDomainAsync(It.IsAny<Domain>()), Times.Never);
    }
}
