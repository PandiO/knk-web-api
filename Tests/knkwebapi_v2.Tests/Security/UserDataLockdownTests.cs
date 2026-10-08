using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Moq;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using knkwebapi_v2.Tests.Api;
using Xunit;

namespace knkwebapi_v2.Tests.Security;

/// <summary>
/// Closed-alpha hardening WP2: player data (emails, permissions, the player list) is no longer
/// public. Staff (knk.admin.user.manage) and the game server see everything; a player sees
/// their own record and permission checks only.
/// </summary>
public class UserDataLockdownTests
{
    private const int Player = 7;
    private const int Other = 8;
    private readonly Mock<IPermissionResolutionService> _permissions = new();

    private static MethodInfo Action(string name) =>
        typeof(UsersController).GetMethods().Single(m => m.Name == name);

    private async Task<int?> Run(string action, HttpContext http, object? routeId = null)
    {
        var method = Action(action);
        var routeData = new RouteData();
        if (routeId != null) routeData.Values["id"] = routeId;
        foreach (var gate in method.GetCustomAttributes(true))
        {
            var context = new AuthorizationFilterContext(new ActionContext(http, routeData, new ActionDescriptor()), new List<IFilterMetadata>());
            switch (gate)
            {
                case RequireServiceSelfOrPermissionAttribute self:
                    await new RequireServiceSelfOrPermissionFilter(self.Node, self.UserIdRouteKey, _permissions.Object).OnAuthorizationAsync(context);
                    break;
                case RequireServiceOrPermissionAttribute node:
                    await new RequireServiceOrPermissionFilter(node.Node, _permissions.Object).OnAuthorizationAsync(context);
                    break;
                default:
                    continue;
            }
            if (context.Result != null)
            {
                return context.Result is UnauthorizedObjectResult ? 401 : (context.Result as ObjectResult)?.StatusCode;
            }
        }
        return null;
    }

    private void Staff(int userId) =>
        _permissions.Setup(p => p.CheckAsync(userId, StaffPermissions.ManageUsers))
            .ReturnsAsync(new PermissionCheckResponseDto { UserId = userId, Node = StaffPermissions.ManageUsers, Result = PermissionResolutionResult.Granted });

    [Fact]
    public async Task UserList_IsStaffOrPluginOnly()
    {
        Assert.Equal(401, await Run(nameof(UsersController.GetAll), ServiceAuthTestHelper.Anonymous()));
        Assert.Equal(403, await Run(nameof(UsersController.GetAll), ServiceAuthTestHelper.WebUser(Player)));
        Assert.Null(await Run(nameof(UsersController.GetAll), ServiceAuthTestHelper.Plugin()));
        Staff(Other);
        Assert.Null(await Run(nameof(UsersController.GetAll), ServiceAuthTestHelper.WebUser(Other)));
    }

    [Theory]
    [InlineData(nameof(UsersController.GetUserById))]
    [InlineData(nameof(UsersController.CheckPermission))]
    [InlineData(nameof(UsersController.GetEffectivePermissions))]
    public async Task OwnRecord_Self_Passes_OtherPlayers_Are403(string action)
    {
        Assert.Null(await Run(action, ServiceAuthTestHelper.WebUser(Player), Player));
        Assert.Equal(403, await Run(action, ServiceAuthTestHelper.WebUser(Player), Other));
        Assert.Equal(401, await Run(action, ServiceAuthTestHelper.Anonymous(), Player));
        Assert.Null(await Run(action, ServiceAuthTestHelper.Plugin(), Other));
        Staff(Other);
        Assert.Null(await Run(action, ServiceAuthTestHelper.WebUser(Other), Player));
    }

    [Theory]
    [InlineData(nameof(UsersController.Create))]
    [InlineData(nameof(UsersController.SearchUsers))]
    public async Task CreateAndSearch_NeedStaffOrPlugin(string action)
    {
        Assert.Equal(403, await Run(action, ServiceAuthTestHelper.WebUser(Player)));
        Assert.Null(await Run(action, ServiceAuthTestHelper.Plugin()));
    }

    [Fact]
    public void PublicUserSummary_HasNoEmail()
    {
        Assert.Null(typeof(UserSummaryDto).GetProperty("Email", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase));
    }
}
