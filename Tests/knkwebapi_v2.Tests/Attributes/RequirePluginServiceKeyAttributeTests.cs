using knkwebapi_v2.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace knkwebapi_v2.Tests.Attributes;

/// <summary>
/// Siege Phase 6: the opt-in plugin service key on the match write endpoints
/// (docs/specs/siege-minigame/DESIGN.md §11.2) - open while Security:PluginServiceKey is empty,
/// enforced once it is set.
/// </summary>
public class RequirePluginServiceKeyAttributeTests
{
    private static AuthorizationFilterContext Context(Dictionary<string, string?> settings, params (string Name, string Value)[] headers)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        foreach (var (name, value) in headers) http.Request.Headers[name] = value;
        return new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>());
    }

    [Fact]
    public void NoKeyConfigured_AllowsEveryone()
    {
        var context = Context(new Dictionary<string, string?> { [RequirePluginServiceKeyAttribute.KeySetting] = "" });

        new RequirePluginServiceKeyAttribute().OnAuthorization(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public void KeyConfigured_MissingOrWrongHeader_Is401()
    {
        var settings = new Dictionary<string, string?> { [RequirePluginServiceKeyAttribute.KeySetting] = "s3cret" };

        var missing = Context(settings);
        new RequirePluginServiceKeyAttribute().OnAuthorization(missing);
        Assert.IsType<UnauthorizedObjectResult>(missing.Result);

        var wrong = Context(settings, ("X-API-Key", "nope"));
        new RequirePluginServiceKeyAttribute().OnAuthorization(wrong);
        Assert.IsType<UnauthorizedObjectResult>(wrong.Result);
    }

    [Fact]
    public void KeyConfigured_MatchingHeader_IsAllowed_IncludingACustomHeaderName()
    {
        var ok = Context(new Dictionary<string, string?> { [RequirePluginServiceKeyAttribute.KeySetting] = "s3cret" },
            ("X-API-Key", "s3cret"));
        new RequirePluginServiceKeyAttribute().OnAuthorization(ok);
        Assert.Null(ok.Result);

        var custom = Context(new Dictionary<string, string?>
        {
            [RequirePluginServiceKeyAttribute.KeySetting] = "s3cret",
            [RequirePluginServiceKeyAttribute.HeaderSetting] = "X-Knk-Plugin-Key"
        }, ("X-Knk-Plugin-Key", "s3cret"));
        new RequirePluginServiceKeyAttribute().OnAuthorization(custom);
        Assert.Null(custom.Result);
    }
}
