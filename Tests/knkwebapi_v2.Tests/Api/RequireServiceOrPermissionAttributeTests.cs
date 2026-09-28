using System.Reflection;
using System.Security.Claims;
using Xunit;
using Moq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// KNG-22 (currency-payments Phase 0): the plugin-key-or-node gate on every currency, user-admin,
/// kit and config write. These run the filters directly; <see cref="ProtectedRoutes_CarryTheirGate"/>
/// pins which routes carry which gate, so together they cover "anonymous → 401 on every
/// protected route" without a live server.
/// </summary>
public class RequireServiceOrPermissionAttributeTests
{
    private const string Node = "knk.admin.user.manage";
    private readonly Mock<IPermissionResolutionService> _permissions = new();

    private static HttpContext Http(ClaimsPrincipal? user = null, string? sentKey = null, string? configuredKey = null,
        bool development = false, bool allowUnauthenticated = false, string? actingUserId = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [PluginServiceAuth.ApiKeyConfigKey] = configuredKey,
            [PluginServiceAuth.AllowUnauthenticatedConfigKey] = allowUnauthenticated ? "true" : "false"
        }).Build();
        var environment = new Mock<IHostEnvironment>();
        environment.Setup(e => e.EnvironmentName).Returns(development ? Environments.Development : Environments.Production);
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IConfiguration))).Returns(configuration);
        services.Setup(s => s.GetService(typeof(IHostEnvironment))).Returns(environment.Object);

        var http = new DefaultHttpContext { RequestServices = services.Object };
        if (user != null) http.User = user;
        if (sentKey != null) http.Request.Headers[PluginServiceAuth.ApiKeyHeader] = sentKey;
        if (actingUserId != null) http.Request.Headers[PluginServiceAuth.ActingUserHeader] = actingUserId;
        return http;
    }

    private static AuthorizationFilterContext Context(HttpContext http) =>
        new(new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>());

    private static ClaimsPrincipal LoggedIn(int userId) =>
        new(new ClaimsIdentity(new[] { new Claim("uid", userId.ToString()) }, "Bearer"));

    private void Resolves(int userId, PermissionResolutionResult result) =>
        _permissions.Setup(p => p.CheckAsync(userId, Node))
            .ReturnsAsync(new PermissionCheckResponseDto { UserId = userId, Node = Node, Result = result });

    private async Task<IActionResult?> RunServiceOrPermission(HttpContext http)
    {
        var context = Context(http);
        await new RequireServiceOrPermissionFilter(Node, _permissions.Object).OnAuthorizationAsync(context);
        return context.Result;
    }

    private static IActionResult? RunPluginOnly(HttpContext http)
    {
        var context = Context(http);
        new RequirePluginServiceAttribute().OnAuthorization(context);
        return context.Result;
    }

    private static int? Status(IActionResult? result) => result switch
    {
        null => null,
        UnauthorizedObjectResult => StatusCodes.Status401Unauthorized,
        ObjectResult o => o.StatusCode,
        _ => -1
    };

    // ===== RequireServiceOrPermission =====

    [Fact]
    public async Task CorrectKey_Passes()
    {
        Assert.Null(await RunServiceOrPermission(Http(sentKey: "secret", configuredKey: "secret")));
        _permissions.Verify(p => p.CheckAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task WrongKey_Is401()
    {
        Assert.Equal(401, Status(await RunServiceOrPermission(Http(sentKey: "guess", configuredKey: "secret"))));
    }

    [Fact]
    public async Task NoKeySent_Is401()
    {
        Assert.Equal(401, Status(await RunServiceOrPermission(Http(configuredKey: "secret"))));
    }

    [Fact]
    public async Task KeyUnsetOnTheApi_FailsClosed()
    {
        // The shipped default: an empty Security:PluginApiKey must not mean "trust everyone".
        Assert.Equal(401, Status(await RunServiceOrPermission(Http(sentKey: ""))));
        Assert.Equal(401, Status(await RunServiceOrPermission(Http(sentKey: "anything"))));
    }

    [Fact]
    public async Task KeyUnset_DevelopmentOptOut_Passes()
    {
        Assert.Null(await RunServiceOrPermission(Http(development: true, allowUnauthenticated: true)));
    }

    [Fact]
    public async Task KeyUnset_OptOutOutsideDevelopment_StillFailsClosed()
    {
        Assert.Equal(401, Status(await RunServiceOrPermission(Http(development: false, allowUnauthenticated: true))));
    }

    [Fact]
    public async Task KeyUnset_DevelopmentWithoutOptOut_StillFailsClosed()
    {
        Assert.Equal(401, Status(await RunServiceOrPermission(Http(development: true, allowUnauthenticated: false))));
    }

    [Fact]
    public async Task KeySet_DevelopmentOptOutDoesNotBypassIt()
    {
        Assert.Equal(401, Status(await RunServiceOrPermission(Http(configuredKey: "secret", development: true, allowUnauthenticated: true))));
    }

    [Fact]
    public async Task WebUserWithTheNode_Passes()
    {
        Resolves(5, PermissionResolutionResult.Granted);
        Assert.Null(await RunServiceOrPermission(Http(user: LoggedIn(5), configuredKey: "secret")));
    }

    [Theory]
    [InlineData(PermissionResolutionResult.Denied)]
    [InlineData(PermissionResolutionResult.Undeclared)]
    public async Task WebUserWithoutTheNode_Is403(PermissionResolutionResult result)
    {
        Resolves(5, result);
        Assert.Equal(403, Status(await RunServiceOrPermission(Http(user: LoggedIn(5), configuredKey: "secret"))));
    }

    // ===== RequirePluginService =====

    [Fact]
    public void PluginOnly_CorrectKey_Passes()
    {
        Assert.Null(RunPluginOnly(Http(sentKey: "secret", configuredKey: "secret")));
    }

    [Fact]
    public void PluginOnly_Anonymous_Is401()
    {
        Assert.Equal(401, Status(RunPluginOnly(Http(configuredKey: "secret"))));
        Assert.Equal(401, Status(RunPluginOnly(Http())));
    }

    [Fact]
    public void PluginOnly_WebUser_Is403()
    {
        Assert.Equal(403, Status(RunPluginOnly(Http(user: LoggedIn(5), configuredKey: "secret"))));
    }

    // ===== Caller / actor =====

    [Fact]
    public void ActingHeader_IsIgnoredWithoutTheKey()
    {
        Assert.Null(Http(configuredKey: "secret", actingUserId: "42").GetKnkCaller().ActorUserId);
        Assert.Null(Http(sentKey: "guess", configuredKey: "secret", actingUserId: "42").GetKnkCaller().ActorUserId);
        Assert.Null(Http(actingUserId: "42").GetKnkCaller().ActorUserId);
    }

    [Fact]
    public void ActingHeader_CountsWithTheKey()
    {
        var caller = Http(sentKey: "secret", configuredKey: "secret", actingUserId: "42").GetKnkCaller();
        Assert.True(caller.IsPluginService);
        Assert.Equal(42, caller.ActorUserId);
    }

    [Fact]
    public void WebUser_IsAlwaysTheirOwnActor()
    {
        var caller = Http(user: LoggedIn(5), sentKey: "secret", configuredKey: "secret", actingUserId: "42").GetKnkCaller();
        Assert.Equal(5, caller.ActorUserId);
    }

    // ===== Which routes carry which gate =====

    public static IEnumerable<object[]> ProtectedRoutes() => new[]
    {
        // UsersController
        R(typeof(UsersController), nameof(UsersController.Update), "knk.admin.user.manage"),
        R(typeof(UsersController), nameof(UsersController.Delete), "knk.admin.user.manage"),
        R(typeof(UsersController), nameof(UsersController.AdjustBalances), "knk.admin.user.manage"),
        R(typeof(UsersController), nameof(UsersController.UpdatePersonalMultipliers), "knk.admin.user.salary"),
        R(typeof(UsersController), nameof(UsersController.PayOutSalary), "knk.admin.user.salary"),
        R(typeof(UsersController), nameof(UsersController.MergeAccounts), "knk.admin.user.manage"),
        R(typeof(UsersController), nameof(UsersController.Freeze), "knk.freeze"),
        R(typeof(UsersController), nameof(UsersController.Unfreeze), "knk.unfreeze"),
        R(typeof(UsersController), nameof(UsersController.ChangePassword), "knk.admin.user.manage"),
        R(typeof(UsersController), nameof(UsersController.UpdateEmail), "knk.admin.user.manage"),
        R(typeof(UsersController), nameof(UsersController.UpdatePresence), null),
        R(typeof(UsersController), nameof(UsersController.UpdateActiveMode), null),
        R(typeof(UsersController), nameof(UsersController.UpdateGatePassThroughMethod), null),
        R(typeof(UsersController), nameof(UsersController.RecordTeleportAudit), null),
        // KitsController
        R(typeof(KitsController), nameof(KitsController.Create), "knk.kit.manage"),
        R(typeof(KitsController), nameof(KitsController.Update), "knk.kit.manage"),
        R(typeof(KitsController), nameof(KitsController.Delete), "knk.kit.manage"),
        R(typeof(KitsController), nameof(KitsController.Give), "knk.kit.give"),
        R(typeof(KitsController), nameof(KitsController.Claim), null),
        R(typeof(KitsController), nameof(KitsController.Purchase), null),
        R(typeof(KitsController), nameof(KitsController.GrantFirstJoin), null),
        // Config and permissions
        R(typeof(SalaryConfigurationController), nameof(SalaryConfigurationController.Update), "knk.admin.currency.policy"),
        R(typeof(AuditLogRetentionConfigurationController), nameof(AuditLogRetentionConfigurationController.Update), "knk.admin.config"),
        R(typeof(KnKWebAPI.Controllers.PermissionGroupsController), "Create", "knk.admin.user.perm"),
        R(typeof(KnKWebAPI.Controllers.PermissionGroupsController), "Update", "knk.admin.user.perm"),
        R(typeof(KnKWebAPI.Controllers.PermissionGroupsController), "Delete", "knk.admin.user.perm"),
        R(typeof(KnKWebAPI.Controllers.PermissionGrantsController), "Create", "knk.admin.user.perm"),
        R(typeof(KnKWebAPI.Controllers.PermissionGrantsController), "Update", "knk.admin.user.perm"),
        R(typeof(KnKWebAPI.Controllers.PermissionGrantsController), "Delete", "knk.admin.user.perm"),
        R(typeof(KnKWebAPI.Controllers.PermissionGrantsController), "UpsertByNode", "knk.admin.user.perm"),
        R(typeof(KnKWebAPI.Controllers.PermissionGrantsController), "RevokeByNode", "knk.admin.user.perm"),
        R(typeof(KnKWebAPI.Controllers.UserPermissionGroupsController), "Upsert", "knk.admin.user.group"),
        R(typeof(KnKWebAPI.Controllers.UserPermissionGroupsController), "Delete", "knk.admin.user.group"),
    };

    /// <summary>node null = plugin-only (RequirePluginService).</summary>
    private static object[] R(Type controller, string action, string? node) => new object[] { controller, action, node! };

    [Theory]
    [MemberData(nameof(ProtectedRoutes))]
    public void ProtectedRoutes_CarryTheirGate(Type controller, string action, string? node)
    {
        var method = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance).Single(m => m.Name == action);
        if (node == null)
        {
            Assert.NotNull(method.GetCustomAttribute<RequirePluginServiceAttribute>());
        }
        else
        {
            var gates = method.GetCustomAttributes<RequireServiceOrPermissionAttribute>().Select(a => a.Node).ToList();
            Assert.Equal(new[] { node }, gates);
        }
    }

    [Fact]
    public void SetCoinsRoutes_AreGone()
    {
        // PUT api/Users/{id}/coins and PUT api/Users/{uuid}/coins set any balance, negatives
        // included, with no audit entry (currency DESIGN.md §1.4 A1/A12).
        var coinRoutes = typeof(UsersController).GetMethods()
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>())
            .Where(a => a.Template != null && a.Template.EndsWith("/coins", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(coinRoutes);
    }
}
