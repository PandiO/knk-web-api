using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Survival;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Survival;

/// <summary>
/// The durable survival match result on InMemory with the real repositories and ledger (DESIGN §3, §9, §10, §11;
/// S32, S59, S62; PandiO/knk-workspace#41, link S8a): stored once and settled once through one SURVIVAL_REWARD posting
/// keyed siege-survival:{matchId} (floor of the exact accruals, present players only), idempotent replay, 409 on a
/// different body or a settlement the API recomputes differently, nothing for aborts and leavers, the open Mystery Box
/// rolls forfeited at the end, the history list, the checkpoint upsert, and the Siege match hooks (no PvP legs for a
/// survival scenario; the abort writes the result row).
///
/// Scenario 100 has survival settings (SurvivalTestData), scenario 101 is an ordinary PvP scenario. Users 1-6 start
/// with 10 coins / 0 XP; brackets Recruit (0), Squire (30 XP, +7 coins, +1 gem, +2 XP), Knight (1000).
/// </summary>
public class SurvivalMatchResultServiceTests : IAsyncLifetime
{
    private const int InProgressMatch = 900;
    private const int CreatedMatch = 901;
    private const int CompletedMatch = 902;
    private const int PvpMatch = 903;

    private KnKDbContext _context = null!;
    private SurvivalMatchResultService _service = null!;
    private Mock<IPlayerNotificationQueue> _notifications = null!;

