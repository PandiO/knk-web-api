using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.ValidationMethods;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services.ValidationMethods
{
    /// <summary>
    /// PandiO/knk-workspace#99: a ConditionalRequired rule never saw its condition as met (flat config
    /// read case-sensitively, a JsonElement compared with a string by object equality), so it never
    /// made a field required. The ConfigJson contract is now the ConditionalValueMatch clause
    /// { "condition": { "op", "value" } } (developer decision 2026-11-04), read case-insensitively,
    /// with the older flat and "operator" forms still accepted.
    /// </summary>
    public class ConditionalRequiredValidatorTests
    {
        private readonly ConditionalRequiredValidator _validator = new();

        private Task<ValidationMethodResult> Validate(object? fieldValue, object? dependencyValue, string? configJson)
            => _validator.ValidateAsync(fieldValue, dependencyValue, configJson, null);

        private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

        [Fact]
        public void ValidationType_ReturnsCorrectValue()
        {
            _validator.ValidationType.Should().Be("ConditionalRequired");
        }

        // ---- the config shapes

        [Theory]
        // the contract
        [InlineData("{\"condition\":{\"op\":\"equals\",\"value\":\"true\"}}")]
        // the web app's rule builder template before the decision (boolean value, "operator")
        [InlineData("{\"condition\":{\"operator\":\"equals\",\"value\":true}}")]
        // flat, as the old FieldValidationServiceTests case wrote it
        [InlineData("{\"operator\": \"Equals\", \"value\": \"true\"}")]
        // flat, PascalCase, as the old ConditionalRequiredConfig class serialised
        [InlineData("{\"Operator\":\"Equals\",\"Value\":\"true\"}")]
        // any casing of the nested names
        [InlineData("{\"Condition\":{\"Op\":\"EQUALS\",\"Value\":\"TRUE\"}}")]
        public async Task ConditionMet_EmptyField_IsInvalid_ForEveryConfigShape(string configJson)
        {
            var result = await Validate(fieldValue: null, dependencyValue: "true", configJson);

            result.IsValid.Should().BeFalse();
            result.Message.Should().Be("This field is required when the condition is met");
        }

        [Fact]
        public async Task ConditionMet_FilledField_IsValid()
        {
            var result = await Validate("A long story", "complex", "{\"condition\":{\"op\":\"equals\",\"value\":\"complex\"}}");

            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task ConditionNotMet_EmptyField_IsValid()
        {
            var result = await Validate(null, "simple", "{\"condition\":{\"op\":\"equals\",\"value\":\"complex\"}}");

            result.IsValid.Should().BeTrue();
            result.Message.Should().Be("Condition not met; field is optional");
        }

        [Fact]
        public async Task NoDependencyValue_ConditionIsNotMet()
        {
            var result = await Validate(null, null, "{\"condition\":{\"op\":\"notEquals\",\"value\":\"x\"}}");

            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task NoConfig_DefaultsToEqualsNull_SoTheConditionIsNotMet()
        {
            (await Validate(null, "anything", null)).IsValid.Should().BeTrue();
            (await Validate(null, "anything", "{}")).IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task InvalidJson_ReportsAValidationError()
        {
            var result = await Validate(null, "true", "{not json");

            result.IsValid.Should().BeFalse();
            result.Message.Should().StartWith("Validation error:");
        }

        [Fact]
        public async Task FailedResult_CarriesTheOperatorAndConditionValue()
        {
            var result = await Validate(null, "complex", "{\"condition\":{\"op\":\"equals\",\"value\":\"complex\"}}");

            result.Placeholders.Should().Contain("operator", "equals").And.Contain("conditionValue", "complex");
        }

        // ---- how values are compared

        [Theory]
        [InlineData(true)]
        [InlineData("true")]
        [InlineData("True")]
        public async Task BooleanConfigValue_MatchesABooleanOrItsText(object dependencyValue)
        {
            var result = await Validate(null, dependencyValue, "{\"condition\":{\"op\":\"equals\",\"value\":true}}");

            result.IsValid.Should().BeFalse();
        }

        [Fact]
        public async Task JsonDependencyValues_AreUnwrapped()
        {
            var config = "{\"condition\":{\"op\":\"equals\",\"value\":true}}";

            (await Validate(null, Json("true"), config)).IsValid.Should().BeFalse();
            (await Validate(null, Json("false"), config)).IsValid.Should().BeTrue();
        }

        [Theory]
        [InlineData(5)]
        [InlineData(5L)]
        [InlineData("5")]
        public async Task NumberConfigValue_MatchesANumberOrItsText(object dependencyValue)
        {
            var result = await Validate(null, dependencyValue, "{\"condition\":{\"op\":\"equals\",\"value\":5}}");

            result.IsValid.Should().BeFalse();
        }

        [Theory]
        [InlineData("12", false)]
        [InlineData("10", true)]
        [InlineData("3", true)]
        public async Task GreaterThan_ComparesNumbers(string dependencyValue, bool expectedValid)
        {
            var result = await Validate(null, dependencyValue, "{\"condition\":{\"op\":\"greaterThan\",\"value\":10}}");

            result.IsValid.Should().Be(expectedValid);
        }

        [Theory]
        [InlineData("{\"condition\":{\"op\":\"in\",\"value\":\"DRAWBRIDGE,DOUBLE_DOORS\"}}")]
        [InlineData("{\"condition\":{\"op\":\"in\",\"value\":[\"DRAWBRIDGE\",\"DOUBLE_DOORS\"]}}")]
        public async Task In_AcceptsAListOrAnArray(string configJson)
        {
            (await Validate(null, "double_doors", configJson)).IsValid.Should().BeFalse();
            (await Validate(null, "PORTCULLIS", configJson)).IsValid.Should().BeTrue();
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(0)]
        public async Task BlankOrZeroField_CountsAsEmpty(object fieldValue)
        {
            var result = await Validate(fieldValue, "true", "{\"condition\":{\"op\":\"equals\",\"value\":true}}");

            result.IsValid.Should().BeFalse();
        }

        [Theory]
        [InlineData("null", false)]
        [InlineData("\"\"", false)]
        [InlineData("0", false)]
        [InlineData("\"text\"", true)]
        [InlineData("7", true)]
        public async Task JsonFieldValues_AreCheckedForEmptiness(string fieldJson, bool expectedValid)
        {
            var result = await Validate(Json(fieldJson), "true", "{\"condition\":{\"op\":\"equals\",\"value\":true}}");

            result.IsValid.Should().Be(expectedValid);
        }

        // ---- a picked record as the dependency (an Object field; ValidationService loads the record)

        [Fact]
        public async Task RecordDependency_ComparesTheConfiguredProperty()
        {
            var town = new Town { Id = 7, Name = "Springfield", Description = "", WgRegionId = "town_7" };
            var config = "{\"condition\":{\"op\":\"equals\",\"value\":\"springfield\",\"property\":\"Name\"}}";

            (await Validate(null, town, config)).IsValid.Should().BeFalse();
            (await Validate(null, new Town { Id = 8, Name = "Shelbyville", Description = "", WgRegionId = "town_8" }, config)).IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task RecordDependency_ComparesTheIdByDefault()
        {
            var town = new Town { Id = 7, Name = "Springfield", Description = "", WgRegionId = "town_7" };

            (await Validate(null, town, "{\"condition\":{\"op\":\"equals\",\"value\":7}}")).IsValid.Should().BeFalse();
            (await Validate(null, town, "{\"condition\":{\"op\":\"equals\",\"value\":8}}")).IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task JsonRecordDependency_ComparesTheConfiguredProperty()
        {
            var record = Json("{\"id\":7,\"name\":\"Springfield\"}");

            (await Validate(null, record, "{\"condition\":{\"op\":\"equals\",\"value\":\"Springfield\",\"property\":\"Name\"}}")).IsValid.Should().BeFalse();
            (await Validate(null, record, "{\"condition\":{\"op\":\"equals\",\"value\":7}}")).IsValid.Should().BeFalse();
        }

        [Fact]
        public async Task RecordDependency_MissingProperty_ConditionIsNotMet()
        {
            var town = new Town { Id = 7, Name = "Springfield", Description = "", WgRegionId = "town_7" };

            (await Validate(null, town, "{\"condition\":{\"op\":\"equals\",\"value\":\"x\",\"property\":\"NoSuchProperty\"}}")).IsValid.Should().BeTrue();
        }

        // ---- the live path: FieldValidationRulesController → ValidationService → this validator

        [Fact]
        public async Task ValidationService_RunsTheRule_AgainstTheLoadedRecord()
        {
            var rule = new FieldValidationRule
            {
                Id = 1,
                FormFieldId = 10,
                ValidationType = "ConditionalRequired",
                ErrorMessage = "A description is required for Springfield.",
                DependsOnFieldId = 3,
                DependsOnField = new FormField { Id = 3, FieldName = "TownId", Label = "Town", FieldType = FieldType.Object, ObjectType = "Town" },
                ConfigJson = "{\"condition\":{\"op\":\"equals\",\"value\":\"Springfield\",\"property\":\"Name\"}}",
                IsBlocking = true
            };
            var rules = new Mock<IFieldValidationRuleRepository>();
            rules.Setup(r => r.GetByFormFieldIdAsync(10)).ReturnsAsync(new List<FieldValidationRule> { rule });
            var placeholders = new Mock<IPlaceholderResolutionService>();
            placeholders.Setup(p => p.LoadEntityByIdAsync("Town", It.IsAny<object>()))
                .ReturnsAsync(new Town { Id = 7, Name = "Springfield", Description = "", WgRegionId = "town_7" });
            var service = new ValidationService(rules.Object, new IValidationMethod[] { new ConditionalRequiredValidator() }, placeholders.Object);

            // The form holds only the picked town's id.
            var empty = await service.ValidateFieldAsync(10, null, 7, null);
            var filled = await service.ValidateFieldAsync(10, "Founded by Jebediah", 7, null);

            empty.IsValid.Should().BeFalse();
            empty.IsBlocking.Should().BeTrue();
            empty.Message.Should().Be("A description is required for Springfield.");
            filled.IsValid.Should().BeTrue();
        }
    }
}
