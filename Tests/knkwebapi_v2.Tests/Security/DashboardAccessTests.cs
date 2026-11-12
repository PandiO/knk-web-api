using System.Reflection;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using Xunit;

namespace knkwebapi_v2.Tests.Security;

/// <summary>
/// The dashboard access registry (PandiO/knk-workspace#280, DESIGN.md § 4.2): every <c>[FormConfigurableEntity]</c> type
/// has an entry with its area, the deletable types match the web app's delete functions, bulk delete leaves out users
/// and permission groups, and the metadata carries the fields.
/// </summary>
public class DashboardAccessTests
{
    /// <summary>The types knk-web-app's <c>getDeleteFunctionForEntity</c> handles (src/utils/entityApiMapping.ts).</summary>
    private static readonly string[] WebAppDeletes =
    {
        "Category", "Domain", "Grade", "Tag", "Street", "Town", "District", "Structure", "GateStructure", "GateDoor",
        "DomainSubRegion", "Location", "ItemBlueprint", "Kit", "EnchantmentDefinition", "MinecraftBlockRef",
        "MinecraftMaterialRef", "User", "PermissionGroup", "LootboxType", "LootboxSpecialEntry", "LootboxSpawnArea",
        "BannerDesign", "BannerLayer", "Clan", "SiegeScenario", "SiegeTeam", "SiegeSpawnpoint", "SiegeObjective", "SiegeLobby",
    };

    /// <summary>Registry entries without <c>[FormConfigurableEntity]</c>: the roads page's profiles.</summary>
    private static readonly string[] OutsideTheForms = { "RoadProfile" };

