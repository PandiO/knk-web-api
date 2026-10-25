using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services.ValidationResultLog
{
    /// <summary>
    /// KNG-269: the two hashes a <see cref="FieldValidationRunLog"/> row is matched on. Both are SHA-256
    /// over canonical JSON: object keys lower-cased and sorted, no whitespace, so the same value hashes the
    /// same whether it came from the form (camelCase JSON) or a loaded record (PascalCase properties).
    /// </summary>
    public static class ValidationInputHasher
    {
        private static readonly JsonSerializerOptions ValueOptions = new()
        {
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            MaxDepth = 32
        };

        /// <summary>
        /// Everything that decides a rule's outcome or the message it produces. A changed message is in
        /// it on purpose: the stored row carries the message as produced, so an edited text must re-run
        /// (once per subject, which is cheap).
        /// </summary>
        public static string RuleHash(FieldValidationRule rule)
        {
            var parts = new Dictionary<string, object?>
            {
                ["validationType"] = rule.ValidationType,
                ["configJson"] = CanonicalOrRaw(rule.ConfigJson),
                ["dependsOnFieldId"] = rule.DependsOnFieldId,
                ["dependencyPath"] = rule.DependencyPath,
                ["requiresDependencyFilled"] = rule.RequiresDependencyFilled,
                ["isBlocking"] = rule.IsBlocking,
                ["errorMessage"] = rule.ErrorMessage,
                ["successMessage"] = rule.SuccessMessage
            };
            return Sha256(Canonical(parts));
        }

        /// <summary>The field value and the (reduced) dependency the validator reads.</summary>
        public static string InputHash(object? fieldValue, object? dependency) =>
            Sha256(Canonical(new Dictionary<string, object?> { ["field"] = fieldValue, ["dependency"] = dependency }));

        /// <summary>
        /// What a validator reads off the resolved dependency record: the properties it names, by
        /// (dotted, case-insensitive) path. Null paths = the whole value. A loaded entity reduced this
        /// way doesn't drag its navigation collections into the hash, so a Town gaining a District
        /// doesn't invalidate a location check against the Town's region.
        /// </summary>
        public static object? ReduceDependency(object? dependency, IReadOnlyCollection<string>? propertyPaths)
        {
            if (dependency == null || propertyPaths == null) return dependency;
            var reduced = new Dictionary<string, object?>();
            foreach (var path in propertyPaths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
                reduced[path] = ExtractPropertyValue(dependency, path);
            return reduced;
        }

        /// <summary>"Town:3" for a dependency that references a saved record of the rule's dependency field type; else null.</summary>
        public static string? DependencyKey(FieldValidationRule rule, object? formDependencyValue)
        {
            var objectType = rule.DependsOnField?.ObjectType;
            if (string.IsNullOrWhiteSpace(objectType) || formDependencyValue == null) return null;
            var id = ReferencedId(formDependencyValue);
            return id == null ? null : $"{objectType}:{id}";
        }

        /// <summary>Canonical JSON text of any value (JsonElement, dictionary, POCO, scalar).</summary>
        public static string Canonical(object? value)
        {
            var element = value is JsonElement e ? e : JsonSerializer.SerializeToElement(value, ValueOptions);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                WriteCanonical(writer, element);
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static object? CanonicalOrRaw(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                return Canonical(doc.RootElement.Clone());
            }
            catch (JsonException)
            {
                return json;
            }
        }

        private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (var property in element.EnumerateObject()
                                 .Select(p => (Name: p.Name.ToLowerInvariant(), p.Value))
                                 .OrderBy(p => p.Name, StringComparer.Ordinal))
                    {
                        writer.WritePropertyName(property.Name);
                        WriteCanonical(writer, property.Value);
                    }
                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (var item in element.EnumerateArray()) WriteCanonical(writer, item);
                    writer.WriteEndArray();
                    break;
                case JsonValueKind.Number:
                    // 1 and 1.0 are the same input to a validator (a double writes without a trailing .0).
                    if (element.TryGetInt64(out var integer)) writer.WriteNumberValue(integer);
                    else if (element.TryGetDouble(out var number)) writer.WriteNumberValue(number);
                    else writer.WriteRawValue(element.GetRawText());
                    break;
                case JsonValueKind.String:
                    writer.WriteStringValue(element.GetString());
                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    writer.WriteBooleanValue(element.GetBoolean());
                    break;
                default:
                    writer.WriteNullValue();
                    break;
            }
        }

        /// <summary>A dotted, case-insensitive path into a JsonElement, dictionary or object; null when any step is missing.</summary>
        public static object? ExtractPropertyValue(object? value, string path)
        {
            object? current = value;
            foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (current == null) return null;
                current = Member(current, segment);
            }
            return current;
        }

        private static object? Member(object value, string name)
        {
            switch (value)
            {
                case JsonElement element:
                    if (element.ValueKind != JsonValueKind.Object) return null;
                    foreach (var property in element.EnumerateObject())
                        if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return property.Value;
                    return null;
                case IDictionary dictionary:
                    foreach (var key in dictionary.Keys)
                        if (key is string text && text.Equals(name, StringComparison.OrdinalIgnoreCase)) return dictionary[key];
                    return null;
                default:
                    var member = value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                    return member != null && member.GetIndexParameters().Length == 0 ? member.GetValue(value) : null;
            }
        }

        /// <summary>
        /// The id of a form value that references a record: a bare id, or form JSON / a dictionary with
        /// an "id". Anything else (e.g. an entity object) has none.
        /// </summary>
        public static object? ReferencedId(object value)
        {
            switch (value)
            {
                case int or long:
                    return value;
                case string text:
                    return long.TryParse(text, out var parsed) ? parsed : null;
                case JsonElement { ValueKind: JsonValueKind.Number } number:
                    return number.TryGetInt64(out var n) ? n : null;
                case JsonElement { ValueKind: JsonValueKind.String } str:
                    return long.TryParse(str.GetString(), out var s) ? s : null;
                case JsonElement { ValueKind: JsonValueKind.Object } obj:
                    foreach (var property in obj.EnumerateObject())
                        if (property.Name.Equals("id", StringComparison.OrdinalIgnoreCase))
                            return ReferencedId(property.Value);
                    return null;
                case IDictionary<string, object?> dictionary:
                    var key = dictionary.Keys.FirstOrDefault(k => k.Equals("id", StringComparison.OrdinalIgnoreCase));
                    return key != null && dictionary[key] is { } idValue ? ReferencedId(idValue) : null;
                default:
                    return null;
            }
        }

        private static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
