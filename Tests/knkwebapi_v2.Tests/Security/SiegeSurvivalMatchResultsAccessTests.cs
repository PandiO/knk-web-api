using System.Reflection;
using KnKWebAPI.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using knkwebapi_v2.Attributes;
using Xunit;

namespace knkwebapi_v2.Tests.Security;

/// <summary>
/// The survival result and checkpoint endpoints (PandiO/knk-workspace#41, link S8a): the result write, the
/// checkpoint write and the checkpoint read need the game server's key or the Siege setup node (match-internal, as
/// the Mystery Box endpoints, S7a); the result read and the history list are "logged in or the game server" like
/// the S4 reads (the default-deny filter's rule for GETs without an attribute), so players can read their history.
/// Nothing is anonymous.
/// </summary>
public class SiegeSurvivalMatchResultsAccessTests
{
    private static readonly Type Controller = typeof(SiegeSurvivalMatchResultsController);

    [Fact]
    public void WritesAndTheCheckpointRead_NeedThePluginKeyOrTheSiegeManageNode()
    {
        Assert.Empty(Controller.GetCustomAttributes<RequireServiceOrPermissionAttribute>(true));
        foreach (var name in new[] { "PostResult", "PutCheckpoint", "GetCheckpoint" })
        {
            var rule = Assert.Single(Controller.GetMethod(name)!.GetCustomAttributes<RequireServiceOrPermissionAttribute>(true));
            Assert.Equal("knk.siege.admin.manage", rule.Node);
        }
    }

    [Fact]
    public void ResultReadAndHistoryList_AreLoggedInOrGameServer_NotAnonymous()
    {
        var actions = Controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToList();
        Assert.Equal(new[] { "GetCheckpoint", "GetResult", "PostResult", "PutCheckpoint", "QueryResults" }, actions.Select(a => a.Name).OrderBy(n => n));
        Assert.All(actions, a => Assert.Empty(a.GetCustomAttributes<AllowAnonymousAttribute>(true)));
        foreach (var name in new[] { "GetResult", "QueryResults" })
        {
            var action = Controller.GetMethod(name)!;
            Assert.Empty(action.GetCustomAttributes<RequireServiceOrPermissionAttribute>(true));
            Assert.Single(action.GetCustomAttributes<Microsoft.AspNetCore.Mvc.HttpGetAttribute>());
        }
        // Every write carries an explicit rule (EndpointAccessRulesTests pins this for the whole app).
        foreach (var name in new[] { "PostResult", "PutCheckpoint" })
            Assert.True(DefaultCallerRequiredFilter.HasAccessRule(Controller.GetCustomAttributes(true).Concat(Controller.GetMethod(name)!.GetCustomAttributes(true))));
    }

    [Fact]
    public void Routes_MatchTheContract()
    {
        Assert.Equal("api/siege-survival", Assert.Single(Controller.GetCustomAttributes<Microsoft.AspNetCore.Mvc.RouteAttribute>()).Template);
        (string Method, string Template) Route(string action)
        {
            var attr = Assert.Single(Controller.GetMethod(action)!.GetCustomAttributes<HttpMethodAttribute>());
            return (attr.HttpMethods.Single(), attr.Template!);
        }
        Assert.Equal(("POST", "matches/{siegeMatchId:int}/result"), Route("PostResult"));
        Assert.Equal(("GET", "matches/{siegeMatchId:int}/result"), Route("GetResult"));
        Assert.Equal(("GET", "results"), Route("QueryResults"));
        Assert.Equal(("PUT", "matches/{siegeMatchId:int}/checkpoint"), Route("PutCheckpoint"));
        Assert.Equal(("GET", "matches/{siegeMatchId:int}/checkpoint"), Route("GetCheckpoint"));
    }
}
