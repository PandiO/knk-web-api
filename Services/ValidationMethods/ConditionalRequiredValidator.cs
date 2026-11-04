using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services.ValidationMethods
{
    /// <summary>
    /// Validates that a field is required only when a condition is met on a dependency field.
    ///
    /// ConfigJson schema (the same "condition" clause as <see cref="ConditionalValueMatchValidator"/>,
    /// developer decision 2026-11-04 on PandiO/knk-workspace#99):
    /// {
    ///   "condition": {
    ///     "op": "equals",          // "equals", "notEquals", "greaterThan", "lessThan", "contains", "in"
    ///     "value": "complex",      // value to compare the dependency against ("in": comma-separated or an array)
    ///     "property": "Name"       // optional: which property of a picked record to compare (default "Id")
    ///   }
    /// }
    /// Names are case-insensitive. Configs saved before that decision still work: "operator" is read
    /// as "op", and a flat { "op"/"operator", "value" } without "condition" is read as the condition.
    ///
    /// Values are compared by text (<see cref="ConditionOperators"/>), so a config's <c>true</c>
    /// matches a checkbox's "true" and <c>5</c> matches "5". When the dependency is an Object field,
    /// <c>ValidationService</c> passes the saved record; its "property" (default "Id") is compared.
    ///
    /// Example: a "description" field becomes required when the user selects status = "complex":
    /// { "condition": { "op": "equals", "value": "complex" } }
    ///
    /// Logic:
    /// - If dependency value matches condition: field MUST be filled
    /// - If dependency value doesn't match condition: field can be empty
    /// </summary>
    public class ConditionalRequiredValidator : IValidationMethod
    {
        private const string DefaultRecordProperty = "Id";

        public string ValidationType => "ConditionalRequired";

        public async Task<ValidationMethodResult> ValidateAsync(
            object? fieldValue,
            object? dependencyValue,
            string? configJson,
            Dictionary<string, object>? formContextData)
        {
            try
            {
                var config = ConditionalRequiredConfig.Parse(configJson);

                var compared = ConditionOperators.IsScalar(dependencyValue)
                    ? dependencyValue
                    : ConditionOperators.ReadProperty(dependencyValue, config.Property ?? DefaultRecordProperty);

                // If condition NOT met: validation passes (field doesn't need to be filled)
                if (!ConditionOperators.Evaluate(compared, config.Op, config.Value))
                {
                    return await Task.FromResult(new ValidationMethodResult
                    {
                        IsValid = true,
                        Message = "Condition not met; field is optional"
                    });
                }

                // Condition IS met: field MUST be filled
                if (IsValueEmpty(fieldValue))
                {
                    return await Task.FromResult(new ValidationMethodResult
                    {
                        IsValid = false,
                        Message = "This field is required when the condition is met",
                        Placeholders = new Dictionary<string, string>
                        {
                            { "operator", config.Op },
                            { "conditionValue", ConditionOperators.ToComparableString(config.Value) ?? "null" }
                        }
                    });
                }

                // Field is filled and condition is met: validation passes
                return await Task.FromResult(new ValidationMethodResult
                {
                    IsValid = true,
                    Message = "Field is properly filled based on condition"
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
        /// Check if a value is considered "empty" for validation purposes: null, blank text, a JSON
        /// null or blank string, or 0 (an unpicked id).
        /// </summary>
        private static bool IsValueEmpty(object? value)
        {
            if (value is JsonElement element)
            {
                return element.ValueKind switch
                {
                    JsonValueKind.Null or JsonValueKind.Undefined => true,
                    JsonValueKind.String => string.IsNullOrWhiteSpace(element.GetString()),
                    JsonValueKind.Number => element.TryGetInt64(out var number) && number == 0,
                    _ => false
                };
            }

            return value == null
                || (value is string str && string.IsNullOrWhiteSpace(str))
                || (value is int intVal && intVal == 0)
                || (value is long longVal && longVal == 0);
        }
    }

    /// <summary>
    /// Configuration for ConditionalRequiredValidator, read from any of the shapes described there.
    /// </summary>
    public class ConditionalRequiredConfig
    {
        /// <summary>
        /// Comparison operator.
        /// Valid values: "equals", "notEquals", "greaterThan", "lessThan", "contains", "in"
        /// </summary>
        public string Op { get; set; } = "equals";

        /// <summary>
        /// Value to compare against (a <see cref="JsonElement"/> when parsed from ConfigJson).
        /// For "in", comma-separated ("value1,value2") or an array.
        /// </summary>
        public object? Value { get; set; }

        /// <summary>The property of a picked record to compare; null = "Id".</summary>
        public string? Property { get; set; }

        public static ConditionalRequiredConfig Parse(string? configJson)
        {
            var config = new ConditionalRequiredConfig();
            if (string.IsNullOrWhiteSpace(configJson))
                return config;

            using var document = JsonDocument.Parse(configJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return config;

            var clause = Member(root, "condition") is { ValueKind: JsonValueKind.Object } nested ? nested : root;

            var op = Member(clause, "op") ?? Member(clause, "operator");
            if (op is { ValueKind: JsonValueKind.String } opText && !string.IsNullOrWhiteSpace(opText.GetString()))
                config.Op = opText.GetString()!;

            // Clone: the document is disposed when this method returns.
            if (Member(clause, "value") is { } value)
                config.Value = value.Clone();

            if (Member(clause, "property") is { ValueKind: JsonValueKind.String } property && !string.IsNullOrWhiteSpace(property.GetString()))
                config.Property = property.GetString();

            return config;
        }

        private static JsonElement? Member(JsonElement obj, string name)
        {
            foreach (var member in obj.EnumerateObject())
            {
                if (string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase))
                    return member.Value;
            }
            return null;
        }
    }
}
