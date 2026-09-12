using AutoMapper;
using FluentAssertions;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using Xunit;

namespace knkwebapi_v2.Tests.Mapping;

// Covers the null-handling that GateStructureMappingProfileTests used to verify for these
// fields before item 5 moved them from GateStructure onto GateDoor - see
// docs/features/gate-structure-animation/GATESTRUCTURE_QOL_IMPLEMENTATION_PLAN.md.
public class GateDoorMappingProfileTests
{
    private readonly IMapper _mapper = new MapperConfiguration(configuration =>
        configuration.AddProfile<GateDoorMappingProfile>()).CreateMapper();

    [Fact]
    public void MapToEntity_WhenOptionalRegionAndJsonFieldsAreNull_UsesEmptyStrings()
    {
        var dto = new GateDoorDto
        {
            GateStructureId = 4,
            Name = "Test Door",
            RegionClosedId = null!,
            RegionOpenedId = null!,
            SeedBlocks = null!,
            ScanMaterialWhitelist = null!,
            ScanMaterialBlacklist = null!,
            PassThroughConditionsJson = null!
        };

        var entity = _mapper.Map<GateDoor>(dto);

        entity.RegionClosedId.Should().BeEmpty();
        entity.RegionOpenedId.Should().BeEmpty();
        entity.SeedBlocks.Should().BeEmpty();
        entity.ScanMaterialWhitelist.Should().BeEmpty();
        entity.ScanMaterialBlacklist.Should().BeEmpty();
        entity.PassThroughConditionsJson.Should().BeEmpty();
    }
}
