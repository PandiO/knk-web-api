using System.Reflection;
using FluentAssertions;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Lootbox;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// Lootboxes Phase 2: every runtime endpoint carries a KNG-22 gate. Game-server-only routes (spawn, claim, deliver,
/// pending, admin spawn/give, runtime config) are <see cref="RequirePluginServiceAttribute"/>, so a browser without the
/// key gets 401; the active list and despawn also let the web app's lootbox admins in; the drop log is web-admin only.
/// Also pins the 409/429 bodies the plugin parses.
/// </summary>
[Trait("Category", "API")]
public class LootboxRuntimeControllersAuthTests
{
    public static IEnumerable<object?[]> Routes() => new[]
    {
        new object?[] { typeof(LootboxSpawnsController), nameof(LootboxSpawnsController.GetRuntimeConfig), "plugin" },
        new object?[] { typeof(LootboxSpawnsController), nameof(LootboxSpawnsController.Spawn), "plugin" },
        new object?[] { typeof(LootboxSpawnsController), nameof(LootboxSpawnsController.AdminSpawn), "plugin" },
        new object?[] { typeof(LootboxSpawnsController), nameof(LootboxSpawnsController.Claim), "plugin" },
        new object?[] { typeof(LootboxSpawnsController), nameof(LootboxSpawnsController.GetActive), "service-or-node" },
        new object?[] { typeof(LootboxSpawnsController), nameof(LootboxSpawnsController.Despawn), "service-or-node" },
        new object?[] { typeof(LootboxClaimsController), nameof(LootboxClaimsController.Delivered), "plugin" },
        new object?[] { typeof(LootboxClaimsController), nameof(LootboxClaimsController.Pending), "plugin" },
        new object?[] { typeof(LootboxClaimsController), nameof(LootboxClaimsController.AdminGive), "plugin" },
        new object?[] { typeof(LootboxClaimsController), nameof(LootboxClaimsController.GetById), "web" },
        new object?[] { typeof(LootboxClaimsController), nameof(LootboxClaimsController.Search), "web" },
        new object?[] { typeof(LootboxSpawnAreasController), nameof(LootboxSpawnAreasController.CreateInGame), "plugin" },
        new object?[] { typeof(LootboxSpawnAreasController), nameof(LootboxSpawnAreasController.DeleteInGame), "plugin" },
        new object?[] { typeof(LootboxTypesController), nameof(LootboxTypesController.GetOdds), "service-or-node" },
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public void Route_CarriesItsGate(Type controller, string action, string gate)
    {
        var method = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Single(m => m.Name == action);
        var plugin = method.GetCustomAttribute<RequirePluginServiceAttribute>() != null;
        var serviceOrNode = method.GetCustomAttributes<RequireServiceOrPermissionAttribute>().Select(a => a.Node).ToList();
        var web = method.GetCustomAttributes<RequirePermissionAttribute>().Select(a => (string)a.Arguments![0]).ToList();

        switch (gate)
        {
            case "plugin":
                plugin.Should().BeTrue();
                serviceOrNode.Should().BeEmpty();
                web.Should().BeEmpty();
                break;
            case "service-or-node":
                serviceOrNode.Should().Equal(StaffPermissions.ManageLootboxes);
                break;
            default:
                web.Should().Equal(StaffPermissions.ManageLootboxes);
                break;
        }
    }

    [Fact]
    public void EveryRuntimeAction_IsListed()
    {
        var listed = Routes().Select(r => ((Type)r[0]!, (string)r[1]!)).ToHashSet();
        foreach (var controller in new[] { typeof(LootboxSpawnsController), typeof(LootboxClaimsController) })
        {
            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                listed.Should().Contain((controller, action.Name));
            }
        }
    }

    private static HttpContext Http(string? sentKey)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [PluginServiceAuth.ApiKeyConfigKey] = "secret",
        }).Build();
        var environment = new Mock<IHostEnvironment>();
        environment.Setup(e => e.EnvironmentName).Returns(Environments.Production);
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IConfiguration))).Returns(configuration);
        services.Setup(s => s.GetService(typeof(IHostEnvironment))).Returns(environment.Object);
        var http = new DefaultHttpContext { RequestServices = services.Object };
        if (sentKey != null) http.Request.Headers[PluginServiceAuth.ApiKeyHeader] = sentKey;
        return http;
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("wrong", 401)]
    [InlineData("secret", null)]
    public void Claim_WithoutTheKey_Is401(string? key, int? status)
    {
        var context = new AuthorizationFilterContext(new ActionContext(Http(key), new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>());

        typeof(LootboxSpawnsController).GetMethod(nameof(LootboxSpawnsController.Claim))!
            .GetCustomAttribute<RequirePluginServiceAttribute>()!.OnAuthorization(context);

        (context.Result as ObjectResult)?.StatusCode.Should().Be(status);
        if (status == null) context.Result.Should().BeNull();
    }

    private static LootboxSpawnsController Spawns(Mock<ILootboxRuntimeService> service) =>
        new(service.Object) { ControllerContext = new ControllerContext { HttpContext = Http("secret") } };

    [Fact]
    public async Task Claim_MapsConflictsAndTheDailyLimit()
    {
        var service = new Mock<ILootboxRuntimeService>();
        var request = new LootboxClaimRequestDto { UserId = 1, IdempotencyKey = "k" };
        service.Setup(s => s.ClaimAsync(1, request)).ThrowsAsync(new LootboxConflictException("AlreadyClaimed", "taken"));
        var resetsAt = new DateTime(2026, 9, 27, 0, 0, 0, DateTimeKind.Utc);
        service.Setup(s => s.ClaimAsync(2, request)).ThrowsAsync(new LootboxDailyLimitException("Global", 10, resetsAt));
        service.Setup(s => s.ClaimAsync(3, request)).ThrowsAsync(new KeyNotFoundException("no spawn"));
        service.Setup(s => s.ClaimAsync(4, request)).ReturnsAsync(new LootboxClaimResultDto { ClaimId = 9 });
        var controller = Spawns(service);

        var conflict = (await controller.Claim(1, request)).Should().BeOfType<ConflictObjectResult>().Subject;
        Prop(conflict.Value, "code").Should().Be("AlreadyClaimed");

        var limited = (await controller.Claim(2, request)).Should().BeOfType<ObjectResult>().Subject;
        limited.StatusCode.Should().Be(429);
        (Prop(limited.Value, "code"), Prop(limited.Value, "scope"), Prop(limited.Value, "limit"), Prop(limited.Value, "resetsAt"))
            .Should().Be(("DailyLimit", "Global", 10, resetsAt));

        (await controller.Claim(3, request)).Should().BeOfType<NotFoundObjectResult>();
        (await controller.Claim(4, request)).Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Spawn_Returns201()
    {
        var service = new Mock<ILootboxRuntimeService>();
        service.Setup(s => s.SpawnAsync(It.IsAny<LootboxSpawnRequestDto>())).ReturnsAsync(new LootboxSpawnDto { Id = 5 });

        var result = await Spawns(service).Spawn(new LootboxSpawnRequestDto());

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(201);
    }

    private static object? Prop(object? value, string name) => value!.GetType().GetProperty(name)!.GetValue(value);
}
