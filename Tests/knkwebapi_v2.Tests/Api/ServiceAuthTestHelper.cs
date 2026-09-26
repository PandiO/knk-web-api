using System.Reflection;
using System.Security.Claims;
using Moq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// Runs the KNG-22 gates an action carries (<see cref="RequirePluginServiceAttribute"/>,
/// <see cref="RequireServiceOrPermissionAttribute"/>) against a hand-built request, so a
/// controller test can assert "anonymous → 401, key → passes" per route without a live server.
/// </summary>
internal static class ServiceAuthTestHelper
{
    public const string Key = "secret";

    public static HttpContext Anonymous() => Http();

    public static HttpContext Plugin(int? actingUserId = null) =>
        Http(sentKey: Key, actingUserId: actingUserId?.ToString());

    public static HttpContext WebUser(int userId) =>
        Http(user: new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("uid", userId.ToString()) }, "Bearer")));

    private static HttpContext Http(ClaimsPrincipal? user = null, string? sentKey = null, string? actingUserId = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [PluginServiceAuth.ApiKeyConfigKey] = Key
        }).Build();
        var environment = new Mock<IHostEnvironment>();
        environment.Setup(e => e.EnvironmentName).Returns(Environments.Production);
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IConfiguration))).Returns(configuration);
        services.Setup(s => s.GetService(typeof(IHostEnvironment))).Returns(environment.Object);

        var http = new DefaultHttpContext { RequestServices = services.Object };
        if (user != null) http.User = user;
        if (sentKey != null) http.Request.Headers[PluginServiceAuth.ApiKeyHeader] = sentKey;
        if (actingUserId != null) http.Request.Headers[PluginServiceAuth.ActingUserHeader] = actingUserId;
        return http;
    }

    /// <summary>
    /// Runs every KNG-22 gate on <paramref name="controller"/>.<paramref name="action"/> in order and
    /// returns the HTTP status of the first refusal, or null when all pass. Throws when the action
    /// carries no gate at all, so an unprotected route can't pass by accident.
    /// </summary>
    public static async Task<int?> RunGates(Type controller, string action, HttpContext http,
        IPermissionResolutionService? permissions = null)
    {
        var method = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance).Single(m => m.Name == action);
        var pluginOnly = method.GetCustomAttributes<RequirePluginServiceAttribute>().ToList();
        var serviceOrNode = method.GetCustomAttributes<RequireServiceOrPermissionAttribute>().ToList();
        if (pluginOnly.Count == 0 && serviceOrNode.Count == 0)
        {
            throw new InvalidOperationException($"{controller.Name}.{action} carries no service-auth gate.");
        }

        foreach (var gate in pluginOnly)
        {
            var context = Context(http);
            gate.OnAuthorization(context);
            if (context.Result != null) return Status(context.Result);
        }
        foreach (var gate in serviceOrNode)
        {
            var context = Context(http);
            var filter = new RequireServiceOrPermissionFilter(gate.Node,
                permissions ?? new Mock<IPermissionResolutionService>().Object);
            await filter.OnAuthorizationAsync(context);
            if (context.Result != null) return Status(context.Result);
        }
        return null;
    }

    private static AuthorizationFilterContext Context(HttpContext http) =>
        new(new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>());

    private static int? Status(IActionResult result) => result switch
    {
        UnauthorizedObjectResult => StatusCodes.Status401Unauthorized,
        ObjectResult o => o.StatusCode,
        _ => -1
    };
}
