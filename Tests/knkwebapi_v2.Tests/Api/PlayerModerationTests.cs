using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Realtime;
using knkwebapi_v2.Realtime.Contracts;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// PandiO/knk-workspace#565 (developer decisions 2026-11-15): kick and ban from the web app. Each endpoint needs its own
/// node (knk.kick / knk.ban), refuses a missing account, a web-only account, oneself and a player ranked at or above the
/// caller, refuses a kick of an offline player and any action while no game server is connected, audits the action and
/// publishes PlayerKick / PlayerBan with the contract payload to every game server.
/// </summary>
public class PlayerModerationTests
{
    private const int Staff = 7;
    private const int Target = 42;
    private const string Uuid = "069a79f4-44e9-4726-a5be-fca90e38aaf5";
    private static readonly DateTime Now = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

    private sealed class RecordingSink : IRealtimeEventSink
    {
        public List<RealtimeEvent> Events { get; } = new();
        public void Publish(RealtimeEvent realtimeEvent) => Events.Add(realtimeEvent);
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<IPermissionEscalationGuard> _guard = new();
    private readonly RecordingSink _sink = new();
    private int _servers = 1;
    private readonly User _target = new() { Id = Target, Username = "Griefer", Uuid = Uuid, IsOnline = true };

    public PlayerModerationTests()
    {
        _users.Setup(u => u.GetByIdAsync(Target)).ReturnsAsync(() => _target);
        _users.Setup(u => u.GetByIdAsync(Staff)).ReturnsAsync(new User { Id = Staff, Username = "Mod" });
        _guard.Setup(g => g.CanModerateUserAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(EscalationCheck.Ok);
    }

    private PlayerModerationService Service() =>
        new(_users.Object, _audit.Object, _guard.Object, () => _servers, _sink, new FixedTime());

    private PlayerModerationController Controller(int? webUser = Staff)
    {
        var controller = new PlayerModerationController(Service());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = webUser.HasValue ? ServiceAuthTestHelper.WebUser(webUser.Value) : new DefaultHttpContext()
        };
        return controller;
    }

    private static (int Status, string? Error) Status(IActionResult result)
    {
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        var error = objectResult.Value?.GetType().GetProperty("error")?.GetValue(objectResult.Value) as string;
        return (objectResult.StatusCode ?? 200, error);
    }

    // ===== access rules =====

    [Theory]
    [InlineData(nameof(PlayerModerationController.Kick), "knk.kick", "{id:int}/kick")]
    [InlineData(nameof(PlayerModerationController.Ban), "knk.ban", "{id:int}/ban")]
    public void EachEndpoint_NeedsItsOwnNode_AndIsAWebOnlyPost(string action, string node, string route)
    {
        var method = typeof(PlayerModerationController).GetMethod(action)!;
        var rule = Assert.Single(method.GetCustomAttributes<RequirePermissionAttribute>());
        Assert.Equal(node, Assert.Single(rule.Arguments!));
        Assert.Equal(route, Assert.Single(method.GetCustomAttributes<HttpPostAttribute>()).Template);
        Assert.Equal("api/Users", typeof(PlayerModerationController).GetCustomAttribute<RouteAttribute>()!.Template);
        // Web staff only: the game server has its own /kick and /ban.
        Assert.Empty(method.GetCustomAttributes<RequireServiceOrPermissionAttribute>());
        Assert.Empty(method.GetCustomAttributes<RequirePluginServiceAttribute>());
    }

    [Fact]
    public void TheNodes_AreTheDevelopersNames()
    {
        Assert.Equal("knk.kick", StaffPermissions.Kick);
        Assert.Equal("knk.ban", StaffPermissions.Ban);
    }

    // ===== refusals =====

    [Fact]
    public async Task NoLoggedInUser_Is401()
    {
        Assert.Equal(401, Status(await Controller(webUser: null).Ban(Target, new PlayerModerationRequestDto { Reason = "x" })).Status);
        Assert.Empty(_sink.Events);
    }

