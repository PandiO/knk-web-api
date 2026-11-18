using System.Text.Json;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Destination engine E12 (DESIGN § 10.3, PandiO/knk-workspace#103): the domain search returns each domain's own
/// Location, null when it has none, so one paged search fills the game server's navigation model. The other fields
/// are unchanged.
/// </summary>
public class DomainSearchLocationTests
{
    private readonly string _dbName = $"domain-search-location-{Guid.NewGuid()}";

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_dbName).Options);

    // Only the profiles the search uses: the Location map lives in DomainMappingProfile itself.
    private static IMapper Mapper() => new MapperConfiguration(cfg =>
    {
        cfg.AddProfile<DomainMappingProfile>();
        cfg.AddProfile<PagedQueryMappingProfile>();
    }).CreateMapper();

    private static DomainService Service(IDomainRepository repo) => new(repo, Mapper(),
        new Mock<IDomainRegionNameFinalizer>().Object, NullLogger<DomainService>.Instance, FixedWorldResolver.Instance);

    [Fact]
    public async Task TheRepositoryLoadsEachDomainsLocation()
    {
        await using (var db = NewContext())
        {
            var spawn = new Location { Name = "Oakhaven spawn", X = 10.5, Y = 64, Z = -20.25, Yaw = 90f, Pitch = 5f, World = "world" };
            db.Locations.Add(spawn);
            await db.SaveChangesAsync();
            db.Towns.Add(new Town { Name = "Oakhaven", Description = "", WgRegionId = "town_1", LocationId = spawn.Id });
            db.Districts.Add(new District { Name = "Old Quarter", Description = "", WgRegionId = "district_2" });
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        var page = await new DomainRepository(read).SearchAsync(new PagedQuery { PageNumber = 1, PageSize = 50, SortBy = "name" });

        var oakhaven = page.Items.Single(d => d.Name == "Oakhaven");
        Assert.NotNull(oakhaven.Location);
        Assert.Equal("Oakhaven spawn", oakhaven.Location!.Name);
        Assert.Null(page.Items.Single(d => d.Name == "Old Quarter").Location);
    }

    [Fact]
    public async Task TheSearchCarriesTheLocationOrNull()
    {
        var repo = new Mock<IDomainRepository>();
        repo.Setup(r => r.SearchAsync(It.IsAny<PagedQuery>())).ReturnsAsync(new PagedResult<Domain>
        {
            Items = new List<Domain>
            {
                new Town
                {
                    Id = 1, Name = "Oakhaven", Description = "the capital", WgRegionId = "town_1", WorldName = "world",
                    LocationId = 7,
                    Location = new Location { Id = 7, Name = "Oakhaven spawn", X = 10.5, Y = 64, Z = -20.25, Yaw = 90f, Pitch = 5f, World = "world" }
                },
                new District { Id = 2, Name = "Old Quarter", Description = "", WgRegionId = "district_2", WorldName = "world" },
            },
            TotalCount = 2,
            PageNumber = 1,
            PageSize = 50
        });
        repo.Setup(r => r.GetNavigationDefaultsAsync()).ReturnsAsync(
            new Dictionary<string, NavigationDestinationMode>(StringComparer.OrdinalIgnoreCase));
        repo.Setup(r => r.GetRoadAccessDefaultsAsync()).ReturnsAsync(
            new Dictionary<string, RoadAccessRule>(StringComparer.OrdinalIgnoreCase));

        var result = await Service(repo.Object).SearchAsync(new PagedQueryDto { PageNumber = 1, PageSize = 50 });

        var town = result.Items[0];
        Assert.NotNull(town.Location);
        Assert.Equal((7, "Oakhaven spawn", 10.5, 64.0, -20.25, 90f, 5f, "world"),
            (town.Location!.Id, town.Location.Name, town.Location.X, town.Location.Y, town.Location.Z,
                town.Location.Yaw, town.Location.Pitch, town.Location.World));
        Assert.Null(result.Items[1].Location);

        // The fields the plugin and the web app already read are unchanged.
        Assert.Equal((1, "Oakhaven", "the capital", "town_1", "world", "Town", "Spawn", "Applies"),
            (town.Id, town.Name, town.Description, town.WgRegionId, town.WorldName, town.DomainType,
                town.NavigationDefault, town.RoadAccess));
        Assert.Equal(("district_2", "District"), (result.Items[1].WgRegionId, result.Items[1].DomainType));
    }

    [Fact]
    public void TheJsonAlwaysHasTheLocationKeyAndKeepsTheOtherFields()
    {
        var withLocation = new DomainListDto
        {
            Id = 1, Name = "Oakhaven", Description = "", WgRegionId = "town_1", DomainType = "Town",
            Location = new LocationDto { Id = 7, Name = "Oakhaven spawn", X = 1, Y = 2, Z = 3, Yaw = 0, Pitch = 0, World = "world" }
        };
        var without = new DomainListDto { Id = 2, Name = "Old Quarter", Description = "", WgRegionId = "d", DomainType = "District" };

        using var a = JsonDocument.Parse(JsonSerializer.Serialize(withLocation));
        using var b = JsonDocument.Parse(JsonSerializer.Serialize(without,
            new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }));

        Assert.Equal("world", a.RootElement.GetProperty("location").GetProperty("world").GetString());
        Assert.Equal(7, a.RootElement.GetProperty("location").GetProperty("id").GetInt32());
        // Null is written even where the host drops nulls: the game server tells "no Location" from an older API.
        Assert.Equal(JsonValueKind.Null, b.RootElement.GetProperty("location").ValueKind);
        foreach (var key in new[] { "id", "name", "description", "wgRegionId", "worldName", "parentDomainId", "parentDomain", "domainType", "navigationDefault", "roadAccess" })
        {
            Assert.True(a.RootElement.TryGetProperty(key, out _), key);
        }
    }
}
