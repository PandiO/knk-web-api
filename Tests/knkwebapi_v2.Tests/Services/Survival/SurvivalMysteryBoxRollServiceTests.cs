using System.Text.Json;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services.Lootbox;
using knkwebapi_v2.Services.Survival;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Survival;

/// <summary>
/// The match-scoped Mystery Box roll on InMemory with the real repository (DESIGN §8a, S34, S39, S59;
/// PandiO/knk-workspace#41, link S7a): idempotent roll per rollId, the checks (match in progress, point of the
/// match's scenario, participant, pool exactly 100 %), the stored snapshot, one claim per roll, claim and forfeit
/// excluding each other, and the match's roll list.
/// </summary>
public class SurvivalMysteryBoxRollServiceTests : IAsyncLifetime
{
    private const int InProgressMatch = 900;
    private const int CreatedMatch = 901;
    private const int CompletedMatch = 902;
    private const int ItemsPoint = 50;
    private const int BooksPoint = 51;
    private const int OtherScenarioPoint = 52;

    private KnKDbContext _context = null!;
    private SurvivalMysteryBoxRollService _service = null!;
    private readonly SeededRandom _random = new(7);

    private sealed class SeededRandom : ILootRandom
    {
        private readonly Random _random;
        public SeededRandom(int seed) { _random = new Random(seed); }
        public int NextInt(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);
        public double NextDouble() => _random.NextDouble();
    }

