using System;
using System.Collections;
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

                var conditionMet = EvaluateOperator(dependencyValue, config.Condition.Op, config.Condition.Value);
                if (!conditionMet)
                {
                    return await Task.FromResult(new ValidationMethodResult
                    {
                        IsValid = true,
                        Message = "Condition not met; field's value is unconstrained by this rule"
                    });
                }

                var matchesExpected = EvaluateOperator(fieldValue, config.Expected.Op, config.Expected.Value);
                if (!matchesExpected)
                {
                    return await Task.FromResult(new ValidationMethodResult
                    {
                        IsValid = false,
                        Message = "This field must be {expectedValue} when the condition is met, but was {actualValue}",
                        Placeholders = new Dictionary<string, string>
                        {
                            { "expectedValue", ToComparableString(config.Expected.Value) ?? "null" },
                            { "actualValue", ToComparableString(fieldValue) ?? "empty" }
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

        /// <summary>
        /// Same operator vocabulary as ConditionalRequiredValidator.EvaluateCondition, reused for
        /// both the dependency-side condition and the field-side expected-value check. Compares
        /// via string representation (rather than raw object equality) since config values arrive
        /// as JSON-deserialized <see cref="object"/>s - always boxed <see cref="JsonElement"/>s,
        /// never plain CLR strings, because System.Text.Json deserializes an "object"-typed
        /// property this way regardless of the JSON token's actual shape - while runtime field/
        /// dependency values are typically plain CLR strings from form data. See
        /// <see cref="ToComparableString"/>.
        /// </summary>
        private static bool EvaluateOperator(object? value, string op, object? comparand)
        {
            if (value == null || string.IsNullOrEmpty(op))
                return false;

            switch (op.ToLowerInvariant())
            {
                case "equals":
                    return string.Equals(ToComparableString(value), ToComparableString(comparand), StringComparison.OrdinalIgnoreCase);
                case "notequals":
                    return !string.Equals(ToComparableString(value), ToComparableString(comparand), StringComparison.OrdinalIgnoreCase);
                case "greaterthan":
                    return Compare(value, comparand) > 0;
                case "lessthan":
                    return Compare(value, comparand) < 0;
                case "contains":
                    return ToComparableString(value)?.Contains(ToComparableString(comparand) ?? "", StringComparison.OrdinalIgnoreCase) ?? false;
                case "in":
                    return IsInList(value, comparand);
                default:
                    return false;
            }
        }

        private static int Compare(object left, object? right)
        {
            var leftText = ToComparableString(left);
            var rightText = ToComparableString(right);

            if (double.TryParse(leftText, out var leftNum) && double.TryParse(rightText ?? "0", out var rightNum))
            {
                return leftNum.CompareTo(rightNum);
            }

            return string.Compare(leftText, rightText, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Check if value is in a comma-separated list ("DRAWBRIDGE,DOUBLE_DOORS") or a genuine
        /// JSON array (["DRAWBRIDGE","DOUBLE_DOORS"]) - "in" for both the dependency condition
        /// (e.g. GateType) and, less commonly, the expected side (e.g. MotionType allowed to be
        /// any of several values).
        /// </summary>
        private static bool IsInList(object value, object? listValue)
        {
            if (listValue == null)
                return false;

            var stringValue = ToComparableString(value);

            if (listValue is JsonElement { ValueKind: JsonValueKind.Array } arrayElement)
            {
                foreach (var item in arrayElement.EnumerateArray())
                {
                    if (ToComparableString(item)?.Equals(stringValue, StringComparison.OrdinalIgnoreCase) ?? false)
                        return true;
                }
                return false;
            }

            if (listValue is IEnumerable enumerable && listValue is not string)
            {
                foreach (var item in enumerable)
                {
                    if (ToComparableString(item)?.Equals(stringValue, StringComparison.OrdinalIgnoreCase) ?? false)
                        return true;
                }
                return false;
            }

            var listText = ToComparableString(listValue) ?? string.Empty;
            var items = listText.Split(',');
            return Array.Exists(items, item => item.Trim().Equals(stringValue, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Unwraps a JSON-deserialized "object" value to a plain string for comparison, since
        /// System.Text.Json always produces a boxed JsonElement for such a property (a string
        /// JSON token does NOT deserialize to a CLR string here) - JsonElement.ToString() happens
        /// to return the right thing for String/Number/bool kinds already, but being explicit
        /// keeps this correct even if that implementation detail ever changes upstream.
        /// </summary>
        private static string? ToComparableString(object? value)
        {
            if (value is JsonElement element)
            {
                return element.ValueKind switch
                {
                    JsonValueKind.String => element.GetString(),
                    JsonValueKind.Null => null,
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => element.ToString()
                };
            }

            return value?.ToString();
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
