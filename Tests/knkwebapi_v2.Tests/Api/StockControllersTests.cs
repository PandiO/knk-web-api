using System.Reflection;
using System.Security.Claims;
using KnKWebAPI.Controllers;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Stock;
using knkwebapi_v2.Tests.Services.Stock;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using static knkwebapi_v2.Tests.Services.Stock.StockTestDb;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// The stock endpoints (PandiO/knk-workspace#97): who may move stock (knk.economy.admin or the game server; a visitor
/// gets 403, smoke § 2.1 step 9), and how results map: capacity is a 200 with status Rejected, a reused idempotency key
/// is 409, a malformed request 400, an unknown storage 404, and the web user is the recorded actor.
/// </summary>
public class StockControllersTests : IDisposable
{
    private readonly KnKDbContext _db = Create();

    public void Dispose() => _db.Dispose();

    private static ClaimsPrincipal LoggedIn(int userId) =>
        new(new ClaimsIdentity(new[] { new Claim("uid", userId.ToString()) }, "Bearer"));

    private StockTransactionsController Ledger(int userId = PlayerId)
    {
        var stock = new StockService(_db);
        return new StockTransactionsController(stock, new StockLedgerService(_db), new StockReconciler(_db), new StockRollupService(_db))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = LoggedIn(userId) } },
        };
    }

    private static StockResultDto Result(IActionResult action) =>
        Assert.IsType<StockResultDto>(Assert.IsType<OkObjectResult>(action).Value);

    [Fact]
    public void Ledger_WritesNeedTheEconomyNode_ReadsFollowTheStockTransactionType()
    {
        var type = typeof(StockTransactionsController);
        Assert.Equal(StaffPermissions.EconomyAdmin, type.GetCustomAttribute<RequireServiceOrPermissionForWritesAttribute>()!.Node);
        Assert.Equal("knk.economy.admin", StaffPermissions.EconomyAdmin);
        Assert.Equal("StockTransaction", type.GetCustomAttribute<DashboardReadsAttribute>()!.TypeName);
        Assert.Contains(StaffPermissions.EconomyAdmin, DashboardAccess.Find("StockTransaction")!.ReadNodes);
        foreach (var name in new[] { "Move", "Adjust", "Reverse", "RollUp" })
        {
            var method = type.GetMethod(name)!;
            Assert.NotNull(method.GetCustomAttribute<HttpPostAttribute>());
            Assert.Null(method.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>());
        }
        Assert.Equal(StaffPermissions.ManageWorld,
            typeof(StoragesController).GetCustomAttribute<RequireServiceOrPermissionForWritesAttribute>()!.Node);
    }

    [Theory]
    [InlineData("api/StockTransactions", "Move", 403)]
    [InlineData("api/StockTransactions/adjust", "Adjust", 403)]
    [InlineData("api/StockTransactions/moves/search", "SearchMoves", null)]
    public async Task AVisitor_CannotMoveStock_ButTheSearchIsLeftToTheReadRule(string template, string action, int? expected)
    {
        var permissions = new Mock<IPermissionResolutionService>();
        permissions.Setup(p => p.CheckAsync(5, StaffPermissions.EconomyAdmin))
            .ReturnsAsync(new PermissionCheckResponseDto { UserId = 5, Node = StaffPermissions.EconomyAdmin, Result = PermissionResolutionResult.Denied });
        var http = new DefaultHttpContext { User = LoggedIn(5) };
        http.Request.Method = "POST";
        var descriptor = new ControllerActionDescriptor
        {
            AttributeRouteInfo = new AttributeRouteInfo { Template = template },
            ControllerTypeInfo = typeof(StockTransactionsController).GetTypeInfo(),
            MethodInfo = typeof(StockTransactionsController).GetMethod(action)!,
        };
        var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), descriptor), new List<IFilterMetadata>());

        await new RequireServiceOrPermissionForWritesFilter(StaffPermissions.EconomyAdmin, permissions.Object).OnAuthorizationAsync(context);

        Assert.Equal(expected, (context.Result as ObjectResult)?.StatusCode);
    }

    [Fact]
    public async Task Adjust_RecordsTheWebUserAsActor()
    {
        var result = Result(await Ledger(userId: 42).Adjust(new StockAdjustRequestDto { StorageId = S1, ItemBlueprintId = OakLog, Delta = 64, Note = "count" }, default));

        Assert.Equal(StockResultStatus.Applied, result.Status);
        var tx = await _db.StockTransactions.SingleAsync(t => t.Id == result.TransactionId);
        Assert.Equal((StockActorKind.User, (int?)42, "web"), (tx.ActorKind, tx.ActorUserId, tx.ActorComponent));
    }

    [Fact]
    public async Task Move_OverCapacity_IsA200WithStatusRejected()
    {
        await Ledger().Adjust(new StockAdjustRequestDto { StorageId = S1, ItemBlueprintId = OakLog, Delta = 64, Note = "count" }, default);

        var result = Result(await Ledger().Move(new StockMoveRequestDto
        {
            Reason = StockReason.Distribution,
            Lines = { new StockLineRequestDto { FromStorageId = S1, ToStorageId = S2, ItemBlueprintId = OakLog, Amount = 60 } },
        }, default));

        Assert.Equal(StockResultStatus.Rejected, result.Status);
        Assert.Equal(StockRejectReason.CapacityFull, result.Lines[0].RejectReason);
    }

    [Fact]
    public async Task Move_ErrorsMapTo400_404_And409()
    {
        await Ledger().Adjust(new StockAdjustRequestDto { StorageId = S1, ItemBlueprintId = OakLog, Delta = 64, Note = "count" }, default);
        StockMoveRequestDto Request(int to, long amount) => new()
        {
            Reason = StockReason.Distribution,
            IdempotencyKey = "same",
            Lines = { new StockLineRequestDto { FromStorageId = S1, ToStorageId = to, ItemBlueprintId = OakLog, Amount = amount } },
        };

        Assert.IsType<BadRequestObjectResult>(await Ledger().Move(Request(S1, 1), default));
        Assert.IsType<NotFoundObjectResult>(await Ledger().Move(Request(9999, 1), default));
        Result(await Ledger().Move(Request(S2, 5), default));
        Assert.True(Result(await Ledger().Move(Request(S2, 5), default)).Replayed);
        Assert.IsType<ConflictObjectResult>(await Ledger().Move(Request(S2, 6), default));
    }

    [Fact]
    public async Task UniqueItem_LineWithoutAmount_MovesOne()
    {
        var world = await new StockService(_db).GetSystemStorageIdAsync(Models.Storage.SysWorld);

        var result = Result(await Ledger().Move(new StockMoveRequestDto
        {
            Reason = StockReason.Deposit,
            Lines = { new StockLineRequestDto { FromStorageId = world, ToStorageId = Vault, ItemInstanceId = SwordInstance } },
        }, default));

        Assert.Equal(StockResultStatus.Applied, result.Status);
    }

    [Fact]
    public async Task LedgerReads_DrillDownFromALineToItsMoves()
    {
        await Ledger().Adjust(new StockAdjustRequestDto { StorageId = S1, ItemBlueprintId = OakLog, Delta = 64, Note = "count" }, default);
        await Ledger().Move(new StockMoveRequestDto
        {
            Reason = StockReason.Distribution,
            Lines = { new StockLineRequestDto { FromStorageId = S1, ToStorageId = S2, ItemBlueprintId = OakLog, Amount = 30 } },
        }, default);
        var filters = new Dictionary<string, string> { ["storageId"] = S2.ToString(), ["itemBlueprintId"] = OakLog.ToString() };

        var moves = (PagedResultDto<StockMoveDto>)Assert.IsType<OkObjectResult>(
            (await Ledger().SearchMoves(new PagedQueryDto { Filters = filters })).Result).Value!;
        var transactions = (PagedResultDto<StockTransactionListDto>)Assert.IsType<OkObjectResult>(
            (await Ledger().Search(new PagedQueryDto { Filters = new() { ["reason"] = "AdminAdjust" } })).Result).Value!;

        var move = Assert.Single(moves.Items);
        Assert.Equal((S1, "S1", S2, "S2", 30L, "Oak log"), (move.FromStorageId, move.FromStorageName, move.ToStorageId, move.ToStorageName, move.Amount, move.ItemBlueprintName));
        Assert.Equal(StockReason.AdminAdjust, Assert.Single(transactions.Items).Reason);
        var detail = Assert.IsType<StockTransactionDto>(Assert.IsType<OkObjectResult>(await Ledger().GetById(move.TransactionId)).Value);
        Assert.Single(detail.Moves);
    }
}