    public async Task InitializeAsync()
    {
        _context = SiegeTestData.NewContext("SurvivalMysteryBoxRoll");
        await SiegeTestData.SeedValidScenarioAsync(_context);
        await SurvivalTestData.SeedReadySurvivalAsync(_context);

        _context.SiegeLobbies.Add(new SiegeLobby { Id = 1, Name = "Siege - Cinix", Key = "cinix", IsEnabled = true, Rotation = { new SiegeLobbyScenario { SiegeScenarioId = 100 } } });
        for (var id = 1; id <= 6; id++)
            _context.Users.Add(new User { Id = id, Username = $"player{id}", Uuid = $"uuid-{id}" });
        _context.SiegeScenarios.Add(new SiegeScenario { Id = 101, Name = "Elsewhere", TownId = 1, HubLocationId = 1000 });
        _context.SurvivalMysteryBoxPoints.Add(new SurvivalMysteryBoxPoint { Id = OtherScenarioPoint, SiegeScenarioId = 101, Name = "Foreign box", LocationId = 1004, SurvivalMysteryBoxPoolId = 40 });

        var now = DateTime.UtcNow;
        _context.SiegeMatches.Add(new SiegeMatch
        {
            Id = InProgressMatch, SiegeLobbyId = 1, SiegeScenarioId = 100, Status = SiegeMatchStatus.InProgress, StartedAt = now,
            Participants =
            {
                new SiegeMatchParticipant { UserId = 1, SiegeTeamId = 201 },
                new SiegeMatchParticipant { UserId = 3, SiegeTeamId = 201 },
                new SiegeMatchParticipant { UserId = 5, SiegeTeamId = 201, LeftAt = now }
            }
        });
        _context.SiegeMatches.Add(new SiegeMatch { Id = CreatedMatch, SiegeLobbyId = 1, SiegeScenarioId = 100, Status = SiegeMatchStatus.Created });
        _context.SiegeMatches.Add(new SiegeMatch
        {
            Id = CompletedMatch, SiegeLobbyId = 1, SiegeScenarioId = 100, Status = SiegeMatchStatus.Completed, StartedAt = now, EndedAt = now,
            Participants = { new SiegeMatchParticipant { UserId = 1, SiegeTeamId = 201 } }
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        _service = NewService();
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    private SurvivalMysteryBoxRollService NewService(SiegeSurvivalOptions? options = null) =>
        new(new SurvivalMysteryBoxRollRepository(_context), _random, SurvivalTestData.Mapper(), options == null ? null : Options.Create(options));

    private static SurvivalMysteryBoxRollRequestDto Request(string rollId = "roll-1", int userId = 1, int price = 1000) =>
        new() { RollId = rollId, UserId = userId, PriceCharged = price };

    // ==== Roll ====

    [Fact]
    public async Task Roll_StoresOneRow_AndAnswersTheContract()
    {
        var (roll, created) = await _service.RollAsync(InProgressMatch, ItemsPoint, Request());

        Assert.True(created);
        Assert.Equal("roll-1", roll.RollId);
        Assert.Equal(InProgressMatch, roll.SiegeMatchId);
        Assert.Equal(ItemsPoint, roll.PointId);
        Assert.Equal(1, roll.UserId);
        Assert.Equal(SurvivalMysteryBoxVariant.Items, roll.Variant);
        Assert.Contains(roll.Entry.Id, new[] { 401, 402 });
        Assert.Equal(40, roll.Entry.SurvivalMysteryBoxPoolId);
        Assert.NotNull(roll.Entry.ItemBlueprintName);
        Assert.True(roll.Entry.ChanceBasisPoints > 0);
        Assert.Equal(20, roll.Reel.Count);
        Assert.Equal(roll.Entry.Id, roll.Reel[^1]);
        Assert.All(roll.Reel, id => Assert.Contains(id, new[] { 401, 402 }));
        if (roll.Entry.Id == 402) Assert.Empty(roll.RolledEnchantments);
        Assert.All(roll.RolledEnchantments, c => { Assert.Equal(SurvivalTestData.EnchantSharpness, c.EnchantmentDefinitionId); Assert.Equal(2, c.Level); });
        Assert.Equal(1000, roll.PriceCharged);
        Assert.Equal(SurvivalMysteryBoxRollState.Rolled, roll.State);
        Assert.Null(roll.ClaimedAt);
        Assert.Null(roll.DeliveryKind);
        Assert.Null(roll.ForfeitedAt);
        Assert.Null(roll.ForfeitReason);

        var row = Assert.Single(await _context.SurvivalMysteryBoxRolls.AsNoTracking().ToListAsync());
        Assert.Equal(roll.Entry.Id, row.SurvivalMysteryBoxEntryId);
        Assert.Equal(40, row.SurvivalMysteryBoxPoolId);
        Assert.Equal(roll.Entry.Id, JsonSerializer.Deserialize<JsonElement>(row.EntryJson).GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Roll_EnchantedBooks_PicksABookEntry()
    {
        var (roll, _) = await _service.RollAsync(InProgressMatch, BooksPoint, Request());
        Assert.Equal(SurvivalMysteryBoxVariant.EnchantedBooks, roll.Variant);
        Assert.Contains(roll.Entry.Id, new[] { 411, 412 });
        Assert.NotNull(roll.Entry.EnchantmentDefinitionId);
        Assert.NotNull(roll.Entry.EnchantmentKey);
        Assert.NotNull(roll.Entry.EnchantmentLevel);
        Assert.Empty(roll.RolledEnchantments);
    }

    [Fact]
    public async Task Roll_SameRollId_SameBody_AnswersTheStoredRoll_WithoutASecondRoll()
    {
        var (first, created) = await _service.RollAsync(InProgressMatch, ItemsPoint, Request());
        Assert.True(created);

        var (again, createdAgain) = await NewService().RollAsync(InProgressMatch, ItemsPoint, Request());

        Assert.False(createdAgain);
        Assert.Equal(first.Entry.Id, again.Entry.Id);
        Assert.Equal(first.Reel, again.Reel);
        Assert.Equal(first.RolledAt, again.RolledAt);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(again));
        Assert.Single(await _context.SurvivalMysteryBoxRolls.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Roll_SameRollId_DifferentBody_IsAConflict()
    {
        await _service.RollAsync(InProgressMatch, ItemsPoint, Request());

        var otherUser = await Assert.ThrowsAsync<SurvivalMysteryBoxConflictException>(() => _service.RollAsync(InProgressMatch, ItemsPoint, Request(userId: 3)));
        Assert.Equal(SurvivalMysteryBoxConflictException.RollIdReused, otherUser.Code);
        var otherPrice = await Assert.ThrowsAsync<SurvivalMysteryBoxConflictException>(() => _service.RollAsync(InProgressMatch, ItemsPoint, Request(price: 999)));
        Assert.Equal(SurvivalMysteryBoxConflictException.RollIdReused, otherPrice.Code);
        var otherPoint = await Assert.ThrowsAsync<SurvivalMysteryBoxConflictException>(() => _service.RollAsync(InProgressMatch, BooksPoint, Request()));
        Assert.Equal(SurvivalMysteryBoxConflictException.RollIdReused, otherPoint.Code);
        var otherMatch = await Assert.ThrowsAsync<SurvivalMysteryBoxConflictException>(() => _service.RollAsync(CompletedMatch, ItemsPoint, Request()));
        Assert.Equal(SurvivalMysteryBoxConflictException.RollIdReused, otherMatch.Code);
        Assert.Single(await _context.SurvivalMysteryBoxRolls.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Roll_SnapshotSurvivesPoolEdits()
    {
        var (first, _) = await _service.RollAsync(InProgressMatch, ItemsPoint, Request());

        var picked = await _context.SurvivalMysteryBoxEntries.FirstAsync(e => e.Id == first.Entry.Id);
        _context.SurvivalMysteryBoxEntries.Remove(picked);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var (again, created) = await NewService().RollAsync(InProgressMatch, ItemsPoint, Request());
        Assert.False(created);
        Assert.Equal(first.Entry.Id, again.Entry.Id);
        Assert.Equal(first.Entry.ItemBlueprintName, again.Entry.ItemBlueprintName);
        Assert.Equal(first.Entry.Id, (await _service.ListAsync(InProgressMatch)).Single().Entry.Id);
    }

    [Fact]
    public async Task Roll_ValidatesTheRequest()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.RollAsync(InProgressMatch, ItemsPoint, Request(rollId: " ")));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.RollAsync(InProgressMatch, ItemsPoint, Request(rollId: new string('x', 65))));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.RollAsync(InProgressMatch, ItemsPoint, Request(userId: 0)));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.RollAsync(InProgressMatch, ItemsPoint, Request(price: -1)));
        Assert.Empty(await _context.SurvivalMysteryBoxRolls.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Roll_UnknownMatch_PointOfAnotherScenario_OrNonParticipant_IsNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.RollAsync(12345, ItemsPoint, Request()));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.RollAsync(InProgressMatch, 999, Request()));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.RollAsync(InProgressMatch, OtherScenarioPoint, Request()));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.RollAsync(InProgressMatch, ItemsPoint, Request(userId: 2)));
        Assert.Empty(await _context.SurvivalMysteryBoxRolls.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Roll_MatchNotInProgress_OrParticipantLeft_IsAConflict()
    {
        var created = await Assert.ThrowsAsync<SurvivalMysteryBoxConflictException>(() => _service.RollAsync(CreatedMatch, ItemsPoint, Request()));
        Assert.Equal(SurvivalMysteryBoxConflictException.MatchNotInProgress, created.Code);
        var completed = await Assert.ThrowsAsync<SurvivalMysteryBoxConflictException>(() => _service.RollAsync(CompletedMatch, ItemsPoint, Request()));
        Assert.Equal(SurvivalMysteryBoxConflictException.MatchNotInProgress, completed.Code);
        var left = await Assert.ThrowsAsync<SurvivalMysteryBoxConflictException>(() => _service.RollAsync(InProgressMatch, ItemsPoint, Request(userId: 5)));
        Assert.Equal(SurvivalMysteryBoxConflictException.ParticipantLeft, left.Code);
        Assert.Empty(await _context.SurvivalMysteryBoxRolls.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Roll_PoolNotExactly100Percent_OrWithAnUnknownReference_IsAConflict()
    {
        var sword = await _context.SurvivalMysteryBoxEntries.FirstAsync(e => e.Id == 401);
        sword.ChancePercent = 60.49m;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var total = await Assert.ThrowsAsync<SurvivalMysteryBoxConflictException>(() => _service.RollAsync(InProgressMatch, ItemsPoint, Request()));
        Assert.Equal(SurvivalMysteryBoxConflictException.PoolInvalid, total.Code);
        Assert.Contains("box.total", total.Message);

        sword = await _context.SurvivalMysteryBoxEntries.FirstAsync(e => e.Id == 401);
        sword.ChancePercent = 60.5m;
        sword.ItemBlueprintId = 12345;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var unknown = await Assert.ThrowsAsync<SurvivalMysteryBoxConflictException>(() => NewService().RollAsync(InProgressMatch, ItemsPoint, Request()));
        Assert.Equal(SurvivalMysteryBoxConflictException.PoolInvalid, unknown.Code);
        Assert.Contains("box.unknown-blueprint", unknown.Message);
        Assert.Empty(await _context.SurvivalMysteryBoxRolls.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Roll_ReelLength_ComesFromTheOptions()
    {
        var (roll, _) = await NewService(new SiegeSurvivalOptions { MysteryBoxReelLength = 3 }).RollAsync(InProgressMatch, ItemsPoint, Request());
        Assert.Equal(3, roll.Reel.Count);
        var (single, _) = await NewService(new SiegeSurvivalOptions { MysteryBoxReelLength = 0 }).RollAsync(InProgressMatch, ItemsPoint, Request("roll-2"));
        Assert.Equal(new[] { single.Entry.Id }, single.Reel);
    }

    // ==== Claim ====

    [Fact]
    public async Task Claim_RecordsOneDelivery_AndARepeatAnswersTheSameState()
    {
        await _service.RollAsync(InProgressMatch, ItemsPoint, Request());

        var claimed = await _service.ClaimAsync(InProgressMatch, "roll-1", new SurvivalMysteryBoxClaimRequestDto { DeliveryKind = SurvivalMysteryBoxDeliveryKind.Drop });
        Assert.Equal(SurvivalMysteryBoxRollState.Claimed, claimed.State);
        Assert.NotNull(claimed.ClaimedAt);
        Assert.Equal(SurvivalMysteryBoxDeliveryKind.Drop, claimed.DeliveryKind);

        var again = await NewService().ClaimAsync(InProgressMatch, "roll-1", new SurvivalMysteryBoxClaimRequestDto { DeliveryKind = SurvivalMysteryBoxDeliveryKind.Inventory });
        Assert.Equal(SurvivalMysteryBoxDeliveryKind.Drop, again.DeliveryKind);
        Assert.Equal(claimed.ClaimedAt, again.ClaimedAt);

        var delivery = Assert.Single(await _context.SurvivalMysteryBoxDeliveries.AsNoTracking().ToListAsync());
        Assert.Equal(SurvivalMysteryBoxDeliveryKind.Drop, delivery.Kind);
        Assert.Equal(claimed.ClaimedAt, delivery.DeliveredAt);
    }

    [Fact]
    public async Task Claim_UnknownRoll_OrRollOfAnotherMatch_IsNotFound()
    {
        await _service.RollAsync(InProgressMatch, ItemsPoint, Request());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.ClaimAsync(InProgressMatch, "nope", new SurvivalMysteryBoxClaimRequestDto()));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.ClaimAsync(CompletedMatch, "roll-1", new SurvivalMysteryBoxClaimRequestDto()));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.ClaimAsync(InProgressMatch, "roll-1", new SurvivalMysteryBoxClaimRequestDto { DeliveryKind = (SurvivalMysteryBoxDeliveryKind)9 }));
    }

    [Fact]
    public async Task Claim_AfterForfeit_IsAConflict()
    {
        await _service.RollAsync(InProgressMatch, ItemsPoint, Request());
        await _service.ForfeitAsync(InProgressMatch, "roll-1", new SurvivalMysteryBoxForfeitRequestDto { Reason = SurvivalMysteryBoxForfeitReason.RollerDied });

        var ex = await Assert.ThrowsAsync<SurvivalMysteryBoxConflictException>(() => _service.ClaimAsync(InProgressMatch, "roll-1", new SurvivalMysteryBoxClaimRequestDto()));
        Assert.Equal(SurvivalMysteryBoxConflictException.RollForfeited, ex.Code);
        Assert.Empty(await _context.SurvivalMysteryBoxDeliveries.AsNoTracking().ToListAsync());
    }

    // ==== Forfeit ====

    [Fact]
    public async Task Forfeit_IsIdempotent_AndKeepsTheFirstReason()
    {
        await _service.RollAsync(InProgressMatch, ItemsPoint, Request());

        var forfeited = await _service.ForfeitAsync(InProgressMatch, "roll-1", new SurvivalMysteryBoxForfeitRequestDto { Reason = SurvivalMysteryBoxForfeitReason.Timeout });
        Assert.Equal(SurvivalMysteryBoxRollState.Forfeited, forfeited.State);
        Assert.NotNull(forfeited.ForfeitedAt);
        Assert.Equal(SurvivalMysteryBoxForfeitReason.Timeout, forfeited.ForfeitReason);

        var again = await NewService().ForfeitAsync(InProgressMatch, "roll-1", new SurvivalMysteryBoxForfeitRequestDto { Reason = SurvivalMysteryBoxForfeitReason.MatchEnded });
        Assert.Equal(SurvivalMysteryBoxForfeitReason.Timeout, again.ForfeitReason);
        Assert.Equal(forfeited.ForfeitedAt, again.ForfeitedAt);
    }

    [Fact]
    public async Task Forfeit_AfterClaim_IsAConflict_AndTheClaimStays()
    {
        await _service.RollAsync(InProgressMatch, ItemsPoint, Request());
        await _service.ClaimAsync(InProgressMatch, "roll-1", new SurvivalMysteryBoxClaimRequestDto());

        var ex = await Assert.ThrowsAsync<SurvivalMysteryBoxConflictException>(() => _service.ForfeitAsync(InProgressMatch, "roll-1", new SurvivalMysteryBoxForfeitRequestDto { Reason = SurvivalMysteryBoxForfeitReason.RollerLeft }));
        Assert.Equal(SurvivalMysteryBoxConflictException.RollClaimed, ex.Code);
        var stored = (await _service.ListAsync(InProgressMatch)).Single();
        Assert.Equal(SurvivalMysteryBoxRollState.Claimed, stored.State);
        Assert.Null(stored.ForfeitedAt);
    }

    [Fact]
    public async Task Forfeit_UnknownRoll_IsNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.ForfeitAsync(InProgressMatch, "nope", new SurvivalMysteryBoxForfeitRequestDto()));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.ForfeitAsync(InProgressMatch, "nope", new SurvivalMysteryBoxForfeitRequestDto { Reason = (SurvivalMysteryBoxForfeitReason)9 }));
    }

    // ==== List ====

    [Fact]
    public async Task List_ReturnsTheMatchsRolls_OldestFirst_AndNotFoundForAnUnknownMatch()
    {
        await _service.RollAsync(InProgressMatch, ItemsPoint, Request("a"));
        await _service.RollAsync(InProgressMatch, BooksPoint, Request("b", userId: 3));
        await _service.ClaimAsync(InProgressMatch, "a", new SurvivalMysteryBoxClaimRequestDto());

        var rolls = await _service.ListAsync(InProgressMatch);
        Assert.Equal(new[] { "a", "b" }, rolls.Select(r => r.RollId));
        Assert.Equal(SurvivalMysteryBoxRollState.Claimed, rolls[0].State);
        Assert.Equal(SurvivalMysteryBoxDeliveryKind.Inventory, rolls[0].DeliveryKind);
        Assert.Equal(SurvivalMysteryBoxRollState.Rolled, rolls[1].State);
        Assert.Equal(3, rolls[1].UserId);

        Assert.Empty(await _service.ListAsync(CompletedMatch));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.ListAsync(12345));
    }
}
