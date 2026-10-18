using System.Text.Json;
using Moq;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// ValidationService replaces an entity-reference dependency that carries only an id by the saved
/// record: an edited form holds a picked record as the read DTO's {id, name} (e.g. a siege
/// scenario's townId + townName), while the validator needs its other properties (Town.WgRegionId).
/// </summary>
public class ValidationServiceDependencyRecordTests
{
    private static readonly Town Cinix = new() { Id = 1, Name = "Cinix", Description = "d", WgRegionId = "cinix" };

    private readonly Mock<IFieldValidationRuleRepository> _rules = new();
    private readonly Mock<IPlaceholderResolutionService> _placeholders = new();
    private readonly Mock<IValidationMethod> _validator = new();

    public ValidationServiceDependencyRecordTests()
    {
        _rules.Setup(r => r.GetByFormFieldIdAsync(10)).ReturnsAsync(new List<FieldValidationRule>
        {
            new()
            {
                Id = 1,
                FormFieldId = 10,
                ValidationType = "LocationInsideRegion",
                DependsOnFieldId = 3,
                DependsOnField = new FormField { Id = 3, FieldName = "TownId", Label = "Town", FieldType = FieldType.Object, ObjectType = "Town" },
                IsBlocking = true
            }
        });
        _placeholders.Setup(p => p.LoadEntityByIdAsync("Town", It.IsAny<object>())).ReturnsAsync(Cinix);
        _validator.SetupGet(v => v.ValidationType).Returns("LocationInsideRegion");
        _validator.Setup(v => v.ValidateAsync(It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<Dictionary<string, object>?>()))
            .ReturnsAsync(new ValidationMethodResult { IsValid = true });
    }

    private ValidationService Service() => new(_rules.Object, new[] { _validator.Object }, _placeholders.Object);

    [Theory]
    [InlineData("""{ "id": 1, "name": "Cinix" }""")]
    [InlineData("1")]
    public async Task AnIdOnlyDependency_IsReplacedByTheSavedRecord(string dependencyJson)
    {
        var context = new Dictionary<string, object> { ["TownId"] = JsonDocument.Parse(dependencyJson).RootElement };

        await Service().ValidateFieldAsync(10, 1000, null, context);

        _validator.Verify(v => v.ValidateAsync(1000, Cinix, It.IsAny<string?>(), It.IsAny<Dictionary<string, object>?>()), Times.Once);
    }

    [Fact]
    public async Task ALoadedEntity_IsUsedAsItIs()
    {
        var town = new Town { Id = 1, Name = "Cinix", Description = "d", WgRegionId = "cinix" };

        await Service().ValidateFieldAsync(10, 1000, null, new Dictionary<string, object> { ["TownId"] = town });

        _validator.Verify(v => v.ValidateAsync(1000, town, It.IsAny<string?>(), It.IsAny<Dictionary<string, object>?>()), Times.Once);
        _placeholders.Verify(p => p.LoadEntityByIdAsync(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public async Task TheResult_EchoesTheFormValue_NotTheLoadedRecord()
    {
        // A tracked record's navigations are fixed up with whatever the request loaded before, e.g. a
        // permission group and its parent, which point at each other: serialising the record looped
        // ("possible object cycle ... DependencyValue.TeleportMinPremiumGroup.ParentGroup.ChildGroups").
        var premium = new PermissionGroup { Id = 5, Name = "Premium" };
        var vip = new PermissionGroup { Id = 6, Name = "VIP", ParentGroup = premium };
        premium.ChildGroups.Add(vip);
        var town = new Town { Id = 1, Name = "Cinix", Description = "d", WgRegionId = "cinix", TeleportMinPremiumGroup = vip };
        _placeholders.Setup(p => p.LoadEntityByIdAsync("Town", It.IsAny<object>())).ReturnsAsync(town);
        // Only a failed rule's result carries the metadata (e.g. a region outside the town).
        _validator.Setup(v => v.ValidateAsync(It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<Dictionary<string, object>?>()))
            .ReturnsAsync(new ValidationMethodResult { IsValid = false, Message = "Outside" });
        var formValue = JsonDocument.Parse("""{ "id": 1, "name": "Cinix" }""").RootElement;

        var result = await Service().ValidateFieldAsync(10, 1000, null, new Dictionary<string, object> { ["TownId"] = formValue });

        _validator.Verify(v => v.ValidateAsync(1000, town, It.IsAny<string?>(), It.IsAny<Dictionary<string, object>?>()), Times.Once);
        Assert.False(result.IsValid);
        Assert.Equal(formValue, result.Metadata!.DependencyValue);
        JsonSerializer.Serialize(result);
    }

    [Fact]
    public async Task AnUnknownId_KeepsTheFormValue()
    {
        _placeholders.Setup(p => p.LoadEntityByIdAsync("Town", It.IsAny<object>())).ReturnsAsync((object?)null);
        var formValue = JsonDocument.Parse("""{ "id": 99, "wgRegionId": "draft" }""").RootElement;

        await Service().ValidateFieldAsync(10, 1000, null, new Dictionary<string, object> { ["TownId"] = formValue });

        _validator.Verify(v => v.ValidateAsync(1000, It.Is<object?>(d => d is JsonElement), It.IsAny<string?>(), It.IsAny<Dictionary<string, object>?>()), Times.Once);
    }
}
