using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// PandiO/knk-workspace#261: "Grant" on the web app's player profile wrote the KitClaim but nothing
/// ever placed the items, because only the game server's own flows (/kit get|give, the kits menu)
/// place them. A grant from outside the game server is now a pending delivery: the claim row carries
/// DeliveryRequestedAt, a KitDeliveryPending notification nudges the game server, and the game
/// server takes it with one conditional update before placing, so it is handed over once.
/// </summary>
public class KitGrantDeliveryTests
{
    private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IKitRepository> _kitRepo = new();
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<IAuditLogService> _auditLog = new();
    private readonly InMemoryPlayerNotificationQueue _notifications = new(TimeSpan.FromHours(24), 1000, () => Now);
    private readonly AutoMapper.IMapper _mapper;
    private readonly KitService _service;

    private readonly User _staff = new() { Id = 30, Username = "staff" };
    private readonly User _player = new() { Id = 31, Username = "player", Uuid = "6f1c0b3e-2a4d-4e0a-9d0b-1b2c3d4e5f60" };
    private readonly Kit _kit = new()
    {
        Id = 10,
        Name = "Knight",
        HelmetId = 5,
        Contents = new List<KitContent> { new() { SlotIndex = 2, ItemBlueprintId = 7, Quantity = 3 } }
    };

    public KitGrantDeliveryTests()
    {
        _mapper = new AutoMapper.MapperConfiguration(cfg =>
        {
            cfg.AddProfile<knkwebapi_v2.Mapping.KitProfile>();
            cfg.AddProfile<knkwebapi_v2.Mapping.ItemBlueprintMappingProfile>();
            cfg.AddProfile<knkwebapi_v2.Mapping.PermissionMappingProfile>();
        }).CreateMapper();

        _service = new KitService(
            _kitRepo.Object,
            _userRepo.Object,
            new Mock<IItemBlueprintRepository>().Object,
            new Mock<ITitleBracketRepository>().Object,
            new Mock<IPermissionGroupRepository>().Object,
            new Mock<ITitleService>().Object,
            new Mock<IUserPermissionGroupService>().Object,
            new Mock<IPermissionResolutionService>().Object,
            _auditLog.Object,
            _mapper,
            new FakeCurrencyService(id => _userRepo.Object.GetByIdAsync(id).Result),
            lootboxTokenGrants: null,
            notifications: _notifications,
            utcNow: () => Now);

        _userRepo.Setup(r => r.GetByIdAsync(_staff.Id)).ReturnsAsync(_staff);
        _userRepo.Setup(r => r.GetByIdAsync(_player.Id)).ReturnsAsync(_player);
        _kitRepo.Setup(r => r.GetByIdAsync(_kit.Id)).ReturnsAsync(_kit);
        _kitRepo.Setup(r => r.AddClaimAsync(It.IsAny<KitClaim>()))
            .ReturnsAsync((KitClaim c) => { c.Id = 501; return c; });
    }

    // ===== GiveKitAsync =====

    [Fact]
    public async Task WebGrant_IsAPendingDelivery_AndNudgesTheGameServer()
    {
        var result = await _service.GiveKitAsync(_staff.Id, _player.Id, _kit.Id, queueDelivery: true);

        _kitRepo.Verify(r => r.AddClaimAsync(It.Is<KitClaim>(c =>
            c.UserId == _player.Id && c.KitId == _kit.Id && c.DeliveryRequestedAt == Now && c.DeliveredAt == null)), Times.Once);
        Assert.True(result.DeliveryPending);
        Assert.Equal(501, result.ClaimId);

        var nudge = Assert.Single(_notifications.GetPending());
        Assert.Equal(PlayerNotificationTypes.KitDeliveryPending, nudge.Type);
        Assert.Equal(_player.Id, nudge.UserId);
        Assert.Equal(_player.Uuid, nudge.Uuid);
        Assert.Equal(_player.Username, nudge.Username);
    }

