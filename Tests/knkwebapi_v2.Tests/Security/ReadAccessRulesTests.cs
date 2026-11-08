using System.Reflection;
using System.Security.Claims;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using KnKWebAPI.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Security;

/// <summary>
/// Dashboard access model AM2 (PandiO/knk-workspace#280, DESIGN.md § 4.6, Appendix A): every read endpoint (GET/HEAD,
/// a POST search, or a POST with a read rule of its own) sits in exactly one category, pinned below like the anonymous
/// allow-list, so a new read has to pick one. Then the behaviour per role, through the real filters the action carries
/// (class first, then action), after the global <see cref="DefaultCallerRequiredFilter"/>.
/// </summary>
public class ReadAccessRulesTests
{
    /// <summary>
    /// Controller.Action → category: F (with the type, or the area for records outside the registry), L, R, O, P, G, U,
    /// D (ReadCategory). Generated from Appendix A and checked against it; a change here is a change to who reads what.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Pinned = new Dictionary<string, string>
    {
        // F: full read, the type's read nodes (or an area node) or the plugin key (101)
        ["BannerDesignsController.GetAll"] = "F BannerDesign",
        ["BannerDesignsController.GetById"] = "F BannerDesign",
        ["BannerDesignsController.GetLayers"] = "F BannerDesign",
        ["BannerDesignsController.Search"] = "F BannerDesign",
        ["BannerLayersController.GetById"] = "F BannerLayer",
        ["CategoriesController.GetAll"] = "F Category",
        ["CategoriesController.GetById"] = "F Category",
        ["CategoriesController.GetChildren"] = "F Category",
        ["CategoriesController.SearchCategories"] = "F Category",
        ["ClansController.GetAll"] = "F Clan",
        ["ClansController.GetById"] = "F Clan",
        ["ClansController.Search"] = "F Clan",
        ["DisplayConfigurationsController.Export"] = "F Content",
        ["DisplayConfigurationsController.GetSeeds"] = "F Content",
        ["DistrictsController.GetAll"] = "F District",
        ["DistrictsController.GetDistrictById"] = "F District",
        ["DistrictsController.SearchDistricts"] = "F District",
        ["DomainsController.GetAccessRules"] = "F World",
        ["DomainsController.GetAll"] = "F Domain",
        ["DomainsController.GetByRegionName"] = "F World",
        ["DomainsController.GetDomainById"] = "F Domain",
        ["DomainsController.GetDomainsWithoutWorld"] = "F World",
        ["DomainsController.ResolveWorld"] = "F World",
        ["DomainsController.Search"] = "F Domain",
        ["DomainsController.SearchDomainRegionDecision"] = "F World",
        ["DomainSubRegionsController.GetByDomain"] = "F DomainSubRegion",
        ["DomainSubRegionsController.GetDomainSubRegionById"] = "F DomainSubRegion",
        ["EnchantmentDefinitionsController.GetAll"] = "F EnchantmentDefinition",
        ["EnchantmentDefinitionsController.GetById"] = "F EnchantmentDefinition",
        ["EnchantmentDefinitionsController.Search"] = "F EnchantmentDefinition",
        ["FormConfigurationsController.Export"] = "F Content",
        ["FormConfigurationsController.GetSeeds"] = "F Content",
        ["GateDoorsController.GetById"] = "F GateDoor",
        ["GateDoorsController.GetByStructure"] = "F GateDoor",
        ["GateDoorsController.GetOpenedSnapshots"] = "F GateDoor",
        ["GateDoorsController.GetSnapshots"] = "F GateDoor",
        ["GateDoorsController.Search"] = "F GateDoor",
        ["GateStructuresController.GetAll"] = "F GateStructure",
        ["GateStructuresController.GetByDomain"] = "F GateStructure",
        ["GateStructuresController.GetById"] = "F GateStructure",
        ["GateStructuresController.SearchGateStructures"] = "F GateStructure",
        ["GradesController.GetAll"] = "F Grade",
        ["GradesController.GetById"] = "F Grade",
        ["GradesController.Search"] = "F Grade",
        ["ItemBlueprintsController.GetAll"] = "F ItemBlueprint",
        ["ItemBlueprintsController.GetById"] = "F ItemBlueprint",
        ["ItemBlueprintsController.Search"] = "F ItemBlueprint",
        ["KitsController.GetAll"] = "F Kit",
        ["KitsController.GetAvailable"] = "F Kit",
        ["KitsController.GetById"] = "F Kit",
        ["KitsController.Search"] = "F Kit",
        ["LocationsController.GetAll"] = "F Location",
        ["LocationsController.GetLocationById"] = "F Location",
        ["LocationsController.SearchLocations"] = "F Location",
        ["MenuTemplatesController.GetAll"] = "F Content",
        ["MenuTemplatesController.GetById"] = "F Content",
        ["MenuTemplatesController.GetByKey"] = "F Content",
        ["MinecraftBlockRefsController.GetAll"] = "F MinecraftBlockRef",
        ["MinecraftBlockRefsController.GetById"] = "F MinecraftBlockRef",
        ["MinecraftBlockRefsController.Search"] = "F MinecraftBlockRef",
        ["MinecraftEnchantmentRefsController.GetAll"] = "F MinecraftEnchantmentRef",
        ["MinecraftEnchantmentRefsController.GetById"] = "F MinecraftEnchantmentRef",
        ["MinecraftEnchantmentRefsController.Search"] = "F MinecraftEnchantmentRef",
        ["MinecraftMaterialRefsController.GetAll"] = "F MinecraftMaterialRef",
        ["MinecraftMaterialRefsController.GetById"] = "F MinecraftMaterialRef",
        ["MinecraftMaterialRefsController.Search"] = "F MinecraftMaterialRef",
        ["RoadProfilesController.Get"] = "F RoadProfile",
        ["RoadProfilesController.List"] = "F RoadProfile",
        ["SiegeLobbiesController.GetAll"] = "F SiegeLobby",
        ["SiegeLobbiesController.GetById"] = "F SiegeLobby",
        ["SiegeLobbiesController.GetRuntimeConfig"] = "F SiegeLobby",
        ["SiegeLobbiesController.Search"] = "F SiegeLobby",
        ["SiegeObjectivesController.GetById"] = "F SiegeObjective",
        ["SiegeScenariosController.GetAll"] = "F SiegeScenario",
        ["SiegeScenariosController.GetById"] = "F SiegeScenario",
        ["SiegeScenariosController.GetObjectives"] = "F SiegeScenario",
        ["SiegeScenariosController.GetReadiness"] = "F SiegeScenario",
        ["SiegeScenariosController.GetTeams"] = "F SiegeScenario",
        ["SiegeScenariosController.Search"] = "F SiegeScenario",
        ["SiegeSpawnpointsController.GetById"] = "F SiegeSpawnpoint",
        ["SiegeTeamsController.GetById"] = "F SiegeTeam",
        ["SiegeTeamsController.GetSpawnpoints"] = "F SiegeTeam",
        ["SiegeTeamsController.Search"] = "F SiegeTeam",
        ["StreetsController.GetAll"] = "F Street",
        ["StreetsController.GetStreetById"] = "F Street",
        ["StreetsController.GetStreetRoad"] = "F Street",
        ["StreetsController.SearchStreets"] = "F Street",
        ["StructuresController.GetAll"] = "F Structure",
        ["StructuresController.GetStructureById"] = "F Structure",
        ["StructuresController.SearchStructures"] = "F Structure",
        ["TagsController.GetAll"] = "F Tag",
        ["TagsController.GetById"] = "F Tag",
        ["TagsController.Search"] = "F Tag",
        ["TownsController.GetAll"] = "F Town",
        ["TownsController.GetTownById"] = "F Town",
        ["TownsController.SearchTowns"] = "F Town",
        ["UsersController.GetUserSummaryByUsername"] = "F User",
        ["UsersController.GetUserSummaryByUuid"] = "F User",
        ["WorldTasksController.GetByUser"] = "F World",
        ["WorldTasksController.ListByStatus"] = "F World",
        ["WorldTasksController.Search"] = "F World",
        // L: lookup, any area node or the plugin key (3)
        ["ClansController.GetDefaultForTown"] = "L",
        ["LookupController.Get"] = "L",
        ["LookupController.Search"] = "L",
        // R: form runtime, any area node or the plugin key (43)
        ["BannerDesignsController.GetPatternKeys"] = "R",
        ["DisplayConfigurationsController.GetAllAsync"] = "R",
        ["DisplayConfigurationsController.GetAllByEntityTypeNameAsync"] = "R",
        ["DisplayConfigurationsController.GetByIdAsync"] = "R",
        ["DisplayConfigurationsController.GetDefaultByEntityTypeNameAsync"] = "R",
        ["DisplayConfigurationsController.GetEntityTypeNamesAsync"] = "R",
        ["DisplayFieldsController.GetAllReusableAsync"] = "R",
        ["DisplayFieldsController.GetByIdAsync"] = "R",
        ["DisplaySectionsController.GetAllReusableAsync"] = "R",
        ["DisplaySectionsController.GetByIdAsync"] = "R",
        ["EntityTypeConfigurationController.GetAll"] = "R",
        ["EntityTypeConfigurationController.GetAllMergedMetadata"] = "R",
        ["EntityTypeConfigurationController.GetByEntityTypeName"] = "R",
        ["EntityTypeConfigurationController.GetById"] = "R",
        ["EntityTypeConfigurationController.GetMergedMetadata"] = "R",
        ["FieldValidationRulesController.GetByConfiguration"] = "R",
        ["FieldValidationRulesController.GetByFormField"] = "R",
        ["FieldValidationRulesController.GetById"] = "R",
        ["FieldValidationRulesController.GetEntityProperties"] = "R",
        ["FieldValidationRulesController.GetPlaceholdersByRule"] = "R",
        ["FieldValidationRulesController.ResolveDependencies"] = "R",
        ["FieldValidationRulesController.ResolvePlaceholders"] = "R",
        ["FieldValidationRulesController.ValidateConfigurationHealth"] = "R",
        ["FieldValidationRulesController.ValidateDraftConfiguration"] = "R",
        ["FieldValidationRulesController.ValidateField"] = "R",
        ["FieldValidationRulesController.ValidateFieldRule"] = "R",
        ["FieldValidationRulesController.ValidateFields"] = "R",
        ["FieldValidationRulesController.ValidatePath"] = "R",
        ["FormConfigurationsController.GetAll"] = "R",
        ["FormConfigurationsController.GetAllByEntityTypeName"] = "R",
        ["FormConfigurationsController.GetAllByEntityTypeNameAll"] = "R",
        ["FormConfigurationsController.GetById"] = "R",
        ["FormConfigurationsController.GetEntityNames"] = "R",
        ["FormConfigurationsController.GetReusableFields"] = "R",
        ["FormConfigurationsController.GetReusableSteps"] = "R",
        ["FormFieldsController.GetAllReusable"] = "R",
        ["FormFieldsController.GetById"] = "R",
        ["FormStepsController.GetAllReusable"] = "R",
        ["FormStepsController.GetById"] = "R",
        ["MetadataController.GetAllEntityMetadata"] = "R",
        ["MetadataController.GetEntityMetadata"] = "R",
        ["MetadataController.GetEntityNames"] = "R",
        ["WorkflowsController.Health"] = "R",
        // O: own records, any area node (+ the action's ownership); drafts web only (13)
        ["FormSubmissionProgressController.GetByEntityTypeName"] = "O",
        ["FormSubmissionProgressController.GetById"] = "O",
        ["FormSubmissionProgressController.GetByUserId"] = "O",
        ["FormSubmissionProgressController.GetEntityReferences"] = "O",
        ["FormSubmissionProgressController.GetWorldTasks"] = "O",
        ["WorkflowsController.GetByGuid"] = "O",
        ["WorkflowsController.GetById"] = "O",
        ["WorkflowsController.GetProgress"] = "O",
        ["WorldTasksController.GetActiveForMe"] = "O",
        ["WorldTasksController.GetById"] = "O",
        ["WorldTasksController.GetByLinkCode"] = "O",
        ["WorldTasksController.GetBySession"] = "O",
        ["WorldTasksController.GetOpenBySession"] = "O",
        // P: [AllowAnonymous] (16)
        ["AuthController.OnboardingInfo"] = "P",
        ["EnchantmentDefinitionsController.PublicInfo"] = "P",
        ["HealthCheckController.GetLiveness"] = "P",
        ["HealthCheckController.GetReadiness"] = "P",
        ["HealthController.GetHealth"] = "P",
        ["LeaderboardsController.GetBoard"] = "P",
        ["LeaderboardsController.GetBoards"] = "P",
        ["PlayersController.GetByName"] = "P",
        ["SiegeMatchesController.PublicMatch"] = "P",
        ["SiegeMatchesController.PublicPlayerMatches"] = "P",
        ["StatisticsController.GetCatalog"] = "P",
        ["StatisticsController.GetDiscoveries"] = "P",
        ["StatisticsController.GetSeries"] = "P",
        ["StatisticsController.GetTitleHistory"] = "P",
        ["StatisticsController.GetUser"] = "P",
        ["UsersController.CheckDuplicateAvailability"] = "P",
        // G: game server only (13)
        ["AdmissionController.Check"] = "G",
        ["DiscoveriesController.GetKnown"] = "G",
        ["KitsController.GetPendingDeliveries"] = "G",
        ["LootboxClaimsController.Pending"] = "G",
        ["LootboxSpawnsController.GetRuntimeConfig"] = "G",
        ["LootboxTokensController.Undelivered"] = "G",
        ["PlayerNotificationsController.GetPending"] = "G",
        ["PrivacyErasuresController.GetSince"] = "G",
        ["TelemetryController.GetConfig"] = "G",
        ["TeleportDestinationsController.List"] = "G",
        ["TeleportDestinationsController.Policy"] = "G",
        ["UserIgnoresController.Get"] = "G",
        ["WorldTasksController.ListOpenForServer"] = "G",
        // U: the action's own stricter rule (node, self-or-node, owner node, [Authorize]) (83)
        ["AdminClientsController.GetActiveClients"] = "U",
        ["AdminClientsController.GetAllClients"] = "U",
        ["AdminClientsController.GetClient"] = "U",
        ["AuditLogController.Get"] = "U",
        ["AuthController.Me"] = "U",
        ["CurrencyAdminController.GetLedger"] = "U",
        ["CurrencyAdminController.GetPolicies"] = "U",
        ["CurrencyAdminController.GetTransaction"] = "U",
        ["CurrencyAdminController.GetTransferLock"] = "U",
        ["CurrencyController.GetLeaderboard"] = "U",
        ["CurrencyMonitorController.GetAlerts"] = "U",
        ["CurrencyMonitorController.GetReconciliation"] = "U",
        ["DataDeletionController.GetForPlayer"] = "U",
        ["DataDeletionController.GetMine"] = "U",
        ["DiscoveriesController.GetStats"] = "U",
        ["DiscoveriesController.GetSummary"] = "U",
        ["DiscoveryRewardsController.GetOverrides"] = "U",
        ["DiscoveryRewardsController.GetRules"] = "U",
        ["DiscoveryRewardsController.Preview"] = "U",
        ["EmailOutboxController.List"] = "U",
        ["GameSettingsController.GetHubDomains"] = "U",
        ["InvitationsController.GetAccessSyncStatus"] = "U",
        ["InvitationsController.GetOptions"] = "U",
        ["InvitationsController.GetRedemptions"] = "U",
        ["InvitationsController.List"] = "U",
        ["ItemInstancesController.GetById"] = "U",
        ["LeaderboardsController.GetExclusions"] = "U",
        ["LocationRetentionController.GetOrphans"] = "U",
        ["LocationRetentionController.GetStatus"] = "U",
        ["LocationRetentionController.GetTeleportTarget"] = "U",
        ["LootboxClaimsController.GetById"] = "U",
        ["LootboxClaimsController.Search"] = "U",
        ["LootboxConfigurationController.Get"] = "U",
        ["LootboxSpawnAreasController.GetAll"] = "U",
        ["LootboxSpawnAreasController.GetById"] = "U",
        ["LootboxSpawnAreasController.Search"] = "U",
        ["LootboxSpawnsController.GetActive"] = "U",
        ["LootboxSpecialEntriesController.GetAll"] = "U",
        ["LootboxSpecialEntriesController.GetById"] = "U",
        ["LootboxSpecialEntriesController.Search"] = "U",
        ["LootboxTokenGrantsController.GetAll"] = "U",
        ["LootboxTokensController.Search"] = "U",
        ["LootboxTypesController.GetAll"] = "U",
        ["LootboxTypesController.GetAllOdds"] = "U",
        ["LootboxTypesController.GetById"] = "U",
        ["LootboxTypesController.GetOdds"] = "U",
        ["LootboxTypesController.Search"] = "U",
        ["MinecraftCatalogController.Audit"] = "U",
        ["MinecraftCatalogController.GetScan"] = "U",
        ["MinecraftCatalogController.ListScans"] = "U",
        ["PermissionGrantsController.GetAll"] = "U",
        ["PermissionGrantsController.GetById"] = "U",
        ["PermissionGrantsController.Search"] = "U",
        ["PermissionGroupsController.CheckPermission"] = "U",
        ["PermissionGroupsController.GetAll"] = "U",
        ["PermissionGroupsController.GetById"] = "U",
        ["PermissionGroupsController.GetExpiringMemberships"] = "U",
        ["PermissionGroupsController.Search"] = "U",
        ["PermissionHoldersController.GetById"] = "U",
        ["PermissionHoldersController.Search"] = "U",
        ["PresenceController.OnlineStaff"] = "U",
        ["PrivacyController.GetRequest"] = "U",
        ["PrivacyController.GetRequests"] = "U",
        ["PrivateMessageLogController.Search"] = "U",
        ["TelemetryController.GetEnhancedTargets"] = "U",
        ["TelemetryController.GetEvent"] = "U",
        ["TelemetryController.GetHealth"] = "U",
        ["TelemetryController.GetTestRuns"] = "U",
        ["TelemetryController.GetTimeline"] = "U",
        ["TelemetryController.Search"] = "U",
        ["UserPermissionGroupsController.Get"] = "U",
        ["UserPermissionGroupsController.GetPremiumTier"] = "U",
        ["UsersController.CheckPermission"] = "U",
        ["UsersController.GetAll"] = "U",
        ["UsersController.GetEffectivePermissions"] = "U",
        ["UsersController.GetProfileSummary"] = "U",
        ["UsersController.GetUserById"] = "U",
        ["UsersController.SearchByGroup"] = "U",
        ["UsersController.SearchUsers"] = "U",
        ["WorldAnalyticsController.GetDomains"] = "U",
        ["WorldAnalyticsController.GetHeatmap"] = "U",
        ["WorldAnalyticsController.GetHeatmapWorlds"] = "U",
        ["WorldAnalyticsController.GetMenuFunnels"] = "U",
        // D: outside the access model, default rule (logged in or the plugin key) (26)
        ["AuditLogRetentionConfigurationController.Get"] = "D",
        ["CurrencyController.GetBalances"] = "D",
        ["CurrencyController.GetLimits"] = "D",
        ["CurrencyController.GetTransactions"] = "D",
        ["GameSettingsController.Get"] = "D",
        ["NavigationSettingsController.GetCompass"] = "D",
        ["NavigationSettingsController.GetDomainDefaults"] = "D",
        ["PresenceController.Me"] = "D",
        ["RoadEdgesController.Search"] = "D",
        ["RoadNetworkController.GetMeta"] = "D",
        ["RoadNetworkController.GetSeedLocations"] = "D",
        ["RoadSeedsController.List"] = "D",
        ["RoadSurveysController.List"] = "D",
        ["RoadTilesController.GetGraph"] = "D",
        ["RoadTilesController.GetProposal"] = "D",
        ["RoadTilesController.List"] = "D",
        ["RoadTilesController.ListProposals"] = "D",
        ["SalaryConfigurationController.Get"] = "D",
        ["SiegeConfigurationController.Get"] = "D",
        ["SiegeMatchesController.GateSnapshots"] = "D",
        ["SiegeMatchesController.GetById"] = "D",
        ["SiegeMatchesController.Query"] = "D",
        ["StatisticsController.GetVisibility"] = "D",
        ["TitleBracketsController.GetAll"] = "D",
        ["TitleBracketsController.GetById"] = "D",
        ["TitleBracketsController.Search"] = "D",
    };

