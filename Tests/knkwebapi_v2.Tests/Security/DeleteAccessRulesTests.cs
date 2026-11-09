using System.Reflection;
using knkwebapi_v2.Attributes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace knkwebapi_v2.Tests.Security;

/// <summary>
/// Dashboard access model (PandiO/knk-workspace#280, DESIGN.md § 4.7): the primary <c>DELETE {id}</c> of every deletable
/// dashboard type carries <c>knk.admin.delete</c> on top of its area rule; every other DELETE is on the pinned exemption
/// list below, so a new delete endpoint has to decide.
/// </summary>
public class DeleteAccessRulesTests
{
    /// <summary>Dashboard type → its primary delete (Controller.Action).</summary>
    public static readonly IReadOnlyDictionary<string, string> PrimaryDeletes = new Dictionary<string, string>
    {
        ["Category"] = "CategoriesController.Delete",
        ["Tag"] = "TagsController.Delete",
        ["Grade"] = "GradesController.Delete",
        ["ItemBlueprint"] = "ItemBlueprintsController.Delete",
        ["EnchantmentDefinition"] = "EnchantmentDefinitionsController.Delete",
        ["BannerDesign"] = "BannerDesignsController.Delete",
        ["BannerLayer"] = "BannerLayersController.Delete",
        ["Clan"] = "ClansController.Delete",
        ["MinecraftBlockRef"] = "MinecraftBlockRefsController.Delete",
        ["MinecraftMaterialRef"] = "MinecraftMaterialRefsController.Delete",
        ["MinecraftEnchantmentRef"] = "MinecraftEnchantmentRefsController.Delete",
        ["Domain"] = "DomainsController.Delete",
        ["Town"] = "TownsController.Delete",
        ["District"] = "DistrictsController.Delete",
        ["Structure"] = "StructuresController.Delete",
        ["Street"] = "StreetsController.Delete",
        ["Location"] = "LocationsController.Delete",
        ["GateStructure"] = "GateStructuresController.Delete",
        ["GateDoor"] = "GateDoorsController.Delete",
        ["DomainSubRegion"] = "DomainSubRegionsController.Delete",
        ["Kit"] = "KitsController.Delete",
        ["LootboxType"] = "LootboxTypesController.Delete",
        ["LootboxSpecialEntry"] = "LootboxSpecialEntriesController.Delete",
        ["LootboxSpawnArea"] = "LootboxSpawnAreasController.Delete",
        ["SiegeScenario"] = "SiegeScenariosController.Delete",
        ["SiegeTeam"] = "SiegeTeamsController.Delete",
        ["SiegeLobby"] = "SiegeLobbiesController.Delete",
        ["SiegeObjective"] = "SiegeObjectivesController.Delete",
        ["SiegeSpawnpoint"] = "SiegeSpawnpointsController.Delete",
        ["RoadProfile"] = "RoadProfilesController.Delete",
        ["User"] = "UsersController.Delete",
        ["PermissionGroup"] = "PermissionGroupsController.Delete",
    };

    /// <summary>
    /// DELETEs that keep their own rules (DESIGN.md § 4.7 "not covered"): builder parts and whole form/display
    /// configurations, menus, your own drafts, world-task and workflow lifecycle, plugin-only and player-own deletes, and
    /// link deletes (memberships, grants, snapshots, overrides).
    /// </summary>
    public static readonly IReadOnlySet<string> Exempt = new HashSet<string>
    {
        "EntityTypeConfigurationController.Delete",
        "DisplayFieldsController.DeleteAsync",
        "DisplaySectionsController.DeleteAsync",
        "DisplayConfigurationsController.DeleteAsync",
        "FormStepsController.Delete",
        "FormFieldsController.Delete",
        "FormConfigurationsController.Delete",
        "FieldValidationRulesController.Delete",
        "MenuTemplatesController.Delete",
        "FormSubmissionProgressController.Delete",
        "WorldTasksController.Delete",
        "WorkflowsController.Delete",
        "GateDoorsController.ClearSnapshots",
        "GateDoorsController.ClearOpenedSnapshots",
        "UsersController.RemoveGroup",
        "UsersController.RevokeNode",
        "UserPermissionGroupsController.Delete",
        "PermissionGrantsController.Delete",
        "PermissionGrantsController.RevokeByNode",
        "LootboxTokenGrantsController.Delete",
        "DiscoveriesController.Reset",
        "DiscoveryRewardsController.DeleteOverride",
        "LeaderboardsController.DeleteExclusion",
        "CurrencyAdminController.UnlockTransfers",
        "UserIgnoresController.Remove",
        "TelemetryController.RemoveEnhancedTarget",
        "RoadEdgesController.Delete",
        "RoadNodesController.Unprune",
        "RoadSeedsController.Delete",
        "RoadTilesController.DeleteProposal",
    };

