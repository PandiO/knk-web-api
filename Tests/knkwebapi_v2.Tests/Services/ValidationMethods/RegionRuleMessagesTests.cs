using Moq;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.ValidationMethods;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace knkwebapi_v2.Tests.Services.ValidationMethods;

/// <summary>
/// KNG-173: a failed region rule keeps the rule's ErrorMessage and says what it must be inside,
/// so "The gate's region is not completely inside the district's region." names the district.
/// </summary>
public class RegionRuleMessagesTests
{
    private const string GateMessage = "The gate's region is not completely inside the district's region.";

    private static FieldValidationRule Rule(string type = "RegionContainment", string? label = "District", string? path = "District.WgRegionId", bool blocking = true) => new()
    {
        Id = 1,
        FormFieldId = 10,
        ValidationType = type,
        ErrorMessage = GateMessage,
        DependencyPath = path,
        DependsOnFieldId = label == null ? null : 3,
        DependsOnField = label == null ? null : new FormField { Id = 3, FieldName = "DistrictId", Label = label, FieldType = FieldType.Object, ObjectType = "District" },
        IsBlocking = blocking
    };

    private static Dictionary<string, string> Containment(string name = "kng-162 step d2.5", string region = "domain_27") => new()
    {
        ["townName"] = name,
        ["parentEntityName"] = name,
        ["childRegionId"] = "gate_1",
        ["parentRegionId"] = region
    };

    [Fact]
    public void ContainmentFailure_NamesTheParentAndItsRegion()
    {
        Assert.Equal(
            GateMessage + " Must be inside District \"kng-162 step d2.5\" (region domain_27).",
            RegionRuleMessages.WithParent(Rule(), GateMessage, Containment()));
    }

    [Fact]
    public void WithoutTheDependencyField_TheLabelComesFromTheDependencyPath()
    {
        Assert.Equal(
            GateMessage + " Must be inside District \"kng-162 step d2.5\" (region domain_27).",
            RegionRuleMessages.WithParent(Rule(label: null), GateMessage, Containment()));
    }

    [Fact]
    public void WithoutLabelOrPath_TheTypeIsLeftOut()
    {
        Assert.Equal(
            GateMessage + " Must be inside \"kng-162 step d2.5\" (region domain_27).",
            RegionRuleMessages.WithParent(Rule(label: null, path: null), GateMessage, Containment()));
    }

    [Fact]
    public void LocationFailure_UsesRegionName()
    {
        var placeholders = new Dictionary<string, string>
        {
            ["parentEntityName"] = "Cinix",
            ["coordinates"] = "10, 20",
            ["regionName"] = "town_cinix"
        };

        Assert.Equal(
            "The location is outside the town. Must be inside Town \"Cinix\" (region town_cinix).",
            RegionRuleMessages.WithParent(Rule("LocationInsideRegion", label: "Town"), "The location is outside the town", placeholders));
    }

    [Theory]
    [InlineData("The gate must be inside kng-162 step d2.5.")]
    [InlineData("Location {coordinates} is outside the region of gate {parentEntityName}.")]
    public void AMessageThatAlreadyNamesTheParent_IsLeftAsItIs(string message)
    {
        Assert.Equal(message, RegionRuleMessages.WithParent(Rule(), message, Containment()));
    }

    [Fact]
    public void ANameThatIsTheValidatorsFallback_IsNotAName()
    {
        Assert.Equal(
            GateMessage + " Must be inside District (region domain_27).",
            RegionRuleMessages.WithParent(Rule(), GateMessage, Containment(name: "Parent Region")));
    }

    [Fact]
    public void NoPlaceholders_LeavesTheMessage()
    {
        Assert.Equal(GateMessage, RegionRuleMessages.WithParent(Rule(), GateMessage, new Dictionary<string, string>()));
        Assert.Equal(GateMessage, RegionRuleMessages.WithParent(Rule(), GateMessage, null));
    }

    [Fact]
    public void AnEmptyRuleMessage_IsJustTheSentence()
    {
        Assert.Equal("Must be inside District \"Old Quarter\" (region d_oq).",
            RegionRuleMessages.WithParent(Rule(), "", Containment("Old Quarter", "d_oq")));
    }

    // ---- the services that run the rules

    private static Mock<IValidationMethod> Validator(string type, ValidationMethodResult result)
    {
        var validator = new Mock<IValidationMethod>();
        validator.SetupGet(v => v.ValidationType).Returns(type);
        validator.Setup(v => v.ValidateAsync(It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<Dictionary<string, object>?>()))
            .ReturnsAsync(result);
        return validator;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ValidationService_AppendsTheParent_AndKeepsIsBlocking(bool blocking)
    {
        // The wizard's endpoint (FieldValidationRulesController → ValidationService).
        var rule = Rule(blocking: blocking);
        var rules = new Mock<IFieldValidationRuleRepository>();
        rules.Setup(r => r.GetByFormFieldIdAsync(10)).ReturnsAsync(new List<FieldValidationRule> { rule });
        var validator = Validator("RegionContainment", new ValidationMethodResult { IsValid = false, Message = "The region extends outside x.", Placeholders = Containment() });
        var service = new ValidationService(rules.Object, new[] { validator.Object }, new Mock<IPlaceholderResolutionService>().Object);

        var result = await service.ValidateFieldAsync(10, "gate_1", new Town { Id = 1, Name = "kng-162 step d2.5", Description = "", WgRegionId = "domain_27" }, null);

        Assert.False(result.IsValid);
        Assert.Equal(blocking, result.IsBlocking);
        Assert.Equal(GateMessage + " Must be inside District \"kng-162 step d2.5\" (region domain_27).", result.Message);
    }

    [Fact]
    public async Task ValidationService_ACheckThatCouldNotRun_KeepsTheValidatorsMessage()
    {
        var rules = new Mock<IFieldValidationRuleRepository>();
        rules.Setup(r => r.GetByFormFieldIdAsync(10)).ReturnsAsync(new List<FieldValidationRule> { Rule() });
        var validator = Validator("RegionContainment", new ValidationMethodResult
        {
            IsValid = false,
            Message = "Cannot verify region containment right now.",
            Metadata = new Dictionary<string, object> { ["failureReason"] = "PluginUnreachable" }
        });
        var service = new ValidationService(rules.Object, new[] { validator.Object }, new Mock<IPlaceholderResolutionService>().Object);

        var result = await service.ValidateFieldAsync(10, "gate_1", new Town { Id = 1, Name = "x", Description = "", WgRegionId = "r" }, null);

        Assert.Equal("Cannot verify region containment right now.", result.Message);
    }

    [Fact]
    public async Task FieldValidationService_AppendsTheParent()
    {
        var validator = Validator("RegionContainment", new ValidationMethodResult { IsValid = false, Message = "outside", Placeholders = Containment() });
        var service = new FieldValidationService(new Mock<IPlaceholderResolutionService>().Object, new[] { validator.Object }, NullLogger<FieldValidationService>.Instance);

        var result = await service.ValidateRegionContainmentAsync(Rule(blocking: false), "gate_1", new object(), new Dictionary<string, string>());

        Assert.False(result.IsBlocking);
        Assert.Equal(GateMessage + " Must be inside District \"kng-162 step d2.5\" (region domain_27).", result.Message);
    }
}