    private static readonly Dictionary<ReadCategory, string> Codes = new()
    {
        [ReadCategory.Full] = "F", [ReadCategory.Lookup] = "L", [ReadCategory.FormRuntime] = "R", [ReadCategory.OwnRecords] = "O",
        [ReadCategory.Public] = "P", [ReadCategory.GameServer] = "G", [ReadCategory.Stricter] = "U", [ReadCategory.Default] = "D",
    };

    /// <summary>
    /// Every read knk-plugin's api-client (knk-plugin <c>main</c>) makes, by the action it lands on: GETs and search
    /// POSTs. The plugin's key must pass all of them (DESIGN.md § 6: the plugin runs with <c>api.auth.type: apikey</c>).
    /// </summary>
    public static readonly string[] PluginReads =
    {
        "CategoriesController.GetAll", "ClansController.GetAll", "ClansController.GetById", "ClansController.GetDefaultForTown",
        "BannerDesignsController.GetById", "EnchantmentDefinitionsController.GetAll", "EnchantmentDefinitionsController.GetById",
        "EnchantmentDefinitionsController.Search", "GradesController.GetById", "GradesController.Search",
        "ItemBlueprintsController.GetById", "ItemBlueprintsController.Search", "MinecraftMaterialRefsController.GetById",
        "MenuTemplatesController.GetAll", "MenuTemplatesController.GetById", "MenuTemplatesController.GetByKey",
        "TagsController.GetById", "TagsController.Search",
        "DomainsController.Search", "DomainsController.GetDomainById", "DomainsController.GetByRegionName",
        "DomainsController.SearchDomainRegionDecision", "DomainsController.GetAccessRules", "DomainsController.GetDomainsWithoutWorld",
        "DistrictsController.SearchDistricts", "DistrictsController.GetDistrictById", "StructuresController.SearchStructures",
        "StructuresController.GetStructureById", "StreetsController.SearchStreets", "StreetsController.GetStreetById",
        "TownsController.SearchTowns", "TownsController.GetTownById", "LocationsController.SearchLocations",
        "LocationsController.GetLocationById", "GateStructuresController.GetAll", "GateStructuresController.GetById",
        "GateDoorsController.GetByStructure", "GateDoorsController.GetById",
        "WorldTasksController.ListByStatus", "WorldTasksController.GetByLinkCode", "WorldTasksController.GetById",
        "WorldTasksController.ListOpenForServer",
        "KitsController.Search", "KitsController.GetById", "KitsController.GetAvailable", "KitsController.GetPendingDeliveries",
        "SiegeLobbiesController.GetRuntimeConfig", "SiegeScenariosController.GetReadiness", "SiegeMatchesController.PublicPlayerMatches",
        "RoadTilesController.List", "RoadTilesController.GetGraph", "RoadTilesController.ListProposals", "RoadTilesController.GetProposal",
        "RoadNetworkController.GetMeta", "RoadNetworkController.GetSeedLocations", "RoadProfilesController.List",
        "RoadProfilesController.Get", "RoadSurveysController.List", "RoadSeedsController.List", "RoadEdgesController.Search",
        "UsersController.GetUserById", "UsersController.GetUserSummaryByUuid", "UsersController.GetUserSummaryByUsername",
        "UsersController.SearchUsers", "UsersController.CheckPermission", "UsersController.GetEffectivePermissions",
        "UsersController.CheckDuplicateAvailability", "UserPermissionGroupsController.Get", "PermissionGroupsController.GetAll",
        "AdmissionController.Check",
        "StatisticsController.GetCatalog", "StatisticsController.GetUser", "StatisticsController.GetTitleHistory",
        "StatisticsController.GetVisibility", "UserIgnoresController.Get", "TeleportDestinationsController.List",
        "TeleportDestinationsController.Policy", "LeaderboardsController.GetBoards", "LeaderboardsController.GetBoard",
        "DiscoveriesController.GetKnown", "DiscoveriesController.GetSummary", "TelemetryController.GetConfig",
        "CurrencyController.GetBalances", "CurrencyController.GetLeaderboard", "CurrencyController.GetLimits",
        "CurrencyController.GetTransactions", "CurrencyMonitorController.GetAlerts", "CurrencyAdminController.GetTransferLock",
        "LocationRetentionController.GetOrphans", "LocationRetentionController.GetTeleportTarget",
        "LootboxSpawnsController.GetRuntimeConfig", "LootboxSpawnsController.GetActive", "LootboxClaimsController.Pending",
        "LootboxTokensController.Undelivered", "LootboxTypesController.GetOdds", "PlayerNotificationsController.GetPending",
        "NavigationSettingsController.GetCompass", "GameSettingsController.Get", "TitleBracketsController.GetAll",
        "HealthCheckController.GetReadiness",
    };