    private static IEnumerable<MethodInfo> DeleteActions() =>
        typeof(DashboardAccess).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes<HttpDeleteAttribute>().Any());

    private static string Name(MethodInfo m) => $"{m.DeclaringType!.Name}.{m.Name}";

    private static IEnumerable<string> Nodes(MemberInfo m) =>
        m.GetCustomAttributes<RequireServiceOrPermissionAttribute>().Select(a => a.Node)
            .Concat(m.GetCustomAttributes<RequirePermissionAttribute>().Select(a => (string)a.Arguments![0]));

    [Fact]
    public void EveryDeletableRegistryType_HasAPinnedPrimaryDelete()
    {
        var deletable = DashboardAccess.All.Where(e => e.Deletable).Select(e => e.TypeName).OrderBy(n => n);
        Assert.Equal(deletable, PrimaryDeletes.Keys.OrderBy(n => n));
    }

    [Fact]
    public void EveryPrimaryDelete_NeedsTheDeleteNode_AndItsAreaOrWriteNode()
    {
        var actions = DeleteActions().ToDictionary(Name);
        foreach (var (typeName, actionName) in PrimaryDeletes)
        {
            Assert.True(actions.TryGetValue(actionName, out var action), actionName);
            var nodes = Nodes(action!).Concat(Nodes(action!.DeclaringType!)).ToList();
            var classRule = action.DeclaringType!.GetCustomAttribute<RequireServiceOrPermissionForWritesAttribute>()?.Node;
            if (classRule != null) nodes.Add(classRule);

            Assert.Contains(StaffPermissions.Delete, nodes);
            Assert.Contains(DashboardAccess.Find(typeName)!.WriteNode, nodes);
        }
    }

    [Fact]
    public void EveryOtherDelete_IsPinnedAsExempt_AndDoesntCarryTheDeleteNode()
    {
        var primary = PrimaryDeletes.Values.ToHashSet();
        var others = DeleteActions().Where(m => !primary.Contains(Name(m))).ToList();

        // Joined, so a failure names every offender.
        var unpinned = others.Select(Name).Where(n => !Exempt.Contains(n)).OrderBy(n => n).ToList();
        Assert.True(unpinned.Count == 0, "Not pinned: " + string.Join(", ", unpinned));
        var stale = Exempt.Where(n => others.All(m => Name(m) != n)).OrderBy(n => n).ToList();
        Assert.True(stale.Count == 0, "No such delete: " + string.Join(", ", stale));
        Assert.All(others, m => Assert.DoesNotContain(StaffPermissions.Delete, Nodes(m)));
    }

    [Fact]
    public void NoWriteBesidesTheDeletesAndTheDashboard_UsesTheDeleteNodes()
    {
        var offenders = typeof(DashboardAccess).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract && t.Name != "DashboardController")
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => !m.GetCustomAttributes<HttpDeleteAttribute>().Any())
            .Where(m => Nodes(m).Any(n => n is StaffPermissions.Delete or StaffPermissions.DeleteBulk))
            .Select(Name);
        Assert.Empty(offenders);
    }
}
