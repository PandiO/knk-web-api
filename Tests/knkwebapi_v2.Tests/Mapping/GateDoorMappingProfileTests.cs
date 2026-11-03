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
            ClosedRegionData = null!,
            OpenedRegionData = null!,
            SeedBlocks = null!,
            ScanMaterialWhitelist = null!,
            ScanMaterialBlacklist = null!,
            PassThroughConditionsJson = null!
        };

        var entity = _mapper.Map<GateDoor>(dto);

        entity.ClosedRegionData.Should().BeEmpty();
        entity.OpenedRegionData.Should().BeEmpty();
        entity.SeedBlocks.Should().BeEmpty();
        entity.ScanMaterialWhitelist.Should().BeEmpty();
        entity.ScanMaterialBlacklist.Should().BeEmpty();
        entity.PassThroughConditionsJson.Should().BeEmpty();
    }

    [Fact]
    public void MapToEntity_WhenPassThroughStylesAreLeftOut_KeepsThePre167Styles()
    {
        // PandiO/knk-workspace#167: a client that doesn't send the styles (the plugin, an older web app)
        // gets the styles players could pick before: animated, walk-path, teleport; snap off; animated default.
        var entity = _mapper.Map<GateDoor>(new GateDoorDto { GateStructureId = 4, Name = "Old Door" });

        entity.AllowAnimatedPassThrough.Should().BeTrue();
        entity.AllowSnapPassThrough.Should().BeFalse();
        entity.AllowWalkPathPassThrough.Should().BeTrue();
        entity.AllowTeleportPassThrough.Should().BeTrue();
        entity.DefaultPassThroughMethod.Should().Be(GatePassThroughMethod.Default);
    }

    [Fact]
    public void PassThroughStyles_RoundTripBetweenDtoAndEntity()
    {
        var entity = _mapper.Map<GateDoor>(new GateDoorDto
        {
            GateStructureId = 4,
            Name = "Snap Door",
            AllowAnimatedPassThrough = false,
            AllowSnapPassThrough = true,
            AllowWalkPathPassThrough = false,
            AllowTeleportPassThrough = true,
            DefaultPassThroughMethod = GatePassThroughMethod.Snap
        });

        // Every profile, as the API registers them (the read map also maps the door's nested locations).
        var fullMapper = new MapperConfiguration(cfg => cfg.AddMaps(typeof(GateDoorMappingProfile).Assembly)).CreateMapper();
        var dto = fullMapper.Map<GateDoorDto>(entity);

        dto.AllowAnimatedPassThrough.Should().BeFalse();
        dto.AllowSnapPassThrough.Should().BeTrue();
        dto.AllowWalkPathPassThrough.Should().BeFalse();
        dto.AllowTeleportPassThrough.Should().BeTrue();
        dto.DefaultPassThroughMethod.Should().Be(GatePassThroughMethod.Snap);
    }
}