    [Fact]
    public async Task GameServerGive_PlacesItself_SoNoDeliveryIsQueued()
    {
        var result = await _service.GiveKitAsync(_staff.Id, _player.Id, _kit.Id);

        _kitRepo.Verify(r => r.AddClaimAsync(It.Is<KitClaim>(c => c.DeliveryRequestedAt == null)), Times.Once);
        Assert.False(result.DeliveryPending);
        Assert.Empty(_notifications.GetPending());
    }

    [Fact]
    public async Task WebGrant_AuditEntrySaysTheDeliveryIsPending()
    {
        await _service.GiveKitAsync(_staff.Id, _player.Id, _kit.Id, queueDelivery: true);

        _auditLog.Verify(s => s.RecordAsync(_staff.Id, _player.Id, AuditAction.KitGranted,
            It.Is<string?>(d => d != null && d.Contains("\"claimId\":501") && d.Contains("\"deliveryPending\":true"))), Times.Once);
    }

    // ===== GetPendingDeliveriesAsync =====

    [Fact]
    public async Task PendingDeliveries_CarryTheLoadoutToPlace()
    {
        _kitRepo.Setup(r => r.GetPendingDeliveriesAsync(_player.Id)).ReturnsAsync(new List<KitClaim>
        {
            new() { Id = 501, KitId = _kit.Id, Kit = _kit, UserId = _player.Id, ClaimedAt = Now, DeliveryRequestedAt = Now }
        });

        var pending = Assert.Single(await _service.GetPendingDeliveriesAsync(_player.Id));

        Assert.Equal(501, pending.ClaimId);
        Assert.Equal("Knight", pending.KitName);
        Assert.Equal(Now, pending.GrantedAt);
        Assert.Equal(_kit.Id, pending.Loadout.KitId);
        Assert.Equal(5, pending.Loadout.HelmetId);
        var slot = Assert.Single(pending.Loadout.Contents);
        Assert.Equal((2, 7, 3), (slot.SlotIndex, slot.ItemBlueprintId, slot.Quantity));
        Assert.Equal(501, pending.Loadout.ClaimId);
    }

    // ===== MarkDeliveredAsync =====

    [Fact]
    public async Task MarkDelivered_TheCallThatTakesIt_Delivers()
    {
        _kitRepo.Setup(r => r.TryMarkDeliveredAsync(501, _player.Id, Now)).ReturnsAsync(true);

        Assert.Equal(KitDeliveryMarkResult.Delivered, await _service.MarkDeliveredAsync(_player.Id, 501));
    }

    [Fact]
    public async Task MarkDelivered_TakenBefore_IsAlreadyDelivered()
    {
        _kitRepo.Setup(r => r.TryMarkDeliveredAsync(501, _player.Id, Now)).ReturnsAsync(false);
        _kitRepo.Setup(r => r.GetClaimAsync(501)).ReturnsAsync(new KitClaim
        {
            Id = 501, UserId = _player.Id, KitId = _kit.Id, DeliveryRequestedAt = Now, DeliveredAt = Now
        });

        Assert.Equal(KitDeliveryMarkResult.AlreadyDelivered, await _service.MarkDeliveredAsync(_player.Id, 501));
    }

    [Theory]
    [InlineData(false, 31)] // a claim the game server placed itself: never a delivery
    [InlineData(true, 99)]  // someone else's claim
    public async Task MarkDelivered_NothingToTake_IsNotFound(bool requested, int owner)
    {
        _kitRepo.Setup(r => r.TryMarkDeliveredAsync(501, _player.Id, Now)).ReturnsAsync(false);
        _kitRepo.Setup(r => r.GetClaimAsync(501)).ReturnsAsync(new KitClaim
        {
            Id = 501, UserId = owner, KitId = _kit.Id, DeliveryRequestedAt = requested ? Now : null, DeliveredAt = requested ? Now : null
        });

        Assert.Equal(KitDeliveryMarkResult.NotFound, await _service.MarkDeliveredAsync(_player.Id, 501));
    }

    // ===== KitsController: who places the items =====

