using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
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
/// api/teleport-destinations (teleport DESIGN.md §3.7.3, Phase 5): game server only on every route
/// (KNG-22 [RequirePluginService]), status codes, and the JSON names the plugin reads.
/// </summary>
[Trait("Category", "API")]
public class TeleportDestinationsControllerTests
{
    private readonly Mock<ITeleportDestinationService> _service = new();

    private TeleportDestinationsController Controller() => new(_service.Object)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    private const string Key = "secret";

    private static HttpContext Http(string? apiKey, ClaimsPrincipal? user = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [PluginServiceAuth.ApiKeyConfigKey] = Key
        }).Build();
        var environment = new Mock<IHostEnvironment>();
        environment.Setup(e => e.EnvironmentName).Returns(Environments.Production);
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IConfiguration))).Returns(configuration);
        services.Setup(s => s.GetService(typeof(IHostEnvironment))).Returns(environment.Object);
        var http = new DefaultHttpContext { RequestServices = services.Object };
        if (user != null) http.User = user;
        if (apiKey != null) http.Request.Headers[PluginServiceAuth.ApiKeyHeader] = apiKey;
        return http;
    }

    private static IActionResult? Authorize(string method, HttpContext http)
    {
        var gate = typeof(TeleportDestinationsController).GetMethod(method)!.GetCustomAttribute<RequirePluginServiceAttribute>();
        Assert.NotNull(gate);
        var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>());
        gate!.OnAuthorization(context);
        return context.Result;
    }

    public static IEnumerable<object[]> Routes() => new[]
    {
        new object[] { nameof(TeleportDestinationsController.List) },
        new object[] { nameof(TeleportDestinationsController.Charge) },
        new object[] { nameof(TeleportDestinationsController.RequestFee) },
        new object[] { nameof(TeleportDestinationsController.BackFee) },
        new object[] { nameof(TeleportDestinationsController.Refund) }
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public void EveryRoute_IsGameServerOnly(string method)
    {
        Assert.Null(Authorize(method, Http(Key)));
        Assert.Equal(401, Assert.IsType<UnauthorizedObjectResult>(Authorize(method, Http(null))).StatusCode);
        Assert.Equal(401, Assert.IsType<UnauthorizedObjectResult>(Authorize(method, Http("wrong"))).StatusCode);
        var webUser = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("uid", "5") }, "Bearer"));
        Assert.Equal(403, Assert.IsType<ObjectResult>(Authorize(method, Http(null, webUser))).StatusCode);
    }

    [Fact]
    public async Task List_ReturnsTheDestinations_And404ForAnUnknownUser()
    {
        _service.Setup(s => s.ListForUserAsync(7)).ReturnsAsync(new List<TeleportDestinationDto> { new() { DomainId = 1, Name = "Kardenna" } });
        _service.Setup(s => s.ListForUserAsync(8)).ThrowsAsync(new KeyNotFoundException());

        var ok = await Controller().List(7);
        var missing = await Controller().List(8);

        Assert.Single(Assert.IsType<List<TeleportDestinationDto>>(Assert.IsType<OkObjectResult>(ok.Result).Value));
        Assert.IsType<NotFoundObjectResult>(missing.Result);
    }

    [Fact]
    public async Task Charge_MapsRefusalsTo409WithTheCode()
    {
        var request = new TeleportChargeRequestDto { UserId = 7, IdempotencyKey = "warp:1" };
        _service.Setup(s => s.ChargeAsync(3, request))
            .ThrowsAsync(new TeleportDestinationException(TeleportDestinationException.InsufficientGems, "You don't have enough gems to teleport to this location!"));

        var result = await Controller().Charge(3, request);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var json = JsonSerializer.Serialize(conflict.Value);
        Assert.Contains("\"error\":\"InsufficientGems\"", json);
        Assert.Contains("enough gems", json);
    }

    [Fact]
    public async Task Charge_BadKeyIs400_UnknownUserIs404_MissingBodyIs400()
    {
        var bad = new TeleportChargeRequestDto { UserId = 7, IdempotencyKey = "bad key" };
        var unknown = new TeleportChargeRequestDto { UserId = 9, IdempotencyKey = "warp:1" };
        _service.Setup(s => s.ChargeAsync(3, bad)).ThrowsAsync(new ArgumentException("idempotencyKey"));
        _service.Setup(s => s.ChargeAsync(3, unknown)).ThrowsAsync(new KeyNotFoundException());

        Assert.IsType<BadRequestObjectResult>(await Controller().Charge(3, bad));
        Assert.IsType<NotFoundObjectResult>(await Controller().Charge(3, unknown));
        Assert.IsType<BadRequestObjectResult>(await Controller().Charge(3, null!));
    }

    [Fact]
    public async Task RequestFeeAndRefund_ReturnTheServiceResult()
    {
        var fee = new TeleportRequestFeeDto { UserId = 7, AmountCoins = 100, IdempotencyKey = "tpa:1" };
        var refund = new TeleportRefundRequestDto { UserId = 7, IdempotencyKey = "tpa:1" };
        _service.Setup(s => s.ChargeRequestFeeAsync(fee)).ReturnsAsync(new TeleportChargeResultDto { Currency = "Coins", Charged = 100 });
        _service.Setup(s => s.RefundAsync(refund)).ReturnsAsync(new TeleportRefundResultDto { Refunded = true, Amount = 100 });

        Assert.Equal(100, Assert.IsType<TeleportChargeResultDto>(Assert.IsType<OkObjectResult>(await Controller().RequestFee(fee)).Value).Charged);
        Assert.True(Assert.IsType<TeleportRefundResultDto>(Assert.IsType<OkObjectResult>(await Controller().Refund(refund)).Value).Refunded);
    }

    [Fact]
    public async Task BackFee_ReturnsTheServiceResult()
    {
        var fee = new TeleportBackFeeDto { UserId = 7, AmountCoins = 250, IdempotencyKey = "back:1", BackKind = "warps" };
        _service.Setup(s => s.ChargeBackFeeAsync(fee)).ReturnsAsync(new TeleportChargeResultDto { Currency = "Coins", Charged = 250 });

        Assert.Equal(250, Assert.IsType<TeleportChargeResultDto>(Assert.IsType<OkObjectResult>(await Controller().BackFee(fee)).Value).Charged);
    }

    [Fact]
    public void Dtos_SerializeWithTheCamelCaseNamesThePluginReads()
    {
        var json = JsonSerializer.Serialize(new TeleportChargeResultDto
        {
            Currency = "Gems",
            Destination = new TeleportDestinationDto { Name = "Kardenna", DomainType = "Town", Location = new TeleportLocationDto { World = "world" } }
        });
        using var doc = JsonDocument.Parse(json);
        foreach (var name in new[] { "currency", "charged", "newBalance", "replayed", "transactionPublicId", "destination" })
        {
            Assert.True(doc.RootElement.TryGetProperty(name, out _), name);
        }
        var destination = doc.RootElement.GetProperty("destination");
        foreach (var name in new[] { "domainId", "name", "domainType", "location", "priceGems", "minTitleName", "minPremiumTierName",
                     "requiresDiscovery", "available", "requirementsMet", "canAfford", "lockCode", "lockReason" })
        {
            Assert.True(destination.TryGetProperty(name, out _), name);
        }
        foreach (var name in new[] { "world", "x", "y", "z", "yaw", "pitch" })
        {
            Assert.True(destination.GetProperty("location").TryGetProperty(name, out _), name);
        }
    }
}
