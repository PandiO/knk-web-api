using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using knkwebapi_v2.Services.ValidationMethods;
using Xunit;

namespace knkwebapi_v2.Tests.Services.ValidationMethods
{
    /// <summary>
    /// Unit tests for ConditionalValueMatchValidator - closes the GateType/MotionType
    /// cross-validation gap flagged in docs/features/gate-structure-animation/
    /// ROTATION_GAP_FILL_DESIGN.md, Phase D (a DRAWBRIDGE/DOUBLE_DOORS gate must have
    /// MotionType=ROTATION, not silently accept VERTICAL/LATERAL).
    ///
    /// Exercises the validator directly via IValidationMethod.ValidateAsync(fieldValue,
    /// dependencyValue, configJson, formContextData) - the real, current signature used by
    /// FieldValidationService (see Services/FieldValidationService.cs's ValidateAsync call
    /// sites), not the stale FieldValidationRule-based signature in the excluded
    /// ValidationMethodsTests.cs (see the &lt;Compile Remove&gt; entries in
    /// knkwebapi_v2.Tests.csproj) - that file predates the current interface and isn't compiled.
    /// </summary>
    public class ConditionalValueMatchValidatorTests
    {
        private readonly ConditionalValueMatchValidator _validator = new();

        private static string GateTypeMotionTypeConfig() => JsonSerializer.Serialize(new
        {
            condition = new { op = "in", value = "DRAWBRIDGE,DOUBLE_DOORS" },
            expected = new { op = "equals", value = "ROTATION" }
        });

        [Fact]
        public void ValidationType_ReturnsCorrectValue()
        {
            _validator.ValidationType.Should().Be("ConditionalValueMatch");
        }

        [Fact]
        public async Task ValidateAsync_DrawbridgeWithVerticalMotion_IsInvalid()
        {
            var result = await _validator.ValidateAsync(
                fieldValue: "VERTICAL",
                dependencyValue: "DRAWBRIDGE",
                configJson: GateTypeMotionTypeConfig(),
                formContextData: null);

            result.IsValid.Should().BeFalse();
            result.Placeholders.Should().ContainKey("expectedValue").WhoseValue.Should().Be("ROTATION");
            result.Placeholders.Should().ContainKey("actualValue").WhoseValue.Should().Be("VERTICAL");
        }

        [Fact]
        public async Task ValidateAsync_DrawbridgeWithRotationMotion_IsValid()
        {
            var result = await _validator.ValidateAsync(
                fieldValue: "ROTATION",
                dependencyValue: "DRAWBRIDGE",
                configJson: GateTypeMotionTypeConfig(),
                formContextData: null);

            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task ValidateAsync_DoubleDoorsWithLateralMotion_IsInvalid()
        {
            var result = await _validator.ValidateAsync(
                fieldValue: "LATERAL",
                dependencyValue: "DOUBLE_DOORS",
                configJson: GateTypeMotionTypeConfig(),
                formContextData: null);

            result.IsValid.Should().BeFalse();
        }

        [Theory]
        [InlineData("SLIDING", "VERTICAL")]
        [InlineData("SLIDING", "LATERAL")]
        [InlineData("TRAP", "VERTICAL")]
        public async Task ValidateAsync_NonRotationGateType_MotionTypeUnconstrained(string gateType, string motionType)
        {
            var result = await _validator.ValidateAsync(
                fieldValue: motionType,
                dependencyValue: gateType,
                configJson: GateTypeMotionTypeConfig(),
                formContextData: null);

            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task ValidateAsync_ConditionOperatorEquals_OnlyMatchesThatExactValue()
        {
            var configJson = JsonSerializer.Serialize(new
            {
                condition = new { op = "equals", value = "DRAWBRIDGE" },
                expected = new { op = "equals", value = "ROTATION" }
            });

            var doubleDoorsResult = await _validator.ValidateAsync("VERTICAL", "DOUBLE_DOORS", configJson, null);
            doubleDoorsResult.IsValid.Should().BeTrue("the condition only targets DRAWBRIDGE, not DOUBLE_DOORS");

            var drawbridgeResult = await _validator.ValidateAsync("VERTICAL", "DRAWBRIDGE", configJson, null);
            drawbridgeResult.IsValid.Should().BeFalse();
        }

        [Fact]
        public async Task ValidateAsync_MissingConfig_TreatsConditionAsNeverMet()
        {
            // Defaults to op="equals", value=null on both clauses; a null dependency/field value
            // never satisfies "equals" against a null comparand's own guard - see EvaluateOperator's
            // "value == null" short-circuit, which fires for the condition's dependencyValue here.
            var result = await _validator.ValidateAsync("VERTICAL", "DRAWBRIDGE", null, null);

            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task ValidateAsync_MalformedConfigJson_ReturnsInvalidWithErrorMessage()
        {
            var result = await _validator.ValidateAsync("VERTICAL", "DRAWBRIDGE", "{not-valid-json", null);

            result.IsValid.Should().BeFalse();
            result.Message.Should().Contain("Validation error");
        }

        [Fact]
        public async Task ValidateAsync_ExpectedInOperator_AllowsAnyOfSeveralValues()
        {
            var configJson = JsonSerializer.Serialize(new
            {
                condition = new { op = "in", value = "DRAWBRIDGE,DOUBLE_DOORS" },
                expected = new { op = "in", value = "ROTATION,VERTICAL" }
            });

            var result = await _validator.ValidateAsync("VERTICAL", "DRAWBRIDGE", configJson, null);

            result.IsValid.Should().BeTrue();
        }
    }
}
