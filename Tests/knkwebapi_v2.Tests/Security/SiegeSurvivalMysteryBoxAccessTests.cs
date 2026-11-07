using System.Reflection;
using KnKWebAPI.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using knkwebapi_v2.Attributes;
using Xunit;

namespace knkwebapi_v2.Tests.Security;

/// <summary>
/// The match-scoped Mystery Box endpoints (PandiO/knk-workspace#41, link S7a) follow the Siege match pattern: the
/// game server's key or the Siege setup node on every action, reads included (the roll list is match-internal), and
/// none of them is anonymous. EndpointAccessRulesTests pins the write rule and the allow-list generally; this pins
/// the node.
/// </summary>
public class SiegeSurvivalMysteryBoxAccessTests
{
    [Fact]
    public void EveryAction_NeedsThePluginKeyOrTheSiegeManageNode()
    {
        var controller = typeof(SiegeSurvivalMysteryBoxController);
        var rule = Assert.Single(controller.GetCustomAttributes<RequireServiceOrPermissionAttribute>(true));
        Assert.Equal(StaffPermissions.SiegeManage, rule.Node);
        Assert.Equal("knk.siege.admin.manage", rule.Node);

        var actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToList();
        Assert.Equal(new[] { "Claim", "Forfeit", "List", "Roll" }, actions.Select(a => a.Name).OrderBy(n => n));
        Assert.All(actions, a => Assert.Empty(a.GetCustomAttributes<AllowAnonymousAttribute>(true)));
        Assert.All(actions, a => Assert.True(DefaultCallerRequiredFilter.HasAccessRule(controller.GetCustomAttributes(true).Concat(a.GetCustomAttributes(true)))));
    }

    [Fact]
    public void Routes_MatchTheContract()
    {
        var controller = typeof(SiegeSurvivalMysteryBoxController);
        Assert.Equal("api/siege-survival/matches/{siegeMatchId:int}/mystery-box", Assert.Single(controller.GetCustomAttributes<Microsoft.AspNetCore.Mvc.RouteAttribute>()).Template);
        string Template(string action) => Assert.Single(controller.GetMethod(action)!.GetCustomAttributes<HttpMethodAttribute>()).Template!;
        Assert.Equal("{pointId:int}/roll", Template("Roll"));
        Assert.Equal("rolls/{rollId}/claim", Template("Claim"));
        Assert.Equal("rolls/{rollId}/forfeit", Template("Forfeit"));
        Assert.Equal("rolls", Template("List"));
    }
}
