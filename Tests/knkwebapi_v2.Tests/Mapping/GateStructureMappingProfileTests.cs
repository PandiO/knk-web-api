using System.Text.Json;
using AutoMapper;
using FluentAssertions;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using Xunit;

namespace knkwebapi_v2.Tests.Mapping;

public class GateStructureMappingProfileTests
{
    private readonly IMapper _mapper = new MapperConfiguration(configuration =>
        configuration.AddProfile<GateStructureMappingProfile>()).CreateMapper();

    [Fact]
    public void MapToEntity_WhenWgRegionIdIsNull_UsesEmptyString()
    {
        var dto = new GateStructureDto
        {
            Name = "Test Gate 4",
            Description = "Test Gate",
            StreetId = 4,
            DistrictId = 8,
            WgRegionId = null!
        };

        var entity = _mapper.Map<GateStructure>(dto);

        entity.WgRegionId.Should().BeEmpty();
    }

    // The GateStructure seed has no House number field; a draft started on the older form still sends
    // "HouseNumber": null, which made the create a 400 ("could not be converted to System.Int32").
    [Fact]
    public void ANullHouseNumber_IsRead_AndANewGateKeepsZero()
    {
        var dto = JsonSerializer.Deserialize<GateStructureDto>(
            """{ "Name": "Keep Garrison", "Description": "None.", "DistrictId": 8, "StreetId": 4, "HouseNumber": null, "WgRegionId": "r" }""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        dto.HouseNumber.Should().BeNull();
        _mapper.Map<GateStructure>(dto).HouseNumber.Should().Be(0);
    }

    [Fact]
    public void ANullHouseNumber_OnUpdate_KeepsTheGatesNumber()
    {
        var existing = new GateStructure { Id = 1, Name = "Old Gate", Description = "d", WgRegionId = "r", HouseNumber = 3 };

        _mapper.Map(new GateStructureDto { Name = "Old Gate", Description = "d", WgRegionId = "r", StreetId = 4, DistrictId = 8 }, existing);
        existing.HouseNumber.Should().Be(3);

        _mapper.Map(new GateStructureDto { Name = "Old Gate", Description = "d", WgRegionId = "r", StreetId = 4, DistrictId = 8, HouseNumber = 7 }, existing);
        existing.HouseNumber.Should().Be(7);
    }
}
