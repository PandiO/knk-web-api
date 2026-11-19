using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Production;
using knkwebapi_v2.Services.Territory;

namespace knkwebapi_v2.Services.ValidationMethods
{
    /// <summary>
    /// <c>InsideTerritory</c> (production DESIGN § 3.4, placement rules P-2 and P-3; P2 PandiO/knk-workspace#91): the
    /// field's region (a region id, or a record with <c>WgRegionId</c>) or location (a record with X and Z) lies inside
    /// the territory of the holder the dependency field names (a Town by default). Asked of the game server through
    /// <see cref="ITerritoryLookupClient"/>, since territory is computed there (territory DESIGN § 1).
    /// <para>
    /// ConfigJson: <c>{ "holderKind": "Town", "holderIdPath": "Id" }</c> (both optional). Seeded as a warning; an admin
    /// can make it blocking (R2). With <c>Production:Enabled</c> off the rule passes without asking (A21). A lookup that
    /// cannot run reports <c>failureReason: TerritoryLookupUnavailable</c>, so a save is not blocked by an offline server.
    /// </para>
    /// </summary>
    public class InsideTerritoryValidator : IValidationMethod
    {
        public const string Type = "InsideTerritory";
        private readonly ITerritoryLookupClient _lookup;
        private readonly IProductionSettingsReader _settings;

        public InsideTerritoryValidator(ITerritoryLookupClient lookup, IProductionSettingsReader settings)
        {
            _lookup = lookup;
            _settings = settings;
        }

        public string ValidationType => Type;

        /// <summary>Territory changes with districts, power and settings, so a stored verdict expires (KNG-269).</summary>
        public bool ReadsExternalState => true;

        public IReadOnlyCollection<string>? DependencyPropertiesRead(string? configJson) =>
            new[] { Config(configJson).HolderIdPath, "Name", "WorldName" };

        public async Task<ValidationMethodResult> ValidateAsync(object? fieldValue, object? dependencyValue, string? configJson,
            Dictionary<string, object>? formContextData)
        {
            if (!(await _settings.GetAsync()).Enabled)
                return new ValidationMethodResult
                {
                    IsValid = true,
                    Message = "Territory check skipped: production is off (game setting production.enabled).",
                    Metadata = new Dictionary<string, object> { { "skipped", "ProductionDisabled" } },
                };

            InsideTerritoryConfig config;
            try
            {
                config = Config(configJson);
            }
            catch (JsonException)
            {
                return Invalid("The rule's configuration is not valid JSON.");
            }
            if (!Enum.TryParse<TerritoryHolderKind>(config.HolderKind, true, out var holderKind) || !Enum.IsDefined(holderKind))
                return Invalid($"holderKind must be Town or Kingdom, not '{config.HolderKind}'.");

            if (fieldValue == null) return Invalid("A region or location is required.");
            if (dependencyValue == null) return Invalid("The town (dependency field) is required.");

            int? holderId = ToInt(dependencyValue is JsonElement { ValueKind: JsonValueKind.Number } n ? n.GetInt32()
                : dependencyValue is int i ? i : Read(dependencyValue, config.HolderIdPath));
            if (holderId is not > 0) return Invalid("Cannot read the holder's id from the dependency field.");
            string holderName = Read(dependencyValue, "Name")?.ToString() ?? $"{holderKind} {holderId}";
            string? world = Read(dependencyValue, "WorldName")?.ToString();

            string? regionId = RegionIdOf(fieldValue);
            (double X, double Z)? position = regionId == null ? PositionOf(fieldValue) : null;
            if (regionId == null && position == null) return Invalid("Cannot read a region or location from the field.");
            world = Read(fieldValue, "World")?.ToString() ?? world;

            bool inside;
            try
            {
                inside = regionId != null
                    ? await _lookup.IsRegionInsideAsync(holderKind, holderId.Value, regionId, world)
                    : await _lookup.IsLocationInsideAsync(holderKind, holderId.Value, position!.Value.X, position.Value.Z, world);
            }
            catch (TerritoryLookupUnavailableException ex)
            {
                return new ValidationMethodResult
                {
                    IsValid = false,
                    Message = $"Cannot check {holderName}'s territory right now: {ex.Message}. Start the Minecraft server, then use \"Re-run validation\".",
                    Metadata = new Dictionary<string, object> { { "failureReason", "TerritoryLookupUnavailable" } },
                    Placeholders = new Dictionary<string, string>(),
                };
            }

            var placeholders = new Dictionary<string, string> { { "townName", holderName }, { "holderName", holderName } };
            return inside
                ? new ValidationMethodResult { IsValid = true, Message = $"Inside {holderName}'s territory.", Placeholders = placeholders }
                : new ValidationMethodResult
                {
                    IsValid = false,
                    Message = regionId != null
                        ? $"The region is not (wholly) inside {holderName}'s territory, or part of it is contested."
                        : $"The location is not inside {holderName}'s territory, or it is contested.",
                    Placeholders = placeholders,
                };
        }

