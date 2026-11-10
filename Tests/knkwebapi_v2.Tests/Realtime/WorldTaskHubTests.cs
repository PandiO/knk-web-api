using Xunit;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Realtime;
using knkwebapi_v2.Realtime.Contracts;
using knkwebapi_v2.Realtime.Hubs;
using knkwebapi_v2.Realtime.Presence;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Tests.Services.Email;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace knkwebapi_v2.Tests.Realtime;

/// <summary>
/// PandiO/knk-workspace#181 (WT2): the world-task parts of the hubs end to end on a test server (same wiring as
/// <see cref="RealtimeHubTests"/>): <c>task:{id}</c> joins with the ownership check (DESIGN §7.3), <c>WorldTaskUpdated</c>
/// once per connection, <c>WorldTaskOffered</c> to the player's server, <c>WorldTaskOfferResult</c> and the re-offer on join.
/// Plus the plain pieces: connection groups, the web-group publish and <see cref="WorldTaskRealtime"/>'s events.
/// </summary>
public sealed class WorldTaskHubTests : IAsyncLifetime
{
    private const string PluginKey = "realtime-test-plugin-key";
    private const string JwtSecret = "realtime-test-jwt-secret-0123456789abcdef-0123";
    private const string Steve = "00000000-0000-0000-0000-00000000000a";

