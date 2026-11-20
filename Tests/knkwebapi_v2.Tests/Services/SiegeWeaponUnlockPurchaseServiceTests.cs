using FluentAssertions;
using KnKWebAPI.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Siege shop L4 (knk-workspace docs/specs/siege-shop/DESIGN.md § 5.6, § 6.2.3; A27; PandiO/knk-workspace#869, #795):
/// unlocking a siege weapon type with coins and an optional minimum title, against the real ledger (in-memory), and the
/// controller's "whose unlock" rule.
/// </summary>
public class SiegeWeaponUnlockPurchaseServiceTests
{
    private readonly string _db = $"siege-unlocks-{Guid.NewGuid()}";

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_db).Options);

    private static SiegeWeaponUnlockPurchaseService Service(KnKDbContext ctx)
    {
        var users = new UserRepository(ctx);
        return new SiegeWeaponUnlockPurchaseService(new SiegeWeaponRepository(ctx), users, new TitleBracketRepository(ctx),
            new CurrencyService(new CurrencyRepository(ctx), users, NullLogger<CurrencyService>.Instance));
    }

    private static CurrencyContext Ctx() => CurrencyContext.ForSystem("test", CurrencyReasons.SiegeUnlock, "-");

    private async Task<(int User, int Trebuchet, int Ballista)> SeedAsync(int coins = 1200, int xp = 50)
    {
        await using var ctx = NewContext();
        ctx.TitleBrackets.AddRange(
            new TitleBracket { Id = 1, MaleName = "Peasant", FemaleName = "Peasant", MinExperience = 0 },
            new TitleBracket { Id = 2, MaleName = "Squire", FemaleName = "Squiress", MinExperience = 100 });
        var user = new User { Username = "a1", Uuid = Guid.NewGuid().ToString(), Coins = coins, ExperiencePoints = xp };
        var trebuchet = new SiegeWeaponTemplate { Name = "Trebuchet", UnlockPrice = 1000, UnlockMinTitleLevel = 2 };
        var ballista = new SiegeWeaponTemplate { Name = "Ballista", UnlockPrice = 500 };
        ctx.Users.Add(user);
        ctx.SiegeWeaponTemplates.AddRange(trebuchet, ballista);
        await ctx.SaveChangesAsync();
        return (user.Id, trebuchet.Id, ballista.Id);
    }

    private async Task<int> CoinsAsync(int userId)
    {
        await using var ctx = NewContext();
        return (await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == userId)).Coins;
    }

    [Fact]
    public async Task Purchase_ChargesTheUnlockPriceOnce_UnderTheSpecKey_AndWritesTheUnlock()
    {
        var (user, _, ballista) = await SeedAsync();

        await using (var ctx = NewContext())
        {
            var result = await Service(ctx).PurchaseAsync(user, ballista, Ctx());
            result.CoinsPaid.Should().Be(500);
            result.AlreadyUnlocked.Should().BeFalse();
            result.Coins.Should().Be(700);
        }

        (await CoinsAsync(user)).Should().Be(700);
        await using var check = NewContext();
        (await check.UserSiegeWeaponUnlocks.CountAsync(u => u.UserId == user && u.SiegeWeaponTemplateId == ballista)).Should().Be(1);
        var posting = await check.CurrencyTransactions.SingleAsync();
        posting.ReasonCode.Should().Be("SIEGE_UNLOCK");
        posting.IdempotencyKey.Should().Be($"siege-unlock:{ballista}:{user}");
    }

    [Fact]
    public async Task Purchase_ADoubleClick_ChargesOnce_A27()
    {
        var (user, _, ballista) = await SeedAsync();

        await using (var first = NewContext()) await Service(first).PurchaseAsync(user, ballista, Ctx());
        SiegeWeaponUnlockPurchaseResultDto second;
        await using (var again = NewContext()) second = await Service(again).PurchaseAsync(user, ballista, Ctx());

        second.AlreadyUnlocked.Should().BeTrue();
        second.CoinsPaid.Should().Be(0);
        (await CoinsAsync(user)).Should().Be(700);
        await using var check = NewContext();
        (await check.CurrencyTransactions.CountAsync()).Should().Be(1);
        (await check.UserSiegeWeaponUnlocks.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Purchase_BelowTheMinimumTitle_IsRefused_AndChargesNothing_A27()
    {
        var (user, trebuchet, _) = await SeedAsync(coins: 5000, xp: 50);

        await using var ctx = NewContext();
        var refusal = await Assert.ThrowsAsync<SiegeWeaponUnlockRefusedException>(() => Service(ctx).PurchaseAsync(user, trebuchet, Ctx()));

        refusal.Code.Should().Be("NeedsTitle");
        refusal.Message.Should().Contain("Squire");
        (await CoinsAsync(user)).Should().Be(5000);
        await using var check = NewContext();
        (await check.CurrencyTransactions.CountAsync()).Should().Be(0);
        (await check.UserSiegeWeaponUnlocks.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Purchase_AtTheMinimumTitle_Unlocks()
    {
        var (user, trebuchet, _) = await SeedAsync(coins: 1000, xp: 100);

        await using var ctx = NewContext();
        var result = await Service(ctx).PurchaseAsync(user, trebuchet, Ctx());

        result.CoinsPaid.Should().Be(1000);
        (await CoinsAsync(user)).Should().Be(0);
    }

    [Fact]
    public async Task Purchase_WithoutEnoughCoins_IsRefused_AndChargesNothing_A27()
    {
        var (user, _, ballista) = await SeedAsync(coins: 300);

        await using var ctx = NewContext();
        var refusal = await Assert.ThrowsAsync<SiegeWeaponUnlockRefusedException>(() => Service(ctx).PurchaseAsync(user, ballista, Ctx()));

        refusal.Code.Should().Be("InsufficientFunds");
        refusal.Message.Should().Contain("300/500");
        (await CoinsAsync(user)).Should().Be(300);
        await using var check = NewContext();
        (await check.UserSiegeWeaponUnlocks.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Purchase_AFreeType_UnlocksWithoutAPosting()
    {
        var (user, _, _) = await SeedAsync();
        int free;
        await using (var seed = NewContext())
        {
            var template = new SiegeWeaponTemplate { Name = "Mangonel", UnlockPrice = 0 };
            seed.SiegeWeaponTemplates.Add(template);
            await seed.SaveChangesAsync();
            free = template.Id;
        }

        await using var ctx = NewContext();
        var result = await Service(ctx).PurchaseAsync(user, free, Ctx());

        result.CoinsPaid.Should().Be(0);
        result.AlreadyUnlocked.Should().BeFalse();
        await using var check = NewContext();
        (await check.CurrencyTransactions.CountAsync()).Should().Be(0);
        (await check.UserSiegeWeaponUnlocks.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Purchase_UnknownTemplateOrUser_IsNotFound()
    {
        var (user, _, ballista) = await SeedAsync();

        await using var ctx = NewContext();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Service(ctx).PurchaseAsync(user, 9999, Ctx()));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Service(ctx).PurchaseAsync(9999, ballista, Ctx()));
    }

    [Fact]
    public async Task Overview_ListsEveryTemplate_WithPriceMinimumTitleAndState()
    {
        var (user, trebuchet, ballista) = await SeedAsync(coins: 600);
        int expensive;
        await using (var seed = NewContext())
        {
            var template = new SiegeWeaponTemplate { Name = "Catapult", UnlockPrice = 800 };
            seed.SiegeWeaponTemplates.Add(template);
            await seed.SaveChangesAsync();
            expensive = template.Id;
        }

        await using (var buy = NewContext())
        {
            (await Service(buy).GetOverviewAsync(user)).Offers.Single(o => o.TemplateId == ballista).State
                .Should().Be(SiegeWeaponUnlockStates.Unlockable);
            await Service(buy).PurchaseAsync(user, ballista, Ctx());
        }

        await using var ctx = NewContext();
        var overview = await Service(ctx).GetOverviewAsync(user);

        overview.Coins.Should().Be(100);
        overview.TitleLevel.Should().Be(1);
        overview.TitleName.Should().Be("Peasant");
        overview.Offers.Select(o => o.Name).Should().Equal("Ballista", "Catapult", "Trebuchet");
        var treb = overview.Offers.Single(o => o.TemplateId == trebuchet);
        treb.State.Should().Be(SiegeWeaponUnlockStates.NeedsTitle);
        treb.MinTitleLevel.Should().Be(2);
        treb.MinTitleName.Should().Be("Squire");
        treb.UnlockPrice.Should().Be(1000);
        overview.Offers.Single(o => o.TemplateId == ballista).State.Should().Be(SiegeWeaponUnlockStates.Unlocked);
        overview.Offers.Single(o => o.TemplateId == ballista).UnlockedAt.Should().NotBeNull();
        overview.Offers.Single(o => o.TemplateId == expensive).State.Should().Be(SiegeWeaponUnlockStates.NeedsCoins);
    }

    // ===== Controller: whose unlock it is =====

    private static SiegeWeaponUnlocksController Controller(Mock<ISiegeWeaponUnlockPurchaseService> service, KnkCaller caller)
    {
        var http = new DefaultHttpContext();
        http.Items["knk.caller"] = caller;
        return new SiegeWeaponUnlocksController(service.Object) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    private static Mock<ISiegeWeaponUnlockPurchaseService> Buying(int userId) {
        var service = new Mock<ISiegeWeaponUnlockPurchaseService>();
        service.Setup(s => s.PurchaseAsync(userId, 3, It.IsAny<CurrencyContext>()))
            .ReturnsAsync(new SiegeWeaponUnlockPurchaseResultDto { TemplateId = 3, UserId = userId });
        return service;
    }

    [Fact]
    public async Task Controller_AWebUser_BuysForThemselves()
    {
        var service = Buying(7);
        var result = await Controller(service, new KnkCaller(false, true, 7, null))
            .Purchase(new SiegeWeaponUnlockPurchaseRequestDto { TemplateId = 3 });
        result.Result.Should().BeOfType<OkObjectResult>();
        service.Verify(s => s.PurchaseAsync(7, 3, It.Is<CurrencyContext>(c => c.InitiatorUserId == 7)), Times.Once);
    }

    [Fact]
    public async Task Controller_AWebUser_CannotBuyForAnotherPlayer()
    {
        var service = Buying(8);
        var result = await Controller(service, new KnkCaller(false, true, 7, null))
            .Purchase(new SiegeWeaponUnlockPurchaseRequestDto { TemplateId = 3, UserId = 8 });
        result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        service.Verify(s => s.PurchaseAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CurrencyContext>()), Times.Never);
    }

    [Fact]
    public async Task Controller_TheGameServer_BuysForTheNamedPlayer_AndNeedsOne()
    {
        var service = Buying(8);
        var plugin = new KnkCaller(true, false, null, null);
        (await Controller(service, plugin).Purchase(new SiegeWeaponUnlockPurchaseRequestDto { TemplateId = 3, UserId = 8 }))
            .Result.Should().BeOfType<OkObjectResult>();
        (await Controller(service, plugin).Purchase(new SiegeWeaponUnlockPurchaseRequestDto { TemplateId = 3 }))
            .Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Controller_ARefusal_IsA409WithItsCode()
    {
        var service = new Mock<ISiegeWeaponUnlockPurchaseService>();
        service.Setup(s => s.PurchaseAsync(8, 3, It.IsAny<CurrencyContext>()))
            .ThrowsAsync(new SiegeWeaponUnlockRefusedException("NeedsTitle", "You need the title Squire to unlock the Trebuchet."));
        var result = await Controller(service, new KnkCaller(true, false, null, null))
            .Purchase(new SiegeWeaponUnlockPurchaseRequestDto { TemplateId = 3, UserId = 8 });
        var conflict = result.Result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.Value!.ToString().Should().Contain("NeedsTitle");
    }

    [Fact]
    public async Task Controller_Mine_NeedsAWebUser()
    {
        var service = new Mock<ISiegeWeaponUnlockPurchaseService>();
        service.Setup(s => s.GetOverviewAsync(7)).ReturnsAsync(new SiegeWeaponUnlocksOverviewDto { UserId = 7 });
        (await Controller(service, new KnkCaller(false, true, 7, null)).GetMine()).Result.Should().BeOfType<OkObjectResult>();
        (await Controller(service, new KnkCaller(true, false, null, null)).GetMine()).Result.Should().BeOfType<UnauthorizedResult>();
    }
}
