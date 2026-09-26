using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>Domain discovery routes: status codes, DTO shape and which ones are staff-only
/// (docs/specs/domain-discovery/DESIGN.md §3.5).</summary>
[Trait("Category", "API")]
public class DiscoveriesControllerTests
{
    private readonly Mock<IDiscoveryService> _service = new();
    private readonly Mock<IDiscoveryConfigurationService> _config = new();

    private DiscoveriesController Controller(ClaimsPrincipal? user = null) => new(_service.Object)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user ?? new ClaimsPrincipal() } }
    };

    private static string? Node(Type controller, string method) =>
        controller.GetMethod(method)!.GetCustomAttributes<RequirePermissionAttribute>()
            .Concat(controller.GetCustomAttributes<RequirePermissionAttribute>())
            .Select(a => (string)a.Arguments![0]!)
            .FirstOrDefault();

    [Fact]
    public async Task Grant_ReturnsTheResult()
    {
        var request = new DiscoveryGrantRequestDto { WgRegionIds = new() { "town_rivia" } };
        _service.Setup(s => s.DiscoverAsync(7, request)).ReturnsAsync(new DiscoveryGrantResultDto
        {
            Granted = new() { new DiscoveryGrantDto { DomainId = 1, Name = "Rivia", DomainType = "Town", Coins = 120, CoinsBase = 100 } },
            TotalCoins = 120,
            CoinMultipliers = new() { RewardMultiplierDto.Personal(1.2m) }
        });

        var result = await Controller().Grant(7, request);

        var body = Assert.IsType<DiscoveryGrantResultDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(120, body.TotalCoins);
    }

    [Fact]
    public void GrantResult_SerializesWithTheCamelCaseNamesThePluginReads()
    {
        var json = JsonSerializer.Serialize(new DiscoveryGrantResultDto
        {
            Granted = new() { new DiscoveryGrantDto { DomainId = 1, WgRegionId = "town_rivia", ParentName = null } },
            AlreadyDiscovered = new() { 2 },
            Skipped = new() { new DiscoverySkipDto { Key = "spawn", Reason = DiscoverySkipDto.NotADomain } }
        });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        foreach (var name in new[] { "granted", "alreadyDiscovered", "skipped", "totalCoins", "totalGems", "totalExp",
                     "totalCoinsBase", "totalGemsBase", "totalExpBase", "coinMultipliers", "gemMultipliers", "expMultipliers",
                     "titleBracketId", "newCoins", "newGems", "newExperiencePoints", "titleChange" })
        {
            Assert.True(root.TryGetProperty(name, out _), name);
        }
        var grant = root.GetProperty("granted")[0];
        foreach (var name in new[] { "domainId", "wgRegionId", "name", "domainType", "parentName", "source", "coins", "gems", "exp", "coinsBase", "gemsBase", "expBase" })
        {
            Assert.True(grant.TryGetProperty(name, out _), name);
        }
        Assert.Equal("NotADomain", root.GetProperty("skipped")[0].GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Grant_InvalidRequestIs400()
    {
        _service.Setup(s => s.DiscoverAsync(7, It.IsAny<DiscoveryGrantRequestDto>())).ThrowsAsync(new ArgumentException("At most 50 ids per request."));

        var result = await Controller().Grant(7, new DiscoveryGrantRequestDto());

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Grant_UnknownUserIs404()
    {
        _service.Setup(s => s.DiscoverAsync(7, It.IsAny<DiscoveryGrantRequestDto>())).ThrowsAsync(new KeyNotFoundException());

        Assert.IsType<NotFoundObjectResult>((await Controller().Grant(7, new DiscoveryGrantRequestDto())).Result);
    }

    [Fact]
    public async Task Known_Progress_Summary_Return200Or404()
    {
        _service.Setup(s => s.GetKnownAsync(7)).ReturnsAsync(new List<KnownDiscoveryDto> { new() { DomainId = 1, WgRegionId = "town_rivia" } });
        _service.Setup(s => s.GetProgressAsync(7, It.IsAny<PagedQueryDto>())).ReturnsAsync(new PagedResultDto<DiscoveryProgressRowDto>());
        _service.Setup(s => s.GetSummaryAsync(7)).ReturnsAsync(new DiscoverySummaryDto());
        _service.Setup(s => s.GetKnownAsync(8)).ThrowsAsync(new KeyNotFoundException());
        _service.Setup(s => s.GetProgressAsync(8, It.IsAny<PagedQueryDto>())).ThrowsAsync(new KeyNotFoundException());
        _service.Setup(s => s.GetSummaryAsync(8)).ThrowsAsync(new KeyNotFoundException());
        var controller = Controller();

        Assert.Single(Assert.IsType<List<KnownDiscoveryDto>>(Assert.IsType<OkObjectResult>((await controller.GetKnown(7)).Result).Value));
        Assert.IsType<OkObjectResult>((await controller.GetProgress(7, null)).Result);
        Assert.IsType<OkObjectResult>((await controller.GetSummary(7)).Result);
        Assert.IsType<NotFoundObjectResult>((await controller.GetKnown(8)).Result);
        Assert.IsType<NotFoundObjectResult>((await controller.GetProgress(8, new PagedQueryDto())).Result);
        Assert.IsType<NotFoundObjectResult>((await controller.GetSummary(8)).Result);
    }

    [Fact]
    public async Task Reset_PassesTheLoggedInStaffMemberAsActor()
    {
        _service.Setup(s => s.ResetAsync(7, 3, 42)).ReturnsAsync(true);
        var staff = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("uid", "42") }, "Bearer"));

        Assert.IsType<NoContentResult>(await Controller(staff).Reset(7, 3));
        _service.Verify(s => s.ResetAsync(7, 3, 42), Times.Once);
    }

    [Fact]
    public async Task Reset_NotDiscoveredOrUnknownUserIs404()
    {
        _service.Setup(s => s.ResetAsync(7, 3, It.IsAny<int?>())).ReturnsAsync(false);
        _service.Setup(s => s.ResetAsync(8, 3, It.IsAny<int?>())).ThrowsAsync(new KeyNotFoundException());

        Assert.IsType<NotFoundObjectResult>(await Controller().Reset(7, 3));
        Assert.IsType<NotFoundObjectResult>(await Controller().Reset(8, 3));
    }

    [Fact]
    public async Task Stats_BuildsTheQueryFromTheQueryString()
    {
        PagedQueryDto? seen = null;
        _service.Setup(s => s.GetStatsAsync(It.IsAny<PagedQueryDto>())).Callback<PagedQueryDto>(q => seen = q).ReturnsAsync(new DiscoveryStatsDto());

        Assert.IsType<OkObjectResult>((await Controller().GetStats(2, 10, "Town", "riv", "name", false)).Result);

        Assert.Equal((2, 10, "riv", "name", false, "Town"), (seen!.PageNumber, seen.PageSize, seen.SearchTerm, seen.SortBy, seen.SortDescending, seen.Filters!["domainType"]));
    }

    [Fact]
    public void PluginRoutesNeedTheServiceKey_AdminRoutesNeedTheDiscoveryNode()
    {
        // KNG-22: grant and the known-set cache are the game server's alone; progress and summary
        // are also read by the player themself and staff on the web; reset is staff-only (it
        // re-enables a reward), from the web or through the plugin's /knk discovery reset.
        var type = typeof(DiscoveriesController);
        Assert.NotNull(type.GetMethod(nameof(DiscoveriesController.Grant))!.GetCustomAttribute<RequirePluginServiceAttribute>());
        Assert.NotNull(type.GetMethod(nameof(DiscoveriesController.GetKnown))!.GetCustomAttribute<RequirePluginServiceAttribute>());
        foreach (var method in new[] { nameof(DiscoveriesController.GetProgress), nameof(DiscoveriesController.GetSummary) })
        {
            var gate = type.GetMethod(method)!.GetCustomAttribute<RequireServiceSelfOrPermissionAttribute>();
            Assert.NotNull(gate);
            Assert.Equal(("knk.admin.discovery", "userId"), (gate!.Node, gate.UserIdRouteKey));
            Assert.Null(Node(type, method));
        }
        Assert.Equal(new[] { "knk.admin.discovery" },
            type.GetMethod(nameof(DiscoveriesController.Reset))!.GetCustomAttributes<RequireServiceOrPermissionAttribute>().Select(a => a.Node));
        Assert.Null(Node(type, nameof(DiscoveriesController.Reset)));
        Assert.Equal("knk.admin.discovery", Node(type, nameof(DiscoveriesController.GetStats)));
        foreach (var method in new[] { nameof(DiscoveryRewardsController.GetRules), nameof(DiscoveryRewardsController.UpdateRule),
                     nameof(DiscoveryRewardsController.GetOverrides), nameof(DiscoveryRewardsController.UpsertOverride),
                     nameof(DiscoveryRewardsController.DeleteOverride), nameof(DiscoveryRewardsController.Preview) })
        {
            Assert.Equal("knk.admin.discovery", Node(typeof(DiscoveryRewardsController), method));
        }
    }

    [Fact]
    public async Task Reset_FromThePlugin_UsesTheActingStaffMember()
    {
        _service.Setup(s => s.ResetAsync(7, 3, 42)).ReturnsAsync(true);
        var controller = Controller();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [PluginServiceAuth.ApiKeyConfigKey] = "secret" }).Build();
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IConfiguration))).Returns(configuration);
        controller.HttpContext.RequestServices = services.Object;
        controller.HttpContext.Request.Headers[PluginServiceAuth.ApiKeyHeader] = "secret";
        controller.HttpContext.Request.Headers[PluginServiceAuth.ActingUserHeader] = "42";

        Assert.IsType<NoContentResult>(await controller.Reset(7, 3));
        _service.Verify(s => s.ResetAsync(7, 3, 42), Times.Once);
    }

    [Fact]
    public async Task Rewards_UpdateRuleMapsErrors()
    {
        var controller = new DiscoveryRewardsController(_config.Object);
        _config.Setup(c => c.UpdateRuleAsync("Town", It.IsAny<UpdateDiscoveryRewardRuleDto>())).ReturnsAsync(new DiscoveryRewardRuleDto { DomainType = "Town" });
        _config.Setup(c => c.UpdateRuleAsync("Castle", It.IsAny<UpdateDiscoveryRewardRuleDto>())).ThrowsAsync(new KeyNotFoundException("no"));
        _config.Setup(c => c.UpdateRuleAsync("District", It.IsAny<UpdateDiscoveryRewardRuleDto>())).ThrowsAsync(new ArgumentException("min > max"));

        Assert.IsType<OkObjectResult>((await controller.UpdateRule("Town", new UpdateDiscoveryRewardRuleDto())).Result);
        Assert.IsType<NotFoundObjectResult>((await controller.UpdateRule("Castle", new UpdateDiscoveryRewardRuleDto())).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.UpdateRule("District", new UpdateDiscoveryRewardRuleDto())).Result);
    }

    [Fact]
    public async Task Rewards_OverridesAndPreviewMapErrors()
    {
        var controller = new DiscoveryRewardsController(_config.Object);
        _config.Setup(c => c.UpsertOverrideAsync(3, It.IsAny<UpdateDomainDiscoveryOverrideDto>())).ReturnsAsync(new DomainDiscoveryOverrideDto { DomainId = 3 });
        _config.Setup(c => c.UpsertOverrideAsync(404, It.IsAny<UpdateDomainDiscoveryOverrideDto>())).ThrowsAsync(new KeyNotFoundException("no"));
        _config.Setup(c => c.UpsertOverrideAsync(5, It.IsAny<UpdateDomainDiscoveryOverrideDto>())).ThrowsAsync(new ArgumentException("bad"));
        _config.Setup(c => c.DeleteOverrideAsync(3)).ReturnsAsync(true);
        _config.Setup(c => c.DeleteOverrideAsync(4)).ReturnsAsync(false);
        _config.Setup(c => c.PreviewAsync(null, null)).ThrowsAsync(new ArgumentException("need one"));
        _config.Setup(c => c.PreviewAsync("Town", null)).ReturnsAsync(new DiscoveryRewardPreviewDto { DomainType = "Town" });
        _config.Setup(c => c.GetOverridesAsync()).ReturnsAsync(new List<DomainDiscoveryOverrideDto>());
        _config.Setup(c => c.GetRulesAsync()).ReturnsAsync(new List<DiscoveryRewardRuleDto>());

        Assert.IsType<OkObjectResult>((await controller.UpsertOverride(3, new UpdateDomainDiscoveryOverrideDto())).Result);
        Assert.IsType<NotFoundObjectResult>((await controller.UpsertOverride(404, new UpdateDomainDiscoveryOverrideDto())).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.UpsertOverride(5, new UpdateDomainDiscoveryOverrideDto())).Result);
        Assert.IsType<NoContentResult>(await controller.DeleteOverride(3));
        Assert.IsType<NotFoundObjectResult>(await controller.DeleteOverride(4));
        Assert.IsType<BadRequestObjectResult>((await controller.Preview(null, null)).Result);
        Assert.IsType<OkObjectResult>((await controller.Preview("Town", null)).Result);
        Assert.IsType<OkObjectResult>((await controller.GetOverrides()).Result);
        Assert.IsType<OkObjectResult>((await controller.GetRules()).Result);
    }
}
