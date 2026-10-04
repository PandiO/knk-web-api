using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// POST /api/users/{id}/teleport-audit (docs/specs/teleport/DESIGN.md §3.10, Phase 2): game server only
/// (KNG-22 [RequirePluginService]), status codes, and the actor taken from X-Acting-User-Id only with the key.
/// </summary>
[Trait("Category", "API")]
public class UsersTeleportAuditTests
{
    private readonly Mock<IUserService> _users = new();
    private readonly UsersController _controller;

    public UsersTeleportAuditTests()
    {
        _controller = new UsersController(_users.Object, new Mock<IMapper>().Object, new Mock<IPermissionResolutionService>().Object,
            new Mock<ISalaryService>().Object, new Mock<IUserProfileSummaryService>().Object,
            new Mock<IUserPermissionGroupService>().Object, new Mock<IPermissionGrantService>().Object);
        SetRequest();
    }

    private const string Key = "secret";

    /// <summary>A request as the plugin sends it (key + acting user) unless told otherwise.</summary>
    private static HttpContext Http(string? actingUserId = null, string? apiKey = Key, string? configuredKey = Key,
        ClaimsPrincipal? user = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [PluginServiceAuth.ApiKeyConfigKey] = configuredKey
        }).Build();
        var environment = new Mock<IHostEnvironment>();
        environment.Setup(e => e.EnvironmentName).Returns(Environments.Production);
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IConfiguration))).Returns(configuration);
        services.Setup(s => s.GetService(typeof(IHostEnvironment))).Returns(environment.Object);

        var httpContext = new DefaultHttpContext { RequestServices = services.Object };
        if (user != null) httpContext.User = user;
        if (actingUserId != null) httpContext.Request.Headers[UsersController.ActingUserHeader] = actingUserId;
        if (apiKey != null) httpContext.Request.Headers[UsersController.PluginApiKeyHeader] = apiKey;
        return httpContext;
    }

    private void SetRequest(string? actingUserId = null, string? apiKey = Key, string? configuredKey = Key) =>
        _controller.ControllerContext = new ControllerContext { HttpContext = Http(actingUserId, apiKey, configuredKey) };

    /// <summary>Runs the route's authorization filter the way MVC would; null = passed.</summary>
    private static IActionResult? Authorize(HttpContext http)
    {
        var method = typeof(UsersController).GetMethod(nameof(UsersController.RecordTeleportAudit))!;
        var gate = method.GetCustomAttribute<RequirePluginServiceAttribute>();
        Assert.NotNull(gate);
        var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>());
        gate!.OnAuthorization(context);
        return context.Result;
    }

    private static ClaimsPrincipal LoggedIn(int userId) =>
        new(new ClaimsIdentity(new[] { new Claim("uid", userId.ToString()) }, "Bearer"));

    private static TeleportAuditDto Body() => new()
    {
        Kind = "STAFF",
        SubjectUserId = 7,
        VisitedUserId = 8,
        From = new TeleportAuditPointDto { World = "world", X = 1, Y = 64, Z = 1 },
        To = new TeleportAuditPointDto { World = "world", X = 50, Y = 70, Z = 50 }
    };

    [Fact]
    public async Task RecordsWithTheActingStaffMemberAndReturns204()
    {
        SetRequest(actingUserId: "42");
        var body = Body();

        var result = await _controller.RecordTeleportAudit(7, body);

        Assert.Equal(204, Assert.IsType<NoContentResult>(result).StatusCode);
        _users.Verify(s => s.RecordTeleportAuditAsync(7, body, 42), Times.Once);
    }

    [Fact]
    public async Task UnknownUserReturns404()
    {
        _users.Setup(s => s.RecordTeleportAuditAsync(999, It.IsAny<TeleportAuditDto>(), It.IsAny<int?>()))
            .ThrowsAsync(new KeyNotFoundException());

        var result = await _controller.RecordTeleportAudit(999, Body());

        Assert.Equal(404, Assert.IsType<NotFoundObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task InvalidBodyReturns400()
    {
        _users.Setup(s => s.RecordTeleportAuditAsync(7, It.IsAny<TeleportAuditDto>(), It.IsAny<int?>()))
            .ThrowsAsync(new ArgumentException("Unknown teleport kind"));

        var result = await _controller.RecordTeleportAudit(7, Body());

        Assert.Equal(400, Assert.IsType<BadRequestObjectResult>(result).StatusCode);
    }

    [Fact]
    public void Anonymous_Is401()
    {
        Assert.IsType<UnauthorizedObjectResult>(Authorize(Http(actingUserId: "42", apiKey: null)));
        Assert.IsType<UnauthorizedObjectResult>(Authorize(Http(actingUserId: "42", apiKey: "guess")));
        // Fails closed when the API has no key configured.
        Assert.IsType<UnauthorizedObjectResult>(Authorize(Http(actingUserId: "42", apiKey: null, configuredKey: null)));
    }

    [Fact]
    public void PluginKey_Passes()
    {
        Assert.Null(Authorize(Http(actingUserId: "42")));
    }

    [Fact]
    public void WebUser_Is403()
    {
        var result = Assert.IsType<ObjectResult>(Authorize(Http(apiKey: null, user: LoggedIn(5))));
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task WithoutTheKey_TheActingHeaderIsIgnored()
    {
        // Belt and braces behind the filter: the actor is only taken from X-Acting-User-Id with the key.
        SetRequest(actingUserId: "42", apiKey: null);
        var body = Body();

        await _controller.RecordTeleportAudit(7, body);

        _users.Verify(s => s.RecordTeleportAuditAsync(7, body, null), Times.Once);
    }

    [Fact]
    public async Task NoActingHeader_RecordsWithoutAnActor()
    {
        SetRequest();
        var body = Body();

        await _controller.RecordTeleportAudit(7, body);

        _users.Verify(s => s.RecordTeleportAuditAsync(7, body, null), Times.Once);
    }
}