        private static ValidationMethodResult Invalid(string message) =>
            new() { IsValid = false, Message = message, Placeholders = new Dictionary<string, string>() };

        private static InsideTerritoryConfig Config(string? json) =>
            string.IsNullOrWhiteSpace(json)
                ? new InsideTerritoryConfig()
                : JsonSerializer.Deserialize<InsideTerritoryConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new InsideTerritoryConfig();

        private static string? RegionIdOf(object value) => value switch
        {
            string s => string.IsNullOrWhiteSpace(s) ? null : s.Trim(),
            JsonElement { ValueKind: JsonValueKind.String } e => e.GetString() is { Length: > 0 } s ? s.Trim() : null,
            _ => Read(value, "WgRegionId")?.ToString() is { Length: > 0 } r ? r : null,
        };

        private static (double, double)? PositionOf(object value)
        {
            var x = ToDouble(Read(value, "X"));
            var z = ToDouble(Read(value, "Z"));
            return x.HasValue && z.HasValue ? (x.Value, z.Value) : null;
        }

        private static int? ToInt(object? v) => v switch
        {
            null => null,
            int i => i,
            long l => (int)l,
            JsonElement { ValueKind: JsonValueKind.Number } e => e.GetInt32(),
            JsonElement { ValueKind: JsonValueKind.String } e => int.TryParse(e.GetString(), out var p) ? p : null,
            string s => int.TryParse(s, out var p) ? p : null,
            _ => null,
        };

        private static double? ToDouble(object? v) => v switch
        {
            null => null,
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            decimal m => (double)m,
            JsonElement { ValueKind: JsonValueKind.Number } e => e.GetDouble(),
            string s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : null,
            _ => null,
        };

        /// <summary>A property of a record, a dictionary or a JSON object (case-insensitive); null when missing.</summary>
        private static object? Read(object? obj, string name)
        {
            switch (obj)
            {
                case null:
                    return null;
                case JsonElement { ValueKind: JsonValueKind.Object } e:
                    foreach (var p in e.EnumerateObject())
                        if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                            return p.Value.ValueKind switch
                            {
                                JsonValueKind.String => p.Value.GetString(),
                                JsonValueKind.Number => p.Value.TryGetInt32(out var i) ? (object)i : p.Value.GetDouble(),
                                JsonValueKind.Null => null,
                                _ => p.Value,
                            };
                    return null;
                case JsonElement:
                    return null;
                case IDictionary<string, object> d:
                    var key = d.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
                    return key == null ? null : d[key] is JsonElement je ? Unwrap(je) : d[key];
                case string:
                    return null;
                default:
                    if (obj.GetType().IsPrimitive) return null;
                    return obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?.GetValue(obj);
            }
        }

        private static object? Unwrap(JsonElement e) => e.ValueKind switch
        {
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Number => e.TryGetInt32(out var i) ? (object)i : e.GetDouble(),
            JsonValueKind.Null => null,
            _ => e,
        };
    }

    public class InsideTerritoryConfig
    {
        /// <summary>"Town" (default) or "Kingdom".</summary>
        public string HolderKind { get; set; } = "Town";

        /// <summary>The dependency record's property that holds the holder's id.</summary>
        public string HolderIdPath { get; set; } = "Id";
    }
}