    public async Task InitializeAsync()
    {
        _context = SiegeTestData.NewContext("SurvivalMatchResult");
        await SiegeTestData.SeedValidScenarioAsync(_context);
        await SurvivalTestData.SeedReadySurvivalAsync(_context);

        _context.SiegeLobbies.Add(new SiegeLobby { Id = 1, Name = "Siege - Cinix", Key = "cinix", IsEnabled = true, Rotation = { new SiegeLobbyScenario { SiegeScenarioId = 100 } } });
        _context.TitleBrackets.Add(new TitleBracket { Id = 1, MaleName = "Recruit", FemaleName = "Recruit", MinExperience = 0 });
        _context.TitleBrackets.Add(new TitleBracket { Id = 2, MaleName = "Squire", FemaleName = "Squire", MinExperience = 30, CoinBonus = 7, GemBonus = 1, ExpBonus = 2 });
        _context.TitleBrackets.Add(new TitleBracket { Id = 3, MaleName = "Knight", FemaleName = "Dame", MinExperience = 1000 });
        for (var id = 1; id <= 6; id++)
            _context.Users.Add(new User { Id = id, Username = $"player{id}", Uuid = $"uuid-{id}", Coins = 10, Gems = 0, ExperiencePoints = 0 });
        _context.SiegeScenarios.Add(new SiegeScenario { Id = 101, Name = "Elsewhere", TownId = 1, HubLocationId = 1000 });

        var now = DateTime.UtcNow;
        _context.SiegeMatches.Add(new SiegeMatch
        {
            Id = InProgressMatch, SiegeLobbyId = 1, SiegeScenarioId = 100, Status = SiegeMatchStatus.InProgress, StartedAt = now.AddMinutes(-20),
            Participants =
            {
                new SiegeMatchParticipant { UserId = 1, SiegeTeamId = 201, Kills = 10, Deaths = 1 },
                new SiegeMatchParticipant { UserId = 3, SiegeTeamId = 201, Kills = 2 },
                new SiegeMatchParticipant { UserId = 5, SiegeTeamId = 201, LeftAt = now.AddMinutes(-5) }
            }
        });
        _context.SiegeMatches.Add(new SiegeMatch { Id = CreatedMatch, SiegeLobbyId = 1, SiegeScenarioId = 100, Status = SiegeMatchStatus.Created });
        _context.SiegeMatches.Add(new SiegeMatch
        {
            Id = CompletedMatch, SiegeLobbyId = 1, SiegeScenarioId = 100, Status = SiegeMatchStatus.Completed, StartedAt = now.AddHours(-2), EndedAt = now.AddHours(-1),
            EndReason = SiegeMatchEndReason.TeamEliminated,
            Participants = { new SiegeMatchParticipant { UserId = 3, SiegeTeamId = 201 } }
        });
        _context.SiegeMatches.Add(new SiegeMatch
        {
            Id = PvpMatch, SiegeLobbyId = 1, SiegeScenarioId = 101, Status = SiegeMatchStatus.InProgress, StartedAt = now,
            Participants = { new SiegeMatchParticipant { UserId = 1, SiegeTeamId = null } }
        });

        // Match 900's Mystery Box rolls (S7a rows): one still open, one claimed, one forfeited on its timeout.
        _context.SurvivalMysteryBoxRolls.Add(Roll("r1", 1, SurvivalMysteryBoxRollState.Rolled));
        var claimed = Roll("r2", 1, SurvivalMysteryBoxRollState.Claimed);
        claimed.ClaimedAt = now;
        claimed.Delivery = new SurvivalMysteryBoxDelivery { Kind = SurvivalMysteryBoxDeliveryKind.Inventory, DeliveredAt = now };
        _context.SurvivalMysteryBoxRolls.Add(claimed);
        var forfeited = Roll("r3", 3, SurvivalMysteryBoxRollState.Forfeited);
        forfeited.ForfeitedAt = now;
        forfeited.ForfeitReason = SurvivalMysteryBoxForfeitReason.Timeout;
        _context.SurvivalMysteryBoxRolls.Add(forfeited);

        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        _notifications = new Mock<IPlayerNotificationQueue>();
        _service = SiegeTestData.SurvivalResultService(_context, notifications: _notifications.Object);
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    private static SurvivalMysteryBoxRoll Roll(string rollId, int userId, SurvivalMysteryBoxRollState state) => new()
    {
        RollId = rollId, SiegeMatchId = InProgressMatch, SurvivalMysteryBoxPointId = 50, SurvivalMysteryBoxPoolId = 40, UserId = userId,
        Variant = SurvivalMysteryBoxVariant.Items, SurvivalMysteryBoxEntryId = 1, PriceCharged = 1000, State = state, RolledAt = DateTime.UtcNow.AddMinutes(-1)
    };

    // ---- helpers ----

    private static SurvivalPlayerResultDto Player(int userId, bool present, decimal coins, decimal xp, int coinsSettled, int xpSettled, int kills = 0, int bonus = 0, int? kitId = null) => new()
    {
        UserId = userId, PresentAtEnd = present, LeftAt = present ? null : new DateTime(2026, 11, 7, 10, 20, 0, DateTimeKind.Utc), KitId = kitId,
        Kills = kills, BonusKills = bonus, Deaths = present ? 1 : 0, RepairWork = 12.5,
        MatchCurrencyEarned = kills * 70L, MatchCurrencySpent = 1000, FinalMatchBalance = kills * 70L - 1000,
        CoinsAccrued = coins, XpAccrued = xp, CoinsSettled = coinsSettled, XpSettled = xpSettled
    };

    /// <summary>A team wipe in wave 10: user 1 settles 303 / 30 (reaches Squire), user 3 settles 63 / 6, user 5 left early.</summary>
    private static SurvivalMatchResultDto TeamWipe(int matchId = InProgressMatch) => new()
    {
        SiegeMatchId = matchId, SiegeLobbyId = 1, SiegeScenarioId = 100, DifficultyProfileId = 10,
        ConfigurationGeneratedAt = new DateTime(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc),
        StartedAt = new DateTime(2026, 11, 7, 10, 1, 0, DateTimeKind.Utc), EndedAt = new DateTime(2026, 11, 7, 10, 31, 0, DateTimeKind.Utc),
        EndReason = SurvivalEndReason.TeamWipe, ReachedWave = 10, CompletedWave = 9, EarnedKills = 123, SystemKills = 4,
        EquipmentMode = SurvivalEquipmentMode.Kits,
        Players =
        {
            Player(1, true, 303.0m, 30.3m, 303, 30, kills: 10, bonus: 1, kitId: SurvivalTestData.KitSword),
            Player(3, true, 63.9m, 6.3m, 63, 6, kills: 2),
            Player(5, false, 30.0m, 3.0m, 0, 0, kills: 1)
        },
        Settlement = new SurvivalSettlementDto { SettlesRewards = true, TotalCoins = 366, TotalXp = 36, LedgerKey = "siege-survival:900" }
    };

    private static SurvivalMatchResultDto NoPlayersLeft() => new()
    {
        SiegeLobbyId = 1, SiegeScenarioId = 100, EndReason = SurvivalEndReason.NoPlayersLeft, ReachedWave = 3, CompletedWave = 2,
        Players = { Player(1, false, 90.0m, 9.0m, 0, 0, kills: 3), Player(3, false, 0m, 0m, 0, 0) },
        Settlement = new SurvivalSettlementDto { SettlesRewards = false, TotalCoins = 0, TotalXp = 0 }
    };

    private CurrencyService Currency() =>
        new(new CurrencyRepository(_context), new UserRepository(_context), NullLogger<CurrencyService>.Instance);

    private async Task<User> UserAsync(int id)
    {
        _context.ChangeTracker.Clear();
        return await _context.Users.AsNoTracking().SingleAsync(u => u.Id == id);
    }

    private async Task<SurvivalMysteryBoxRoll> RollAsync(string rollId)
    {
        _context.ChangeTracker.Clear();
        return await _context.SurvivalMysteryBoxRolls.AsNoTracking().SingleAsync(r => r.RollId == rollId);
    }

    private static Task<SurvivalMatchResultConflictException> Conflict(Func<Task> act) => Assert.ThrowsAsync<SurvivalMatchResultConflictException>(act);

    // ==== Post: settlement ====

    [Fact]
    public async Task PostResult_StoresTheRow_SettlesOnceThroughTheLedger_AndForfeitsOpenRolls()
    {
        var (result, created) = await _service.PostResultAsync(InProgressMatch, TeamWipe());

        Assert.True(created);
        Assert.Equal((InProgressMatch, SurvivalEndReason.TeamWipe, 10, 9, 123, 4), (result.SiegeMatchId, result.EndReason, result.ReachedWave, result.CompletedWave, result.EarnedKills, result.SystemKills));
        Assert.Equal((SurvivalEquipmentMode.Kits, SurvivalMatchResult.RewardPolicyV1PerKill, SurvivalMatchResultSource.Plugin), (result.EquipmentMode, result.RewardPolicyVersion, result.Source));
        Assert.NotNull(result.RecordedAt);
        Assert.Equal((true, 366, 36, "siege-survival:900", 2), (result.Settlement.SettlesRewards, result.Settlement.TotalCoins, result.Settlement.TotalXp, result.Settlement.LedgerKey, result.Settlement.PlayersSettled));
        Assert.NotNull(result.Settlement.SettledAt);
        Assert.False(string.IsNullOrEmpty(result.Settlement.LedgerPostingId));

        // One SURVIVAL_REWARD posting with the deterministic key: the settled integers, present players only.
        var posting = await Currency().FindAsync(CurrencyIdempotencyScopes.System, "siege-survival:900");
        Assert.NotNull(posting);
        Assert.Equal(CurrencyReasons.SurvivalReward, posting!.ReasonCode);
        Assert.Equal(result.Settlement.LedgerPostingId, posting.PublicId);
        var legs = posting.Entries.Where(e => e.UserId is 1 or 3 or 5).Select(e => (e.UserId, e.Currency, e.Amount)).OrderBy(x => x.UserId).ThenBy(x => x.Currency).ToList();
        Assert.Equal(new[] { (1, "Coins", 303L), (1, "Experience", 30L), (3, "Coins", 63L), (3, "Experience", 6L) }, legs);
        Assert.Null(await Currency().FindAsync(CurrencyIdempotencyScopes.System, "siege-match:900"));

        // Balances: user 1 crosses Squire (30 XP) and gets its one-time bonus through the shared title path; user 5 forfeited.
        var u1 = await UserAsync(1);
        Assert.Equal((10 + 303 + 7, 1, 30 + 2), (u1.Coins, u1.Gems, u1.ExperiencePoints));
        var u3 = await UserAsync(3);
        Assert.Equal((10 + 63, 0, 6), (u3.Coins, u3.Gems, u3.ExperiencePoints));
        var u5 = await UserAsync(5);
        Assert.Equal((10, 0, 0), (u5.Coins, u5.Gems, u5.ExperiencePoints));
        _notifications.Verify(n => n.Enqueue(1, "uuid-1", "player1", PlayerNotificationTypes.TitleChanged, It.IsAny<TitleChangeResultDto>()), Times.Once);
        _notifications.Verify(n => n.Enqueue(3, It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TitleChangeResultDto?>()), Times.Never);

        // Players: the stored statistics, leaver flagged, Mystery Box counts from the roll rows (r1 forfeited now).
        var p1 = result.Players.Single(p => p.UserId == 1);
        Assert.Equal((true, SurvivalTestData.KitSword, 10, 1, 303.0m, 30.3m, 303, 30), (p1.PresentAtEnd, p1.KitId, p1.Kills, p1.BonusKills, p1.CoinsAccrued, p1.XpAccrued, p1.CoinsSettled, p1.XpSettled));
        Assert.Equal((2, 1, 1), (p1.MysteryBoxRolls, p1.MysteryBoxClaims, p1.MysteryBoxForfeits));
        var p3 = result.Players.Single(p => p.UserId == 3);
        Assert.Equal((1, 0, 1), (p3.MysteryBoxRolls, p3.MysteryBoxClaims, p3.MysteryBoxForfeits));
        var p5 = result.Players.Single(p => p.UserId == 5);
        Assert.Equal((false, 30.0m, 0, 0), (p5.PresentAtEnd, p5.CoinsAccrued, p5.CoinsSettled, p5.XpSettled));
        Assert.NotNull(p5.LeftAt);
        var r1 = await RollAsync("r1");
        Assert.Equal((SurvivalMysteryBoxRollState.Forfeited, SurvivalMysteryBoxForfeitReason.MatchEnded), (r1.State, r1.ForfeitReason));
        Assert.Equal(SurvivalMysteryBoxForfeitReason.Timeout, (await RollAsync("r3")).ForfeitReason); // first reason kept
        Assert.Equal(SurvivalMysteryBoxRollState.Claimed, (await RollAsync("r2")).State);

        _context.ChangeTracker.Clear();
        var row = await _context.SurvivalMatchResults.Include(r => r.Players).SingleAsync();
        Assert.Equal((3, 2, "siege-survival:900", SurvivalMatchResultSource.Plugin), (row.PlayersAtStart, row.PlayersAtEnd, row.LedgerKey, row.Source));
        Assert.Equal(SurvivalMatchResultService.Fingerprint(TeamWipe()), row.RequestFingerprint);
        Assert.True(row.Players.Single(p => p.UserId == 5).LeftEarly);
        // The Siege match itself is untouched: the plugin still calls POST api/siege-matches/{id}/complete.
        Assert.Equal(SiegeMatchStatus.InProgress, (await _context.SiegeMatches.AsNoTracking().SingleAsync(m => m.Id == InProgressMatch)).Status);
    }

    [Fact]
    public async Task PostResult_SameBodyAgain_AnswersTheStoredResult_AndSettlesNothingTwice()
    {
        var (first, _) = await _service.PostResultAsync(InProgressMatch, TeamWipe());
        _context.ChangeTracker.Clear();

        var (again, created) = await _service.PostResultAsync(InProgressMatch, TeamWipe());

        Assert.False(created);
        Assert.Equal((first.RecordedAt, first.Settlement.SettledAt, first.Settlement.LedgerPostingId), (again.RecordedAt, again.Settlement.SettledAt, again.Settlement.LedgerPostingId));
        Assert.Equal(10 + 303 + 7, (await UserAsync(1)).Coins);
        Assert.Equal(1, await _context.SurvivalMatchResults.CountAsync());
        Assert.Equal(1, await _context.CurrencyTransactions.CountAsync(t => t.ReasonCode == CurrencyReasons.SurvivalReward));
        // The idempotency ignores the API-filled fields and the player order.
        var reordered = TeamWipe();
        reordered.Players.Reverse();
        reordered.RecordedAt = DateTime.UtcNow;
        reordered.Source = SurvivalMatchResultSource.Abort;
        reordered.Players[0].MysteryBoxRolls = 99;
        Assert.False((await _service.PostResultAsync(InProgressMatch, reordered)).Created);
    }

    [Fact]
    public async Task PostResult_DifferentBody_Is409ResultAlreadyStored()
    {
        await _service.PostResultAsync(InProgressMatch, TeamWipe());
        _context.ChangeTracker.Clear();

        var other = TeamWipe();
        other.ReachedWave = 11;
        var ex = await Conflict(() => _service.PostResultAsync(InProgressMatch, other));
        Assert.Equal(SurvivalMatchResultConflictException.ResultAlreadyStored, ex.Code);
        Assert.Equal(10 + 303 + 7, (await UserAsync(1)).Coins);
    }

    [Theory]
    [InlineData("settled-up")]
    [InlineData("settled-down")]
    [InlineData("xp")]
    [InlineData("leaver-paid")]
    [InlineData("totals")]
    [InlineData("flag")]
    [InlineData("key")]
    public async Task PostResult_SettlementTheApiRecomputesDifferently_Is409SettlementMismatch_AndStoresNothing(string variant)
    {
        var dto = TeamWipe();
        switch (variant)
        {
            case "settled-up": dto.Players[0].CoinsSettled = 304; dto.Settlement.TotalCoins = 367; break;
            case "settled-down": dto.Players[1].CoinsSettled = 62; dto.Settlement.TotalCoins = 365; break; // 63.9 floors to 63, not 62
            case "xp": dto.Players[0].XpSettled = 31; dto.Settlement.TotalXp = 37; break; // 30.3 floors to 30
            case "leaver-paid": dto.Players[2].CoinsSettled = 30; dto.Players[2].XpSettled = 3; dto.Settlement.TotalCoins = 396; dto.Settlement.TotalXp = 39; break;
            case "totals": dto.Settlement.TotalCoins = 400; break;
            case "flag": dto.EndReason = SurvivalEndReason.AdminAbort; break; // settlesRewards true but S62 says nothing settles
            case "key": dto.Settlement.LedgerKey = "siege-match:900"; break;
        }

        var ex = await Conflict(() => _service.PostResultAsync(InProgressMatch, dto));

        Assert.Equal(SurvivalMatchResultConflictException.SettlementMismatch, ex.Code);
        _context.ChangeTracker.Clear();
        Assert.Equal(0, await _context.SurvivalMatchResults.CountAsync());
        Assert.Null(await Currency().FindAsync(CurrencyIdempotencyScopes.System, "siege-survival:900"));
        Assert.Equal(10, (await UserAsync(1)).Coins);
        Assert.Equal(SurvivalMysteryBoxRollState.Rolled, (await RollAsync("r1")).State);
    }

    [Fact]
    public async Task PostResult_EndReasonThatDoesNotSettle_StoresTheRow_PostsNothing()
    {
        var (result, created) = await _service.PostResultAsync(InProgressMatch, NoPlayersLeft());

        Assert.True(created);
        Assert.Equal((false, 0, 0, 0), (result.Settlement.SettlesRewards, result.Settlement.TotalCoins, result.Settlement.TotalXp, result.Settlement.PlayersSettled));
        Assert.Null(result.Settlement.SettledAt);
        Assert.Null(result.Settlement.LedgerPostingId);
        Assert.Null(result.Settlement.LedgerKey);
        Assert.Equal(90.0m, result.Players.Single(p => p.UserId == 1).CoinsAccrued); // the forfeited accrual stays in history
        Assert.Equal(SurvivalEquipmentMode.OwnInventory, result.EquipmentMode); // from the scenario's settings
        Assert.Null(await Currency().FindAsync(CurrencyIdempotencyScopes.System, "siege-survival:900"));
        Assert.Equal(10, (await UserAsync(1)).Coins);
        Assert.Equal(SurvivalMysteryBoxForfeitReason.MatchEnded, (await RollAsync("r1")).ForfeitReason);
        _notifications.Verify(n => n.Enqueue(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TitleChangeResultDto?>()), Times.Never);
    }

    [Fact]
    public async Task PostResult_SettlingEndReasonWithNothingAccrued_MarksSettledWithoutAPosting()
    {
        var dto = TeamWipe();
        foreach (var p in dto.Players) { p.CoinsAccrued = 0; p.XpAccrued = 0; p.CoinsSettled = 0; p.XpSettled = 0; }
        dto.Settlement.TotalCoins = 0;
        dto.Settlement.TotalXp = 0;

        var (result, _) = await _service.PostResultAsync(InProgressMatch, dto);

        Assert.True(result.Settlement.SettlesRewards);
        Assert.NotNull(result.Settlement.SettledAt);
        Assert.Null(result.Settlement.LedgerPostingId);
        Assert.Equal(0, result.Settlement.PlayersSettled);
        Assert.Null(await Currency().FindAsync(CurrencyIdempotencyScopes.System, "siege-survival:900"));
    }

    [Fact]
    public async Task PostResult_UnknownCreatedOrForeignMatch_Fails()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.PostResultAsync(999, TeamWipe(999)));
        var created = await Conflict(() => _service.PostResultAsync(CreatedMatch, TeamWipe(CreatedMatch)));
        Assert.Equal(SurvivalMatchResultConflictException.MatchNotInProgress, created.Code);
        // A completed Siege match (the plugin called complete first) still takes its survival result.
        var dto = TeamWipe(CompletedMatch);
        dto.Settlement.LedgerKey = null;
        Assert.True((await _service.PostResultAsync(CompletedMatch, dto)).Created);
        Assert.NotNull(await Currency().FindAsync(CurrencyIdempotencyScopes.System, "siege-survival:902"));
    }

    [Fact]
    public async Task PostResult_ValidationFailures_Are400()
    {
        var waves = TeamWipe(); waves.CompletedWave = 11;
        await Assert.ThrowsAsync<ArgumentException>(() => _service.PostResultAsync(InProgressMatch, waves));
        var route = TeamWipe(); route.SiegeMatchId = 901;
        await Assert.ThrowsAsync<ArgumentException>(() => _service.PostResultAsync(InProgressMatch, route));
        var user = TeamWipe(); user.Players[0].UserId = 99;
        await Assert.ThrowsAsync<ArgumentException>(() => _service.PostResultAsync(InProgressMatch, user));
        var twice = TeamWipe(); twice.Players[1].UserId = 1;
        await Assert.ThrowsAsync<ArgumentException>(() => _service.PostResultAsync(InProgressMatch, twice));
        var bonus = TeamWipe(); bonus.Players[0].BonusKills = 11;
        await Assert.ThrowsAsync<ArgumentException>(() => _service.PostResultAsync(InProgressMatch, bonus));
        var negative = TeamWipe(); negative.Players[0].CoinsAccrued = -1;
        await Assert.ThrowsAsync<ArgumentException>(() => _service.PostResultAsync(InProgressMatch, negative));
        _context.ChangeTracker.Clear();
        Assert.Equal(0, await _context.SurvivalMatchResults.CountAsync());
    }

    // ==== Reads ====

    [Fact]
    public async Task GetAndQuery_AnswerTheStoredResults_NewestFirst_FilteredAndPaged()
    {
        Assert.Null(await _service.GetResultAsync(InProgressMatch));
        var older = TeamWipe(CompletedMatch);
        older.Settlement.LedgerKey = null;
        older.EndedAt = new DateTime(2026, 11, 6, 10, 0, 0, DateTimeKind.Utc);
        await _service.PostResultAsync(CompletedMatch, older);
        await _service.PostResultAsync(InProgressMatch, NoPlayersLeft());
        _context.ChangeTracker.Clear();

        var stored = await _service.GetResultAsync(InProgressMatch);
        Assert.Equal((SurvivalEndReason.NoPlayersLeft, 3, 2), (stored!.EndReason, stored.ReachedWave, stored.Players.Count));

        var all = await _service.QueryResultsAsync(null, null, 1, 10);
        Assert.Equal((2, 1, 10), (all.TotalCount, all.PageNumber, all.PageSize));
        Assert.Equal(new[] { InProgressMatch, CompletedMatch }, all.Items.Select(r => r.SiegeMatchId!.Value));
        var scenario = await _service.QueryResultsAsync(100, null, 1, 10);
        Assert.Equal(2, scenario.TotalCount);
        Assert.Equal(0, (await _service.QueryResultsAsync(101, null, 1, 10)).TotalCount);
        var user5 = await _service.QueryResultsAsync(null, 5, 1, 10);
        Assert.Equal(new[] { CompletedMatch }, user5.Items.Select(r => r.SiegeMatchId!.Value));
        var page2 = await _service.QueryResultsAsync(null, 3, 2, 1);
        Assert.Equal((2, 2, 1), (page2.TotalCount, page2.PageNumber, page2.PageSize));
        Assert.Equal(new[] { CompletedMatch }, page2.Items.Select(r => r.SiegeMatchId!.Value));
        Assert.Equal(SurvivalMatchResultService.MaxPageSize, (await _service.QueryResultsAsync(null, null, 0, 1000)).PageSize);
    }

    // ==== Checkpoint ====

    private static SurvivalMatchCheckpointDto Checkpoint(int wave, DateTime capturedAt) => new()
    {
        SiegeScenarioId = 100, DifficultyProfileId = 10, CapturedAt = capturedAt, Substate = SurvivalSubstate.WaveActive, Wave = wave, CompletedWave = wave - 1,
        SpecialProfileKey = wave % 5 == 0 ? "wolves" : null, AliveEnemies = 7, QueuedEnemies = 12,
        NpcHeldObjectiveIds = { 502 }, ActiveSpawnZoneIds = { 30, 32 },
        DoorRepairs = { new SurvivalDoorRepairCheckpointDto { GateDoorId = 1, Destroyed = true, RepairWork = 3.5, RepairTarget = 10, Repairers = 2 } },
        Players =
        {
            new SurvivalPlayerCheckpointDto { UserId = 1, Ghost = false, KitId = SurvivalTestData.KitSword, MatchBalance = 1500, Kills = 10, Deaths = 1, PendingMysteryBoxRollId = "r1" },
            new SurvivalPlayerCheckpointDto { UserId = 3, Ghost = true, MatchBalance = 0, Kills = 2 }
        }
    };

    [Fact]
    public async Task Checkpoint_IsUpsertedPerMatch_IgnoresAnOlderCapture_AndReadsBack()
    {
        var t0 = new DateTime(2026, 11, 7, 10, 5, 0, DateTimeKind.Utc);
        var (first, created) = await _service.PutCheckpointAsync(InProgressMatch, Checkpoint(5, t0));
        Assert.True(created);
        Assert.Equal((InProgressMatch, 5, 4, 1, "wolves"), (first.SiegeMatchId, first.Wave, first.CompletedWave, first.Revision, first.SpecialProfileKey));
        Assert.NotNull(first.UpdatedAt);

        var (second, createdAgain) = await _service.PutCheckpointAsync(InProgressMatch, Checkpoint(6, t0.AddMinutes(2)));
        Assert.False(createdAgain);
        Assert.Equal((6, 2), (second.Wave, second.Revision));

        var (stale, _) = await _service.PutCheckpointAsync(InProgressMatch, Checkpoint(4, t0.AddMinutes(-3)));
        Assert.Equal((6, 2, t0.AddMinutes(2)), (stale.Wave, stale.Revision, stale.CapturedAt));

        _context.ChangeTracker.Clear();
        var read = await _service.GetCheckpointAsync(InProgressMatch);
        Assert.Equal((6, 5, SurvivalSubstate.WaveActive, 7, 12, 2), (read!.Wave, read.CompletedWave, read.Substate, read.AliveEnemies, read.QueuedEnemies, read.Revision));
        Assert.Equal(new[] { 502 }, read.NpcHeldObjectiveIds);
        Assert.Equal(new[] { 30, 32 }, read.ActiveSpawnZoneIds);
        Assert.Equal((1, true, 3.5, 2), (read.DoorRepairs.Single().GateDoorId, read.DoorRepairs.Single().Destroyed, read.DoorRepairs.Single().RepairWork, read.DoorRepairs.Single().Repairers));
        var p1 = read.Players.Single(p => p.UserId == 1);
        Assert.Equal((false, SurvivalTestData.KitSword, 1500L, "r1"), (p1.Ghost, p1.KitId, p1.MatchBalance, p1.PendingMysteryBoxRollId));
        Assert.True(read.Players.Single(p => p.UserId == 3).Ghost);
        Assert.Equal(1, await _context.SurvivalMatchCheckpoints.CountAsync());
        var row = await _context.SurvivalMatchCheckpoints.AsNoTracking().SingleAsync();
        Assert.Equal((2, 1), (row.Players, row.Ghosts));

        Assert.Null(await _service.GetCheckpointAsync(CompletedMatch));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.PutCheckpointAsync(999, Checkpoint(1, t0)));
        var bad = Checkpoint(3, t0); bad.CompletedWave = 4;
        await Assert.ThrowsAsync<ArgumentException>(() => _service.PutCheckpointAsync(InProgressMatch, bad));
        var route = Checkpoint(3, t0); route.SiegeMatchId = CompletedMatch;
        await Assert.ThrowsAsync<ArgumentException>(() => _service.PutCheckpointAsync(InProgressMatch, route));
    }

    // ==== Siege match hooks ====

    [Fact]
    public async Task SiegeAbort_OfASurvivalMatch_ForfeitsOpenRolls_AndRecordsAResultWithoutSettlement()
    {
        var matches = SiegeTestData.MatchService(_context, _notifications.Object);

        var aborted = await matches.AbortAsync(InProgressMatch, new SiegeMatchAbortDto { EndReason = SiegeMatchEndReason.ServerRestart });

        Assert.Equal(SiegeMatchStatus.Aborted, aborted.Status);
        _context.ChangeTracker.Clear();
        var result = await _service.GetResultAsync(InProgressMatch);
        Assert.NotNull(result);
        Assert.Equal((SurvivalMatchResultSource.Abort, SurvivalEndReason.ServerRestart, false, 0, 0), (result!.Source, result.EndReason, result.Settlement.SettlesRewards, result.Settlement.TotalCoins, result.ReachedWave));
        Assert.Null(result.Settlement.SettledAt);
        Assert.Equal(SurvivalEquipmentMode.OwnInventory, result.EquipmentMode);
        Assert.Equal(new[] { 1, 3, 5 }, result.Players.Select(p => p.UserId));
        Assert.Equal(new[] { true, true, false }, result.Players.Select(p => p.PresentAtEnd)); // who was still in when the restart hit
        Assert.Equal((10, 1), (result.Players[0].Kills, result.Players[0].Deaths)); // the Siege participant rows
        Assert.Equal((2, 1, 1), (result.Players[0].MysteryBoxRolls, result.Players[0].MysteryBoxClaims, result.Players[0].MysteryBoxForfeits));
        Assert.Equal(SurvivalMysteryBoxForfeitReason.MatchEnded, (await RollAsync("r1")).ForfeitReason);
        Assert.Equal(10, (await UserAsync(1)).Coins);
        Assert.Null(await Currency().FindAsync(CurrencyIdempotencyScopes.System, "siege-survival:900"));

        // A late plugin result for the aborted match can't replace the abort record (S61: the restart aborted it).
        var late = await Conflict(() => _service.PostResultAsync(InProgressMatch, TeamWipe()));
        Assert.Equal(SurvivalMatchResultConflictException.ResultAlreadyStored, late.Code);
        // A replayed abort writes nothing more.
        await matches.AbortAsync(InProgressMatch, new SiegeMatchAbortDto { EndReason = SiegeMatchEndReason.AdminStopped });
        _context.ChangeTracker.Clear();
        Assert.Equal(1, await _context.SurvivalMatchResults.CountAsync());
        Assert.Equal(SurvivalEndReason.ServerRestart, (await _service.GetResultAsync(InProgressMatch))!.EndReason);
    }

    [Fact]
    public async Task SiegeAbort_AfterThePluginPostedTheResult_KeepsThePostedResult()
    {
        var (posted, _) = await _service.PostResultAsync(InProgressMatch, TeamWipe());
        _context.ChangeTracker.Clear();

        await SiegeTestData.MatchService(_context, _notifications.Object).AbortAsync(InProgressMatch, new SiegeMatchAbortDto { EndReason = SiegeMatchEndReason.AdminStopped });

        _context.ChangeTracker.Clear();
        var stored = await _service.GetResultAsync(InProgressMatch);
        Assert.Equal((SurvivalMatchResultSource.Plugin, SurvivalEndReason.TeamWipe, posted.Settlement.LedgerPostingId), (stored!.Source, stored.EndReason, stored.Settlement.LedgerPostingId));
        Assert.Equal(10 + 303 + 7, (await UserAsync(1)).Coins);
    }

    [Fact]
    public async Task SiegeAbort_OfAPvpMatch_WritesNoSurvivalRow()
    {
        var matches = SiegeTestData.MatchService(_context, _notifications.Object);

        await matches.AbortAsync(PvpMatch, new SiegeMatchAbortDto { EndReason = SiegeMatchEndReason.ServerRestart });

        _context.ChangeTracker.Clear();
        Assert.Null(await _service.GetResultAsync(PvpMatch));
        Assert.Equal(0, await _context.SurvivalMatchResults.CountAsync());
    }

    [Fact]
    public async Task SiegeAbortUnfinished_RecordsTheSurvivalMatches_OnlyThem()
    {
        var matches = SiegeTestData.MatchService(_context, _notifications.Object);

        var aborted = await matches.AbortUnfinishedAsync(new SiegeMatchAbortUnfinishedDto { EndReason = SiegeMatchEndReason.ServerRestart });

        Assert.Contains(InProgressMatch, aborted.AbortedMatchIds);
        Assert.Contains(PvpMatch, aborted.AbortedMatchIds);
        _context.ChangeTracker.Clear();
        var rows = await _context.SurvivalMatchResults.AsNoTracking().OrderBy(r => r.SiegeMatchId).ToListAsync();
        Assert.All(rows, r => Assert.Equal((SurvivalMatchResultSource.Abort, SurvivalEndReason.ServerRestart, false), (r.Source, r.EndReason, r.SettlesRewards)));
        Assert.DoesNotContain(PvpMatch, rows.Select(r => r.SiegeMatchId));
        Assert.Contains(InProgressMatch, rows.Select(r => r.SiegeMatchId));
    }

    [Fact]
    public async Task SiegeComplete_OfASurvivalMatch_RecordsTheCompletion_ButPostsNoPvpRewardLegs()
    {
        var matches = SiegeTestData.MatchService(_context, _notifications.Object);
        var created = await matches.CreateAsync(new SiegeMatchCreateDto { SiegeLobbyId = 1, SiegeScenarioId = 100 });
        await matches.StartAsync(created.Id, new SiegeMatchStartDto
        {
            Participants = { new SiegeMatchParticipantStartDto { UserId = 3, SiegeTeamId = 202 },
                             new SiegeMatchParticipantStartDto { UserId = 4, SiegeTeamId = 202 },
                             new SiegeMatchParticipantStartDto { UserId = 1, SiegeTeamId = 201 } }
        });
        _context.ChangeTracker.Clear();

        // The same completion that pays the attackers 250 coins / 25 XP / 1 gem each in SiegeMatchServiceTests.
        var result = await matches.CompleteAsync(created.Id, new SiegeMatchCompleteDto
        {
            EndReason = SiegeMatchEndReason.InstantVictory,
            WinningAllianceGroup = 2,
            Participants =
            {
                new SiegeMatchParticipantResultDto { UserId = 3, SiegeTeamId = 202, Kills = 4, Captures = 1 },
                new SiegeMatchParticipantResultDto { UserId = 4, SiegeTeamId = 202, Captures = 1 },
                new SiegeMatchParticipantResultDto { UserId = 1, SiegeTeamId = 201 }
            },
            Objectives =
            {
                new SiegeMatchObjectiveResultInputDto { SiegeObjectiveId = 502, FinalHolderTeamId = 202, CapturedByUserId = 3, CapturedAt = DateTime.UtcNow },
                new SiegeMatchObjectiveResultInputDto { SiegeObjectiveId = 501, FinalHolderTeamId = 202, CapturedByUserId = 4, CapturedAt = DateTime.UtcNow }
            }
        });

        Assert.Equal(SiegeMatchStatus.Completed, result.Status);
        Assert.False(result.AlreadyCompleted);
        Assert.Equal(3, result.Rewards.Count);
        Assert.All(result.Rewards, r => Assert.Equal((0, 0, 0), (r.Coins, r.Experience, r.Gems)));
        Assert.True(result.Rewards.Single(r => r.UserId == 3).Won); // the outcome is still recorded
        foreach (var id in new[] { 1, 3, 4 })
        {
            var u = await UserAsync(id);
            Assert.Equal((10, 0, 0), (u.Coins, u.Gems, u.ExperiencePoints));
        }
        Assert.Null(await Currency().FindAsync(CurrencyIdempotencyScopes.System, $"siege-match:{created.Id}"));
        var stored = await matches.GetByIdAsync(created.Id);
        Assert.Equal((SiegeMatchStatus.Completed, SiegeMatchEndReason.InstantVictory, 2), (stored!.Status, stored.EndReason, stored.ObjectiveResults.Count));
        Assert.All(stored.Participants, p => Assert.Equal((0, 0, 0), (p.CoinsAwarded, p.ExpAwarded, p.GemsAwarded)));
        Assert.Equal(4, stored.Participants.Single(p => p.UserId == 3).Kills);
        _notifications.Verify(n => n.Enqueue(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TitleChangeResultDto?>()), Times.Never);
    }

    [Fact]
    public async Task IsSurvivalScenario_NeedsEnabledSettings()
    {
        Assert.True(await _service.IsSurvivalScenarioAsync(100));
        Assert.False(await _service.IsSurvivalScenarioAsync(101));
        var settings = await _context.SiegeSurvivalSettings.SingleAsync(s => s.SiegeScenarioId == 100);
        settings.Enabled = false;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        Assert.False(await _service.IsSurvivalScenarioAsync(100));
    }

    // ==== Pure rules ====

    [Theory]
    [InlineData(SurvivalEndReason.TeamWipe, true)]
    [InlineData(SurvivalEndReason.InstaLoseObjectiveCaptured, true)]
    [InlineData(SurvivalEndReason.AllObjectivesCaptured, true)]
    [InlineData(SurvivalEndReason.NoPlayersLeft, false)]
    [InlineData(SurvivalEndReason.AdminAbort, false)]
    [InlineData(SurvivalEndReason.ServerRestart, false)]
    [InlineData(SurvivalEndReason.TechnicalLimit, false)]
    public void Settles_FollowsS62(SurvivalEndReason reason, bool settles) => Assert.Equal(settles, SurvivalMatchResultService.Settles(reason));

    [Theory]
    [InlineData(SiegeMatchEndReason.AdminStopped, SurvivalEndReason.AdminAbort)]
    [InlineData(SiegeMatchEndReason.ServerRestart, SurvivalEndReason.ServerRestart)]
    [InlineData(SiegeMatchEndReason.NotEnoughPlayers, SurvivalEndReason.NoPlayersLeft)]
    [InlineData(SiegeMatchEndReason.ApiOutage, SurvivalEndReason.TechnicalLimit)]
    [InlineData(SiegeMatchEndReason.TimeExpired, SurvivalEndReason.TechnicalLimit)]
    public void FromAbortReason_MapsTheSiegeAborts(SiegeMatchEndReason siege, SurvivalEndReason survival) =>
        Assert.Equal(survival, SurvivalMatchResultService.FromAbortReason(siege));

    [Fact]
    public void Fingerprint_IgnoresApiFilledFieldsAndPlayerOrder_SeesEveryPluginField()
    {
        var a = TeamWipe();
        var b = TeamWipe();
        b.Players.Reverse();
        b.RecordedAt = DateTime.UtcNow;
        b.RewardPolicyVersion = "x";
        b.Source = SurvivalMatchResultSource.Abort;
        b.Settlement.SettledAt = DateTime.UtcNow;
        b.Settlement.LedgerPostingId = "p";
        b.Settlement.PlayersSettled = 9;
        b.Players[0].MysteryBoxRolls = 5;
        Assert.Equal(SurvivalMatchResultService.Fingerprint(a), SurvivalMatchResultService.Fingerprint(b));
        Assert.Equal(64, SurvivalMatchResultService.Fingerprint(a).Length);

        var c = TeamWipe(); c.Players[0].Kills = 11;
        var d = TeamWipe(); d.Players[0].CoinsAccrued = 303.1m;
        var e = TeamWipe(); e.EndReason = SurvivalEndReason.AllObjectivesCaptured;
        var f = TeamWipe(); f.EquipmentMode = null;
        foreach (var other in new[] { c, d, e, f })
            Assert.NotEqual(SurvivalMatchResultService.Fingerprint(a), SurvivalMatchResultService.Fingerprint(other));
    }
}
