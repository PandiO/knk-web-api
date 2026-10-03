using knkwebapi_v2.Attributes;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// Owner-only nodes need an exact grant (KNG-34 D12, L1-17; link 2 acceptance criterion 8): the
/// real PermissionResolutionService resolves <c>*</c>, <c>knk.*</c> and <c>knk.owner.*</c> to the
/// owner node, and the attribute still refuses them.
/// </summary>
public class RequireOwnerPermissionAttributeTests
{
    private const string Node = OwnerPermissions.TelemetryView;

    private static PermissionResolutionService Resolver(params (int HolderId, string Node, bool Value)[] directGrants)
    {
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => new User { Id = id, Username = "u" + id });
        var grants = new Mock<IPermissionGrantRepository>();
        grants.Setup(r => r.GetActiveGrantsForHolderAsync(It.IsAny<int>(), It.IsAny<DateTime>()))
            .ReturnsAsync((int holder, DateTime _) => directGrants.Where(g => g.HolderId == holder)
                .Select(g => new PermissionGrant { HolderId = g.HolderId, Node = g.Node, Value = g.Value }).ToList());
        var groups = new Mock<IPermissionGroupRepository>();
        groups.Setup(r => r.GetActiveGroupsForUserAsync(It.IsAny<int>(), It.IsAny<DateTime>())).ReturnsAsync(new List<PermissionGroup>());
        return new PermissionResolutionService(users.Object, grants.Object, groups.Object);
    }

    private static async Task<int?> Run(HttpContext http, IPermissionResolutionService permissions)
    {
        var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>());
        await new RequireOwnerPermissionFilter(Node, permissions).OnAuthorizationAsync(context);
        return context.Result switch
        {
            null => null,
            UnauthorizedObjectResult => StatusCodes.Status401Unauthorized,
            ObjectResult o => o.StatusCode,
            _ => -1
        };
    }

    [Fact]
    public async Task ExactGrant_Passes_ForTheWebUserAndThePluginActingUser()
    {
        var permissions = Resolver((7, Node, true));

        Assert.Null(await Run(ServiceAuthTestHelper.WebUser(7), permissions));
        Assert.Null(await Run(ServiceAuthTestHelper.Plugin(actingUserId: 7), permissions));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("knk.*")]
    [InlineData("knk.owner.*")]
    [InlineData("knk.owner.telemetry.*")]
    public async Task WildcardGrants_Get403_ThoughTheyResolveToGranted(string wildcard)
    {
        var permissions = Resolver((7, wildcard, true));
        Assert.True((await permissions.CheckAsync(7, Node))!.Allowed); // the resolver says yes …

        Assert.Equal(403, await Run(ServiceAuthTestHelper.WebUser(7), permissions)); // … the attribute doesn't
    }

    [Fact]
    public async Task ExplicitDeny_AndNoGrant_Get403()
    {
        Assert.Equal(403, await Run(ServiceAuthTestHelper.WebUser(7), Resolver((7, Node, false))));
        Assert.Equal(403, await Run(ServiceAuthTestHelper.WebUser(7), Resolver()));
        Assert.Equal(403, await Run(ServiceAuthTestHelper.WebUser(7), Resolver((8, Node, true)))); // someone else's grant
    }

    [Fact]
    public async Task Anonymous_AndThePluginWithoutAnActingUser_Get401()
    {
        var permissions = Resolver((7, Node, true));

        Assert.Equal(401, await Run(ServiceAuthTestHelper.Anonymous(), permissions));
        Assert.Equal(401, await Run(ServiceAuthTestHelper.Plugin(), permissions));
    }

    [Fact]
    public void OwnerNodes_UseTheOwnerPrefix()
    {
        var nodes = typeof(OwnerPermissions).GetFields().Where(f => f.IsLiteral && f.Name != nameof(OwnerPermissions.Prefix))
            .Select(f => (string)f.GetRawConstantValue()!).ToList();

        Assert.Equal(5, nodes.Count);
        Assert.All(nodes, n => Assert.StartsWith(OwnerPermissions.Prefix, n));
        Assert.Equal("knk.admin.statistics.view", StaffPermissions.ViewStatistics);
    }
}
