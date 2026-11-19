using System.Reflection;
using System.Text.Json;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Realtime;
using knkwebapi_v2.Realtime.Contracts;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Map;
using knkwebapi_v2.Tests.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Map;

/// <summary>
/// Overview map L7 (PandiO/knk-workspace#741, OVERVIEW_MAP_DESIGN § 5 rule 11, § 6.2.5, O17, O27; tier:3): the gate
/// command endpoint's rules through the real controller and service. Off by default (404, nothing sent, nothing
/// audited); 409 during a siege match on the gate; 503 without a game server; 400 for a door of another gate; the
/// first game-server result answers; no result within the timeout is 504; every command is audit-logged with the user,
/// the action, the door and the result (A19).
/// </summary>
public sealed class GateCommandServiceTests : IDisposable
{
    private const int Staff = 7;
    private const int Gate = 12;
    private const int OtherGate = 13;
    private const int Door = 21;
    private const int OtherDoor = 31;

    private sealed class RecordingSink : IRealtimeEventSink
    {
        public List<RealtimeEvent> Events { get; } = new();
        public Action<RealtimeEvent>? OnPublish { get; set; }

        public void Publish(RealtimeEvent realtimeEvent)
        {
            Events.Add(realtimeEvent);
            OnPublish?.Invoke(realtimeEvent);
        }
    }

    private sealed record AuditCall(int? Actor, string SubjectType, int SubjectId, AuditAction Action, JsonElement Details);

    private readonly KnKDbContext _db;
    private readonly MapOptions _options = new() { GateCommands = new MapGateCommandsOptions { Enabled = true, TimeoutSeconds = 1 } };
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly List<AuditCall> _audits = new();
    private readonly RecordingSink _sink = new();
    private readonly GateCommandWaiter _waiter = new();
    private int _servers = 1;

    public GateCommandServiceTests()
    {
        _db = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _db.GateStructures.Add(new GateStructure { Id = Gate, Name = "North Gate", Description = "", WgRegionId = "domain_12", WorldName = "world" });
        _db.GateStructures.Add(new GateStructure { Id = OtherGate, Name = "South Gate", Description = "", WgRegionId = "domain_13", WorldName = "world" });
        _db.Set<GateDoor>().Add(new GateDoor { Id = Door, GateStructureId = Gate, Name = "West" });
        _db.Set<GateDoor>().Add(new GateDoor { Id = OtherDoor, GateStructureId = OtherGate, Name = "Main" });
        _db.SaveChanges();
        _users.Setup(u => u.GetByIdAsync(Staff)).ReturnsAsync(new User { Id = Staff, Username = "GateKeeper" });
        _audit.Setup(a => a.RecordForSubjectAsync(It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<AuditAction>(), It.IsAny<string?>()))
            .Callback((int? actor, string type, int id, AuditAction action, string? details) =>
                _audits.Add(new AuditCall(actor, type, id, action, JsonDocument.Parse(details ?? "{}").RootElement.Clone())))
            .Returns(Task.CompletedTask);
    }

    public void Dispose() => _db.Dispose();

    private GateCommandService Service()
    {
        var monitor = new Mock<IOptionsMonitor<MapOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(_options);
        return new GateCommandService(_db, _audit.Object, _users.Object, _waiter, monitor.Object, () => _servers, _sink);
    }

