using System.Reflection;
using KnKWebAPI.Controllers;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using knkwebapi_v2.Tests.Api;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Security;

/// <summary>
/// PandiO/knk-workspace#408: the dashboard's PermissionGrant table reads through <c>POST api/PermissionGrants/search</c>,
/// so the search carries the same read node as <c>GET</c> and <c>GET {id}</c>: the type's view node in
/// <see cref="DashboardAccess"/> (<c>knk.admin.user.manage</c>). Writes stay on <c>knk.admin.user.perm</c>.
/// </summary>
public class PermissionGrantReadGateTests
{
    private static readonly string[] Reads = { "GetAll", "GetById", "Search" };
    private static readonly string[] Writes = { "Create", "Update", "Delete", "UpsertByNode", "RevokeByNode" };

    private static IReadOnlyList<string> GatesOf(string action) =>
        typeof(PermissionGrantsController).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m => m.Name == action)
            .GetCustomAttributes<RequireServiceOrPermissionAttribute>()
            .Select(a => a.Node)
            .ToList();

    [Fact]
    public void EveryRead_CarriesTheTypesReadNodes()
    {
        var access = DashboardAccess.Find("PermissionGrant");
        Assert.NotNull(access);
        Assert.Equal(new[] { StaffPermissions.ManageUsers }, access!.ReadNodes);

        foreach (var action in Reads)
        {
            Assert.Equal(access.ReadNodes, GatesOf(action));
        }
    }

    [Fact]
    public void EveryWrite_StaysOnTheWriteNode()
    {
        var access = DashboardAccess.Find("PermissionGrant")!;
        Assert.Equal(StaffPermissions.UserPermissions, access.WriteNode);

        foreach (var action in Writes)
        {
            Assert.Equal(new[] { StaffPermissions.UserPermissions }, GatesOf(action));
        }
    }

    [Fact]
    public void TheDashboardOffersNoDeleteForGrants()
    {
        // ViewOnly: grants are removed through the by-node routes and the profile page, not the dashboard's Delete.
        var access = DashboardAccess.Find("PermissionGrant")!;
        Assert.Null(access.Delete);
        Assert.False(access.BulkDeletable);
    }

    [Theory]
    [InlineData("GetAll")]
    [InlineData("GetById")]
    [InlineData("Search")]
    public async Task ARead_PassesForAWebUserWithManageUsersOnly(string action)
    {
        // Only knk.admin.user.manage is granted; asking for knk.admin.user.perm would fail the read.
        Assert.Null(await ServiceAuthTestHelper.RunGates(typeof(PermissionGrantsController), action,
            ServiceAuthTestHelper.WebUser(UserId), Holding(StaffPermissions.ManageUsers)));
    }

    [Theory]
    [InlineData("GetAll")]
    [InlineData("GetById")]
    [InlineData("Search")]
    public async Task ARead_Is403ForAWebUserWithTheWriteNodeOnly(string action)
    {
        Assert.Equal(StatusCodes.Status403Forbidden, await ServiceAuthTestHelper.RunGates(typeof(PermissionGrantsController),
            action, ServiceAuthTestHelper.WebUser(UserId), Holding(StaffPermissions.UserPermissions)));
    }

    [Fact]
    public async Task Search_IsStill401WhenAnonymousAndOpenToThePluginKey()
    {
        Assert.Equal(StatusCodes.Status401Unauthorized,
            await ServiceAuthTestHelper.RunGates(typeof(PermissionGrantsController), "Search", ServiceAuthTestHelper.Anonymous()));
        Assert.Null(await ServiceAuthTestHelper.RunGates(typeof(PermissionGrantsController), "Search", ServiceAuthTestHelper.Plugin()));
    }

    private const int UserId = 12;

    /// <summary>A resolver that grants exactly <paramref name="node"/> to <see cref="UserId"/>.</summary>
    private static IPermissionResolutionService Holding(string node)
    {
        var permissions = new Mock<IPermissionResolutionService>();
        permissions.Setup(p => p.CheckAsync(UserId, It.IsAny<string>()))
            .ReturnsAsync((int userId, string asked) => new PermissionCheckResponseDto
            {
                UserId = userId,
                Node = asked,
                Result = asked == node ? PermissionResolutionResult.Granted : PermissionResolutionResult.Undeclared
            });
        return permissions.Object;
    }
}
