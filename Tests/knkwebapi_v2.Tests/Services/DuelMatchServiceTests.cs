using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Duel;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Arena duels P2 (KNG-213, IMPLEMENTATION_PLAN.md "Phase 2" test list): lock-in all or nothing, every
/// refusal, the end paths and their idempotency, Σ SYS_DUEL = 0 after each, the review tiers and escalation,
/// staff release/void/revert/confiscate, abort-unfinished, restrictions and the policy. On EF InMemory with
/// the real ledger and alert service (locking is proven for the ledger against MySQL elsewhere).
/// </summary>
public class DuelMatchServiceTests : IDisposable
{
    private const int Alice = 1, Bob = 2, Carol = 3, Dave = 4, Young = 5, Staff = 900;
    private const int ArenaId = 50, OtherArenaId = 51, GroundId = 60, OtherGroundId = 61, StandsId = 62, KnightKit = 7, ArcherKit = 8;

    private readonly KnKDbContext _context;
    private readonly TestClock _clock = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly CurrencyService _currency;
    private readonly CurrencyAlertService _alerts;
    private readonly DuelMatchService _service;

    public DuelMatchServiceTests()
    {
        _context = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase($"duels-{Guid.NewGuid()}").Options);
        _currency = new CurrencyService(new CurrencyRepository(_context), new UserRepository(_context), NullLogger<CurrencyService>.Instance);
        var options = new CurrencyMonitorOptions();
        _alerts = new CurrencyAlertService(_context, new CurrencyReconciler(_context), new CurrencyAnomalyDetector(_context, Options.Create(options)),
            new CurrencyReconciliationState(), new CurrencyMonitorSignals(), NullLogger<CurrencyAlertService>.Instance, Options.Create(options));
        _service = new DuelMatchService(_context, _currency, new UserRepository(_context), _alerts, _audit.Object,
            NullLogger<DuelMatchService>.Instance, _clock);
        Seed();
    }

    public void Dispose() => _context.Dispose();

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(TimeSpan by) => Now += by;
    }

    private void Seed()
    {
        var old = DateTime.UtcNow.AddDays(-60);
        _context.Users.AddRange(
            new User { Id = Alice, Username = "alice", Coins = 50_000, ExperiencePoints = 1_000, CreatedAt = old },
            new User { Id = Bob, Username = "bob", Coins = 50_000, ExperiencePoints = 1_000, CreatedAt = old },
            new User { Id = Carol, Username = "carol", Coins = 50_000, ExperiencePoints = 1_000, CreatedAt = old },
            new User { Id = Dave, Username = "dave", Coins = 50_000, ExperiencePoints = 1_000, CreatedAt = old },
            new User { Id = Young, Username = "fresh", Coins = 50_000, ExperiencePoints = 1_000, CreatedAt = DateTime.UtcNow.AddHours(-2) },
            new User { Id = Staff, Username = "mod", CreatedAt = old });
        _context.CurrencyPolicies.Add(new CurrencyPolicy
        {
            Currency = Currency.Coins, Transferable = true, TransfersEnabled = true, MinTransfer = 1, MaxTransfer = 100_000,
            MinSenderAccountAgeHours = 48, MaxBalance = 2_000_000_000
        });
        _context.TitleBrackets.Add(new TitleBracket { Id = 2, MaleName = "Squire", FemaleName = "Squire", MinExperience = 500 });
        _context.Kits.AddRange(new Kit { Id = KnightKit, Name = "Knight" }, new Kit { Id = ArcherKit, Name = "Archer" });
        _context.Arenas.AddRange(
            new Arena
            {
                Id = ArenaId, Name = "Arena", Key = "arena_2", Description = "a", WgRegionId = "arena_2", DistrictId = 10, StreetId = 5,
                AllowFixedKit = true, AllowKitChoice = true, AllowOwnGearDrop = true,
                EligibleKits = new List<ArenaKit> { new() { KitId = KnightKit, SequenceNumber = 0 } }
            },
            new Arena { Id = OtherArenaId, Name = "Pit", Key = "pit", Description = "p", WgRegionId = "pit", DistrictId = 10, StreetId = 5 });
        // Rev. 6: battlegrounds are arena areas of kind BATTLEGROUND; a spectator area is no battleground.
        _context.ArenaAreas.AddRange(
            new ArenaArea { Id = GroundId, ArenaId = ArenaId, Kind = ArenaAreaKind.BATTLEGROUND, Key = "north", IsEnabled = true },
            new ArenaArea { Id = OtherGroundId, ArenaId = OtherArenaId, Kind = ArenaAreaKind.BATTLEGROUND, Key = "pit1", IsEnabled = true },
            new ArenaArea { Id = StandsId, ArenaId = ArenaId, Kind = ArenaAreaKind.SPECTATOR, Key = "stands", IsEnabled = true });
        _context.SaveChanges();
    }

    // ===== Helpers =====

    private static DuelMatchCreateDto Duel(int challenger = Alice, int opponent = Bob, long a = 1_000, long b = 1_000,
        DuelGearMode mode = DuelGearMode.OWN_GEAR_KEEP, string? token = null, params DuelItemDto[] items) => new()
    {
        PublicToken = token ?? Guid.NewGuid().ToString(),
        ArenaId = ArenaId,
        ChallengerUserId = challenger,
        OpponentUserId = opponent,
        GearMode = mode,
        ChallengerCoins = a,
        OpponentCoins = b,
        StakeItems = items.ToList(),
        OfferCount = 2
    };

    private static DuelItemDto Item(int owner, string material = "DIAMOND_SWORD") => new() { OwnerUserId = owner, Material = material, Amount = 1 };

    /// <summary>A clean fight: both fought, long enough, nobody passive.</summary>
    private static DuelTelemetryDto Clean() => new()
    {
        DurationSeconds = 60,
        Challenger = new DuelSideTelemetryDto { DamageDealt = 40, DamageTaken = 20, Hits = 10, AttackAttempts = 15, ArmorPoints = 10, HadWeapon = true },
        Opponent = new DuelSideTelemetryDto { DamageDealt = 20, DamageTaken = 40, Hits = 6, AttackAttempts = 12, ArmorPoints = 10, HadWeapon = true }
    };

    /// <summary>A thrown fight: the loser (either side) never swung.</summary>
    private static DuelTelemetryDto Thrown() => new()
    {
        DurationSeconds = 4,
        Challenger = new DuelSideTelemetryDto { DamageDealt = 20, Hits = 5, AttackAttempts = 5, ArmorPoints = 0, HadWeapon = false },
        Opponent = new DuelSideTelemetryDto { DamageDealt = 20, Hits = 5, AttackAttempts = 5, ArmorPoints = 0, HadWeapon = false }
    };

    private static DuelTelemetryDto ThrownBy(bool challengerLoses)
    {
        var t = Thrown();
        var loser = challengerLoses ? t.Challenger! : t.Opponent!;
        loser.DamageDealt = 0;
        loser.Hits = 0;
        loser.AttackAttempts = 0;
        return t;
    }

    private async Task<DuelMatchDto> LockAsync(DuelMatchCreateDto dto) => (await _service.CreateAsync(dto)).Match;

    private async Task<DuelResultDto> WinAsync(DuelMatchDto match, int winner, DuelTelemetryDto? telemetry = null)
    {
        await _service.StartFightAsync(match.Id, new DuelStartFightDto { BattlegroundId = GroundId });
        return await _service.CompleteAsync(match.Id, new DuelMatchCompleteDto
        {
            WinnerUserId = winner,
            Telemetry = telemetry ?? Clean()
        });
    }

    private async Task<DuelResultDto> PlayAsync(int winner, int loser, DuelTelemetryDto telemetry, long coins = 1_000)
    {
        _clock.Advance(TimeSpan.FromMinutes(30)); // past the pair cooldown
        var match = await LockAsync(Duel(winner, loser, coins, coins));
        return await WinAsync(match, winner, telemetry);
    }

    private async Task<int> CoinsAsync(int userId) =>
        await _context.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Coins).SingleAsync();

    private async Task<long> SystemSumAsync(string account, int? matchId = null)
    {
        var query = _context.CurrencyEntries.AsNoTracking().Where(e => e.AccountKind == CurrencyAccountKind.System && e.SystemAccount == account);
        if (matchId.HasValue)
        {
            var source = matchId.Value.ToString();
            query = query.Where(e => e.Transaction.SourceType == DuelMatchService.SourceType && e.Transaction.SourceRef == source);
        }
        return await query.SumAsync(e => e.Amount);
    }

    private async Task<DuelErrorCode> RefusalAsync(Func<Task> call) => (await Assert.ThrowsAsync<DuelException>(call)).Code;

    private async Task AssertReconcilesAsync() => Assert.Empty(await new CurrencyReconciler(_context).FindMismatchesAsync());

    // ===== Lock-in =====

    [Fact]
    public async Task Create_DebitsBothStakesInOnePosting_AndTheOpenMatchHoldsThePot()
    {
        var match = await LockAsync(Duel(a: 1_000, b: 800));

        Assert.Equal(DuelMatchStatus.Created, match.Status);
        Assert.Equal((49_000, 49_200), (await CoinsAsync(Alice), await CoinsAsync(Bob)));
        var tx = await _context.CurrencyTransactions.AsNoTracking().Include(t => t.Entries).SingleAsync(t => t.Id == match.StakeTransactionId);
        Assert.Equal((CurrencyReasons.DuelStake, $"duel-lock:{match.PublicToken}", "DuelMatch", match.Id.ToString()),
            (tx.ReasonCode, tx.IdempotencyKey, tx.SourceType, tx.SourceRef));
        Assert.Equal(2, tx.Entries.Count(e => e.AccountKind == CurrencyAccountKind.User));
        Assert.Equal(1_800, await SystemSumAsync(CurrencyReasons.SysDuel, match.Id));
        Assert.Equal(5, match.FeePercent);
        await AssertReconcilesAsync();
    }

    [Fact]
    public async Task Create_IsAllOrNothing_WhenOneSideCantPay()
    {
        var code = await RefusalAsync(() => _service.CreateAsync(Duel(a: 1_000, b: 1_000, opponent: Staff)));

        Assert.Equal(DuelErrorCode.InsufficientFunds, code);
        Assert.Empty(_context.DuelMatches);
        Assert.Empty(_context.CurrencyTransactions);
        Assert.Equal(50_000, await CoinsAsync(Alice));
    }

    [Fact]
    public async Task Create_SameToken_ReturnsTheStoredMatch_AndADifferentBodyIsRefused()
    {
        var dto = Duel();
        var (first, created) = await _service.CreateAsync(dto);
        var (again, createdAgain) = await _service.CreateAsync(dto);

        Assert.True(created);
        Assert.False(createdAgain);
        Assert.Equal(first.Id, again.Id);
        Assert.Single(_context.CurrencyTransactions);
        Assert.Equal(49_000, await CoinsAsync(Alice));

        var changed = Duel(a: 2_000, b: 2_000, token: dto.PublicToken);
        Assert.Equal(DuelErrorCode.TokenReused, await RefusalAsync(() => _service.CreateAsync(changed)));
    }

    [Fact]
    public async Task Create_FriendlyDuel_MovesNothing_AndSkipsEligibility()
    {
        var match = await LockAsync(Duel(Young, Bob, 0, 0));

        Assert.False(match.ValueMoving);
        Assert.Null(match.StakeTransactionId);
        Assert.Empty(_context.CurrencyTransactions);
    }

    [Fact]
    public async Task Gems_AreRefused()
    {
        var dto = Duel();
        dto.OpponentGems = 5;
        Assert.Equal(DuelErrorCode.GemsNotStakeable, await RefusalAsync(() => _service.CreateAsync(dto)));
    }

    [Fact]
    public async Task Eligibility_AccountAgeTitleLocksAndKillSwitch_AreRefused()
    {
        // A two-hour-old account can't stake (CurrencyPolicy 48 h).
        Assert.Equal(DuelErrorCode.NewAccountRestricted, await RefusalAsync(() => _service.CreateAsync(Duel(Alice, Young))));

        // The arena's override replaces the /pay rule: 1 h is enough, a title is now needed.
        var arena = await _context.Arenas.SingleAsync(a => a.Id == ArenaId);
        arena.StakeMinAccountAgeHours = 1;
        await _context.SaveChangesAsync();
        Assert.NotNull(await LockAsync(Duel(Alice, Young, token: null)));
        arena.StakeMinTitleBracketId = 2;
        (await _context.Users.SingleAsync(u => u.Id == Carol)).ExperiencePoints = 100; // ledger-free test setup
        await _context.SaveChangesAsync();
        Assert.Equal(DuelErrorCode.NewAccountRestricted, await RefusalAsync(() => _service.CreateAsync(Duel(Carol, Dave))));

        // Frozen or transfer-locked players can't take part at all, staking or not.
        (await _context.Users.SingleAsync(u => u.Id == Dave)).IsFrozen = true;
        await _context.SaveChangesAsync();
        Assert.Equal(DuelErrorCode.AccountLocked, await RefusalAsync(() => _service.CreateAsync(Duel(Bob, Dave, 0, 0, items: Item(Bob)))));

        // The R1 kill switch stops coin stakes, not item stakes.
        var policy = await _context.CurrencyPolicies.SingleAsync();
        policy.TransfersEnabled = false;
        (await _context.Users.SingleAsync(u => u.Id == Dave)).IsFrozen = false;
        await _context.SaveChangesAsync();
        Assert.Equal(DuelErrorCode.TransfersDisabled, await RefusalAsync(() => _service.CreateAsync(Duel(Bob, Dave))));
        Assert.NotNull(await LockAsync(Duel(Bob, Dave, 0, 0, items: Item(Bob))));
    }

    [Fact]
    public async Task DropMode_NeedsBothPlayersEligible_EvenWithoutStakes()
    {
        Assert.Equal(DuelErrorCode.NewAccountRestricted,
            await RefusalAsync(() => _service.CreateAsync(Duel(Alice, Young, 0, 0, DuelGearMode.OWN_GEAR_DROP))));
        Assert.True((await LockAsync(Duel(Alice, Bob, 0, 0, DuelGearMode.OWN_GEAR_DROP))).ValueMoving);
    }

    [Theory]
    [InlineData(1_000, 499, DuelErrorCode.StakeRatioTooLow)]
    [InlineData(1_000, 0, DuelErrorCode.StakeRatioTooLow)]
    [InlineData(10_001, 10_001, DuelErrorCode.CoinStakeOutOfRange)]
    public async Task StakeRules_AreRefused(long a, long b, DuelErrorCode expected)
    {
        Assert.Equal(expected, await RefusalAsync(() => _service.CreateAsync(Duel(a: a, b: b))));
    }

    [Fact]
    public async Task StakeRatio_AtExactlyTheMinimum_Passes_AndZeroSwitchesItOff()
    {
        Assert.NotNull(await LockAsync(Duel(a: 1_000, b: 500)));
        var arena = await _context.Arenas.SingleAsync(a => a.Id == ArenaId);
        arena.MinStakeRatioPercent = 0;
        await _context.SaveChangesAsync();
        Assert.NotNull(await LockAsync(Duel(Carol, Dave, 1_000, 0)));
    }

    [Fact]
    public async Task StakeRatio_ValuesItemsAtTheirBlueprintsBasePrice()
    {
        _context.ItemBlueprints.Add(new ItemBlueprint { Id = 1, Name = "Edge", DefaultDisplayName = "Edge", BasePriceMin = 300.9m, BasePriceMax = 900 });
        _context.ItemInstances.Add(new ItemInstance { Id = 99, ItemBlueprintId = 1 });
        await _context.SaveChangesAsync();
        DuelItemDto Sword(int owner, int amount = 1) => new() { OwnerUserId = owner, Material = "DIAMOND_SWORD", Amount = amount, InstanceId = 99 };

        // "My diamond sword vs your 500 coins": 2 × 300 = 600 against 500 passes.
        Assert.NotNull(await LockAsync(Duel(a: 0, b: 500, items: Sword(Alice, 2))));
        // One sword (300) against 1,000 coins is under 50%; an item without a priced instance is worth nothing.
        Assert.Equal(DuelErrorCode.StakeRatioTooLow, await RefusalAsync(() => _service.CreateAsync(Duel(Carol, Dave, 0, 1_000, items: Sword(Carol)))));
        Assert.Equal(DuelErrorCode.StakeRatioTooLow, await RefusalAsync(() => _service.CreateAsync(Duel(Carol, Dave, 0, 500, items: Item(Carol)))));
        var check = await _service.CheckAsync(Duel(Carol, Dave, 0, 1_000, items: Sword(Carol)));
        Assert.Contains("300 vs 1,000", check.Refusals.Single().Message);
    }

    [Fact]
    public async Task ArenaGearKitAndItemRules_AreRefused()
    {
        var tooMany = Enumerable.Range(0, 10).Select(i => Item(Alice, $"STONE_{i}")).ToArray();
        Assert.Equal(DuelErrorCode.TooManyItemStacks, await RefusalAsync(() => _service.CreateAsync(Duel(items: tooMany))));

        var fixedKit = Duel(mode: DuelGearMode.FIXED_KIT);
        fixedKit.FixedKitId = ArcherKit;
        Assert.Equal(DuelErrorCode.KitNotEligible, await RefusalAsync(() => _service.CreateAsync(fixedKit)));

        var pit = Duel();
        pit.ArenaId = OtherArenaId;
        pit.GearMode = DuelGearMode.KIT_CHOICE;
        Assert.Equal(DuelErrorCode.GearModeNotAllowed, await RefusalAsync(() => _service.CreateAsync(pit)));

        var arena = await _context.Arenas.SingleAsync(a => a.Id == ArenaId);
        arena.AllowCoinStakes = false;
        arena.AllowItemStakes = false;
        await _context.SaveChangesAsync();
        Assert.Equal(DuelErrorCode.CoinStakesNotAllowed, await RefusalAsync(() => _service.CreateAsync(Duel())));
        Assert.Equal(DuelErrorCode.ItemStakesNotAllowed, await RefusalAsync(() => _service.CreateAsync(Duel(a: 0, b: 0, items: Item(Alice)))));

        arena.IsDuelEnabled = false;
        await _context.SaveChangesAsync();
        Assert.Equal(DuelErrorCode.DuelsDisabled, await RefusalAsync(() => _service.CreateAsync(Duel(a: 0, b: 0))));
        Assert.Equal(DuelErrorCode.ArenaNotFound, await RefusalAsync(() => _service.CreateAsync(new DuelMatchCreateDto
        {
            PublicToken = Guid.NewGuid().ToString(), ArenaId = 999, ChallengerUserId = Alice, OpponentUserId = Bob
        })));
    }

    [Fact]
    public async Task Check_ListsEveryRefusal_AndWritesNothing()
    {
        var dto = Duel(Alice, Young, 10_001, 1);
        dto.OpponentGems = 1;

        var check = await _service.CheckAsync(dto);

        Assert.False(check.Allowed);
        Assert.Contains(check.Refusals, r => r.Code == nameof(DuelErrorCode.GemsNotStakeable));
        Assert.Contains(check.Refusals, r => r.Code == nameof(DuelErrorCode.CoinStakeOutOfRange));
        Assert.Contains(check.Refusals, r => r.Code == nameof(DuelErrorCode.StakeRatioTooLow));
        Assert.Contains(check.Refusals, r => r.Code == nameof(DuelErrorCode.NewAccountRestricted));
        Assert.Empty(_context.DuelMatches);

        var ok = await _service.CheckAsync(Duel(a: 1_999, b: 1_000));
        Assert.True(ok.Allowed);
        Assert.Equal(149, ok.FeeCoins); // 5% of 2,999, rounded down
    }

    // ===== Pair and daily limits =====

    [Fact]
    public async Task PairCooldown_AndPerDayLimit_AreRefused_UntilTheyPass()
    {
        await WinAsync(await LockAsync(Duel()), Alice);

        Assert.Equal(DuelErrorCode.PairCooldown, await RefusalAsync(() => _service.CreateAsync(Duel(Bob, Alice))));
        Assert.NotNull(await LockAsync(Duel(Alice, Carol))); // another pair is fine
        Assert.NotNull(await LockAsync(Duel(Bob, Alice, 0, 0))); // and a friendly duel

        _clock.Advance(TimeSpan.FromMinutes(11));
        await WinAsync(await LockAsync(Duel(Bob, Alice)), Bob);
        _clock.Advance(TimeSpan.FromMinutes(11));
        await WinAsync(await LockAsync(Duel(Bob, Alice)), Alice);
        _clock.Advance(TimeSpan.FromMinutes(11));

        Assert.Equal(DuelErrorCode.PairDailyLimit, await RefusalAsync(() => _service.CreateAsync(Duel())));
        _clock.Advance(TimeSpan.FromHours(24));
        Assert.NotNull(await LockAsync(Duel()));
    }

    [Fact]
    public async Task CancelledMatches_DontCountForThePairLimits()
    {
        for (var i = 0; i < 3; i++)
        {
            var match = await LockAsync(Duel());
            await _service.CancelAsync(match.Id, new DuelMatchCancelDto { EndReason = DuelEndReason.TeleportFailed });
        }
        Assert.NotNull(await LockAsync(Duel()));
    }

    [Fact]
    public async Task DailyCoinWinCap_CountsWhatAPlayerCouldWin()
    {
        var arena = await _context.Arenas.SingleAsync(a => a.Id == ArenaId);
        arena.DailyCoinWinCap = 2_500;
        await _context.SaveChangesAsync();
        await WinAsync(await LockAsync(Duel(Alice, Bob, 2_000, 2_000)), Alice);

        // Alice already won 2,000; Carol's 1,000 would take her past 2,500.
        Assert.Equal(DuelErrorCode.DailyWinCap, await RefusalAsync(() => _service.CreateAsync(Duel(Carol, Alice, 1_000, 1_000))));
        Assert.NotNull(await LockAsync(Duel(Carol, Alice, 500, 500)));
    }

    // ===== Restrictions =====

    [Fact]
    public async Task PlayerRestriction_RefusesValueMovingDuels_ButNotFriendlyOnes_UntilItExpiresOrIsLifted()
    {
        var restriction = await _service.CreateRestrictionAsync(new DuelRestrictionCreateDto
        {
            UserId = Bob, Reason = "suspected alt feeding", ExpiresAt = _clock.Now.UtcDateTime.AddDays(2)
        }, Staff);

        var refusal = await Assert.ThrowsAsync<DuelException>(() => _service.CreateAsync(Duel(Carol, Bob)));
        Assert.Equal(DuelErrorCode.DuelRestricted, refusal.Code);
        Assert.Contains("suspected alt feeding", refusal.Message);
        Assert.Contains("UTC", refusal.Message); // the end date
        Assert.Equal(DuelErrorCode.DuelRestricted, await RefusalAsync(() => _service.CreateAsync(Duel(Bob, Dave, 0, 0, DuelGearMode.OWN_GEAR_DROP))));
        Assert.NotNull(await LockAsync(Duel(Carol, Bob, 0, 0)));

        _clock.Advance(TimeSpan.FromDays(3));
        Assert.NotNull(await LockAsync(Duel(Carol, Bob)));
        Assert.False((await _service.GetRestrictionsAsync(Bob, activeOnly: false)).Single().Active);
        _audit.Verify(a => a.RecordAsync(Staff, Bob, AuditAction.DuelRestrictionCreated, It.Is<string>(d => d.Contains("suspected"))), Times.Once);

        var lifted = await _service.LiftRestrictionAsync(restriction.Id, Staff, "expired anyway");
        Assert.NotNull(lifted.LiftedAt);
        _audit.Verify(a => a.RecordAsync(Staff, Bob, AuditAction.DuelRestrictionLifted, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task PairRestriction_RefusesOnlyThatPair_AndLiftingEndsIt()
    {
        var restriction = await _service.CreateRestrictionAsync(new DuelRestrictionCreateDto { UserId = Alice, OtherUserId = Bob, Reason = "pair" }, Staff);

        Assert.Equal(DuelErrorCode.DuelRestricted, await RefusalAsync(() => _service.CreateAsync(Duel(Bob, Alice))));
        Assert.NotNull(await LockAsync(Duel(Alice, Carol)));

        await _service.LiftRestrictionAsync(restriction.Id, Staff, null);
        Assert.NotNull(await LockAsync(Duel(Bob, Alice)));
        _audit.Verify(a => a.RecordAsync(Staff, Alice, AuditAction.DuelRestrictionCreated, It.IsAny<string>()), Times.Once);
        _audit.Verify(a => a.RecordAsync(Staff, Bob, AuditAction.DuelRestrictionCreated, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Restrictions_ValidateTheirInput()
    {
        Assert.Equal(DuelErrorCode.InvalidRequest, await RefusalAsync(() =>
            _service.CreateRestrictionAsync(new DuelRestrictionCreateDto { UserId = Alice, OtherUserId = Alice, Reason = "x" }, Staff)));
        Assert.Equal(DuelErrorCode.InvalidRequest, await RefusalAsync(() =>
            _service.CreateRestrictionAsync(new DuelRestrictionCreateDto { UserId = Alice, Reason = " " }, Staff)));
        Assert.Equal(DuelErrorCode.InvalidRequest, await RefusalAsync(() =>
            _service.CreateRestrictionAsync(new DuelRestrictionCreateDto { UserId = Alice, Reason = "x", ExpiresAt = _clock.Now.UtcDateTime.AddMinutes(-1) }, Staff)));
        Assert.Equal(DuelErrorCode.UserNotFound, await RefusalAsync(() =>
            _service.CreateRestrictionAsync(new DuelRestrictionCreateDto { UserId = 4242, Reason = "x" }, Staff)));
        Assert.Equal(DuelErrorCode.RestrictionNotFound, await RefusalAsync(() => _service.LiftRestrictionAsync(4242, Staff, null)));
    }

    // ===== Settlement =====

    [Fact]
    public async Task Complete_PaysPotMinusFee_TheFeeGoesToSysFees_AndSysDuelNetsToZero()
    {
        var match = await LockAsync(Duel(a: 1_000, b: 999));
        var result = await WinAsync(match, Bob);

        Assert.Equal(DuelSettlement.Paid, result.Settlement);
        Assert.Equal(99, result.Match.FeeCoins); // 5% of 1,999 = 99.95, rounded down
        Assert.Equal(49_000, await CoinsAsync(Alice));
        Assert.Equal(50_000 - 999 + 1_999 - 99, await CoinsAsync(Bob));
        Assert.Equal(0, await SystemSumAsync(CurrencyReasons.SysDuel, match.Id));
        Assert.Equal(99, await SystemSumAsync(CurrencyReasons.SysFees, match.Id));
        Assert.Equal((DuelReviewState.None, 0), (result.Match.Review, result.Match.ReviewTier));
        var fee = await _context.CurrencyTransactions.AsNoTracking().Include(t => t.Entries).SingleAsync(t => t.Id == result.Match.FeeTransactionId);
        Assert.Equal(CurrencyReasons.DuelFee, fee.ReasonCode);
        Assert.All(fee.Entries, e => Assert.Equal(CurrencyAccountKind.System, e.AccountKind));
        await AssertReconcilesAsync();
    }

    [Fact]
    public async Task Complete_IsIdempotent_AndRefusesAnotherWinnerOrADraw()
    {
        var match = await LockAsync(Duel());
        await WinAsync(match, Alice);
        var before = await _context.CurrencyTransactions.CountAsync();

        var again = await _service.CompleteAsync(match.Id, new DuelMatchCompleteDto { WinnerUserId = Alice, Telemetry = Clean() });

        Assert.True(again.AlreadyDone);
        Assert.Equal(DuelSettlement.Paid, again.Settlement);
        Assert.Equal(before, await _context.CurrencyTransactions.CountAsync());
        Assert.Equal(DuelErrorCode.WrongState, await RefusalAsync(() =>
            _service.CompleteAsync(match.Id, new DuelMatchCompleteDto { WinnerUserId = Bob })));
        Assert.Equal(DuelErrorCode.InvalidRequest, await RefusalAsync(() =>
            _service.CompleteAsync(match.Id, new DuelMatchCompleteDto { WinnerUserId = Alice, EndReason = DuelEndReason.TimeLimit })));
        Assert.Equal(DuelErrorCode.WrongState, await RefusalAsync(() =>
            _service.CancelAsync(match.Id, new DuelMatchCancelDto { EndReason = DuelEndReason.AdminCancelled })));
        Assert.Equal(DuelErrorCode.MatchNotFound, await RefusalAsync(() =>
            _service.CompleteAsync(4242, new DuelMatchCompleteDto { WinnerUserId = Alice })));
    }

    [Theory]
    [InlineData(DuelEndReason.TeleportFailed, DuelMatchStatus.Cancelled)]
    [InlineData(DuelEndReason.PlayerLeftLobby, DuelMatchStatus.Cancelled)]
    [InlineData(DuelEndReason.NoBattleground, DuelMatchStatus.Cancelled)]
    [InlineData(DuelEndReason.AdminCancelled, DuelMatchStatus.Cancelled)]
    [InlineData(DuelEndReason.ServerRestart, DuelMatchStatus.Aborted)]
    [InlineData(DuelEndReason.ApiOutage, DuelMatchStatus.Aborted)]
    [InlineData(DuelEndReason.TimeLimit, DuelMatchStatus.Completed)]
    [InlineData(DuelEndReason.AgreedDraw, DuelMatchStatus.Completed)]
    [InlineData(DuelEndReason.SimultaneousDeath, DuelMatchStatus.Completed)]
    public async Task Cancel_RefundsBothStakesOnce_WithNoFee(DuelEndReason reason, DuelMatchStatus status)
    {
        var match = await LockAsync(Duel(a: 1_000, b: 700));
        var result = await _service.CancelAsync(match.Id, new DuelMatchCancelDto { EndReason = reason });
        var again = await _service.CancelAsync(match.Id, new DuelMatchCancelDto { EndReason = reason });

        Assert.Equal((status, (int?)null, DuelSettlement.Refunded), (result.Match.Status, result.Match.WinnerUserId, result.Settlement));
        Assert.True(again.AlreadyDone);
        Assert.Equal((50_000, 50_000), (await CoinsAsync(Alice), await CoinsAsync(Bob)));
        Assert.Equal(0, await SystemSumAsync(CurrencyReasons.SysDuel, match.Id));
        Assert.Equal(0, await SystemSumAsync(CurrencyReasons.SysFees));
        Assert.Equal(2, await _context.CurrencyTransactions.CountAsync()); // stake + one reversal
        Assert.Equal(DuelErrorCode.WrongState, await RefusalAsync(() =>
            _service.CompleteAsync(match.Id, new DuelMatchCompleteDto { WinnerUserId = Alice })));
        Assert.Equal(DuelErrorCode.InvalidRequest, await RefusalAsync(() =>
            _service.CancelAsync(match.Id, new DuelMatchCancelDto { EndReason = DuelEndReason.Win })));
        await AssertReconcilesAsync();
    }

    [Fact]
    public async Task StartFight_IsIdempotent_AndChecksTheBattlegroundAndKits()
    {
        var kitMatch = await LockAsync(Duel(mode: DuelGearMode.KIT_CHOICE));
        Assert.Equal(DuelErrorCode.InvalidRequest, await RefusalAsync(() =>
            _service.StartFightAsync(kitMatch.Id, new DuelStartFightDto { BattlegroundId = OtherGroundId })));
        // Rev. 6: an area of another kind is no battleground.
        Assert.Equal(DuelErrorCode.InvalidRequest, await RefusalAsync(() =>
            _service.StartFightAsync(kitMatch.Id, new DuelStartFightDto { BattlegroundId = StandsId })));
        Assert.Equal(DuelErrorCode.KitNotEligible, await RefusalAsync(() =>
            _service.StartFightAsync(kitMatch.Id, new DuelStartFightDto { BattlegroundId = GroundId, ChallengerKitId = ArcherKit })));

        var started = await _service.StartFightAsync(kitMatch.Id, new DuelStartFightDto { BattlegroundId = GroundId, ChallengerKitId = KnightKit, OpponentKitId = KnightKit });
        var again = await _service.StartFightAsync(kitMatch.Id, new DuelStartFightDto { BattlegroundId = GroundId });

        Assert.Equal((DuelMatchStatus.InProgress, GroundId, "north", KnightKit), (started.Status, started.BattlegroundId, started.BattlegroundKey, started.ChallengerKitId));
        Assert.Equal(started.FightStartedAt, again.FightStartedAt);
    }

    [Fact]
    public async Task ItemsOnlyAndDropModeResults_MoveNoCoins_ButAreScored()
    {
        var match = await LockAsync(Duel(a: 0, b: 0, mode: DuelGearMode.OWN_GEAR_DROP, items: Item(Alice)));
        var result = await _service.CompleteAsync(match.Id, new DuelMatchCompleteDto
        {
            WinnerUserId = Bob, Telemetry = Clean(), SpoilsItems = new List<DuelItemDto> { Item(Alice, "IRON_HELMET") }
        });

        Assert.Equal((DuelSettlement.Paid, 0, 0L), (result.Settlement, result.Match.ReviewTier, result.Match.FeeCoins));
        Assert.Single(result.Match.SpoilsItems);
        Assert.Equal("DIAMOND_SWORD", result.Match.StakeItems.Single().Material);
        Assert.Empty(_context.CurrencyTransactions);
    }

    // ===== Review tiers =====

    [Fact]
    public async Task OneFlaggedResult_IsPaidAndFlagged_WithADuelFlagAlert()
    {
        var result = await PlayAsync(Alice, Bob, ThrownBy(challengerLoses: false));

        Assert.Equal((DuelSettlement.Paid, DuelReviewState.Flagged, 1), (result.Settlement, result.Match.Review, result.Match.ReviewTier));
        Assert.Contains(DuelReviewRules.S1, result.Match.ReviewSignals);
        Assert.Contains(DuelReviewRules.S2, result.Match.ReviewSignals);
        Assert.Contains(DuelReviewRules.S3, result.Match.ReviewSignals);
        Assert.Contains(DuelReviewRules.S4, result.Match.ReviewSignals);
        var alert = await _context.CurrencyAlerts.AsNoTracking().SingleAsync();
        Assert.Equal((CurrencyAlertRules.DuelFlag, CurrencyAlertSeverity.Medium, Alice, result.Match.AlertId),
            (alert.Rule, alert.Severity, alert.UserId, (long?)alert.Id));
        Assert.Equal(result.Match.SettlementTransactionId, alert.TransactionId);
        Assert.Contains("paid and flagged", alert.Summary);
    }

    [Fact]
    public async Task AFirstFlagNeverHolds_HoweverLargeThePot()
    {
        var result = await PlayAsync(Alice, Bob, ThrownBy(challengerLoses: false), coins: 10_000);
        Assert.Equal(DuelSettlement.Paid, result.Settlement);
        Assert.Equal(1, result.Match.ReviewTier);
    }

    [Fact]
    public async Task SecondFlagForThePair_IsHeld_NothingIsPaid_AndNewStakedInvitesAreRefused()
    {
        await PlayAsync(Alice, Bob, ThrownBy(challengerLoses: false));
        var held = await PlayAsync(Bob, Alice, ThrownBy(challengerLoses: false)); // either direction counts

        Assert.Equal((DuelSettlement.Held, DuelReviewState.Held, 2), (held.Settlement, held.Match.Review, held.Match.ReviewTier));
        Assert.Null(held.Match.SettlementTransactionId);
        Assert.Equal(2_000, await SystemSumAsync(CurrencyReasons.SysDuel, held.Match.Id)); // the pot waits in SYS_DUEL
        var alert = await _context.CurrencyAlerts.AsNoTracking().SingleAsync(a => a.Id == held.Match.AlertId);
        Assert.Equal((CurrencyAlertRules.DuelHold, CurrencyAlertSeverity.High), (alert.Rule, alert.Severity));

        _clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(DuelErrorCode.ReviewHoldOpen, await RefusalAsync(() => _service.CreateAsync(Duel(Alice, Bob))));
        // The beneficiary (Bob won the held one) can't stake against anyone else either; Alice can.
        Assert.Equal(DuelErrorCode.ReviewHoldOpen, await RefusalAsync(() => _service.CreateAsync(Duel(Bob, Carol))));
        Assert.NotNull(await LockAsync(Duel(Alice, Carol)));
        Assert.NotNull(await LockAsync(Duel(Alice, Bob, 0, 0))); // friendly duels stay allowed
    }

    [Fact]
    public async Task AnAcknowledgedFlag_IsAFalsePositive_AndDoesntCount()
    {
        var first = await PlayAsync(Alice, Bob, ThrownBy(challengerLoses: false));
        await _alerts.AcknowledgeAsync(first.Match.AlertId!.Value, Staff);

        var second = await PlayAsync(Alice, Bob, ThrownBy(challengerLoses: false));

        Assert.Equal((DuelSettlement.Paid, 1), (second.Settlement, second.Match.ReviewTier));
    }

    [Fact]
    public async Task Escalation_CountsByBeneficiary_AcrossDifferentOpponents()
    {
        await PlayAsync(Alice, Bob, ThrownBy(challengerLoses: false));
        var second = await PlayAsync(Alice, Carol, ThrownBy(challengerLoses: false));

        Assert.Equal(DuelSettlement.Held, second.Settlement);
        var details = second.Match.ReviewDetails!.Value;
        Assert.True(details.GetProperty("escalatedByBeneficiary").GetBoolean());
        Assert.False(details.GetProperty("escalatedByPair").GetBoolean());
    }

    [Fact]
    public async Task FlagsOutsideTheWindow_DontCount_AndCleanResultsAreNotFlags()
    {
        await PlayAsync(Alice, Bob, ThrownBy(challengerLoses: false));
        _clock.Advance(TimeSpan.FromDays(8));
        await PlayAsync(Alice, Bob, Clean());
        var third = await PlayAsync(Alice, Bob, ThrownBy(challengerLoses: false));

        Assert.Equal(1, third.Match.ReviewTier);
    }

    [Fact]
    public async Task OneWayPair_S5_FlagsTheThirdWinInARow()
    {
        await PlayAsync(Alice, Bob, Clean());
        Assert.Equal(0, (await PlayAsync(Alice, Bob, Clean())).Match.ReviewTier);
        var third = await PlayAsync(Alice, Bob, Clean());

        Assert.Equal(new[] { DuelReviewRules.S5 }, third.Match.ReviewSignals);
    }

    [Fact]
    public async Task Funnel_S6_FlagsAWinnerFedByThreeYoungAccounts()
    {
        var arena = await _context.Arenas.SingleAsync(a => a.Id == ArenaId);
        arena.StakeMinAccountAgeHours = 0;
        foreach (var id in new[] { Bob, Carol, Dave })
        {
            (await _context.Users.SingleAsync(u => u.Id == id)).CreatedAt = DateTime.UtcNow.AddDays(-3);
        }
        await _context.SaveChangesAsync();

        Assert.Equal(0, (await PlayAsync(Alice, Bob, Clean())).Match.ReviewTier);
        Assert.Equal(0, (await PlayAsync(Alice, Carol, Clean())).Match.ReviewTier);
        var third = await PlayAsync(Alice, Dave, Clean());

        Assert.Equal(new[] { DuelReviewRules.S6 }, third.Match.ReviewSignals);
    }

    [Fact]
    public async Task FriendlyDuels_AreNeverScored()
    {
        _clock.Advance(TimeSpan.FromMinutes(30));
        var match = await LockAsync(Duel(a: 0, b: 0));
        var result = await WinAsync(match, Alice, ThrownBy(challengerLoses: false));

        Assert.Null(result.Match.ReviewTier);
        Assert.Equal(DuelReviewState.None, result.Match.Review);
        Assert.Empty(_context.CurrencyAlerts);
    }

    // ===== Held results =====

    private async Task<DuelResultDto> HeldAsync()
    {
        await PlayAsync(Alice, Bob, ThrownBy(challengerLoses: false));
        var held = await PlayAsync(Alice, Bob, ThrownBy(challengerLoses: false));
        Assert.Equal(DuelSettlement.Held, held.Settlement);
        return held;
    }

    [Fact]
    public async Task Release_PaysTheHeldResult_Once()
    {
        var held = await HeldAsync();
        var aliceBefore = await CoinsAsync(Alice);

        var released = await _service.ReleaseAsync(held.Match.Id, Staff, "checked the fight recording");
        var again = await _service.ReleaseAsync(held.Match.Id, Staff, null);

        Assert.Equal((DuelSettlement.Paid, DuelReviewState.Released, Staff), (released.Settlement, released.Match.Review, released.Match.ReviewedByUserId));
        Assert.True(again.AlreadyDone);
        Assert.Equal(aliceBefore + 1_900, await CoinsAsync(Alice));
        Assert.Equal(0, await SystemSumAsync(CurrencyReasons.SysDuel, held.Match.Id));
        _audit.Verify(a => a.RecordAsync(Staff, Alice, AuditAction.DuelResultReleased, It.IsAny<string>()), Times.Once);
        Assert.Equal(DuelErrorCode.WrongState, await RefusalAsync(() => _service.VoidAsync(held.Match.Id, Staff, "too late")));
        await AssertReconcilesAsync();
    }

    [Fact]
    public async Task Void_RefundsBothStakes_NoFee_AndTheSpoilsGoBack()
    {
        var held = await HeldAsync();
        var (aliceBefore, bobBefore) = (await CoinsAsync(Alice), await CoinsAsync(Bob));

        var voided = await _service.VoidAsync(held.Match.Id, Staff, "alt account");
        var again = await _service.VoidAsync(held.Match.Id, Staff, "alt account");

        Assert.Equal((DuelSettlement.Refunded, DuelReviewState.Voided, DuelUndeliveredItems.ReturnToOwners),
            (voided.Settlement, voided.Match.Review, voided.Match.UndeliveredItems));
        Assert.True(again.AlreadyDone);
        Assert.Equal((aliceBefore + 1_000, bobBefore + 1_000), (await CoinsAsync(Alice), await CoinsAsync(Bob)));
        Assert.Equal(0, await SystemSumAsync(CurrencyReasons.SysDuel, held.Match.Id));
        Assert.Equal(0, await SystemSumAsync(CurrencyReasons.SysFees, held.Match.Id));
        Assert.Equal(DuelErrorCode.InvalidRequest, await RefusalAsync(() => _service.VoidAsync(held.Match.Id, Staff, "")));
        Assert.Equal(DuelErrorCode.WrongState, await RefusalAsync(() => _service.ReleaseAsync(held.Match.Id, Staff, null)));
        _audit.Verify(a => a.RecordAsync(Staff, Alice, AuditAction.DuelResultVoided, It.Is<string>(d => d.Contains("alt account"))), Times.Once);
        await AssertReconcilesAsync();
    }

    [Fact]
    public async Task AbortUnfinished_RefundsOpenMatches_AndLeavesHeldAndFinishedOnesAlone()
    {
        var held = await HeldAsync();
        _clock.Advance(TimeSpan.FromMinutes(30));
        var open = await LockAsync(Duel(Carol, Dave));
        var fighting = await LockAsync(Duel(Bob, Carol)); // Alice won the held one, so she can't stake now
        await _service.StartFightAsync(fighting.Id, new DuelStartFightDto { BattlegroundId = GroundId });

        var result = await _service.AbortUnfinishedAsync(DuelEndReason.ServerRestart);

        Assert.Equal(new[] { open.Id, fighting.Id }, result.AbortedMatchIds);
        Assert.Equal(DuelMatchStatus.Aborted, (await _service.GetByIdAsync(open.Id))!.Status);
        Assert.Equal(DuelReviewState.Held, (await _service.GetByIdAsync(held.Match.Id))!.Review);
        Assert.Equal(0, await SystemSumAsync(CurrencyReasons.SysDuel, open.Id));
        Assert.Empty((await _service.AbortUnfinishedAsync(DuelEndReason.ServerRestart)).AbortedMatchIds);
        Assert.Equal(DuelErrorCode.InvalidRequest, await RefusalAsync(() => _service.AbortUnfinishedAsync(DuelEndReason.AdminCancelled)));
    }

    // ===== Revert and confiscate =====

    [Fact]
    public async Task Revert_WithSpentCoins_RecoversWhatIsLeft_RefundsTheLoserFirst_AndReversesTheFee()
    {
        var match = await LockAsync(Duel(a: 4_000, b: 4_000));
        var paid = await WinAsync(match, Alice); // Alice: 46,000 + 7,600 = 53,600
        await _currency.SpendAsync(Alice, Currency.Coins, 50_000,
            CurrencyContext.ForSystem("Test", CurrencyReasons.KitPurchase, "spent-it")); // 3,600 left

        var result = await _service.RevertAsync(match.Id, Staff, "alt feeding confirmed", confiscate: false);
        var revert = result.Revert!;

        Assert.Equal((7_600L, 3_600L, 400L), (revert.PayoutCoins, revert.RecoveredCoins, revert.FeeReversedCoins));
        Assert.Equal((4_000L, 0L, 4_000L), (revert.ReturnedToLoserCoins, revert.ReturnedToWinnerCoins, revert.MissingCoins));
        Assert.Equal(0, await CoinsAsync(Alice));
        Assert.Equal(50_000, await CoinsAsync(Bob)); // the loser got his stake back first
        Assert.Equal((DuelReviewState.Voided, DuelUndeliveredItems.ReturnToOwners, Staff), (result.Match.Review, result.Match.UndeliveredItems, result.Match.ReviewedByUserId));
        Assert.Equal(0, await SystemSumAsync(CurrencyReasons.SysDuel, match.Id));
        Assert.Equal(0, await SystemSumAsync(CurrencyReasons.SysFees, match.Id));
        var reversal = await _context.CurrencyTransactions.AsNoTracking().SingleAsync(t => t.ReversesTransactionId == paid.Match.SettlementTransactionId);
        Assert.Contains("shortfall", reversal.MetadataJson);
        _audit.Verify(a => a.RecordAsync(Staff, Alice, AuditAction.DuelResultReverted, It.Is<string>(d => d.Contains("alt feeding confirmed"))), Times.Once);

        var again = await _service.RevertAsync(match.Id, Staff, "alt feeding confirmed", confiscate: false);
        Assert.True(again.AlreadyDone);
        Assert.Equal(DuelErrorCode.WrongState, await RefusalAsync(() => _service.RevertAsync(match.Id, Staff, "x", confiscate: true)));
        await AssertReconcilesAsync();
    }

    [Fact]
    public async Task Revert_WithEverythingStillThere_GivesEachSideItsOwnStakeBack()
    {
        var match = await LockAsync(Duel(a: 2_000, b: 1_500));
        await WinAsync(match, Bob);

        var revert = (await _service.RevertAsync(match.Id, Staff, "bug in the arena", confiscate: false)).Revert!;

        Assert.Equal((2_000L, 1_500L, 0L), (revert.ReturnedToLoserCoins, revert.ReturnedToWinnerCoins, revert.MissingCoins));
        Assert.Equal((50_000, 50_000), (await CoinsAsync(Alice), await CoinsAsync(Bob)));
        Assert.Equal(0, await SystemSumAsync(CurrencyReasons.SysDuel, match.Id));
        await AssertReconcilesAsync();
    }

    [Fact]
    public async Task Confiscate_SendsTheRecoveredCoinsToTheSink_AndKeepsTheFee()
    {
        var match = await LockAsync(Duel(a: 1_000, b: 1_000));
        await WinAsync(match, Alice);

        var result = await _service.RevertAsync(match.Id, Staff, "both accounts are one person", confiscate: true);
        var revert = result.Revert!;

        Assert.Equal((1_900L, 1_900L, 0L, 0L), (revert.RecoveredCoins, revert.ConfiscatedCoins, revert.ReturnedToLoserCoins, revert.MissingCoins));
        Assert.Equal((49_000, 49_000), (await CoinsAsync(Alice), await CoinsAsync(Bob)));
        Assert.Equal(DuelUndeliveredItems.Destroy, result.Match.UndeliveredItems);
        Assert.Equal(0, await SystemSumAsync(CurrencyReasons.SysDuel, match.Id));
        Assert.Equal(100, await SystemSumAsync(CurrencyReasons.SysFees, match.Id)); // the fee stays a fee
        Assert.Equal(1_900, await SystemSumAsync(CurrencyReasons.SysConfiscated, match.Id));
        _audit.Verify(a => a.RecordAsync(Staff, Alice, AuditAction.DuelResultConfiscated, It.IsAny<string>()), Times.Once);
        Assert.True((await _service.RevertAsync(match.Id, Staff, "again", confiscate: true)).AlreadyDone);
        await AssertReconcilesAsync();
    }

    [Fact]
    public async Task Revert_RefusesHeldAndUnpaidMatches_AndNeedsAReason()
    {
        var held = await HeldAsync();
        Assert.Equal(DuelErrorCode.WrongState, await RefusalAsync(() => _service.RevertAsync(held.Match.Id, Staff, "x", confiscate: false)));
        _clock.Advance(TimeSpan.FromMinutes(30));
        var open = await LockAsync(Duel(Carol, Dave));
        Assert.Equal(DuelErrorCode.WrongState, await RefusalAsync(() => _service.RevertAsync(open.Id, Staff, "x", confiscate: false)));
        Assert.Equal(DuelErrorCode.InvalidRequest, await RefusalAsync(() => _service.RevertAsync(open.Id, Staff, " ", confiscate: false)));
    }

    [Fact]
    public async Task ARevertedResult_CountsAsAConfirmedFlag_EvenAfterItsAlertIsAcknowledged()
    {
        var first = await PlayAsync(Alice, Bob, ThrownBy(challengerLoses: false));
        await _service.RevertAsync(first.Match.Id, Staff, "confirmed", confiscate: false);
        await _alerts.AcknowledgeAsync(first.Match.AlertId!.Value, Staff);

        var second = await PlayAsync(Alice, Bob, ThrownBy(challengerLoses: false));

        Assert.Equal(DuelSettlement.Held, second.Settlement);
    }

    [Fact]
    public async Task ClearEscalation_AcknowledgesThePairsOpenFlags()
    {
        var first = await PlayAsync(Alice, Bob, ThrownBy(challengerLoses: false));

        var cleared = await _service.ClearEscalationAsync(new DuelEscalationClearDto { UserId = Bob, OtherUserId = Alice, Reason = "known friends" }, Staff);
        var second = await PlayAsync(Alice, Bob, ThrownBy(challengerLoses: false));

        Assert.Equal(new[] { first.Match.AlertId!.Value }, cleared.AcknowledgedAlertIds);
        Assert.Equal(1, second.Match.ReviewTier);
        _audit.Verify(a => a.RecordAsync(Staff, Bob, AuditAction.DuelEscalationCleared, It.IsAny<string>()), Times.Once);
    }

    // ===== Policy =====

    [Fact]
    public async Task Policy_DefaultsMatchTheDesign_UpdatesAreVersioned_AndAFlagCountBelowTwoIsRefused()
    {
        var policy = await _service.GetPolicyAsync();
        Assert.Equal((10, 10, 0, 3, 3, 7, 7, 2, 7), (policy.S1LoserDamagePercent, policy.S2ShortFightSeconds, policy.S4MaxLoserAttackAttempts,
            policy.S5PairDuels, policy.S6MinYoungOpponents, policy.S6YoungAccountDays, policy.S6WindowDays, policy.EscalationFlagCount, policy.EscalationWindowDays));

        policy.S2ShortFightSeconds = 20;
        var saved = await _service.UpdatePolicyAsync(policy, Staff);
        Assert.Equal(20, saved.S2ShortFightSeconds);

        var stale = saved;
        stale.UpdatedAt = saved.UpdatedAt.AddSeconds(-1); // loaded before the save above
        Assert.Equal(DuelErrorCode.PolicyChanged, await RefusalAsync(() => _service.UpdatePolicyAsync(stale, Staff)));

        saved = await _service.GetPolicyAsync();
        saved.EscalationFlagCount = 1;
        Assert.Equal(DuelErrorCode.InvalidRequest, await RefusalAsync(() => _service.UpdatePolicyAsync(saved, Staff)));
        _audit.Verify(a => a.RecordAsync(Staff, Staff, AuditAction.DuelPolicyChanged, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Policy_ThresholdsDriveTheSignals()
    {
        var policy = await _service.GetPolicyAsync();
        policy.S1Enabled = policy.S2Enabled = policy.S3Enabled = policy.S4Enabled = false;
        await _service.UpdatePolicyAsync(policy, Staff);

        var result = await PlayAsync(Alice, Bob, ThrownBy(challengerLoses: false));

        Assert.Equal(0, result.Match.ReviewTier);
    }

    // ===== Reads =====

    [Fact]
    public async Task Reads_ByTokenAndHistory()
    {
        var match = await LockAsync(Duel());
        await WinAsync(match, Alice);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await LockAsync(Duel(Carol, Dave));

        var byToken = await _service.GetByTokenAsync(match.PublicToken.ToUpperInvariant());
        Assert.Equal((match.Id, "alice", "bob", "arena_2"), (byToken!.Id, byToken.ChallengerUsername, byToken.OpponentUsername, byToken.ArenaKey));
        Assert.Null(await _service.GetByTokenAsync("not-a-token"));
        Assert.Single(await _service.QueryAsync(Alice, null, null, null, 50));
        Assert.Equal(2, (await _service.QueryAsync(null, ArenaId, null, null, 50)).Count);
        Assert.Single(await _service.QueryAsync(null, null, DuelMatchStatus.Created, null, 50));
    }
}
