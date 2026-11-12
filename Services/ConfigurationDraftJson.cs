using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// PandiO/knk-workspace#71: the JSON helpers of the form and display builders' drafts: how a working copy is stored
    /// (<c>DraftJson</c>) and how Duplicate gives a portable file new GUIDs.
    /// </summary>
    public static class ConfigurationDraftJson
    {
        /// <summary>A saved working copy may be at most this long (characters); a real configuration is far smaller.</summary>
        public const int MaxDraftLength = 2_000_000;

        /// <summary>A configuration name may be at most this long (the import's limit).</summary>
        public const int MaxNameLength = 200;

        public static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

        public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

        /// <summary>
        /// Gives the configuration and every step, section and field it defines a new GUID, everywhere the old one
        /// appears in the file (conditions and validation rules refer to fields by GUID). GUIDs it only refers to
        /// (another configuration's field, a sub-configuration) stay as they are.
        /// </summary>
        /// <param name="json">The portable file as JSON.</param>
        /// <param name="containers">Arrays that hold definitions (<c>steps</c>, <c>childFormSteps</c>, <c>fields</c>; <c>sections</c>, <c>subSections</c>).</param>
        /// <param name="guidKeys">A definition's own GUID property (<c>stepGuid</c>, <c>fieldGuid</c>; <c>sectionGuid</c>).</param>
        public static string WithNewGuids(string json, IReadOnlyCollection<string> containers, IReadOnlyCollection<string> guidKeys)
        {
            var root = JsonNode.Parse(json) as JsonObject
                       ?? throw new InvalidOperationException("The configuration could not be read for copying.");

            var own = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (root["configurationGuid"]?.GetValue<string>() is { Length: > 0 } configGuid) own.Add(configGuid);

            void Visit(JsonObject node)
            {
                foreach (var container in containers)
                {
                    if (node[container] is not JsonArray items) continue;
                    foreach (var item in items.OfType<JsonObject>())
                    {
                        foreach (var key in guidKeys)
                        {
                            if (item[key] is JsonValue value && value.TryGetValue<string>(out var guid) && Guid.TryParse(guid, out _))
                                own.Add(guid);
                        }
                        Visit(item);
                    }
                }
            }
            Visit(root);

            var result = json;
            foreach (var guid in own)
            {
                result = result.Replace(guid, Guid.NewGuid().ToString(), StringComparison.OrdinalIgnoreCase);
            }
            return result;
        }

        /// <summary>"Town form" → "Town form (copy)"; a given name is trimmed.</summary>
        public static string CopyName(string? requested, string sourceName)
        {
            var name = string.IsNullOrWhiteSpace(requested) ? $"{sourceName} (copy)" : requested.Trim();
            if (name.Length > MaxNameLength)
                throw new ArgumentException($"The name may be at most {MaxNameLength} characters.");
            return name;
        }
    }
}
