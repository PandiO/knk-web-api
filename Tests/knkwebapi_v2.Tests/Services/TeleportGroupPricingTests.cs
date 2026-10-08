using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Teleport fees and cooldowns per permission group (Linear KNG-41, teleport
/// IMPLEMENTATION_PLAN.md "KNG-41"): which group's settings win (highest Weight first, each group
/// followed by its parents), a warp's multiplier or fixed price, combination prices in one ledger
/// transaction, /tpa and /spawn pricing, XP prices and title progression, the policy read and the
/// PermissionGroup form fields.
/// </summary>
public class TeleportGroupPricingTests
{
    private readonly string _db = $"teleport-groups-{Guid.NewGuid()}";
    private readonly Mock<ITitleProgressionService> _titles = new();
    private readonly Mock<IPlayerNotificationQueue> _notifications = new();

    public TeleportGroupPricingTests()
    {
        _titles.Setup(t => t.ApplyForPostingAsync(It.IsAny<PostingResult>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, TitleChangeResultDto>());
    }

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_db).Options);

    private TeleportDestinationService Service(KnKDbContext ctx)
    {
        var users = new UserRepository(ctx);
        var groups = new UserPermissionGroupService(new UserPermissionGroupRepository(ctx), users,
            new PermissionGroupRepository(ctx), new Mock<IAuditLogService>().Object);
        return new TeleportDestinationService(new TeleportDestinationRepository(ctx), users, groups,
            new DiscoveryRepository(ctx), new TitleBracketRepository(ctx), new PermissionGroupRepository(ctx),
            new CurrencyService(new CurrencyRepository(ctx), users, NullLogger<CurrencyService>.Instance),
            new CurrencyRepository(ctx), NullLogger<TeleportDestinationService>.Instance,
            _titles.Object, _notifications.Object);
    }

    // ===== Seeding =====

    private async Task<int> UserAsync(int gems = 50, int coins = 1000, int xp = 500)
    {
        await using var ctx = NewContext();
        var user = new User { Username = "p" + Guid.NewGuid().ToString("N")[..8], Uuid = Guid.NewGuid().ToString(), Gems = gems, Coins = coins, ExperiencePoints = xp };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    private async Task<int> GroupAsync(string name, int weight, Action<PermissionGroup>? configure = null, int? parentId = null)
    {
        await using var ctx = NewContext();
        var group = new PermissionGroup { Name = name, Weight = weight, ParentGroupId = parentId };
        configure?.Invoke(group);
        ctx.PermissionGroups.Add(group);
        await ctx.SaveChangesAsync();
        return group.Id;
    }

    private async Task JoinAsync(int userId, int groupId, DateTime? expiresAt = null)
    {
        await using var ctx = NewContext();
        ctx.UserPermissionGroups.Add(new UserPermissionGroup { UserId = userId, PermissionGroupId = groupId, ExpiresAt = expiresAt });
        await ctx.SaveChangesAsync();
    }

    private async Task<int> TownAsync(int price)
    {
        await using var ctx = NewContext();
        var town = new Town
        {
            Name = "Kardenna", Description = "", WgRegionId = "kardenna", AllowEntry = true,
            Location = new Location { World = "world", X = 10.5, Y = 64, Z = -20.5 },
            TeleportEnabled = true, TeleportPriceGems = price
        };
        ctx.Towns.Add(town);
        await ctx.SaveChangesAsync();
        return town.Id;
    }

    private async Task<User> ReloadAsync(int id)
    {
        await using var ctx = NewContext();
        return await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == id);
    }

    private async Task<List<CurrencyTransaction>> LedgerAsync()
    {
        await using var ctx = NewContext();
        return await ctx.CurrencyTransactions.AsNoTracking().Include(t => t.Entries).OrderBy(t => t.Id).ToListAsync();
    }

    private async Task<TeleportChargeResultDto> WarpAsync(int domainId, int userId, string key = "warp:1", bool bypassCost = false)
    {
        await using var ctx = NewContext();
        return await Service(ctx).ChargeAsync(domainId, new TeleportChargeRequestDto { UserId = userId, IdempotencyKey = key, BypassCost = bypassCost });
    }

    private async Task<TeleportChargeResultDto> RequestFeeAsync(int userId, int defaultCoins, string key = "tpa:1")
    {
        await using var ctx = NewContext();
        return await Service(ctx).ChargeRequestFeeAsync(new TeleportRequestFeeDto { UserId = userId, AmountCoins = defaultCoins, IdempotencyKey = key });
    }

    private async Task<TeleportChargeResultDto> SpawnFeeAsync(int userId, string key = "spawn:1")
    {
        await using var ctx = NewContext();
        return await Service(ctx).ChargeSpawnFeeAsync(new TeleportSpawnFeeDto { UserId = userId, IdempotencyKey = key });
    }

    private async Task<TeleportRefundResultDto> RefundAsync(int userId, string key)
    {
        await using var ctx = NewContext();
        return await Service(ctx).RefundAsync(new TeleportRefundRequestDto { UserId = userId, IdempotencyKey = key });
    }

    private async Task<TeleportPolicyDto> PolicyAsync(int userId)
    {
        await using var ctx = NewContext();
        return await Service(ctx).GetPolicyAsync(userId);
    }

    private async Task<TeleportDestinationDto> OnlyDestinationAsync(int userId)
    {
        await using var ctx = NewContext();
        return Assert.Single(await Service(ctx).ListForUserAsync(userId));
    }

    private static Action<PermissionGroup> WarpMultiplier(decimal m) => g =>
    {
        g.TeleportWarpPriceMode = TeleportPriceMode.Multiplier;
        g.TeleportWarpPriceMultiplier = m;
    };

    private static Action<PermissionGroup> WarpFixed(int? coins = null, int? gems = null, int? xp = null) => g =>
    {
        g.TeleportWarpPriceMode = TeleportPriceMode.Fixed;
        g.TeleportWarpPriceCoins = coins;
        g.TeleportWarpPriceGems = gems;
        g.TeleportWarpPriceExperience = xp;
    };

    // ===== Which group wins =====

    [Fact]
    public void Chain_HighestWeightFirst_EachGroupFollowedByItsParents()
    {
        var root = new PermissionGroup { Id = 1, Name = "Default", Weight = 0 };
        var noble = new PermissionGroup { Id = 2, Name = "Noble", Weight = 10, ParentGroup = root };
        var royal = new PermissionGroup { Id = 3, Name = "Royal", Weight = 20, ParentGroup = noble };
        var builder = new PermissionGroup { Id = 4, Name = "Builder", Weight = 15 };

        var chain = TeleportGroupPolicy.Chain(new[] { builder, root, royal });

        // Royal's parents (Noble, Default) come before the lower-weight Builder; Default once only.
        Assert.Equal(new[] { "Royal", "Noble", "Default", "Builder" }, chain.Select(g => g.Name));
    }

    [Fact]
    public void Resolve_FirstGroupThatSetsAValue_PriceAndCooldownSeparately()
    {
        var root = new PermissionGroup { Id = 1, Name = "Default", Weight = 0, TeleportWarpCooldownSeconds = 60 };
        var noble = new PermissionGroup
        {
            Id = 2, Name = "Noble", Weight = 10, ParentGroup = root,
            TeleportWarpPriceMode = TeleportPriceMode.Multiplier, TeleportWarpPriceMultiplier = 0.5m
        };
        var royal = new PermissionGroup { Id = 3, Name = "Royal", Weight = 20, ParentGroup = noble, TeleportWarpCooldownSeconds = 5 };
        var builder = new PermissionGroup
        {
            Id = 4, Name = "Builder", Weight = 15,
            TeleportWarpPriceMode = TeleportPriceMode.Fixed, TeleportWarpPriceGems = 1
        };

        var policy = TeleportGroupPolicy.Resolve(TeleportGroupPolicy.Chain(new[] { royal, builder }), TeleportFeeKind.Warp);

        // Royal sets only the cooldown; the price comes from its parent Noble, not from Builder.
        Assert.Equal("Noble", policy.PriceGroup!.Name);
        Assert.Equal(TeleportPriceMode.Multiplier, policy.Price!.Mode);
        Assert.Equal("Royal", policy.CooldownGroup!.Name);
        Assert.Equal(5, policy.CooldownSeconds);
        // Other kinds are untouched.
        var spawn = TeleportGroupPolicy.Resolve(TeleportGroupPolicy.Chain(new[] { royal, builder }), TeleportFeeKind.Spawn);
        Assert.Null(spawn.Price);
        Assert.Null(spawn.CooldownSeconds);
    }

    [Theory]
    [InlineData(10, 0.5, 5)]
    [InlineData(10, 0.25, 3)] // 2.5 rounds half away from zero
    [InlineData(10, 0, 0)]
    [InlineData(10, 2, 20)]
    [InlineData(999_999, 1000, 999_999)] // capped at the gem balance cap
    public void Multiplier_ScalesTheDefaultPrice(int defaultGems, double multiplier, int expected)
    {
        var policy = new TeleportKindPolicy(TeleportFeeKind.Warp, null,
            new TeleportKindSettings(TeleportPriceMode.Multiplier, (decimal)multiplier, null, null, null, null), null, null);

        var price = policy.PriceFor(new[] { new TeleportPriceLeg(Currency.Gems, defaultGems) });

        Assert.Equal(expected, price.Where(l => l.Currency == Currency.Gems).Sum(l => l.Amount));
    }

    [Fact]
    public async Task ExpiredMembership_DoesNotPrice()
    {
        var user = await UserAsync(gems: 50);
        var town = await TownAsync(price: 10);
        await JoinAsync(user, await GroupAsync("Royal", 20, WarpMultiplier(0m)), expiresAt: DateTime.UtcNow.AddDays(-1));

        var charge = await WarpAsync(town, user);

        Assert.Equal(10, charge.Charged);
    }

    // ===== /warp =====

    [Fact]
    public async Task Warp_GroupMultiplier_ChargesAndListsTheScaledGemPrice()
    {
        var user = await UserAsync(gems: 50);
        var town = await TownAsync(price: 10);
        await JoinAsync(user, await GroupAsync("Noble", 10, WarpMultiplier(0.5m)));

        var listed = await OnlyDestinationAsync(user);
        var charge = await WarpAsync(town, user);

        Assert.Equal(5, listed.PriceGems);
        Assert.Equal(0, listed.PriceCoins);
        Assert.Equal("Gems", charge.Currency);
        Assert.Equal(5, charge.Charged);
        Assert.Equal(45, (await ReloadAsync(user)).Gems);
        var tx = Assert.Single(await LedgerAsync());
        Assert.Contains("\"priceGroup\":\"Noble\"", tx.MetadataJson);
    }

    [Fact]
    public async Task Warp_GroupMultiplierZero_IsFree_WithoutALedgerPosting()
    {
        var user = await UserAsync(gems: 0);
        var town = await TownAsync(price: 10);
        await JoinAsync(user, await GroupAsync("Royal", 20, WarpMultiplier(0m)));

        Assert.True((await OnlyDestinationAsync(user)).Available);
        var charge = await WarpAsync(town, user);

        Assert.Equal(0, charge.Charged);
        Assert.Empty(charge.Payments);
        Assert.Empty(await LedgerAsync());
    }

    [Fact]
    public async Task Warp_FixedComboPrice_OneTransaction_AllCurrencies_RefundedTogether()
    {
        var user = await UserAsync(gems: 50, coins: 1000, xp: 500);
        var town = await TownAsync(price: 10);
        await JoinAsync(user, await GroupAsync("Builder", 15, WarpFixed(coins: 100, gems: 1, xp: 50)));

        var listed = await OnlyDestinationAsync(user);
        var charge = await WarpAsync(town, user, "warp:combo");

        Assert.Equal((100, 1, 50), (listed.PriceCoins, listed.PriceGems, listed.PriceExperience));
        Assert.Equal(new[] { "Coins", "Gems", "Experience" }, charge.Payments.Select(p => p.Currency));
        Assert.Equal(new long[] { 100, 1, 50 }, charge.Payments.Select(p => p.Amount));
        Assert.Equal(new long[] { 900, 49, 450 }, charge.Payments.Select(p => p.NewBalance));
        Assert.Equal(("Coins", 100L, 900L), (charge.Currency, charge.Charged, charge.NewBalance));
        var tx = Assert.Single(await LedgerAsync());
        Assert.Equal(CurrencyReasons.TeleportFee, tx.ReasonCode);
        Assert.Equal(3, tx.Entries.Count(e => e.AccountKind == CurrencyAccountKind.User));
        _titles.Verify(t => t.ApplyForPostingAsync(It.Is<PostingResult>(p => p.Entries.Any(e => e.Currency == "Experience")), null, It.IsAny<CancellationToken>()), Times.Once);

        // A replay reports the same three payments.
        var replay = await WarpAsync(town, user, "warp:combo");
        Assert.True(replay.Replayed);
        Assert.Equal(new long[] { 100, 1, 50 }, replay.Payments.Select(p => p.Amount));

        var refund = await RefundAsync(user, "warp:combo");
        Assert.True(refund.Refunded);
        Assert.Equal(new long[] { 100, 1, 50 }, refund.Payments.Select(p => p.Amount));
        var after = await ReloadAsync(user);
        Assert.Equal((1000, 50, 500), (after.Coins, after.Gems, after.ExperiencePoints));
        // Giving XP back runs title progression again (re-promotion; bonuses stay once per bracket).
        _titles.Verify(t => t.ApplyForPostingAsync(It.IsAny<PostingResult>(), null, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Warp_FixedComboPrice_ShortOfOneCurrency_RefusesWithoutAnyMutation()
    {
        var user = await UserAsync(gems: 0, coins: 1000, xp: 500);
        var town = await TownAsync(price: 10);
        await JoinAsync(user, await GroupAsync("Builder", 15, WarpFixed(coins: 100, gems: 1)));

        var listed = await OnlyDestinationAsync(user);
        var refused = await Assert.ThrowsAsync<TeleportDestinationException>(() => WarpAsync(town, user));

        Assert.False(listed.CanAfford);
        Assert.Equal(TeleportDestinationException.InsufficientGems, listed.LockCode);
        Assert.Equal(TeleportDestinationException.InsufficientGems, refused.Code);
        Assert.Equal(1000, (await ReloadAsync(user)).Coins);
        Assert.Empty(await LedgerAsync());
    }

    [Fact]
    public async Task Warp_XpPrice_MayDemote_ShortOfXp_IsRefused()
    {
        var user = await UserAsync(gems: 0, xp: 40);
        var town = await TownAsync(price: 10);
        await JoinAsync(user, await GroupAsync("Squire", 5, WarpFixed(xp: 50)));

        var refused = await Assert.ThrowsAsync<TeleportDestinationException>(() => WarpAsync(town, user));

        Assert.Equal(TeleportDestinationException.InsufficientExperience, refused.Code);
        Assert.Contains("XP", refused.Message);
        Assert.Equal(40, (await ReloadAsync(user)).ExperiencePoints);
    }

    [Fact]
    public async Task Warp_XpPrice_TitleChange_IsQueuedForThePlayer()
    {
        var user = await UserAsync(gems: 0, xp: 120);
        var town = await TownAsync(price: 10);
        await JoinAsync(user, await GroupAsync("Squire", 5, WarpFixed(xp: 50)));
        var change = new TitleChangeResultDto();
        _titles.Setup(t => t.ApplyForPostingAsync(It.IsAny<PostingResult>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, TitleChangeResultDto> { [user] = change });

        await WarpAsync(town, user);

        Assert.Equal(70, (await ReloadAsync(user)).ExperiencePoints);
        _notifications.Verify(n => n.Enqueue(user, It.IsAny<string?>(), It.IsAny<string>(), PlayerNotificationTypes.TitleChanged, change), Times.Once);
    }

    [Fact]
    public async Task Warp_BypassCost_IgnoresTheGroupPriceToo()
    {
        var user = await UserAsync(coins: 0);
        var town = await TownAsync(price: 10);
        await JoinAsync(user, await GroupAsync("Builder", 15, WarpFixed(coins: 100)));

        var charge = await WarpAsync(town, user, bypassCost: true);

        Assert.Equal(0, charge.Charged);
        Assert.Empty(await LedgerAsync());
    }

    // ===== /tpa =====

    [Fact]
    public async Task RequestFee_NoGroupPrice_ChargesTheDefault()
    {
        var user = await UserAsync(coins: 1000);
        await JoinAsync(user, await GroupAsync("Default", 0, g => g.TeleportRequestCooldownSeconds = 5));

        var fee = await RequestFeeAsync(user, 250);

        Assert.Equal(250, fee.Charged);
    }

    [Fact]
    public async Task RequestFee_GroupMultiplier_ScalesTheDefault_GroupFixed_ReplacesIt()
    {
        var noble = await UserAsync(coins: 1000);
        await JoinAsync(noble, await GroupAsync("Noble", 10, g =>
        {
            g.TeleportRequestPriceMode = TeleportPriceMode.Multiplier;
            g.TeleportRequestPriceMultiplier = 0.5m;
        }));
        var royal = await UserAsync(coins: 1000, gems: 5);
        await JoinAsync(royal, await GroupAsync("Royal", 20, g =>
        {
            g.TeleportRequestPriceMode = TeleportPriceMode.Fixed;
            g.TeleportRequestPriceGems = 2;
        }));

        var half = await RequestFeeAsync(noble, 250, "tpa:noble");
        var gems = await RequestFeeAsync(royal, 250, "tpa:royal");

        Assert.Equal(("Coins", 125L), (half.Currency, half.Charged));
        Assert.Equal(("Gems", 2L), (gems.Currency, gems.Charged));
        Assert.Equal(1000, (await ReloadAsync(royal)).Coins);
    }

    [Fact]
    public async Task RequestFee_GroupPrice_AppliesEvenWhenTheDefaultIsFree()
    {
        var user = await UserAsync(coins: 1000);
        await JoinAsync(user, await GroupAsync("Default", 0, g =>
        {
            g.TeleportRequestPriceMode = TeleportPriceMode.Fixed;
            g.TeleportRequestPriceCoins = 10;
        }));

        var fee = await RequestFeeAsync(user, 0);

        Assert.Equal(10, fee.Charged);
    }

    // ===== /spawn =====

    [Fact]
    public async Task SpawnFee_FreeWithoutAGroupPrice()
    {
        var user = await UserAsync();

        var fee = await SpawnFeeAsync(user);

        Assert.Equal(0, fee.Charged);
        Assert.Empty(fee.Payments);
        Assert.Empty(await LedgerAsync());
    }

    [Fact]
    public async Task SpawnFee_GroupFixedPrice_ChargedOnce_AndRefunded()
    {
        var user = await UserAsync(coins: 100);
        await JoinAsync(user, await GroupAsync("Default", 0, g =>
        {
            g.TeleportSpawnPriceMode = TeleportPriceMode.Fixed;
            g.TeleportSpawnPriceCoins = 30;
        }));

        var fee = await SpawnFeeAsync(user, "spawn:paid");
        var replay = await SpawnFeeAsync(user, "spawn:paid");

        Assert.Equal(30, fee.Charged);
        Assert.True(replay.Replayed);
        var tx = Assert.Single(await LedgerAsync());
        Assert.Equal(TeleportDestinationService.SpawnSourceType, tx.SourceType);
        Assert.Equal(70, (await ReloadAsync(user)).Coins);

        await RefundAsync(user, "spawn:paid");
        Assert.Equal(100, (await ReloadAsync(user)).Coins);
    }

    [Fact]
    public async Task SpawnFee_Short_IsRefusedWithASpawnMessage()
    {
        var user = await UserAsync(coins: 10);
        await JoinAsync(user, await GroupAsync("Default", 0, g =>
        {
            g.TeleportSpawnPriceMode = TeleportPriceMode.Fixed;
            g.TeleportSpawnPriceCoins = 30;
        }));

        var refused = await Assert.ThrowsAsync<TeleportDestinationException>(() => SpawnFeeAsync(user));

        Assert.Equal(TeleportDestinationException.InsufficientCoins, refused.Code);
        Assert.Equal("You don't have enough coins to teleport to spawn!", refused.Message);
    }

    // ===== Policy read =====

    [Fact]
    public async Task Policy_ReportsEachKind_WithTheGroupItCameFrom()
    {
        var user = await UserAsync();
        var parent = await GroupAsync("Default", 0, g =>
        {
            g.TeleportSpawnPriceMode = TeleportPriceMode.Fixed;
            g.TeleportSpawnPriceCoins = 30;
            g.TeleportWarpCooldownSeconds = 60;
        });
        await JoinAsync(user, await GroupAsync("Noble", 10, g =>
        {
            WarpMultiplier(0.5m)(g);
            g.TeleportRequestCooldownSeconds = 3;
        }, parentId: parent));

        var policy = await PolicyAsync(user);

        Assert.Equal(TeleportPriceMode.None, policy.Request.PriceMode);
        Assert.Equal((3, "Noble"), (policy.Request.CooldownSeconds, policy.Request.CooldownGroupName));
        Assert.Equal((TeleportPriceMode.Multiplier, 0.5m, "Noble"), (policy.Warp.PriceMode, policy.Warp.PriceMultiplier, policy.Warp.PriceGroupName));
        Assert.Equal((60, "Default"), (policy.Warp.CooldownSeconds, policy.Warp.CooldownGroupName));
        Assert.Equal((TeleportPriceMode.Fixed, 30, 0, 0), (policy.Spawn.PriceMode, policy.Spawn.PriceCoins, policy.Spawn.PriceGems, policy.Spawn.PriceExperience));
        Assert.Null(policy.Spawn.CooldownSeconds);
    }

    [Fact]
    public async Task Policy_UnknownUser_Throws()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => PolicyAsync(424242));
    }

    // ===== PermissionGroup form fields =====

    private PermissionGroupService GroupService(KnKDbContext ctx) =>
        new(new PermissionGroupRepository(ctx), new MapperConfiguration(c => c.AddProfile<PermissionMappingProfile>()).CreateMapper());

    [Fact]
    public async Task GroupUpdate_WithoutAPriceMode_KeepsThatKindsSettings()
    {
        var id = await GroupAsync("Noble", 10, g =>
        {
            WarpMultiplier(0.5m)(g);
            g.TeleportWarpCooldownSeconds = 10;
            g.TeleportSpawnPriceMode = TeleportPriceMode.Fixed;
            g.TeleportSpawnPriceGems = 1;
        });

        await using (var ctx = NewContext())
        {
            // A form without the teleport fields (or with only /spawn's) must not wipe /warp's.
            await GroupService(ctx).UpdateAsync(id, new PermissionGroupDto
            {
                Name = "Noble", Weight = 10, SalaryMultiplier = 1m,
                TeleportSpawnPriceMode = TeleportPriceMode.None
            });
        }

        await using var check = NewContext();
        var group = await check.PermissionGroups.AsNoTracking().SingleAsync(g => g.Id == id);
        Assert.Equal((TeleportPriceMode.Multiplier, 0.5m, 10), (group.TeleportWarpPriceMode, group.TeleportWarpPriceMultiplier, group.TeleportWarpCooldownSeconds));
        Assert.Equal(TeleportPriceMode.None, group.TeleportSpawnPriceMode);
        Assert.Null(group.TeleportSpawnPriceGems);
    }

    [Fact]
    public async Task GroupCreateAndRead_RoundTripsTheTeleportFields()
    {
        int id;
        await using (var ctx = NewContext())
        {
            var created = await GroupService(ctx).CreateAsync(new PermissionGroupDto
            {
                Name = "Royal", Weight = 20, SalaryMultiplier = 1m,
                TeleportRequestPriceMode = TeleportPriceMode.Fixed, TeleportRequestPriceCoins = 5, TeleportRequestCooldownSeconds = 2,
                TeleportWarpPriceMode = TeleportPriceMode.Multiplier, TeleportWarpPriceMultiplier = 0.25m
            });
            id = created.Id!.Value;
        }

        await using var read = NewContext();
        var dto = await GroupService(read).GetByIdAsync(id);
        Assert.Equal((TeleportPriceMode.Fixed, 5, 2), (dto!.TeleportRequestPriceMode, dto.TeleportRequestPriceCoins, dto.TeleportRequestCooldownSeconds));
        Assert.Equal((TeleportPriceMode.Multiplier, 0.25m), (dto.TeleportWarpPriceMode, dto.TeleportWarpPriceMultiplier));
        Assert.Equal(TeleportPriceMode.None, dto.TeleportSpawnPriceMode);
    }

    public static IEnumerable<object[]> InvalidSettings() => new[]
    {
        new object[] { new PermissionGroupDto { TeleportSpawnPriceMode = TeleportPriceMode.Multiplier, TeleportSpawnPriceCoins = 1 } },
        new object[] { new PermissionGroupDto { TeleportWarpPriceMode = TeleportPriceMode.Multiplier } },
        new object[] { new PermissionGroupDto { TeleportWarpPriceMode = TeleportPriceMode.Multiplier, TeleportWarpPriceMultiplier = -0.5m } },
        new object[] { new PermissionGroupDto { TeleportWarpPriceMode = TeleportPriceMode.Fixed, TeleportWarpPriceGems = BalanceLimits.MaxGems + 1 } },
        new object[] { new PermissionGroupDto { TeleportRequestPriceMode = TeleportPriceMode.Fixed, TeleportRequestPriceCoins = -1 } },
        new object[] { new PermissionGroupDto { TeleportRequestPriceMode = TeleportPriceMode.Fixed, TeleportRequestPriceExperience = -1 } },
        new object[] { new PermissionGroupDto { TeleportRequestPriceMode = TeleportPriceMode.None, TeleportRequestCooldownSeconds = -1 } },
        new object[] { new PermissionGroupDto { TeleportSpawnPriceMode = TeleportPriceMode.None, TeleportSpawnCooldownSeconds = PermissionGroupTeleportSettings.MaxCooldownSeconds + 1 } },
    };

    [Theory]
    [MemberData(nameof(InvalidSettings))]
    public async Task GroupCreate_RejectsInvalidTeleportSettings(PermissionGroupDto dto)
    {
        dto.Name = "Bad";
        dto.SalaryMultiplier = 1m;
        await using var ctx = NewContext();
        await Assert.ThrowsAsync<ArgumentException>(() => GroupService(ctx).CreateAsync(dto));
    }
}