    // ===== The pinned categories =====

    internal sealed record Endpoint(Type Controller, MethodInfo Action, string Verb, string? Template)
    {
        public string Name => $"{Controller.Name}.{Action.Name}";
    }

    internal static IEnumerable<Endpoint> ReadEndpoints()
    {
        foreach (var controller in typeof(DashboardAccess).Assembly.GetTypes()
                     .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract))
        {
            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var routes = action.GetCustomAttributes<HttpMethodAttribute>().ToList();
                if (routes.Count == 0) continue;
                var read = routes.FirstOrDefault(r => r.HttpMethods.Any(m => m is "GET" or "HEAD"));
                if (read != null)
                {
                    yield return new Endpoint(controller, action, "GET", read.Template);
                    continue;
                }
                var post = routes.FirstOrDefault(r => r.HttpMethods.Contains("POST"));
                if (post != null && (IsSearchTemplate(post.Template) || action.GetCustomAttributes(true).OfType<IKnkReadRule>().Any()))
                {
                    yield return new Endpoint(controller, action, "POST", post.Template);
                }
            }
        }
    }

    private static bool IsSearchTemplate(string? template)
    {
        if (string.IsNullOrEmpty(template)) return false;
        var last = template.TrimEnd('/').Split('/')[^1];
        return last.Equals("search", StringComparison.OrdinalIgnoreCase) || last.StartsWith("search-", StringComparison.OrdinalIgnoreCase);
    }

    private static string Code(Endpoint e)
    {
        var rule = ReadAccess.Of(e.Controller, e.Action);
        return Codes[rule.Category] + (rule.Category == ReadCategory.Full ? " " + rule.Label : "");
    }

    [Fact]
    public void EveryReadEndpoint_HasItsPinnedCategory()
    {
        var endpoints = ReadEndpoints().ToList();
        var unpinned = endpoints.Where(e => !Pinned.ContainsKey(e.Name)).Select(e => $"{e.Name} ({Code(e)})").OrderBy(n => n).ToList();
        Assert.True(unpinned.Count == 0, "Read endpoints without a pinned category (pick one, DESIGN.md § 4.6.1):\n" + string.Join("\n", unpinned));

        var changed = endpoints.Where(e => Pinned[e.Name] != Code(e)).Select(e => $"{e.Name}: pinned {Pinned[e.Name]}, is {Code(e)}").ToList();
        Assert.True(changed.Count == 0, string.Join("\n", changed));

        var stale = Pinned.Keys.Except(endpoints.Select(e => e.Name)).OrderBy(n => n).ToList();
        Assert.True(stale.Count == 0, "No such read endpoint: " + string.Join(", ", stale));
    }

    [Fact]
    public void EveryRegistryTypeWithAController_IsReadThroughItsReadNodes()
    {
        var fullReads = ReadEndpoints().Select(e => ReadAccess.Of(e.Controller, e.Action))
            .Where(r => r.Category == ReadCategory.Full).ToList();
        foreach (var rule in fullReads)
        {
            var entry = DashboardAccess.Find(rule.Label);
            var expected = entry != null ? entry.ReadNodes : new[] { DashboardAccess.AreaNodeOf(Enum.Parse<DashboardArea>(rule.Label!)) };
            Assert.Equal(expected, rule.Nodes);
        }
        // The cross-area page nodes of § 4.6.2 reach the full reads.
        Assert.Contains(StaffPermissions.ServerConfig, ReadAccess.Of(typeof(LocationsController), Method(typeof(LocationsController), "GetAll")).Nodes);
    }

    [Fact]
    public void DashboardReads_IsNotAWriteRule_SoDefaultDenyStillHoldsForWrites()
    {
        Assert.False(DefaultCallerRequiredFilter.HasAccessRule(new object[] { new DashboardReadsAttribute("Town") }));
        Assert.True(DefaultCallerRequiredFilter.HasAccessRule(new object[] { new RequireServiceOrAnyAreaNodeAttribute() }));
    }

    [Fact]
    public void PluginReads_AreAllRealReadEndpoints()
    {
        var names = ReadEndpoints().Select(e => e.Name).ToHashSet();
        Assert.DoesNotContain(PluginReads, n => !names.Contains(n));
    }

    // ===== Behaviour per role =====

    private const int WorldOnly = 1, ContentOnly = 2, ConfigOnly = 3, KitsOnly = 4, DiscoveryOnly = 5, NoNodes = 6, Owner = 7;

    internal sealed class FakePermissions : IPermissionResolutionService
    {
        private readonly Dictionary<int, string[]> _nodes = new()
        {
            [WorldOnly] = new[] { StaffPermissions.ManageWorld },
            [ContentOnly] = new[] { StaffPermissions.ManageContent },
            [ConfigOnly] = new[] { StaffPermissions.ServerConfig },
            [KitsOnly] = new[] { StaffPermissions.ManageKits },
            [DiscoveryOnly] = new[] { StaffPermissions.ManageDiscovery },
            [NoNodes] = Array.Empty<string>(),
            [Owner] = new[] { "*" },
        };

        public int AnyCalls { get; private set; }

        public void Grant(int userId, params string[] nodes) => _nodes[userId] = nodes;

        private bool Holds(int userId, string node) =>
            _nodes.TryGetValue(userId, out var held) && (held.Contains("*") || held.Contains(node));

        public Task<PermissionCheckResponseDto?> CheckAsync(int userId, string node) =>
            Task.FromResult<PermissionCheckResponseDto?>(new PermissionCheckResponseDto
            {
                UserId = userId, Node = node,
                Result = Holds(userId, node) ? PermissionResolutionResult.Granted : PermissionResolutionResult.Undeclared
            });

        public Task<string?> CheckAnyAsync(int userId, IReadOnlyList<string> nodes)
        {
            AnyCalls++;
            return Task.FromResult(nodes.FirstOrDefault(n => Holds(userId, n)));
        }

        public Task<PermissionEffectiveResponseDto?> GetEffectiveAsync(int userId) => throw new NotSupportedException();

        public Task<PermissionGroupCheckResponseDto?> CheckGroupAsync(int groupId, string node) => throw new NotSupportedException();
    }

    internal static MethodInfo Method(Type controller, string action) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Single(m => m.Name == action);

    internal sealed class Pipeline
    {
        public FakePermissions Permissions { get; } = new();
        public PermissionReadCache Cache { get; } = new(null, TimeSpan.FromSeconds(30));

        private IServiceProvider Services()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [PluginServiceAuth.ApiKeyConfigKey] = "plugin-key"
            }).Build());
            var environment = new Mock<IHostEnvironment>();
            environment.Setup(e => e.EnvironmentName).Returns(Environments.Production);
            services.AddSingleton(environment.Object);
            services.AddSingleton<IPermissionResolutionService>(Permissions);
            services.AddSingleton<IPermissionReadCache>(Cache);
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            return services.BuildServiceProvider();
        }

        /// <summary>The HTTP status of the first refusal, or null when every filter passes.</summary>
        public async Task<int?> RunAsync(Type controller, string action, int? userId = null, bool plugin = false, string? method = null)
        {
            var info = Method(controller, action);
            var route = info.GetCustomAttributes<HttpMethodAttribute>().First();
            var verb = method ?? (route.HttpMethods.Contains("GET") ? "GET" : route.HttpMethods.First());

            var services = Services();
            var http = new DefaultHttpContext { RequestServices = services };
            http.Request.Method = verb;
            if (userId != null)
                http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("uid", userId.Value.ToString()) }, "Bearer"));
            if (plugin) http.Request.Headers[PluginServiceAuth.ApiKeyHeader] = "plugin-key";

            var classAttributes = controller.GetCustomAttributes(true);
            var methodAttributes = info.GetCustomAttributes(true);
            var descriptor = new ControllerActionDescriptor
            {
                ControllerTypeInfo = controller.GetTypeInfo(),
                MethodInfo = info,
                ControllerName = controller.Name,
                ActionName = info.Name,
                EndpointMetadata = classAttributes.Concat(methodAttributes).ToList(),
                AttributeRouteInfo = new AttributeRouteInfo { Template = route.Template ?? "" },
            };

            var filters = new List<IFilterMetadata> { new DefaultCallerRequiredFilter(NullLogger<DefaultCallerRequiredFilter>.Instance) };
            filters.AddRange(classAttributes.Concat(methodAttributes).OfType<IFilterMetadata>());
            foreach (var metadata in filters)
            {
                var filter = metadata is IFilterFactory factory ? factory.CreateInstance(services) : metadata;
                var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), descriptor), filters);
                switch (filter)
                {
                    case IAsyncAuthorizationFilter asyncFilter:
                        await asyncFilter.OnAuthorizationAsync(context);
                        break;
                    case IAuthorizationFilter syncFilter:
                        syncFilter.OnAuthorization(context);
                        break;
                    default:
                        continue;
                }
                if (context.Result != null) return Status(context.Result);
            }
            return null;
        }

        public static int Status(IActionResult result) => result switch
        {
            UnauthorizedObjectResult => 401,
            ObjectResult o => o.StatusCode ?? 200,
            StatusCodeResult s => s.StatusCode,
            _ => -1
        };
    }

    private readonly Pipeline _pipeline = new();

    [Fact]
    public async Task WorldOnly_CantReadItems_ButCanLookThemUp()
    {
        Assert.Equal(403, await _pipeline.RunAsync(typeof(ItemBlueprintsController), "GetAll", WorldOnly));
        Assert.Equal(403, await _pipeline.RunAsync(typeof(ItemBlueprintsController), "Search", WorldOnly));
        Assert.Null(await _pipeline.RunAsync(typeof(LookupController), "Search", WorldOnly));
        Assert.Null(await _pipeline.RunAsync(typeof(LookupController), "Get", WorldOnly));
        Assert.Null(await _pipeline.RunAsync(typeof(TownsController), "GetAll", WorldOnly));
    }

    [Fact]
    public async Task ContentOnly_CantReadWorldData()
    {
        Assert.Equal(403, await _pipeline.RunAsync(typeof(TownsController), "SearchTowns", ContentOnly));
        Assert.Equal(403, await _pipeline.RunAsync(typeof(GateDoorsController), "GetSnapshots", ContentOnly));
        Assert.Equal(403, await _pipeline.RunAsync(typeof(WorldTasksController), "Search", ContentOnly));
        Assert.Null(await _pipeline.RunAsync(typeof(CategoriesController), "GetChildren", ContentOnly));
    }

    [Theory]
    [InlineData(typeof(LocationsController), "GetAll")]
    [InlineData(typeof(TownsController), "SearchTowns")]
    [InlineData(typeof(DistrictsController), "GetAll")]
    [InlineData(typeof(StructuresController), "GetAll")]
    public async Task ConfigOnly_ReadsWhatGameSettingsNeeds(Type controller, string action)
    {
        Assert.Null(await _pipeline.RunAsync(controller, action, ConfigOnly));
    }

    [Fact]
    public async Task CrossAreaPages_ReadOnlyWhatTheyNeed()
    {
        Assert.Equal(403, await _pipeline.RunAsync(typeof(StreetsController), "GetAll", ConfigOnly));
        Assert.Null(await _pipeline.RunAsync(typeof(ItemBlueprintsController), "Search", KitsOnly));
        Assert.Null(await _pipeline.RunAsync(typeof(GradesController), "GetAll", KitsOnly));
        Assert.Equal(403, await _pipeline.RunAsync(typeof(TagsController), "GetAll", KitsOnly));
        // Discovery overrides list domains, but nothing else of the world.
        Assert.Null(await _pipeline.RunAsync(typeof(DomainsController), "GetAll", DiscoveryOnly));
        Assert.Null(await _pipeline.RunAsync(typeof(DomainsController), "Search", DiscoveryOnly));
        Assert.Equal(403, await _pipeline.RunAsync(typeof(DomainsController), "GetAccessRules", DiscoveryOnly));
        Assert.Equal(403, await _pipeline.RunAsync(typeof(TownsController), "GetAll", DiscoveryOnly));
        Assert.Null(await _pipeline.RunAsync(typeof(DomainsController), "GetAccessRules", WorldOnly));
    }

    [Fact]
    public async Task WizardNeeds_AreOpenToAnyAreaNode()
    {
        foreach (var user in new[] { WorldOnly, KitsOnly, ContentOnly })
        {
            Assert.Null(await _pipeline.RunAsync(typeof(MetadataController), "GetEntityMetadata", user));
            Assert.Null(await _pipeline.RunAsync(typeof(FormConfigurationsController), "GetAllByEntityTypeName", user));
            Assert.Null(await _pipeline.RunAsync(typeof(DisplayConfigurationsController), "GetDefaultByEntityTypeNameAsync", user));
            Assert.Null(await _pipeline.RunAsync(typeof(FieldValidationRulesController), "ValidateField", user));
            Assert.Null(await _pipeline.RunAsync(typeof(FieldValidationRulesController), "ResolvePlaceholders", user));
            Assert.Null(await _pipeline.RunAsync(typeof(FormSubmissionProgressController), "GetByUserId", user));
            Assert.Null(await _pipeline.RunAsync(typeof(FormSubmissionProgressController), "SaveProgress", user));
            Assert.Null(await _pipeline.RunAsync(typeof(WorldTasksController), "GetActiveForMe", user));
            Assert.Null(await _pipeline.RunAsync(typeof(WorkflowsController), "GetProgress", user));
            Assert.Null(await _pipeline.RunAsync(typeof(BannerDesignsController), "GetPatternKeys", user));
            Assert.Null(await _pipeline.RunAsync(typeof(ClansController), "GetDefaultForTown", user));
        }
    }

    [Fact]
    public async Task BuilderWrites_AndBuilderTools_StayContent()
    {
        Assert.Equal(403, await _pipeline.RunAsync(typeof(FormConfigurationsController), "Create", WorldOnly));
        Assert.Equal(403, await _pipeline.RunAsync(typeof(FieldValidationRulesController), "Create", WorldOnly));
        Assert.Equal(403, await _pipeline.RunAsync(typeof(FormConfigurationsController), "Export", WorldOnly));
        Assert.Equal(403, await _pipeline.RunAsync(typeof(DisplayConfigurationsController), "GetSeeds", WorldOnly));
        Assert.Null(await _pipeline.RunAsync(typeof(FormConfigurationsController), "Export", ContentOnly));
        Assert.Null(await _pipeline.RunAsync(typeof(FormConfigurationsController), "Create", ContentOnly));
    }

    [Fact]
    public async Task Writes_KeepTheirAreaNode()
    {
        Assert.Equal(403, await _pipeline.RunAsync(typeof(TownsController), "Create", ConfigOnly));
        Assert.Null(await _pipeline.RunAsync(typeof(TownsController), "Create", WorldOnly));
        Assert.Equal(403, await _pipeline.RunAsync(typeof(DomainsController), "ResolveWorld", DiscoveryOnly));
        Assert.Null(await _pipeline.RunAsync(typeof(DomainsController), "ResolveWorld", WorldOnly));
    }

    [Fact]
    public async Task NoAreaNode_IsRefusedEverywhereInTheModel_ButKeepsPublicSelfAndDefaultReads()
    {
        Assert.Equal(403, await _pipeline.RunAsync(typeof(KitsController), "GetAll", NoNodes));
        Assert.Equal(403, await _pipeline.RunAsync(typeof(LookupController), "Search", NoNodes));
        Assert.Equal(403, await _pipeline.RunAsync(typeof(MetadataController), "GetAllEntityMetadata", NoNodes));
        Assert.Equal(403, await _pipeline.RunAsync(typeof(FormSubmissionProgressController), "GetById", NoNodes));
        Assert.Equal(403, await _pipeline.RunAsync(typeof(UsersController), "GetUserSummaryByUuid", NoNodes));

        Assert.Null(await _pipeline.RunAsync(typeof(LeaderboardsController), "GetBoards"));
        Assert.Null(await _pipeline.RunAsync(typeof(EnchantmentDefinitionsController), "PublicInfo"));
        Assert.Null(await _pipeline.RunAsync(typeof(PlayersController), "GetByName"));
        Assert.Null(await _pipeline.RunAsync(typeof(AuthController), "Me", NoNodes));
        Assert.Null(await _pipeline.RunAsync(typeof(GameSettingsController), "Get", NoNodes));
        Assert.Null(await _pipeline.RunAsync(typeof(TitleBracketsController), "GetAll", NoNodes));
    }

    [Fact]
    public async Task SelfReads_StayOpenToTheUserThemselves()
    {
        // RequireServiceSelfOrPermission reads the route's id; the pipeline has no route values, so only the node path
        // is exercised here (RequireServiceSelfOrPermissionAttributeTests covers "self"). The category must stay U.
        Assert.Equal("U", Pinned["UsersController.GetUserById"]);
        Assert.Equal("U", Pinned["UsersController.CheckPermission"]);
        Assert.Equal("U", Pinned["UsersController.GetEffectivePermissions"]);
        Assert.Null(await _pipeline.RunAsync(typeof(UsersController), "GetUserById", Owner));
    }

    [Fact]
    public async Task Anonymous_Gets401_OnEveryGatedRead()
    {
        foreach (var e in ReadEndpoints().Where(e => ReadAccess.Of(e.Controller, e.Action).Category
                     is ReadCategory.Full or ReadCategory.Lookup or ReadCategory.FormRuntime or ReadCategory.OwnRecords))
        {
            Assert.True(await _pipeline.RunAsync(e.Controller, e.Action.Name, method: e.Verb) == 401, e.Name);
        }
    }

    [Fact]
    public async Task PluginKey_PassesEveryFullLookupAndRuntimeRead()
    {
        foreach (var e in ReadEndpoints().Where(e => ReadAccess.Of(e.Controller, e.Action).Category
                     is ReadCategory.Full or ReadCategory.Lookup or ReadCategory.FormRuntime))
        {
            Assert.True(await _pipeline.RunAsync(e.Controller, e.Action.Name, plugin: true, method: e.Verb) == null, e.Name);
        }
    }

    [Fact]
    public async Task PluginKey_PassesEveryReadThePluginMakes()
    {
        var byName = ReadEndpoints().ToDictionary(e => e.Name);
        var refused = new List<string>();
        foreach (var name in PluginReads)
        {
            var e = byName[name];
            var status = await _pipeline.RunAsync(e.Controller, e.Action.Name, plugin: true, method: e.Verb);
            if (status != null) refused.Add($"{name}: {status}");
        }
        Assert.True(refused.Count == 0, string.Join("\n", refused));
    }

    [Fact]
    public async Task Drafts_StayWebOnly()
    {
        Assert.Equal(401, await _pipeline.RunAsync(typeof(FormSubmissionProgressController), "GetByUserId", plugin: true));
        Assert.Null(await _pipeline.RunAsync(typeof(WorldTasksController), "GetByLinkCode", plugin: true));
    }

    [Fact]
    public void Refusal_NamesTheType_AndItsReadNodes()
    {
        var result = ReadAccess.Refused("Kit", DashboardAccess.Find("Kit")!.ReadNodes) as ObjectResult;
        Assert.Equal(403, result!.StatusCode);
        var body = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.Contains("\"code\":\"NoReadAccess\"", body);
        Assert.Contains("\"entityType\":\"Kit\"", body);
        Assert.Contains(StaffPermissions.ManageLootboxes, body);
    }

    [Fact]
    public async Task ReadChecks_AreCached_AndForgottenWhenGrantsChange()
    {
        Assert.Equal(403, await _pipeline.RunAsync(typeof(TownsController), "GetAll", ContentOnly));
        var calls = _pipeline.Permissions.AnyCalls;
        Assert.Equal(403, await _pipeline.RunAsync(typeof(TownsController), "GetAll", ContentOnly));
        Assert.Equal(calls, _pipeline.Permissions.AnyCalls);

        _pipeline.Permissions.Grant(ContentOnly, StaffPermissions.ManageContent, StaffPermissions.ManageWorld);
        Assert.Equal(403, await _pipeline.RunAsync(typeof(TownsController), "GetAll", ContentOnly)); // still the cached answer
        _pipeline.Cache.ForgetUser(ContentOnly);
        Assert.Null(await _pipeline.RunAsync(typeof(TownsController), "GetAll", ContentOnly));
    }
}
