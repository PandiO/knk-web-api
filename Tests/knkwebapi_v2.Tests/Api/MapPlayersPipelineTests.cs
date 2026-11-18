using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Map;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;
using static knkwebapi_v2.Tests.Services.Map.MapPlayerRegistryTests;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// Overview map L4 (PandiO/knk-workspace#738, spec § 6.2.4, O21, O22, O27; tier:3): <c>GET api/Map/players</c> through
/// the real controller, default-deny filter, JWT bearer and rate limiter. The raw response body of each caller is checked:
/// anonymous, a logged-in player without the node and a viewer with <c>knk.mode.staff</c> (A12, A14).
/// </summary>
public sealed class MapPlayersPipelineTests
{
    private const string JwtSecret = "map-players-test-jwt-secret-0123456789abcdef";
    private const string Plain = "00000000-0000-0000-0000-0000000000a1";
    private const string Staff = "00000000-0000-0000-0000-0000000000b2";
    private const string Sieging = "00000000-0000-0000-0000-0000000000d4";

    private static string Jwt(int userId)
    {
        var token = new JwtSecurityToken(
            claims: new[] { new Claim("uid", userId.ToString()) },
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class Labels : IMapLivePlayerData
    {
        public int Calls;

        public Task<IReadOnlyDictionary<string, MapPlayerLabelDto>> LabelsAsync(IReadOnlyCollection<string> uuids, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            IReadOnlyDictionary<string, MapPlayerLabelDto> result = uuids.ToDictionary(u => u, u => new MapPlayerLabelDto { Name = "N" + u[^2..] });
            return Task.FromResult(result);
        }

        public Task<List<MapArenaDto>> ArenasAsync(IReadOnlyList<MapPlayerEntry> live, CancellationToken ct = default)
        {
            var count = live.Count(p => p.Minigame != null);
            return Task.FromResult(count == 0 ? new List<MapArenaDto>() : new List<MapArenaDto>
            {
                new() { Minigame = "Siege", Label = MapPlayerLabels.ArenaLabel("Siege", count), PlayerCount = count, Pin = new MapPinDto { World = "world" } },
            });
        }
    }

    private static async Task<(WebApplication App, HttpClient Client, Labels Data)> StartAsync(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)),
                ValidateIssuer = false,
                ValidateAudience = false,
            };
        });
        builder.Services.AddControllers(o => o.Filters.Add<DefaultCallerRequiredFilter>())
            .AddApplicationPart(typeof(MapController).Assembly);
        builder.Services.Configure<MapOptions>(builder.Configuration.GetSection(MapOptions.SectionName));
        builder.Services.AddKnkRateLimiting(builder.Configuration);
        builder.Services.AddSingleton(new Mock<IMapPublicService>(MockBehavior.Strict).Object);
        var permissions = new Mock<IPermissionResolutionService>();
        permissions.Setup(p => p.CheckAnyAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<string>>()))
            .ReturnsAsync((int id, IReadOnlyList<string> nodes) => id == 2 && nodes.Contains("knk.mode.staff") ? "knk.mode.staff" : null);
        builder.Services.AddSingleton(permissions.Object);
        builder.Services.AddSingleton<IPermissionReadCache, PermissionReadCache>();
        builder.Services.AddSingleton<MapPlayerRegistry>();
        builder.Services.AddSingleton<MapPlayerSnapshots>();
        var data = new Labels();
        builder.Services.AddScoped<IMapLivePlayerData>(_ => data);
        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        app.Services.GetRequiredService<MapPlayerRegistry>().Apply("s1", Report(
            Player(Plain), Player(Staff, mode: "staff"), Player(Sieging, minigame: "siege", matchId: 500)));
        return (app, app.GetTestClient(), data);
    }

    private static Dictionary<string, string?> On() => new()
    {
        ["Map:Enabled"] = "true",
        ["Map:LivePlayers:Enabled"] = "true",
    };

    private static async Task<(HttpStatusCode Status, string Body, HttpResponseMessage Response)> Get(HttpClient client, int? userId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/Map/players");
        if (userId is { } id) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Jwt(id));
        var response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(), response);
    }

    [Theory]
    [InlineData("false", "true")]
    [InlineData("true", "false")]
    [InlineData("true", null)]
    public async Task Off_Answers404_AndReadsNothing(string mapEnabled, string? livePlayers) // A03, O27
    {
        var settings = new Dictionary<string, string?> { ["Map:Enabled"] = mapEnabled };
        if (livePlayers != null) settings["Map:LivePlayers:Enabled"] = livePlayers;
        var (app, client, data) = await StartAsync(settings);
        await using var _ = app;

        Assert.Equal(HttpStatusCode.NotFound, (await Get(client)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Get(client, 2)).Status);
        Assert.Equal(0, data.Calls);
    }

    [Fact]
    public async Task Anonymous_And_PlainLogin_GetOnlyThePublicPlayers_RawBody() // A11, A12, A14
    {
        var (app, client, _) = await StartAsync(On());
        await using var __ = app;

        foreach (var caller in new int?[] { null, 1 })
        {
            var (status, body, response) = await Get(client, caller);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Contains(Plain, body);
            Assert.DoesNotContain(Staff, body);
            Assert.DoesNotContain(Sieging, body);
            Assert.DoesNotContain("\"staff\"", body);
            Assert.Contains("[Siege] 1 player", body);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        }
    }

    [Fact]
    public async Task StaffViewer_GetsHiddenPlayersTagged_RawBody() // A12, O22
    {
        var (app, client, _) = await StartAsync(On());
        await using var __ = app;

        var (status, body, _) = await Get(client, 2);

        Assert.Equal(HttpStatusCode.OK, status);
        using var json = JsonDocument.Parse(body);
        var players = json.RootElement.GetProperty("players").EnumerateArray()
            .ToDictionary(p => p.GetProperty("uuid").GetString()!, p => p);
        Assert.Equal(3, players.Count);
        Assert.Equal("staff", players[Staff].GetProperty("mode").GetString());
        Assert.Equal("Siege", players[Sieging].GetProperty("minigame").GetString());
    }

    [Fact]
    public async Task InvalidToken_IsServedAsAnonymous()
    {
        var (app, client, _) = await StartAsync(On());
        await using var __ = app;
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/Map/players");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");

        var body = await (await client.SendAsync(request)).Content.ReadAsStringAsync();

        Assert.Contains(Plain, body);
        Assert.DoesNotContain(Staff, body);
    }

    [Fact]
    public async Task Players_ShareTheMapRateLimit() // O29: 2 s polling is 30 a minute, under the 120 limit
    {
        var settings = On();
        settings["RateLimiting:Map:PermitPerMinute"] = "2";
        var (app, client, _) = await StartAsync(settings);
        await using var __ = app;

        Assert.Equal(HttpStatusCode.OK, (await Get(client)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Get(client)).Status);
        Assert.Equal((HttpStatusCode)429, (await Get(client)).Status);
    }
}
