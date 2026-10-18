using System.Text.Json;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// KNG-138: export/import of FormConfigurations as "knk-formconfig/1" files. The round trip uses a
/// configuration set shaped like knk-workspace docs/specs/siege-minigame/PHASE_3_FORMCONFIGS.md
/// (a join entry, a parent with a many-to-many step on it, an owned child with a step display
/// condition, world tasks, enum subsets and validation rules incl. one on a parent form's field),
/// authored through the same create path as POST api/FormConfigurations.
/// </summary>
public class FormConfigurationPortabilityServiceTests : IDisposable
{
    private static readonly IMapper Mapper = new MapperConfiguration(cfg => cfg.AddProfile<FormMappingProfile>()).CreateMapper();

    private const string JoinGuid = "6f1c2f0e-1d7a-4c55-9a7e-0b8f1d2c3a01";
    private const string ScenarioGuid = "6f1c2f0e-1d7a-4c55-9a7e-0b8f1d2c3a02";
    private const string ObjectiveGuid = "6f1c2f0e-1d7a-4c55-9a7e-0b8f1d2c3a03";
    private const string TownFieldGuid = "a1000000-0000-4000-8000-000000000001";
    private const string HubFieldGuid = "a1000000-0000-4000-8000-000000000002";
    private const string GateFieldGuid = "1518749a-dbeb-4115-9142-fac723833696";
    private const string ObjectiveLocationFieldGuid = "a1000000-0000-4000-8000-000000000003";

    private readonly List<ServiceProvider> _providers = new();
    private readonly string _seedDir = Path.Combine(Path.GetTempPath(), "knk-seeds-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        foreach (var provider in _providers) provider.Dispose();
        if (Directory.Exists(_seedDir)) Directory.Delete(_seedDir, true);
    }

    // ------------------------------------------------------------------ setup

    private sealed record Db(KnKDbContext Context, FormConfigurationPortabilityService Portability, FormConfigurationService Configs);

    private Db NewDb(string? seedDir = null)
    {
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<KnKDbContext>(o => o.UseInMemoryDatabase(name));
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);

