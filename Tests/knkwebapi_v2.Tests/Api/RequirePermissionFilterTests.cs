using System.Security.Claims;
using Xunit;
using Moq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Tests.Api;

/// <summary>Staff-only gate for the web moderation endpoints (developer request 2026-09-26).</summary>
public class RequirePermissionFilterTests
{
    private readonly Mock<IPermissionResolutionService> _permissions = new();

    private AuthorizationFilterContext Context(ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext { User = user };
        return new AuthorizationFilterContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>());
    }

    private static ClaimsPrincipal LoggedIn(int userId) =>
        new(new ClaimsIdentity(new[] { new Claim("uid", userId.ToString()) }, "Bearer"));

    private void Resolves(int userId, PermissionResolutionResult result) =>
        _permissions.Setup(p => p.CheckAsync(userId, StaffPermissions.ManageUsers))
            .ReturnsAsync(new PermissionCheckResponseDto { UserId = userId, Node = StaffPermissions.ManageUsers, Result = result });

    [Fact]
    public async Task Anonymous_IsUnauthorized()
    {
        var context = Context(new ClaimsPrincipal(new ClaimsIdentity()));

        await new RequirePermissionFilter(StaffPermissions.ManageUsers, _permissions.Object).OnAuthorizationAsync(context);

        Assert.IsType<UnauthorizedObjectResult>(context.Result);
        _permissions.Verify(p => p.CheckAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(PermissionResolutionResult.Denied)]
    [InlineData(PermissionResolutionResult.Undeclared)]
    public async Task LoggedInWithoutTheNode_IsForbidden(PermissionResolutionResult result)
    {
        Resolves(5, result);
        var context = Context(LoggedIn(5));

        await new RequirePermissionFilter(StaffPermissions.ManageUsers, _permissions.Object).OnAuthorizationAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(context.Result).StatusCode);
    }

    [Fact]
    public async Task Staff_PassesThrough()
    {
        Resolves(5, PermissionResolutionResult.Granted);
        var context = Context(LoggedIn(5));

        await new RequirePermissionFilter(StaffPermissions.ManageUsers, _permissions.Object).OnAuthorizationAsync(context);

        Assert.Null(context.Result);
    }
}
