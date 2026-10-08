using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Tests.Api;
using Xunit;

namespace knkwebapi_v2.Tests.Security;

/// <summary>
/// Closed-alpha hardening WP1.4: the default-deny contract, checked by reflection over every
/// controller so a new endpoint can't ship open. Every write (non-GET) action needs an access
/// rule (RequirePermission, RequireServiceOrPermission[ForWrites], RequirePluginService,
/// RequireServiceSelfOrPermission, RequireServiceOrLoggedIn, Authorize) or [AllowAnonymous], and
/// [AllowAnonymous] itself is limited to the list below.
/// </summary>
public class EndpointAccessRulesTests
{
    /// <summary>The only actions anyone may call without the plugin key or a login.</summary>
    private static readonly HashSet<string> AnonymousAllowList = new()
    {
        "AuthController.Login",
        "AuthController.Refresh",
        "AuthController.Logout",
        "AuthController.ForgotPassword",
        "AuthController.ResetPassword",
        "AuthController.ValidateToken",
        "HealthController.GetHealth",
        "HealthCheckController.GetLiveness",
        "HealthCheckController.GetReadiness",
        "UsersController.ValidateLinkCode",
        "UsersController.CheckDuplicateAvailability",
        "UsersController.CheckDuplicate",
    };

    private sealed record Endpoint(Type Controller, MethodInfo Action, IReadOnlyList<string> Verbs)
    {
        public string Name => $"{Controller.Name}.{Action.Name}";
        public IEnumerable<object> Metadata =>
            Controller.GetCustomAttributes(true).Concat(Action.GetCustomAttributes(true));
    }

    private static IEnumerable<Endpoint> Endpoints()
    {
        var assembly = typeof(UsersController).Assembly;
        foreach (var controller in assembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract))
        {
            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var verbs = action.GetCustomAttributes<HttpMethodAttribute>().SelectMany(a => a.HttpMethods).Distinct().ToList();
                if (verbs.Count > 0)
                {
                    yield return new Endpoint(controller, action, verbs);
                }
            }
        }
    }

    [Fact]
    public void EveryWriteAction_HasAnAccessRule()
    {
        var offenders = Endpoints()
            .Where(e => e.Verbs.Any(v => !DefaultCallerRequiredFilter.IsRead(v)))
            .Where(e => !DefaultCallerRequiredFilter.HasAccessRule(e.Metadata))
            .Select(e => $"{e.Name} [{string.Join(",", e.Verbs)}]")
            .OrderBy(n => n)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Write actions without an access rule (add RequirePermission, RequireServiceOrPermission, RequirePluginService, " +
            "RequireServiceSelfOrPermission, Authorize or AllowAnonymous):\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void AllowAnonymous_IsLimitedToTheAllowList()
    {
        var anonymous = Endpoints()
            .Where(e => e.Metadata.OfType<IAllowAnonymous>().Any())
            .Select(e => e.Name)
            .ToHashSet();

        Assert.Empty(anonymous.Except(AnonymousAllowList).OrderBy(n => n));
        Assert.Empty(AnonymousAllowList.Except(anonymous).OrderBy(n => n));
    }

    [Theory]
    [InlineData("TestDisplayController")]
    [InlineData("WeatherForecastController")]
    public void DebugControllers_AreGone(string name)
    {
        Assert.DoesNotContain(typeof(UsersController).Assembly.GetTypes(), t => t.Name == name);
    }

    [Fact]
    public void LinkAccountEndpoint_IsGone()
    {
        Assert.DoesNotContain(Endpoints(), e => e.Name == "UsersController.LinkAccount");
    }

    // ===== DefaultCallerRequiredFilter at runtime =====

    private static async Task<int?> RunFilter(HttpContext http, string method, params object[] metadata)
    {
        http.Request.Method = method;
        var descriptor = new ActionDescriptor { EndpointMetadata = metadata.ToList(), DisplayName = "Test.Action" };
        var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), descriptor), new List<IFilterMetadata>());
        await new DefaultCallerRequiredFilter(NullLogger<DefaultCallerRequiredFilter>.Instance).OnAuthorizationAsync(context);
        return context.Result switch
        {
            null => null,
            UnauthorizedObjectResult => StatusCodes.Status401Unauthorized,
            ObjectResult o => o.StatusCode,
            _ => -1
        };
    }

    [Fact]
    public async Task Anonymous_Read_Is401()
    {
        Assert.Equal(401, await RunFilter(ServiceAuthTestHelper.Anonymous(), "GET"));
    }

    [Fact]
    public async Task Anonymous_WithAllowAnonymous_Passes()
    {
        Assert.Null(await RunFilter(ServiceAuthTestHelper.Anonymous(), "POST", new AllowAnonymousAttribute()));
    }

    [Fact]
    public async Task LoggedIn_Read_WithoutRule_Passes()
    {
        Assert.Null(await RunFilter(ServiceAuthTestHelper.WebUser(5), "GET"));
    }

    [Fact]
    public async Task Plugin_Read_WithoutRule_Passes()
    {
        Assert.Null(await RunFilter(ServiceAuthTestHelper.Plugin(), "GET"));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Write_WithoutRule_Is403_EvenForThePlugin(string method)
    {
        Assert.Equal(403, await RunFilter(ServiceAuthTestHelper.WebUser(5), method));
        Assert.Equal(403, await RunFilter(ServiceAuthTestHelper.Plugin(), method));
    }

    [Fact]
    public async Task Write_WithRule_IsLeftToTheRule()
    {
        Assert.Null(await RunFilter(ServiceAuthTestHelper.WebUser(5), "POST", new RequirePluginServiceAttribute()));
        Assert.Null(await RunFilter(ServiceAuthTestHelper.WebUser(5), "POST", new AuthorizeAttribute()));
    }

    [Fact]
    public async Task Anonymous_Write_WithRule_Is401()
    {
        Assert.Equal(401, await RunFilter(ServiceAuthTestHelper.Anonymous(), "POST", new RequireServiceOrLoggedInAttribute()));
    }
}
