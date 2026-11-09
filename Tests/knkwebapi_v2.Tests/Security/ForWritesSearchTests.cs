using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Services;
using Xunit;

namespace knkwebapi_v2.Tests.Security;

/// <summary>
/// KNG-64 smoke test: content staff (knk.admin.content only) saw no world data in the dashboard,
/// because the list views POST to …/search and the writes-only rule treated that as a write.
/// </summary>
public class ForWritesSearchTests
{
    private readonly Mock<IPermissionResolutionService> _permissions = new();

    private static HttpContext LoggedIn(string method)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [PluginServiceAuth.ApiKeyConfigKey] = "configured-key"
        }).Build();
        var environment = new Mock<IHostEnvironment>();
        environment.Setup(e => e.EnvironmentName).Returns(Environments.Production);
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IConfiguration))).Returns(configuration);
        services.Setup(s => s.GetService(typeof(IHostEnvironment))).Returns(environment.Object);
        var http = new DefaultHttpContext { RequestServices = services.Object };
        http.Request.Method = method;
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("uid", "7") }, "Bearer"));
        return http;
    }

    private async Task<bool> Refused(string method, string template)
    {
        var action = new ActionDescriptor { AttributeRouteInfo = new AttributeRouteInfo { Template = template } };
        var context = new AuthorizationFilterContext(new ActionContext(LoggedIn(method), new RouteData(), action), new List<IFilterMetadata>());
        await new RequireServiceOrPermissionForWritesFilter(StaffPermissions.ManageWorld, _permissions.Object).OnAuthorizationAsync(context);
        return context.Result != null;
    }

    [Theory]
    [InlineData("api/Domains/search")]
    [InlineData("api/Domains/search-region-decisions")]
    [InlineData("api/WorldTasks/user/{userId:int}/search")]
    public async Task PostSearch_IsARead(string template) =>
        Assert.False(await Refused("POST", template));

    [Theory]
    [InlineData("POST", "api/Domains")]
    [InlineData("POST", "api/Domains/{id:int}/research")]
    [InlineData("PUT", "api/Domains/search")]
    [InlineData("DELETE", "api/Domains/{id:int}")]
    public async Task OtherWrites_StillNeedTheNode(string method, string template) =>
        Assert.True(await Refused(method, template));

    // ===== #280 AM2: a POST whose action carries a read rule of its own is a read too =====

    private async Task<bool> RefusedAction(Type controller, string action)
    {
        var method = controller.GetMethods().Single(m => m.Name == action);
        var descriptor = new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor
        {
            ControllerTypeInfo = System.Reflection.IntrospectionExtensions.GetTypeInfo(controller),
            MethodInfo = method,
            AttributeRouteInfo = new AttributeRouteInfo { Template = "api/x/" + action },
        };
        var context = new AuthorizationFilterContext(new ActionContext(LoggedIn("POST"), new RouteData(), descriptor), new List<IFilterMetadata>());
        await new RequireServiceOrPermissionForWritesFilter(StaffPermissions.ManageContent, _permissions.Object).OnAuthorizationAsync(context);
        return context.Result != null;
    }

    [Theory]
    [InlineData(nameof(KnKWebAPI.Controllers.FieldValidationRulesController.ValidateField))]
    [InlineData(nameof(KnKWebAPI.Controllers.FieldValidationRulesController.ValidateFields))]
    [InlineData(nameof(KnKWebAPI.Controllers.FieldValidationRulesController.ResolvePlaceholders))]
    [InlineData(nameof(KnKWebAPI.Controllers.FieldValidationRulesController.ResolveDependencies))]
    [InlineData(nameof(KnKWebAPI.Controllers.FieldValidationRulesController.ValidatePath))]
    [InlineData(nameof(KnKWebAPI.Controllers.FieldValidationRulesController.ValidateDraftConfiguration))]
    [InlineData(nameof(KnKWebAPI.Controllers.FieldValidationRulesController.ValidateFieldRule))]
    public async Task FormRuntimePosts_AreLeftToTheirAnyAreaNodeRule(string action) =>
        Assert.False(await RefusedAction(typeof(KnKWebAPI.Controllers.FieldValidationRulesController), action));

    [Fact]
    public async Task BuilderWrites_StillNeedTheNode() =>
        Assert.True(await RefusedAction(typeof(KnKWebAPI.Controllers.FieldValidationRulesController),
            nameof(KnKWebAPI.Controllers.FieldValidationRulesController.Create)));
}
