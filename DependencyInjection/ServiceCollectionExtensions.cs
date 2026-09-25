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
            services.AddScoped<IPasswordService, PasswordService>();
            services.AddScoped<ITokenService, TokenService>();
            services.AddScoped<IAuthService, AuthService>();

            var emailSection = configuration?.GetSection("Email");
            if (emailSection != null)
            {
                services.Configure<knkwebapi_v2.Configuration.EmailSettings>(emailSection);
            }
            var emailProvider = emailSection?["Provider"];
            if (string.Equals(emailProvider, "Smtp", System.StringComparison.OrdinalIgnoreCase))
            {
                services.AddScoped<IPasswordResetDeliveryService, SmtpPasswordResetDeliveryService>();
            }
            else
            {
                services.AddScoped<IPasswordResetDeliveryService, PasswordResetDeliveryService>();
            }
            services.AddScoped<ILinkCodeRepository, LinkCodeRepository>();
            services.AddScoped<ILinkCodeService, LinkCodeService>();
            services.AddScoped<ILocationRepository, LocationRepository>();
            services.AddScoped<ILocationService, LocationService>();
            services.AddScoped<IDomainRepository, DomainRepository>();
            services.AddScoped<IDomainService, DomainService>();
            services.AddScoped<IMinecraftMaterialRefRepository, MinecraftMaterialRefRepository>();
            services.AddScoped<IMinecraftMaterialRefService, MinecraftMaterialRefService>();
            services.AddScoped<IMinecraftBlockRefRepository, MinecraftBlockRefRepository>();
            services.AddScoped<IMinecraftBlockRefService, MinecraftBlockRefService>();
            services.AddScoped<IMinecraftEnchantmentRefRepository, MinecraftEnchantmentRefRepository>();
            services.AddScoped<IMinecraftEnchantmentRefService, MinecraftEnchantmentRefService>();
            services.AddSingleton<IMinecraftMaterialCatalogService, MinecraftMaterialCatalogService>();
            services.AddSingleton<IMinecraftEnchantmentCatalogService, MinecraftEnchantmentCatalogService>();
            services.AddScoped<IFormConfigurationRepository, FormConfigurationRepository>();
            services.AddScoped<IFormConfigurationService, FormConfigurationService>();
            services.AddScoped<IFormStepRepository, FormStepRepository>();
            services.AddScoped<IFormStepService, FormStepService>();
            services.AddScoped<IFormFieldRepository, FormFieldRepository>();
            services.AddScoped<IFormFieldService, FormFieldService>();
            services.AddScoped<IFieldValidationRuleRepository, FieldValidationRuleRepository>();
            services.AddScoped<IValidationService, ValidationService>();
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
            services.AddScoped<IGateStructureRepository, GateStructureRepository>();
            services.AddScoped<IGateStructureService, GateStructureService>();
            services.AddScoped<IGateDoorRepository, GateDoorRepository>();
            services.AddScoped<IGateDoorService, GateDoorService>();
            services.AddScoped<IItemBlueprintRepository, ItemBlueprintRepository>();
            services.AddScoped<IItemBlueprintService, ItemBlueprintService>();
            services.AddScoped<IEnchantmentDefinitionRepository, EnchantmentDefinitionRepository>();
            services.AddScoped<IEnchantmentDefinitionService, EnchantmentDefinitionService>();
            services.AddScoped<IGradeRepository, GradeRepository>();
            services.AddScoped<IGradeService, GradeService>();
            services.AddScoped<ITagRepository, TagRepository>();
            services.AddScoped<ITagService, TagService>();
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
            // Workflow + WorldTasks
            services.AddScoped<IWorkflowRepository, WorkflowRepository>();
            services.AddScoped<IWorkflowService, WorkflowService>();
            services.AddScoped<IWorldTaskRepository, WorldTaskRepository>();
            services.AddScoped<IWorldTaskService, WorldTaskService>();
            
            // DisplayConfiguration repositories
            services.AddScoped<IDisplayConfigurationRepository, DisplayConfigurationRepository>();
            services.AddScoped<IDisplaySectionRepository, DisplaySectionRepository>();
            services.AddScoped<IDisplayFieldRepository, DisplayFieldRepository>();
            
            // DisplayConfiguration services
            services.AddScoped<IDisplayConfigurationService, DisplayConfigurationService>();
            services.AddScoped<IDisplaySectionService, DisplaySectionService>();
            services.AddScoped<IDisplayFieldService, DisplayFieldService>();

            // InventoryMenu (docs/specs/inventory-menu/IMPLEMENTATION_PLAN.md Phase 1)
            services.AddScoped<IMenuTemplateRepository, MenuTemplateRepository>();
            services.AddScoped<IMenuTemplateService, MenuTemplateService>();

            // Add MetadataService for dynamic form building
            services.AddSingleton<IMetadataService, MetadataService>();

            // Entity type configuration services
            services.AddScoped<IEntityTypeConfigurationRepository, EntityTypeConfigurationRepository>();
            services.AddScoped<IEntityTypeConfigurationService, EntityTypeConfigurationService>();

            // Global game settings services
            services.AddScoped<IGameSettingsRepository, GameSettingsRepository>();
            services.AddScoped<IGameSettingsService, GameSettingsService>();

            // Add FormTemplate services for reusable step/field management
            services.AddScoped<IFormTemplateValidationService, FormTemplateValidationService>();
            services.AddScoped<IDisplayConditionEvaluator, DisplayConditionEvaluator>();
            services.AddScoped<IFormTemplateReusableService, FormTemplateReusableService>();

            // Register HttpClient for HTTP calls to external services
            services.AddHttpClient();

            // Region management service - requires configuration from appsettings
            string? minecraftPluginBaseUrl = configuration?.GetSection("MinecraftPlugin:BaseUrl").Value ?? "http://localhost:8081";
            services.AddScoped<IRegionService>(sp => 
                new RegionService(sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<ILogger<RegionService>>(), minecraftPluginBaseUrl)
            );

            // Retention policy service - background task for cleaning up old records
            services.AddHostedService<RetentionPolicyService>();

            // Validation method implementations (Phase 3)
            services.AddScoped<IValidationMethod, LocationInsideRegionValidator>();
            services.AddScoped<IValidationMethod, RegionContainmentValidator>();
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
