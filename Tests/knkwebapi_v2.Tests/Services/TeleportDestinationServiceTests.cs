using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Domain warps (teleport IMPLEMENTATION_PLAN.md Phase 5, DESIGN §3.7): the listing and its lock
/// reasons in DESIGN §3.7.2's order, bypass flags, the gem charge through the ledger (once per
/// idempotency key, TELEPORT_FEE), refunds, voided keys, request fees and form validation.
/// EF InMemory has no row locks or unique indexes; the concurrent same-key charge is proven on
/// MySQL in Tests/MySql/TeleportChargeMySqlTests.cs.
/// </summary>
public class TeleportDestinationServiceTests
{
    private readonly string _db = $"teleport-{Guid.NewGuid()}";

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
            new CurrencyRepository(ctx), NullLogger<TeleportDestinationService>.Instance);
    }

    // ===== Seeding =====

    private async Task<int> UserAsync(int gems = 50, int coins = 100, int xp = 0, Gender? gender = null)
    {
        await using var ctx = NewContext();
        var user = new User { Username = "p" + Guid.NewGuid().ToString("N")[..8], Gems = gems, Coins = coins, ExperiencePoints = xp, Gender = gender };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    private async Task<int> BracketAsync(string male, string female, int minXp)
    {
        await using var ctx = NewContext();
        var bracket = new TitleBracket { MaleName = male, FemaleName = female, MinExperience = minXp };
        ctx.TitleBrackets.Add(bracket);
        await ctx.SaveChangesAsync();
        return bracket.Id;
    }

    private async Task<int> GroupAsync(string name, int weight, bool premium = true)
    {
        await using var ctx = NewContext();
        var group = new PermissionGroup { Name = name, Weight = weight, IsPremiumTier = premium };
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

    private async Task DiscoverAsync(int userId, int domainId)
    {
        await using var ctx = NewContext();
        ctx.UserDomainDiscoveries.Add(new UserDomainDiscovery { UserId = userId, DomainId = domainId });
        await ctx.SaveChangesAsync();
    }

    private async Task<int> TownAsync(string name = "Kardenna", bool enabled = true, int price = 0, bool allowEntry = true,
        bool withLocation = true, int? titleId = null, int? premiumId = null, bool requiresDiscovery = false)
    {
        await using var ctx = NewContext();
        var town = new Town
        {
            Name = name, Description = "", WgRegionId = name.ToLowerInvariant(), AllowEntry = allowEntry,
            Location = withLocation ? new Location { World = "world", X = 10.5, Y = 64, Z = -20.5, Yaw = 90 } : null,
            TeleportEnabled = enabled, TeleportPriceGems = price, TeleportMinTitleBracketId = titleId,
            TeleportMinPremiumGroupId = premiumId, TeleportRequiresDiscovery = requiresDiscovery
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

    private async Task<TeleportDestinationDto> OnlyAsync(int userId)
    {
        await using var ctx = NewContext();
        return Assert.Single(await Service(ctx).ListForUserAsync(userId));
    }

    private async Task<TeleportChargeResultDto> ChargeAsync(int domainId, int userId, string key = "warp:1",
        bool bypassRequirements = false, bool bypassCost = false)
    {
        await using var ctx = NewContext();
        return await Service(ctx).ChargeAsync(domainId, new TeleportChargeRequestDto
        {
            UserId = userId, IdempotencyKey = key, BypassRequirements = bypassRequirements, BypassCost = bypassCost
        });
    }

    private async Task<TeleportDestinationException> RefusedAsync(int domainId, int userId, string key = "warp:1",
        bool bypassRequirements = false, bool bypassCost = false) =>
        await Assert.ThrowsAsync<TeleportDestinationException>(() => ChargeAsync(domainId, userId, key, bypassRequirements, bypassCost));

    // ===== Listing =====

    [Fact]
    public async Task List_ShowsOnlyEnabledTownsWithALocationAndOpenEntry()
    {
        var user = await UserAsync();
        await TownAsync("Open");
        await TownAsync("Disabled", enabled: false);
        await TownAsync("Closed", allowEntry: false);
        await TownAsync("Nowhere", withLocation: false);

        var only = await OnlyAsync(user);

        Assert.Equal("Open", only.Name);
        Assert.Equal("Town", only.DomainType);
        Assert.True(only.Available);
        Assert.Equal("world", only.Location.World);
        Assert.Equal(10.5, only.Location.X);
        Assert.Equal(90, only.Location.Yaw);
    }

    [Fact]
    public async Task List_OrdersTownsBeforeDistrictsThenByName()
    {
        var user = await UserAsync();
        var townId = await TownAsync("Zed");
        await TownAsync("Alpha");
        await using (var ctx = NewContext())
        {
            ctx.Districts.Add(new District
            {
                Name = "Market", Description = "", WgRegionId = "market", TownId = townId,
                Location = new Location { World = "world" }, TeleportEnabled = true
            });
            await ctx.SaveChangesAsync();
        }

        await using var read = NewContext();
        var list = await Service(read).ListForUserAsync(user);

        Assert.Equal(new[] { "Alpha", "Zed", "Market" }, list.Select(d => d.Name));
        Assert.Equal("District", list[2].DomainType);
    }

    [Fact]
    public async Task List_UnknownUser_Throws()
    {
        await using var ctx = NewContext();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Service(ctx).ListForUserAsync(12345));
    }

    [Fact]
    public async Task List_TitleTooLow_UsesTheGenderedTitleName()
    {
        var user = await UserAsync(xp: 99, gender: Gender.Female);
        var knight = await BracketAsync("Knight", "Dame", 100);
        await TownAsync(titleId: knight);

        var dest = await OnlyAsync(user);

        Assert.False(dest.Available);
        Assert.False(dest.RequirementsMet);
        Assert.Equal("TitleTooLow", dest.LockCode);
        Assert.Equal("Reach title Dame to unlock", dest.LockReason);
        Assert.Equal("Dame", dest.MinTitleName);
    }

    [Fact]
    public async Task List_TitleReached_IsAvailable()
    {
        var user = await UserAsync(xp: 100);
        await TownAsync(titleId: await BracketAsync("Knight", "Dame", 100));

        Assert.True((await OnlyAsync(user)).Available);
    }

    [Fact]
    public async Task List_PremiumTier_ComparesWeights_AndIgnoresExpiredTiers()
    {
        var noble = await GroupAsync("Noble", 10);
        var royal = await GroupAsync("Royal", 20);
        await TownAsync(premiumId: royal);

        var none = await UserAsync();
        var lower = await UserAsync();
        await JoinAsync(lower, noble);
        var expired = await UserAsync();
        await JoinAsync(expired, royal, DateTime.UtcNow.AddDays(-1));
        var equal = await UserAsync();
        await JoinAsync(equal, royal);
        var higher = await UserAsync();
        await JoinAsync(higher, await GroupAsync("Dragon Blood", 30));

        foreach (var locked in new[] { none, lower, expired })
        {
            var dest = await OnlyAsync(locked);
            Assert.Equal("PremiumTooLow", dest.LockCode);
            Assert.Equal("Premium tier Royal or higher required", dest.LockReason);
        }
        Assert.True((await OnlyAsync(equal)).Available);
        Assert.True((await OnlyAsync(higher)).Available);
    }

    [Fact]
    public async Task List_Discovery_OnlyWhenTheDomainRequiresIt()
    {
        var user = await UserAsync();
        var gated = await TownAsync("Gated", requiresDiscovery: true);
        await TownAsync("Free");

        await using (var ctx = NewContext())
        {
            var list = await Service(ctx).ListForUserAsync(user);
            Assert.True(list.Single(d => d.Name == "Free").Available);
            var locked = list.Single(d => d.Name == "Gated");
            Assert.Equal("NotDiscovered", locked.LockCode);
            Assert.Equal("Discover Gated first", locked.LockReason);
            Assert.True(locked.RequiresDiscovery);
        }

        await DiscoverAsync(user, gated);
        await using var after = NewContext();
        Assert.All(await Service(after).ListForUserAsync(user), d => Assert.True(d.Available));
    }

    [Fact]
    public async Task List_Price_LocksOnlyWhenEverythingElseIsMet()
    {
        var poor = await UserAsync(gems: 9);
        await TownAsync(price: 10);

        var dest = await OnlyAsync(poor);

        Assert.Equal("InsufficientGems", dest.LockCode);
        Assert.True(dest.RequirementsMet);
        Assert.False(dest.CanAfford);
        Assert.Equal(10, dest.PriceGems);
    }

    [Fact]
    public async Task List_FirstFailureWins_TitleBeforePremiumBeforeDiscoveryBeforePrice()
    {
        var user = await UserAsync(gems: 0, xp: 0);
        var title = await BracketAsync("Knight", "Dame", 100);
        var premium = await GroupAsync("Royal", 20);
        await TownAsync(price: 10, titleId: title, premiumId: premium, requiresDiscovery: true);

        var dest = await OnlyAsync(user);

        Assert.Equal("TitleTooLow", dest.LockCode);
        Assert.False(dest.CanAfford);
    }

    [Fact]
    public async Task List_PremiumBeforeDiscovery()
    {
        var user = await UserAsync();
        await TownAsync(premiumId: await GroupAsync("Royal", 20), requiresDiscovery: true);

        Assert.Equal("PremiumTooLow", (await OnlyAsync(user)).LockCode);
    }

    // ===== Charging =====

    [Fact]
    public async Task Charge_DeductsThePriceOnce_AsATeleportFee()
    {
        var user = await UserAsync(gems: 25);
        var town = await TownAsync(price: 10);

        var result = await ChargeAsync(town, user, "warp:abc");

        Assert.Equal("Gems", result.Currency);
        Assert.Equal(10, result.Charged);
        Assert.Equal(15, result.NewBalance);
        Assert.False(result.Replayed);
        Assert.NotNull(result.TransactionPublicId);
        Assert.Equal(town, result.Destination!.DomainId);
        Assert.True(result.Destination.Available);
        Assert.Equal(15, (await ReloadAsync(user)).Gems);

        var tx = Assert.Single(await LedgerAsync());
        Assert.Equal(CurrencyReasons.TeleportFee, tx.ReasonCode);
        Assert.Equal(CurrencyIdempotencyScopes.Plugin, tx.IdempotencyScope);
        Assert.Equal("warp:abc", tx.IdempotencyKey);
        Assert.Equal(CurrencyInitiator.Player, tx.Initiator);
        Assert.Equal(user, tx.InitiatorUserId);
        Assert.Equal("Domain", tx.SourceType);
        Assert.Equal(town.ToString(), tx.SourceRef);
        var leg = tx.Entries.Single(e => e.AccountKind == CurrencyAccountKind.User);
        Assert.Equal(Currency.Gems, leg.Currency);
        Assert.Equal(-10, leg.Amount);
        Assert.Equal(CurrencyReasons.SysTeleport, tx.Entries.Single(e => e.AccountKind == CurrencyAccountKind.System).SystemAccount);
    }

    [Fact]
    public async Task Charge_RetryWithTheSameKey_ReplaysWithoutChargingAgain_EvenWhenNowShort()
    {
        var user = await UserAsync(gems: 15);
        var town = await TownAsync(price: 10);

        await ChargeAsync(town, user, "warp:retry");
        var retry = await ChargeAsync(town, user, "warp:retry");

        Assert.True(retry.Replayed);
        Assert.Equal(10, retry.Charged);
        Assert.Equal(5, retry.NewBalance);
        Assert.Equal(5, (await ReloadAsync(user)).Gems);
        Assert.Single(await LedgerAsync());
    }

    [Fact]
    public async Task Charge_NewKey_ChargesAgain()
    {
        var user = await UserAsync(gems: 30);
        var town = await TownAsync(price: 10);

        await ChargeAsync(town, user, "warp:1");
        await ChargeAsync(town, user, "warp:2");

        Assert.Equal(10, (await ReloadAsync(user)).Gems);
        Assert.Equal(2, (await LedgerAsync()).Count);
    }

    [Fact]
    public async Task Charge_SameKeyForAnotherDomainOrPlayer_IsKeyReuse()
    {
        var user = await UserAsync(gems: 30);
        var other = await UserAsync(gems: 30);
        var a = await TownAsync("A", price: 10);
        var b = await TownAsync("B", price: 10);
        await ChargeAsync(a, user, "warp:k");

        Assert.Equal("IdempotencyKeyReuse", (await RefusedAsync(b, user, "warp:k")).Code);
        Assert.Equal("IdempotencyKeyReuse", (await RefusedAsync(a, other, "warp:k")).Code);
        Assert.Equal(20, (await ReloadAsync(user)).Gems);
        Assert.Equal(30, (await ReloadAsync(other)).Gems);
    }

    [Fact]
    public async Task Charge_InsufficientGems_RefusesWithoutAnyMutation()
    {
        var user = await UserAsync(gems: 9);
        var town = await TownAsync(price: 10);

        var refused = await RefusedAsync(town, user);

        Assert.Equal("InsufficientGems", refused.Code);
        Assert.Equal("You don't have enough gems to teleport to this location!", refused.Message);
        Assert.Equal(9, (await ReloadAsync(user)).Gems);
        Assert.Empty(await LedgerAsync());
    }

    [Fact]
    public async Task Charge_ReEvaluatesRequirementsServerSide()
    {
        var user = await UserAsync(gems: 50, xp: 5);
        var town = await TownAsync(price: 10, titleId: await BracketAsync("Knight", "Dame", 100));

        var refused = await RefusedAsync(town, user);

        Assert.Equal("TitleTooLow", refused.Code);
        Assert.Equal("Reach title Knight to unlock", refused.Message);
        Assert.Empty(await LedgerAsync());
    }

    [Fact]
    public async Task Charge_DiscoveryRequired_RefusedUntilDiscovered()
    {
        var user = await UserAsync();
        var town = await TownAsync(requiresDiscovery: true);

        Assert.Equal("NotDiscovered", (await RefusedAsync(town, user, "warp:a")).Code);
        await DiscoverAsync(user, town);
        Assert.Equal(0, (await ChargeAsync(town, user, "warp:b")).Charged);
    }

    [Fact]
    public async Task Charge_BypassRequirements_SkipsTheLocksButStillCharges()
    {
        var user = await UserAsync(gems: 20);
        var town = await TownAsync(price: 10, premiumId: await GroupAsync("Royal", 20), requiresDiscovery: true);

        var result = await ChargeAsync(town, user, bypassRequirements: true);

        Assert.Equal(10, result.Charged);
        Assert.Equal(10, (await ReloadAsync(user)).Gems);
    }

    [Fact]
    public async Task Charge_BypassCost_ChargesNothing()
    {
        var user = await UserAsync(gems: 0);
        var town = await TownAsync(price: 10);

        var result = await ChargeAsync(town, user, bypassCost: true);

        Assert.Equal(0, result.Charged);
        Assert.Null(result.TransactionPublicId);
        Assert.Empty(await LedgerAsync());
    }

    [Fact]
    public async Task Charge_FreeWarp_AuthorizesWithoutALedgerPosting()
    {
        var user = await UserAsync(gems: 3);
        var town = await TownAsync(price: 0);

        var result = await ChargeAsync(town, user);

        Assert.Equal(0, result.Charged);
        Assert.Equal(3, result.NewBalance);
        Assert.Equal("Kardenna", result.Destination!.Name);
        Assert.Empty(await LedgerAsync());
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task Charge_NotADestination_IsNotAvailable(bool enabled, bool allowEntry, bool withLocation)
    {
        var user = await UserAsync();
        var town = await TownAsync(enabled: enabled, allowEntry: allowEntry, withLocation: withLocation);

        Assert.Equal("NotAvailable", (await RefusedAsync(town, user)).Code);
    }

    [Fact]
    public async Task Charge_UnknownDomain_IsNotAvailable_UnknownUser_Throws()
    {
        var user = await UserAsync();
        var town = await TownAsync();

        Assert.Equal("NotAvailable", (await RefusedAsync(99999, user)).Code);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => ChargeAsync(town, 99999));
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("semi;colon")]
    public async Task Charge_BadKey_IsRejected(string key)
    {
        var user = await UserAsync();
        var town = await TownAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => ChargeAsync(town, user, key));
    }

    [Fact]
    public async Task Charge_RetryAfterStaffClosedTheDomain_StillReplaysThePaidCharge()
    {
        var user = await UserAsync(gems: 20);
        var town = await TownAsync(price: 10);
        await ChargeAsync(town, user, "warp:paid");
        await using (var ctx = NewContext())
        {
            var t = await ctx.Towns.SingleAsync(x => x.Id == town);
            t.TeleportEnabled = false;
            await ctx.SaveChangesAsync();
        }

        var retry = await ChargeAsync(town, user, "warp:paid");

        Assert.True(retry.Replayed);
        Assert.Equal(10, (await ReloadAsync(user)).Gems);
    }

    // ===== Refunds =====

    private async Task<TeleportRefundResultDto> RefundAsync(int userId, string key)
    {
        await using var ctx = NewContext();
        return await Service(ctx).RefundAsync(new TeleportRefundRequestDto { UserId = userId, IdempotencyKey = key, Reason = "cancelled by a listener" });
    }

    [Fact]
    public async Task Refund_RestoresTheGems_AsAReversal_AndIsSafeToRepeat()
    {
        var user = await UserAsync(gems: 25);
        var town = await TownAsync(price: 10);
        await ChargeAsync(town, user, "warp:r");

        var refund = await RefundAsync(user, "warp:r");
        var again = await RefundAsync(user, "warp:r");

        Assert.True(refund.Refunded);
        Assert.False(refund.Replayed);
        Assert.Equal(10, refund.Amount);
        Assert.Equal("Gems", refund.Currency);
        Assert.Equal(25, refund.NewBalance);
        Assert.True(again.Refunded);
        Assert.True(again.Replayed);
        Assert.Equal(25, (await ReloadAsync(user)).Gems);

        var ledger = await LedgerAsync();
        Assert.Equal(2, ledger.Count);
        Assert.Equal(CurrencyReasons.Reversal, ledger[1].ReasonCode);
        Assert.Equal(ledger[0].Id, ledger[1].ReversesTransactionId);
        Assert.Contains("cancelled by a listener", ledger[1].Reason);
    }

    [Fact]
    public async Task Charge_AfterRefund_WithTheSameKey_IsRefused()
    {
        var user = await UserAsync(gems: 25);
        var town = await TownAsync(price: 10);
        await ChargeAsync(town, user, "warp:x");
        await RefundAsync(user, "warp:x");

        Assert.Equal("Refunded", (await RefusedAsync(town, user, "warp:x")).Code);
        Assert.Equal(25, (await ReloadAsync(user)).Gems);
    }

    [Fact]
    public async Task Refund_BeforeAnyCharge_VoidsTheKey_SoALateChargeIsRefused()
    {
        var user = await UserAsync(gems: 25);
        var town = await TownAsync(price: 10);

        var refund = await RefundAsync(user, "warp:late");

        Assert.False(refund.Refunded);
        Assert.Equal("Refunded", (await RefusedAsync(town, user, "warp:late")).Code);
        Assert.Equal(25, (await ReloadAsync(user)).Gems);
        Assert.Empty(await LedgerAsync());
        // Other keys are unaffected.
        Assert.Equal(10, (await ChargeAsync(town, user, "warp:next")).Charged);
    }

    [Fact]
    public async Task Refund_BeforeAnyCharge_PersistsTheVoid_SoItOutlivesTheService()
    {
        var user = await UserAsync(gems: 25, coins: 100);
        var town = await TownAsync(price: 10);

        await RefundAsync(user, "warp:durable");
        await RefundAsync(user, "warp:durable");

        // A fresh service and context (an API restart, or another instance) still refuses it.
        await using (var ctx = NewContext())
        {
            var marker = Assert.Single(await ctx.TeleportFeeVoids.AsNoTracking().ToListAsync());
            Assert.Equal("warp:durable", marker.IdempotencyKey);
            Assert.Equal(user, marker.UserId);
            Assert.Equal("cancelled by a listener", marker.Reason);
        }
        Assert.Equal("Refunded", (await RefusedAsync(town, user, "warp:durable")).Code);
        await using (var ctx = NewContext())
        {
            var error = await Assert.ThrowsAsync<TeleportDestinationException>(() => Service(ctx).ChargeRequestFeeAsync(
                new TeleportRequestFeeDto { UserId = user, IdempotencyKey = "warp:durable", AmountCoins = 5 }));
            Assert.Equal(TeleportDestinationException.Refunded, error.Code);
        }
        Assert.Equal(25, (await ReloadAsync(user)).Gems);
        Assert.Equal(100, (await ReloadAsync(user)).Coins);
        Assert.Empty(await LedgerAsync());
    }

    [Fact]
    public async Task Refund_AfterTheCharge_RecordsNoVoid()
    {
        var user = await UserAsync(gems: 25);
        var town = await TownAsync(price: 10);
        await ChargeAsync(town, user, "warp:paid");

        await RefundAsync(user, "warp:paid");

        await using var ctx = NewContext();
        Assert.Empty(await ctx.TeleportFeeVoids.ToListAsync());
    }

    [Fact]
    public async Task Refund_OfAnotherPlayersCharge_IsKeyReuse()
    {
        var payer = await UserAsync(gems: 25);
        var other = await UserAsync(gems: 25);
        var town = await TownAsync(price: 10);
        await ChargeAsync(town, payer, "warp:mine");

        await using var ctx = NewContext();
        var refused = await Assert.ThrowsAsync<TeleportDestinationException>(() =>
            Service(ctx).RefundAsync(new TeleportRefundRequestDto { UserId = other, IdempotencyKey = "warp:mine" }));

        Assert.Equal("IdempotencyKeyReuse", refused.Code);
        Assert.Equal(15, (await ReloadAsync(payer)).Gems);
        Assert.Equal(25, (await ReloadAsync(other)).Gems);
    }

    // ===== Request fees (/tpa, /tpahere) =====

    private async Task<TeleportChargeResultDto> FeeAsync(int userId, int amount, string key, int? other = null)
    {
        await using var ctx = NewContext();
        return await Service(ctx).ChargeRequestFeeAsync(new TeleportRequestFeeDto
        {
            UserId = userId, AmountCoins = amount, IdempotencyKey = key, OtherUserId = other
        });
    }

    [Fact]
    public async Task RequestFee_ChargesCoinsOnce_AndRefunds()
    {
        var user = await UserAsync(coins: 1000);
        var target = await UserAsync();

        var fee = await FeeAsync(user, 250, "tpa:1", target);
        var replay = await FeeAsync(user, 250, "tpa:1", target);

        Assert.Equal("Coins", fee.Currency);
        Assert.Equal(250, fee.Charged);
        Assert.Equal(750, fee.NewBalance);
        Assert.True(replay.Replayed);
        Assert.Equal(750, (await ReloadAsync(user)).Coins);
        var tx = Assert.Single(await LedgerAsync());
        Assert.Equal(CurrencyReasons.TeleportFee, tx.ReasonCode);
        Assert.Equal("TeleportRequest", tx.SourceType);
        Assert.Equal(target.ToString(), tx.SourceRef);

        var refund = await RefundAsync(user, "tpa:1");
        Assert.Equal("Coins", refund.Currency);
        Assert.Equal(1000, (await ReloadAsync(user)).Coins);
    }

    [Fact]
    public async Task RequestFee_InsufficientCoins_And_AmountBounds()
    {
        var user = await UserAsync(coins: 10);

        var refused = await Assert.ThrowsAsync<TeleportDestinationException>(() => FeeAsync(user, 11, "tpa:a"));
        Assert.Equal("InsufficientCoins", refused.Code);
        await Assert.ThrowsAsync<ArgumentException>(() => FeeAsync(user, 0, "tpa:b"));
        await Assert.ThrowsAsync<ArgumentException>(() => FeeAsync(user, -5, "tpa:c"));
        Assert.Equal(10, (await ReloadAsync(user)).Coins);
    }

    [Fact]
    public async Task RequestFee_SameKeyDifferentAmount_IsKeyReuse()
    {
        var user = await UserAsync(coins: 1000);
        await FeeAsync(user, 100, "tpa:k");

        var refused = await Assert.ThrowsAsync<TeleportDestinationException>(() => FeeAsync(user, 200, "tpa:k"));

        Assert.Equal("IdempotencyKeyReuse", refused.Code);
        Assert.Equal(900, (await ReloadAsync(user)).Coins);
    }

    [Fact]
    public async Task WarpKey_CantBeReplayedAsARequestFee()
    {
        var user = await UserAsync(gems: 20, coins: 1000);
        var town = await TownAsync(price: 10);
        await ChargeAsync(town, user, "shared:1");

        var refused = await Assert.ThrowsAsync<TeleportDestinationException>(() => FeeAsync(user, 10, "shared:1"));

        Assert.Equal("IdempotencyKeyReuse", refused.Code);
    }

    // ===== Authoring validation and apply =====

    [Fact]
    public async Task ValidateSettings_ChecksPriceTitleAndPremiumTier()
    {
        var title = await BracketAsync("Knight", "Dame", 100);
        var premium = await GroupAsync("Royal", 20);
        var staff = await GroupAsync("Moderator", 50, premium: false);
        await using var ctx = NewContext();
        var service = Service(ctx);

        await service.ValidateSettingsAsync(new TownDto { TeleportEnabled = true, TeleportPriceGems = 10, TeleportMinTitleBracketId = title, TeleportMinPremiumGroupId = premium });
        await service.ValidateSettingsAsync(new TownDto { TeleportEnabled = null, TeleportPriceGems = -1 }); // not on the form: ignored
        await Assert.ThrowsAsync<ArgumentException>(() => service.ValidateSettingsAsync(new TownDto { TeleportEnabled = true, TeleportPriceGems = -1 }));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ValidateSettingsAsync(new TownDto { TeleportEnabled = true, TeleportPriceGems = BalanceLimits.MaxGems + 1 }));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ValidateSettingsAsync(new TownDto { TeleportEnabled = true, TeleportMinTitleBracketId = 98765 }));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ValidateSettingsAsync(new TownDto { TeleportEnabled = true, TeleportMinPremiumGroupId = 98765 }));
        var notPremium = await Assert.ThrowsAsync<ArgumentException>(() => service.ValidateSettingsAsync(new TownDto { TeleportEnabled = false, TeleportMinPremiumGroupId = staff }));
        Assert.Contains("isn't a premium tier", notPremium.Message);
    }

    [Fact]
    public void ApplySettings_LeavesEverythingWhenTheFormDoesntCarryThem()
    {
        var town = new Town { TeleportEnabled = true, TeleportPriceGems = 10, TeleportMinTitleBracketId = 3, TeleportRequiresDiscovery = true };

        DomainTeleportSettings.Apply(town, new TownDto { TeleportEnabled = null, TeleportPriceGems = 0 });
        Assert.True(town.TeleportEnabled);
        Assert.Equal(10, town.TeleportPriceGems);

        DomainTeleportSettings.Apply(town, new TownDto { TeleportEnabled = false });
        Assert.False(town.TeleportEnabled);
        Assert.Equal(0, town.TeleportPriceGems);
        Assert.Null(town.TeleportMinTitleBracketId);
        Assert.False(town.TeleportRequiresDiscovery);
    }
}
