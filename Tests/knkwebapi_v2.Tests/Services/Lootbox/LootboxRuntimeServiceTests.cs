using AutoMapper;
using FluentAssertions;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Lootbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Lootbox;

/// <summary>
/// Lootboxes Phase 2 (docs/specs/lootboxes/IMPLEMENTATION_PLAN.md "LootboxRuntimeServiceTests"): spawn caps, the claim
/// transaction (instance mint, idempotent replay, double and concurrent claims, expiry, token), the per-UTC-day cap,
/// admin spawn/give, delivery and pending, and the in-game area endpoints. EF InMemory with the real repositories and
/// services, a scripted RNG and a pinned clock. InMemory enforces the Status concurrency token but not unique indexes;
/// those were checked on MySQL 8 (see the phase notes).
/// </summary>
public class LootboxRuntimeServiceTests
{
    private readonly string _dbName = $"LootboxRuntimeTestDb_{Guid.NewGuid()}";
    private readonly ScriptedRandom _random = new();
    private readonly PinnedClock _clock = new(new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero));

    private int _alice, _bob, _staff, _weapons, _food, _area, _sharpness, _unbreaking, _sword, _bread, _grade3, _grade5;

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
            NullLogger<LootboxRuntimeService>.Instance);
    }

    private static LootboxSpawnAreaService Areas(KnKDbContext db) => new(
        new LootboxSpawnAreaRepository(db),
        Mapper(),
        new AuditLogService(new AuditLogRepository(db), new UserRepository(db)),
        new UserRepository(db),
        NullLogger<LootboxSpawnAreaService>.Instance);

    // A small catalog: grades ★1-5 (no enchant cap), a Weapons type whose pool is one ★3 sword with Unbreaking 1 by
    // default and a 100% Sharpness 1-3 roll, and a Food type whose pool is one stackable ★3 bread.
    private async Task SeedAsync()
    {
        await using var db = NewContext();
        var grades = Enumerable.Range(1, 5)
            .Select(s => new Grade { Name = $"Grade{s}", Stars = s, DropChance = new[] { 70m, 60m, 40m, 25m, 15m }[s - 1] })
            .ToList();
        var weaponsCategory = new Category { Name = "Weapons" };
        var foodCategory = new Category { Name = "Food" };
        var sharpness = new EnchantmentDefinition { Key = "minecraft:sharpness", DisplayName = "Sharpness", MaxLevel = 5 };
        var unbreaking = new EnchantmentDefinition { Key = "minecraft:unbreaking", DisplayName = "Unbreaking", MaxLevel = 3 };
        var sword = new ItemBlueprint { Name = "Steel Sword", DefaultDisplayName = "&bSteel Sword", MaxStackSize = 1, Category = weaponsCategory, Grade = grades[2] };
        sword.DefaultEnchantments.Add(new ItemBlueprintDefaultEnchantment { ItemBlueprint = sword, EnchantmentDefinition = unbreaking, Level = 1 });
        var bread = new ItemBlueprint { Name = "Bread", DefaultDisplayName = "&9Bread", MaxStackSize = 64, DefaultQuantity = 8, Category = foodCategory, Grade = grades[2] };
        var weapons = new LootboxType { Name = "Weapons Lootbox", Category = weaponsCategory, Enabled = true, MinBoxStars = 3, MaxBoxStars = 3 };
        weapons.EnchantRolls.Add(new LootboxEnchantRoll { LootboxType = weapons, EnchantmentDefinition = sharpness, ChancePercent = 100m, MinLevel = 1, MaxLevel = 3 });
        var food = new LootboxType { Name = "Food Lootbox", Category = foodCategory, Enabled = true, MinBoxStars = 3, MaxBoxStars = 3 };
        var area = new LootboxSpawnArea { Name = "spawn", World = "world", WgRegionId = "lootbox_spawn", Enabled = true, MaxActive = 2 };
        var alice = new User { Username = "alice" };
        var bob = new User { Username = "bob" };
        var staff = new User { Username = "staff" };
        db.AddRange(grades);
        db.AddRange(weaponsCategory, foodCategory, sharpness, unbreaking, sword, bread, weapons, food, area, alice, bob, staff);
        db.LootboxConfigurations.Add(new LootboxConfiguration { Id = "global", GlobalMaxActive = 15, MaxClaimsPerPlayerPerDay = 10 });
        await db.SaveChangesAsync();

        (_alice, _bob, _staff) = (alice.Id, bob.Id, staff.Id);
        (_weapons, _food, _area) = (weapons.Id, food.Id, area.Id);
        (_sharpness, _unbreaking, _sword, _bread) = (sharpness.Id, unbreaking.Id, sword.Id, bread.Id);
        (_grade3, _grade5) = (grades[2].Id, grades[4].Id);
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    private async Task<LootboxSpawn> AddSpawnAsync(int typeId, int? areaId = null, DateTime? expiresAt = null)
    {
        await using var db = NewContext();
        var spawn = new LootboxSpawn
        {
            LootboxTypeId = typeId,
            BoxGradeId = _grade3,
            SpawnAreaId = areaId,
            World = "world",
            SpawnedAt = Now,
            ExpiresAt = expiresAt ?? Now.AddMinutes(30),
        };
        db.LootboxSpawns.Add(spawn);
        await db.SaveChangesAsync();
        return spawn;
    }

    private static LootboxClaimRequestDto Claim(LootboxSpawn spawn, int userId, string? key = null) =>
        new() { Token = spawn.Token, UserId = userId, IdempotencyKey = key ?? $"{spawn.Token}:{userId}" };

    private async Task<LootboxClaimResultDto> ClaimAsync(LootboxSpawn spawn, int userId, string? key = null)
    {
        await using var db = NewContext();
        return await Runtime(db).ClaimAsync(spawn.Id, Claim(spawn, userId, key));
    }

    private async Task<LootboxConflictException> ClaimFailsAsync(LootboxSpawn spawn, int userId, string? key = null)
    {
        await using var db = NewContext();
        return (await Runtime(db).Invoking(s => s.ClaimAsync(spawn.Id, Claim(spawn, userId, key)))
            .Should().ThrowAsync<LootboxConflictException>()).Which;
    }

    private async Task UpdateAsync(Action<KnKDbContext> change)
    {
        await using var db = NewContext();
        change(db);
        await db.SaveChangesAsync();
    }

    // ===== Spawn =====

    [Fact]
    public async Task Spawn_InAnArea_RollsAnAllowedTypeAndExpiresAfterTheAreaLifetime()
    {
        await SeedAsync();
        await UpdateAsync(db => db.LootboxSpawnAreaTypes.Add(new LootboxSpawnAreaType { LootboxSpawnAreaId = _area, LootboxTypeId = _food }));
        await using var db = NewContext();

        var spawn = await Runtime(db).SpawnAsync(new LootboxSpawnRequestDto { AreaId = _area, World = "world", X = 10, Y = 64, Z = -5, ServerId = "main" });

        spawn.LootboxTypeId.Should().Be(_food, "the area only allows Food boxes");
        (spawn.Status, spawn.BoxStars, spawn.SpawnAreaId, spawn.SpawnAreaName, spawn.X, spawn.Y, spawn.Z)
            .Should().Be(("Active", 3, (int?)_area, "spawn", 10, 64, -5));
        spawn.BoxLabel.Should().Be("Grade3 Food Lootbox");
        spawn.ExpiresAt.Should().Be(Now.AddMinutes(30));
        spawn.Token.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Spawn_RespectsTheAreaAndGlobalCaps_AndTheSwitches()
    {
        await SeedAsync();
        var request = new LootboxSpawnRequestDto { AreaId = _area, World = "world", X = 0, Y = 64, Z = 0 };
        async Task<string> RefusedAsync()
        {
            await using var db = NewContext();
            return (await Runtime(db).Invoking(s => s.SpawnAsync(request)).Should().ThrowAsync<LootboxConflictException>()).Which.Code;
        }

        await AddSpawnAsync(_weapons, _area);
        await AddSpawnAsync(_weapons, _area);
        (await RefusedAsync()).Should().Be("AreaFull");

        await UpdateAsync(db =>
        {
            db.LootboxSpawnAreas.Single().MaxActive = 10;
            db.LootboxConfigurations.Single().GlobalMaxActive = 2;
        });
        (await RefusedAsync()).Should().Be("GlobalFull");

        await UpdateAsync(db => db.LootboxConfigurations.Single().Enabled = false);
        (await RefusedAsync()).Should().Be("Disabled");

        await UpdateAsync(db =>
        {
            var config = db.LootboxConfigurations.Single();
            config.Enabled = true;
            config.GlobalMaxActive = 15;
            db.LootboxSpawnAreas.Single().Enabled = false;
        });
        (await RefusedAsync()).Should().Be("Disabled");

        await UpdateAsync(db =>
        {
            db.LootboxSpawnAreas.Single().Enabled = true;
            foreach (var type in db.LootboxTypes) type.Enabled = false;
        });
        (await RefusedAsync()).Should().Be("NoEnabledType");
    }

    [Fact]
    public async Task Spawn_InTheWrongWorld_IsRefused()
    {
        await SeedAsync();
        await using var db = NewContext();

        await Runtime(db).Invoking(s => s.SpawnAsync(new LootboxSpawnRequestDto { AreaId = _area, World = "world_nether", Y = 64 }))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task AdminSpawn_IgnoresCaps_AndIsAudited()
    {
        await SeedAsync();
        await UpdateAsync(db => db.LootboxConfigurations.Single().GlobalMaxActive = 0);
        await using var db = NewContext();

        var spawn = await Runtime(db).AdminSpawnAsync(
            new LootboxAdminSpawnRequestDto { TypeId = _weapons, BoxStars = 5, World = "world", X = 1, Y = 70, Z = 2 }, _staff);

        (spawn.BoxStars, spawn.SpawnAreaId, spawn.CreatedByUserId).Should().Be((5, (int?)null, (int?)_staff));
        var audit = await db.AuditLogEntries.SingleAsync();
        (audit.Action, audit.ActorUserId, audit.TargetUserId).Should().Be((AuditAction.LootboxSpawnedByAdmin, (int?)_staff, _staff));
        audit.Details.Should().Contain("\"event\":\"Spawned\"");
    }

    [Fact]
    public async Task Despawn_RemovesAnActiveBox_AndLeavesAClaimedOneAlone()
    {
        await SeedAsync();
        var active = await AddSpawnAsync(_weapons);
        var claimed = await AddSpawnAsync(_weapons);
        await ClaimAsync(claimed, _alice);
        await using var db = NewContext();

        (await Runtime(db).DespawnAsync(active.Id, _staff)).Status.Should().Be("Removed");
        (await Runtime(db).DespawnAsync(claimed.Id, _staff)).Status.Should().Be("Claimed");
        (await Runtime(db).GetActiveAsync()).Should().BeEmpty();
    }

    // ===== Claim =====

    [Fact]
    public async Task Claim_MintsOneInstanceOwnedByTheClaimer_WithTheRolledEnchantments()
    {
        await SeedAsync();
        var spawn = await AddSpawnAsync(_weapons);

        var result = await ClaimAsync(spawn, _alice);

        result.Replay.Should().BeFalse();
        (result.ItemBlueprintId, result.ItemName, result.ItemGradeStars, result.Quantity, result.IsSpecial, result.BoxStars)
            .Should().Be((_sword, "Steel Sword", (int?)3, 1, false, 3));
        // Unbreaking 1 by default, Sharpness rolled at the top of 1-3 by the scripted RNG.
        result.Enchantments.Select(e => (e.DefinitionId, e.Key, e.Level))
            .Should().BeEquivalentTo(new[] { (_sharpness, "minecraft:sharpness", 3), (_unbreaking, "minecraft:unbreaking", 1) });
        result.ItemInstanceId.Should().NotBeNull();

        await using var db = NewContext();
        var claim = await db.LootboxClaims.SingleAsync();
        (claim.Id, claim.UserId, claim.LootboxSpawnId, claim.ItemInstanceId).Should().Be((result.ClaimId, _alice, (int?)spawn.Id, result.ItemInstanceId));
        var instance = await db.ItemInstances.Include(i => i.Enchantments).SingleAsync();
        (instance.Id, instance.Origin, instance.OriginRef, instance.OwnerUserId, instance.ItemBlueprintId, instance.GradeId)
            .Should().Be((result.ItemInstanceId!.Value, ItemInstanceOrigin.Lootbox, result.ClaimId.ToString(), (int?)_alice, _sword, (int?)_grade3));
        instance.Enchantments.Select(e => (e.EnchantmentDefinitionId, e.Level))
            .Should().BeEquivalentTo(new[] { (_sharpness, 3), (_unbreaking, 1) });
        var stored = await db.LootboxSpawns.SingleAsync();
        (stored.Status, stored.ClaimedByUserId, stored.ClaimedAt).Should().Be((LootboxSpawnStatus.Claimed, (int?)_alice, (DateTime?)Now));
    }

    [Fact]
    public async Task Claim_OfAStackableItem_HasNoInstance()
    {
        await SeedAsync();
        var spawn = await AddSpawnAsync(_food);

        var result = await ClaimAsync(spawn, _alice);

        (result.ItemBlueprintId, result.Quantity, result.ItemInstanceId).Should().Be((_bread, 8, (long?)null));
        await using var db = NewContext();
        (await db.ItemInstances.AnyAsync()).Should().BeFalse();
        (await db.LootboxClaims.SingleAsync()).ItemInstanceId.Should().BeNull();
    }

    [Fact]
    public async Task Claim_SameIdempotencyKey_ReplaysTheSameResult_WithoutRollingOrMintingAgain()
    {
        await SeedAsync();
        var spawn = await AddSpawnAsync(_weapons);

        var first = await ClaimAsync(spawn, _alice);
        var rollsAfterFirst = _random.Calls;
        var second = await ClaimAsync(spawn, _alice);

        second.Replay.Should().BeTrue();
        second.Should().BeEquivalentTo(first, o => o.Excluding(r => r.Replay));
        _random.Calls.Should().Be(rollsAfterFirst, "a replay never rolls");
        await using var db = NewContext();
        (await db.LootboxClaims.CountAsync()).Should().Be(1);
        (await db.ItemInstances.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Claim_BySomeoneElse_IsAlreadyClaimed()
    {
        await SeedAsync();
        var spawn = await AddSpawnAsync(_weapons);
        await ClaimAsync(spawn, _alice);

        (await ClaimFailsAsync(spawn, _bob)).Code.Should().Be("AlreadyClaimed");
    }

    [Fact]
    public async Task Claim_ReusingSomeoneElsesIdempotencyKey_IsRefused()
    {
        await SeedAsync();
        var spawn = await AddSpawnAsync(_weapons);
        await ClaimAsync(spawn, _alice);

        (await ClaimFailsAsync(spawn, _bob, $"{spawn.Token}:{_alice}")).Code.Should().Be("IdempotencyKeyReused");
    }

    [Fact]
    public async Task Claim_RacingAnotherClaim_LosesOnTheStatusConcurrencyCheck()
    {
        await SeedAsync();
        var spawn = await AddSpawnAsync(_weapons);

        // Bob's request has already read the box as Active when Alice's claim commits.
        await using var bobDb = NewContext();
        (await bobDb.LootboxSpawns.FindAsync(spawn.Id))!.Status.Should().Be(LootboxSpawnStatus.Active);
        await ClaimAsync(spawn, _alice);

        var lost = await Runtime(bobDb).Invoking(s => s.ClaimAsync(spawn.Id, Claim(spawn, _bob)))
            .Should().ThrowAsync<LootboxConflictException>();
        lost.Which.Code.Should().Be("AlreadyClaimed");

        await using var db = NewContext();
        (await db.LootboxClaims.SingleAsync()).UserId.Should().Be(_alice);
        (await db.ItemInstances.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Claim_AfterExpiry_IsRefused_AndTheSweepMarksItExpired()
    {
        await SeedAsync();
        var spawn = await AddSpawnAsync(_weapons, expiresAt: Now.AddMinutes(5));
        _clock.Advance(TimeSpan.FromMinutes(5));

        (await ClaimFailsAsync(spawn, _alice)).Code.Should().Be("Expired");

        await using var db = NewContext();
        (await db.LootboxSpawns.SingleAsync()).Status.Should().Be(LootboxSpawnStatus.Expired);
        (await Runtime(db).GetActiveAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Claim_WithTheWrongToken_IsRefused()
    {
        await SeedAsync();
        var spawn = await AddSpawnAsync(_weapons);
        await using var db = NewContext();

        var refused = await Runtime(db).Invoking(s => s.ClaimAsync(spawn.Id, new LootboxClaimRequestDto { Token = Guid.NewGuid(), UserId = _alice, IdempotencyKey = "k" }))
            .Should().ThrowAsync<LootboxConflictException>();

        refused.Which.Code.Should().Be("TokenMismatch");
    }

    [Fact]
    public async Task Claim_ByAFrozenPlayer_IsRefused()
    {
        await SeedAsync();
        await UpdateAsync(db => db.Users.Single(u => u.Id == _alice).IsFrozen = true);
        var spawn = await AddSpawnAsync(_weapons);

        (await ClaimFailsAsync(spawn, _alice)).Code.Should().Be("Frozen");
    }

    // ===== Daily cap (UTC calendar day) =====

    private async Task<LootboxDailyLimitException> CapHitAsync(int typeId, int userId)
    {
        var spawn = await AddSpawnAsync(typeId);
        await using var db = NewContext();
        return (await Runtime(db).Invoking(s => s.ClaimAsync(spawn.Id, Claim(spawn, userId)))
            .Should().ThrowAsync<LootboxDailyLimitException>()).Which;
    }

    [Fact]
    public async Task DailyCap_TheEleventhBoxOfTheDay_IsRefused_UntilMidnightUtc()
    {
        await SeedAsync();
        _clock.Set(new DateTimeOffset(2026, 9, 26, 0, 0, 0, TimeSpan.Zero));
        for (var i = 0; i < 10; i++)
        {
            await ClaimAsync(await AddSpawnAsync(i % 2 == 0 ? _weapons : _food), _alice);
            _clock.Advance(TimeSpan.FromHours(2));
        }

        // 20:00 → 23:59:59 on the same UTC day.
        _clock.Set(new DateTimeOffset(2026, 9, 26, 23, 59, 59, TimeSpan.Zero));
        var refused = await CapHitAsync(_weapons, _alice);
        (refused.Code, refused.Scope, refused.Limit).Should().Be(("DailyLimit", "Global", 10));
        refused.ResetsAt.Should().Be(new DateTime(2026, 9, 27, 0, 0, 0, DateTimeKind.Utc));

        (await ClaimAsync(await AddSpawnAsync(_weapons), _bob)).Replay.Should().BeFalse("the cap is per player");

        _clock.Set(new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero));
        (await ClaimAsync(await AddSpawnAsync(_weapons), _alice)).Replay.Should().BeFalse("00:00 UTC starts a fresh count");
    }

    [Fact]
    public async Task DailyCap_AClaimAt235959_CountsForThatDay()
    {
        await SeedAsync();
        await UpdateAsync(db => db.LootboxConfigurations.Single().MaxClaimsPerPlayerPerDay = 1);

        _clock.Set(new DateTimeOffset(2026, 9, 26, 23, 59, 59, 700, TimeSpan.Zero));
        var late = await ClaimAsync(await AddSpawnAsync(_weapons), _alice);
        late.ClaimedAt.Should().Be(new DateTime(2026, 9, 26, 23, 59, 59, DateTimeKind.Utc), "stored in whole seconds, never rounded into tomorrow");
        (await CapHitAsync(_weapons, _alice)).ResetsAt.Should().Be(new DateTime(2026, 9, 27, 0, 0, 0, DateTimeKind.Utc));

        _clock.Set(new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero));
        (await ClaimAsync(await AddSpawnAsync(_weapons), _alice)).ClaimId.Should().BeGreaterThan(late.ClaimId);
    }

    [Fact]
    public async Task DailyCap_PerTypeLimit_StopsThatTypeOnly()
    {
        await SeedAsync();
        await UpdateAsync(db => db.LootboxTypes.Single(t => t.Id == _weapons).MaxClaimsPerPlayerPerDay = 2);

        await ClaimAsync(await AddSpawnAsync(_weapons), _alice);
        await ClaimAsync(await AddSpawnAsync(_weapons), _alice);
        var refused = await CapHitAsync(_weapons, _alice);

        (refused.Scope, refused.Limit).Should().Be(("Type", 2));
        (await ClaimAsync(await AddSpawnAsync(_food), _alice)).ItemBlueprintId.Should().Be(_bread);
    }

    [Fact]
    public async Task DailyCap_AdminGivesDontCount_AndAreAudited()
    {
        await SeedAsync();
        await UpdateAsync(db => db.LootboxConfigurations.Single().MaxClaimsPerPlayerPerDay = 1);

        await using (var db = NewContext())
        {
            for (var i = 0; i < 3; i++)
            {
                var given = await Runtime(db).AdminGiveAsync(new LootboxAdminGiveRequestDto { UserId = _alice, TypeId = _weapons, BoxStars = 3 }, _staff);
                (given.LootboxSpawnId, given.ItemInstanceId.HasValue).Should().Be(((int?)null, true));
            }
        }

        (await ClaimAsync(await AddSpawnAsync(_weapons), _alice)).Replay.Should().BeFalse("admin gives aren't counted");
        (await CapHitAsync(_weapons, _alice)).Scope.Should().Be("Global");

        await using var read = NewContext();
        var audits = await read.AuditLogEntries.ToListAsync();
        audits.Should().HaveCount(3).And.OnlyContain(a => a.Action == AuditAction.LootboxGranted && a.ActorUserId == _staff && a.TargetUserId == _alice);
        (await read.ItemInstances.CountAsync()).Should().Be(4);
    }

    [Fact]
    public async Task AdminGive_WithAKey_ReplaysOnRetry()
    {
        await SeedAsync();
        await using var db = NewContext();
        var request = new LootboxAdminGiveRequestDto { UserId = _alice, TypeId = _weapons, IdempotencyKey = "give-1" };

        var first = await Runtime(db).AdminGiveAsync(request, _staff);
        var second = await Runtime(db).AdminGiveAsync(request, _staff);

        second.Replay.Should().BeTrue();
        second.ClaimId.Should().Be(first.ClaimId);
        second.ItemInstanceId.Should().Be(first.ItemInstanceId);
        (await db.AuditLogEntries.CountAsync()).Should().Be(1);
    }

    // ===== Delivery =====

    [Fact]
    public async Task Pending_ListsUndeliveredClaimsOlderThan30Seconds_AndDeliveredIsIdempotent()
    {
        await SeedAsync();
        var old = await ClaimAsync(await AddSpawnAsync(_weapons), _alice);
        _clock.Advance(TimeSpan.FromSeconds(20));
        var fresh = await ClaimAsync(await AddSpawnAsync(_food), _alice);
        _clock.Advance(TimeSpan.FromSeconds(15));
        await using var db = NewContext();
        var runtime = Runtime(db);

        var pending = await runtime.GetPendingAsync(_alice);
        pending.Select(p => p.ClaimId).Should().Equal(old.ClaimId);
        pending[0].Should().BeEquivalentTo(old, "pending carries the claim payload, instance id included");
        (await runtime.GetPendingAsync(_bob)).Should().BeEmpty();

        var delivered = await runtime.MarkDeliveredAsync(old.ClaimId, new LootboxDeliveredRequestDto { Method = "Redelivered", Note = "join", UserId = _alice });
        _clock.Advance(TimeSpan.FromMinutes(1));
        var again = await runtime.MarkDeliveredAsync(old.ClaimId, new LootboxDeliveredRequestDto { Method = "Inventory" });

        (delivered.AlreadyDelivered, again.AlreadyDelivered).Should().Be((false, true));
        again.DeliveredAt.Should().Be(delivered.DeliveredAt);
        again.DeliveryMethod.Should().Be("Redelivered");
        (await runtime.GetPendingAsync(_alice)).Select(p => p.ClaimId).Should().Equal(fresh.ClaimId);

        await runtime.Invoking(r => r.MarkDeliveredAsync(fresh.ClaimId, new LootboxDeliveredRequestDto { Method = "Inventory", UserId = _bob }))
            .Should().ThrowAsync<LootboxConflictException>();
        await runtime.Invoking(r => r.MarkDeliveredAsync(fresh.ClaimId, new LootboxDeliveredRequestDto { Method = "Teleported" }))
            .Should().ThrowAsync<ArgumentException>();
    }

    // ===== Reads =====

    [Fact]
    public async Task RuntimeConfig_HasEnabledTypes_AllAreasWithTheirActiveCount()
    {
        await SeedAsync();
        await UpdateAsync(db =>
        {
            db.LootboxTypes.Single(t => t.Id == _food).Enabled = false;
            db.LootboxSpawnAreas.Add(new LootboxSpawnArea { Name = "arena", World = "world", WgRegionId = "arena", Enabled = false, ExcludedRegionIds = "a,b" });
        });
        await AddSpawnAsync(_weapons, _area);
        await using var db = NewContext();

        var config = await Runtime(db).GetRuntimeConfigAsync();

        config.Types.Select(t => (t.Id, t.DisplayMaterialKey)).Should().Equal((_weapons, "minecraft:chest"));
        config.Areas.Select(a => (a.Name, a.Enabled, a.ActiveCount)).Should().Equal(("arena", false, 0), ("spawn", true, 1));
        config.Areas[0].ExcludedRegionIds.Should().Equal("a", "b");
        config.Grades.Select(g => g.Stars).Should().Equal(1, 2, 3, 4, 5);
        (config.MaxClaimsPerPlayerPerDay, config.GlobalMaxActive).Should().Be(((int?)10, 15));
    }

    [Fact]
    public async Task SearchClaims_FiltersTheDropLog()
    {
        await SeedAsync();
        await ClaimAsync(await AddSpawnAsync(_weapons), _alice);
        await ClaimAsync(await AddSpawnAsync(_food), _bob);
        await using var db = NewContext();
        await Runtime(db).AdminGiveAsync(new LootboxAdminGiveRequestDto { UserId = _bob, TypeId = _weapons }, _staff);

        var bob = await Runtime(db).SearchClaimsAsync(new PagedQueryDto { Filters = new() { ["userId"] = _bob.ToString() } });
        bob.TotalCount.Should().Be(2);
        bob.Items.Select(i => i.IsAdminGive).Should().Equal(true, false);

        var sword = await Runtime(db).SearchClaimsAsync(new PagedQueryDto { SearchTerm = "steel" });
        sword.Items.Should().HaveCount(2).And.OnlyContain(i => i.ItemInstanceId != null && i.ItemName == "Steel Sword");
    }

    // ===== In-game areas =====

    [Fact]
    public async Task InGameArea_Create_IsEnabledWithDefaults_AndRefusesTakenNamesAndRegions()
    {
        await SeedAsync();
        await using var db = NewContext();
        var areas = Areas(db);

        var created = await areas.CreateInGameAsync(new LootboxInGameAreaCreateDto { Name = "market", World = "world", WgRegionId = "lootbox_market" }, _staff);

        (created.Enabled, created.MaxActive, created.SpawnIntervalSeconds, created.MinOnlinePlayers, created.CreatedByUserId)
            .Should().Be((true, 3, 600, 3, (int?)_staff));
        var audit = await db.AuditLogEntries.SingleAsync();
        (audit.Action, audit.TargetUserId).Should().Be((AuditAction.LootboxSpawnedByAdmin, _staff));
        audit.Details.Should().Contain("\"event\":\"AreaCreated\"");

        (await areas.Invoking(a => a.CreateInGameAsync(new LootboxInGameAreaCreateDto { Name = "MARKET", World = "world", WgRegionId = "lootbox_other" }, _staff))
            .Should().ThrowAsync<LootboxConflictException>()).Which.Code.Should().Be("NameTaken");
        (await areas.Invoking(a => a.CreateInGameAsync(new LootboxInGameAreaCreateDto { Name = "market2", World = "world", WgRegionId = "LOOTBOX_SPAWN" }, _staff))
            .Should().ThrowAsync<LootboxConflictException>()).Which.Code.Should().Be("RegionInUse");
        await areas.Invoking(a => a.CreateInGameAsync(new LootboxInGameAreaCreateDto { Name = "bad name", World = "world", WgRegionId = "x" }, _staff))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task InGameArea_Delete_RemovesTheRow_AndItsActiveBoxes_KeepingTheirRows()
    {
        await SeedAsync();
        var active = await AddSpawnAsync(_weapons, _area);
        await using var db = NewContext();

        var deleted = await Areas(db).DeleteInGameAsync(_area, _staff);

        (deleted.Name, deleted.WgRegionId).Should().Be(("spawn", "lootbox_spawn"));
        deleted.RemovedSpawnIds.Should().Equal(active.Id);
        await using var read = NewContext();
        (await read.LootboxSpawnAreas.AnyAsync()).Should().BeFalse();
        var spawn = await read.LootboxSpawns.SingleAsync();
        (spawn.Status, spawn.SpawnAreaId).Should().Be((LootboxSpawnStatus.Removed, (int?)null));
        (await read.AuditLogEntries.SingleAsync()).Details.Should().Contain("\"event\":\"AreaDeleted\"");
        await Areas(read).Invoking(a => a.DeleteInGameAsync(_area, _staff)).Should().ThrowAsync<KeyNotFoundException>();
    }

    // ===== Test doubles =====

    /// <summary>Picks the first weighted option and the top of every integer range (so a 100% roll hits and a
    /// ChancePerMillion below 1,000,000 never does); counts calls.</summary>
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

        public void Advance(TimeSpan by) => _now += by;
    }
}
