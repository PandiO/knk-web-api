using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Moq;
using Xunit;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// The siege configuration writes (SiegeConfiguration, scenarios with their rewards, lobbies,
/// teams, objectives, spawnpoints) and the gate structure overrides take
/// RequireServiceOrPermission (currency-payments Phase 5, the Phase 4 follow-up; currency
/// DESIGN.md §3.4 lists siege rewards among the currency levers): the plugin's key, or a web user
/// holding knk.siege.admin.manage / knk.gate.admin. Anonymous callers get 401 even when the key is
/// unset (fail closed), unlike the old RequirePluginServiceKey.
/// </summary>
[Trait("Category", "API")]
public class SiegeConfigurationAuthTests
{
    private const string Key = "the-plugin-key";
    private const int StaffId = 900;

    private readonly Mock<IPermissionResolutionService> _permissions = new();

    private static DefaultHttpContext Http(bool plugin = false, int? webUserId = null)
    {
        var configuration = new Mock<Microsoft.Extensions.Configuration.IConfiguration>();
        configuration.Setup(c => c["Security:PluginApiKey"]).Returns(Key);
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(Microsoft.Extensions.Configuration.IConfiguration))).Returns(configuration.Object);
        var httpContext = new DefaultHttpContext { RequestServices = services.Object };
        if (plugin) httpContext.Request.Headers[PluginServiceAuth.ApiKeyHeader] = Key;
        if (webUserId != null)
        {
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("uid", webUserId.Value.ToString()) }, "Test"));
        }
        return httpContext;
    }

    private async Task AssertServiceOrNodeAsync(MethodInfo method, string node)
    {
        var filter = method.GetCustomAttributes<TypeFilterAttribute>().Single();
        Assert.Equal(typeof(RequireServiceOrPermissionFilter), filter.ImplementationType);
        Assert.Equal(node, Assert.Single(filter.Arguments!));

        async Task<IActionResult?> Run(HttpContext http)
        {
            var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>());
            await new RequireServiceOrPermissionFilter(node, _permissions.Object).OnAuthorizationAsync(context);
            return context.Result;
        }

        Assert.Equal(401, ((ObjectResult)(await Run(Http()))!).StatusCode);
        Assert.Equal(403, ((ObjectResult)(await Run(Http(webUserId: StaffId)))!).StatusCode);
        _permissions.Setup(p => p.CheckAsync(StaffId, node)).ReturnsAsync(new PermissionCheckResponseDto { Result = PermissionResolutionResult.Granted });
        Assert.Null(await Run(Http(webUserId: StaffId)));
        Assert.Null(await Run(Http(plugin: true)));
    }

    public static IEnumerable<object[]> SiegeWrites()
    {
        var controllers = new[]
        {
            typeof(SiegeConfigurationController), typeof(KnKWebAPI.Controllers.SiegeLobbiesController),
            typeof(KnKWebAPI.Controllers.SiegeObjectivesController), typeof(KnKWebAPI.Controllers.SiegeScenariosController),
            typeof(KnKWebAPI.Controllers.SiegeSpawnpointsController), typeof(KnKWebAPI.Controllers.SiegeTeamsController)
        };
        foreach (var controller in controllers)
        {
            foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var verbs = method.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().ToList();
                var writes = verbs.Any(v => v is HttpPostAttribute or HttpPutAttribute or HttpDeleteAttribute or HttpPatchAttribute);
                var isSearch = verbs.Any(v => v.Template == "search");
                if (writes && !isSearch)
                {
                    yield return new object[] { controller.Name + "." + method.Name };
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(SiegeWrites))]
    public async Task SiegeConfigurationWrites_NeedTheSiegeManageNode_OrThePluginKey(string action)
    {
        var parts = action.Split('.');
        var type = typeof(SiegeConfigurationController).Assembly.GetTypes().Single(t => t.Name == parts[0]);
        await AssertServiceOrNodeAsync(type.GetMethod(parts[1])!, StaffPermissions.SiegeManage);
    }

    [Fact]
    public async Task GateOverrides_NeedTheGateAdminNode_OrThePluginKey()
    {
        await AssertServiceOrNodeAsync(typeof(KnKWebAPI.Controllers.GateStructuresController).GetMethod("UpdateOverrides")!,
            StaffPermissions.GateAdmin);
    }
}
