using Moq;
using Xunit;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// KNG-80: every Location retention route has its own explicit permission check, server side,
/// so nothing relies on anonymous access (the alpha-hardening default-deny filter comes later).
/// </summary>
public class LocationRetentionControllerTests
{
    private readonly Mock<IPermissionResolutionService> _permissions = new();

    private void Holds(int userId, string node) =>
        _permissions.Setup(p => p.CheckAsync(userId, node)).ReturnsAsync(new PermissionCheckResponseDto
        {
            UserId = userId,
            Node = node,
            Result = PermissionResolutionResult.Granted
        });

    private Task<int?> Gates(string action, Microsoft.AspNetCore.Http.HttpContext http) =>
        ServiceAuthTestHelper.RunGates(typeof(LocationRetentionController), action, http, _permissions.Object);

    public static IEnumerable<object[]> Routes() => new[]
    {
        new object[] { nameof(LocationRetentionController.GetOrphans), StaffPermissions.LocationOrphansView, true },
        new object[] { nameof(LocationRetentionController.GetTeleportTarget), StaffPermissions.LocationTeleport, true },
        new object[] { nameof(LocationRetentionController.Keep), StaffPermissions.LocationOrphansKeep, false },
        new object[] { nameof(LocationRetentionController.Delete), StaffPermissions.LocationOrphansDelete, false },
        new object[] { nameof(LocationRetentionController.GetStatus), StaffPermissions.LocationOrphansView, false },
        new object[] { nameof(LocationRetentionController.Run), StaffPermissions.LocationOrphansRun, false },
        new object[] { nameof(LocationRetentionController.UpdateSettings), StaffPermissions.LocationRetentionSettings, false },
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Anonymous_Is401(string action, string node, bool pluginCalls)
    {
        _ = (node, pluginCalls);
        Assert.Equal(401, await Gates(action, ServiceAuthTestHelper.Anonymous()));
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task WebUser_WithoutTheNode_Is403(string action, string node, bool pluginCalls)
    {
        _ = pluginCalls;
        // Holding every other Location retention node is not enough.
        foreach (var other in Routes().Select(r => (string)r[1]).Where(n => n != node).Distinct()) Holds(5, other);
        Assert.Equal(403, await Gates(action, ServiceAuthTestHelper.WebUser(5)));
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task WebUser_WithTheNode_Passes(string action, string node, bool pluginCalls)
    {
        _ = pluginCalls;
        Holds(5, node);
        Assert.Null(await Gates(action, ServiceAuthTestHelper.WebUser(5)));
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task PluginKey_PassesOnlyTheRoutesTheGameServerCalls(string action, string node, bool pluginCalls)
    {
        _ = node;
        var status = await Gates(action, ServiceAuthTestHelper.Plugin(actingUserId: 5));
        if (pluginCalls) Assert.Null(status);
        else Assert.NotNull(status);
    }

    [Fact]
    public void DeleteHasItsOwnNode_SeparateFromKeep()
    {
        Assert.NotEqual(StaffPermissions.LocationOrphansKeep, StaffPermissions.LocationOrphansDelete);
        Assert.StartsWith("knk.admin.location.", StaffPermissions.LocationOrphansDelete);
    }

    [Fact]
    public async Task Delete_NoLongerOrphan_Is409WithTheResult()
    {
        var service = new Mock<knkwebapi_v2.Services.Interfaces.ILocationRetentionService>();
        service.Setup(s => s.DeleteAsync(3, 5, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationOrphanDeleteResultDto { Outcome = "NoLongerOrphan", Message = "Not deleted", Item = new LocationOrphanDto() });
        var controller = new LocationRetentionController(service.Object)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = ServiceAuthTestHelper.WebUser(5) }
        };

        var result = await controller.Delete(3, null, CancellationToken.None);

        var conflict = Assert.IsType<Microsoft.AspNetCore.Mvc.ConflictObjectResult>(result);
        Assert.IsType<LocationOrphanDeleteResultDto>(conflict.Value);
    }
}
