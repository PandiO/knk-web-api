using System.Security.Claims;
using Xunit;
using Moq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// The plugin-key, own-data or node gate on per-player reads (a player's discovery progress and
/// summary): the game server, the player themself and staff pass; anonymous callers and other
/// players don't.
/// </summary>
public class RequireServiceSelfOrPermissionAttributeTests
{
    private const string Node = "knk.admin.discovery";
    private readonly Mock<IPermissionResolutionService> _permissions = new();

    private static AuthorizationFilterContext Context(ClaimsPrincipal? user = null, string? sentKey = null, object? routeUserId = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [PluginServiceAuth.ApiKeyConfigKey] = "secret"
        }).Build();
        var environment = new Mock<IHostEnvironment>();
        environment.Setup(e => e.EnvironmentName).Returns(Environments.Production);
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IConfiguration))).Returns(configuration);
        services.Setup(s => s.GetService(typeof(IHostEnvironment))).Returns(environment.Object);

        var http = new DefaultHttpContext { RequestServices = services.Object };
        if (user != null) http.User = user;
        if (sentKey != null) http.Request.Headers[PluginServiceAuth.ApiKeyHeader] = sentKey;
        var routeData = new RouteData();
        if (routeUserId != null) routeData.Values["userId"] = routeUserId;
        return new AuthorizationFilterContext(new ActionContext(http, routeData, new ActionDescriptor()), new List<IFilterMetadata>());
    }

    private static ClaimsPrincipal LoggedIn(int userId) =>
        new(new ClaimsIdentity(new[] { new Claim("uid", userId.ToString()) }, "Bearer"));

    private void Resolves(int userId, PermissionResolutionResult result) =>
        _permissions.Setup(p => p.CheckAsync(userId, Node))
            .ReturnsAsync(new PermissionCheckResponseDto { UserId = userId, Node = Node, Result = result });

    private async Task<int?> Run(AuthorizationFilterContext context)
    {
        await new RequireServiceSelfOrPermissionFilter(Node, "userId", _permissions.Object).OnAuthorizationAsync(context);
        return context.Result switch
        {
            null => null,
            UnauthorizedObjectResult => StatusCodes.Status401Unauthorized,
            ObjectResult o => o.StatusCode,
            _ => -1
        };
    }

    [Fact]
    public async Task ThePlugin_Passes()
    {
        Assert.Null(await Run(Context(sentKey: "secret", routeUserId: "7")));
        _permissions.Verify(p => p.CheckAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Anonymous_OrAWrongKey_Is401()
    {
        Assert.Equal(401, await Run(Context(routeUserId: "7")));
        Assert.Equal(401, await Run(Context(sentKey: "guess", routeUserId: "7")));
    }

    [Fact]
    public async Task ThePlayerThemself_PassesWithoutTheNode()
    {
        Assert.Null(await Run(Context(user: LoggedIn(7), routeUserId: "7")));
        Assert.Null(await Run(Context(user: LoggedIn(7), routeUserId: 7)));
        _permissions.Verify(p => p.CheckAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(PermissionResolutionResult.Denied)]
    [InlineData(PermissionResolutionResult.Undeclared)]
    public async Task AnotherPlayer_WithoutTheNode_Is403(PermissionResolutionResult result)
    {
        Resolves(5, result);
        Assert.Equal(403, await Run(Context(user: LoggedIn(5), routeUserId: "7")));
    }

    [Fact]
    public async Task Staff_WithTheNode_Pass()
    {
        Resolves(5, PermissionResolutionResult.Granted);
        Assert.Null(await Run(Context(user: LoggedIn(5), routeUserId: "7")));
    }

    [Fact]
    public async Task NoUserIdInTheRoute_FallsBackToTheNode()
    {
        Resolves(5, PermissionResolutionResult.Denied);
        Assert.Equal(403, await Run(Context(user: LoggedIn(5))));
    }
}
