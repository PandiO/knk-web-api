using System.Reflection;
using FluentAssertions;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// PandiO/knk-workspace#175: <c>POST api/ItemInstances/{id}/clone</c> is game-server only, attributes the copies to the
/// staff member in X-Acting-User-Id, and maps the service's refusals; the lookup stays web-admin only.
/// </summary>
[Trait("Category", "API")]
public class ItemInstancesControllerTests
{
    private static MethodInfo Action(string name) =>
        typeof(ItemInstancesController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Single(m => m.Name == name);

    [Fact]
    public void Clone_IsPluginOnly_AndGetStaysWebAdmin()
    {
        Action(nameof(ItemInstancesController.Clone)).GetCustomAttribute<RequirePluginServiceAttribute>().Should().NotBeNull();
        Action(nameof(ItemInstancesController.GetById)).GetCustomAttributes<RequirePermissionAttribute>()
            .Select(a => (string)a.Arguments![0]).Should().Equal(StaffPermissions.ManageLootboxes);
    }

    private static ItemInstancesController Controller(Mock<IItemInstanceService> service, int? actingUser)
    {
        var http = new DefaultHttpContext();
        // The caller resolved for this request (as PluginServiceAuth caches it): the plugin naming a staff member.
        http.Items["knk.caller"] = new KnkCaller(true, false, null, actingUser);
        return new ItemInstancesController(service.Object) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    [Fact]
    public async Task Clone_PassesTheActingStaffMember()
    {
        var service = new Mock<IItemInstanceService>();
        service.Setup(s => s.CloneAsync(5, 2, 9, 42))
            .ReturnsAsync(new ItemInstanceCloneResultDto { SourceItemInstanceId = 5, ItemInstanceIds = new() { 6, 7 } });

        var ok = (await Controller(service, 42).Clone(5, new ItemInstanceCloneRequestDto { Count = 2, OwnerUserId = 9 }))
            .Should().BeOfType<OkObjectResult>().Subject;

        ((ItemInstanceCloneResultDto)ok.Value!).ItemInstanceIds.Should().Equal(6, 7);
    }

    [Fact]
    public async Task Clone_MapsRefusals()
    {
        var service = new Mock<IItemInstanceService>();
        service.Setup(s => s.CloneAsync(1, 1, null, null)).ThrowsAsync(new KeyNotFoundException("gone"));
        service.Setup(s => s.CloneAsync(2, 1, null, null)).ThrowsAsync(new InvalidOperationException("stackable"));
        service.Setup(s => s.CloneAsync(3, 1, null, null)).ThrowsAsync(new ArgumentException("count"));
        var controller = Controller(service, null);
        var one = new ItemInstanceCloneRequestDto { Count = 1 };

        (await controller.Clone(1, one)).Should().BeOfType<NotFoundObjectResult>();
        (await controller.Clone(2, one)).Should().BeOfType<ConflictObjectResult>();
        (await controller.Clone(3, one)).Should().BeOfType<BadRequestObjectResult>();
        (await controller.Clone(3, null!)).Should().BeOfType<BadRequestResult>();
    }
}