    [Fact]
    public async Task UnknownUser_Is404()
    {
        Assert.Equal((404, "UserNotFound"), Status(await Controller().Kick(999, null)));
    }

    [Fact]
    public async Task Self_Is400()
    {
        _users.Setup(u => u.GetByIdAsync(Staff)).ReturnsAsync(new User { Id = Staff, Username = "Mod", Uuid = Uuid, IsOnline = true });
        Assert.Equal((400, "CannotModerateSelf"), Status(await Controller().Kick(Staff, null)));
        Assert.Equal((400, "CannotModerateSelf"), Status(await Controller().Ban(Staff, new PlayerModerationRequestDto { Reason = "x" })));
        Assert.Empty(_sink.Events);
    }

    [Fact]
    public async Task NoMinecraftAccount_Is400()
    {
        _target.Uuid = null;
        Assert.Equal((400, "NoMinecraftAccount"), Status(await Controller().Ban(Target, new PlayerModerationRequestDto { Reason = "x" })));
        Assert.Empty(_sink.Events);
    }

    [Fact]
    public async Task ATargetRankedAtOrAboveTheCaller_Is403_EscalationRefused()
    {
        _guard.Setup(g => g.CanModerateUserAsync(Staff, Target))
            .ReturnsAsync(EscalationCheck.Refused(PermissionEscalationGuard.ModerateRankMessage));
        Assert.Equal((403, "EscalationRefused"), Status(await Controller().Kick(Target, null)));
        Assert.Equal((403, "EscalationRefused"), Status(await Controller().Ban(Target, new PlayerModerationRequestDto { Reason = "x" })));
        Assert.Empty(_sink.Events);
        _audit.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Ban_WithoutAReason_Is400(string? reason)
    {
        Assert.Equal((400, "ValidationFailed"), Status(await Controller().Ban(Target, new PlayerModerationRequestDto { Reason = reason })));
        Assert.Equal((400, "ValidationFailed"), Status(await Controller().Ban(Target, null)));
        Assert.Empty(_sink.Events);
    }

    [Fact]
    public async Task AReasonOver200Characters_Is400()
    {
        var reason = new string('x', PlayerModerationService.MaxReasonLength + 1);
        Assert.Equal((400, "ValidationFailed"), Status(await Controller().Kick(Target, new PlayerModerationRequestDto { Reason = reason })));
    }

    [Fact]
    public async Task Kick_OfAnOfflinePlayer_Is409_AndNothingIsAudited()
    {
        _target.IsOnline = false;
        Assert.Equal((409, "PlayerNotOnline"), Status(await Controller().Kick(Target, null)));
        Assert.Empty(_sink.Events);
        _audit.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Ban_OfAnOfflinePlayer_IsAccepted_AndSentToTheGameServers()
    {
        _target.IsOnline = false;
        Assert.Equal(202, Status(await Controller().Ban(Target, new PlayerModerationRequestDto { Reason = "Alt account" })).Status);
        Assert.Equal(RealtimeEventTypes.PlayerBan, Assert.Single(_sink.Events).Type);
    }

    [Fact]
    public async Task NoGameServerConnected_Is503_AndNothingIsAudited()
    {
        _servers = 0;
        Assert.Equal((503, "NoGameServerConnected"), Status(await Controller().Ban(Target, new PlayerModerationRequestDto { Reason = "x" })));
        Assert.Equal((503, "NoGameServerConnected"), Status(await Controller().Kick(Target, null)));
        Assert.Empty(_sink.Events);
        _audit.VerifyNoOtherCalls();
    }

    // ===== audit and realtime =====

    [Fact]
    public async Task Kick_AuditsPlayerKicked_AndPublishesPlayerKick_WithTheContractPayload()
    {
        var result = Assert.IsType<ObjectResult>(await Controller().Kick(Target, new PlayerModerationRequestDto { Reason = "  Spamming chat  " }));
        Assert.Equal(202, result.StatusCode);
        var dto = Assert.IsType<PlayerModerationResultDto>(result.Value);
        Assert.Equal((Target, 1), (dto.UserId, dto.GameServers));

        var sent = Assert.Single(_sink.Events);
        Assert.Equal(RealtimeEventTypes.PlayerKick, sent.Type);
        Assert.Equal(1, sent.V);
        Assert.Null(sent.ServerId); // every game server
        Assert.Equal(Staff, sent.ActorUserId);
        Assert.Equal(new PlayerModerationPayload(dto.ActionId, Target, Uuid, "Griefer", "Spamming chat", Staff, "Mod", Now), sent.Payload);
        Assert.True(Guid.TryParse(dto.ActionId, out _));

        _audit.Verify(a => a.RecordAsync(Staff, Target, AuditAction.PlayerKicked,
            It.Is<string>(d => d.Contains("\"reason\":\"Spamming chat\"") && d.Contains($"\"actionId\":\"{dto.ActionId}\"")
                && d.Contains($"\"uuid\":\"{Uuid}\"") && d.Contains("\"gameServers\":1"))), Times.Once);
    }

    [Fact]
    public async Task Kick_WithoutAReason_SendsANullReason()
    {
        await Controller().Kick(Target, null);
        Assert.Null(((PlayerModerationPayload)Assert.Single(_sink.Events).Payload!).Reason);
    }

    [Fact]
    public async Task Ban_AuditsPlayerBanned_Permanent_AndPublishesPlayerBan()
    {
        var result = Assert.IsType<ObjectResult>(await Controller().Ban(Target, new PlayerModerationRequestDto { Reason = "X-ray" }));
        var dto = Assert.IsType<PlayerModerationResultDto>(result.Value);

        var sent = Assert.Single(_sink.Events);
        Assert.Equal(RealtimeEventTypes.PlayerBan, sent.Type);
        Assert.Equal(new PlayerModerationPayload(dto.ActionId, Target, Uuid, "Griefer", "X-ray", Staff, "Mod", Now), sent.Payload);
        _audit.Verify(a => a.RecordAsync(Staff, Target, AuditAction.PlayerBanned,
            It.Is<string>(d => d.Contains("\"reason\":\"X-ray\"") && d.Contains("\"permanent\":true"))), Times.Once);
    }

    [Fact]
    public void ThePayload_SerializesWithTheContractFieldNames()
    {
        var payload = new PlayerModerationPayload("a1", Target, Uuid, "Griefer", "X-ray", Staff, "Mod", Now);
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("{\"actionId\":\"a1\",\"userId\":42,\"uuid\":\"" + Uuid + "\",\"username\":\"Griefer\",\"reason\":\"X-ray\","
            + "\"staffUserId\":7,\"staffUsername\":\"Mod\",\"at\":\"2026-11-15T12:00:00Z\"}", json);
        Assert.Equal("PlayerKick", RealtimeEventTypes.PlayerKick);
        Assert.Equal("PlayerBan", RealtimeEventTypes.PlayerBan);
        Assert.Equal(63, (int)AuditAction.PlayerKicked);
        Assert.Equal(64, (int)AuditAction.PlayerBanned);
    }

    [Fact]
    public async Task ThroughTheRealSink_TheEventGoesToEveryGameServer_AsWeb()
    {
        var publisher = new Mock<IRealtimePublisher>();
        var http = new HttpContextAccessor { HttpContext = ServiceAuthTestHelper.WebUser(Staff) };
        var sink = new AfterCommitRealtimeEventSink(publisher.Object, new RealtimeTransactionEvents(), null, http);
        var service = new PlayerModerationService(_users.Object, _audit.Object, _guard.Object, () => 2, sink, new FixedTime());

        await service.BanAsync(Target, "X-ray", Staff);

        publisher.Verify(p => p.PublishToGameServers(RealtimeEventTypes.PlayerBan, It.IsAny<PlayerModerationPayload>(), null,
            It.Is<RealtimeActor>(a => a.UserId == Staff && a.Source == "web"), 1), Times.Once);
    }

    [Fact]
    public void TheConnectionCount_IsTheGameHubs()
    {
        var connections = new RealtimeConnections();
        connections.Add(new RealtimeConnection("web-1", RealtimeHubKind.Web, Array.Empty<string>(), userId: Staff));
        var service = new PlayerModerationService(_users.Object, _audit.Object, _guard.Object, connections, _sink, new FixedTime());
        var refused = Assert.ThrowsAsync<PlayerModerationException>(() => service.BanAsync(Target, "x", Staff)).Result;
        Assert.Equal(PlayerModerationRefusal.NoGameServer, refused.Refusal);

        connections.Add(new RealtimeConnection("game-1", RealtimeHubKind.Game, new[] { RealtimeGroups.Servers }, serverId: "survival"));
        Assert.Equal(1, service.BanAsync(Target, "x", Staff).Result.GameServers);
    }
}

/// <summary>#565: the rank rule of kick and ban (<see cref="PermissionEscalationGuard.CanModerateUserAsync"/>).</summary>
public class ModerateRankGuardTests
{
    private const int Owner = 1, Admin = 2, Moderator = 3, Peer = 4, Player = 5, Groupless = 6;

    private readonly Dictionary<int, string[]> _held = new() { [Owner] = new[] { "*" } };
    private readonly Dictionary<int, int[]> _weights = new()
    {
        [Admin] = new[] { 80 }, [Moderator] = new[] { 50, 10 }, [Peer] = new[] { 50 }, [Player] = new[] { 0 }, [Owner] = new[] { 100 },
    };
    private readonly PermissionEscalationGuard _guard;

    public ModerateRankGuardTests()
    {
        var permissions = new Mock<IPermissionResolutionService>();
        permissions.Setup(p => p.CheckAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync((int userId, string node) => new PermissionCheckResponseDto
            {
                UserId = userId, Node = node,
                Result = _held.GetValueOrDefault(userId, Array.Empty<string>()).Contains(node)
                    ? PermissionResolutionResult.Granted : PermissionResolutionResult.Undeclared
            });
        var memberships = new Mock<IUserPermissionGroupRepository>();
        memberships.Setup(m => m.GetByUserAsync(It.IsAny<int>())).ReturnsAsync((int userId) =>
            _weights.GetValueOrDefault(userId, Array.Empty<int>())
                .Select((w, i) => new UserPermissionGroup { UserId = userId, PermissionGroupId = 900 + i, PermissionGroup = new PermissionGroup { Id = 900 + i, Weight = w } })
                .ToList());
        _guard = new PermissionEscalationGuard(permissions.Object, new Mock<IPermissionGroupRepository>().Object, memberships.Object);
    }

    [Theory]
    [InlineData(Moderator, Player, true)]
    [InlineData(Moderator, Groupless, true)]
    [InlineData(Moderator, Peer, false)]      // same rank
    [InlineData(Moderator, Admin, false)]     // above
    [InlineData(Admin, Moderator, true)]
    [InlineData(Admin, Owner, false)]         // holds *
    [InlineData(Owner, Admin, true)]          // * acts on anyone
    [InlineData(Groupless, Player, false)]    // no rank, the target has one (Weight 0)
    public async Task OnlyPlayersRankedBelowTheCaller(int actor, int target, bool allowed)
    {
        var check = await _guard.CanModerateUserAsync(actor, target);
        Assert.Equal(allowed, check.Allowed);
        if (!allowed) Assert.Equal(PermissionEscalationGuard.ModerateRankMessage, check.Message);
    }
}
