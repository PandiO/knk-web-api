using AutoMapper;
using FluentAssertions;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using Xunit;

namespace knkwebapi_v2.Tests.Mapping;

public class TownMappingProfileTests
{
    private readonly IMapper _mapper = new MapperConfiguration(configuration =>
        configuration.AddProfile<TownMappingProfile>()).CreateMapper();

    [Fact]
    public void MapToDto_FillsStreetIdsAndDistrictIds()
    {
        // KNG-135: DistrictIds was never filled on read, so a form field authored as DistrictIds loaded empty.
        var town = new Town
        {
            Id = 1,
            Name = "Rivia",
            Description = "",
            WgRegionId = "town_rivia",
            Streets = { new Street { Id = 4, Name = "High Street" } },
            Districts =
            {
                new District { Id = 2, Name = "Old Quarter", Description = "", WgRegionId = "d", TownId = 1 },
                new District { Id = 3, Name = "Harbour", Description = "", WgRegionId = "h", TownId = 1 }
            }
        };

        var dto = _mapper.Map<TownDto>(town);

        dto.StreetIds.Should().Equal(4);
        dto.DistrictIds.Should().Equal(2, 3);
        dto.Districts.Select(d => d.Id).Should().Equal(2, 3);
    }
}
