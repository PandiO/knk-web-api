using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using AutoMapper;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.ValidationMethods;

namespace knkwebapi_v2.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// KNG-216: the named "cloudflare" HttpClient (base address, bearer token, 10 s timeout,
        /// the optional forward proxy), the Zero Trust email list, the sync singleton and its
        /// reconcile loop. Settings are read when the client is built, so tests can configure them.
        /// </summary>
        public static IServiceCollection AddCloudflareAccessSync(this IServiceCollection services)
        {
            services.AddOptions<knkwebapi_v2.Configuration.CloudflareAccessSettings>();
            services.AddHttpClient(knkwebapi_v2.Configuration.CloudflareAccessSettings.HttpClientName, (sp, client) =>
                {
                    var cf = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<knkwebapi_v2.Configuration.CloudflareAccessSettings>>().Value;
                    client.BaseAddress = new System.Uri(cf.BaseUrl);
                    client.Timeout = System.TimeSpan.FromSeconds(System.Math.Clamp(cf.RequestTimeoutSeconds, 1, 60));
                    if (!string.IsNullOrWhiteSpace(cf.ApiToken))
                    {
                        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cf.ApiToken);
                    }
                })
                .ConfigurePrimaryHttpMessageHandler(sp =>
                {
                    var cf = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<knkwebapi_v2.Configuration.CloudflareAccessSettings>>().Value;
                    var handler = new System.Net.Http.HttpClientHandler();
                    if (!string.IsNullOrWhiteSpace(cf.ProxyUrl) && System.Uri.TryCreate(cf.ProxyUrl, System.UriKind.Absolute, out var proxy))
                    {
                        handler.Proxy = new System.Net.WebProxy(proxy);
                        handler.UseProxy = true;
                    }
                    return handler;
                });
            services.AddSingleton<knkwebapi_v2.Services.CloudflareAccess.ICloudflareAccessList, knkwebapi_v2.Services.CloudflareAccess.ZeroTrustEmailList>();
            services.AddScoped<knkwebapi_v2.Services.CloudflareAccess.ICloudflareAccessDesiredSet, knkwebapi_v2.Services.CloudflareAccess.CloudflareAccessDesiredSet>();
            services.AddSingleton<knkwebapi_v2.Services.CloudflareAccess.CloudflareAccessSync>();
            services.AddSingleton<knkwebapi_v2.Services.CloudflareAccess.ICloudflareAccessSync>(sp => sp.GetRequiredService<knkwebapi_v2.Services.CloudflareAccess.CloudflareAccessSync>());
            services.AddHostedService<knkwebapi_v2.Services.CloudflareAccess.CloudflareAccessSyncService>();
            return services;
        }

        /// <summary>
        /// Centralized registration for application services and repositories.
        /// - Add explicit registrations for special cases.
        /// - Use simple convention scanning to auto-register IThing -> Thing for types named *Repository or *Service.
        /// Consider adding Scrutor (NuGet) for richer scanning and lifetime control when needed.
        /// </summary>
        public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration? configuration = null)
        {
            services.AddMemoryCache();

            // explicit registrations (recommended for clarity)
            services.AddScoped<ICategoryRepository, CategoryRepository>();
            services.AddScoped<ICategoryService, CategoryService>();
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IUserService, UserService>();
            services.AddSingleton<IPlayerNotificationQueue, InMemoryPlayerNotificationQueue>();
            services.AddScoped<IPasswordService, PasswordService>();
            services.AddScoped<ITokenService, TokenService>();
            services.AddScoped<IAuthService, AuthService>();
            // Closed-alpha WP4: per-user session state (TokenVersion, active) cached for the
            // per-request token check; one instance so a revocation invalidates it for everyone.
            services.AddSingleton<IUserSessionStateCache, UserSessionStateCache>();
            // Closed-alpha WP6: per-account login lockout.
            services.AddSingleton<ILoginAttemptLimiter, LoginAttemptLimiter>();
            // KNG-208: failed link/claim codes per account, and hourly linkcodes housekeeping.
            services.AddSingleton<ILinkAttemptLimiter, LinkAttemptLimiter>();
            services.AddHostedService<LinkCodeCleanupService>();
            // Closed-alpha WP3: staff can't hand out nodes or groups they don't hold.
            services.AddScoped<IPermissionEscalationGuard, PermissionEscalationGuard>();
            // PandiO/knk-workspace#565: kick and ban from the web app (audited, sent to the game servers over the hub).
            services.AddScoped<IPlayerModerationService>(sp => new PlayerModerationService(
                sp.GetRequiredService<IUserRepository>(), sp.GetRequiredService<IAuditLogService>(),
                sp.GetRequiredService<IPermissionEscalationGuard>(),
                sp.GetRequiredService<knkwebapi_v2.Realtime.RealtimeConnections>(),
                sp.GetService<knkwebapi_v2.Realtime.IRealtimeEventSink>()));
            // PandiO/knk-workspace#280 AM2: dashboard read checks, 30 s per user and node, dropped when grants change.
            services.AddSingleton<IPermissionReadCache>(_ => new PermissionReadCache());
            // KNG-212: invitations and invite-based registration (Security:Registration:Mode).
            services.AddScoped<IInvitationRepository, InvitationRepository>();
            services.AddScoped<knkwebapi_v2.Services.Invitations.IInvitationService, knkwebapi_v2.Services.Invitations.InvitationService>();
            // KNG-214: the game server's admission gate (claim codes) and the FinishLinking reminder.
            services.AddScoped<IAdmissionService, AdmissionService>();
            var admissionSection = configuration?.GetSection(knkwebapi_v2.Configuration.AdmissionSettings.SectionName);
            if (admissionSection != null)
            {
                services.Configure<knkwebapi_v2.Configuration.AdmissionSettings>(admissionSection);
            }
            var invitationsSection = configuration?.GetSection(knkwebapi_v2.Configuration.InvitationSettings.SectionName);
            if (invitationsSection != null)
            {
                services.Configure<knkwebapi_v2.Configuration.InvitationSettings>(invitationsSection);
            }

            // KNG-216: the Cloudflare Access sync (DESIGN § 5.8). Off by default: with
            // CloudflareAccess:SyncEnabled=false nothing calls Cloudflare.
            var cloudflareSection = configuration?.GetSection(knkwebapi_v2.Configuration.CloudflareAccessSettings.SectionName);
            if (cloudflareSection != null)
            {
                services.Configure<knkwebapi_v2.Configuration.CloudflareAccessSettings>(cloudflareSection);
            }
            services.AddCloudflareAccessSync();

            var emailSection = configuration?.GetSection(knkwebapi_v2.Configuration.EmailSettings.SectionName);
            if (emailSection != null)
            {
                services.Configure<knkwebapi_v2.Configuration.EmailSettings>(emailSection);
            }
            var appSection = configuration?.GetSection(knkwebapi_v2.Configuration.AppSettings.SectionName);
            if (appSection != null)
            {
                services.Configure<knkwebapi_v2.Configuration.AppSettings>(appSection);
            }
            // KNG-206: the mail engine. Callers queue through IEmailComposer into email_outbox (same
            // DbContext, same transaction); EmailDispatcher sends with the transport Email:Transport
            // names (Email:Provider as the fallback; Log by default).
            services.AddSingleton<knkwebapi_v2.Services.Email.EmailDispatchSignal>();
            services.AddSingleton<knkwebapi_v2.Services.Email.IEmailTemplateRenderer, knkwebapi_v2.Services.Email.EmailTemplateRenderer>();
            services.AddScoped<knkwebapi_v2.Services.Email.IEmailThrottle, knkwebapi_v2.Services.Email.EmailThrottle>();
            services.AddScoped<knkwebapi_v2.Services.Email.IEmailComposer, knkwebapi_v2.Services.Email.EmailComposer>();
            services.AddHostedService<knkwebapi_v2.Services.Email.EmailDispatcher>();
            services.AddScoped<knkwebapi_v2.Services.Email.IEmailOutboxAdminService, knkwebapi_v2.Services.Email.EmailOutboxAdminService>();
            var emailTransport = (emailSection?.Get<knkwebapi_v2.Configuration.EmailSettings>() ?? new knkwebapi_v2.Configuration.EmailSettings())
                .EffectiveTransport();
            switch (emailTransport)
            {
                case knkwebapi_v2.Configuration.EmailSettings.SmtpTransport:
                    services.AddScoped<knkwebapi_v2.Services.Email.IEmailTransport, knkwebapi_v2.Services.Email.SmtpEmailTransport>();
                    break;
                case knkwebapi_v2.Configuration.EmailSettings.PickupTransport:
                    services.AddScoped<knkwebapi_v2.Services.Email.IEmailTransport, knkwebapi_v2.Services.Email.PickupEmailTransport>();
                    break;
                default:
                    services.AddScoped<knkwebapi_v2.Services.Email.IEmailTransport, knkwebapi_v2.Services.Email.LogEmailTransport>();
                    break;
            }
            var discoverySection = configuration?.GetSection(knkwebapi_v2.Configuration.DiscoveryOptions.SectionName);
            if (discoverySection != null)
            {
                services.Configure<knkwebapi_v2.Configuration.DiscoveryOptions>(discoverySection);
            }
            services.AddScoped<ILinkCodeRepository, LinkCodeRepository>();
            services.AddScoped<ILinkCodeService, LinkCodeService>();
            services.AddScoped<ILocationRepository, LocationRepository>();
            services.AddScoped<ILocationService, LocationService>();
            services.AddScoped<IDomainRepository, DomainRepository>();
            services.AddScoped<IDomainService, DomainService>();
            services.AddScoped<IDomainSubRegionService, DomainSubRegionService>();
            // Overview map L1 (PandiO/knk-workspace#735): outlines the game server reports over the hub.
            services.AddScoped<knkwebapi_v2.Services.Map.IMapRegionOutlineService, knkwebapi_v2.Services.Map.MapRegionOutlineService>();
            // Overview map L2 (PandiO/knk-workspace#736): the public map endpoints, off by default (Map:Enabled).
            if (configuration != null)
            {
                services.Configure<knkwebapi_v2.Configuration.MapOptions>(
                    configuration.GetSection(knkwebapi_v2.Configuration.MapOptions.SectionName));
            }
            services.AddSingleton<knkwebapi_v2.Services.Map.MapMarkerCache>();
            services.AddScoped<knkwebapi_v2.Services.Map.IMapPublicService, knkwebapi_v2.Services.Map.MapPublicService>();
            // Overview map L4 (#738): live players (registry and audience snapshots in memory, reads per tick in a scope).
            services.AddSingleton<knkwebapi_v2.Services.Map.MapPlayerRegistry>();
            services.AddSingleton<knkwebapi_v2.Services.Map.MapPlayerSnapshots>();
            services.AddScoped<knkwebapi_v2.Services.Map.IMapLivePlayerData, knkwebapi_v2.Services.Map.MapLivePlayerData>();
            // Overview map L7 (#741): live gate commands (Map:GateCommands:Enabled, off by default); results wait in the singleton.
            services.AddSingleton<knkwebapi_v2.Services.Map.GateCommandWaiter>();
            services.AddScoped<knkwebapi_v2.Services.Map.IGateCommandService, knkwebapi_v2.Services.Map.GateCommandService>();
            services.AddScoped<IMinecraftMaterialRefRepository, MinecraftMaterialRefRepository>();
            services.AddScoped<IMinecraftMaterialRefService, MinecraftMaterialRefService>();
            services.AddScoped<IMinecraftBlockRefRepository, MinecraftBlockRefRepository>();
            services.AddScoped<IMinecraftBlockRefService, MinecraftBlockRefService>();
            services.AddScoped<IMinecraftCatalogMaintenanceService, knkwebapi_v2.Services.MinecraftCatalog.MinecraftCatalogMaintenanceService>();
            services.AddScoped<IMinecraftCatalogScanService, knkwebapi_v2.Services.MinecraftCatalog.MinecraftCatalogScanService>();
            services.AddSingleton(sp => sp.GetRequiredService<IConfiguration>()
                .GetSection(knkwebapi_v2.Services.MinecraftCatalog.MinecraftIconOptions.SectionName)
                .Get<knkwebapi_v2.Services.MinecraftCatalog.MinecraftIconOptions>() ?? new knkwebapi_v2.Services.MinecraftCatalog.MinecraftIconOptions());
            services.AddScoped<knkwebapi_v2.Services.MinecraftCatalog.IMinecraftIconResolver, knkwebapi_v2.Services.MinecraftCatalog.MinecraftIconResolver>();
            services.AddScoped<IMinecraftEnchantmentRefRepository, MinecraftEnchantmentRefRepository>();
            services.AddScoped<IMinecraftEnchantmentRefService, MinecraftEnchantmentRefService>();
            services.AddScoped<IFormConfigurationRepository, FormConfigurationRepository>();
            services.AddScoped<IFormConfigurationService, FormConfigurationService>();
            // KNG-138: bundled FormConfiguration seed files, imported on request (no startup seeder).
            services.AddSingleton(sp => new FormConfigurationSeedOptions
            {
                Directory = Path.Combine(sp.GetRequiredService<IHostEnvironment>().ContentRootPath, "Data", "FormConfigurationSeeds")
            });
            services.AddScoped<IFormConfigurationPortabilityService, FormConfigurationPortabilityService>();
            services.AddScoped<IFormConfigurationDraftService, FormConfigurationDraftService>(); // #71
            services.AddScoped<IFormStepRepository, FormStepRepository>();
            services.AddScoped<IFormStepService, FormStepService>();
            services.AddScoped<IFormFieldRepository, FormFieldRepository>();
            services.AddScoped<IFormFieldService, FormFieldService>();
            services.AddScoped<IFieldValidationRuleRepository, FieldValidationRuleRepository>();
            services.AddScoped<IValidationService, ValidationService>();
            services.AddScoped<knkwebapi_v2.Services.ValidationResultLog.IValidationResultLog, knkwebapi_v2.Services.ValidationResultLog.EfValidationResultLog>();
            services.AddScoped<ISavedEntityRuleValidator, SavedEntityRuleValidator>();
            services.AddScoped<IFieldValidationRuleService, FieldValidationRuleService>();
            services.AddScoped<IPlaceholderResolutionService, PlaceholderResolutionService>();
            services.AddScoped<DependencyResolutionService>();
            services.AddScoped<IDependencyResolutionService>(sp =>
                new CachedDependencyResolutionService(
                    sp.GetRequiredService<IMemoryCache>(),
                    sp.GetRequiredService<DependencyResolutionService>()));
            services.AddScoped<IPathResolutionService, PathResolutionService>();
            services.AddScoped<IFormSubmissionProgressRepository, FormSubmissionProgressRepository>();
            services.AddScoped<IFormSubmissionProgressService, FormSubmissionProgressService>();
            services.AddScoped<IEntityReferenceService, EntityReferenceService>();
            services.AddScoped<IGateStructureRepository, GateStructureRepository>();
            services.AddScoped<IGateStructureService, GateStructureService>();
            services.AddScoped<IGateDoorRepository, GateDoorRepository>();
            services.AddScoped<IGateDoorService, GateDoorService>();
            services.AddScoped<IItemBlueprintRepository, ItemBlueprintRepository>();
            services.AddScoped<IItemBlueprintService, ItemBlueprintService>();
            services.AddScoped<IItemIdentityService, knkwebapi_v2.Services.ItemIdentity.ItemIdentityService>(); // #141
            services.AddScoped<IEnchantmentDefinitionRepository, EnchantmentDefinitionRepository>();
            services.AddScoped<IEnchantmentDefinitionService, EnchantmentDefinitionService>();
            // PandiO/knk-workspace#221: the public enchantment info sheets.
            services.AddScoped<knkwebapi_v2.Services.EnchantmentInfo.IEnchantmentInfoService,
                knkwebapi_v2.Services.EnchantmentInfo.EnchantmentInfoService>();
            services.AddScoped<IGradeRepository, GradeRepository>();
            services.AddScoped<IGradeService, GradeService>();
            services.AddScoped<ITagRepository, TagRepository>();
            services.AddScoped<ITagService, TagService>();
            // Off-road navigation phase 5.1 (PandiO/knk-workspace#708): landmarks.
            services.AddScoped<ILandmarkRepository, LandmarkRepository>();
            services.AddScoped<ILandmarkService, LandmarkService>();
            // Territory V1 (PandiO/knk-workspace#326)
            services.AddScoped<knkwebapi_v2.Services.Territory.IProductionSettingsReader, knkwebapi_v2.Services.Territory.ProductionSettingsReader>();
            services.AddScoped<ITerritoryHolderRules, TerritoryHolderRules>();
            services.AddScoped<IKingdomService, KingdomService>();
            services.AddScoped<IProvinceService, ProvinceService>();
            services.AddScoped<ICrownLandService, CrownLandService>();
            services.AddScoped<ITerritoryConflictService, TerritoryConflictService>();
            // Stock core P1 (PandiO/knk-workspace#97): storages, the ledger, reconciler, roll-up; background work gated
            // by Production:Enabled.
            services.AddScoped<knkwebapi_v2.Services.Stock.IStockService, knkwebapi_v2.Services.Stock.StockService>();
            services.AddScoped<knkwebapi_v2.Services.Stock.IStorageService, knkwebapi_v2.Services.Stock.StorageService>();
            services.AddScoped<knkwebapi_v2.Services.Stock.IStockLedgerService, knkwebapi_v2.Services.Stock.StockLedgerService>();
            services.AddScoped<knkwebapi_v2.Services.Stock.IStockReconciler, knkwebapi_v2.Services.Stock.StockReconciler>();
            services.AddScoped<knkwebapi_v2.Services.Stock.IStockRollupService, knkwebapi_v2.Services.Stock.StockRollupService>();
            services.AddScoped<knkwebapi_v2.Services.Stock.StockMaintenanceJob>();
            services.AddHostedService<knkwebapi_v2.Services.Stock.StockMaintenanceService>();
            services.AddHostedService<knkwebapi_v2.Services.Stock.InboxMaintenanceService>(); // P7 (#60): inbox reminders and expiry, flag-gated
            services.AddScoped<ITerritoryService, TerritoryService>();
            // Production world P2 (PandiO/knk-workspace#91): District-xor-Town placement, placement rules on save,
            // Warehouse and ProductionStructure CRUD with default storages.
            services.AddScoped<knkwebapi_v2.Services.Production.IStructurePlacement, knkwebapi_v2.Services.Production.StructurePlacement>();
            services.AddScoped<knkwebapi_v2.Services.Production.StructureBaseWriter>();
            services.AddScoped<knkwebapi_v2.Services.Production.IWarehouseService, knkwebapi_v2.Services.Production.WarehouseService>();
            services.AddScoped<knkwebapi_v2.Services.Production.IProductionStructureService, knkwebapi_v2.Services.Production.ProductionStructureService>();

            // Production engine P3 (PandiO/knk-workspace#86): kind catalogue and levels, lines, modifiers, weather, the
            // settle engine and its sweep (gated by Production:Enabled; admin CRUD always available). Modifier sources and
            // base-rate providers are extension points: P4 adds Derived/Hybrid providers, later phases more sources.
            services.AddScoped<knkwebapi_v2.Services.Production.IProductionWeatherService, knkwebapi_v2.Services.Production.ProductionWeatherService>();
            services.AddScoped<knkwebapi_v2.Services.Production.IProductionModifierSource, knkwebapi_v2.Services.Production.LevelModifierSource>();
            services.AddScoped<knkwebapi_v2.Services.Production.IProductionModifierSource, knkwebapi_v2.Services.Production.WeatherModifierSource>();
            services.AddScoped<knkwebapi_v2.Services.Production.IProductionModifierSource, knkwebapi_v2.Services.Production.StoredModifierSource>();
            services.AddScoped<knkwebapi_v2.Services.Production.IProductionBaseRateProvider, knkwebapi_v2.Services.Production.ConfiguredBaseRateProvider>();
            services.AddScoped<knkwebapi_v2.Services.Production.IProductionEngine, knkwebapi_v2.Services.Production.ProductionEngine>();
            services.AddScoped<knkwebapi_v2.Services.Production.IProductionKindService, knkwebapi_v2.Services.Production.ProductionKindService>();
            services.AddScoped<knkwebapi_v2.Services.Production.IProductionKindLevelService, knkwebapi_v2.Services.Production.ProductionKindLevelService>();
            services.AddScoped<knkwebapi_v2.Services.Production.IProductionLineService, knkwebapi_v2.Services.Production.ProductionLineService>();
            services.AddScoped<knkwebapi_v2.Services.Production.IProductionModifierService, knkwebapi_v2.Services.Production.ProductionModifierService>();
            services.AddScoped<knkwebapi_v2.Services.Production.IClimateZoneService, knkwebapi_v2.Services.Production.ClimateZoneService>();
            services.AddScoped<knkwebapi_v2.Services.Production.IWeatherTypeService, knkwebapi_v2.Services.Production.WeatherTypeService>();
            services.AddScoped<knkwebapi_v2.Services.Production.IProductionCycleRecordService, knkwebapi_v2.Services.Production.ProductionCycleRecordService>();
            services.AddHostedService<knkwebapi_v2.Services.Production.ProductionSweepService>();
            // Yield areas and surveys P4 (PandiO/knk-workspace#138): Derived and Hybrid base rates from the latest surveys,
            // the primary and variation steps, areas, the survey queue and history, Hybrid overrides and materialization.
            services.AddScoped<knkwebapi_v2.Services.Production.IProductionYieldCalculator, knkwebapi_v2.Services.Production.ProductionYieldCalculator>();
            services.AddScoped<knkwebapi_v2.Services.Production.IProductionBaseRateProvider, knkwebapi_v2.Services.Production.DerivedBaseRateProvider>();
            services.AddScoped<knkwebapi_v2.Services.Production.IProductionBaseRateProvider, knkwebapi_v2.Services.Production.HybridBaseRateProvider>();
            services.AddScoped<knkwebapi_v2.Services.Production.IProductionModifierSource, knkwebapi_v2.Services.Production.PrimaryOutputModifierSource>();
            services.AddScoped<knkwebapi_v2.Services.Production.IProductionModifierSource, knkwebapi_v2.Services.Production.VariationModifierSource>();
            services.AddScoped<knkwebapi_v2.Services.Production.IProductionYieldService, knkwebapi_v2.Services.Production.ProductionYieldService>();
            services.AddScoped<knkwebapi_v2.Services.Production.IProductionAreaService, knkwebapi_v2.Services.Production.ProductionAreaService>();
            services.AddScoped<knkwebapi_v2.Services.Production.IAreaSurveyService, knkwebapi_v2.Services.Production.AreaSurveyService>();
            services.AddScoped<knkwebapi_v2.Services.Production.IProductionYieldOverrideService, knkwebapi_v2.Services.Production.ProductionYieldOverrideService>();

            // Transport core P5 (PandiO/knk-workspace#79): orders with a Transit storage, the simulated carrier (the
            // producer's own worker, G14), lanes, the automatic trigger and the sweep (gated by Production:Enabled; admin
            // CRUD, manual orders, dry runs and cancel always available).
            services.AddScoped<knkwebapi_v2.Services.Production.ITransportService, knkwebapi_v2.Services.Production.TransportService>();
            services.AddScoped<knkwebapi_v2.Services.Production.ITransportLaneService, knkwebapi_v2.Services.Production.TransportLaneService>();
            // Terminals and menus P6a (PandiO/knk-workspace#683): containers, the M3 caller filter, structure menus.
            services.AddScoped<knkwebapi_v2.Services.Production.ITownMembershipResolver, knkwebapi_v2.Services.Production.PermissionTownMembershipResolver>();
            services.AddScoped<knkwebapi_v2.Services.Production.IProductionViewerResolver, knkwebapi_v2.Services.Production.ProductionViewerResolver>();
            services.AddScoped<knkwebapi_v2.Services.Stock.IStorageContainerService, knkwebapi_v2.Services.Stock.StorageContainerService>();
            services.AddScoped<knkwebapi_v2.Services.Production.IStructureMenuService, knkwebapi_v2.Services.Production.StructureMenuService>();
            // Item delivery and reward inbox P7 (PandiO/knk-workspace#60): deliveries, claims, stash, hourly expiry.
            services.AddScoped<knkwebapi_v2.Services.Stock.IItemDeliveryService, knkwebapi_v2.Services.Stock.ItemDeliveryService>();
            services.AddHostedService<knkwebapi_v2.Services.Production.TransportSweepService>();
            // Siege Phase 1 — banner + minimal clan
            services.AddScoped<IBannerDesignRepository, BannerDesignRepository>();
            services.AddScoped<IBannerDesignService, BannerDesignService>();
            services.AddScoped<IClanRepository, ClanRepository>();
            services.AddScoped<IClanService, ClanService>();
            // Siege Phase 2 — scenarios, lobbies, configuration singleton
            services.AddScoped<ISiegeScenarioRepository, SiegeScenarioRepository>();
            services.AddScoped<ISiegeScenarioService, SiegeScenarioService>();
            services.AddScoped<ISiegeLobbyRepository, SiegeLobbyRepository>();
            services.AddScoped<ISiegeLobbyService, SiegeLobbyService>();
            services.AddScoped<ISiegeConfigurationRepository, SiegeConfigurationRepository>();
            services.AddScoped<ISiegeConfigurationService, SiegeConfigurationService>();
            // Siege weapons phase 1 (PandiO/knk-workspace#782): templates, ammo types, weapon spots, unlocks
            services.AddScoped<ISiegeWeaponRepository, SiegeWeaponRepository>();
            services.AddScoped<ISiegeWeaponService, SiegeWeaponService>();
            // Siege shop catalogue (PandiO/knk-workspace#867).
            services.AddScoped<ISiegeShopRepository, SiegeShopRepository>();
            services.AddScoped<ISiegeShopService, SiegeShopService>();
            // Siege Phase 6 — match lifecycle + server-side rewards
            services.AddScoped<ISiegeMatchRepository, SiegeMatchRepository>();
            services.AddScoped<ISiegeMatchService, SiegeMatchService>();
            services.AddScoped<ISiegeMatchHistoryService, SiegeMatchHistoryService>(); // #174 public results log
            services.AddScoped<ISiegeMatchGateService, SiegeMatchGateService>();
            // Road navigation (docs/specs/navigation/IMPLEMENTATION_PLAN.md)
            services.AddScoped<IRoadNetworkRepository, RoadNetworkRepository>();
            services.AddScoped<IRoadNetworkService, RoadNetworkService>();
            services.AddScoped<IDomainNavigationSettingsService, DomainNavigationSettingsService>();
            services.AddScoped<INavigationCompassSettingsService, NavigationCompassSettingsService>();
            // Workflow + WorldTasks
            services.AddScoped<IWorkflowRepository, WorkflowRepository>();
            services.AddScoped<IWorkflowService, WorkflowService>();
            services.AddScoped<IWorldTaskRepository, WorldTaskRepository>();
            services.AddScoped<IWorldTaskService, WorldTaskService>();
            // PandiO/knk-workspace#269: temp regions of abandoned drafts and closed tasks, released for the game server.
            services.AddScoped<ITempRegionReleaseService, TempRegionReleaseService>();
            // PandiO/knk-workspace#180: lifetimes (section "WorldTasks", optional) and the 30 s sweep.
            services.AddOptions<knkwebapi_v2.Configuration.WorldTaskOptions>();
            if (configuration != null)
            {
                services.Configure<knkwebapi_v2.Configuration.WorldTaskOptions>(
                    configuration.GetSection(knkwebapi_v2.Configuration.WorldTaskOptions.SectionName));
            }
            services.AddScoped<WorldTaskSweeper>();
            services.AddHostedService<WorldTaskSweepService>();
            
            // DisplayConfiguration repositories
            services.AddScoped<IDisplayConfigurationRepository, DisplayConfigurationRepository>();
            services.AddScoped<IDisplaySectionRepository, DisplaySectionRepository>();
            services.AddScoped<IDisplayFieldRepository, DisplayFieldRepository>();
            
            // DisplayConfiguration services
            services.AddScoped<IDisplayConfigurationService, DisplayConfigurationService>();
            services.AddScoped<IDisplayConfigurationDraftService, DisplayConfigurationDraftService>(); // #71
            services.AddScoped<IDisplaySectionService, DisplaySectionService>();
            services.AddScoped<IDisplayFieldService, DisplayFieldService>();
            // knk-displayconfig/1: bundled DisplayConfiguration seed files, imported on request (no startup seeder).
            services.AddSingleton(sp => new DisplayConfigurationSeedOptions
            {
                Directory = Path.Combine(sp.GetRequiredService<IHostEnvironment>().ContentRootPath, "Data", "DisplayConfigurationSeeds")
            });
            services.AddScoped<IDisplayConfigurationPortabilityService, DisplayConfigurationPortabilityService>();

            // InventoryMenu (docs/specs/inventory-menu/IMPLEMENTATION_PLAN.md Phase 1)
            services.AddScoped<IMenuTemplateRepository, MenuTemplateRepository>();
            services.AddScoped<IMenuTemplateService, MenuTemplateService>();

            // Lootboxes (docs/specs/lootboxes/IMPLEMENTATION_PLAN.md Phase 1). The repositories/services follow the
            // I<Name> convention scan below; the roll engine and its randomness are registered here.
            services.AddSingleton<knkwebapi_v2.Services.Lootbox.ILootRandom, knkwebapi_v2.Services.Lootbox.CryptoLootRandom>();
            services.AddSingleton<knkwebapi_v2.Services.Lootbox.LootboxRollEngine>();
            // The runtime's clock (Phase 2): the UTC-day claim cap and expiry read it, so tests can pin the time.
            services.AddSingleton(TimeProvider.System);

            // Private messages Phase 2 — ignore list (docs/specs/private-messages/IMPLEMENTATION_PLAN.md §2)
            services.AddScoped<IUserIgnoreRepository, UserIgnoreRepository>();
            services.AddScoped<IUserIgnoreService, UserIgnoreService>();

            // Private messages Phase 3 — server-side PM log (docs/specs/private-messages/IMPLEMENTATION_PLAN.md §3)
            services.AddScoped<IPrivateMessageLogRepository, PrivateMessageLogRepository>();
            services.AddScoped<IPrivateMessageLogService, PrivateMessageLogService>();

            // Add MetadataService for dynamic form building
            services.AddSingleton<IMetadataService, MetadataService>();

            // Entity type configuration services
            services.AddScoped<IEntityTypeConfigurationRepository, EntityTypeConfigurationRepository>();
            services.AddScoped<IEntityTypeConfigurationService, EntityTypeConfigurationService>();

            // Global game settings services
            services.AddScoped<IGameSettingsRepository, GameSettingsRepository>();
            services.AddScoped<IGameSettingsService, GameSettingsService>();
            services.AddScoped<IHubSettingsService, HubSettingsService>();

            // Add FormTemplate services for reusable step/field management
            services.AddScoped<IFormTemplateValidationService, FormTemplateValidationService>();
            services.AddScoped<IDisplayConditionEvaluator, DisplayConditionEvaluator>();

            // Register HttpClient for HTTP calls to external services
            services.AddHttpClient();

            // Region management service - requires configuration from appsettings
            string? minecraftPluginBaseUrl = configuration?.GetSection("MinecraftPlugin:BaseUrl").Value ?? "http://localhost:8081";
            // Closed-alpha WP7.5: calls to the plugin's region HTTP server carry the shared key.
            services.AddScoped<IRegionService>(sp =>
                new RegionService(sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<ILogger<RegionService>>(), minecraftPluginBaseUrl,
                    configuration?[knkwebapi_v2.Attributes.PluginServiceAuth.ApiKeyConfigKey])
            );
            // P2 (#91): the territory lookup behind the InsideTerritory rule, on the same plugin HTTP server and key.
            services.AddScoped<knkwebapi_v2.Services.Production.ITerritoryLookupClient>(sp =>
                new knkwebapi_v2.Services.Production.TerritoryLookupClient(sp.GetRequiredService<IHttpClientFactory>(),
                    sp.GetRequiredService<ILogger<knkwebapi_v2.Services.Production.TerritoryLookupClient>>(), minecraftPluginBaseUrl,
                    configuration?[knkwebapi_v2.Attributes.PluginServiceAuth.ApiKeyConfigKey]));
            services.AddScoped<IDomainRegionNameFinalizer, DomainRegionNameFinalizer>();
            // KNG-111: each domain's Minecraft world (resolution, validation, backfill of older rows).
            services.AddScoped<IDomainWorldResolver, DomainWorldResolver>();
            services.AddScoped<IDomainWorldBackfill, DomainWorldBackfill>();

            // Currency ledger (docs/specs/currency-payments/IMPLEMENTATION_PLAN.md Phases 1-2)
            services.AddScoped<ICurrencyRepository, CurrencyRepository>();
            // One scoped CurrencyService per request behind both interfaces (Phase 3 transfers).
            services.AddScoped<CurrencyService>();
            services.AddScoped<ICurrencyService>(sp => sp.GetRequiredService<CurrencyService>());
            services.AddScoped<ICurrencyTransferService>(sp => sp.GetRequiredService<CurrencyService>());
            services.AddScoped<ITitleProgressionService, TitleProgressionService>();
            services.AddScoped<ICurrencyAdminService, CurrencyAdminService>();
            services.AddScoped<CurrencyReconciler>();
            // Currency monitor (Phase 5): alerts R1–R9, reconciliation, metrics.
            if (configuration != null)
            {
                services.Configure<knkwebapi_v2.Configuration.CurrencyMonitorOptions>(
                    configuration.GetSection(knkwebapi_v2.Configuration.CurrencyMonitorOptions.SectionName));
            }
            services.AddSingleton<CurrencyMetrics>();
            services.AddSingleton<CurrencyMonitorSignals>();
            services.AddSingleton<CurrencyReconciliationState>();
            services.AddScoped<CurrencyAnomalyDetector>();
            services.AddScoped<ICurrencyAlertService, CurrencyAlertService>();
            services.AddHostedService<CurrencyMonitorService>();

            // Player statistics (KNG-34, knk-workspace docs/specs/player-statistics/IMPLEMENTATION_PLAN.md §4).
            if (configuration != null)
            {
                services.Configure<knkwebapi_v2.Configuration.StatisticsOptions>(
                    configuration.GetSection(knkwebapi_v2.Configuration.StatisticsOptions.SectionName));
            }
            services.AddSingleton<knkwebapi_v2.Services.Statistics.StatisticsMetrics>();
            services.AddScoped<IStatisticsRepository, StatisticsRepository>();
            services.AddScoped<knkwebapi_v2.Services.Statistics.LedgerStatisticsProjector>();
            services.AddScoped<knkwebapi_v2.Services.Statistics.SiegeStatisticsProjector>();
            services.AddScoped<knkwebapi_v2.Services.Statistics.IStatisticsViewerResolver, knkwebapi_v2.Services.Statistics.StatisticsViewerResolver>();
            services.AddScoped<IStatisticsIngestionService, knkwebapi_v2.Services.Statistics.StatisticsIngestionService>();
            services.AddScoped<IStatisticsQueryService, knkwebapi_v2.Services.Statistics.StatisticsQueryService>();
            services.AddScoped<IStatisticsVisibilityService, knkwebapi_v2.Services.Statistics.StatisticsVisibilityService>();
            services.AddScoped<IStatisticsRebuildService, knkwebapi_v2.Services.Statistics.StatisticsRebuildService>();
            services.AddHostedService<knkwebapi_v2.Services.Statistics.StatisticsProjectionService>();
            services.AddHostedService<knkwebapi_v2.Services.Statistics.StatisticsRetentionService>();

            // Leaderboards (KNG-34 link 5): snapshots every Leaderboards:RefreshSeconds; reads from snapshots only.
            if (configuration != null)
            {
                services.Configure<knkwebapi_v2.Configuration.LeaderboardsOptions>(
                    configuration.GetSection(knkwebapi_v2.Configuration.LeaderboardsOptions.SectionName));
            }
            services.AddScoped<ILeaderboardRepository, LeaderboardRepository>();
            services.AddScoped<knkwebapi_v2.Services.Leaderboards.LeaderboardSnapshotBuilder>();
            services.AddScoped<ILeaderboardQueryService, knkwebapi_v2.Services.Leaderboards.LeaderboardQueryService>();
            services.AddHostedService<knkwebapi_v2.Services.Leaderboards.LeaderboardSnapshotService>();

            // Diagnostic telemetry + GDPR deletion (KNG-34 link 6, IMPLEMENTATION_PLAN.md §4): bounded write
            // queue drained by one background writer; owner-only reads; daily retention and deadline jobs.
            if (configuration != null)
            {
                services.Configure<knkwebapi_v2.Configuration.DiagnosticTelemetryOptions>(
                    configuration.GetSection(knkwebapi_v2.Configuration.DiagnosticTelemetryOptions.SectionName));
                services.Configure<knkwebapi_v2.Configuration.PrivacyOptions>(
                    configuration.GetSection(knkwebapi_v2.Configuration.PrivacyOptions.SectionName));
            }
            services.AddSingleton<knkwebapi_v2.Services.Telemetry.TelemetryMetrics>();
            services.AddSingleton(sp => new knkwebapi_v2.Services.Telemetry.TelemetryWriteQueue(
                sp.GetService<Microsoft.Extensions.Options.IOptions<knkwebapi_v2.Configuration.DiagnosticTelemetryOptions>>(),
                sp.GetService<knkwebapi_v2.Services.Telemetry.TelemetryMetrics>()));
            services.AddScoped<ITelemetryRepository, TelemetryRepository>();
            services.AddScoped<knkwebapi_v2.Services.Telemetry.ITelemetryIngestionService, knkwebapi_v2.Services.Telemetry.TelemetryIngestionService>();
            services.AddScoped<knkwebapi_v2.Services.Telemetry.ITelemetryQueryService, knkwebapi_v2.Services.Telemetry.TelemetryQueryService>();
            services.AddHostedService<knkwebapi_v2.Services.Telemetry.TelemetryWriterService>();
            services.AddHostedService<knkwebapi_v2.Services.Telemetry.TelemetryRetentionService>();
            services.AddScoped<IPrivacyRepository, PrivacyRepository>();
            services.AddScoped<knkwebapi_v2.Services.Privacy.IPrivacyDeletionService, knkwebapi_v2.Services.Privacy.PrivacyDeletionService>();
            services.AddScoped<knkwebapi_v2.Services.Privacy.IPrivacyEmailService, knkwebapi_v2.Services.Privacy.PrivacyEmailService>();
            services.AddHostedService<knkwebapi_v2.Services.Privacy.PrivacyDeletionDueService>();

            // World analytics (KNG-34 link 7, IMPLEMENTATION_PLAN.md §4): anonymous daily aggregates from the
            // plugin; owner-only reads; daily retention.
            if (configuration != null)
            {
                services.Configure<knkwebapi_v2.Configuration.WorldAnalyticsOptions>(
                    configuration.GetSection(knkwebapi_v2.Configuration.WorldAnalyticsOptions.SectionName));
            }
            services.AddScoped<IWorldAnalyticsRepository, WorldAnalyticsRepository>();
            services.AddScoped<knkwebapi_v2.Services.WorldAnalytics.IWorldAnalyticsIngestionService, knkwebapi_v2.Services.WorldAnalytics.WorldAnalyticsIngestionService>();
            services.AddScoped<knkwebapi_v2.Services.WorldAnalytics.IWorldAnalyticsQueryService, knkwebapi_v2.Services.WorldAnalytics.WorldAnalyticsQueryService>();
            services.AddHostedService<knkwebapi_v2.Services.WorldAnalytics.WorldAnalyticsRetentionService>();

            // Location retention (KNG-80): orphan check, review items, weekly scheduler.
            if (configuration != null)
            {
                services.Configure<knkwebapi_v2.Configuration.LocationRetentionOptions>(
                    configuration.GetSection(knkwebapi_v2.Configuration.LocationRetentionOptions.SectionName));
            }
            services.AddSingleton<knkwebapi_v2.Services.LocationRetention.LocationRetentionRunGate>();
            services.AddScoped<knkwebapi_v2.Services.LocationRetention.ILocationReferenceSource, knkwebapi_v2.Services.LocationRetention.GameSettingsLocationReferenceSource>();
            services.AddScoped<knkwebapi_v2.Services.LocationRetention.ILocationReferenceSource, knkwebapi_v2.Services.LocationRetention.FormDraftLocationReferenceSource>();
            services.AddScoped<ILocationRetentionService, knkwebapi_v2.Services.LocationRetention.LocationRetentionService>();
            services.AddHostedService<knkwebapi_v2.Services.LocationRetention.LocationRetentionScheduler>();

            // Retention policy service - background task for cleaning up old records
            services.AddHostedService<RetentionPolicyService>();
            // Temporary ranks expiring: back to Default + tell the plugin (RANK_DISPLAY.md).
            services.AddHostedService<RankExpirySweepService>();

            // Validation method implementations (Phase 3)
            services.AddScoped<IValidationMethod, LocationInsideRegionValidator>();
            services.AddScoped<IValidationMethod, RegionContainmentValidator>();
            // P2 (PandiO/knk-workspace#91): placement rules P-2/P-3, a territory lookup on the game server.
            services.AddScoped<IValidationMethod, InsideTerritoryValidator>();
            services.AddScoped<IValidationMethod, knkwebapi_v2.Services.ValidationMethods.ProductionAreaPlacementValidator>(); // P-3 (#883)
            services.AddScoped<IValidationMethod, ConditionalRequiredValidator>();
            services.AddScoped<IValidationMethod, ConditionalValueMatchValidator>();

            // convention-based registrations for other services/repositories in the same assembly
            var asm = Assembly.GetExecutingAssembly();
            var candidates = asm.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && (t.Name.EndsWith("Repository") || t.Name.EndsWith("Service")));

            foreach (var impl in candidates)
            {
                var iface = impl.GetInterfaces().FirstOrDefault(i => i.Name == "I" + impl.Name);
                if (iface != null)
                {
                    // avoid double-registering explicit ones
                    if (!services.Any(sd => sd.ServiceType == iface)) 
                    {
                        services.AddScoped(iface, impl);
                    }
                }
            }

            // AutoMapper profiles in this assembly
            services.AddAutoMapper(Assembly.GetExecutingAssembly());

            return services;
        }
    }
}
