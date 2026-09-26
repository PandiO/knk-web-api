using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Moq;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Siege Phase 2 test data: one complete, READY scenario graph (docs/specs/siege-minigame/DESIGN.md
/// §3.9) that individual tests break one rule at a time.
///
/// Town "Cinix" (1, region "cinix") with districts 10 and 11 (and 12 in another town); scenario 100
/// with district 10; Defender team 201 sourced from clan 5, Attacker team 202 ad-hoc; one spawnpoint
/// each; selected gate 400 (in district 10, own location 1003); objective 501 "Keep"
/// (instant victory, location 1004) and objective 502 "Gatehouse" (gate 400, no own location).
/// </summary>
public static class SiegeTestData
{
    public static IMapper Mapper() => new MapperConfiguration(cfg =>
    {
        cfg.AddProfile<SiegeMappingProfile>();
        cfg.AddProfile<ClanMappingProfile>();
        cfg.AddProfile<LocationMappingProfile>();
        cfg.AddProfile<PagedQueryMappingProfile>();
    }).CreateMapper();

    public static Location Loc(int id, double x = 0, double z = 0) =>
        new() { Id = id, Name = $"loc{id}", X = x, Y = 64, Z = z, World = "world" };

    public static Town Town() => new()
    {
        Id = 1, Name = "Cinix", Description = "Capital", WgRegionId = "cinix"
    };

    public static SiegeScenario ValidScenario()
    {
        var town = Town();
        var district10 = new District { Id = 10, Name = "Old Town", Description = "d", WgRegionId = "cinix_old", TownId = 1, Town = town };
        var gateLocation = Loc(1003);
        var gate = new GateStructure
        {
            Id = 400, Name = "Main gate", Description = "g", WgRegionId = "cinix_gate",
            DistrictId = 10, District = district10, LocationId = gateLocation.Id, Location = gateLocation
        };

        var clanBanner = new BannerDesign { Id = 50, Name = "Cinix crown", BaseColor = BannerDyeColor.YELLOW };
        var clan = new Clan { Id = 5, Name = "Cinix Garrison", ChatColor = "GOLD", BannerDesignId = 50, BannerDesign = clanBanner };
        var raiderBanner = new BannerDesign { Id = 51, Name = "Raider skull", BaseColor = BannerDyeColor.BLACK };

        var scenario = new SiegeScenario
        {
            Id = 100, Name = "Siege of Cinix", TownId = 1, Town = town,
            HubLocationId = 1000, HubLocation = Loc(1000),
            PlayersMin = 2, PlayersMax = 20
        };
        scenario.Districts.Add(new SiegeScenarioDistrict { SiegeScenarioId = 100, DistrictId = 10, District = district10 });

        var defenders = new SiegeTeam
        {
            Id = 201, SiegeScenarioId = 100, SortOrder = 0, Role = SiegeTeamRole.Defender, AllianceGroup = 1,
            ClanId = 5, Clan = clan
        };
        defenders.Spawnpoints.Add(new SiegeSpawnpoint { Id = 301, SiegeTeamId = 201, SortOrder = 0, Name = "Keep", LocationId = 1001, Location = Loc(1001) });

        var attackers = new SiegeTeam
        {
            Id = 202, SiegeScenarioId = 100, SortOrder = 1, Role = SiegeTeamRole.Attacker, AllianceGroup = 2,
            Name = "Raiders", ChatColor = "RED", BannerDesignId = 51, BannerDesign = raiderBanner
        };
        attackers.Spawnpoints.Add(new SiegeSpawnpoint { Id = 302, SiegeTeamId = 202, SortOrder = 0, Name = "Camp", LocationId = 1002, Location = Loc(1002) });

        scenario.Teams.Add(defenders);
        scenario.Teams.Add(attackers);

        scenario.Gates.Add(new SiegeScenarioGate { SiegeScenarioId = 100, GateStructureId = 400, GateStructure = gate });

        scenario.Objectives.Add(new SiegeObjective
        {
            Id = 501, SiegeScenarioId = 100, SortOrder = 0, Name = "Keep", InstantVictory = true,
            LocationId = 1004, Location = Loc(1004)
        });
        scenario.Objectives.Add(new SiegeObjective
        {
            Id = 502, SiegeScenarioId = 100, SortOrder = 1, Name = "Gatehouse",
            GateStructureId = 400, GateStructure = gate
        });

        return scenario;
    }

    /// <summary>
    /// A "LocationInsideRegion" validation method that reports the given location ids as outside the
    /// region (or the plugin as unreachable), recording every call.
    /// </summary>
    public static Mock<IValidationMethod> RegionValidator(ISet<int>? outsideLocationIds = null, bool unreachable = false)
    {
        var mock = new Mock<IValidationMethod>();
        mock.SetupGet(v => v.ValidationType).Returns("LocationInsideRegion");
        mock.Setup(v => v.ValidateAsync(It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<Dictionary<string, object>?>()))
            .ReturnsAsync((object? field, object? dep, string? cfg, Dictionary<string, object>? ctx) =>
            {
                if (unreachable)
                    return new ValidationMethodResult
                    {
                        IsValid = false,
                        Message = "unreachable",
                        Metadata = new Dictionary<string, object> { { "failureReason", "PluginUnreachable" } }
                    };
                var location = (Location)field!;
                return outsideLocationIds != null && outsideLocationIds.Contains(location.Id)
                    ? new ValidationMethodResult { IsValid = false, Message = $"Location {location.X}, {location.Z} is outside Cinix's boundaries." }
                    : new ValidationMethodResult { IsValid = true };
            });
        return mock;
    }

    // ---- InMemory database (real repositories) ----

    public static KnKDbContext NewContext(string name) =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase($"{name}_{Guid.NewGuid()}").Options);

    /// <summary>
    /// Persists ValidScenario()'s graph (plus the shared rows it needs) and returns the ids-only
    /// shape - navigation properties are left to EF so nothing is tracked twice.
    /// </summary>
    public static async Task SeedValidScenarioAsync(KnKDbContext context)
    {
        AddSharedRows(context);

        context.SiegeScenarios.Add(new SiegeScenario
        {
            Id = 100, Name = "Siege of Cinix", TownId = 1, HubLocationId = 1000, PlayersMin = 2, PlayersMax = 20,
            Districts = { new SiegeScenarioDistrict { DistrictId = 10 } },
            Gates = { new SiegeScenarioGate { GateStructureId = 400 } },
            Teams =
            {
                new SiegeTeam
                {
                    Id = 201, SortOrder = 0, Role = SiegeTeamRole.Defender, AllianceGroup = 1, ClanId = 5,
                    Spawnpoints = { new SiegeSpawnpoint { Id = 301, Name = "Keep", LocationId = 1001 } }
                },
                new SiegeTeam
                {
                    Id = 202, SortOrder = 1, Role = SiegeTeamRole.Attacker, AllianceGroup = 2,
                    Name = "Raiders", ChatColor = "RED", BannerDesignId = 51,
                    Spawnpoints = { new SiegeSpawnpoint { Id = 302, Name = "Camp", LocationId = 1002 } }
                }
            },
            Objectives =
            {
                new SiegeObjective { Id = 501, SortOrder = 0, Name = "Keep", InstantVictory = true, LocationId = 1004 },
                new SiegeObjective { Id = 502, SortOrder = 1, Name = "Gatehouse", GateStructureId = 400 }
            }
        });

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    /// <summary>The shared world/catalog rows a scenario references, without any siege rows.</summary>
    public static async Task SeedSharedRowsAsync(KnKDbContext context)
    {
        AddSharedRows(context);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private static void AddSharedRows(KnKDbContext context)
    {
        var street = new Street { Id = 1, Name = "Main street" };
        context.Streets.Add(street);
        context.Towns.Add(new Town { Id = 1, Name = "Cinix", Description = "Capital", WgRegionId = "cinix" });
        context.Districts.Add(new District { Id = 10, Name = "Old Town", Description = "d", WgRegionId = "cinix_old", TownId = 1 });
        foreach (var id in new[] { 1000, 1001, 1002, 1003, 1004 })
            context.Locations.Add(Loc(id));
        context.GateStructures.Add(new GateStructure
        {
            Id = 400, Name = "Main gate", Description = "g", WgRegionId = "cinix_gate",
            DistrictId = 10, StreetId = 1, LocationId = 1003
        });
        context.BannerDesigns.Add(new BannerDesign { Id = 50, Name = "Cinix crown", BaseColor = BannerDyeColor.YELLOW });
        context.BannerDesigns.Add(new BannerDesign { Id = 51, Name = "Raider skull", BaseColor = BannerDyeColor.BLACK });
        context.Clans.Add(new Clan { Id = 5, Name = "Cinix Garrison", ChatColor = "GOLD", BannerDesignId = 50 });
    }
}