        var context = provider.GetRequiredService<KnKDbContext>();
        var repo = new FormConfigurationRepository(context);
        var metadata = new MetadataService(provider.GetRequiredService<IServiceScopeFactory>());
        var validation = new FormTemplateValidationService();
        return new Db(
            context,
            new FormConfigurationPortabilityService(context, repo, validation, metadata, new FormConfigurationSeedOptions { Directory = seedDir ?? _seedDir }),
            new FormConfigurationService(repo, Mapper, validation, metadata));
    }

    private static FormFieldDto Field(string name, string label, FieldType type, int order, bool required = false,
        string? objectType = null, string? settings = null, string? guid = null, string? description = null, string? defaultValue = null) => new()
    {
        FieldName = name,
        Label = label,
        FieldType = type,
        Order = order,
        IsRequired = required,
        ObjectType = objectType,
        SettingsJson = settings,
        FieldGuid = guid,
        Description = description,
        DefaultValue = defaultValue
    };

    private static FormStepDto Step(string name, int order, params FormFieldDto[] fields) => new()
    {
        StepName = name,
        Order = order,
        Fields = fields.ToList()
    };

    private static DisplayConditionGroupDto NotEmpty(string sourceGuid, DisplayConditionTargetType target, ConditionOperator op = ConditionOperator.IsNotEmpty) => new()
    {
        TargetType = target,
        InnerLogic = DisplayConditionLogic.And,
        CombineWithPreviousLogic = DisplayConditionLogic.And,
        IsActive = true,
        Conditions = { new DisplayConditionDto { SourceFieldGuid = sourceGuid, Operator = op, ValueJson = "null" } }
    };

    /// <summary>The PHASE_3 shape, authored as the API's POST does, plus two rules. Returns (join, scenario, objective) ids.</summary>
    private static async Task<(int Join, int Scenario, int Objective)> AuthorSiegeSetAsync(Db db)
    {
        var join = await db.Configs.CreateAsync(new FormConfigurationDto
        {
            EntityTypeName = "SiegeScenarioDistrict",
            ConfigurationName = "SiegeScenarioDistrict - Join Entry",
            Description = "Join entry for adding a district to a siege scenario.",
            IsDefault = true,
            Steps =
            {
                Step("District", 0,
                    Field("DistrictId", "District", FieldType.Object, 0, true, "District",
                        "{\"pickerFilters\": {\"townId\": \"{parent.TownId?}\"}}", description: "Lists the districts of the scenario's town."))
            }
        });
        await SetGuidAsync(db, join.Id!, JoinGuid);

        var scenarioDto = new FormConfigurationDto
        {
            EntityTypeName = "SiegeScenario",
            ConfigurationName = "SiegeScenario - Default",
            Description = "A siege map/mode.",
            IsDefault = true,
            Steps =
            {
                Step("General", 0,
                    Field("Name", "Name", FieldType.String, 0, true),
                    Field("Description", "Description", FieldType.String, 1),
                    Field("TownId", "Town", FieldType.Object, 2, true, "Town", guid: TownFieldGuid, description: "The town being besieged.")),
                new FormStepDto
                {
                    StepName = "Districts",
                    Order = 1,
                    Description = "The districts the siege takes place in.",
                    IsManyToManyRelationship = true,
                    RelatedEntityPropertyName = "Districts",
                    JoinEntityType = "SiegeScenarioDistrict",
                    SubConfigurationId = join.Id,
                    Fields = { Field("Districts", "Districts", FieldType.List, 0, false, "SiegeScenarioDistrict") }
                },
                Step("Hub & entry", 2,
                    Field("HubLocationId", "Hub location", FieldType.Object, 0, true, "Location",
                        "{\"worldTask\": {\"enabled\": true, \"taskType\": \"LocationSelection\"}}", HubFieldGuid),
                    Field("PlayersMin", "Minimum players", FieldType.Integer, 1, true, defaultValue: "2"),
                    Field("LockdownScenarioArea", "Lock down the scenario area", FieldType.Boolean, 2, defaultValue: "true",
                        description: "Non-members can't enter the scenario's districts during a match.")),
                Step("Teams", 3,
                    Field("Teams", "Teams", FieldType.List, 0, false, "SiegeTeam", "{\"ownedChildCollection\": true}")),
                Step("Readiness", 4,
                    Field("Id", "Readiness", FieldType.Integer, 0, false, settings: "{\"displayPanel\": \"siegeScenarioReadiness\"}"))
            }
        };
        scenarioDto.Steps[1].Fields[0].MinSelection = 1;
        scenarioDto.Steps[1].Fields[0].CanCreate = true;
        scenarioDto.Steps[2].Fields[1].IncrementValue = 1;
        scenarioDto.Steps[2].Fields[1].Validations.Add(new FieldValidationDto { ValidationType = ValidationType.MinValue, ParametersJson = "{\"min\": 2}", ErrorMessage = "At least two." });
        scenarioDto.Steps[3].Conditions.Add(new StepConditionDto { ConditionType = "Entry", ConditionJson = "{\"requiresSaved\": true}", ErrorMessage = "Save first." });
        var scenario = await db.Configs.CreateAsync(scenarioDto);
        await SetGuidAsync(db, scenario.Id!, ScenarioGuid);

        var objectiveDto = new FormConfigurationDto
        {
            EntityTypeName = "SiegeObjective",
            ConfigurationName = "SiegeObjective - Default",
            Description = "A capture objective of a siege scenario.",
            IsDefault = true,
            Steps =
            {
                Step("General", 0,
                    new FormFieldDto { FieldName = "SiegeScenarioId", Label = "Scenario", FieldType = FieldType.Object, ObjectType = "SiegeScenario", IsRequired = true, IsReadOnly = true, Order = 0 },
                    Field("Name", "Name", FieldType.String, 1, true),
                    Field("InstantVictory", "Instant victory", FieldType.Boolean, 2, defaultValue: "false")),
                Step("Capture point", 1,
                    Field("GateStructureId", "Gate", FieldType.Object, 0, false, "GateStructure",
                        "{\"pickerFilters\": {\"siegeScenarioId\": \"{SiegeScenarioId}\"}}", GateFieldGuid),
                    Field("LocationId", "Capture location", FieldType.Object, 1, false, "Location",
                        "{\"worldTask\": {\"enabled\": true, \"taskType\": \"LocationSelection\"}}", ObjectiveLocationFieldGuid),
                    Field("CapturePoints", "Capture points", FieldType.Integer, 2, true, defaultValue: "500"),
                    Field("CaptureRadius", "Capture radius", FieldType.Decimal, 3, true, defaultValue: "2.5")),
                Step("Gate behaviour", 2,
                    Field("GateStateOnCapture", "Gate state on capture", FieldType.Enum, 0, true,
                        settings: "{\"enumValues\": [\"OPEN\", \"CLOSED\"], \"enumValuesSubset\": true}", defaultValue: "OPEN"))
            }
        };
        objectiveDto.Steps[2].DisplayConditionGroups.Add(NotEmpty(GateFieldGuid, DisplayConditionTargetType.FormStep));
        objectiveDto.Steps[1].Fields[1].DisplayConditionGroups.Add(NotEmpty(GateFieldGuid, DisplayConditionTargetType.FormField, ConditionOperator.IsEmpty));
        var objective = await db.Configs.CreateAsync(objectiveDto);
        await SetGuidAsync(db, objective.Id!, ObjectiveGuid);

        // Rules: hub inside the town (same form), capture point inside the parent scenario's town.
        var townFieldId = await FieldIdAsync(db, int.Parse(scenario.Id!), "TownId");
        db.Context.FieldValidationRules.Add(new FieldValidationRule
        {
            FormFieldId = await FieldIdAsync(db, int.Parse(scenario.Id!), "HubLocationId"),
            ValidationType = "LocationInsideRegion",
            DependsOnFieldId = townFieldId,
            DependencyPath = "Town.WgRegionId",
            ConfigJson = "{\"regionPropertyPath\": \"WgRegionId\", \"allowBoundary\": false}",
            ErrorMessage = "Location {coordinates} is outside {parentEntityName}'s boundaries.",
            IsBlocking = true
        });
        db.Context.FieldValidationRules.Add(new FieldValidationRule
        {
            FormFieldId = await FieldIdAsync(db, int.Parse(objective.Id!), "LocationId"),
            ValidationType = "LocationInsideRegion",
            DependsOnFieldId = townFieldId,
            DependencyPath = "Town.WgRegionId",
            ConfigJson = "{\"regionPropertyPath\": \"WgRegionId\", \"allowBoundary\": false}",
            ErrorMessage = "Capture point outside the town.",
            IsBlocking = false,
            RequiresDependencyFilled = true
        });
        await db.Context.SaveChangesAsync();

        return (int.Parse(join.Id!), int.Parse(scenario.Id!), int.Parse(objective.Id!));
    }

    private static async Task SetGuidAsync(Db db, string id, string guid)
    {
        var config = await db.Context.FormConfigurations.SingleAsync(c => c.Id == int.Parse(id));
        config.ConfigurationGuid = Guid.Parse(guid);
        await db.Context.SaveChangesAsync();
    }

    private static Task<int> FieldIdAsync(Db db, int configId, string fieldName) =>
        db.Context.FormFields.Where(f => f.FormStep!.FormConfigurationId == configId && f.FieldName == fieldName).Select(f => f.Id).SingleAsync();

    private static async Task<int> RowCountAsync(Db db) =>
        await db.Context.FormConfigurations.CountAsync() + await db.Context.FormSteps.CountAsync() +
        await db.Context.FormFields.CountAsync() + await db.Context.FieldValidationRules.CountAsync() +
        await db.Context.DisplayConditionGroups.CountAsync();

    private static PortableFormConfigurationDto Minimal(string guid, string entity = "SiegeScenarioDistrict", string name = "Join") => new()
    {
        Format = PortableFormConfigurationDto.CurrentFormat,
        ConfigurationGuid = guid,
        EntityTypeName = entity,
        Name = name,
        Steps =
        {
            new PortableFormStepDto
            {
                StepGuid = Guid.NewGuid().ToString(),
                StepName = "District",
                Fields = { new PortableFormFieldDto { FieldGuid = Guid.NewGuid().ToString(), FieldName = "DistrictId", Label = "District", FieldType = FieldType.Object, ObjectType = "District", IsRequired = true } }
            }
        }
    };

    // ------------------------------------------------------------------ round trip

    [Fact]
    public async Task RoundTrip_SiegeSet_IntoAnEmptyDatabase_GivesIdenticalConfigurations()
    {
        var source = NewDb();
        var ids = await AuthorSiegeSetAsync(source);
        var exports = new[]
        {
            (await source.Portability.ExportAsync(ids.Join))!,
            (await source.Portability.ExportAsync(ids.Scenario))!,
            (await source.Portability.ExportAsync(ids.Objective))!
        };

        var target = NewDb();
        foreach (var file in exports) // join entry first, then the parent, then the child (dependency order)
        {
            var result = await target.Portability.ImportJsonAsync(FormConfigurationPortabilityService.Serialize(file), dryRun: false, makeDefault: false);
            Assert.True(result.Status == FormConfigurationImportStatus.Imported, string.Join(" | ", result.Errors));
            Assert.Empty(result.Warnings);
            Assert.True(result.IsDefault); // the empty database has no default for these entities
        }

        foreach (var file in exports)
        {
            var importedId = await target.Context.FormConfigurations.Where(c => c.ConfigurationGuid == Guid.Parse(file.ConfigurationGuid!)).Select(c => c.Id).SingleAsync();
            var again = await target.Portability.ExportAsync(importedId);
            Assert.Equal(FormConfigurationPortabilityService.Serialize(file), FormConfigurationPortabilityService.Serialize(again!));
        }

        // What the wizard reads: same steps, order, fields, settings and conditions; the M2M step points at the new join entry.
        var sourceDto = await source.Configs.GetByIdAsync(ids.Scenario);
        var targetScenarioId = await target.Context.FormConfigurations.Where(c => c.EntityTypeName == "SiegeScenario").Select(c => c.Id).SingleAsync();
        var targetJoinId = await target.Context.FormConfigurations.Where(c => c.EntityTypeName == "SiegeScenarioDistrict").Select(c => c.Id).SingleAsync();
        var targetDto = await target.Configs.GetByIdAsync(targetScenarioId);
        Assert.Equal(sourceDto!.Steps.Select(s => s.StepName), targetDto!.Steps.Select(s => s.StepName));
        Assert.Equal(targetJoinId.ToString(), targetDto.Steps[1].SubConfigurationId);
        Assert.Equal(
            sourceDto.Steps.SelectMany(s => s.Fields).Select(f => (f.FieldGuid, f.FieldName, f.Label, f.SettingsJson, f.IsRequired, f.MinSelection, f.CanCreate, f.IncrementValue)),
            targetDto.Steps.SelectMany(s => s.Fields).Select(f => (f.FieldGuid, f.FieldName, f.Label, f.SettingsJson, f.IsRequired, f.MinSelection, f.CanCreate, f.IncrementValue)));
    }

    [Fact]
    public async Task Export_UsesGuidsAndReferencesInsteadOfDatabaseIds()
    {
        var db = NewDb();
        var ids = await AuthorSiegeSetAsync(db);

        var scenario = (await db.Portability.ExportAsync(ids.Scenario))!;
        var objective = (await db.Portability.ExportAsync(ids.Objective))!;

        Assert.Equal("knk-formconfig/1", scenario.Format);
        Assert.Equal(ScenarioGuid, scenario.ConfigurationGuid);
        Assert.Equal(new[] { "General", "Districts", "Hub & entry", "Teams", "Readiness" }, scenario.Steps.Select(s => s.StepName));
        Assert.Equal(new[] { "Name", "Description", "TownId" }, scenario.Steps[0].Fields.Select(f => f.FieldName));
        Assert.Equal(JoinGuid, scenario.Steps[1].SubConfigurationRef!.ConfigurationGuid);
        Assert.Equal("SiegeScenarioDistrict", scenario.Steps[1].SubConfigurationRef!.EntityTypeName);
        Assert.Equal("Entry", scenario.Steps[3].Conditions!.Single().ConditionType);

        var localRule = scenario.ValidationRules.Single();
        Assert.Equal(HubFieldGuid, localRule.FieldGuid);
        Assert.Equal(TownFieldGuid, localRule.DependsOn!.FieldGuid);
        Assert.Null(localRule.DependsOn.ConfigurationGuid);

        var parentRule = objective.ValidationRules.Single();
        Assert.Equal(ObjectiveLocationFieldGuid, parentRule.FieldGuid);
        Assert.Equal(TownFieldGuid, parentRule.DependsOn!.FieldGuid);
        Assert.Equal(ScenarioGuid, parentRule.DependsOn.ConfigurationGuid);
        Assert.Equal(GateFieldGuid, objective.Steps[2].DisplayConditionGroups!.Single().Conditions.Single().SourceFieldGuid);

        var json = FormConfigurationPortabilityService.Serialize(scenario);
        Assert.DoesNotContain("\"id\"", json);
        Assert.DoesNotContain("subConfigurationId", json);
        Assert.Contains("\"fieldType\": \"Object\"", json);
    }

    [Fact]
    public async Task Export_UnknownId_IsNull()
    {
        Assert.Null(await NewDb().Portability.ExportAsync(4242));
    }

    // ------------------------------------------------------------------ never overwrite

    [Fact]
    public async Task ReImport_OfTheSameGuid_WritesNothing_EvenWhenTheFileChanged()
    {
        var db = NewDb();
        var first = await db.Portability.ImportAsync(Minimal(JoinGuid), dryRun: false, makeDefault: false);
        Assert.Equal(FormConfigurationImportStatus.Imported, first.Status);
        var before = await RowCountAsync(db);

        var changed = Minimal(JoinGuid, name: "Join v2");
        changed.Steps[0].StepName = "Renamed";
        var again = await db.Portability.ImportAsync(changed, dryRun: false, makeDefault: true);

        Assert.Equal(FormConfigurationImportStatus.AlreadyImported, again.Status);
        Assert.Equal(first.FormConfigurationId, again.FormConfigurationId);
        Assert.Equal(before, await RowCountAsync(db));
        Assert.Equal("District", (await db.Context.FormSteps.SingleAsync()).StepName);
        Assert.Equal("Join", (await db.Context.FormConfigurations.SingleAsync()).Name);
    }

    // ------------------------------------------------------------------ dry run

    [Fact]
    public async Task DryRun_ReportsMetadataDriftAndUnresolvedReferences_WithoutWriting()
    {
        var db = NewDb();
        await db.Portability.ImportAsync(Minimal(Guid.NewGuid().ToString(), name: "Existing"), dryRun: false, makeDefault: false);
        var before = await RowCountAsync(db);

        var file = Minimal(ScenarioGuid, "SiegeScenario", "Scenario");
        file.Steps[0].Fields[0] = new PortableFormFieldDto { FieldGuid = Guid.NewGuid().ToString(), FieldName = "NoSuchProperty", Label = "Gone", FieldType = FieldType.String };
        file.Steps.Add(new PortableFormStepDto
        {
            StepGuid = Guid.NewGuid().ToString(),
            StepName = "Districts",
            IsManyToManyRelationship = true,
            SubConfigurationRef = new PortableConfigurationRefDto { ConfigurationGuid = JoinGuid, Name = "SiegeScenarioDistrict - Join Entry" },
            Fields = { new PortableFormFieldDto { FieldGuid = Guid.NewGuid().ToString(), FieldName = "Districts", Label = "Districts", FieldType = FieldType.List, ObjectType = "SiegeScenarioDistrict" } }
        });
        file.ValidationRules.Add(new PortableValidationRuleDto
        {
            FieldGuid = file.Steps[1].Fields[0].FieldGuid,
            ValidationType = "LocationInsideRegion",
            DependsOn = new PortableFieldRefDto { FieldGuid = Guid.NewGuid().ToString(), ConfigurationGuid = Guid.NewGuid().ToString() }
        });

        var result = await db.Portability.ImportAsync(file, dryRun: true, makeDefault: false);

        Assert.Equal(FormConfigurationImportStatus.Invalid, result.Status);
        Assert.True(result.DryRun);
        Assert.Contains(result.Errors, e => e.Contains("NoSuchProperty"));
        Assert.Contains(result.Errors, e => e.Contains("SiegeScenarioDistrict - Join Entry") && e.Contains("not in this database"));
        Assert.Contains(result.Errors, e => e.StartsWith("Validation rule 1") && e.Contains("not in this database"));
        Assert.Contains("nothing was written", result.Message);
        Assert.Equal(before, await RowCountAsync(db));
    }

    [Fact]
    public async Task DryRun_OfAValidFile_PassesAndWritesNothing()
    {
        var db = NewDb();
        var result = await db.Portability.ImportAsync(Minimal(JoinGuid), dryRun: true, makeDefault: false);

        Assert.Equal(FormConfigurationImportStatus.DryRunPassed, result.Status);
        Assert.True(result.IsDefault);
        Assert.Equal((1, 1), (result.StepCount, result.FieldCount));
        Assert.Equal(0, await RowCountAsync(db));
    }

    [Theory]
    [InlineData("{\"format\": \"knk-formconfig/2\", \"configurationGuid\": \"6f1c2f0e-1d7a-4c55-9a7e-0b8f1d2c3a01\", \"entityTypeName\": \"X\", \"name\": \"X\", \"steps\": [{\"stepName\": \"a\", \"fields\": []}]}", "Unknown format")]
    [InlineData("{\"format\": \"knk-formconfig/1\", \"entityTypeName\": \"X\", \"name\": \"X\", \"steps\": [{\"stepName\": \"a\", \"fields\": []}]}", "configurationGuid")]
    [InlineData("{\"format\": \"knk-formconfig/1\", \"configurationGuid\": \"6f1c2f0e-1d7a-4c55-9a7e-0b8f1d2c3a01\", \"entityTypeName\": \"X\", \"name\": \"X\", \"steps\": []}", "At least one step")]
    [InlineData("{\"format\": \"knk-formconfig/1\", \"steps\": [{\"fields\": [{\"fieldType\": \"NotAType\"}]}]}", "Not a readable")]
    [InlineData("[1, 2]", "Not a readable")]
    public async Task Import_RejectsMalformedFiles(string json, string expected)
    {
        var db = NewDb();
        var result = await db.Portability.ImportJsonAsync(json, dryRun: false, makeDefault: false);

        Assert.Equal(FormConfigurationImportStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, e => e.Contains(expected));
        Assert.Equal(0, await RowCountAsync(db));
    }

    [Fact]
    public async Task Import_RejectsConditionsOnUnknownFieldsAndDuplicateGuids()
    {
        var db = NewDb();
        var file = Minimal(JoinGuid);
        file.Steps[0].DisplayConditionGroups = new() { new PortableDisplayConditionGroupDto { Conditions = { new PortableDisplayConditionDto { SourceFieldGuid = Guid.NewGuid().ToString(), Operator = ConditionOperator.IsNotEmpty } } } };
        file.Steps.Add(new PortableFormStepDto { StepGuid = file.Steps[0].StepGuid, StepName = "Twin", Fields = { new PortableFormFieldDto { FieldGuid = file.Steps[0].Fields[0].FieldGuid, FieldName = "SiegeScenarioId", Label = "Scenario", FieldType = FieldType.Object, ObjectType = "SiegeScenario", IsRequired = true } } });

        var result = await db.Portability.ImportAsync(file, dryRun: false, makeDefault: false);

        Assert.Equal(FormConfigurationImportStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, e => e.Contains("display condition reads field") && e.Contains("not a field of this file"));
        Assert.Contains(result.Errors, e => e.Contains("stepGuid") && e.Contains("used twice"));
        Assert.Contains(result.Errors, e => e.Contains("fieldGuid") && e.Contains("used twice"));
        Assert.Equal(0, await RowCountAsync(db));
    }

    [Fact]
    public async Task Import_WarnsOnUnknownWorldTaskTypes_AndMissingGuids_ButImports()
    {
        var db = NewDb();
        var file = Minimal(ObjectiveGuid, "SiegeObjective", "Objective");
        file.Steps[0].Fields = new()
        {
            new PortableFormFieldDto { FieldName = "SiegeScenarioId", Label = "Scenario", FieldType = FieldType.Object, ObjectType = "SiegeScenario", IsRequired = true, IsReadOnly = true },
            new PortableFormFieldDto { FieldGuid = Guid.NewGuid().ToString(), FieldName = "Name", Label = "Name", FieldType = FieldType.String, IsRequired = true },
            new PortableFormFieldDto { FieldGuid = Guid.NewGuid().ToString(), FieldName = "LocationId", Label = "Capture location", FieldType = FieldType.Object, ObjectType = "Location", SettingsJson = "{\"worldTask\": {\"enabled\": true, \"taskType\": \"LocationSelect\"}}" }
        };

        var result = await db.Portability.ImportAsync(file, dryRun: false, makeDefault: false);

        Assert.True(result.Status == FormConfigurationImportStatus.Imported, string.Join(" | ", result.Errors));
        Assert.Contains(result.Warnings, w => w.Contains("LocationSelect") && w.Contains("not one of the known types"));
        Assert.Contains(result.Warnings, w => w.Contains("fieldGuid is missing"));
        Assert.Equal("{\"worldTask\": {\"enabled\": true, \"taskType\": \"LocationSelect\"}}",
            await db.Context.FormFields.Where(f => f.FieldName == "LocationId").Select(f => f.SettingsJson).SingleAsync());
    }

    [Fact]
    public async Task Import_RejectsSettingsThatAreNotJsonObjects()
    {
        var db = NewDb();
        var file = Minimal(JoinGuid);
        file.Steps[0].Fields[0].SettingsJson = "{not json";

        var result = await db.Portability.ImportAsync(file, dryRun: true, makeDefault: false);

        Assert.Contains(result.Errors, e => e.Contains("settingsJson is not valid JSON"));
    }

    // ------------------------------------------------------------------ references and rules

    [Fact]
    public async Task Import_ResolvesLocalAndParentFormRuleDependencies_ToTheNewFieldIds()
    {
        var source = NewDb();
        var ids = await AuthorSiegeSetAsync(source);
        var target = NewDb();
        foreach (var id in new[] { ids.Join, ids.Scenario, ids.Objective })
        {
            var result = await target.Portability.ImportAsync((await source.Portability.ExportAsync(id))!, dryRun: false, makeDefault: false);
            Assert.True(result.Status == FormConfigurationImportStatus.Imported, string.Join(" | ", result.Errors));
        }

        var rules = await target.Context.FieldValidationRules.Include(r => r.FormField).Include(r => r.DependsOnField).ToListAsync();
        Assert.Equal(2, rules.Count);
        Assert.All(rules, r => Assert.Equal(Guid.Parse(TownFieldGuid), r.DependsOnField!.FieldGuid));
        var townField = await target.Context.FormFields.SingleAsync(f => f.FieldGuid == Guid.Parse(TownFieldGuid));
        Assert.All(rules, r => Assert.Equal(townField.Id, r.DependsOnFieldId));
        var parentRule = rules.Single(r => r.FormField.FieldName == "LocationId");
        Assert.False(parentRule.IsBlocking);
        Assert.True(parentRule.RequiresDependencyFilled);
        Assert.Equal("Town.WgRegionId", parentRule.DependencyPath);

        var gateField = await target.Context.FormFields.SingleAsync(f => f.FieldGuid == Guid.Parse(GateFieldGuid));
        var stepCondition = await target.Context.DisplayConditions.SingleAsync(c => c.DisplayConditionGroup.TargetType == DisplayConditionTargetType.FormStep);
        Assert.Equal(gateField.Id, stepCondition.SourceFormFieldId);
    }

    [Fact]
    public async Task Import_OfAChildBeforeItsParent_NamesTheMissingConfiguration()
    {
        var source = NewDb();
        var ids = await AuthorSiegeSetAsync(source);
        var target = NewDb();

        var result = await target.Portability.ImportAsync((await source.Portability.ExportAsync(ids.Objective))!, dryRun: true, makeDefault: false);

        Assert.Equal(FormConfigurationImportStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, e => e.Contains(ScenarioGuid) && e.Contains("Import that configuration first"));
    }

    [Fact]
    public async Task SubConfigurationRef_WithOnlyAnEntityName_ResolvesToThatEntitysDefault_WithAWarning()
    {
        var db = NewDb();
        var join = await db.Portability.ImportAsync(Minimal(JoinGuid), dryRun: false, makeDefault: false);
        var file = Minimal(ScenarioGuid, "SiegeScenario", "Scenario");
        file.Steps[0].Fields[0] = new PortableFormFieldDto { FieldGuid = Guid.NewGuid().ToString(), FieldName = "Name", Label = "Name", FieldType = FieldType.String, IsRequired = true };
        file.Steps.Add(new PortableFormStepDto
        {
            StepGuid = Guid.NewGuid().ToString(),
            StepName = "Districts",
            IsManyToManyRelationship = true,
            RelatedEntityPropertyName = "Districts",
            JoinEntityType = "SiegeScenarioDistrict",
            SubConfigurationRef = new PortableConfigurationRefDto { EntityTypeName = "SiegeScenarioDistrict" },
            Fields = { new PortableFormFieldDto { FieldGuid = Guid.NewGuid().ToString(), FieldName = "Districts", Label = "Districts", FieldType = FieldType.List, ObjectType = "SiegeScenarioDistrict" } }
        });

        var result = await db.Portability.ImportAsync(file, dryRun: false, makeDefault: false);

        Assert.True(result.Status == FormConfigurationImportStatus.Imported, string.Join(" | ", result.Errors));
        Assert.Contains(result.Warnings, w => w.Contains("resolved by entity"));
        Assert.Equal(int.Parse(join.FormConfigurationId!), (await db.Context.FormSteps.SingleAsync(s => s.StepName == "Districts")).SubConfigurationId);
    }

    // ------------------------------------------------------------------ default handling

    [Fact]
    public async Task Default_IsSetOnlyWhenTheEntityHasNone()
    {
        var db = NewDb();
        var first = await db.Portability.ImportAsync(Minimal(Guid.NewGuid().ToString(), name: "First"), dryRun: false, makeDefault: false);
        var second = await db.Portability.ImportAsync(Minimal(Guid.NewGuid().ToString(), name: "Second"), dryRun: false, makeDefault: false);

        Assert.True(first.IsDefault);
        Assert.False(second.IsDefault);
        Assert.Null(second.ReplacedDefaultId);
        Assert.Equal(new[] { "First" }, await db.Context.FormConfigurations.Where(c => c.IsDefault).Select(c => c.Name).ToListAsync());
    }

    [Fact]
    public async Task MakeDefault_ReplacesTheOldDefault_AndADryRunOnlySaysSo()
    {
        var db = NewDb();
        var old = await db.Portability.ImportAsync(Minimal(Guid.NewGuid().ToString(), name: "Old"), dryRun: false, makeDefault: false);

        var dry = await db.Portability.ImportAsync(Minimal(JoinGuid, name: "New"), dryRun: true, makeDefault: true);
        Assert.Equal(FormConfigurationImportStatus.DryRunPassed, dry.Status);
        Assert.True(dry.IsDefault);
        Assert.Equal(old.FormConfigurationId, dry.ReplacedDefaultId);
        Assert.Contains("replacing 'Old'", dry.Message);
        Assert.True((await db.Context.FormConfigurations.SingleAsync()).IsDefault);

        var real = await db.Portability.ImportAsync(Minimal(JoinGuid, name: "New"), dryRun: false, makeDefault: true);
        Assert.Equal(FormConfigurationImportStatus.Imported, real.Status);
        Assert.Equal(old.FormConfigurationId, real.ReplacedDefaultId);
        Assert.Equal(new[] { "New" }, await db.Context.FormConfigurations.Where(c => c.IsDefault).Select(c => c.Name).ToListAsync());
    }

    // ------------------------------------------------------------------ bundled seeds

    [Fact]
    public async Task Seeds_AreListedWithTheirImportState_AndImportedByName()
    {
        Directory.CreateDirectory(_seedDir);
        await File.WriteAllTextAsync(Path.Combine(_seedDir, "SiegeScenarioDistrict.json"), FormConfigurationPortabilityService.Serialize(Minimal(JoinGuid, name: "Join entry")));
        await File.WriteAllTextAsync(Path.Combine(_seedDir, "Broken.json"), "{ nope");
        await File.WriteAllTextAsync(Path.Combine(_seedDir, "README.md"), "not a seed");
        var db = NewDb();

        var listed = await db.Portability.ListSeedsAsync();
        Assert.Equal(new[] { "Broken", "SiegeScenarioDistrict" }, listed.Select(s => s.Name));
        Assert.NotNull(listed[0].Error);
        Assert.False(listed[1].Imported);
        Assert.Equal(("SiegeScenarioDistrict", "Join entry", 1, 1), (listed[1].EntityTypeName, listed[1].ConfigurationName, listed[1].StepCount, listed[1].FieldCount));

        var dry = await db.Portability.ImportSeedAsync("SiegeScenarioDistrict", dryRun: true, makeDefault: false);
        Assert.Equal(FormConfigurationImportStatus.DryRunPassed, dry!.Status);
        var imported = await db.Portability.ImportSeedAsync("SiegeScenarioDistrict", dryRun: false, makeDefault: false);
        Assert.Equal(FormConfigurationImportStatus.Imported, imported!.Status);

        listed = await db.Portability.ListSeedsAsync();
        Assert.True(listed[1].Imported);
        Assert.Equal(imported.FormConfigurationId, listed[1].ImportedFormConfigurationId);
        Assert.Equal(FormConfigurationImportStatus.AlreadyImported, (await db.Portability.ImportSeedAsync("SiegeScenarioDistrict", false, false))!.Status);
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("../SiegeScenarioDistrict")]
    [InlineData("..")]
    [InlineData("")]
    public async Task Seeds_UnknownOrUnsafeNames_AreNotFound(string name)
    {
        Directory.CreateDirectory(_seedDir);
        await File.WriteAllTextAsync(Path.Combine(_seedDir, "SiegeScenarioDistrict.json"), FormConfigurationPortabilityService.Serialize(Minimal(JoinGuid)));

        Assert.Null(await NewDb().Portability.ImportSeedAsync(name, dryRun: true, makeDefault: false));
    }

    [Fact]
    public async Task Seeds_UnresolvedReference_NamesTheBundledSeedToImportFirst()
    {
        Directory.CreateDirectory(_seedDir);
        await File.WriteAllTextAsync(Path.Combine(_seedDir, "SiegeScenarioDistrict.json"), FormConfigurationPortabilityService.Serialize(Minimal(JoinGuid)));
        var parent = Minimal(ScenarioGuid, "SiegeScenario", "Scenario");
        parent.Steps[0].Fields[0] = new PortableFormFieldDto { FieldGuid = Guid.NewGuid().ToString(), FieldName = "Districts", Label = "Districts", FieldType = FieldType.List, ObjectType = "SiegeScenarioDistrict" };
        parent.Steps[0].IsManyToManyRelationship = true;
        parent.Steps[0].SubConfigurationRef = new PortableConfigurationRefDto { ConfigurationGuid = JoinGuid };
        await File.WriteAllTextAsync(Path.Combine(_seedDir, "SiegeScenario.json"), FormConfigurationPortabilityService.Serialize(parent));

        var result = await NewDb().Portability.ImportSeedAsync("SiegeScenario", dryRun: true, makeDefault: false);

        Assert.Contains(result!.Errors, e => e.Contains("It is the bundled seed 'SiegeScenarioDistrict'"));
    }

    [Fact]
    public async Task Seeds_MissingFolder_ListsNothing()
    {
        Assert.Empty(await NewDb(Path.Combine(_seedDir, "nope")).Portability.ListSeedsAsync());
    }

    [Fact]
    public void PortableFile_ReadsCommentsAndPascalCase()
    {
        var file = JsonSerializer.Deserialize<PortableFormConfigurationDto>(
            "{ // seed\n \"Format\": \"knk-formconfig/1\", \"steps\": [{\"fields\": [{\"fieldType\": \"List\", \"elementType\": \"Object\"}]}], }",
            FormConfigurationPortabilityService.JsonOptions)!;

        Assert.Equal("knk-formconfig/1", file.Format);
        Assert.Equal(FieldType.List, file.Steps[0].Fields[0].FieldType);
        Assert.Equal(FieldType.Object, file.Steps[0].Fields[0].ElementType);
    }
}
