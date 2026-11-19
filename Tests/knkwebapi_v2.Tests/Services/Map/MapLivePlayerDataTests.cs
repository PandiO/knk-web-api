using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Realtime.Presence;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Map;
using knkwebapi_v2.Tests.Services.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Map;

/// <summary>
/// Overview map L4 (PandiO/knk-workspace#738, O23, O24, A14, A15): name labels from the account (title, tier, group
/// colours) and the <c>knk.mode.*</c> rank; arena pins at the running match's objective gate with the participant count.
/// </summary>
public sealed class MapLivePlayerDataTests : IDisposable
{
    private const string OwnerUuid = "00000000-0000-0000-0000-000000000001";
    private const string StaffUuid = "00000000-0000-0000-0000-000000000002";
    private const string PremiumUuid = "00000000-0000-0000-0000-000000000003";
    private const string GuestUuid = "00000000-0000-0000-0000-000000000004";
    private const string NobodyUuid = "00000000-0000-0000-0000-000000000005";

    private readonly KnKDbContext _db;
    private readonly Mock<IUserService> _users = new();
    private readonly Mock<IPermissionResolutionService> _permissions = new();
    private readonly Mock<IMapPublicService> _map = new();
    private readonly PresenceRegistry _presence = new();
    private readonly DateTime _now = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

    public MapLivePlayerDataTests()
    {
        _db = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        _users.Setup(u => u.GetByUuidAsync(OwnerUuid)).ReturnsAsync(new UserDto { Id = 1, Username = "Pandi", TitleName = "King" });
        _users.Setup(u => u.GetByUuidAsync(StaffUuid)).ReturnsAsync(new UserDto { Id = 2, Username = "Mod", TitleName = "Knight" });
        _users.Setup(u => u.GetByUuidAsync(PremiumUuid)).ReturnsAsync(new UserDto
        {
            Id = 3, Username = "Rich", TitleName = "Squire", PremiumTierName = "Noble", ChatPrimaryColor = "&d", ChatSecondaryColor = "&5",
        });
        _permissions.Setup(p => p.CheckAnyAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<string>>()))
            .ReturnsAsync((int id, IReadOnlyList<string> nodes) =>
                id == 1 && nodes.Contains("knk.mode.owner") ? "knk.mode.owner"
                : (id == 1 || id == 2) && nodes.Contains("knk.mode.staff") ? "knk.mode.staff"
                : null);

        _map.Setup(m => m.GetMarkersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new MapMarkersDto
        {
            Structures =
            {
                new MapStructureMarkerDto { Id = 12, Name = "North Gate", Pin = new MapPinDto { World = "world", X = 31.5, Y = 70, Z = 40.5, Source = "location" } },
                new MapStructureMarkerDto { Id = 13, Name = "South Gate", Pin = new MapPinDto { World = "world", X = -10, Y = null, Z = 5, Source = "centroid" } },
            },
        });
    }

    public void Dispose() => _db.Dispose();

    private MapLivePlayerData Data() => new(_db, _users.Object, _permissions.Object, new PermissionReadCache(), _map.Object,
        new MapMarkerCache(), NullLogger<MapLivePlayerData>.Instance, _presence);

    private MapPlayerEntry Entry(string uuid, string? minigame = null, long? matchId = null) =>
        new("s1", uuid, "world", 0, 64, 0, 0, false, MapPlayerMode.None, minigame, matchId, _now, 2000);

    [Fact]
    public async Task Labels_OwnerStaffPremiumAndUnknown() // A15, O23
    {
        _presence.ServerConnected("s1", "c1");
        _presence.PlayerJoined("s1", new RosterEntry(GuestUuid, "Guest", "world", null));

        var labels = await Data().LabelsAsync(new[] { OwnerUuid, StaffUuid, PremiumUuid, GuestUuid, NobodyUuid });

        Assert.Equal(("OWNER", "King", "Pandi", "&6", "&5"), (labels[OwnerUuid].RankTag, labels[OwnerUuid].Braced, labels[OwnerUuid].Name, labels[OwnerUuid].BracketColor, labels[OwnerUuid].TextColor));
        Assert.Equal(("STAFF", "Knight", "&3", "&9"), (labels[StaffUuid].RankTag, labels[StaffUuid].Braced, labels[StaffUuid].BracketColor, labels[StaffUuid].TextColor));
        Assert.Equal(((string?)null, "Noble Squire", "&5", "&d"), (labels[PremiumUuid].RankTag, labels[PremiumUuid].Braced, labels[PremiumUuid].BracketColor, labels[PremiumUuid].TextColor));
        Assert.Equal(((string?)null, (string?)null, "Guest"), (labels[GuestUuid].RankTag, labels[GuestUuid].Braced, labels[GuestUuid].Name));
        Assert.False(labels.ContainsKey(NobodyUuid));
    }

    private void SeedSiege(SiegeMatchStatus status, int matchId = 500, int scenarioId = 7)
    {
        _db.SiegeMatches.Add(new SiegeMatch { Id = matchId, SiegeScenarioId = scenarioId, Status = status });
        _db.SiegeScenarioGates.Add(new SiegeScenarioGate { SiegeScenarioId = scenarioId, GateStructureId = 13 });
        _db.SiegeScenarioGates.Add(new SiegeScenarioGate { SiegeScenarioId = scenarioId, GateStructureId = 12 });
        _db.SiegeObjectives.Add(new SiegeObjective { Id = 70 + matchId, SiegeScenarioId = scenarioId, SortOrder = 1, Name = "Gate", GateStructureId = 13 });
        _db.SaveChanges();
    }

    [Fact]
    public async Task Arena_AtTheObjectiveGate_WithTheParticipantCount() // A14, O24, O31
    {
        SeedSiege(SiegeMatchStatus.InProgress);
        var live = new[] { Entry(OwnerUuid, "siege", 500), Entry(StaffUuid, "siege", 500), Entry(PremiumUuid) };

        var arena = Assert.Single(await Data().ArenasAsync(live));

        Assert.Equal(("Siege", "[Siege] 2 players", 2), (arena.Minigame, arena.Label, arena.PlayerCount));
        // The objective's gate (13), not the scenario's lowest gate id (12).
        Assert.Equal(("world", -10.0, (double?)null, 5.0), (arena.Pin.World, arena.Pin.X, arena.Pin.Y, arena.Pin.Z));
    }

    [Fact]
    public async Task Arena_WithoutObjectiveGate_UsesAScenarioGate()
    {
        SeedSiege(SiegeMatchStatus.InProgress);
        _db.SiegeObjectives.RemoveRange(_db.SiegeObjectives);
        _db.SaveChanges();

        var arena = Assert.Single(await Data().ArenasAsync(new[] { Entry(OwnerUuid, "siege", 500) }));

        Assert.Equal((31.5, (double?)70), (arena.Pin.X, arena.Pin.Y));
    }

    [Fact]
    public async Task NoArena_ForAnEndedMatch_AnUnknownMatchIdOrAGateWithoutPin()
    {
        SeedSiege(SiegeMatchStatus.Completed);
        SeedSiege(SiegeMatchStatus.InProgress, matchId: 501, scenarioId: 8);
        _map.Setup(m => m.GetMarkersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new MapMarkersDto());

        Assert.Empty(await Data().ArenasAsync(new[] { Entry(OwnerUuid, "siege", 500), Entry(StaffUuid, "siege", 501), Entry(PremiumUuid, "siege", null) }));
        Assert.Empty(await Data().ArenasAsync(new[] { Entry(OwnerUuid) }));
    }
}
