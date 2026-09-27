using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
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
/// CurrencyAdminController (currency-payments IMPLEMENTATION_PLAN.md Phase 4): the node each
/// route needs (and that a web user without it gets 403), the event-log query mapping, and the
/// adjustment route's reason rules.
/// </summary>
[Trait("Category", "API")]
public class CurrencyAdminControllerTests
{
    private const string Key = "the-plugin-key";
    private const int StaffId = 900;

    private readonly Mock<ICurrencyService> _currency = new();
    private readonly Mock<ICurrencyAdminService> _admin = new();
    private readonly Mock<IUserService> _users = new();
    private readonly Mock<IPermissionResolutionService> _permissions = new();
    private readonly CurrencyAdminController _controller;

    public CurrencyAdminControllerTests()
    {
        _controller = new CurrencyAdminController(_currency.Object, _admin.Object, _users.Object, _permissions.Object);
    }

    private static DefaultHttpContext Http(bool plugin = false, int? webUserId = null, string? idempotencyKey = "adjust-key-1")
    {
        var configuration = new Mock<Microsoft.Extensions.Configuration.IConfiguration>();
        configuration.Setup(c => c["Security:PluginApiKey"]).Returns(Key);
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(Microsoft.Extensions.Configuration.IConfiguration))).Returns(configuration.Object);
        var httpContext = new DefaultHttpContext { RequestServices = services.Object };
        if (plugin) httpContext.Request.Headers[PluginServiceAuth.ApiKeyHeader] = Key;
        if (webUserId != null)
        {
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("uid", webUserId.Value.ToString()) }, "Test"));
        }
        if (idempotencyKey != null) httpContext.Request.Headers["Idempotency-Key"] = idempotencyKey;
        return httpContext;
    }

    private void SetRequest(bool plugin = false, int? webUserId = null, string? idempotencyKey = "adjust-key-1") =>
        _controller.ControllerContext = new ControllerContext { HttpContext = Http(plugin, webUserId, idempotencyKey) };

    private void Grant(string node) =>
        _permissions.Setup(p => p.CheckAsync(StaffId, node)).ReturnsAsync(new PermissionCheckResponseDto { Result = PermissionResolutionResult.Granted });

    // ===== Nodes per route =====

    public static IEnumerable<object[]> Routes() => new[]
    {
        new object[] { nameof(CurrencyAdminController.GetLedger), StaffPermissions.CurrencyHistory, false },
        new object[] { nameof(CurrencyAdminController.GetTransaction), StaffPermissions.CurrencyHistory, true },
        new object[] { nameof(CurrencyAdminController.Reverse), StaffPermissions.CurrencyReverse, true },
        new object[] { nameof(CurrencyAdminController.Adjust), StaffPermissions.ManageUsers, true },
        new object[] { nameof(CurrencyAdminController.GetTransferLock), StaffPermissions.ManageUsers, true },
        new object[] { nameof(CurrencyAdminController.LockTransfers), StaffPermissions.CurrencyLock, true },
        new object[] { nameof(CurrencyAdminController.UnlockTransfers), StaffPermissions.CurrencyLock, true },
        new object[] { nameof(CurrencyAdminController.GetPolicies), StaffPermissions.CurrencyPolicy, false },
        new object[] { nameof(CurrencyAdminController.UpdatePolicy), StaffPermissions.CurrencyPolicy, false }
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task EachRoute_NeedsItsNode_WebUsersWithoutItGet403(string action, string node, bool pluginAllowed)
    {
        var method = typeof(CurrencyAdminController).GetMethod(action)!;
        var filter = method.GetCustomAttributes<TypeFilterAttribute>().Single();
        Assert.Equal(pluginAllowed ? typeof(RequireServiceOrPermissionFilter) : typeof(RequirePermissionFilter), filter.ImplementationType);
        Assert.Equal(node, Assert.Single(filter.Arguments!));

        async Task<IActionResult?> Run(HttpContext http)
        {
            IAsyncAuthorizationFilter instance = pluginAllowed
                ? new RequireServiceOrPermissionFilter(node, _permissions.Object)
                : new RequirePermissionFilter(node, _permissions.Object);
            var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>());
            await instance.OnAuthorizationAsync(context);
            return context.Result;
        }

        Assert.Equal(401, ((ObjectResult)(await Run(Http()))!).StatusCode);
        Assert.Equal(403, ((ObjectResult)(await Run(Http(webUserId: StaffId)))!).StatusCode);
        Grant(node);
        Assert.Null(await Run(Http(webUserId: StaffId)));
        var plugin = await Run(Http(plugin: true));
        if (pluginAllowed) Assert.Null(plugin);
        else Assert.NotNull(plugin);
    }

    // ===== Event log =====

    [Fact]
    public async Task Ledger_MapsFiltersSortingAndPaging_ToTheQuery()
    {
        LedgerQuery? seen = null;
        _currency.Setup(c => c.GetHistoryAsync(It.IsAny<LedgerQuery>(), It.IsAny<CancellationToken>()))
            .Callback<LedgerQuery, CancellationToken>((q, _) => seen = q)
            .ReturnsAsync(new PagedResultDto<LedgerLineDto>());

        var from = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.IsType<OkObjectResult>(await _controller.GetLedger(currency: "xp", recipient: " ali ", initiator: "Salary",
            initiatorType: "system", source: "SiegeMatch", reason: "salary", kind: "grant", transaction: "TX 01jabcdefghjkmnpqrstvwxyz0",
            from: from, to: from.AddDays(1), sort: "recipient", dir: "asc", page: 3, pageSize: 500));

        Assert.Equal(Currency.Experience, seen!.Currency);
        Assert.Equal(("ali", "Salary", CurrencyInitiator.System, "SiegeMatch", "SALARY", CurrencyTransactionKind.Grant),
            (seen.UserSearch, seen.InitiatorSearch, seen.Initiator, seen.SourceType, seen.ReasonCode, seen.Kind));
        Assert.Equal("01JABCDEFGHJKMNPQRSTVWXYZ0", seen.TransactionPublicId);
        Assert.Equal((LedgerSort.Recipient, false, 3, 200), (seen.Sort, seen.Descending, seen.Page, seen.PageSize));
        Assert.Equal((from, from.AddDays(1)), (seen.From, seen.To));

        Assert.IsType<OkObjectResult>(await _controller.GetLedger());
        Assert.Equal((LedgerSort.CreatedAt, true, 50), (seen.Sort, seen.Descending, seen.PageSize));

        Assert.IsType<BadRequestObjectResult>(await _controller.GetLedger(currency: "diamonds"));
        Assert.IsType<BadRequestObjectResult>(await _controller.GetLedger(sort: "password"));
        Assert.IsType<BadRequestObjectResult>(await _controller.GetLedger(kind: "Theft"));
    }

    // ===== Adjustments =====

    private static AdminAdjustmentDto Adjustment(Currency currency = Currency.Coins, string category = "compensation", string note = "Lost items to a lag spike") => new()
    {
        TargetUserId = 7, Currency = currency, Mode = CurrencyOperation.Set, Amount = 500, ExpectedCurrent = 200, Category = category, Note = note
    };

    [Fact]
    public async Task Adjust_PostsOneStaffChange_WithCategoryAndNote()
    {
        SetRequest(webUserId: StaffId);
        Grant(StaffPermissions.UserCoins);
        CurrencyContext? ctx = null;
        IReadOnlyList<BalanceChangeDto>? changes = null;
        _users.Setup(u => u.AdjustBalancesAsync(7, It.IsAny<IReadOnlyList<BalanceChangeDto>>(), It.IsAny<CurrencyContext>(), It.IsAny<string?>(), true))
            .Callback<int, IReadOnlyList<BalanceChangeDto>, CurrencyContext, string?, bool>((_, c, x, _, _) => (changes, ctx) = (c, x))
            .ReturnsAsync(new BalanceAdjustmentResultDto());

        Assert.IsType<OkObjectResult>(await _controller.Adjust(Adjustment(), CancellationToken.None));

        var change = Assert.Single(changes!);
        Assert.Equal((Currency.Coins, CurrencyOperation.Set, 500L, (long?)200), (change.Currency, change.Mode, change.Amount, change.ExpectedCurrent));
        Assert.Equal((CurrencyReasons.AdminSet, "Compensation: Lost items to a lag spike", CurrencyInitiator.Admin, (int?)StaffId, CurrencyIdempotencyScopes.Web, "adjust-key-1"),
            (ctx!.ReasonCode, ctx.Reason, ctx.Initiator, ctx.InitiatorUserId, ctx.IdempotencyScope, ctx.IdempotencyKey));
        Assert.Contains("COMPENSATION", ctx.MetadataJson);
    }

    [Fact]
    public async Task Adjust_NeedsTheBalanceNode_ACategory_ATenCharacterNote_AndAKey()
    {
        SetRequest(webUserId: StaffId);
        var forbidden = Assert.IsType<ObjectResult>(await _controller.Adjust(Adjustment(Currency.Gems), CancellationToken.None));
        Assert.Equal(403, forbidden.StatusCode);

        Grant(StaffPermissions.UserCoins);
        Assert.IsType<BadRequestObjectResult>(await _controller.Adjust(Adjustment(category: "because"), CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await _controller.Adjust(Adjustment(note: "fix"), CancellationToken.None));
        SetRequest(webUserId: StaffId, idempotencyKey: null);
        Assert.IsType<BadRequestObjectResult>(await _controller.Adjust(Adjustment(), CancellationToken.None));
        _users.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(CurrencyErrorCode.AdminDailyCapExceeded, 422)]
    [InlineData(CurrencyErrorCode.ExpectedBalanceMismatch, 409)]
    [InlineData(CurrencyErrorCode.InsufficientFunds, 400)]
    public async Task Adjust_Refusals_MapToStatusCodes(CurrencyErrorCode code, int status)
    {
        SetRequest(plugin: true);
        _users.Setup(u => u.AdjustBalancesAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<BalanceChangeDto>>(), It.IsAny<CurrencyContext>(), It.IsAny<string?>(), It.IsAny<bool>()))
            .ThrowsAsync(new CurrencyException(code, "no"));
        var result = Assert.IsAssignableFrom<ObjectResult>(await _controller.Adjust(Adjustment(), CancellationToken.None));
        Assert.Equal(status, result.StatusCode);
    }

    // ===== Reverse, locks, policy =====

    [Fact]
    public async Task Reverse_NamesTheWebComponent_AndMapsRefusals()
    {
        SetRequest(webUserId: StaffId);
        _admin.SetupSequence(a => a.ReverseAsync("01JABCDEFGHJKMNPQRSTVWXYZ0", It.IsAny<ReverseTransactionDto>(),
                It.Is<KnkCaller>(c => c.ActorUserId == StaffId), "WebAppLedger", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReversalResultDto())
            .ThrowsAsync(new CurrencyException(CurrencyErrorCode.AlreadyReversed, "no"))
            .ThrowsAsync(new ArgumentException("short note"));

        var body = new ReverseTransactionDto { Note = "Paid twice by a bug" };
        Assert.IsType<OkObjectResult>(await _controller.Reverse("01JABCDEFGHJKMNPQRSTVWXYZ0", body, CancellationToken.None));
        Assert.Equal(409, Assert.IsAssignableFrom<ObjectResult>(await _controller.Reverse("01JABCDEFGHJKMNPQRSTVWXYZ0", body, CancellationToken.None)).StatusCode);
        Assert.IsType<BadRequestObjectResult>(await _controller.Reverse("01JABCDEFGHJKMNPQRSTVWXYZ0", body, CancellationToken.None));
    }

    [Fact]
    public async Task Locks_AndPolicy_MapNotFoundAndValidation()
    {
        SetRequest(webUserId: StaffId);
        _admin.Setup(a => a.SetTransferLockAsync(404, It.IsAny<string>(), StaffId, It.IsAny<CancellationToken>())).ThrowsAsync(new KeyNotFoundException("no"));
        _admin.Setup(a => a.SetTransferLockAsync(7, "", StaffId, It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("reason"));
        _admin.Setup(a => a.ClearTransferLockAsync(7, StaffId, It.IsAny<CancellationToken>())).ReturnsAsync(new TransferLockDto { UserId = 7 });

        Assert.IsType<NotFoundObjectResult>(await _controller.LockTransfers(404, new SetTransferLockDto { Reason = "Alt funnel" }, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await _controller.LockTransfers(7, new SetTransferLockDto { Reason = "" }, CancellationToken.None));
        Assert.IsType<OkObjectResult>(await _controller.UnlockTransfers(7, CancellationToken.None));

        Assert.IsType<BadRequestObjectResult>(await _controller.UpdatePolicy("xp", new CurrencyPolicyDto(), CancellationToken.None));
        _admin.Setup(a => a.UpdatePolicyAsync(Currency.Gems, It.IsAny<CurrencyPolicyDto>(), StaffId, It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("range"));
        Assert.IsType<BadRequestObjectResult>(await _controller.UpdatePolicy("gems", new CurrencyPolicyDto(), CancellationToken.None));

        var current = new CurrencyPolicyDto { Currency = "Coins", TransfersEnabled = false };
        _admin.Setup(a => a.UpdatePolicyAsync(Currency.Coins, It.IsAny<CurrencyPolicyDto>(), StaffId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CurrencyException(CurrencyErrorCode.PolicyChanged, "changed", current));
        var conflict = Assert.IsType<ConflictObjectResult>(await _controller.UpdatePolicy("coins", new CurrencyPolicyDto(), CancellationToken.None));
        var body = System.Text.Json.JsonSerializer.SerializeToElement(conflict.Value);
        Assert.Equal("PolicyChanged", body.GetProperty("error").GetString());
        Assert.False(body.GetProperty("details").GetProperty("transfersEnabled").GetBoolean());
    }
}