    [Fact]
    public async Task Controller_WebUser_QueuesTheDelivery()
    {
        var (controller, kits) = Controller(Http(user: LoggedIn(_staff.Id), configuredKey: "secret"));

        await controller.Give(_kit.Id, new GiveKitRequestDto { TargetUserId = _player.Id });

        kits.Verify(s => s.GiveKitAsync(_staff.Id, _player.Id, _kit.Id, true), Times.Once);
    }

    [Fact]
    public async Task Controller_WebUserUnderTheDevelopmentBypass_StillQueuesTheDelivery()
    {
        var (controller, kits) = Controller(Http(user: LoggedIn(_staff.Id), development: true, allowUnauthenticated: true));

        await controller.Give(_kit.Id, new GiveKitRequestDto { TargetUserId = _player.Id });

        kits.Verify(s => s.GiveKitAsync(_staff.Id, _player.Id, _kit.Id, true), Times.Once);
    }

    [Fact]
    public async Task Controller_GameServer_PlacesItself()
    {
        var (controller, kits) = Controller(Http(sentKey: "secret", configuredKey: "secret", actingUserId: "30"));

        await controller.Give(_kit.Id, new GiveKitRequestDto { TargetUserId = _player.Id });

        kits.Verify(s => s.GiveKitAsync(30, _player.Id, _kit.Id, false), Times.Once);
    }

    [Fact]
    public async Task Controller_MarkDelivered_MapsTheOutcome()
    {
        var kits = new Mock<IKitService>();
        kits.Setup(s => s.MarkDeliveredAsync(31, 1)).ReturnsAsync(KitDeliveryMarkResult.Delivered);
        kits.Setup(s => s.MarkDeliveredAsync(31, 2)).ReturnsAsync(KitDeliveryMarkResult.AlreadyDelivered);
        kits.Setup(s => s.MarkDeliveredAsync(31, 3)).ReturnsAsync(KitDeliveryMarkResult.NotFound);
        var controller = new KitsController(kits.Object);

        var ok = Assert.IsType<OkObjectResult>(await controller.MarkDelivered(1, 31));
        Assert.True(Assert.IsType<KitDeliveryMarkResultDto>(ok.Value).Delivered);
        Assert.IsType<ConflictObjectResult>(await controller.MarkDelivered(2, 31));
        Assert.IsType<NotFoundObjectResult>(await controller.MarkDelivered(3, 31));
    }

    private static (KitsController, Mock<IKitService>) Controller(HttpContext http)
    {
        var kits = new Mock<IKitService>();
        kits.Setup(s => s.GiveKitAsync(It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(new KitClaimResultDto());
        var controller = new KitsController(kits.Object) { ControllerContext = new ControllerContext { HttpContext = http } };
        return (controller, kits);
    }

    private static HttpContext Http(ClaimsPrincipal? user = null, string? sentKey = null, string? configuredKey = null,
        bool development = false, bool allowUnauthenticated = false, string? actingUserId = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [PluginServiceAuth.ApiKeyConfigKey] = configuredKey,
            [PluginServiceAuth.AllowUnauthenticatedConfigKey] = allowUnauthenticated ? "true" : "false"
        }).Build();
        var environment = new Mock<IHostEnvironment>();
        environment.Setup(e => e.EnvironmentName).Returns(development ? Environments.Development : Environments.Production);
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IConfiguration))).Returns(configuration);
        services.Setup(s => s.GetService(typeof(IHostEnvironment))).Returns(environment.Object);

        var http = new DefaultHttpContext { RequestServices = services.Object };
        if (user != null) http.User = user;
        if (sentKey != null) http.Request.Headers[PluginServiceAuth.ApiKeyHeader] = sentKey;
        if (actingUserId != null) http.Request.Headers[PluginServiceAuth.ActingUserHeader] = actingUserId;
        return http;
    }

    private static ClaimsPrincipal LoggedIn(int userId) =>
        new(new ClaimsIdentity(new[] { new Claim("uid", userId.ToString()) }, "Bearer"));
}
