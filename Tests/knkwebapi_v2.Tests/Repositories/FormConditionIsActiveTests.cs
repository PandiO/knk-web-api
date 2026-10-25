using System.Text.Json;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Migrations;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace knkwebapi_v2.Tests.Repositories;

/// <summary>
/// KNG-186: StepConditionDto and FieldValidationDto carried an IsActive the models never had, and the
/// mapping ignored it, so the API sent isActive: false for every condition and validation and the
/// wizard (which runs a condition only while it is active) never ran a step completion condition.
/// IsActive is a column on both now (default true), mapped both ways, kept by create, update, clone
/// and export/import. FormConfigurationDto.IsActive, which nothing read, is gone.
/// </summary>
public class FormConditionIsActiveTests : IDisposable
{
    private static readonly IMapper Mapper = new MapperConfiguration(cfg => cfg.AddProfile<FormMappingProfile>()).CreateMapper();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private readonly DbContextOptions<KnKDbContext> _options = new DbContextOptionsBuilder<KnKDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options;
    private readonly List<ServiceProvider> _providers = new();

    public void Dispose()
    {
        foreach (var provider in _providers) provider.Dispose();
    }

    // ------------------------------------------------------------------ mapping

    [Fact]
    public void Mapping_KeepsIsActiveBothWays()
    {
        var offCondition = Mapper.Map<StepCondition>(new StepConditionDto { ConditionType = "Completion", ConditionJson = "{}", IsActive = false });
        var onCondition = Mapper.Map<StepCondition>(new StepConditionDto { ConditionType = "Completion", ConditionJson = "{}", IsActive = true });
        Assert.False(offCondition.IsActive);
        Assert.True(onCondition.IsActive);
        Assert.False(Mapper.Map<StepConditionDto>(offCondition).IsActive);
        Assert.True(Mapper.Map<StepConditionDto>(onCondition).IsActive);

        var offValidation = Mapper.Map<FieldValidation>(new FieldValidationDto { ValidationType = ValidationType.Required, IsActive = false });
        var onValidation = Mapper.Map<FieldValidation>(new FieldValidationDto { ValidationType = ValidationType.Required, IsActive = true });
        Assert.False(offValidation.IsActive);
        Assert.True(onValidation.IsActive);
        Assert.False(Mapper.Map<FieldValidationDto>(offValidation).IsActive);
        Assert.True(Mapper.Map<FieldValidationDto>(onValidation).IsActive);
    }

    // The builder never sent the flag for a new condition or validation (it has no toggle for it yet),
    // and a client that leaves it out must get an active rule, not a silently disabled one.
    [Fact]
    public void Dto_WithoutIsActive_IsActive()
    {
        var condition = JsonSerializer.Deserialize<StepConditionDto>("""{ "conditionType": "Completion", "conditionJson": "{}" }""", Json)!;
        var validation = JsonSerializer.Deserialize<FieldValidationDto>("""{ "validationType": "Required" }""", Json)!;

        Assert.True(condition.IsActive);
        Assert.True(validation.IsActive);
        Assert.False(JsonSerializer.Deserialize<StepConditionDto>("""{ "conditionType": "Completion", "isActive": false }""", Json)!.IsActive);
    }

    [Fact]
    public void FormConfigurationDto_HasNoIsActive()
    {
        var json = JsonSerializer.Serialize(Mapper.Map<FormConfigurationDto>(new FormConfiguration { Name = "District - Default", EntityTypeName = "District" }), Json);

        Assert.DoesNotContain("isActive", json);
        Assert.Contains("\"isDefault\"", json);
    }

    // ------------------------------------------------------------------ repository

    [Fact]
    public async Task CreateAndUpdate_StoreIsActive()
    {
        var config = new FormConfiguration
        {
            Name = "PermissionGroup - Default",
            EntityTypeName = "PermissionGroup",
            Steps =
            {
                new FormStep
                {
                    StepName = "Terms",
                    StepConditions = { new StepCondition { ConditionType = "CompletionCondition", ConditionLogicJson = "{}", IsActive = false } },
                    Fields =
                    {
                        new FormField
                        {
                            FieldName = "AgreedToTerms", Label = "Agreed", FieldType = FieldType.Boolean,
                            Validations = { new FieldValidation { Type = ValidationType.Required, IsActive = false } }
                        }
                    }
                }
            }
        };
        await using (var db = new KnKDbContext(_options))
        {
            await new FormConfigurationRepository(db).AddAsync(config);
        }

        await using (var db = new KnKDbContext(_options))
        {
            Assert.False((await db.StepConditions.SingleAsync()).IsActive);
            Assert.False((await db.FieldValidations.SingleAsync()).IsActive);
        }

        // The builder's save sends the whole graph with ids; UpdateAsync merges condition by condition.
        var step = config.Steps[0];
        var field = step.Fields[0];
        var update = new FormConfiguration
        {
            Id = config.Id,
            Name = config.Name,
            EntityTypeName = config.EntityTypeName,
            Steps =
            {
                new FormStep
                {
                    Id = step.Id, StepGuid = step.StepGuid, StepName = step.StepName,
                    StepConditions = { new StepCondition { Id = step.StepConditions[0].Id, ConditionType = "CompletionCondition", ConditionLogicJson = "{}", IsActive = true } },
                    Fields =
                    {
                        new FormField
                        {
                            Id = field.Id, FieldGuid = field.FieldGuid, FieldName = field.FieldName, Label = field.Label, FieldType = field.FieldType,
                            Validations =
                            {
                                new FieldValidation { Id = field.Validations[0].Id, Type = ValidationType.Required, IsActive = true },
                                new FieldValidation { Type = ValidationType.Range, IsActive = false }
                            }
                        }
                    }
                }
            }
        };
        await using (var db = new KnKDbContext(_options))
        {
            await new FormConfigurationRepository(db).UpdateAsync(update);
        }

        await using (var db = new KnKDbContext(_options))
        {
            Assert.True((await db.StepConditions.SingleAsync()).IsActive);
            var validations = await db.FieldValidations.OrderBy(v => v.Id).ToListAsync();
            Assert.Equal(new[] { true, false }, validations.Select(v => v.IsActive));
        }
    }

