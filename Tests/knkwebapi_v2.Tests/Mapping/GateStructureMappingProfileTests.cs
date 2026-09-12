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
}
