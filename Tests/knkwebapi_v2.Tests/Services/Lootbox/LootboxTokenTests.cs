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
/// Lootboxes Phase 5 (docs/specs/lootboxes/IMPLEMENTATION_PLAN.md "Lootbox token items"): issuing token items,
/// redeeming one through the claim path (single use, idempotent retry, a duplicated item refused, the UTC daily cap),
/// delivery, revoke, the drop log and the grant hooks (premium tier, kit). EF InMemory with the real repositories and
/// services, a scripted RNG and a pinned clock. The row-level race on MySQL is covered by
/// <c>LootboxTokenMySqlTests</c> (opt-in, needs a server).
/// </summary>
public class LootboxTokenTests
{
    private readonly string _dbName = $"LootboxTokenTestDb_{Guid.NewGuid()}";
    private readonly ScriptedRandom _random = new();
    private readonly PinnedClock _clock = new(new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryPlayerNotificationQueue _notifications = new();

    private int _alice, _bob, _staff, _weapons, _food, _empty, _sword, _grade3, _grade5, _noble, _defaultGroup, _kit;

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

    private LootboxTokenGrantService Grants(KnKDbContext db) => new(
        new LootboxTokenGrantRepository(db),
        Runtime(db),
        new UserRepository(db),
        _clock,
        NullLogger<LootboxTokenGrantService>.Instance,
        _notifications);

    // Grades ★1-5, a Weapons type (one ★3 sword, a 100% Sharpness roll), a Food type (one stackable ★3 bread), an
    // Armor type with nothing in its pool, a Noble premium tier, the Default group and a kit.
    private async Task SeedAsync()
    {
        await using var db = NewContext();
        var grades = Enumerable.Range(1, 5)
            .Select(s => new Grade { Name = new[] { "Common", "Uncommon", "Rare", "Epic", "Legendary" }[s - 1], Stars = s, DropChance = new[] { 70m, 60m, 40m, 25m, 15m }[s - 1] })
            .ToList();
        var weaponsCategory = new Category { Name = "Weapons" };
        var foodCategory = new Category { Name = "Food" };
        var armorCategory = new Category { Name = "Armor" };
        var sharpness = new EnchantmentDefinition { Key = "minecraft:sharpness", DisplayName = "Sharpness", MaxLevel = 5 };
        var sword = new ItemBlueprint { Name = "Steel Sword", DefaultDisplayName = "&bSteel Sword", MaxStackSize = 1, Category = weaponsCategory, Grade = grades[2] };
        var bread = new ItemBlueprint { Name = "Bread", DefaultDisplayName = "&9Bread", MaxStackSize = 64, DefaultQuantity = 8, Category = foodCategory, Grade = grades[2] };
        var weapons = new LootboxType { Name = "Weapons Lootbox", Category = weaponsCategory, Enabled = false, MinBoxStars = 3, MaxBoxStars = 3 };
        weapons.EnchantRolls.Add(new LootboxEnchantRoll { LootboxType = weapons, EnchantmentDefinition = sharpness, ChancePercent = 100m, MinLevel = 1, MaxLevel = 3 });
        var food = new LootboxType { Name = "Food Lootbox", Category = foodCategory, Enabled = true, MinBoxStars = 3, MaxBoxStars = 3 };
        var empty = new LootboxType { Name = "Armor Lootbox", Category = armorCategory, Enabled = true };
        var alice = new User { Username = "alice", Uuid = "00000000-0000-0000-0000-00000000000a" };
        var bob = new User { Username = "bob" };
        var staff = new User { Username = "staff" };
        var noble = new PermissionGroup { Name = "Noble", IsPremiumTier = true, Weight = 10 };
        var defaultGroup = new PermissionGroup { Name = "Default", Weight = 0 };
        var kit = new Kit { Name = "Starter" };
        db.AddRange(grades);
        db.AddRange(weaponsCategory, foodCategory, armorCategory, sharpness, sword, bread, weapons, food, empty, alice, bob, staff, noble, defaultGroup, kit);
        db.LootboxConfigurations.Add(new LootboxConfiguration { Id = "global", GlobalMaxActive = 15, MaxClaimsPerPlayerPerDay = 10 });
        await db.SaveChangesAsync();

        (_alice, _bob, _staff) = (alice.Id, bob.Id, staff.Id);
        (_weapons, _food, _empty, _sword) = (weapons.Id, food.Id, empty.Id, sword.Id);
        (_grade3, _grade5) = (grades[2].Id, grades[4].Id);
        (_noble, _defaultGroup, _kit) = (noble.Id, defaultGroup.Id, kit.Id);
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    private async Task<List<LootboxTokenDto>> IssueAsync(int typeId, int userId, int quantity = 1, int? stars = 3, string? key = null, string? reason = null)
    {
        await using var db = NewContext();
        var result = await Runtime(db).IssueTokensAsync(new LootboxTokenIssueRequestDto
        {
            UserId = userId, TypeId = typeId, BoxStars = stars, Quantity = quantity, IdempotencyKey = key, Reason = reason,
        }, _staff);
        return result.Tokens;
    }

    private static LootboxTokenRedeemRequestDto Redeem(int userId, string? key = null) =>
        new() { UserId = userId, IdempotencyKey = key ?? $"token-open:{Guid.NewGuid():N}" };

    private async Task<LootboxClaimResultDto> RedeemAsync(Guid token, int userId, string? key = null)
    {
        await using var db = NewContext();
        return await Runtime(db).RedeemTokenAsync(token, Redeem(userId, key));
    }

    private async Task<string> RedeemFailsAsync(Guid token, int userId, string? key = null)
    {
        await using var db = NewContext();
        return (await Runtime(db).Invoking(s => s.RedeemTokenAsync(token, Redeem(userId, key)))
            .Should().ThrowAsync<LootboxConflictException>()).Which.Code;
    }

    private async Task UpdateAsync(Action<KnKDbContext> change)
    {
        await using var db = NewContext();
        change(db);
        await db.SaveChangesAsync();
    }

    // ===== Issue =====

    [Fact]
    public async Task Issue_CreatesDistinctUnopenedTokens_Audited()
    {
        await SeedAsync();

        var tokens = await IssueAsync(_weapons, _alice, quantity: 3, stars: 5);

        tokens.Should().HaveCount(3);
        tokens.Select(t => t.Token).Distinct().Should().HaveCount(3, "each token item is its own identity");
        tokens.Should().OnlyContain(t => t.Status == "Issued" && t.BoxStars == 5 && t.BoxLabel == "Legendary Weapons Lootbox"
            && t.IssuedToUserId == _alice && t.Reason == "Admin" && t.IssuedByUserId == _staff && t.DeliveredAt == null);

        await using var db = NewContext();
        var audit = await db.AuditLogEntries.SingleAsync();
        (audit.Action, audit.ActorUserId, audit.TargetUserId).Should().Be((AuditAction.LootboxGranted, (int?)_staff, _alice));
        audit.Details.Should().Contain("\"event\":\"TokensIssued\"").And.Contain("\"quantity\":3");
    }

    [Fact]
    public async Task Issue_WithoutStars_RollsTheTypesBoxGrade_PerToken()
    {
        await SeedAsync();

        var tokens = await IssueAsync(_weapons, _alice, quantity: 2, stars: null);

        tokens.Should().OnlyContain(t => t.BoxStars == 3, "the Weapons type only has ★3 boxes");
    }

    [Fact]
    public async Task Issue_WithAKey_ReplaysTheSameTokens_AndRefusesTheKeyForAnotherIssue()
    {
        await SeedAsync();
        await using var db = NewContext();
        var request = new LootboxTokenIssueRequestDto { UserId = _alice, TypeId = _weapons, BoxStars = 3, Quantity = 2, IdempotencyKey = "perk:1" };

        var first = await Runtime(db).IssueTokensAsync(request, _staff);
        var again = await Runtime(db).IssueTokensAsync(request, _staff);

        (first.Replay, again.Replay).Should().Be((false, true));
        again.Tokens.Select(t => t.Token).Should().Equal(first.Tokens.Select(t => t.Token));
        (await db.LootboxTokens.CountAsync()).Should().Be(2);
        (await Runtime(db).Invoking(s => s.IssueTokensAsync(new LootboxTokenIssueRequestDto { UserId = _bob, TypeId = _weapons, Quantity = 2, IdempotencyKey = "perk:1" }, _staff))
            .Should().ThrowAsync<LootboxConflictException>()).Which.Code.Should().Be("IdempotencyKeyReused");
    }

    [Fact]
    public async Task Issue_RejectsBadInput_AndATypeWithNothingToGive()
    {
        await SeedAsync();
        await using var db = NewContext();
        var runtime = Runtime(db);

        foreach (var bad in new[]
        {
            new LootboxTokenIssueRequestDto { UserId = _alice, TypeId = _weapons, Quantity = 0 },
            new LootboxTokenIssueRequestDto { UserId = _alice, TypeId = _weapons, Quantity = 65 },
            new LootboxTokenIssueRequestDto { UserId = _alice, TypeId = _weapons, BoxStars = 6 },
            new LootboxTokenIssueRequestDto { UserId = _alice, TypeId = _weapons, Reason = "Lottery" },
            new LootboxTokenIssueRequestDto { UserId = 0, TypeId = _weapons },
        })
        {
            await runtime.Invoking(s => s.IssueTokensAsync(bad, _staff)).Should().ThrowAsync<ArgumentException>();
        }
        await runtime.Invoking(s => s.IssueTokensAsync(new LootboxTokenIssueRequestDto { UserId = 999, TypeId = _weapons }, _staff))
            .Should().ThrowAsync<KeyNotFoundException>();
        (await runtime.Invoking(s => s.IssueTokensAsync(new LootboxTokenIssueRequestDto { UserId = _alice, TypeId = _empty }, _staff))
            .Should().ThrowAsync<LootboxConflictException>()).Which.Code.Should().Be("EmptyPool");
        (await db.LootboxTokens.AnyAsync()).Should().BeFalse();
    }

    // ===== Redeem =====

    [Fact]
    public async Task Redeem_ConsumesTheToken_AndWritesAClaimWithAnInstance_EvenForADisabledType()
    {
        await SeedAsync();
        var token = (await IssueAsync(_weapons, _alice, stars: 3)).Single();

        var result = await RedeemAsync(token.Token, _alice);

        result.Replay.Should().BeFalse();
        (result.LootboxTokenId, result.LootboxSpawnId, result.ItemBlueprintId, result.BoxStars, result.BoxLabel)
            .Should().Be(((int?)token.Id, (int?)null, _sword, 3, "Rare Weapons Lootbox"));
        result.ItemInstanceId.Should().NotBeNull();
        result.Enchantments.Should().ContainSingle(e => e.Key == "minecraft:sharpness" && e.Level == 3);

        await using var db = NewContext();
        var row = await db.LootboxTokens.SingleAsync();
        (row.Status, row.RedeemedByUserId, row.RedeemedAt).Should().Be((LootboxTokenStatus.Redeemed, (int?)_alice, (DateTime?)Now));
        var claim = await db.LootboxClaims.SingleAsync();
        (claim.LootboxTokenId, claim.LootboxSpawnId, claim.UserId).Should().Be(((int?)token.Id, (int?)null, _alice));
        var instance = await db.ItemInstances.SingleAsync();
        (instance.OwnerUserId, instance.Origin, instance.OriginRef).Should().Be(((int?)_alice, ItemInstanceOrigin.Lootbox, claim.Id.ToString()));
    }

    [Fact]
    public async Task Redeem_ADuplicatedItem_OpensOnce_AndEveryOtherCopyIsRefused()
    {
        await SeedAsync();
        var token = (await IssueAsync(_weapons, _alice)).Single();

        await RedeemAsync(token.Token, _alice);
        var rolls = _random.Calls;

        // A second copy of the same item (a creative or dupe glitch): a new click, so a new key.
        (await RedeemFailsAsync(token.Token, _alice)).Should().Be("AlreadyRedeemed");
        (await RedeemFailsAsync(token.Token, _bob)).Should().Be("AlreadyRedeemed");

        _random.Calls.Should().Be(rolls, "a refused copy never rolls");
        await using var db = NewContext();
        (await db.LootboxClaims.CountAsync()).Should().Be(1);
        (await db.ItemInstances.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Redeem_RetryOfTheSameClick_ReplaysTheStoredResult()
    {
        await SeedAsync();
        var token = (await IssueAsync(_weapons, _alice)).Single();

        var first = await RedeemAsync(token.Token, _alice, "open-1");
        var rolls = _random.Calls;
        var retry = await RedeemAsync(token.Token, _alice, "open-1");

        retry.Replay.Should().BeTrue();
        (retry.ClaimId, retry.ItemInstanceId).Should().Be((first.ClaimId, first.ItemInstanceId));
        _random.Calls.Should().Be(rolls);

        // The key belongs to that user and token only.
        (await RedeemFailsAsync(token.Token, _bob, "open-1")).Should().Be("IdempotencyKeyReused");
        var other = (await IssueAsync(_weapons, _alice)).Single();
        (await RedeemFailsAsync(other.Token, _alice, "open-1")).Should().Be("IdempotencyKeyReused");
    }

    [Fact]
    public async Task Redeem_RacingAnotherRedeem_LosesOnTheStatusConcurrencyCheck()
    {
        await SeedAsync();
        var token = (await IssueAsync(_weapons, _alice)).Single();

        // Bob's request has already read the token as Issued when Alice's redeem commits.
        await using var bobDb = NewContext();
        (await bobDb.LootboxTokens.SingleAsync()).Status.Should().Be(LootboxTokenStatus.Issued);
        await RedeemAsync(token.Token, _alice);

        (await Runtime(bobDb).Invoking(s => s.RedeemTokenAsync(token.Token, Redeem(_bob)))
            .Should().ThrowAsync<LootboxConflictException>()).Which.Code.Should().Be("AlreadyRedeemed");

        await using var db = NewContext();
        (await db.LootboxClaims.SingleAsync()).UserId.Should().Be(_alice);
    }

    [Fact]
    public async Task Redeem_ByAnotherPlayer_Works_TokensAreTradeable()
    {
        await SeedAsync();
        var token = (await IssueAsync(_food, _alice)).Single();

        var result = await RedeemAsync(token.Token, _bob);

        (result.UserId, result.ItemInstanceId, result.Quantity).Should().Be((_bob, (long?)null, 8));
        await using var db = NewContext();
        var row = await db.LootboxTokens.SingleAsync();
        (row.IssuedToUserId, row.RedeemedByUserId).Should().Be(((int?)_alice, (int?)_bob));
    }

    [Fact]
    public async Task Redeem_UnknownRevokedAndSwitchedOff_AreRefused_AndTheTokenStays()
    {
        await SeedAsync();
        (await RedeemFailsAsync(Guid.NewGuid(), _alice)).Should().Be("InvalidToken", "a forged id was never issued");

        var revoked = (await IssueAsync(_weapons, _alice)).Single();
        await using (var db = NewContext())
        {
            (await Runtime(db).RevokeTokenAsync(revoked.Token, _staff)).Status.Should().Be("Revoked");
        }
        (await RedeemFailsAsync(revoked.Token, _alice)).Should().Be("Revoked");

        var kept = (await IssueAsync(_weapons, _alice)).Single();
        await UpdateAsync(db => db.LootboxConfigurations.Single().Enabled = false);
        (await RedeemFailsAsync(kept.Token, _alice)).Should().Be("Disabled");
        await UpdateAsync(db =>
        {
            db.LootboxConfigurations.Single().Enabled = true;
            db.Users.Single(u => u.Id == _alice).IsFrozen = true;
        });
        (await RedeemFailsAsync(kept.Token, _alice)).Should().Be("Frozen");

        await using var read = NewContext();
        (await read.LootboxTokens.SingleAsync(t => t.Id == kept.Id)).Status.Should().Be(LootboxTokenStatus.Issued);
        (await read.LootboxClaims.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Revoke_AnOpenedToken_IsRefused_AndIsAudited()
    {
        await SeedAsync();
        var opened = (await IssueAsync(_weapons, _alice)).Single();
        await RedeemAsync(opened.Token, _alice);
        var other = (await IssueAsync(_weapons, _alice)).Single();
        await using var db = NewContext();

        (await Runtime(db).Invoking(s => s.RevokeTokenAsync(opened.Token, _staff)).Should().ThrowAsync<LootboxConflictException>())
            .Which.Code.Should().Be("AlreadyRedeemed");
        var revoked = await Runtime(db).RevokeTokenAsync(other.Token, _staff);
        (revoked.Status, revoked.RevokedAt).Should().Be(("Revoked", (DateTime?)Now));
        (await Runtime(db).RevokeTokenAsync(other.Token, _staff)).Status.Should().Be("Revoked", "revoking twice is a no-op");
        (await db.AuditLogEntries.CountAsync(a => a.Details!.Contains("TokenRevoked"))).Should().Be(1);
    }

    // ===== Daily cap =====

    [Fact]
    public async Task DailyCap_CountsTokenRedeems_AndRefusesATokenAtTheCap_KeepingIt()
    {
        await SeedAsync();
        await UpdateAsync(db => db.LootboxConfigurations.Single().MaxClaimsPerPlayerPerDay = 2);
        var tokens = await IssueAsync(_weapons, _alice, quantity: 3);

        await RedeemAsync(tokens[0].Token, _alice);
        await RedeemAsync(tokens[1].Token, _alice);
        await using (var db = NewContext())
        {
            var refused = (await Runtime(db).Invoking(s => s.RedeemTokenAsync(tokens[2].Token, Redeem(_alice)))
                .Should().ThrowAsync<LootboxDailyLimitException>()).Which;
            (refused.Scope, refused.Limit).Should().Be(("Global", 2));
        }

        // A world box counts against the same cap.
        await using (var db = NewContext())
        {
            var spawn = new LootboxSpawn { LootboxTypeId = _food, BoxGradeId = _grade3, World = "world", SpawnedAt = Now, ExpiresAt = Now.AddMinutes(30) };
            db.LootboxSpawns.Add(spawn);
            await db.SaveChangesAsync();
            await Runtime(db).Invoking(s => s.ClaimAsync(spawn.Id, new LootboxClaimRequestDto { Token = spawn.Token, UserId = _alice, IdempotencyKey = "w" }))
                .Should().ThrowAsync<LootboxDailyLimitException>();
        }

        _clock.Set(new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero));
        (await RedeemAsync(tokens[2].Token, _alice)).Replay.Should().BeFalse("the kept token opens the next UTC day");
    }

    [Fact]
    public async Task DailyCap_PerTypeLimit_AppliesToTokensOfThatType()
    {
        await SeedAsync();
        await UpdateAsync(db => db.LootboxTypes.Single(t => t.Id == _weapons).MaxClaimsPerPlayerPerDay = 1);
        var weapons = await IssueAsync(_weapons, _alice, quantity: 2);
        var food = (await IssueAsync(_food, _alice)).Single();

        await RedeemAsync(weapons[0].Token, _alice);
        await using (var db = NewContext())
        {
            (await Runtime(db).Invoking(s => s.RedeemTokenAsync(weapons[1].Token, Redeem(_alice)))
                .Should().ThrowAsync<LootboxDailyLimitException>()).Which.Scope.Should().Be("Type");
        }
        (await RedeemAsync(food.Token, _alice)).LootboxTypeId.Should().Be(_food);
    }

    // ===== Delivery and reads =====

    [Fact]
    public async Task Undelivered_ListsTheUsersIssuedTokens_UntilTheyAreMarkedDelivered()
    {
        await SeedAsync();
        var mine = await IssueAsync(_weapons, _alice, quantity: 2);
        var bobs = (await IssueAsync(_weapons, _bob)).Single();
        await using var db = NewContext();
        var runtime = Runtime(db);

        (await runtime.GetUndeliveredTokensAsync(_alice)).Select(t => t.Token).Should().Equal(mine.Select(t => t.Token));

        var marked = await runtime.MarkTokensDeliveredAsync(new LootboxTokensDeliveredRequestDto
        {
            UserId = _alice,
            Tokens = new List<Guid> { mine[0].Token, bobs.Token },
        });
        marked.Updated.Should().Be(1, "bob's token isn't alice's to confirm");
        (await runtime.GetUndeliveredTokensAsync(_alice)).Select(t => t.Token).Should().Equal(mine[1].Token);
        (await runtime.MarkTokensDeliveredAsync(new LootboxTokensDeliveredRequestDto { UserId = _alice, Tokens = new() { mine[0].Token } }))
            .Updated.Should().Be(0, "confirming twice changes nothing");
        (await runtime.GetUndeliveredTokensAsync(_bob)).Should().ContainSingle();

        await RedeemAsync(mine[1].Token, _alice);
        (await Runtime(NewContext()).GetUndeliveredTokensAsync(_alice)).Should().BeEmpty("an opened token needs no delivery");
    }

    [Fact]
    public async Task DropLog_ShowsTheSource_AndFiltersByIt()
    {
        await SeedAsync();
        var token = (await IssueAsync(_weapons, _alice)).Single();
        await RedeemAsync(token.Token, _alice);
        await using var db = NewContext();
        await Runtime(db).AdminGiveAsync(new LootboxAdminGiveRequestDto { UserId = _alice, TypeId = _weapons }, _staff);

        var all = await Runtime(db).SearchClaimsAsync(new PagedQueryDto());
        all.Items.Select(i => (i.Source, i.IsAdminGive, i.LootboxTokenId)).Should().BeEquivalentTo(new[]
        {
            ("Token", false, (int?)token.Id),
            ("AdminGive", true, (int?)null),
        });
        (await Runtime(db).SearchClaimsAsync(new PagedQueryDto { Filters = new() { ["source"] = "token" } })).Items.Should().ContainSingle(i => i.Source == "Token");
        (await Runtime(db).SearchClaimsAsync(new PagedQueryDto { Filters = new() { ["adminGive"] = "true" } })).Items.Should().ContainSingle(i => i.Source == "AdminGive");
    }

    [Fact]
    public async Task SearchTokens_FiltersByStatusAndUser_AndLinksTheClaim()
    {
        await SeedAsync();
        var opened = (await IssueAsync(_weapons, _alice)).Single();
        var claim = await RedeemAsync(opened.Token, _bob);
        await IssueAsync(_food, _alice, quantity: 2, reason: "PvpKill");
        await using var db = NewContext();
        var runtime = Runtime(db);

        var redeemed = await runtime.SearchTokensAsync(new PagedQueryDto { Filters = new() { ["status"] = "Redeemed" } });
        redeemed.Items.Should().ContainSingle();
        (redeemed.Items[0].ClaimId, redeemed.Items[0].RedeemedByUsername, redeemed.Items[0].IssuedToUsername)
            .Should().Be(((int?)claim.ClaimId, "bob", "alice"));

        (await runtime.SearchTokensAsync(new PagedQueryDto { Filters = new() { ["reason"] = "PvpKill" } })).TotalCount.Should().Be(2);
        (await runtime.SearchTokensAsync(new PagedQueryDto { Filters = new() { ["userId"] = _bob.ToString() } })).TotalCount.Should().Be(1);
        (await runtime.SearchTokensAsync(new PagedQueryDto { SearchTerm = opened.Token.ToString() })).Items.Single().Id.Should().Be(opened.Id);
    }

    // ===== Grant rules and hooks =====

    [Fact]
    public async Task GrantRules_Validate_ExactlyOneTarget_PremiumTiersOnly()
    {
        await SeedAsync();
        await using var db = NewContext();
        var grants = Grants(db);

        foreach (var bad in new[]
        {
            new LootboxTokenGrantDto { LootboxTypeId = _weapons, Quantity = 1 },
            new LootboxTokenGrantDto { LootboxTypeId = _weapons, Quantity = 1, PermissionGroupId = _noble, KitId = _kit },
            new LootboxTokenGrantDto { LootboxTypeId = _weapons, Quantity = 1, PermissionGroupId = _defaultGroup },
            new LootboxTokenGrantDto { LootboxTypeId = _weapons, Quantity = 0, KitId = _kit },
            new LootboxTokenGrantDto { LootboxTypeId = _weapons, Quantity = 1, KitId = _kit, BoxStars = 6 },
            new LootboxTokenGrantDto { LootboxTypeId = 999, Quantity = 1, KitId = _kit },
        })
        {
            await grants.Invoking(g => g.CreateAsync(bad)).Should().ThrowAsync<ArgumentException>();
        }

        var created = await grants.CreateAsync(new LootboxTokenGrantDto { LootboxTypeId = _weapons, BoxStars = 5, Quantity = 2, PermissionGroupId = _noble });
        (created.PermissionGroupName, created.LootboxTypeName, created.Quantity).Should().Be(("Noble", "Weapons Lootbox", 2));
        (await grants.UpdateAsync(created.Id, new LootboxTokenGrantDto { LootboxTypeId = _weapons, Quantity = 1, KitId = _kit, Enabled = false }))
            .KitName.Should().Be("Starter");
        await grants.DeleteAsync(created.Id);
        (await grants.GetAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task JoiningAPremiumTier_IssuesItsTokens_AndTellsThePlugin_ButExtendingDoesNot()
    {
        await SeedAsync();
        await UpdateAsync(db => db.LootboxTokenGrants.Add(new LootboxTokenGrant { LootboxTypeId = _weapons, BoxStars = 5, Quantity = 2, PermissionGroupId = _noble }));

        async Task AssignAsync(DateTime? expiresAt)
        {
            await using var db = NewContext();
            var users = new UserRepository(db);
            var service = new UserPermissionGroupService(
                new UserPermissionGroupRepository(db), users, new PermissionGroupRepository(db),
                new AuditLogService(new AuditLogRepository(db), users), _notifications, Grants(db));
            await service.UpsertAsync(new UpsertUserPermissionGroupDto { UserId = _alice, PermissionGroupId = _noble, ExpiresAt = expiresAt }, _staff);
        }

        await AssignAsync(DateTime.UtcNow.AddDays(30));
        await using (var db = NewContext())
        {
            var tokens = await db.LootboxTokens.ToListAsync();
            tokens.Should().HaveCount(2).And.OnlyContain(t => t.IssuedReason == LootboxTokenReason.PremiumTier && t.IssuedToUserId == _alice && t.IssuedByUserId == _staff);
        }
        _notifications.GetPending().Should().Contain(n => n.UserId == _alice && n.Type == PlayerNotificationTypes.LootboxTokensIssued);

        _clock.Advance(TimeSpan.FromMinutes(1));
        await AssignAsync(DateTime.UtcNow.AddDays(60));
        await using (var db = NewContext())
        {
            (await db.LootboxTokens.CountAsync()).Should().Be(2, "extending an active membership issues nothing");
        }
    }

    [Fact]
    public async Task KitHook_IssuesOncePerKitClaim()
    {
        await SeedAsync();
        await UpdateAsync(db => db.LootboxTokenGrants.Add(new LootboxTokenGrant { LootboxTypeId = _food, Quantity = 1, KitId = _kit }));
        await using var db = NewContext();
        var grants = Grants(db);

        (await grants.IssueForKitAsync(_alice, _kit, kitClaimId: 41, actorUserId: null)).Should().Be(1);
        (await grants.IssueForKitAsync(_alice, _kit, kitClaimId: 41, actorUserId: null)).Should().Be(0, "the same kit claim replays");
        (await grants.IssueForKitAsync(_alice, _kit, kitClaimId: 42, actorUserId: null)).Should().Be(1);
        (await grants.IssueForKitAsync(_alice, _kit + 100, kitClaimId: 43, actorUserId: null)).Should().Be(0, "a kit without rules issues nothing");

        var tokens = await db.LootboxTokens.ToListAsync();
        tokens.Should().HaveCount(2).And.OnlyContain(t => t.IssuedReason == LootboxTokenReason.Kit && t.IssuedByUserId == null);
    }

    [Fact]
    public async Task Hooks_NeverThrow_WhenARuleCantIssue()
    {
        await SeedAsync();
        await UpdateAsync(db => db.LootboxTokenGrants.Add(new LootboxTokenGrant { LootboxTypeId = _empty, Quantity = 1, KitId = _kit }));
        await using var db = NewContext();

        (await Grants(db).IssueForKitAsync(_alice, _kit, kitClaimId: 7, actorUserId: null)).Should().Be(0);
        (await db.LootboxTokens.AnyAsync()).Should().BeFalse();
    }

    // ===== Test doubles =====

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
