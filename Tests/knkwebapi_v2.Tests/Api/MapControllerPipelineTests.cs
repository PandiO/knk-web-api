using System.Net;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Map;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// Overview map L2 (PandiO/knk-workspace#736): the real <see cref="MapController"/> behind the real global default-deny
/// filter and the real rate limiter, called anonymously. A03 (off means 404 on every route) and A06 (the 121st request
/// in a minute from one IP gets 429).
/// </summary>
public sealed class MapControllerPipelineTests
{
    public static readonly string[] Routes =
    {
        "/api/Map/config",
        "/api/Map/markers",
        "/api/Map/towns/1",
        "/api/Map/districts/2",
        "/api/Map/structures/3",
    };

    private static async Task<(WebApplication App, HttpClient Client, Mock<IMapPublicService> Map)> StartAsync(
        bool enabled, Dictionary<string, string?>? settings = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var values = new Dictionary<string, string?> { ["Map:Enabled"] = enabled ? "true" : "false" };
        foreach (var kv in settings ?? new()) values[kv.Key] = kv.Value;
        builder.Configuration.AddInMemoryCollection(values);
        builder.Services.AddControllers(o => o.Filters.Add<DefaultCallerRequiredFilter>())
            .AddApplicationPart(typeof(MapController).Assembly);
        builder.Services.Configure<MapOptions>(builder.Configuration.GetSection(MapOptions.SectionName));
        builder.Services.AddKnkRateLimiting(builder.Configuration);
        var map = new Mock<IMapPublicService>();
        map.Setup(m => m.GetConfigAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new MapConfigDto { Enabled = true });
        map.Setup(m => m.GetMarkersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new MapMarkersDto());
        map.Setup(m => m.GetTownAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new MapTownPanelDto { Id = 1 });
        map.Setup(m => m.GetDistrictAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(new MapDistrictPanelDto { Id = 2 });
        map.Setup(m => m.GetStructureAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(new MapStructurePanelDto { Id = 3 });
        builder.Services.AddSingleton(map.Object);
        var app = builder.Build();
        app.UseRouting();
        app.UseRateLimiter();
        app.MapControllers();
        await app.StartAsync();
        return (app, app.GetTestClient(), map);
    }

    [Fact]
    public async Task MapOff_EveryRouteAnswers404_AndReadsNothing() // A03
    {
        var (app, client, map) = await StartAsync(enabled: false);
        await using var _ = app;

        foreach (var route in Routes)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(route)).StatusCode);
        }
        map.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MapOffByDefault_WithoutAnySetting_EveryRouteAnswers404() // A03, O27
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers(o => o.Filters.Add<DefaultCallerRequiredFilter>())
            .AddApplicationPart(typeof(MapController).Assembly);
        builder.Services.Configure<MapOptions>(builder.Configuration.GetSection(MapOptions.SectionName));
        builder.Services.AddKnkRateLimiting(builder.Configuration);
        builder.Services.AddSingleton(new Mock<IMapPublicService>(MockBehavior.Strict).Object);
        await using var app = builder.Build();
        app.UseRouting();
        app.UseRateLimiter();
        app.MapControllers();
        await app.StartAsync();
        var client = app.GetTestClient();

        foreach (var route in Routes)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(route)).StatusCode);
        }
    }

    [Fact]
    public async Task MapOn_EveryRouteAnswersAnonymously()
    {
        var (app, client, _) = await StartAsync(enabled: true);
        await using var __ = app;

        foreach (var route in Routes)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(route)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/Map/towns/99")).StatusCode);
    }

    [Fact]
    public async Task MapPolicy_Refuses121stRequestInAMinute_With429() // A06
    {
        var (app, client, _) = await StartAsync(enabled: true);
        await using var __ = app;

        for (var i = 0; i < 120; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Routes[i % Routes.Length])).StatusCode);
        }
        var refused = await client.GetAsync("/api/Map/markers");
        Assert.Equal((HttpStatusCode)429, refused.StatusCode);
        Assert.True(refused.Headers.RetryAfter != null);
    }

    [Fact]
    public async Task MapPolicy_PermitIsConfigurable()
    {
        var (app, client, _) = await StartAsync(enabled: true,
            new Dictionary<string, string?> { ["RateLimiting:Map:PermitPerMinute"] = "3" });
        await using var __ = app;

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/Map/config")).StatusCode);
        }
        Assert.Equal((HttpStatusCode)429, (await client.GetAsync("/api/Map/config")).StatusCode);
    }
}
