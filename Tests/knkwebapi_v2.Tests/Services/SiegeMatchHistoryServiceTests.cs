using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Statistics;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Alpha run 1 (PandiO/knk-workspace#174): the public siege results log. Finished matches only; match facts public;
/// a participant's own numbers follow their statistics visibility in the "siege" context (self and staff see all,
/// other signed-in players what is set to Everyone, anonymous visitors none).
///
/// Scenario 100 (SiegeTestData): Defender 201 "Cinix Garrison" (alliance 1), Attacker 202 "Raiders" (alliance 2).
/// </summary>
public class SiegeMatchHistoryServiceTests : IAsyncLifetime
{
    private KnKDbContext _context = null!;
    private SiegeMatchService _matches = null!;
    private SiegeMatchHistoryService _history = null!;

    private static readonly StatisticsViewer Anonymous = StatisticsViewer.Anonymous;
    private static StatisticsViewer SignedIn(int id) => new(StatisticsViewerKind.SignedIn, id);
    private static StatisticsViewer Staff(int id) => new(StatisticsViewerKind.Staff, id);

    public async Task InitializeAsync()
    {
        _context = SiegeTestData.NewContext("SiegeMatchHistory");
        await SiegeTestData.SeedValidScenarioAsync(_context);
        _context.SiegeLobbies.Add(new SiegeLobby
        {
            Id = 1, Name = "Siege - Cinix", Key = "cinix", IsEnabled = true,
            Rotation = { new SiegeLobbyScenario { SiegeScenarioId = 100 } }
        });
        _context.TitleBrackets.Add(new TitleBracket { Id = 1, MaleName = "Recruit", FemaleName = "Recruit", MinExperience = 0 });
        for (var id = 1; id <= 4; id++)
        {
            _context.Users.Add(new User { Id = id, Username = $"player{id}", Uuid = $"uuid-{id}", Coins = 10 });
        }
        _context.Users.Add(new User { Id = 9, Username = "gone", Uuid = "uuid-9", IsActive = false });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        _matches = SiegeTestData.MatchService(_context);
        _history = new SiegeMatchHistoryService(new SiegeMatchRepository(_context), new StatisticsRepository(_context));
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    /// <summary>Users 1, 2 defend (201), 3, 4 attack (202); user 4 leaves early; the attackers win by the Keep.</summary>
    private async Task<int> CompletedMatchAsync()
    {
        var created = await _matches.CreateAsync(new SiegeMatchCreateDto { SiegeLobbyId = 1, SiegeScenarioId = 100 });
        await _matches.StartAsync(created.Id, new SiegeMatchStartDto
        {
            Participants =
            {
                new SiegeMatchParticipantStartDto { UserId = 1, SiegeTeamId = 201 },
                new SiegeMatchParticipantStartDto { UserId = 2, SiegeTeamId = 201 },
                new SiegeMatchParticipantStartDto { UserId = 3, SiegeTeamId = 202 },
                new SiegeMatchParticipantStartDto { UserId = 4, SiegeTeamId = 202 }
            }
        });
        await _matches.ParticipantLeftAsync(created.Id, 4, new SiegeMatchParticipantLeftDto { LeftAt = DateTime.UtcNow.AddMinutes(-1) });
        await _matches.CompleteAsync(created.Id, new SiegeMatchCompleteDto
        {
            EndReason = SiegeMatchEndReason.InstantVictory,
            WinningAllianceGroup = 2,
            Participants =
            {
                new SiegeMatchParticipantResultDto { UserId = 1, SiegeTeamId = 201, Kills = 1, Deaths = 2 },
                new SiegeMatchParticipantResultDto { UserId = 2, SiegeTeamId = 201 },
                new SiegeMatchParticipantResultDto { UserId = 3, SiegeTeamId = 202, Kills = 4, Deaths = 0, Captures = 1, GateDamage = 250 }
            },
            Objectives =
            {
                new SiegeMatchObjectiveResultInputDto { SiegeObjectiveId = 502, FinalHolderTeamId = 202, CapturedByUserId = 3, CapturedAt = DateTime.UtcNow.AddMinutes(-2) },
                new SiegeMatchObjectiveResultInputDto { SiegeObjectiveId = 501, FinalHolderTeamId = 202, CapturedByUserId = 3, CapturedAt = DateTime.UtcNow.AddMinutes(-1) }
            }
        });
        _context.ChangeTracker.Clear();
        return created.Id;
    }

    private async Task MakePublicAsync(int userId, params string[] settings)
    {
        foreach (var setting in settings)
        {
            _context.PlayerStatVisibilities.Add(new PlayerStatVisibility
            {
                UserId = userId, SettingKey = setting, ContextKey = "", Visibility = StatisticVisibility.Everyone, UpdatedAt = DateTime.UtcNow
            });
        }
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task TheMatchPage_ShowsTheFactsToEveryone_AndHidesNumbersFromAnonymousVisitors()
    {
        var id = await CompletedMatchAsync();

        var page = await _history.GetMatchAsync(id, Anonymous);

        Assert.NotNull(page);
        Assert.Equal(("Siege - Cinix", "Siege of Cinix"), (page!.SiegeLobbyName, page.SiegeScenarioName));
        Assert.Equal(2, page.WinningAllianceGroup);
        Assert.Equal(new[] { ("Cinix Garrison", "Loss"), ("Raiders", "Win") }, page.Teams.Select(t => (t.Name, t.Outcome)));
        Assert.Equal(4, page.Participants.Count);
        Assert.All(page.Participants, p => Assert.Null(p.Kills));
        Assert.All(page.Participants, p => Assert.Null(p.CoinsAwarded));
        var p3 = page.Participants.Single(p => p.UserId == 3);
        Assert.Equal(("player3", "Win", true), (p3.Username, p3.Outcome, p3.PresentAtEnd));
        Assert.True(p3.ExpAwarded > 0, "XP is always public");
        var p4 = page.Participants.Single(p => p.UserId == 4);
        Assert.Equal(("Left", false), (p4.Outcome, p4.PresentAtEnd));
        // The Keep (instant victory) first, then the Gatehouse; both taken by player3.
        Assert.Equal(new int?[] { 501, 502 }, page.Objectives.Select(o => o.SiegeObjectiveId));
        Assert.All(page.Objectives, o => Assert.Equal("player3", o.Captures.Single().CapturedByUsername));
    }

    [Fact]
    public async Task NumbersFollowEachPlayersSiegeVisibility_ForOtherSignedInPlayers()
    {
        var id = await CompletedMatchAsync();
        await MakePublicAsync(3, "pvp_kills", "deaths", "economy");

        var page = (await _history.GetMatchAsync(id, SignedIn(2)))!;

        var p3 = page.Participants.Single(p => p.UserId == 3);
        Assert.Equal(4, p3.Kills);
        Assert.Equal(0, p3.Deaths);
        Assert.NotNull(p3.CoinsAwarded);
        Assert.Null(p3.Captures);   // objectives_captured stays private (no row = Nobody)
        Assert.Null(p3.GateDamage);
        var p1 = page.Participants.Single(p => p.UserId == 1);
        Assert.Null(p1.Kills);      // player1 set nothing
        var own = page.Participants.Single(p => p.UserId == 2);
        Assert.Equal(0, own.Kills); // the viewer sees their own row in full
        Assert.NotNull(own.CoinsAwarded);
    }

    [Fact]
    public async Task StaffSeeEveryNumber()
    {
        var id = await CompletedMatchAsync();

        var p3 = (await _history.GetMatchAsync(id, Staff(1)))!.Participants.Single(p => p.UserId == 3);

        Assert.Equal(4, p3.Kills);
        Assert.Equal(0, p3.Deaths);
        Assert.Equal(1, p3.Captures);
        Assert.Equal(250, p3.GateDamage);
    }

    [Fact]
    public async Task APlayersList_HasTheirFinishedMatchesOnly_WithTheirOwnRow()
    {
        var finished = await CompletedMatchAsync();
        var running = await _matches.CreateAsync(new SiegeMatchCreateDto { SiegeLobbyId = 1, SiegeScenarioId = 100 });
        await _matches.StartAsync(running.Id, new SiegeMatchStartDto
        {
            Participants = { new SiegeMatchParticipantStartDto { UserId = 3, SiegeTeamId = 202 } }
        });
        _context.ChangeTracker.Clear();

        var rows = await _history.GetPlayerMatchesAsync(3, new StatisticsViewer(StatisticsViewerKind.Self, 3), 0);

        var row = Assert.Single(rows!);
        Assert.Equal(finished, row.Id);
        Assert.Equal(("Raiders", "Win"), (row.Team!.Name, row.Team.Outcome));
        Assert.Equal(3, row.Participant.UserId);
        Assert.Equal(4, row.Participant.Kills);
        Assert.Equal(4, row.ParticipantCount);
    }

    [Fact]
    public async Task UnknownOrInactivePlayers_AndRunningMatches_AreNotFound()
    {
        Assert.Null(await _history.GetPlayerMatchesAsync(77, Anonymous, 5));
        Assert.Null(await _history.GetPlayerMatchesAsync(9, Anonymous, 5));

        var running = await _matches.CreateAsync(new SiegeMatchCreateDto { SiegeLobbyId = 1, SiegeScenarioId = 100 });
        Assert.Null(await _history.GetMatchAsync(running.Id, Anonymous));
    }

    [Fact]
    public async Task AnAbortedMatch_IsShownAsAborted()
    {
        var created = await _matches.CreateAsync(new SiegeMatchCreateDto { SiegeLobbyId = 1, SiegeScenarioId = 100 });
        await _matches.StartAsync(created.Id, new SiegeMatchStartDto
        {
            Participants = { new SiegeMatchParticipantStartDto { UserId = 1, SiegeTeamId = 201 } }
        });
        await _matches.AbortAsync(created.Id, new SiegeMatchAbortDto { EndReason = SiegeMatchEndReason.AdminStopped });
        _context.ChangeTracker.Clear();

        var page = (await _history.GetMatchAsync(created.Id, Anonymous))!;

        Assert.Equal(SiegeMatchStatus.Aborted, page.Status);
        Assert.All(page.Teams, t => Assert.Equal("Aborted", t.Outcome));
        Assert.Equal(("Aborted", true), (page.Participants.Single().Outcome, page.Participants.Single().PresentAtEnd));
    }
}
