using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Moq;
using Xunit;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// CurrencyMonitorController (currency-payments IMPLEMENTATION_PLAN.md Phase 5): the node each
/// route needs, query validation, acknowledge needing a named staff member, and 409 while a
/// reconciliation runs.
/// </summary>
[Trait("Category", "API")]
public class CurrencyMonitorControllerTests
{
    private const string Key = "the-plugin-key";
    private const int StaffId = 900;

    private readonly Mock<ICurrencyAlertService> _alerts = new();
    private readonly Mock<IPermissionResolutionService> _permissions = new();
    private readonly CurrencyMonitorController _controller;

    public CurrencyMonitorControllerTests()
    {
        _controller = new CurrencyMonitorController(_alerts.Object);
    }

    private static DefaultHttpContext Http(bool plugin = false, int? webUserId = null, int? actingUserId = null)
    {
        var configuration = new Mock<Microsoft.Extensions.Configuration.IConfiguration>();
        configuration.Setup(c => c["Security:PluginApiKey"]).Returns(Key);
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(Microsoft.Extensions.Configuration.IConfiguration))).Returns(configuration.Object);
        var httpContext = new DefaultHttpContext { RequestServices = services.Object };
        if (plugin) httpContext.Request.Headers[PluginServiceAuth.ApiKeyHeader] = Key;
        if (actingUserId != null) httpContext.Request.Headers[PluginServiceAuth.ActingUserHeader] = actingUserId.Value.ToString();
        if (webUserId != null)
        {
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("uid", webUserId.Value.ToString()) }, "Test"));
        }
        return httpContext;
    }

    private void SetRequest(bool plugin = false, int? webUserId = null, int? actingUserId = null) =>
        _controller.ControllerContext = new ControllerContext { HttpContext = Http(plugin, webUserId, actingUserId) };

    private void Grant(string node) =>
        _permissions.Setup(p => p.CheckAsync(StaffId, node)).ReturnsAsync(new PermissionCheckResponseDto { Result = PermissionResolutionResult.Granted });

    private async Task AssertNodeAsync(MethodInfo method, string node, bool pluginAllowed)
    {
        var filter = method.GetCustomAttributes<TypeFilterAttribute>().Single();
        Assert.Equal(pluginAllowed ? typeof(RequireServiceOrPermissionFilter) : typeof(RequirePermissionFilter), filter.ImplementationType);
        Assert.Equal(node, Assert.Single(filter.Arguments!));

        async Task<IActionResult?> Run(HttpContext http)
        {
            IAsyncAuthorizationFilter instance = pluginAllowed
                ? new RequireServiceOrPermissionFilter(node, _permissions.Object)
                : new RequirePermissionFilter(node, _permissions.Object);
            var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>());
            await instance.OnAuthorizationAsync(context);
            return context.Result;
        }

        Assert.Equal(401, ((ObjectResult)(await Run(Http()))!).StatusCode);
        Assert.Equal(403, ((ObjectResult)(await Run(Http(webUserId: StaffId)))!).StatusCode);
        Grant(node);
        Assert.Null(await Run(Http(webUserId: StaffId)));
        var plugin = await Run(Http(plugin: true));
        if (pluginAllowed) Assert.Null(plugin);
        else Assert.NotNull(plugin);
    }

    public static IEnumerable<object[]> Routes() => new[]
    {
        new object[] { nameof(CurrencyMonitorController.GetAlerts), StaffPermissions.CurrencyAlerts, true },
        new object[] { nameof(CurrencyMonitorController.Acknowledge), StaffPermissions.CurrencyAlerts, true },
        new object[] { nameof(CurrencyMonitorController.GetReconciliation), StaffPermissions.CurrencyHistory, false },
        new object[] { nameof(CurrencyMonitorController.RunReconciliation), StaffPermissions.CurrencyAlerts, false }
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public Task EachRoute_NeedsItsNode_WebUsersWithoutItGet403(string action, string node, bool pluginAllowed) =>
        AssertNodeAsync(typeof(CurrencyMonitorController).GetMethod(action)!, node, pluginAllowed);

    [Fact]
    public async Task Alerts_MapTheQuery_AndRejectUnknownStatusOrSeverity()
    {
        CurrencyAlertQuery? seen = null;
        _alerts.Setup(a => a.ListAsync(It.IsAny<CurrencyAlertQuery>(), It.IsAny<CancellationToken>()))
            .Callback<CurrencyAlertQuery, CancellationToken>((q, _) => seen = q)
            .ReturnsAsync(new CurrencyAlertPageDto());
        SetRequest(webUserId: StaffId);

        Assert.IsType<OkObjectResult>(await _controller.GetAlerts("ALL", "high", " R3 ", 7, 0, 500));
        Assert.Equal(("all", CurrencyAlertSeverity.High, "R3", 7, 1, 100),
            (seen!.Status, seen.MinSeverity, seen.Rule, seen.UserId, seen.Page, seen.PageSize));

        Assert.IsType<OkObjectResult>(await _controller.GetAlerts());
        Assert.Equal(("open", (CurrencyAlertSeverity?)null), (seen.Status, seen.MinSeverity));

        Assert.IsType<BadRequestObjectResult>(await _controller.GetAlerts(status: "closed"));
        Assert.IsType<BadRequestObjectResult>(await _controller.GetAlerts(severity: "apocalyptic"));
    }

    [Fact]
    public async Task Acknowledge_NeedsANamedStaffMember()
    {
        _alerts.Setup(a => a.AcknowledgeAsync(5, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long id, int actor, CancellationToken _) => new CurrencyAlertDto { Id = id, AckedByUserId = actor });
        _alerts.Setup(a => a.AcknowledgeAsync(6, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("nope"));

        SetRequest(plugin: true);
        Assert.IsType<BadRequestObjectResult>(await _controller.Acknowledge(5, default));

        SetRequest(plugin: true, actingUserId: 42);
        var ok = Assert.IsType<OkObjectResult>(await _controller.Acknowledge(5, default));
        Assert.Equal(42, ((CurrencyAlertDto)ok.Value!).AckedByUserId);

        SetRequest(webUserId: StaffId);
        Assert.Equal(StaffId, ((CurrencyAlertDto)Assert.IsType<OkObjectResult>(await _controller.Acknowledge(5, default)).Value!).AckedByUserId);
        Assert.IsType<NotFoundObjectResult>(await _controller.Acknowledge(6, default));
    }

    [Fact]
    public async Task RunReconciliation_Is409WhileOneIsRunning()
    {
        SetRequest(webUserId: StaffId);
        _alerts.Setup(a => a.RunReconciliationAsync("manual", StaffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CurrencyReconciliationRunDto?)null);
        Assert.IsType<ConflictObjectResult>(await _controller.RunReconciliation(default));

        _alerts.Setup(a => a.RunReconciliationAsync("manual", StaffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyReconciliationRunDto { Trigger = "manual" });
        Assert.IsType<OkObjectResult>(await _controller.RunReconciliation(default));
    }
}
