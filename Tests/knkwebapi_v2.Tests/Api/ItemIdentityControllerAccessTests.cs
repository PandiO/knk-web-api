using System.Reflection;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// Item identity (#141): who may call what. The pinned suites (EndpointAccessRulesTests, ReadAccessRulesTests) check
/// that every action has a rule; this pins which rule, so the counts endpoint stays game-server only and the
/// staff actions stay behind knk.admin.content.
/// </summary>
public class ItemIdentityControllerAccessTests
{
    private static MethodInfo Action(string name) => typeof(ItemIdentityController).GetMethod(name)!;

    [Fact]
    public void ConversionCounts_AreGameServerOnly()
    {
        Assert.NotNull(Action(nameof(ItemIdentityController.RecordConversionCounts)).GetCustomAttribute<RequirePluginServiceAttribute>());
    }

    [Theory]
    [InlineData(nameof(ItemIdentityController.GenerateShadows))]
    [InlineData(nameof(ItemIdentityController.SetMatch))]
    public void StaffWrites_NeedTheContentNode_OrThePluginKey(string action)
    {
        Assert.NotNull(Action(action).GetCustomAttribute<RequireServiceOrPermissionAttribute>());
        Assert.Contains(StaffPermissions.ManageContent, ContentNodeOf(Action(action)));
    }

    [Fact]
    public void Report_NeedsTheContentNode()
    {
        Assert.NotNull(Action(nameof(ItemIdentityController.GetReport)).GetCustomAttribute<RequirePermissionAttribute>());
    }

    [Fact]
    public void NothingIsAnonymous()
    {
        Assert.Null(typeof(ItemIdentityController).GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.All(typeof(ItemIdentityController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            m => Assert.Null(m.GetCustomAttribute<AllowAnonymousAttribute>()));
    }

    private static string ContentNodeOf(MethodInfo method) =>
        string.Join(",", method.GetCustomAttributesData()
            .Where(a => a.AttributeType == typeof(RequireServiceOrPermissionAttribute))
            .SelectMany(a => a.ConstructorArguments)
            .Select(a => a.Value?.ToString()));
}
