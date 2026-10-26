using Xunit;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Realtime;
using knkwebapi_v2.Realtime.Hubs;
using knkwebapi_v2.Realtime.Presence;
using knkwebapi_v2.Tests.Services.Email;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace knkwebapi_v2.Tests.Realtime;

/// <summary>
/// PandiO/knk-workspace#178: both hubs end to end on a test server with the same wiring as Program.cs
/// (<see cref="RealtimeSetup"/>): plugin key on the game hub, JWT (header or <c>access_token</c>) on the web hub, the
/// <c>Welcome</c> envelope, and presence reaching User.IsOnline.
/// </summary>
public sealed class RealtimeHubTests : IAsyncLifetime
{
    private const string PluginKey = "realtime-test-plugin-key";
    private const string JwtSecret = "realtime-test-jwt-secret-0123456789abcdef-0123";
    private const string A = "00000000-0000-0000-0000-00000000000a";
    private const string B = "00000000-0000-0000-0000-00000000000b";

    private readonly ManualClock _clock = new(new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc));
    private readonly string _dbName = "realtime-hub-" + Guid.NewGuid();
    private WebApplication _app = null!;
    private TestServer _server = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Configuration["Security:PluginApiKey"] = PluginKey;
        builder.Configuration["Realtime:PresenceGraceSeconds"] = "60";
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

    private HubConnection GameConnection(string? key = PluginKey, string? serverId = "test-1") =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(_server.BaseAddress, "api/hubs/game"), o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => _server.CreateHandler();
                if (key != null) o.Headers["X-API-Key"] = key;
                if (serverId != null) o.Headers[GameServerHub.ServerIdHeader] = serverId;
            })
            .Build();

    private HubConnection WebConnection(string? bearer = null, string? queryToken = null) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(_server.BaseAddress, "api/hubs/web" + (queryToken == null ? "" : "?access_token=" + queryToken)), o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => _server.CreateHandler();
                if (bearer != null) o.AccessTokenProvider = () => Task.FromResult<string?>(bearer);
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

    private async Task<int> AddUser(string name, string uuid, bool online)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KnKDbContext>();
        var user = new User { Username = name, Uuid = uuid, IsOnline = online, CreatedAt = _clock.UtcNow };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<bool> IsOnline(int userId)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KnKDbContext>();
        return (await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId)).IsOnline;
    }

    private static async Task Eventually(Func<Task<bool>> condition, string what)
    {
        for (var i = 0; i < 200; i++)
        {
            if (await condition()) return;
            await Task.Delay(25);
        }
        Assert.Fail("Timed out waiting for " + what);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task GameHub_WithoutTheRightKey_Is401(string? key)
    {
        await using var connection = GameConnection(key);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => connection.StartAsync());

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task GameHub_WithKey_ConnectsAndGetsWelcomeAsSeq1()
    {
        await using var connection = GameConnection(serverId: "survival-1");
        var welcome = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<JsonElement>("Welcome", e => welcome.TrySetResult(e));

        await connection.StartAsync();
        var envelope = await welcome.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, envelope.GetProperty("v").GetInt32());
        Assert.Equal(1, envelope.GetProperty("seq").GetInt64());
        Assert.Equal("Welcome", envelope.GetProperty("type").GetString());
        Assert.Equal("system", envelope.GetProperty("actor").GetProperty("source").GetString());
        Assert.Equal("survival-1", envelope.GetProperty("payload").GetProperty("serverId").GetString());
        Assert.Equal(60, envelope.GetProperty("payload").GetProperty("presenceGraceSeconds").GetInt32());
        Assert.Equal(1, _app.Services.GetRequiredService<RealtimeConnections>().Count(RealtimeHubKind.Game));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("bad id with spaces")]
    public async Task GameHub_WithoutAValidServerId_IsClosed(string? serverId)
    {
        await using var connection = GameConnection(serverId: serverId);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Closed += _ => { closed.TrySetResult(); return Task.CompletedTask; };

        try
        {
            await connection.StartAsync();
        }
        catch (Exception)
        {
            closed.TrySetResult();
        }

        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, _app.Services.GetRequiredService<RealtimeConnections>().Count(RealtimeHubKind.Game));
    }

    [Fact]
    public async Task Roster_ReconcilesIsOnline_AndJoinLeaveFollow()
    {
        var a = await AddUser("a", A, online: false);
        var b = await AddUser("b", B, online: true); // left online by a crash
        await using var connection = GameConnection();
        await connection.StartAsync();

        await connection.InvokeAsync("ReportRoster", new[] { new { uuid = A.ToUpperInvariant(), name = "a", world = "world", userId = a } });
        Assert.True(await IsOnline(a));
        Assert.False(await IsOnline(b));
        Assert.Equal("test-1", _app.Services.GetRequiredService<PresenceRegistry>().ServerOf(A));

        await connection.InvokeAsync("PlayerJoined", new { uuid = B, name = "b", world = "world_nether" });
        Assert.True(await IsOnline(b));

        await connection.InvokeAsync("PlayerLeft", new { uuid = A, world = "world" });
        Assert.False(await IsOnline(a));
    }

    [Fact]
    public async Task ServerGoneLongerThanTheGrace_PlayersMarkedOffline()
    {
        var a = await AddUser("a", A, online: false);
        var connection = GameConnection();
        await connection.StartAsync();
        await connection.InvokeAsync("ReportRoster", new[] { new { uuid = A } });
        Assert.True(await IsOnline(a));

        await connection.DisposeAsync();
        var registry = _app.Services.GetRequiredService<PresenceRegistry>();
        await Eventually(() => Task.FromResult(registry.Servers().All(s => !s.Connected)), "the disconnect");
        var sweep = _app.Services.GetServices<IHostedService>().OfType<PresenceSweepService>().Single();

        _clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(0, await sweep.RunOnceAsync());
        Assert.True(await IsOnline(a));

        _clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(1, await sweep.RunOnceAsync());
        Assert.False(await IsOnline(a));
    }

    [Fact]
    public async Task WebHub_WithoutToken_Is401()
    {
        await using var connection = WebConnection();

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => connection.StartAsync());

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task WebHub_PluginKeyIsNotAWebLogin()
    {
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_server.BaseAddress, "api/hubs/web"), o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => _server.CreateHandler();
                o.Headers["X-API-Key"] = PluginKey;
            })
            .Build();

        await Assert.ThrowsAsync<HttpRequestException>(() => connection.StartAsync());
    }

    [Fact]
    public async Task WebHub_WithBearer_JoinsTheUserGroup_AndReceivesPublishedMessages()
    {
        await using var connection = WebConnection(bearer: Jwt(42));
        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<JsonElement>("Hello", e => received.TrySetResult(e));
        await connection.StartAsync();

        _app.Services.GetRequiredService<IRealtimePublisher>().PublishToWebUser(42, "Hello", new { n = 1 });
        var envelope = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, envelope.GetProperty("seq").GetInt64());
        Assert.Equal(1, envelope.GetProperty("payload").GetProperty("n").GetInt32());
    }

    [Fact]
    public async Task WebHub_AcceptsTheAccessTokenQueryString()
    {
        await using var connection = WebConnection(queryToken: Jwt(7));

        await connection.StartAsync();

        Assert.Equal(HubConnectionState.Connected, connection.State);
        Assert.Single(_app.Services.GetRequiredService<RealtimeConnections>().InGroup(RealtimeHubKind.Web, RealtimeGroups.User(7)));
    }

    [Theory]
    [InlineData("/api/hubs/web", null, "tok", "tok")]
    [InlineData("/api/hubs/web/negotiate", null, "tok", "tok")]
    [InlineData("/api/hubs/web", "Bearer header", "tok", null)]
    [InlineData("/api/hubs/game", null, "tok", null)]
    [InlineData("/api/Users", null, "tok", null)]
    [InlineData("/api/hubs/web", null, null, null)]
    public void ReadHubAccessToken_OnlyForTheWebHubWithoutAHeader(string path, string? authorization, string? query, string? expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (authorization != null) context.Request.Headers.Authorization = authorization;
        if (query != null) context.Request.QueryString = new QueryString("?access_token=" + query);

        Assert.Equal(expected, RealtimeSetup.ReadHubAccessToken(context.Request));
    }

    [Theory]
    [InlineData("localhost", "localhost")]
    [InlineData(" survival-1 ", "survival-1")]
    [InlineData("a.b_c-9", "a.b_c-9")]
    [InlineData("", null)]
    [InlineData("has space", null)]
    [InlineData("ünicode", null)]
    public void NormalizeServerId(string raw, string? expected)
    {
        Assert.Equal(expected, GameServerHub.NormalizeServerId(raw));
    }
}
