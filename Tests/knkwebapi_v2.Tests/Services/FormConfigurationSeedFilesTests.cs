using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// KNG-138/KNG-139: every bundled seed in the repo's Data/FormConfigurationSeeds/ must pass the
/// import dry run on an empty database, and import cleanly - seeds that refer to other seeds
/// (join entries, child forms, parent-form rule dependencies) in dependency order.
/// </summary>
public class FormConfigurationSeedFilesTests
{
    private static string SeedDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "knkwebapi_v2.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Data", "FormConfigurationSeeds");
    }

    private static FormConfigurationPortabilityService NewService(out ServiceProvider provider)
    {
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<KnKDbContext>(o => o.UseInMemoryDatabase(name));
        provider = services.BuildServiceProvider();
        var context = provider.GetRequiredService<KnKDbContext>();
        return new FormConfigurationPortabilityService(
            context,
            new FormConfigurationRepository(context),
            new FormTemplateValidationService(),
            new MetadataService(provider.GetRequiredService<IServiceScopeFactory>()),
            new FormConfigurationSeedOptions { Directory = SeedDirectory() });
    }

    [Fact]
    public void SeedFolder_Exists()
    {
        Assert.True(Directory.Exists(SeedDirectory()));
    }

    [Fact]
    public async Task EverySeed_IsReadable_AndHasAUniqueConfigurationGuid()
    {
        var service = NewService(out var provider);
        using (provider)
        {
            var seeds = await service.ListSeedsAsync();
            Assert.All(seeds, s => Assert.True(s.Error == null, $"{s.Name}: {s.Error}"));
            Assert.Equal(seeds.Count, seeds.Select(s => s.ConfigurationGuid).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }

    /// <summary>
    /// Imports every seed, whatever resolves first (join entries/children before parents), repeating while
    /// something new got in. Returns the import results by seed name and, per seed that never got in, its
    /// last dry-run errors.
    /// </summary>
    private static async Task<(Dictionary<string, FormConfigurationImportResultDto> Imported, Dictionary<string, string> NotImported)>
        ImportAllAsync(FormConfigurationPortabilityService service)
    {
        var remaining = (await service.ListSeedsAsync()).Select(s => s.Name).ToList();
        var imported = new Dictionary<string, FormConfigurationImportResultDto>();
        var lastErrors = new Dictionary<string, string>();

        bool progress = true;
        while (remaining.Count > 0 && progress)
        {
            progress = false;
            foreach (var name in remaining.ToList())
            {
                var dry = (await service.ImportSeedAsync(name, dryRun: true, makeDefault: false))!;
                if (dry.Status != FormConfigurationImportStatus.DryRunPassed)
                {
                    lastErrors[name] = string.Join(" | ", dry.Errors);
                    continue;
                }

                var result = (await service.ImportSeedAsync(name, dryRun: false, makeDefault: false))!;
                Assert.True(result.Status == FormConfigurationImportStatus.Imported, $"{name}: dry run passed but import said {result.Status}: {string.Join(" | ", result.Errors)}");
                imported[name] = result;
                remaining.Remove(name);
                progress = true;
            }
        }

        return (imported, remaining.ToDictionary(n => n, n => lastErrors.GetValueOrDefault(n) ?? ""));
    }

    [Fact]
    public async Task EverySeed_PassesTheDryRun_AndImports_InDependencyOrder()
    {
        var service = NewService(out var provider);
        using (provider)
        {
            var (_, notImported) = await ImportAllAsync(service);
            Assert.True(notImported.Count == 0,
                "Seeds that never passed the dry run: " + string.Join("; ", notImported.Select(kv => $"{kv.Key}: {kv.Value}")));
        }
    }

    [Fact]
    public async Task EverySeed_ImportsWithoutWarnings()
    {
        var service = NewService(out var provider);
        using (provider)
        {
            var (imported, _) = await ImportAllAsync(service);
            Assert.All(imported, kv => Assert.True(kv.Value.Warnings.Count == 0, $"{kv.Key}: {string.Join(" | ", kv.Value.Warnings)}"));
        }
    }

    /// <summary>
    /// Properties that are non-nullable without a default on the entity but are deliberately not on its seed
    /// form, with the reason. Everything else of that kind must be on the form (KNG-139 acceptance: every
    /// required non-nullable property is on the form), or creating the entity from the form would fail.
    /// Value-type columns with a usable default (numbers, booleans, enums) don't count as required.
    /// </summary>
    private static readonly Dictionary<string, Dictionary<string, string>> NotOnFormByDesign = new()
    {
        ["SiegeScenarioDistrict"] = new() { ["SiegeScenarioId"] = "set by the scenario's Districts step (join entry)" },
        ["SiegeScenarioGate"] = new() { ["SiegeScenarioId"] = "set by the scenario's Gates step (join entry)" },
        ["SiegeLobbyScenario"] = new() { ["SiegeLobbyId"] = "set by the lobby's Rotation step (join entry)" },
        // GateDoorMappingProfile maps a missing value to "" for this one; a form field would have to be required
        // (FormTemplateValidationService doesn't see the "= string.Empty" initialiser), and empty is the normal value.
        // ScanMaterialWhitelist/ScanMaterialBlacklist carry [DefaultValue("")] since KNG-193, so the whitelist is an
        // optional field of the seed (v2) and the blacklist simply has a default.
        ["GateDoor"] = new()
        {
            ["PassThroughConditionsJson"] = "not read by the plugin",
        },
    };

    [Fact]
    public async Task EverySeed_HasEveryRequiredNonNullableProperty_OnTheForm()
    {
        var service = NewService(out var provider);
        using (provider)
        {
            var metadata = new MetadataService(provider.GetRequiredService<IServiceScopeFactory>());
            var seeds = await service.ListSeedsAsync();
            var problems = new List<string>();
            foreach (var seed in seeds)
            {
                var json = await File.ReadAllTextAsync(Path.Combine(SeedDirectory(), seed.Name + ".json"));
                var config = System.Text.Json.JsonSerializer.Deserialize<PortableFormConfigurationDto>(json,
                    new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })!;
                var onForm = config.Steps.SelectMany(st => st.Fields).Select(f => f.FieldName!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var entity = metadata.GetEntityMetadata(config.EntityTypeName!);
                Assert.True(entity != null, $"{seed.Name}: no metadata for {config.EntityTypeName}");
                var allowed = NotOnFormByDesign.GetValueOrDefault(config.EntityTypeName!) ?? new();

                foreach (var field in entity!.Fields)
                {
                    // Primary keys, navigation objects and collections aren't form values; their *Id field is.
                    if (field.FieldName.Equals("Id", StringComparison.OrdinalIgnoreCase)) continue;
                    if (field.IsRelatedEntity && !field.FieldName.EndsWith("Id", StringComparison.Ordinal)) continue;
                    if (field.FieldType.Contains("List", StringComparison.OrdinalIgnoreCase)
                        || field.FieldType.Contains("Collection", StringComparison.OrdinalIgnoreCase)) continue;
                    if (field.IsNullable) continue;
                    // The metadata counts every value type as "has a default" (0), but a foreign key of 0 can't be
                    // saved, so a non-nullable *Id of a related entity always needs the form.
                    var isForeignKey = field.IsRelatedEntity && field.FieldName.EndsWith("Id", StringComparison.Ordinal);
                    if (field.HasDefaultValue && !isForeignKey) continue;
                    if (onForm.Contains(field.FieldName) || allowed.ContainsKey(field.FieldName)) continue;
                    problems.Add($"{seed.Name}: '{field.FieldName}' ({field.FieldType}) must be set when the entity is created but is not on the form");
                }
            }

            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        }
    }

    [Fact]
    public async Task SeedReferences_ResolveToTheImportedConfigurations()
    {
        var service = NewService(out var provider);
        using (provider)
        {
            var (imported, _) = await ImportAllAsync(service);
            var db = provider.GetRequiredService<KnKDbContext>();
            int IdOf(string seed) => int.Parse(imported[seed].FormConfigurationId!);
            async Task<FormStep> StepAsync(string seed, string stepName)
            {
                var configId = IdOf(seed);
                return await db.FormSteps.SingleAsync(s => s.FormConfigurationId == configId && s.StepName == stepName);
            }
            async Task<int> FieldIdAsync(string seed, string fieldName)
            {
                var configId = IdOf(seed);
                return (await db.FormFields.Include(f => f.FormStep)
                    .SingleAsync(f => f.FormStep!.FormConfigurationId == configId && f.FieldName == fieldName)).Id;
            }

            // Many-to-many steps point at the join entries imported from their own seeds.
            Assert.Equal(IdOf("SiegeScenarioDistrict.join"), (await StepAsync("SiegeScenario", "Districts")).SubConfigurationId);
            Assert.Equal(IdOf("SiegeScenarioGate.join"), (await StepAsync("SiegeScenario", "Gates")).SubConfigurationId);
            Assert.Equal(IdOf("SiegeLobbyScenario.join"), (await StepAsync("SiegeLobby", "Rotation")).SubConfigurationId);

            // Parent-form rules depend on the scenario seed's Town field; same-form rules on their own form's field.
            var scenarioTown = await FieldIdAsync("SiegeScenario", "TownId");
            foreach (var (seed, field, dependsOn) in new[]
            {
                ("SiegeScenario", "HubLocationId", scenarioTown),
                ("SiegeSpawnpoint", "LocationId", scenarioTown),
                ("SiegeObjective", "LocationId", scenarioTown),
                ("GateDoor", "AnchorPointId", await FieldIdAsync("GateDoor", "GateStructureId")),
                ("GateStructure", "WgRegionId", await FieldIdAsync("GateStructure", "DistrictId")),
            })
            {
                var fieldId = await FieldIdAsync(seed, field);
                var rule = await db.FieldValidationRules.SingleAsync(r => r.FormFieldId == fieldId);
                Assert.Equal(dependsOn, rule.DependsOnFieldId);
            }

            // Every seed imported as the entity's default (empty database), so the wizard finds it.
            Assert.All(imported, kv => Assert.True(kv.Value.IsDefault, $"{kv.Key} was not made default on an empty database"));
        }
    }
}