    // ------------------------------------------------------------------ export / import

    private sealed record Db(KnKDbContext Context, FormConfigurationPortabilityService Portability);

    private Db NewDb()
    {
        var services = new ServiceCollection();
        services.AddDbContext<KnKDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);

        var context = provider.GetRequiredService<KnKDbContext>();
        var repo = new FormConfigurationRepository(context);
        var metadata = new MetadataService(provider.GetRequiredService<IServiceScopeFactory>());
        var seedDir = Path.Combine(Path.GetTempPath(), "knk-seeds-" + Guid.NewGuid().ToString("N"));
        return new Db(context, new FormConfigurationPortabilityService(context, repo, new FormTemplateValidationService(), metadata, new FormConfigurationSeedOptions { Directory = seedDir }));
    }

    private static PortableFormConfigurationDto JoinEntryFile(string guid) => new()
    {
        Format = PortableFormConfigurationDto.CurrentFormat,
        ConfigurationGuid = guid,
        EntityTypeName = "SiegeScenarioDistrict",
        Name = "Join",
        Steps =
        {
            new PortableFormStepDto
            {
                StepGuid = Guid.NewGuid().ToString(),
                StepName = "District",
                Conditions = new()
                {
                    new PortableStepConditionDto { ConditionType = "Completion", ConditionJson = "{}", ErrorMessage = "Off", IsActive = false },
                    new PortableStepConditionDto { ConditionType = "Completion", ConditionJson = "{}", ErrorMessage = "On (flag left out, as in every file written before KNG-186)" }
                },
                Fields =
                {
                    new PortableFormFieldDto
                    {
                        FieldGuid = Guid.NewGuid().ToString(), FieldName = "DistrictId", Label = "District", FieldType = FieldType.Object, ObjectType = "District", IsRequired = true,
                        Validations = new()
                        {
                            new PortableFieldValidationDto { ValidationType = ValidationType.Required, IsActive = false },
                            new PortableFieldValidationDto { ValidationType = ValidationType.Required }
                        }
                    }
                }
            }
        }
    };

    [Fact]
    public async Task Import_ReadsIsActive_AbsentMeansActive_AndExportWritesOnlyFalse()
    {
        var db = NewDb();
        var guid = Guid.NewGuid().ToString();

        var result = await db.Portability.ImportJsonAsync(FormConfigurationPortabilityService.Serialize(JoinEntryFile(guid)), dryRun: false, makeDefault: false);
        Assert.True(result.Status == FormConfigurationImportStatus.Imported, string.Join(" | ", result.Errors));

        var conditions = await db.Context.StepConditions.OrderBy(c => c.Id).ToListAsync();
        var validations = await db.Context.FieldValidations.OrderBy(v => v.Id).ToListAsync();
        Assert.Equal(new[] { false, true }, conditions.Select(c => c.IsActive));
        Assert.Equal(new[] { false, true }, validations.Select(v => v.IsActive));

        var id = await db.Context.FormConfigurations.Where(c => c.ConfigurationGuid == Guid.Parse(guid)).Select(c => c.Id).SingleAsync();
        var json = FormConfigurationPortabilityService.Serialize((await db.Portability.ExportAsync(id))!);
        Assert.Equal(2, json.Split("\"isActive\": false").Length - 1); // one condition, one validation
        Assert.DoesNotContain("\"isActive\": true", json);                // an active rule is the default: not written, so older files stay identical
    }

    // ------------------------------------------------------------------ migration

    [Fact]
    public void Migration_AddsTwoColumnsThatDefaultToActive()
    {
        var up = new FormConditionAndValidationIsActive().UpOperations;

        var columns = up.OfType<AddColumnOperation>().ToList();
        Assert.Equal(up.Count, columns.Count);
        Assert.Equal(new[] { "FieldValidations.IsActive", "StepConditions.IsActive" }, columns.Select(c => $"{c.Table}.{c.Name}").OrderBy(n => n));
        Assert.All(columns, c => Assert.False(c.IsNullable));
        Assert.All(columns, c => Assert.Equal(true, c.DefaultValue)); // existing rows stay active, not the scaffolded false
    }
}