    private GateCommandsController Controller(int? webUser = Staff) => new(Service())
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = webUser.HasValue ? ServiceAuthTestHelper.WebUser(webUser.Value) : new DefaultHttpContext(),
        },
    };

    /// <summary>The game server's side: answers every GateCommand with <paramref name="answer"/>.</summary>
    private void GameServerAnswers(Func<GateCommandPayload, GateCommandResultDto> answer) =>
        _sink.OnPublish = e =>
        {
            var payload = Assert.IsType<GateCommandPayload>(e.Payload);
            Assert.True(_waiter.Complete(answer(payload)));
        };

    private static (int Status, string? Error, object? Value) Status(IActionResult result)
    {
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        var error = objectResult.Value?.GetType().GetProperty("error")?.GetValue(objectResult.Value) as string;
        return (objectResult.StatusCode ?? 200, error, objectResult.Value);
    }

    private static (int, string?) Two(IActionResult result)
    {
        var (status, error, _) = Status(result);
        return (status, error);
    }

    private static GateCommandRequestDto Cmd(string action, int? door = null) => new() { Action = action, DoorId = door };

    // ===== access rule =====

    [Fact]
    public void TheEndpoint_IsAWebOnlyPost_UnderKnkGateAdmin()
    {
        var method = typeof(GateCommandsController).GetMethod(nameof(GateCommandsController.Send))!;
        var rule = Assert.Single(method.GetCustomAttributes<RequirePermissionAttribute>());
        Assert.Equal("knk.gate.admin", Assert.Single(rule.Arguments!));
        Assert.Equal("{id:int}/commands", Assert.Single(method.GetCustomAttributes<HttpPostAttribute>()).Template);
        Assert.Equal("api/GateStructures", typeof(GateCommandsController).GetCustomAttribute<RouteAttribute>()!.Template);
        // Spec § 6.2.5: the plugin executes, it doesn't call; no service-key route.
        Assert.Empty(method.GetCustomAttributes<RequireServiceOrPermissionAttribute>());
        Assert.Empty(method.GetCustomAttributes<RequirePluginServiceAttribute>());
        Assert.Empty(method.GetCustomAttributes<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>());
        Assert.Equal("knk.gate.admin", StaffPermissions.GateAdmin);
    }

    // ===== flag (O27) =====

    [Fact]
    public async Task Off_Is404_SendsNothing_AuditsNothing()
    {
        _options.GateCommands.Enabled = false;
        Assert.Equal((404, "GateCommandsOff"), Two(await Controller().Send(Gate, Cmd("open", Door))));
        Assert.Empty(_sink.Events);
        Assert.Empty(_audits);
    }

    [Fact]
    public void TheFlag_IsOffByDefault()
    {
        Assert.False(new MapOptions().GateCommands.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(5), new MapOptions().GateCommands.EffectiveTimeout);
    }

    // ===== refusals =====

    [Fact]
    public async Task NoLoggedInUser_Is401()
    {
        Assert.Equal(401, Status(await Controller(webUser: null).Send(Gate, Cmd("open"))).Status);
        Assert.Empty(_sink.Events);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("explode")]
    public async Task UnknownAction_Is400(string? action)
    {
        Assert.Equal((400, "ValidationFailed"), Two(await Controller().Send(Gate, Cmd(action!))));
        Assert.Empty(_sink.Events);
    }

    [Fact]
    public async Task UnknownGate_Is404()
    {
        Assert.Equal((404, "GateNotFound"), Two(await Controller().Send(999, Cmd("open"))));
        Assert.Empty(_sink.Events);
    }

    [Theory]
    [InlineData(OtherDoor)] // a door of another gate
    [InlineData(4040)]      // no such door
    public async Task ADoorOfAnotherGate_Is400_AndNothingIsSent(int door)
    {
        Assert.Equal((400, "DoorNotOfGate"), Two(await Controller().Send(Gate, Cmd("open", door))));
        Assert.Empty(_sink.Events);
    }

    [Theory]
    [InlineData(SiegeMatchStatus.Created)]
    [InlineData(SiegeMatchStatus.InProgress)]
    public async Task DuringASiegeMatchOnTheGate_Is409_NothingSent_Audited(SiegeMatchStatus status)
    {
        _db.SiegeMatches.Add(new SiegeMatch { Id = 500, Status = status });
        (await _db.GateStructures.FindAsync(Gate))!.CurrentSiegeId = 500;
        await _db.SaveChangesAsync();

        var (code, error, value) = Status(await Controller().Send(Gate, Cmd("open", Door)));

        Assert.Equal((409, "GateInSiege"), (code, error));
        Assert.Equal("Gate is in a running siege.", value!.GetType().GetProperty("message")!.GetValue(value));
        Assert.Empty(_sink.Events);
        var audit = Assert.Single(_audits);
        Assert.Equal("siege", audit.Details.GetProperty("result").GetString());
    }

    [Fact]
    public async Task AFinishedMatch_DoesNotBlock()
    {
        _db.SiegeMatches.Add(new SiegeMatch { Id = 501, Status = SiegeMatchStatus.Completed });
        (await _db.GateStructures.FindAsync(Gate))!.CurrentSiegeId = 501;
        await _db.SaveChangesAsync();
        GameServerAnswers(p => new GateCommandResultDto { CommandId = p.CommandId, Result = "done" });

        Assert.Equal(200, Status(await Controller().Send(Gate, Cmd("open", Door))).Status);
    }

    [Fact]
    public async Task ASiegeOnAnotherGate_DoesNotBlock()
    {
        _db.SiegeMatches.Add(new SiegeMatch { Id = 502, Status = SiegeMatchStatus.InProgress });
        (await _db.GateStructures.FindAsync(OtherGate))!.CurrentSiegeId = 502;
        await _db.SaveChangesAsync();
        GameServerAnswers(p => new GateCommandResultDto { CommandId = p.CommandId, Result = "done" });

        Assert.Equal(200, Status(await Controller().Send(Gate, Cmd("close"))).Status);
    }

    [Fact]
    public async Task NoGameServer_Is503_NothingSent_Audited()
    {
        _servers = 0;
        Assert.Equal((503, "NoGameServerConnected"), Two(await Controller().Send(Gate, Cmd("repair", Door))));
        Assert.Empty(_sink.Events);
        var audit = Assert.Single(_audits);
        Assert.Equal("no-game-server", audit.Details.GetProperty("result").GetString());
        Assert.Equal(0, _waiter.PendingCount);
    }

    // ===== sending and the result =====

    [Theory]
    [InlineData("open")]
    [InlineData("CLOSE")]
    [InlineData("toggle")]
    [InlineData("repair")]
    public async Task ACommand_IsPublishedToTheGameServers_AndTheResultAnswers(string action)
    {
        GameServerAnswers(p => new GateCommandResultDto
        {
            CommandId = p.CommandId,
            Result = "done",
            Message = "Door West: started",
            Doors = new List<GateCommandDoorStateDto> { new() { DoorId = Door, State = "opening", HealthCurrent = 500, HealthMax = 500, Outcome = "started" } },
        });

        var (code, _, value) = Status(await Controller().Send(Gate, Cmd(action, Door)));

        Assert.Equal(200, code);
        var response = Assert.IsType<GateCommandResponseDto>(value);
        Assert.Equal(("done", action.ToLowerInvariant(), Gate, (int?)Door), (response.Result, response.Action, response.GateId, response.DoorId));
        Assert.Equal("opening", Assert.Single(response.Doors).State);
        var published = Assert.Single(_sink.Events);
        Assert.Equal(GateCommandEventTypes.GateCommand, published.Type);
        Assert.Null(published.ServerId); // every connected game server
        var payload = Assert.IsType<GateCommandPayload>(published.Payload);
        Assert.Equal((response.CommandId, Gate, (int?)Door, action.ToLowerInvariant(), Staff, "GateKeeper"),
            (payload.CommandId, payload.GateId, payload.DoorId, payload.Action, payload.StaffUserId, payload.StaffUsername));
        Assert.Equal(0, _waiter.PendingCount);
    }

    [Fact]
    public async Task WithoutADoor_TheCommandIsForTheWholeGate()
    {
        GameServerAnswers(p => new GateCommandResultDto { CommandId = p.CommandId, Result = "done" });

        await Controller().Send(Gate, Cmd("open"));

        Assert.Null(Assert.IsType<GateCommandPayload>(Assert.Single(_sink.Events).Payload).DoorId);
    }

    [Fact]
    public async Task ARefusalByTheGameServer_Is200_WithResultRefused_AndItsMessage()
    {
        GameServerAnswers(p => new GateCommandResultDto { CommandId = p.CommandId, Result = "refused", Message = "Door West is destroyed and cannot be opened." });

        var (code, _, value) = Status(await Controller().Send(Gate, Cmd("open", Door)));

        Assert.Equal(200, code);
        var response = Assert.IsType<GateCommandResponseDto>(value);
        Assert.Equal(("refused", "Door West is destroyed and cannot be opened."), (response.Result, response.Message));
    }

    [Fact]
    public async Task ASiegeSeenByTheGameServer_Is409()
    {
        GameServerAnswers(p => new GateCommandResultDto { CommandId = p.CommandId, Result = "siege", Message = "Gate is in a running siege." });

        Assert.Equal(409, Status(await Controller().Send(Gate, Cmd("open"))).Status);
        Assert.Equal("siege", Assert.Single(_audits).Details.GetProperty("result").GetString());
    }

    [Fact]
    public async Task AnUnknownResultWord_IsReportedAsRefused()
    {
        GameServerAnswers(p => new GateCommandResultDto { CommandId = p.CommandId, Result = "exploded" });

        var response = Assert.IsType<GateCommandResponseDto>(Status(await Controller().Send(Gate, Cmd("open"))).Value);
        Assert.Equal("refused", response.Result);
    }

    [Fact]
    public async Task NoResult_TimesOutWith504_AndALateResultIsIgnored()
    {
        string? commandId = null;
        _sink.OnPublish = e => commandId = ((GateCommandPayload)e.Payload!).CommandId;
        var started = DateTime.UtcNow;

        var (code, _, value) = Status(await Controller().Send(Gate, Cmd("toggle", Door)));

        Assert.Equal(504, code);
        Assert.Equal("timeout", Assert.IsType<GateCommandResponseDto>(value).Result);
        Assert.True(DateTime.UtcNow - started >= TimeSpan.FromSeconds(0.9));
        Assert.Equal(0, _waiter.PendingCount);
        Assert.False(_waiter.Complete(new GateCommandResultDto { CommandId = commandId!, Result = "done" }));
        Assert.Equal("timeout", Assert.Single(_audits).Details.GetProperty("result").GetString());
    }

    // ===== audit (A19) =====

    [Fact]
    public async Task EveryCommand_IsAuditLogged_WithUserActionDoorAndResult()
    {
        GameServerAnswers(p => new GateCommandResultDto { CommandId = p.CommandId, Result = "done", Message = "ok" });

        var response = Assert.IsType<GateCommandResponseDto>(Status(await Controller().Send(Gate, Cmd("repair", Door))).Value);
        await Controller().Send(Gate, Cmd("close"));

        Assert.Equal(2, _audits.Count);
        var first = _audits[0];
        Assert.Equal((Staff, AuditSubjectTypes.GateStructure, Gate, AuditAction.GateCommandSent),
            (first.Actor!.Value, first.SubjectType, first.SubjectId, first.Action));
        Assert.Equal(response.CommandId, first.Details.GetProperty("commandId").GetString());
        Assert.Equal("repair", first.Details.GetProperty("action").GetString());
        Assert.Equal(Door, first.Details.GetProperty("doorId").GetInt32());
        Assert.Equal("done", first.Details.GetProperty("result").GetString());
        Assert.Equal("ok", first.Details.GetProperty("message").GetString());
        Assert.Equal(1, first.Details.GetProperty("gameServers").GetInt32());
        Assert.False(_audits[1].Details.TryGetProperty("doorId", out _)); // the whole gate
    }

    [Fact]
    public void TheAuditAction_FitsTheColumn()
    {
        Assert.True(nameof(AuditAction.GateCommandSent).Length <= 32);
        Assert.Equal(65, (int)AuditAction.GateCommandSent);
    }
}
