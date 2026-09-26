using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// POST /api/users/{id}/teleport-audit (docs/specs/teleport/DESIGN.md §3.10, Phase 2): status codes,
/// and the actor taken from X-Acting-User-Id only as far as the plugin key allows.
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

    private void SetRequest(string? actingUserId = null, string? apiKey = null, string? configuredKey = null)
    {
        var configuration = new Mock<Microsoft.Extensions.Configuration.IConfiguration>();
        configuration.Setup(c => c["Security:PluginApiKey"]).Returns(configuredKey);
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(Microsoft.Extensions.Configuration.IConfiguration))).Returns(configuration.Object);

        var httpContext = new DefaultHttpContext { RequestServices = services.Object };
        if (actingUserId != null) httpContext.Request.Headers[UsersController.ActingUserHeader] = actingUserId;
        if (apiKey != null) httpContext.Request.Headers[UsersController.PluginApiKeyHeader] = apiKey;
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

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
    public async Task PluginKeyConfiguredButMissing_RecordsWithoutAnActor()
    {
        // No service-key attribute on master yet (KNG-22): the call is accepted, but an anonymous
        // caller can't pin the teleport on a staff member.
        SetRequest(actingUserId: "42", configuredKey: "secret");
        var body = Body();

        await _controller.RecordTeleportAudit(7, body);

        _users.Verify(s => s.RecordTeleportAuditAsync(7, body, null), Times.Once);
    }

    [Fact]
    public async Task PluginKeyConfiguredAndSent_TrustsTheActor()
    {
        SetRequest(actingUserId: "42", apiKey: "secret", configuredKey: "secret");
        var body = Body();

        await _controller.RecordTeleportAudit(7, body);

        _users.Verify(s => s.RecordTeleportAuditAsync(7, body, 42), Times.Once);
    }
}
