using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Production;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.ValidationMethods
{
    /// <summary>
    /// <c>ProductionAreaPlacement</c>: placement rule P-3 (production DESIGN § 3.4, R2; PandiO/knk-workspace#883). A
    /// production area stays inside its structure's district (the district's WorldGuard region, as P-1) or, for a rural
    /// structure, inside its town's territory (as P-2). The rule sits on the area form's structure field; the area's
    /// shape comes from the other form values:
    /// <list type="number">
    ///   <item>Region: the whole region (<c>WgRegionId</c>) is inside the district's region, or the town's territory.</item>
    ///   <item>Cuboid: its four corners (<c>MinX</c>/<c>MaxX</c> × <c>MinZ</c>/<c>MaxZ</c>) are inside.</item>
    ///   <item>Radius: the centre and the four points one radius away along x and z are inside.</item>
    ///   <item>Polygon: not checked here (the form holds no points); it passes with a note.</item>
    /// </list>
    /// Seeded as a warning; an admin can make it blocking. The territory half passes without a lookup while
    /// <c>Production:Enabled</c> is off (A21), like <see cref="InsideTerritoryValidator"/>. A game server that cannot be
    /// asked reports a <c>failureReason</c>, so a save is not blocked by an offline server.
    /// </summary>
    public class ProductionAreaPlacementValidator : IValidationMethod
    {
        public const string Type = "ProductionAreaPlacement";

        private readonly KnKDbContext _db;
        private readonly IRegionService _regions;
        private readonly ITerritoryLookupClient _territory;
        private readonly IProductionSettingsReader _settings;

        public ProductionAreaPlacementValidator(KnKDbContext db, IRegionService regions, ITerritoryLookupClient territory,
            IProductionSettingsReader settings)
        {
            _db = db;
            _regions = regions;
            _territory = territory;
            _settings = settings;
        }

        public string ValidationType => Type;

        /// <summary>Regions and territory change outside the form, so a stored verdict expires (KNG-269).</summary>
        public bool ReadsExternalState => true;

        public async Task<ValidationMethodResult> ValidateAsync(object? fieldValue, object? dependencyValue, string? configJson,
            Dictionary<string, object>? formContextData)
        {
            var structureId = IdOf(fieldValue);
            if (structureId is not > 0) return Invalid("Choose the production structure first.");
            var s = await _db.ProductionStructures.AsNoTracking().Where(x => x.Id == structureId)
                .Select(x => new
                {
                    x.Name,
                    DistrictRegion = x.District != null ? x.District.WgRegionId : null,
                    DistrictName = x.District != null ? x.District.Name : null,
                    x.TownId,
                    TownName = x.Town != null ? x.Town.Name : null,
                    x.WorldName,
                }).FirstOrDefaultAsync();
            if (s == null) return Invalid($"Production structure {structureId} not found.");

            var ctx = formContextData ?? new Dictionary<string, object>();
            string? world = Text(ctx, "WorldName") ?? s.WorldName;
            string? region = Text(ctx, "WgRegionId");
            string shape = Text(ctx, "Shape") ?? (region != null ? "Region" : "");
            var points = region == null ? Points(shape, ctx) : null;
            if (region == null && points == null)
                return Valid($"P-3 not checked: a {(shape.Length == 0 ? "shape" : shape)} area without a region or bounds is not checked here.");

            bool inDistrict = s.DistrictRegion != null;
            string holder = inDistrict ? s.DistrictName ?? "the district" : s.TownName ?? $"town {s.TownId}";
            var placeholders = new Dictionary<string, string> { { "holderName", holder }, { "structureName", s.Name ?? "" } };
            bool inside;
            try
            {
                if (inDistrict)
                {
                    inside = region != null
                        ? await _regions.IsRegionContainedAsync(s.DistrictRegion!, region, true, world)
                        : await AllAsync(points!, p => _regions.IsLocationInsideRegionAsync(s.DistrictRegion!, p.X, p.Z, true, world));
                }
                else if (s.TownId is { } townId)
                {
                    if (!(await _settings.GetAsync()).Enabled)
                        return new ValidationMethodResult
                        {
                            IsValid = true,
                            Message = "Territory check skipped: production is off (game setting production.enabled).",
                            Metadata = new Dictionary<string, object> { { "skipped", "ProductionDisabled" } },
                        };
                    inside = region != null
                        ? await _territory.IsRegionInsideAsync(TerritoryHolderKind.Town, townId, region, world)
                        : await AllAsync(points!, p => _territory.IsLocationInsideAsync(TerritoryHolderKind.Town, townId, p.X, p.Z, world));
                }
                else
                {
                    return Invalid("The structure has neither a district nor a town.");
                }
            }
            catch (RegionServiceUnavailableException ex)
            {
                return CouldNotRun("PluginUnreachable", $"Cannot check {holder}'s region right now: {ex.Message}. Start the Minecraft server, then use \"Re-run validation\".");
            }
            catch (TerritoryLookupUnavailableException ex)
            {
                return CouldNotRun("TerritoryLookupUnavailable", $"Cannot check {holder}'s territory right now: {ex.Message}. Start the Minecraft server, then use \"Re-run validation\".");
            }

            return inside
                ? new ValidationMethodResult { IsValid = true, Message = $"Inside {holder}.", Placeholders = placeholders }
                : new ValidationMethodResult
                {
                    IsValid = false,
                    Message = inDistrict
                        ? $"The area is not completely inside {holder}'s region."
                        : $"The area is not (wholly) inside {holder}'s territory, or part of it is contested.",
                    Placeholders = placeholders,
                };
        }

        private static async Task<bool> AllAsync(IEnumerable<(double X, double Z)> points, Func<(double X, double Z), Task<bool>> check)
        {
            foreach (var p in points)
                if (!await check(p)) return false;
            return true;
        }

        /// <summary>The points that stand for a cuboid or radius area; null when the values are missing.</summary>
        internal static List<(double X, double Z)>? Points(string shape, Dictionary<string, object> ctx)
        {
            if (string.Equals(shape, "Radius", StringComparison.OrdinalIgnoreCase))
            {
                if (Number(ctx, "CenterX") is not { } cx || Number(ctx, "CenterZ") is not { } cz || Number(ctx, "Radius") is not { } r) return null;
                return new() { (cx, cz), (cx - r, cz), (cx + r, cz), (cx, cz - r), (cx, cz + r) };
            }
            if (string.Equals(shape, "Cuboid", StringComparison.OrdinalIgnoreCase))
            {
                if (Number(ctx, "MinX") is not { } x1 || Number(ctx, "MinZ") is not { } z1
                    || Number(ctx, "MaxX") is not { } x2 || Number(ctx, "MaxZ") is not { } z2) return null;
                return new() { (x1, z1), (x1, z2), (x2, z1), (x2, z2) };
            }
            return null;
        }

        private static ValidationMethodResult Valid(string message) =>
            new() { IsValid = true, Message = message, Placeholders = new Dictionary<string, string>() };

        private static ValidationMethodResult Invalid(string message) =>
            new() { IsValid = false, Message = message, Placeholders = new Dictionary<string, string>() };

        private static ValidationMethodResult CouldNotRun(string reason, string message) => new()
        {
            IsValid = false,
            Message = message,
            Metadata = new Dictionary<string, object> { { "failureReason", reason } },
            Placeholders = new Dictionary<string, string>(),
        };

        private static int? IdOf(object? v) => v switch
        {
            null => null,
            int i => i,
            long l => (int)l,
            string t => int.TryParse(t, out var p) ? p : null,
            JsonElement { ValueKind: JsonValueKind.Number } e => e.TryGetInt32(out var n) ? n : null,
            JsonElement { ValueKind: JsonValueKind.String } e => int.TryParse(e.GetString(), out var p) ? p : null,
            JsonElement { ValueKind: JsonValueKind.Object } e => e.EnumerateObject()
                .Where(p => string.Equals(p.Name, "id", StringComparison.OrdinalIgnoreCase)).Select(p => IdOf(p.Value)).FirstOrDefault(),
            IDictionary<string, object> d => d.Where(kv => string.Equals(kv.Key, "id", StringComparison.OrdinalIgnoreCase))
                .Select(kv => IdOf(kv.Value)).FirstOrDefault(),
            _ => v.GetType().IsPrimitive ? null
                : IdOf(v.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?.GetValue(v)),
        };

        private static object? Get(Dictionary<string, object> ctx, string name) =>
            ctx.FirstOrDefault(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

        private static string? Text(Dictionary<string, object> ctx, string name) => Get(ctx, name) switch
        {
            null => null,
            string s => string.IsNullOrWhiteSpace(s) ? null : s.Trim(),
            JsonElement { ValueKind: JsonValueKind.String } e => string.IsNullOrWhiteSpace(e.GetString()) ? null : e.GetString()!.Trim(),
            JsonElement => null,
            var o => o.ToString(),
        };

        private static double? Number(Dictionary<string, object> ctx, string name) => Get(ctx, name) switch
        {
            null => null,
            int i => i,
            long l => l,
            double d => d,
            decimal m => (double)m,
            JsonElement { ValueKind: JsonValueKind.Number } e => e.GetDouble(),
            JsonElement { ValueKind: JsonValueKind.String } e => double.TryParse(e.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : null,
            string s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : null,
            _ => null,
        };
    }
}
