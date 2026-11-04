using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services.ValidationMethods
{
    /// <summary>
    /// Validates that a field's own value matches an expected value (or one of several) whenever
    /// a condition on a dependency field is met - e.g. GateStructure.MotionType must equal
    /// "ROTATION" whenever GateType is "DRAWBRIDGE" or "DOUBLE_DOORS", instead of silently
    /// accepting whatever motion type a form happens to submit. This is the cross-field
    /// consistency gap flagged in docs/features/gate-structure-animation/
    /// ROTATION_GAP_FILL_DESIGN.md, Phase D ("a DRAWBRIDGE should default-suggest ROTATION, not
    /// silently accept VERTICAL") - the same class of bug as an earlier MotionType-defaulting
    /// incident in that work.
    ///
    /// Complements <see cref="ConditionalRequiredValidator"/>, which only checks a field's
    /// presence/absence based on a condition, never its actual value.
    ///
    /// ConfigJson schema:
    /// {
    ///   "condition": { "op": "in", "value": "DRAWBRIDGE,DOUBLE_DOORS" },
    ///   "expected":  { "op": "equals", "value": "ROTATION" }
    /// }
    ///
    /// - "condition" is evaluated against the dependency field's value (e.g. GateType). Same
    ///   operator vocabulary as ConditionalRequiredValidator: "equals", "notEquals",
    ///   "greaterThan", "lessThan", "contains", "in" (comma-separated list or array).
    /// - If the condition is NOT met: validation passes unconditionally - the field being
    ///   validated (e.g. MotionType) is unconstrained by this rule for that dependency value.
    /// - If the condition IS met: the field's own value is checked against "expected" using the
    ///   same operator vocabulary - typically "equals" (exactly one allowed value) or "in" (any
    ///   of several).
    /// </summary>
    public class ConditionalValueMatchValidator : IValidationMethod
    {
        // Case-insensitive: ConfigJson is admin-authored (via the FormConfigBuilder UI), and
        // "condition"/"expected"/"op"/"value" (camelCase, matching every other JSON payload in
        // this API) should deserialize onto this class's PascalCase properties without requiring
        // the admin to know or match C#'s exact casing.
        private static readonly JsonSerializerOptions ConfigJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public string ValidationType => "ConditionalValueMatch";

        public async Task<ValidationMethodResult> ValidateAsync(
            object? fieldValue,
            object? dependencyValue,
            string? configJson,
            Dictionary<string, object>? formContextData)
        {
            try
            {
                var config = string.IsNullOrEmpty(configJson)
                    ? new ConditionalValueMatchConfig()
                    : JsonSerializer.Deserialize<ConditionalValueMatchConfig>(configJson, ConfigJsonOptions)
                        ?? new ConditionalValueMatchConfig();

                var conditionMet = ConditionOperators.Evaluate(dependencyValue, config.Condition.Op, config.Condition.Value);
                if (!conditionMet)
                {
                    return await Task.FromResult(new ValidationMethodResult
                    {
                        IsValid = true,
                        Message = "Condition not met; field's value is unconstrained by this rule"
                    });
                }

                var matchesExpected = ConditionOperators.Evaluate(fieldValue, config.Expected.Op, config.Expected.Value);
                if (!matchesExpected)
                {
                    return await Task.FromResult(new ValidationMethodResult
                    {
                        IsValid = false,
                        Message = "This field must be {expectedValue} when the condition is met, but was {actualValue}",
                        Placeholders = new Dictionary<string, string>
                        {
                            { "expectedValue", ConditionOperators.ToComparableString(config.Expected.Value) ?? "null" },
                            { "actualValue", ConditionOperators.ToComparableString(fieldValue) ?? "empty" }
                        }
                    });
                }

                return await Task.FromResult(new ValidationMethodResult
                {
                    IsValid = true,
                    Message = "Field value matches the expected value for the current condition"
                });
            }
            catch (Exception ex)
            {
                return new ValidationMethodResult
                {
                    IsValid = false,
                    Message = $"Validation error: {ex.Message}"
                };
            }
        }
    }

    /// <summary>
    /// Configuration for ConditionalValueMatchValidator.
    /// </summary>
    public class ConditionalValueMatchConfig
    {
        public ConditionalValueMatchClause Condition { get; set; } = new();
        public ConditionalValueMatchClause Expected { get; set; } = new();
    }

    public class ConditionalValueMatchClause
    {
        /// <summary>
        /// Comparison operator. Valid values: "equals", "notEquals", "greaterThan", "lessThan",
        /// "contains", "in".
        /// </summary>
        public string Op { get; set; } = "equals";

        /// <summary>
        /// Value to compare against. For the "in" operator, can be comma-separated:
        /// "value1,value2,value3".
        /// </summary>
        public object? Value { get; set; }
    }
}
