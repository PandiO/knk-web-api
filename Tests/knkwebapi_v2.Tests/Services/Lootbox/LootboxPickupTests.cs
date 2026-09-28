using AutoMapper;
using FluentAssertions;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Lootbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Lootbox;

/// <summary>
/// Smoke test round 1 (2026-09-27, docs/specs/lootboxes/DESIGN.md §3.8-§3.9): clicking a world box picks it up as a
/// token item instead of opening it (replay, races, expiry, wrong token, frozen player); the daily cap counts pickups;
/// token status for the plugin's join scan; and the
/// LootboxWorldChanged notifications that make web despawns, web area deletes and token revokes reach the game
/// server within seconds. EF InMemory with the real repositories and services, a scripted RNG and a pinned clock.
/// </summary>
public class LootboxPickupTests
{
    private readonly string _dbName = $"LootboxPickupTestDb_{Guid.NewGuid()}";
    private readonly ScriptedRandom _random = new();
    private readonly PinnedClock _clock = new(new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryPlayerNotificationQueue _notifications = new();

    private int _alice, _bob, _staff, _weapons, _food, _grade3;

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_dbName).Options);

    private static IMapper Mapper() => new MapperConfiguration(cfg =>
    {
        cfg.AddProfile<LootboxMappingProfile>();
        cfg.AddProfile<ItemInstanceMappingProfile>();
        cfg.AddProfile<ItemBlueprintMappingProfile>();
        cfg.AddProfile<GradeMappingProfile>();
        cfg.AddProfile<PagedQueryMappingProfile>();
    }).CreateMapper();

    private LootboxRuntimeService Runtime(KnKDbContext db)
    {
        var users = new UserRepository(db);
        return new LootboxRuntimeService(
            new LootboxRuntimeRepository(db),
            new LootboxTypeService(new LootboxTypeRepository(db), Mapper()),
            new ItemInstanceService(new ItemInstanceRepository(db), Mapper()),
            users,
            new AuditLogService(new AuditLogRepository(db), users),
            new LootboxRollEngine(_random),
            _random,
            _clock,
            NullLogger<LootboxRuntimeService>.Instance,
            _notifications);
    }

    // Grades ★1-5; a Weapons type (one ★3 sword) and a Food type (one ★3 bread), both ★3 boxes.
    private async Task SeedAsync()
    {
        await using var db = NewContext();
        var grades = Enumerable.Range(1, 5)
            .Select(s => new Grade { Name = $"G{s}", Stars = s, DropChance = new[] { 70m, 60m, 40m, 25m, 15m }[s - 1] })
            .ToList();
        var weaponsCategory = new Category { Name = "Weapons" };
        var foodCategory = new Category { Name = "Food" };
        var sword = new ItemBlueprint { Name = "Steel Sword", DefaultDisplayName = "&bSteel Sword", MaxStackSize = 1, Category = weaponsCategory, Grade = grades[2] };
        var bread = new ItemBlueprint { Name = "Bread", DefaultDisplayName = "&9Bread", MaxStackSize = 64, DefaultQuantity = 8, Category = foodCategory, Grade = grades[2] };
        var weapons = new LootboxType { Name = "Weapons Lootbox", Category = weaponsCategory, Enabled = true, MinBoxStars = 3, MaxBoxStars = 3 };
        var food = new LootboxType { Name = "Food Lootbox", Category = foodCategory, Enabled = true, MinBoxStars = 3, MaxBoxStars = 3 };
        var alice = new User { Username = "alice" };
        var bob = new User { Username = "bob" };
        var staff = new User { Username = "staff" };
        db.AddRange(grades);
        db.AddRange(weaponsCategory, foodCategory, sword, bread, weapons, food, alice, bob, staff);
        db.LootboxConfigurations.Add(new LootboxConfiguration { Id = "global", GlobalMaxActive = 100, MaxClaimsPerPlayerPerDay = 10 });
        await db.SaveChangesAsync();
        (_alice, _bob, _staff, _weapons, _food, _grade3) = (alice.Id, bob.Id, staff.Id, weapons.Id, food.Id, grades[2].Id);
    }

    private async Task<LootboxSpawnDto> SpawnAsync(int typeId)
    {
        await using var db = NewContext();
        return await Runtime(db).AdminSpawnAsync(
            new LootboxAdminSpawnRequestDto { TypeId = typeId, BoxStars = 3, World = "world", X = 1, Y = 70, Z = 2 }, _staff);
    }

    private async Task<LootboxPickupResultDto> PickupAsync(LootboxSpawnDto spawn, int userId, Guid? token = null)
    {
        await using var db = NewContext();
        return await Runtime(db).PickupAsync(spawn.Id, new LootboxPickupRequestDto { Token = token ?? spawn.Token, UserId = userId });
    }

    private async Task<LootboxConflictException> PickupFailsAsync(LootboxSpawnDto spawn, int userId, Guid? token = null)
    {
        await using var db = NewContext();
        return (await Runtime(db)
            .Invoking(s => s.PickupAsync(spawn.Id, new LootboxPickupRequestDto { Token = token ?? spawn.Token, UserId = userId }))
            .Should().ThrowAsync<LootboxConflictException>()).Which;
    }

    private async Task UpdateAsync(Action<KnKDbContext> change)
    {
        await using var db = NewContext();
        change(db);
        await db.SaveChangesAsync();
    }

    private List<LootboxWorldChangedNotificationDto> WorldChanges() => _notifications.GetPending()
        .Where(n => n.Type == PlayerNotificationTypes.LootboxWorldChanged)
        .Select(n => n.LootboxWorldChanged!)
        .ToList();

    // ===== Pickup =====

    [Fact]
    public async Task Pickup_GivesTheClickerATokenOfTheBoxsTypeAndGrade_AndTakesTheBox()
    {
        await SeedAsync();
        var spawn = await SpawnAsync(_weapons);

        var result = await PickupAsync(spawn, _alice);

        result.Replay.Should().BeFalse();
        result.SpawnId.Should().Be(spawn.Id);
        var token = result.LootboxToken;
        (token.LootboxTypeId, token.BoxGradeId, token.IssuedToUserId, token.Reason, token.Status, token.SourceSpawnId)
            .Should().Be((_weapons, _grade3, (int?)_alice, "WorldPickup", "Issued", (int?)spawn.Id));
        token.DeliveredAt.Should().BeNull("the plugin confirms it after putting the item in the inventory");

        await using var db = NewContext();
        var row = await db.LootboxSpawns.SingleAsync(s => s.Id == spawn.Id);
        (row.Status, row.ClaimedByUserId).Should().Be((LootboxSpawnStatus.Claimed, (int?)_alice));
        db.LootboxClaims.Should().BeEmpty("nothing is rolled until the token is opened");
        _random.Calls.Should().Be(0, "a pickup rolls nothing (the admin spawn had explicit stars)");
    }

    [Fact]
    public async Task Pickup_RepeatedByTheSamePlayer_ReturnsTheirToken_AndAnotherPlayerIsRefused()
    {
        await SeedAsync();
        var spawn = await SpawnAsync(_weapons);
        var first = await PickupAsync(spawn, _alice);

        var again = await PickupAsync(spawn, _alice);
        var other = await PickupFailsAsync(spawn, _bob);

        again.Replay.Should().BeTrue();
        again.LootboxToken.Token.Should().Be(first.LootboxToken.Token);
        other.Code.Should().Be("AlreadyClaimed");
        await using var db = NewContext();
        db.LootboxTokens.Should().ContainSingle();
    }

    [Fact]
    public async Task Pickup_RefusesAWrongToken_AnExpiredBox_AndARemovedBox()
    {
        await SeedAsync();
        var wrong = await SpawnAsync(_weapons);
        var expired = await SpawnAsync(_weapons);
        var removed = await SpawnAsync(_weapons);
        await UpdateAsync(db => db.LootboxSpawns.Single(s => s.Id == expired.Id).ExpiresAt = _clock.GetUtcNow().UtcDateTime.AddSeconds(-1));
        await using (var db = NewContext())
        {
            await Runtime(db).DespawnAsync(removed.Id, _staff);
        }

        (await PickupFailsAsync(wrong, _alice, Guid.NewGuid())).Code.Should().Be("TokenMismatch");
        (await PickupFailsAsync(expired, _alice)).Code.Should().Be("Expired");
        (await PickupFailsAsync(removed, _alice)).Code.Should().Be("Removed");
    }

    // Ported from the removed open-on-the-spot claim's tests (LootboxRuntimeServiceTests), KNG-31.

    [Fact]
    public async Task Pickup_AfterTheLifetime_IsRefused_AndTheSweepMarksTheBoxExpired()
    {
        await SeedAsync();
        var spawn = await SpawnAsync(_weapons);
        _clock.Set(_clock.GetUtcNow().AddMinutes(LootboxRuntimeServiceConstants.DefaultLifetimeMinutes));

        (await PickupFailsAsync(spawn, _alice)).Code.Should().Be("Expired");

        await using var db = NewContext();
        (await db.LootboxSpawns.SingleAsync()).Status.Should().Be(LootboxSpawnStatus.Expired);
        (await Runtime(db).GetActiveAsync()).Should().BeEmpty();
        db.LootboxTokens.Should().BeEmpty();
    }

    [Fact]
    public async Task Pickup_RacingAnotherPickup_LosesOnTheStatusConcurrencyCheck()
    {
        await SeedAsync();
        var spawn = await SpawnAsync(_weapons);

        // Bob's request has already read the box as Active when another pickup flips it (its token not yet visible, as
        // inside that pickup's uncommitted transaction), so Bob gets past the replay lookup and the status check.
        await using var bobDb = NewContext();
        (await bobDb.LootboxSpawns.FindAsync(spawn.Id))!.Status.Should().Be(LootboxSpawnStatus.Active);
        await UpdateAsync(db =>
        {
            var box = db.LootboxSpawns.Single(s => s.Id == spawn.Id);
            box.Status = LootboxSpawnStatus.Claimed;
            box.ClaimedByUserId = _alice;
        });

        (await Runtime(bobDb).Invoking(s => s.PickupAsync(spawn.Id, new LootboxPickupRequestDto { Token = spawn.Token, UserId = _bob }))
            .Should().ThrowAsync<LootboxConflictException>()).Which.Code.Should().Be("AlreadyClaimed");

        await using var read = NewContext();
        read.LootboxTokens.Should().BeEmpty("the losing pickup wrote nothing");
        (await read.LootboxSpawns.SingleAsync()).ClaimedByUserId.Should().Be(_alice);
    }

    [Fact]
    public async Task Pickup_ByAFrozenPlayer_IsRefused_AndLeavesTheBox()
    {
        await SeedAsync();
        await UpdateAsync(db => db.Users.Single(u => u.Id == _alice).IsFrozen = true);
        var spawn = await SpawnAsync(_weapons);

        (await PickupFailsAsync(spawn, _alice)).Code.Should().Be("Frozen");

        await using var db = NewContext();
        (await db.LootboxSpawns.SingleAsync()).Status.Should().Be(LootboxSpawnStatus.Active);
        db.LootboxTokens.Should().BeEmpty();
    }

    [Fact]
    public async Task Pickup_CountsAgainstTheDailyCap_PerUtcDay_GlobalThenPerType()
    {
        await SeedAsync();
        await UpdateAsync(db => db.LootboxConfigurations.Single().MaxClaimsPerPlayerPerDay = 2);
        await PickupAsync(await SpawnAsync(_weapons), _alice);
        await PickupAsync(await SpawnAsync(_food), _alice);

        var third = await PickupFailsAsync(await SpawnAsync(_weapons), _alice);

        var limit = third.Should().BeOfType<LootboxDailyLimitException>().Subject;
        (limit.Code, limit.Scope, limit.Limit).Should().Be(("DailyPickupLimit", "Global", 2));
        limit.ResetsAt.Should().Be(new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc));
        (await PickupAsync(await SpawnAsync(_weapons), _bob)).Replay.Should().BeFalse("the cap is per player");

        // A new UTC day starts a fresh count; a per-type limit then applies on its own.
        _clock.Set(new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero));
        await UpdateAsync(db =>
        {
            db.LootboxConfigurations.Single().MaxClaimsPerPlayerPerDay = 10;
            db.LootboxTypes.Single(t => t.Id == _weapons).MaxClaimsPerPlayerPerDay = 1;
        });
        await PickupAsync(await SpawnAsync(_weapons), _alice);
        var perType = (LootboxDailyLimitException)await PickupFailsAsync(await SpawnAsync(_weapons), _alice);
        (perType.Code, perType.Scope).Should().Be(("DailyPickupLimit", "Type"));
        (await PickupAsync(await SpawnAsync(_food), _alice)).Replay.Should().BeFalse();
    }

    [Fact]
    public async Task APickedUpToken_OpensLikeAnyToken_AndTheDropLogCallsItAWorldDrop()
    {
        await SeedAsync();
        var picked = (await PickupAsync(await SpawnAsync(_weapons), _alice)).LootboxToken;

        LootboxClaimResultDto claim;
        await using (var db = NewContext())
        {
            claim = await Runtime(db).RedeemTokenAsync(picked.Token, new LootboxTokenRedeemRequestDto { UserId = _alice, IdempotencyKey = "open-1" });
        }

        claim.ItemName.Should().Be("Steel Sword");
        claim.ItemInstanceId.Should().NotBeNull();
        await using var read = NewContext();
        var log = await Runtime(read).SearchClaimsAsync(new PagedQueryDto
        {
            PageNumber = 1, PageSize = 10, Filters = new Dictionary<string, string> { ["source"] = "World" },
        });
        log.Items.Should().ContainSingle().Which.Source.Should().Be("World");
        var tokensOnly = await Runtime(read).SearchClaimsAsync(new PagedQueryDto
        {
            PageNumber = 1, PageSize = 10, Filters = new Dictionary<string, string> { ["source"] = "Token" },
        });
        tokensOnly.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task WorldPickup_IsNotAReasonStaffCanIssue()
    {
        await SeedAsync();
        await using var db = NewContext();

        await Runtime(db).Invoking(s => s.IssueTokensAsync(new LootboxTokenIssueRequestDto
        {
            UserId = _alice, TypeId = _weapons, BoxStars = 3, Quantity = 1, Reason = "WorldPickup",
        }, _staff)).Should().ThrowAsync<ArgumentException>();
    }

    // ===== Token status (join scan) =====

    [Fact]
    public async Task TokenStatus_ReportsIssuedRedeemedRevokedAndUnknown()
    {
        await SeedAsync();
        List<LootboxTokenDto> issued;
        await using (var db = NewContext())
        {
            issued = (await Runtime(db).IssueTokensAsync(new LootboxTokenIssueRequestDto
            {
                UserId = _alice, TypeId = _weapons, BoxStars = 3, Quantity = 3,
            }, _staff)).Tokens;
            await Runtime(db).RedeemTokenAsync(issued[1].Token, new LootboxTokenRedeemRequestDto { UserId = _alice, IdempotencyKey = "k" });
            await Runtime(db).RevokeTokenAsync(issued[2].Token, _staff);
        }
        var unknown = Guid.NewGuid();

        await using var read = NewContext();
        var statuses = await Runtime(read).GetTokenStatusesAsync(new LootboxTokenStatusRequestDto
        {
            Tokens = new List<Guid> { issued[0].Token, issued[1].Token, issued[2].Token, unknown },
        });

        statuses.Select(s => (s.Token, s.Status)).Should().Equal(
            (issued[0].Token, "Issued"), (issued[1].Token, "Redeemed"), (issued[2].Token, "Revoked"), (unknown, "Unknown"));
    }

    // ===== LootboxWorldChanged =====

    [Fact]
    public async Task Despawn_AndRevoke_TellTheGameServer()
    {
        await SeedAsync();
        var spawn = await SpawnAsync(_weapons);
        LootboxTokenDto token;
        await using (var db = NewContext())
        {
            await Runtime(db).DespawnAsync(spawn.Id, _staff);
            await Runtime(db).DespawnAsync(spawn.Id, _staff); // already gone: nothing new to tell
            token = (await Runtime(db).IssueTokensAsync(new LootboxTokenIssueRequestDto
            {
                UserId = _alice, TypeId = _weapons, BoxStars = 3, Quantity = 1,
            }, _staff)).Tokens.Single();
            await Runtime(db).RevokeTokenAsync(token.Token, _staff);
        }

        var changes = WorldChanges();
        changes.Should().HaveCount(2);
        changes[0].RemovedSpawnIds.Should().Equal(spawn.Id);
        changes[0].RevokedTokens.Should().BeEmpty();
        changes[1].RevokedTokens.Should().Equal(token.Token);
        _notifications.GetPending().Where(n => n.Type == PlayerNotificationTypes.LootboxWorldChanged)
            .Should().OnlyContain(n => n.UserId == 0, "it is for the game server, not one player");
    }

    [Fact]
    public async Task AWebAreaDelete_TellsTheGameServerAboutItsActiveBoxes()
    {
        await SeedAsync();
        int areaId, activeId;
        await using (var db = NewContext())
        {
            var area = new LootboxSpawnArea { Name = "market", World = "world", WgRegionId = "market_region", Enabled = true };
            db.LootboxSpawnAreas.Add(area);
            await db.SaveChangesAsync();
            var active = new LootboxSpawn
            {
                Token = Guid.NewGuid(), LootboxTypeId = _weapons, BoxGradeId = _grade3, SpawnAreaId = area.Id, World = "world",
                X = 1, Y = 64, Z = 1, Status = LootboxSpawnStatus.Active, SpawnedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1), ServerId = "dev",
            };
            db.LootboxSpawns.Add(active);
            await db.SaveChangesAsync();
            (areaId, activeId) = (area.Id, active.Id);
        }

        await using (var db = NewContext())
        {
            var users = new UserRepository(db);
            var service = new LootboxSpawnAreaService(new LootboxSpawnAreaRepository(db), Mapper(),
                new AuditLogService(new AuditLogRepository(db), users), users, NullLogger<LootboxSpawnAreaService>.Instance, _notifications);
            await service.DeleteAsync(areaId);
        }

        WorldChanges().Should().ContainSingle().Which.RemovedSpawnIds.Should().Equal(activeId);
    }

    private sealed class ScriptedRandom : ILootRandom
    {
        public int Calls { get; private set; }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            Calls++;
            return maxExclusive - 1;
        }

        public double NextDouble()
        {
            Calls++;
            return 0.0;
        }
    }

    private sealed class PinnedClock : TimeProvider
    {
        private DateTimeOffset _now;

        public PinnedClock(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Set(DateTimeOffset now) => _now = now;
    }
}
