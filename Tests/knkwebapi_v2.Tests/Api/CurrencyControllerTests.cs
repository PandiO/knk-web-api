using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// CurrencyController (currency-payments IMPLEMENTATION_PLAN.md Phase 3): who may send and read,
/// status codes per outcome, the recipient's PaymentReceived notification.
/// </summary>
[Trait("Category", "API")]
public class CurrencyControllerTests
{
    private const string Key = "the-plugin-key";

    private readonly Mock<ICurrencyService> _currency = new();
    private readonly Mock<ICurrencyTransferService> _transfers = new();
    private readonly Mock<IPermissionResolutionService> _permissions = new();
    private readonly Mock<IPlayerNotificationQueue> _notifications = new();
    private readonly CurrencyController _controller;

    public CurrencyControllerTests()
    {
        _controller = new CurrencyController(_currency.Object, _transfers.Object, _permissions.Object, _notifications.Object);
    }

    private void SetRequest(bool plugin = false, int? actingUserId = null, int? webUserId = null, string? idempotencyKey = "pay-key-1")
    {
        var configuration = new Mock<Microsoft.Extensions.Configuration.IConfiguration>();
        configuration.Setup(c => c["Security:PluginApiKey"]).Returns(Key);
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(Microsoft.Extensions.Configuration.IConfiguration))).Returns(configuration.Object);
        var httpContext = new DefaultHttpContext { RequestServices = services.Object };
        if (plugin) httpContext.Request.Headers[PluginServiceAuth.ApiKeyHeader] = Key;
        if (actingUserId != null) httpContext.Request.Headers[PluginServiceAuth.ActingUserHeader] = actingUserId.Value.ToString();
        if (webUserId != null)
        {
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("uid", webUserId.Value.ToString()) }, "Test"));
        }
        if (idempotencyKey != null) httpContext.Request.Headers["Idempotency-Key"] = idempotencyKey;
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    private static TransferResultDto Completed(bool replayed = false) => new()
    {
        Status = TransferResultDto.StatusCompleted, PublicId = "01JABCDEFGHJKMNPQRSTVWXYZ0", TransactionId = 5, Replayed = replayed,
        Currency = "Coins", Amount = 500, SenderUserId = 7, SenderUsername = "alice", RecipientUserId = 8, RecipientUsername = "bob",
        RecipientUuid = "uuid-bob", RecipientBalanceAfter = 1500
    };

    private static CreateTransferDto Body(int sender = 7) => new() { SenderUserId = sender, RecipientUserId = 8, Currency = Currency.Coins, Amount = 500 };

    [Fact]
    public async Task Transfer_ByTheGameServerForTheSender_Completes_AndNotifiesTheRecipient()
    {
        SetRequest(plugin: true, actingUserId: 7);
        CurrencyContext? seen = null;
        _transfers.Setup(t => t.TransferAsync(It.IsAny<TransferRequest>(), It.IsAny<CurrencyContext>(), It.IsAny<CancellationToken>()))
            .Callback<TransferRequest, CurrencyContext, CancellationToken>((_, ctx, _) => seen = ctx)
            .ReturnsAsync(Completed());

        var result = await _controller.CreateTransfer(Body(), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(("pay-key-1", CurrencyIdempotencyScopes.Plugin, CurrencyInitiator.Player, (int?)7),
            (seen!.IdempotencyKey, seen.IdempotencyScope, seen.Initiator, seen.InitiatorUserId));
        _notifications.Verify(n => n.EnqueuePayment(8, "uuid-bob", "bob", It.Is<PaymentNotificationDto>(p =>
            p.Amount == 500 && p.FromUsername == "alice" && p.BalanceAfter == 1500 && p.Currency == "Coins")), Times.Once);
    }

    [Fact]
    public async Task Transfer_ReplayedOrPending_DoesNotNotifyAgain()
    {
        SetRequest(plugin: true, actingUserId: 7);
        _transfers.SetupSequence(t => t.TransferAsync(It.IsAny<TransferRequest>(), It.IsAny<CurrencyContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Completed(replayed: true))
            .ReturnsAsync(new TransferResultDto { Status = TransferResultDto.StatusPendingConfirmation, Currency = "Coins", Pending = new PendingTransferDto { PublicId = "x", Status = "Pending", Currency = "Coins" } });

        Assert.IsType<OkObjectResult>(await _controller.CreateTransfer(Body(), CancellationToken.None));
        var pending = Assert.IsType<ObjectResult>(await _controller.CreateTransfer(Body(), CancellationToken.None));
        Assert.Equal(202, pending.StatusCode);
        _notifications.Verify(n => n.EnqueuePayment(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<PaymentNotificationDto>()), Times.Never);
    }

    [Fact]
    public async Task Transfer_ForSomeoneElse_IsForbidden()
    {
        SetRequest(plugin: true, actingUserId: 9);
        var result = Assert.IsType<ObjectResult>(await _controller.CreateTransfer(Body(sender: 7), CancellationToken.None));
        Assert.Equal(403, result.StatusCode);
        _transfers.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Transfer_WithoutActingPlayerOrKey_IsRefused()
    {
        SetRequest(plugin: true);
        Assert.IsType<BadRequestObjectResult>(await _controller.CreateTransfer(Body(), CancellationToken.None));

        SetRequest(plugin: true, actingUserId: 7, idempotencyKey: null);
        Assert.IsType<BadRequestObjectResult>(await _controller.CreateTransfer(Body(), CancellationToken.None));

        // A web user never counts as the game server, whatever X-Acting-User-Id says.
        SetRequest(webUserId: 7, actingUserId: 7);
        Assert.IsType<BadRequestObjectResult>(await _controller.CreateTransfer(Body(), CancellationToken.None));
        _transfers.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(CurrencyErrorCode.DailyCapExceeded, 422)]
    [InlineData(CurrencyErrorCode.NotTransferable, 422)]
    [InlineData(CurrencyErrorCode.NewAccountRestricted, 422)]
    [InlineData(CurrencyErrorCode.InsufficientFunds, 400)]
    [InlineData(CurrencyErrorCode.RecipientNotFound, 404)]
    [InlineData(CurrencyErrorCode.PendingTransferClosed, 409)]
    [InlineData(CurrencyErrorCode.IdempotencyKeyReuse, 409)]
    public async Task Refusals_MapToStatusCodes(CurrencyErrorCode code, int status)
    {
        SetRequest(plugin: true, actingUserId: 7);
        _transfers.Setup(t => t.TransferAsync(It.IsAny<TransferRequest>(), It.IsAny<CurrencyContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CurrencyException(code, "no"));

        var result = Assert.IsAssignableFrom<ObjectResult>(await _controller.CreateTransfer(Body(), CancellationToken.None));
        Assert.Equal(status, result.StatusCode);
    }

    [Fact]
    public async Task Confirm_UsesTheActingPlayerAsSender_AndRejectsMalformedIds()
    {
        SetRequest(plugin: true, actingUserId: 7);
        var id = "01JABCDEFGHJKMNPQRSTVWXYZ0";
        _transfers.Setup(t => t.ConfirmTransferAsync(id, 7, It.IsAny<CurrencyContext>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Completed());

        Assert.IsType<OkObjectResult>(await _controller.ConfirmTransfer(id));
        Assert.IsType<NotFoundObjectResult>(await _controller.ConfirmTransfer("../../etc"));
        Assert.IsType<NotFoundObjectResult>(await _controller.CancelTransfer("short", CancellationToken.None));
    }

    [Fact]
    public async Task Balances_ForAnonymous_AreUnauthorized_AndForOthers_NeedTheHistoryNode()
    {
        _currency.Setup(c => c.GetBalancesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new BalancesDto { UserId = 8 });

        SetRequest();
        Assert.IsType<UnauthorizedObjectResult>(await _controller.GetBalances(8, CancellationToken.None));

        SetRequest(webUserId: 8);
        Assert.IsType<OkObjectResult>(await _controller.GetBalances(8, CancellationToken.None));

        SetRequest(webUserId: 9);
        _permissions.Setup(p => p.CheckAsync(9, CurrencyController.CurrencyHistoryNode))
            .ReturnsAsync(new PermissionCheckResponseDto { Result = PermissionResolutionResult.Undeclared });
        Assert.Equal(403, Assert.IsType<ObjectResult>(await _controller.GetBalances(8, CancellationToken.None)).StatusCode);

        _permissions.Setup(p => p.CheckAsync(9, CurrencyController.CurrencyHistoryNode))
            .ReturnsAsync(new PermissionCheckResponseDto { Result = PermissionResolutionResult.Granted });
        Assert.IsType<OkObjectResult>(await _controller.GetBalances(8, CancellationToken.None));

        SetRequest(plugin: true);
        Assert.IsType<OkObjectResult>(await _controller.GetBalances(8, CancellationToken.None));
    }

    [Fact]
    public async Task Transactions_FilterByCurrency()
    {
        SetRequest(plugin: true);
        LedgerQuery? seen = null;
        _currency.Setup(c => c.GetBalancesAsync(8, It.IsAny<CancellationToken>())).ReturnsAsync(new BalancesDto { UserId = 8 });
        _currency.Setup(c => c.GetHistoryAsync(It.IsAny<LedgerQuery>(), It.IsAny<CancellationToken>()))
            .Callback<LedgerQuery, CancellationToken>((q, _) => seen = q)
            .ReturnsAsync(new PagedResultDto<LedgerLineDto>());

        Assert.IsType<OkObjectResult>(await _controller.GetTransactions(8, "xp", 2, 500));
        Assert.Equal((8, (Currency?)Currency.Experience, 2, 100), (seen!.UserId!.Value, seen.Currency, seen.Page, seen.PageSize));
        Assert.IsType<BadRequestObjectResult>(await _controller.GetTransactions(8, "diamonds"));
    }

    [Fact]
    public async Task Leaderboard_ClampsPageAndSize_BeforeCachingAndQuerying()
    {
        using var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var controller = new CurrencyController(_currency.Object, _transfers.Object, _permissions.Object, _notifications.Object, cache);
        var calls = new List<(int Page, int Size)>();
        _transfers.Setup(t => t.GetLeaderboardAsync(Currency.Coins, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<Currency, int, int, CancellationToken>((_, page, size, _) => calls.Add((page, size)))
            .ReturnsAsync((Currency _, int page, int size, CancellationToken _) => new LeaderboardDto { Currency = "Coins", Page = page, PageSize = size });

        // However far past the limits, every request lands on the same bounded key and query.
        foreach (var page in new[] { 101, 5_000, int.MaxValue })
        {
            var board = Assert.IsType<LeaderboardDto>(Assert.IsType<OkObjectResult>(await controller.GetLeaderboard("coins", page, 10_000)).Value);
            Assert.Equal((CurrencyService.MaxLeaderboardPage, CurrencyService.MaxLeaderboardPageSize), (board.Page, board.PageSize));
        }
        await controller.GetLeaderboard("coins", -3, 0);

        Assert.Equal(new[] { (100, 50), (1, 1) }, calls);
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void GameServerOnlyRoutes_CarryTheServiceFilter()
    {
        foreach (var name in new[] { nameof(CurrencyController.CreateTransfer), nameof(CurrencyController.ConfirmTransfer), nameof(CurrencyController.CancelTransfer) })
        {
            Assert.NotNull(typeof(CurrencyController).GetMethod(name)!.GetCustomAttribute<RequirePluginServiceAttribute>());
        }
        var baltop = typeof(CurrencyController).GetMethod(nameof(CurrencyController.GetLeaderboard))!.GetCustomAttribute<RequireServiceOrPermissionAttribute>();
        Assert.Equal("knk.baltop", baltop!.Node);
        Assert.NotNull(typeof(PlayerNotificationsController).GetCustomAttribute<RequirePluginServiceAttribute>());
    }
}
