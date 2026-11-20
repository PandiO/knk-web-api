using System.Reflection;
using System.Security.Claims;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Stock;
using knkwebapi_v2.Services.Territory;
using KnKWebAPI.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using static knkwebapi_v2.Tests.Services.Stock.StockTestDb;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// The P7 routes (PandiO/knk-workspace#60): who may deliver, claim and use the stash, and how results map. The game
/// server claims only for the player it names in X-Acting-User-Id; staff (knk.economy.admin) deliver and use the stash
/// from the web app; the flag off is 409 ProductionDisabled; a reused key 409.
/// </summary>
public class InboxControllersTests : IDisposable
{
    private readonly KnKDbContext _db = Create();

    public void Dispose() => _db.Dispose();

    private ItemDeliveryService Service() => new(_db, new StockService(_db), new ProductionSettingsReader(_db));

    private static HttpContext Web(int userId) =>
        new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("uid", userId.ToString()) }, "Bearer")) };

    private const string Key = "plugin-test-key";

    private static HttpContext GameServer(int? actingUserId)
    {
        var configuration = new Moq.Mock<Microsoft.Extensions.Configuration.IConfiguration>();
        configuration.Setup(c => c["Security:PluginApiKey"]).Returns(Key);
        var services = new Moq.Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(Microsoft.Extensions.Configuration.IConfiguration))).Returns(configuration.Object);
        var http = new DefaultHttpContext { RequestServices = services.Object };
        http.Request.Headers[PluginServiceAuth.ApiKeyHeader] = Key;
        if (actingUserId.HasValue) http.Request.Headers[PluginServiceAuth.ActingUserHeader] = actingUserId.Value.ToString();
        return http;
    }

    private T With<T>(T controller, HttpContext http) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private static PlayerStorageMoveDto Move(string key, long amount) => new()
    {
        IdempotencyKey = key, Lines = new() { new ItemDeliveryLineRequestDto { ItemBlueprintId = OakLog, Amount = amount } },
    };

    [Fact]
    public void TheRoutes_CarryTheirAccessRules()
    {
        var deliveries = typeof(ItemDeliveriesController);
        Assert.Equal(StaffPermissions.EconomyAdmin, deliveries.GetCustomAttribute<RequireServiceOrPermissionForWritesAttribute>()!.Node);
        Assert.Equal("ItemDelivery", deliveries.GetCustomAttribute<DashboardReadsAttribute>()!.TypeName);
        Assert.Contains(StaffPermissions.EconomyAdmin, DashboardAccess.Find("ItemDelivery")!.ReadNodes);

        var inbox = typeof(InboxController);
        Assert.NotNull(inbox.GetMethod("Claim")!.GetCustomAttribute<RequirePluginServiceAttribute>());
        Assert.Equal(StaffPermissions.EconomyAdmin, inbox.GetMethod("Inbox")!.GetCustomAttribute<RequireServiceSelfOrPermissionAttribute>()!.Node);
        foreach (var name in new[] { "Deposit", "Withdraw", "Sweep" })
            Assert.NotNull(inbox.GetMethod(name)!.GetCustomAttribute<RequireServiceOrPermissionAttribute>());
    }

    [Fact]
    public async Task StaffDeliver_FromTheWeb_AndTheGameServerClaims_ForTheNamedPlayerOnly()
    {
        var delivered = await With(new ItemDeliveriesController(Service()), Web(99)).Deliver(new ItemDeliveryRequestDto
        {
            UserId = PlayerId, Reason = ItemDeliveryReason.Gift, IdempotencyKey = "gift-1",
            Lines = new() { new ItemDeliveryLineRequestDto { ItemBlueprintId = OakLog, Amount = 5 } },
        }, default);
        var dto = Assert.IsType<ItemDeliveryDto>(Assert.IsType<OkObjectResult>(delivered).Value);
        Assert.Equal((StockActorKind.User, 99), (dto.ActorKind, dto.ActorUserId!.Value));

        var stranger = await With(new InboxController(Service()), GameServer(4242)).Claim(PlayerId, Move("c1", 5), default);
        Assert.IsType<BadRequestObjectResult>(stranger);
        var none = await With(new InboxController(Service()), GameServer(null)).Claim(PlayerId, Move("c1", 5), default);
        Assert.IsType<BadRequestObjectResult>(none);

        var claimed = await With(new InboxController(Service()), GameServer(PlayerId)).Claim(PlayerId, Move("c1", 2), default);
        var result = Assert.IsType<PlayerStorageMoveResultDto>(Assert.IsType<OkObjectResult>(claimed).Value);
        Assert.Equal((2L, 3L), (result.Result.Accepted, result.View.Entries.Single().Amount));

        var reused = await With(new InboxController(Service()), GameServer(PlayerId)).Claim(PlayerId, Move("c1", 3), default);
        Assert.Equal("IdempotencyKeyReuse", Code(Assert.IsType<ConflictObjectResult>(reused)));
    }

    [Fact]
    public async Task TheStash_NeedsTheNamedPlayer_OrAWebStaffMember()
    {
        var other = await With(new InboxController(Service()), GameServer(4242)).Deposit(PlayerId, Move("d1", 1), default);
        Assert.IsType<BadRequestObjectResult>(other);
        var staff = await With(new InboxController(Service()), Web(99)).Deposit(PlayerId, Move("d1", 1), default);
        Assert.IsType<OkObjectResult>(staff); // Rejected (no stash room without a tier), but answered
    }

    [Fact]
    public async Task WithTheFlagOff_MovesAre409_AndTheViewWorks()
    {
        using var off = Create(productionEnabled: false);
        var service = new ItemDeliveryService(off, new StockService(off), new ProductionSettingsReader(off));
        var deliver = await With(new ItemDeliveriesController(service), Web(99)).Deliver(new ItemDeliveryRequestDto
        {
            UserId = PlayerId, IdempotencyKey = "x", Lines = new() { new ItemDeliveryLineRequestDto { ItemBlueprintId = OakLog, Amount = 1 } },
        }, default);
        Assert.Equal("ProductionDisabled", Code(Assert.IsType<ConflictObjectResult>(deliver)));
        var view = await With(new InboxController(service), Web(PlayerId)).Inbox(PlayerId, default);
        Assert.Null(Assert.IsType<PlayerStorageViewDto>(Assert.IsType<OkObjectResult>(view).Value).StorageId);
        Assert.IsType<NotFoundObjectResult>(await With(new InboxController(service), Web(PlayerId)).Inbox(4242, default));
    }

    private static string? Code(ObjectResult r) => r.Value!.GetType().GetProperty("code")?.GetValue(r.Value)?.ToString();
}
