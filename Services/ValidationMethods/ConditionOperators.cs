using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace knkwebapi_v2.Services.ValidationMethods
{
    /// <summary>
    /// The condition vocabulary shared by <see cref="ConditionalRequiredValidator"/> and
    /// <see cref="ConditionalValueMatchValidator"/>: "equals", "notEquals", "greaterThan", "lessThan",
    /// "contains", "in" (comma-separated list or array), all case-insensitive.
    ///
    /// Values are compared by their text, not by object equality: ConfigJson values arrive as boxed
    /// <see cref="JsonElement"/>s (System.Text.Json does that for an <see cref="object"/> property,
    /// whatever the token), while form values arrive as strings, numbers or JSON. So the config's
    /// <c>true</c> matches a form's <c>"true"</c> or <c>True</c>, and <c>5</c> matches <c>"5"</c>
    /// (PandiO/knk-workspace#99).
    /// </summary>
    public static class ConditionOperators
    {
        public static bool Evaluate(object? value, string? op, object? comparand)
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

        /// <summary>
        /// A value as comparable text: a JSON string unwrapped, booleans as "true"/"false", numbers in
        /// the invariant culture (so "1.5" never becomes "1,5" on a Dutch server).
        /// </summary>
        public static string? ToComparableString(object? value)
        {
            switch (value)
            {
                case null:
                    return null;
                case JsonElement element:
                    return element.ValueKind switch
                    {
                        JsonValueKind.String => element.GetString(),
                        JsonValueKind.Null or JsonValueKind.Undefined => null,
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => element.ToString()
                    };
                case bool b:
                    return b ? "true" : "false";
                case IFormattable formattable:
                    return formattable.ToString(null, CultureInfo.InvariantCulture);
                default:
                    return value.ToString();
            }
        }

        /// <summary>
        /// True for a value that is compared as it is; false for a record (an entity, a dictionary,
        /// a JSON object) whose property has to be picked first.
        /// </summary>
        public static bool IsScalar(object? value) => value switch
        {
            null => true,
            JsonElement element => element.ValueKind != JsonValueKind.Object,
            string or bool or char or Enum or Guid or DateTime or DateTimeOffset or TimeSpan => true,
            _ => value.GetType().IsPrimitive || value is decimal
        };

        /// <summary>
        /// Reads a property off a record by a dotted, case-insensitive path ("Id", "Town.Name"): CLR
        /// objects through reflection, dictionaries by key, JSON objects by member name. Null when a
        /// segment is missing.
        /// </summary>
        public static object? ReadProperty(object? record, string path)
        {
            var current = record;
            foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                current = current switch
                {
                    null => null,
                    JsonElement { ValueKind: JsonValueKind.Object } obj => JsonMember(obj, segment),
                    IDictionary<string, object?> dict => DictionaryValue(dict, segment),
                    _ => current.GetType()
                        .GetProperty(segment, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                        ?.GetValue(current)
                };
            }
            return current;
        }

        private static object? JsonMember(JsonElement obj, string name)
        {
            foreach (var member in obj.EnumerateObject())
            {
                if (string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase))
                    return member.Value;
            }
            return null;
        }

        private static object? DictionaryValue(IEnumerable<KeyValuePair<string, object?>> dict, string key)
            => dict.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

        private static int Compare(object left, object? right)
        {
            var leftText = ToComparableString(left);
            var rightText = ToComparableString(right);

            if (double.TryParse(leftText, NumberStyles.Float, CultureInfo.InvariantCulture, out var leftNum)
                && double.TryParse(rightText ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture, out var rightNum))
            {
                return leftNum.CompareTo(rightNum);
            }

            return string.Compare(leftText, rightText, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Check if value is in a comma-separated list ("DRAWBRIDGE,DOUBLE_DOORS") or a genuine
        /// JSON array (["DRAWBRIDGE","DOUBLE_DOORS"]).
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
    }
}
