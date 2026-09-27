using Moq;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// The default siege FormConfigurations (the shape of the dev DB's 29-32, trimmed to the fields the
/// readiness rules care about) behind mocked repositories, plus the rules a test adds - so readiness
/// runs through the real SavedEntityRuleValidator and ValidationService.
/// </summary>
public sealed class SiegeFormRules
{
    public const int ScenarioTownFieldId = 3201;
    public const int HubLocationFieldId = 3202;
    public const int SpawnpointLocationFieldId = 2901;
    public const int ObjectiveLocationFieldId = 3101;

    public Mock<IFormConfigurationRepository> Configs { get; } = new();
    public Mock<IFieldValidationRuleRepository> Rules { get; } = new();

    private readonly Dictionary<string, FormConfiguration> _configs = new();
    private readonly List<FieldValidationRule> _rules = new();
    private readonly Dictionary<int, (FormField field, int configId)> _fields = new();

    public SiegeFormRules()
    {
        AddConfig(32, nameof(SiegeScenario),
            Field(3200, "Name", FieldType.String),
            Field(ScenarioTownFieldId, "TownId", FieldType.Object, "Town"),
            Field(HubLocationFieldId, "HubLocationId", FieldType.Object, "Location"),
            Field(3203, "Teams", FieldType.List, "SiegeTeam"),
            Field(3204, "Id", FieldType.Integer));
        AddConfig(30, nameof(SiegeTeam),
            Field(3000, "SiegeScenarioId", FieldType.Object, "SiegeScenario"),
            Field(3001, "Name", FieldType.String),
            Field(3002, "Spawnpoints", FieldType.List, "SiegeSpawnpoint"));
        AddConfig(29, nameof(SiegeSpawnpoint),
            Field(2900, "SiegeTeamId", FieldType.Object, "SiegeTeam"),
            Field(2902, "Name", FieldType.String),
            Field(SpawnpointLocationFieldId, "LocationId", FieldType.Object, "Location", "Location"));
        AddConfig(31, nameof(SiegeObjective),
            Field(3100, "SiegeScenarioId", FieldType.Object, "SiegeScenario"),
            Field(3102, "Name", FieldType.String),
            Field(ObjectiveLocationFieldId, "LocationId", FieldType.Object, "Location", "Capture location"));

        Configs.Setup(c => c.GetDefaultByEntityTypeNameAsync(It.IsAny<string>()))
            .ReturnsAsync((string name) => _configs.GetValueOrDefault(name));
        Rules.Setup(r => r.GetByFormConfigurationIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int configId) => _rules.Where(r => _fields[r.FormFieldId].configId == configId).ToList());
        Rules.Setup(r => r.GetByFormFieldIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int fieldId) => _rules.Where(r => r.FormFieldId == fieldId).ToList());
    }

    /// <summary>
    /// A LocationInsideRegion rule on <paramref name="fieldId"/> that depends on the scenario form's
    /// Town field - for the spawnpoint/objective forms a field of a parent form.
    /// </summary>
    public FieldValidationRule AddLocationInsideTownRule(int fieldId, bool blocking = true)
    {
        var rule = new FieldValidationRule
        {
            Id = 9000 + _rules.Count,
            FormFieldId = fieldId,
            FormField = _fields[fieldId].field,
            ValidationType = "LocationInsideRegion",
            DependsOnFieldId = ScenarioTownFieldId,
            DependsOnField = _fields[ScenarioTownFieldId].field,
            DependencyPath = "Town.WgRegionId",
            ConfigJson = """{ "regionPropertyPath": "WgRegionId", "allowBoundary": false }""",
            ErrorMessage = "Location {coordinates} is outside {Town.Name}'s boundaries.",
            IsBlocking = blocking
        };
        _rules.Add(rule);
        return rule;
    }

    public ISavedEntityRuleValidator Validator(params IValidationMethod[] validationMethods) =>
        new SavedEntityRuleValidator(Configs.Object, Rules.Object,
            new ValidationService(Rules.Object, validationMethods, new Mock<IPlaceholderResolutionService>().Object));

    private static FormField Field(int id, string name, FieldType type, string? objectType = null, string? label = null) =>
        new() { Id = id, FieldName = name, Label = label ?? name, FieldType = type, ObjectType = objectType };

    private void AddConfig(int id, string entityTypeName, params FormField[] fields)
    {
        var step = new FormStep { Id = id * 10, StepName = "Step", FormConfigurationId = id, Fields = fields.ToList() };
        foreach (var field in fields)
        {
            field.FormStepId = step.Id;
            field.FormStep = step;
            _fields[field.Id] = (field, id);
        }
        _configs[entityTypeName] = new FormConfiguration
        {
            Id = id, Name = $"{entityTypeName} - Default", EntityTypeName = entityTypeName, IsDefault = true,
            Steps = new List<FormStep> { step }
        };
    }
}