    private static IEnumerable<string> FormConfigurableTypeNames() =>
        typeof(DashboardAccess).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<FormConfigurableEntityAttribute>() != null) // as MetadataService scans them
            .Select(t => t.Name);

    [Fact]
    public void EveryFormConfigurableType_IsInTheRegistry_AndNothingElseIs()
    {
        var formTypes = FormConfigurableTypeNames().ToHashSet();
        var missing = formTypes.Where(n => DashboardAccess.Find(n) == null).OrderBy(n => n).ToList();
        Assert.True(missing.Count == 0, "Not in DashboardAccess: " + string.Join(", ", missing));
        var extra = DashboardAccess.All.Select(e => e.TypeName).Where(n => !formTypes.Contains(n) && !OutsideTheForms.Contains(n)).ToList();
        Assert.True(extra.Count == 0, "Not a form type: " + string.Join(", ", extra));
        Assert.Equal(DashboardAccess.All.Count, DashboardAccess.All.Select(e => e.TypeName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EveryTypeTheWebAppDeletes_IsDeletable()
    {
        Assert.All(WebAppDeletes, n => Assert.True(DashboardAccess.Find(n)?.Deletable, n));
    }

    [Fact]
    public void BulkDelete_IsEveryDeletableTypeButUsersAndPermissionGroups()
    {
        var bulk = DashboardAccess.All.Where(e => e.BulkDeletable).Select(e => e.TypeName).ToHashSet();
        var expected = DashboardAccess.All.Where(e => e.Deletable).Select(e => e.TypeName).Except(new[] { "User", "PermissionGroup" });
        Assert.Equal(expected.OrderBy(n => n), bulk.OrderBy(n => n));
    }

    [Theory]
    [InlineData("ItemBlueprint", DashboardArea.Content, "knk.admin.content")]
    [InlineData("MinecraftEnchantmentRef", DashboardArea.Content, "knk.admin.content")]
    [InlineData("Town", DashboardArea.World, "knk.admin.world")]
    [InlineData("GateDoor", DashboardArea.World, "knk.admin.world")]
    [InlineData("Kit", DashboardArea.Kits, "knk.kit.manage")]
    [InlineData("LootboxSpawnArea", DashboardArea.Lootboxes, "knk.admin.lootbox.manage")]
    [InlineData("SiegeTeam", DashboardArea.Siege, "knk.siege.admin.manage")]
    [InlineData("RoadProfile", DashboardArea.Roads, "knk.admin.road")]
    [InlineData("User", DashboardArea.Players, "knk.admin.user.manage")]
    [InlineData("PermissionGroup", DashboardArea.Players, "knk.admin.user.manage")]
    public void TypesSitInTheirArea(string typeName, DashboardArea area, string node)
    {
        var entry = DashboardAccess.Find(typeName.ToLowerInvariant())!; // case-insensitive, like the metadata lookup
        Assert.Equal(area, entry.Area);
        Assert.Equal(node, entry.AreaNode);
        Assert.Equal(node, entry.ReadNodes[0]);
    }

    [Fact]
    public void PermissionGroupWrites_NeedTheGroupNode()
    {
        Assert.Equal(StaffPermissions.UserPermissions, DashboardAccess.Find("PermissionGroup")!.WriteNode);
        Assert.Equal(StaffPermissions.ManageUsers, DashboardAccess.Find("User")!.WriteNode);
    }

    [Theory]
    [InlineData("Location", "knk.admin.config")]
    [InlineData("Domain", "knk.admin.discovery")]
    [InlineData("Street", "knk.admin.road")]
    [InlineData("ItemBlueprint", "knk.kit.manage")]
    [InlineData("Kit", "knk.admin.user.manage")]
    public void CrossAreaPages_AreReadNodes(string typeName, string node)
    {
        Assert.Contains(node, DashboardAccess.Find(typeName)!.ReadNodes);
    }

    [Fact]
    public void AreaNodes_AreTheSevenAreas()
    {
        Assert.Equal(new[]
        {
            "knk.admin.content", "knk.admin.world", "knk.kit.manage", "knk.admin.lootbox.manage", "knk.siege.admin.manage",
            "knk.admin.road", "knk.admin.user.manage",
        }, DashboardAccess.AreaNodes);
    }

    [Fact]
    public void UnknownTypes_AreNotFound()
    {
        Assert.Null(DashboardAccess.Find("FormConfiguration"));
        Assert.Null(DashboardAccess.Find(null));
        Assert.Null(DashboardAccess.Find("  "));
    }

    [Fact]
    public void Metadata_CarriesTheAccessFields()
    {
        var meta = new EntityMetadataDto { EntityName = "Town", DisplayName = "Town" };
        MetadataService.ApplyDashboardAccess(meta);

        Assert.Equal("World", meta.Area);
        Assert.Equal("knk.admin.world", meta.AccessNode);
        Assert.Equal("knk.admin.world", meta.WriteNode);
        Assert.Equal(new[] { "knk.admin.world", "knk.admin.config", "knk.admin.road" }, meta.ReadNodes);
        Assert.True(meta.Deletable);
        Assert.True(meta.BulkDeletable);
        Assert.True(meta.Exportable);
        Assert.Equal(new[] { "knk.admin.world", "knk.admin.delete" }, meta.ActionNodes["delete"]);
        Assert.Equal(new[] { "knk.admin.world", "knk.admin.delete", "knk.admin.delete.bulk" }, meta.ActionNodes["bulkDelete"]);
        Assert.Equal(new[] { "knk.admin.world", "knk.admin.export" }, meta.ActionNodes["export"]);
        // AM2: the dashboard lists the type for its area node; lookups for pickers and labels.
        Assert.Equal(new[] { "knk.admin.world" }, meta.ActionNodes["view"]);
        Assert.True(meta.LookupSearchable);
        Assert.True(meta.LookupGettable);
    }

    [Fact]
    public void Metadata_ForUsersAndGroups_HasNoBulkDelete()
    {
        var group = new EntityMetadataDto { EntityName = "PermissionGroup", DisplayName = "PermissionGroup" };
        MetadataService.ApplyDashboardAccess(group);
        Assert.False(group.BulkDeletable);
        Assert.False(group.ActionNodes.ContainsKey("bulkDelete"));
        Assert.Equal(new[] { "knk.admin.user.perm", "knk.admin.delete" }, group.ActionNodes["delete"]);
        Assert.Equal(new[] { "knk.admin.user.perm" }, group.ActionNodes["create"]);
        Assert.Equal(new[] { "knk.admin.user.manage" }, group.ActionNodes["view"]);

        var link = new EntityMetadataDto { EntityName = "CategoryTag", DisplayName = "CategoryTag" };
        MetadataService.ApplyDashboardAccess(link);
        Assert.Equal("Content", link.Area);
        Assert.False(link.Deletable);
        Assert.False(link.ActionNodes.ContainsKey("delete"));
        Assert.False(link.Exportable);
        Assert.False(link.LookupSearchable);
        Assert.False(link.LookupGettable);
    }
}
