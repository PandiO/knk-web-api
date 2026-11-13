using System.Reflection;
using System.Security.Claims;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Security;

/// <summary>
/// PandiO/knk-workspace#374 (developer 2026-11-13: "Area staff should not be able to change forms at this point. They
/// only work with the content available."): the primary create and update action of every dashboard type carries
/// <see cref="RequireDashboardChangeAttribute"/>, which asks for the type's <see cref="DashboardTypeAccess.ChangeNodes"/>
/// (its write node AND knk.admin.content), and the filter refuses a web user without all of them. The Players types
/// and RoadProfile keep their own rules; delete, bulk delete, export and reads are untouched; the plugin key passes.
/// </summary>
public class DashboardChangeAccessRulesTests
{
    /// <summary>Controller, action, type: the create/update endpoints the web app's create and update clients call.</summary>
    private static readonly (string Controller, string Action, string Type)[] ChangeActions =
    {
        ("CategoriesController", "Create", "Category"), ("CategoriesController", "Update", "Category"),
        ("TagsController", "Create", "Tag"), ("TagsController", "Update", "Tag"),
        ("GradesController", "Create", "Grade"), ("GradesController", "Update", "Grade"),
        ("ItemBlueprintsController", "Create", "ItemBlueprint"), ("ItemBlueprintsController", "Update", "ItemBlueprint"),
        ("EnchantmentDefinitionsController", "Create", "EnchantmentDefinition"), ("EnchantmentDefinitionsController", "Update", "EnchantmentDefinition"),
        ("BannerDesignsController", "Create", "BannerDesign"), ("BannerDesignsController", "Update", "BannerDesign"),
        ("BannerDesignsController", "CreateLayer", "BannerLayer"), ("BannerLayersController", "Update", "BannerLayer"),
        ("ClansController", "Create", "Clan"), ("ClansController", "Update", "Clan"),
        ("MinecraftBlockRefsController", "Create", "MinecraftBlockRef"), ("MinecraftBlockRefsController", "Update", "MinecraftBlockRef"),
        ("MinecraftMaterialRefsController", "Create", "MinecraftMaterialRef"), ("MinecraftMaterialRefsController", "Update", "MinecraftMaterialRef"),
        ("MinecraftEnchantmentRefsController", "Create", "MinecraftEnchantmentRef"), ("MinecraftEnchantmentRefsController", "Update", "MinecraftEnchantmentRef"),
        ("DomainsController", "Create", "Domain"), ("DomainsController", "Update", "Domain"),
        ("TownsController", "Create", "Town"), ("TownsController", "Update", "Town"),
        ("DistrictsController", "Create", "District"), ("DistrictsController", "Update", "District"),
        ("StructuresController", "Create", "Structure"), ("StructuresController", "Update", "Structure"),
        ("StreetsController", "Create", "Street"), ("StreetsController", "Update", "Street"),
        ("LocationsController", "Create", "Location"), ("LocationsController", "Update", "Location"),
        ("GateStructuresController", "Create", "GateStructure"), ("GateStructuresController", "Update", "GateStructure"),
        ("GateDoorsController", "Create", "GateDoor"), ("GateDoorsController", "Update", "GateDoor"),
        ("DomainSubRegionsController", "Create", "DomainSubRegion"), ("DomainSubRegionsController", "Update", "DomainSubRegion"),
        ("KitsController", "Create", "Kit"), ("KitsController", "Update", "Kit"),
        ("LootboxTypesController", "Create", "LootboxType"), ("LootboxTypesController", "Update", "LootboxType"),
        ("LootboxSpecialEntriesController", "Create", "LootboxSpecialEntry"), ("LootboxSpecialEntriesController", "Update", "LootboxSpecialEntry"),
        ("LootboxSpawnAreasController", "Create", "LootboxSpawnArea"), ("LootboxSpawnAreasController", "Update", "LootboxSpawnArea"),
        ("SiegeScenariosController", "Create", "SiegeScenario"), ("SiegeScenariosController", "Update", "SiegeScenario"),
        ("SiegeScenariosController", "CreateTeam", "SiegeTeam"), ("SiegeTeamsController", "Update", "SiegeTeam"),
        ("SiegeScenariosController", "CreateObjective", "SiegeObjective"), ("SiegeObjectivesController", "Update", "SiegeObjective"),
        ("SiegeTeamsController", "CreateSpawnpoint", "SiegeSpawnpoint"), ("SiegeSpawnpointsController", "Update", "SiegeSpawnpoint"),
        ("SiegeLobbiesController", "Create", "SiegeLobby"), ("SiegeLobbiesController", "Update", "SiegeLobby"),
    };