    private readonly ManualClock _clock = new(new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc));
    private readonly string _dbName = "world-task-hub-" + Guid.NewGuid();
    private readonly Mock<IWorldTaskService> _tasks = new();
    private readonly Mock<IPermissionResolutionService> _permissions = new();
    private WebApplication _app = null!;
    private TestServer _server = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Configuration["Security:PluginApiKey"] = PluginKey;
        builder.Configuration["Realtime:PresenceSweepSeconds"] = "3600";
        builder.Services.AddSingleton<TimeProvider>(_clock);
        builder.Services.AddDbContext<KnKDbContext>(o => o.UseInMemoryDatabase(_dbName));
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)),
                ValidateIssuer = false,
                ValidateAudience = false,
            };
            o.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var token = RealtimeSetup.ReadHubAccessToken(context.Request);
                    if (token != null) context.Token = token;
                    return Task.CompletedTask;
                }
            };
        });
        builder.Services.AddKnkRealtime(builder.Configuration);
        builder.Services.AddScoped<IWorldTaskRepository, WorldTaskRepository>();
        builder.Services.AddSingleton(_tasks.Object);
        builder.Services.AddSingleton(_permissions.Object);
        _app = builder.Build();
        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapKnkRealtimeHubs();
        await _app.StartAsync();
        _server = _app.GetTestServer();
    }

    public async Task DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private HubConnection GameConnection(string serverId = "survival-1") =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(_server.BaseAddress, "api/hubs/game"), o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => _server.CreateHandler();
                o.Headers["X-API-Key"] = PluginKey;
                o.Headers[GameServerHub.ServerIdHeader] = serverId;
            })
            .Build();

    private HubConnection WebConnection(int userId) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(_server.BaseAddress, "api/hubs/web"), o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => _server.CreateHandler();
                o.AccessTokenProvider = () => Task.FromResult<string?>(Jwt(userId));
            })
            .Build();

    private static string Jwt(int userId)
    {
        var token = new JwtSecurityToken(
            claims: new[] { new Claim("uid", userId.ToString()) },
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<WorldTask> AddTaskAsync(int createdBy)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KnKDbContext>();
        var session = new WorkflowSession { UserId = createdBy, EntityTypeName = "Town", CreatedAt = _clock.UtcNow };
        var task = new WorldTask
        {
            WorkflowSession = session, TaskType = "LocationSelection", FieldName = "Location", CreatedByUserId = createdBy,
            CreatedAt = _clock.UtcNow, LinkCode = "ABC123"
        };
        db.WorldTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private void Publish(Action<IWorldTaskRealtime> publish)
    {
        using var scope = _app.Services.CreateScope();
        publish(scope.ServiceProvider.GetRequiredService<IWorldTaskRealtime>());
    }

    private static async Task Eventually(Func<bool> condition, string what)
    {
        for (var i = 0; i < 200; i++)
        {
            if (condition()) return;
            await Task.Delay(25);
        }
        Assert.Fail("Timed out waiting for " + what);
    }

    // ===== web hub =====

    [Fact]
    public async Task JoinTask_OwnTask_ReceivesWorldTaskUpdated_OncePerConnection()
    {
        var task = await AddTaskAsync(createdBy: 42);
        await using var connection = WebConnection(42);
        var received = new List<JsonElement>();
        connection.On<JsonElement>(WorldTaskEventTypes.WorldTaskUpdated, e => { lock (received) received.Add(e); });
        await connection.StartAsync();

        Assert.True(await connection.InvokeAsync<bool>("JoinTask", task.Id));
        // The creator is in user:42 and task:{id}: the update still arrives once.
        task.Status = WorldTaskStatuses.InProgress;
        task.ClaimedByMinecraftUsername = "Steve";
        Publish(r => r.Changed(task));
        await Eventually(() => { lock (received) return received.Count == 1; }, "WorldTaskUpdated");
        await Task.Delay(200);

        JsonElement envelope;
        lock (received) envelope = Assert.Single(received);
        var payload = envelope.GetProperty("payload");
        Assert.Equal(task.Id, payload.GetProperty("taskId").GetInt32());
        Assert.Equal("InProgress", payload.GetProperty("status").GetString());
        Assert.Equal("Steve", payload.GetProperty("claimedByMinecraftUsername").GetString());
        Assert.False(payload.TryGetProperty("linkCode", out _)); // never the task code
    }

    [Fact]
    public async Task JoinTask_SomeoneElsesTask_IsRefused_UnlessTheyHoldKnkAdminWorld()
    {
        var task = await AddTaskAsync(createdBy: 42);
        _permissions.Setup(p => p.CheckAsync(8, "knk.admin.world")).ReturnsAsync(new PermissionCheckResponseDto { Result = PermissionResolutionResult.Granted });
        await using var stranger = WebConnection(7);
        await using var staff = WebConnection(8);
        await stranger.StartAsync();
        await staff.StartAsync();

        Assert.False(await stranger.InvokeAsync<bool>("JoinTask", task.Id));
        Assert.False(await stranger.InvokeAsync<bool>("JoinTask", 99999));
        Assert.True(await staff.InvokeAsync<bool>("JoinTask", task.Id));

        var connections = _app.Services.GetRequiredService<RealtimeConnections>();
        Assert.Single(connections.InGroup(RealtimeHubKind.Web, RealtimeGroups.Task(task.Id)));
        await staff.InvokeAsync("LeaveTask", task.Id);
        Assert.Empty(connections.InGroup(RealtimeHubKind.Web, RealtimeGroups.Task(task.Id)));
    }

    // ===== game hub =====

    [Fact]
    public async Task Offer_ReachesThePlayersServer_AndTheAnswerIsRecorded()
    {
        var task = await AddTaskAsync(createdBy: 42);
        await using var game = GameConnection("survival-1");
        var offered = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        game.On<JsonElement>(WorldTaskEventTypes.WorldTaskOffered, e => offered.TrySetResult(e));
        await game.StartAsync();
        await game.InvokeAsync("ReportRoster", new[] { new { uuid = Steve, name = "Steve", world = "world" } });

        Publish(r =>
        {
            Assert.Equal("survival-1", r.ServerOf(Steve.ToUpperInvariant()));
            r.Offered(task, "survival-1", Steve, WorldTaskOfferModes.AutoClaim, null);
        });
        var envelope = await offered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var payload = envelope.GetProperty("payload");
        Assert.Equal(task.Id, payload.GetProperty("taskId").GetInt32());
        Assert.Equal(Steve, payload.GetProperty("uuid").GetString());
        Assert.Equal("auto-claim", payload.GetProperty("mode").GetString());
        Assert.Equal("Town › Location", payload.GetProperty("label").GetString());
        Assert.Equal(120, payload.GetProperty("timeoutSeconds").GetInt32());

        _tasks.Setup(t => t.RecordOfferResultAsync(task.Id, "declined", Steve)).ReturnsAsync(true);
        await game.InvokeAsync("WorldTaskOfferResult", new { taskId = task.Id, result = "declined", uuid = Steve });
        _tasks.Verify(t => t.RecordOfferResultAsync(task.Id, "declined", Steve), Times.Once);
    }

    [Fact]
    public async Task RosterAndJoin_OfferWhatWaitsForThosePlayers()
    {
        await using var game = GameConnection();
        await game.StartAsync();

        await game.InvokeAsync("ReportRoster", new[] { new { uuid = Steve, name = "Steve", world = "world" } });
        await game.InvokeAsync("PlayerJoined", new { uuid = "00000000-0000-0000-0000-00000000000b", name = "Alex", world = "world" });

        _tasks.Verify(t => t.OfferWaitingForPlayersAsync(It.Is<IReadOnlyCollection<string>>(u => u.Contains(Steve))), Times.Once);
        _tasks.Verify(t => t.OfferWaitingForPlayersAsync(It.Is<IReadOnlyCollection<string>>(u => u.Contains("00000000-0000-0000-0000-00000000000b"))), Times.Once);
    }

    [Fact]
    public async Task HeadlessAndCancelled_GoToEveryServer()
    {
        var task = await AddTaskAsync(createdBy: 42);
        await using var game = GameConnection();
        var created = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        game.On<JsonElement>(WorldTaskEventTypes.WorldTaskCreated, e => created.TrySetResult(e));
        game.On<JsonElement>(WorldTaskEventTypes.WorldTaskCancelled, e => cancelled.TrySetResult(e));
        await game.StartAsync();
        await Eventually(() => _app.Services.GetRequiredService<RealtimeConnections>().Count(RealtimeHubKind.Game) == 1, "the connection");

        Publish(r => r.HeadlessPending(task));
        task.Status = WorldTaskStatuses.Cancelled;
        Publish(r => r.Cancelled(task, Steve, "Cancelled in the web app."));

        Assert.Equal(task.Id, (await created.Task.WaitAsync(TimeSpan.FromSeconds(10))).GetProperty("payload").GetProperty("taskId").GetInt32());
        var payload = (await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10))).GetProperty("payload");
        Assert.Equal("Cancelled", payload.GetProperty("status").GetString());
        Assert.Equal(Steve, payload.GetProperty("uuid").GetString());
    }

    // ===== plain pieces =====

    [Fact]
    public void Connection_JoinAndLeave_ChangeItsGroups()
    {
        var connection = new RealtimeConnection("w1", RealtimeHubKind.Web, new[] { RealtimeGroups.User(1) }, userId: 1);

        Assert.True(connection.Join(RealtimeGroups.Task(5)));
        Assert.False(connection.Join(RealtimeGroups.Task(5)));
        Assert.Contains("task:5", connection.Groups);
        Assert.True(connection.Leave(RealtimeGroups.Task(5)));
        Assert.False(connection.Leave(RealtimeGroups.Task(5)));
        Assert.Equal(new[] { "user:1" }, connection.Groups);
    }

    [Fact]
    public void WorldTaskRealtime_SendsUpdatesToTheTaskAndCreator_AndScanUpdatesToTheStarter()
    {
        var sink = new RecordingSink();
        var realtime = new WorldTaskRealtime(sink);
        var task = new WorldTask
        {
            Id = 3, TaskType = WorldTaskTypes.MinecraftCatalogScan, Status = WorldTaskStatuses.InProgress, CreatedByUserId = 42,
            InputJson = "{\"scanId\":17}", LinkCode = "SECRET"
        };

        realtime.Changed(task);

        var update = Assert.Single(sink.Events, e => e.Type == WorldTaskEventTypes.WorldTaskUpdated);
        Assert.Equal(new[] { "task:3", "user:42" }, update.WebGroups);
        var scan = Assert.Single(sink.Events, e => e.Type == WorldTaskEventTypes.CatalogScanUpdated);
        Assert.Equal(new[] { "user:42" }, scan.WebGroups);
        Assert.Equal(new CatalogScanUpdatedPayload(17, 3, "InProgress"), scan.Payload);
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(update.Payload));
        Assert.Null(realtime.ServerOf("anyone")); // no presence registry: nobody online
    }

    [Fact]
    public void Sink_SendsAWebEventToTheWebGroups_NotTheGameServers()
    {
        var publisher = new RealtimePublisher(Options.Create(new RealtimeOptions()), NullLogger<RealtimePublisher>.Instance);
        var sink = new AfterCommitRealtimeEventSink(publisher, new RealtimeTransactionEvents());

        sink.Publish(RealtimeEvent.ToWeb("WorldTaskUpdated", new { taskId = 1 }, new[] { "task:1", "user:2", "task:1" }));

        Assert.True(publisher.Reader.TryRead(out var message));
        Assert.Equal(RealtimeHubKind.Web, message!.Hub);
        Assert.Equal(new[] { "task:1", "user:2" }, message.Groups);
        Assert.Null(message.Group);
    }

    [Fact]
    public void Presence_ConnectedServerOf_IgnoresServersInTheirGracePeriod()
    {
        var presence = new PresenceRegistry();
        presence.ServerConnected("s1", "c1");
        presence.PlayerJoined("s1", new RosterEntry(Steve, "Steve", "world", null));
        Assert.Equal("s1", presence.ConnectedServerOf(Steve));
        Assert.Single(presence.ConnectedPlayers());

        presence.ServerDisconnected("s1", "c1");

        Assert.Equal("s1", presence.ServerOf(Steve)); // still online for the grace period
        Assert.Null(presence.ConnectedServerOf(Steve)); // but an offer can't reach it
        Assert.Empty(presence.ConnectedPlayers());
    }

    private sealed class RecordingSink : IRealtimeEventSink
    {
        public List<RealtimeEvent> Events { get; } = new();
        public void Publish(RealtimeEvent realtimeEvent) => Events.Add(realtimeEvent);
    }
}