    private static IEnumerable<(Type Controller, MethodInfo Method, RequireDashboardChangeAttribute Rule)> Carriers() =>
        typeof(DashboardAccess).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(m => (t, m, m.GetCustomAttribute<RequireDashboardChangeAttribute>()))
                .Where(x => x.Item3 != null)
                .Select(x => (x.t, x.m, x.Item3!)));

    [Fact]
    public void TheChangeRule_SitsOnExactlyThePinnedCreateAndUpdateActions()
    {
        var found = Carriers().Select(c => (c.Controller.Name, c.Method.Name, c.Rule.TypeName)).OrderBy(x => x.ToString()).ToList();
        var expected = ChangeActions.Select(a => (a.Controller, a.Action, a.Type)).OrderBy(x => x.ToString()).ToList();
        Assert.Equal(expected, found);
    }

    [Fact]
    public void EveryDeletableTypeWhoseChangesNeedContent_HasItsCreateAndUpdatePinned()
    {
        var pinned = ChangeActions.Select(a => a.Type).ToHashSet();
        var missing = DashboardAccess.All.Where(e => e.Deletable && e.ChangesNeedContent && !pinned.Contains(e.TypeName))
            .Select(e => e.TypeName).ToList();
        Assert.True(missing.Count == 0, "No create/update with RequireDashboardChange: " + string.Join(", ", missing));
        Assert.All(ChangeActions, a => Assert.True(DashboardAccess.Find(a.Type)?.ChangesNeedContent, a.Type));
    }

    [Theory]
    [InlineData("UsersController", "Create")]
    [InlineData("UsersController", "Update")]
    [InlineData("UsersController", "Freeze")]
    [InlineData("UsersController", "AdjustBalances")]
    [InlineData("PermissionGroupsController", "Create")]
    [InlineData("PermissionGroupsController", "Update")]
    [InlineData("RoadProfilesController", "Create")]
    [InlineData("TownsController", "Delete")]
    [InlineData("TownsController", "SearchTowns")]
    [InlineData("DashboardController", "BulkDelete")]
    [InlineData("DashboardController", "Export")]
    [InlineData("GateDoorsController", "UpdateState")]
    [InlineData("LootboxSpawnAreasController", "CreateInGame")]
    public void NotAffected_HaveNoChangeRule(string controller, string action)
    {
        var type = typeof(DashboardAccess).Assembly.GetTypes().Single(t => t.Name == controller && typeof(ControllerBase).IsAssignableFrom(t));
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => m.Name == action).ToList();
        Assert.NotEmpty(methods);
        Assert.All(methods, m => Assert.Null(m.GetCustomAttribute<RequireDashboardChangeAttribute>()));
    }

    // ===== The filter =====

    private readonly Mock<IPermissionResolutionService> _permissions = new();

    private static HttpContext Http(int? userId = null, string? sentKey = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [PluginServiceAuth.ApiKeyConfigKey] = "secret",
        }).Build();
        var environment = new Mock<IHostEnvironment>();
        environment.Setup(e => e.EnvironmentName).Returns(Environments.Production);
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IConfiguration))).Returns(configuration);
        services.Setup(s => s.GetService(typeof(IHostEnvironment))).Returns(environment.Object);

        var http = new DefaultHttpContext { RequestServices = services.Object };
        if (userId != null) http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("uid", userId.Value.ToString()) }, "Bearer"));
        if (sentKey != null) http.Request.Headers[PluginServiceAuth.ApiKeyHeader] = sentKey;
        return http;
    }

    private void Holds(int userId, params string[] nodes)
    {
        _permissions.Setup(p => p.CheckAsync(userId, It.IsAny<string>()))
            .ReturnsAsync((int id, string node) => new PermissionCheckResponseDto
            {
                UserId = id,
                Node = node,
                Result = nodes.Contains(node) ? PermissionResolutionResult.Granted : PermissionResolutionResult.Undeclared,
            });
    }

    private async Task<IActionResult?> Run(string typeName, HttpContext http)
    {
        var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>());
        await new DashboardChangeFilter(typeName, _permissions.Object).OnAuthorizationAsync(context);
        return context.Result;
    }

    [Fact]
    public async Task AreaNodeWithoutContent_Is403_WithTheNodes()
    {
        Holds(7, "knk.admin.world");
        var result = Assert.IsType<ObjectResult>(await Run("Town", Http(userId: 7)));
        Assert.Equal(403, result.StatusCode);
        var body = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.Contains("\"code\":\"NoWriteAccess\"", body);
        Assert.Contains("\"nodes\":[\"knk.admin.world\",\"knk.admin.content\"]", body);
        Assert.Contains("requires knk.admin.world and knk.admin.content", body);
    }

    [Fact]
    public async Task ContentWithoutTheAreaNode_Is403()
    {
        Holds(7, "knk.admin.content");
        Assert.Equal(403, Assert.IsType<ObjectResult>(await Run("Kit", Http(userId: 7))).StatusCode);
    }

    [Fact]
    public async Task AreaNodeAndContent_Passes()
    {
        Holds(7, "knk.admin.lootbox.manage", "knk.admin.content");
        Assert.Null(await Run("LootboxType", Http(userId: 7)));
    }

    [Fact]
    public async Task ContentTypes_NeedOnlyContent()
    {
        Holds(7, "knk.admin.content");
        Assert.Null(await Run("ItemBlueprint", Http(userId: 7)));
    }

    [Fact]
    public async Task ThePluginKey_Passes_WithoutAnyCheck()
    {
        Assert.Null(await Run("Town", Http(sentKey: "secret")));
        _permissions.Verify(p => p.CheckAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Anonymous_Is401()
    {
        Assert.IsType<UnauthorizedObjectResult>(await Run("Town", Http()));
    }

    [Fact]
    public async Task AnUnknownType_IsRefused_NotOpen()
    {
        Holds(7, "knk.admin.world", "knk.admin.content");
        Assert.Equal(403, Assert.IsType<ObjectResult>(await Run("NoSuchType", Http(userId: 7))).StatusCode);
    }
}
