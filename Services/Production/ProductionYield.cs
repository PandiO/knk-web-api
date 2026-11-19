using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Production
{
    // Yield areas and surveys P4 (PandiO/knk-workspace#138, PRODUCTION_KINDS.md § 2-4): the area geometry, the yield
    // calculation from the latest surveys, the Derived and Hybrid base-rate providers, the primary and variation
    // modifier sources, and the materialization of outputs into production lines. Validation errors are
    // ArgumentException (400), missing rows KeyNotFoundException (404), conflicts InvalidOperationException (409).

    /// <summary>Bounding boxes, sizes and overlaps of production areas (PRODUCTION_KINDS § 3.1, G6).</summary>
    public static class AreaGeometry
    {
        public const int MinCoordinateY = -2048;
        public const int MaxCoordinateY = 2048;

        /// <summary>"blocks" (bordered fields, pastures), "volume" (exclusive volumes) or "radius" (shared radius, hunting grounds).</summary>
        public static string SizeUnit(ProductionAreaModel model) => model switch
        {
            ProductionAreaModel.ExclusiveVolume => "volume",
            ProductionAreaModel.SharedRadius or ProductionAreaModel.HuntingGround => "radius",
            _ => "blocks",
        };

        /// <summary>What the level's <c>MaxAreaSize</c> caps, by the model: footprint blocks, volume or radius.</summary>
        public static long Size(ProductionArea a, IReadOnlyList<AreaPointDto>? points)
        {
            long dx = (long)a.MaxX - a.MinX + 1, dy = (long)a.MaxY - a.MinY + 1, dz = (long)a.MaxZ - a.MinZ + 1;
            long footprint = a.Shape switch
            {
                ProductionAreaShape.Polygon when points is { Count: >= 3 } => PolygonBlocks(points),
                ProductionAreaShape.Radius when a.Radius.HasValue => (long)Math.Ceiling(Math.PI * a.Radius.Value * a.Radius.Value),
                _ => dx * dz,
            };
            return SizeUnit(a.Model) switch
            {
                "volume" => footprint * dy,
                "radius" => a.Shape == ProductionAreaShape.Radius && a.Radius.HasValue
                    ? a.Radius.Value
                    : (long)Math.Ceiling(Math.Max(dx, dz) / 2.0),
                _ => footprint,
            };
        }

        /// <summary>The shoelace area of the polygon through the points, counted in whole blocks (at least 1).</summary>
        public static long PolygonBlocks(IReadOnlyList<AreaPointDto> p)
        {
            long twice = 0;
            for (int i = 0; i < p.Count; i++)
            {
                var a = p[i];
                var b = p[(i + 1) % p.Count];
                twice += (long)a.X * b.Z - (long)b.X * a.Z;
            }
            return Math.Max(1, (long)Math.Ceiling(Math.Abs(twice) / 2.0));
        }

        public static bool Intersects(ProductionArea a, ProductionArea b) =>
            string.Equals(a.WorldName, b.WorldName, StringComparison.OrdinalIgnoreCase)
            && a.MinX <= b.MaxX && b.MinX <= a.MaxX
            && a.MinY <= b.MaxY && b.MinY <= a.MaxY
            && a.MinZ <= b.MaxZ && b.MinZ <= a.MaxZ;

        public static List<AreaPointDto>? Points(string? json) =>
            string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<List<AreaPointDto>>(json);
    }

    /// <summary>Survey unit keys: upper-case segments of letters, digits and underscores, joined by colons
    /// (<c>TREE:OAK:LARGE</c>). A yield row may end in <c>*</c> to match a prefix.</summary>
    public static class SurveyUnitKeys
    {
        private static readonly Regex Key = new("^[A-Z0-9_]+(:[A-Z0-9_]+)*$", RegexOptions.Compiled);
        private static readonly Regex Pattern = new(@"^([A-Z0-9_]+(:[A-Z0-9_]+)*(:\*)?|\*)$", RegexOptions.Compiled);

        public static string NormalizeKey(string? value, string field)
        {
            var key = value?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(key) || key.Length > AreaSurveyUnit.UnitKeyMaxLength || !Key.IsMatch(key))
                throw new ArgumentException($"{field} must look like TREE:OAK:LARGE (letters, digits, _ and :, at most {AreaSurveyUnit.UnitKeyMaxLength} characters).");
            return key;
        }

        public static string NormalizePattern(string? value, string field)
        {
            var key = value?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(key) || key.Length > AreaSurveyUnit.UnitKeyMaxLength || !Pattern.IsMatch(key))
                throw new ArgumentException($"{field} must be a unit key such as BLOCK:COAL_ORE or a prefix such as TREE:OAK:* (at most {AreaSurveyUnit.UnitKeyMaxLength} characters).");
            return key;
        }

        /// <summary>How specifically <paramref name="pattern"/> matches <paramref name="key"/>: -1 = no match, else higher is
        /// more specific (an exact key beats every prefix, a longer prefix beats a shorter one).</summary>
        public static int Match(string pattern, string key)
        {
            if (pattern == key) return int.MaxValue;
            if (pattern == "*") return 0;
            if (pattern.EndsWith(":*"))
            {
                var prefix = pattern[..^1]; // keeps the colon: "TREE:OAK:"
                return key.StartsWith(prefix, StringComparison.Ordinal) ? prefix.Length : -1;
            }
            return -1;
        }
    }

    /// <summary>The yield of one structure computed from the latest surveys of its enabled areas (PRODUCTION_KINDS § 4.1-4.2).</summary>
    public interface IProductionYieldCalculator
    {
        /// <summary>Every output with its contributions, Hybrid overrides applied when <paramref name="hybrid"/>.</summary>
        Task<YieldComputation> ComputeAsync(int structureId, bool hybrid, CancellationToken ct = default);
    }

    public sealed class YieldComputation
    {
        public List<YieldOutputDto> Outputs { get; } = new();
        public List<string> UnmatchedUnitKeys { get; } = new();
        public HashSet<int> PrimaryBlueprintIds { get; } = new();
        public List<ProductionArea> Areas { get; } = new();

        public decimal BaseRate(int blueprintId) => Outputs.FirstOrDefault(o => o.ItemBlueprintId == blueprintId)?.BaseRatePerHour ?? 0m;
    }

    public sealed class ProductionYieldCalculator : IProductionYieldCalculator
    {
        private readonly KnKDbContext _db;

        public ProductionYieldCalculator(KnKDbContext db) => _db = db;

        public async Task<YieldComputation> ComputeAsync(int structureId, bool hybrid, CancellationToken ct = default)
        {
            var result = new YieldComputation();
            var structure = await _db.ProductionStructures.AsNoTracking().FirstOrDefaultAsync(s => s.Id == structureId, ct);
            if (structure == null) return result;
            result.Areas.AddRange(await _db.ProductionAreas.AsNoTracking().Where(a => a.ProductionStructureId == structureId)
                .OrderBy(a => a.Id).ToListAsync(ct));
            var rows = structure.ProductionKindId == null ? new List<ProductionYieldRow>()
                : await _db.ProductionYieldRows.AsNoTracking().Where(r => r.ProductionKindId == structure.ProductionKindId)
                    .OrderBy(r => r.Id).ToListAsync(ct);
            if (structure.ProductionKindId != null)
                foreach (var id in await _db.ProductionKindPrimaryOutputs.AsNoTracking()
                             .Where(p => p.ProductionKindId == structure.ProductionKindId).Select(p => p.ItemBlueprintId).ToListAsync(ct))
                    result.PrimaryBlueprintIds.Add(id);
            var activeRows = rows.Where(r => r.MinLevel <= structure.Level).ToList();

            var outputs = new Dictionary<int, YieldOutputDto>();
            YieldOutputDto Output(int bp) => outputs.TryGetValue(bp, out var o) ? o
                : outputs[bp] = new YieldOutputDto { ItemBlueprintId = bp, IsPrimary = result.PrimaryBlueprintIds.Contains(bp) };

            var unmatched = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var area in result.Areas.Where(a => a.Enabled && a.LastSurveyId != null))
            {
                var units = await _db.AreaSurveyUnits.AsNoTracking().Where(u => u.AreaSurveyId == area.LastSurveyId)
                    .OrderBy(u => u.UnitKey).ThenBy(u => u.SharedBy).ToListAsync(ct);
                foreach (var unit in units)
                {
                    bool any = false;
                    // Per blueprint, the most specific row that matches the unit counts (exact beats prefix).
                    foreach (var group in activeRows.GroupBy(r => r.ItemBlueprintId))
                    {
                        var best = group.Select(r => (Row: r, Score: SurveyUnitKeys.Match(r.UnitKey, unit.UnitKey)))
                            .Where(x => x.Score >= 0).OrderByDescending(x => x.Score).ThenBy(x => x.Row.Id).FirstOrDefault();
                        if (best.Row == null) continue;
                        any = true;
                        int sharedBy = Math.Max(1, unit.SharedBy);
                        decimal share = Math.Round(1m / sharedBy, ProductionRate.Decimals);
                        decimal rate = Math.Round(unit.Count / (decimal)sharedBy * best.Row.RatePerUnitPerHour * best.Row.ByproductChance,
                            ProductionRate.Decimals);
                        var o = Output(group.Key);
                        o.Contributions.Add(new YieldContributionDto
                        {
                            ProductionAreaId = area.Id, AreaName = area.Name, AreaSurveyId = area.LastSurveyId!.Value,
                            UnitKey = unit.UnitKey, YieldRowUnitKey = best.Row.UnitKey, Count = unit.Count, SharedBy = sharedBy,
                            Share = share, RatePerUnitPerHour = best.Row.RatePerUnitPerHour, ByproductChance = best.Row.ByproductChance,
                            RatePerHour = rate,
                        });
                        o.DerivedRatePerHour += rate;
                    }
                    if (!any) unmatched.Add(unit.UnitKey);
                }
            }
            result.UnmatchedUnitKeys.AddRange(unmatched);

            var overrides = hybrid
                ? await _db.ProductionYieldOverrides.AsNoTracking().Where(o => o.ProductionStructureId == structureId).ToListAsync(ct)
                : new List<ProductionYieldOverride>();
            foreach (var ov in overrides) Output(ov.ItemBlueprintId);
            foreach (var o in outputs.Values)
            {
                o.DerivedRatePerHour = Math.Round(o.DerivedRatePerHour, ProductionRate.Decimals);
                var ov = overrides.FirstOrDefault(x => x.ItemBlueprintId == o.ItemBlueprintId);
                o.Override = ov?.Kind.ToString();
                o.OverrideValue = ov == null || ov.Kind == ProductionYieldOverrideKind.Exclude ? null : ov.Value;
                o.BaseRatePerHour = ov?.Kind switch
                {
                    ProductionYieldOverrideKind.Pin => ov.Value,
                    ProductionYieldOverrideKind.Exclude => 0m,
                    ProductionYieldOverrideKind.Scale => Math.Round(o.DerivedRatePerHour * ov.Value, ProductionRate.Decimals),
                    _ => o.DerivedRatePerHour,
                };
            }
            var names = await _db.ItemBlueprints.AsNoTracking().Where(b => outputs.Keys.Contains(b.Id))
                .Select(b => new { b.Id, b.Name }).ToDictionaryAsync(b => b.Id, b => b.Name, ct);
            foreach (var o in outputs.Values.OrderBy(o => o.ItemBlueprintId))
            {
                o.ItemBlueprintName = names.GetValueOrDefault(o.ItemBlueprintId);
                result.Outputs.Add(o);
            }
            return result;
        }
    }

    /// <summary>Derived (§ 4.1): Σ over the latest surveys' units: count × share × rate × chance.</summary>
    public sealed class DerivedBaseRateProvider : IProductionBaseRateProvider
    {
        private readonly IProductionYieldCalculator _calculator;

        public DerivedBaseRateProvider(IProductionYieldCalculator calculator) => _calculator = calculator;

        public ProductionRateSource Source => ProductionRateSource.Derived;

        public async Task<decimal> BaseRatePerHourAsync(ProductionSettleContext ctx, CancellationToken ct = default) =>
            (await _calculator.ComputeAsync(ctx.Structure.Id, hybrid: false, ct)).BaseRate(ctx.Line.ItemBlueprintId);
    }

    /// <summary>Hybrid (§ 4.1, the default, P-D1): Derived, then the structure's pins, exclusions and scales.</summary>
    public sealed class HybridBaseRateProvider : IProductionBaseRateProvider
    {
        private readonly IProductionYieldCalculator _calculator;

        public HybridBaseRateProvider(IProductionYieldCalculator calculator) => _calculator = calculator;

        public ProductionRateSource Source => ProductionRateSource.Hybrid;

        public async Task<decimal> BaseRatePerHourAsync(ProductionSettleContext ctx, CancellationToken ct = default) =>
            (await _calculator.ComputeAsync(ctx.Structure.Id, hybrid: true, ct)).BaseRate(ctx.Line.ItemBlueprintId);
    }

    /// <summary>The built-in "primary" step (§ 4.2): a kind's primary outputs get its primary multiplier on Derived and
    /// Hybrid lines. Listed in the trace like a modifier (source "Primary").</summary>
    public sealed class PrimaryOutputModifierSource : IProductionModifierSource
    {
        private readonly KnKDbContext _db;

        public PrimaryOutputModifierSource(KnKDbContext db) => _db = db;

        public string Name => "Primary";

        public async Task<IReadOnlyList<AppliedModifierDto>> CollectAsync(ProductionSettleContext ctx, CancellationToken ct = default)
        {
            var kind = ctx.Structure.ProductionKind;
            if (ctx.Line.RateSource == ProductionRateSource.Configured || kind == null || kind.PrimaryMultiplier == 1m)
                return Array.Empty<AppliedModifierDto>();
            bool primary = await _db.ProductionKindPrimaryOutputs.AsNoTracking()
                .AnyAsync(p => p.ProductionKindId == kind.Id && p.ItemBlueprintId == ctx.Line.ItemBlueprintId, ct);
            return primary
                ? new[]
                {
                    new AppliedModifierDto
                    {
                        Source = Name, Name = $"Primary output of {kind.Name}", Scope = nameof(ProductionModifierScope.Output),
                        Effect = nameof(ProductionModifierEffect.Multiply), Value = kind.PrimaryMultiplier,
                    },
                }
                : Array.Empty<AppliedModifierDto>();
        }
    }

    /// <summary>The optional ±10% variation per kind (P-D9): a deterministic factor per line and hour, so a replay of
    /// the same settle gives the same output. Listed in the trace (source "Variation").</summary>
    public sealed class VariationModifierSource : IProductionModifierSource
    {
        public const decimal Spread = 0.1m;

        public string Name => "Variation";

        public Task<IReadOnlyList<AppliedModifierDto>> CollectAsync(ProductionSettleContext ctx, CancellationToken ct = default)
        {
            if (ctx.Structure.ProductionKind?.CycleVariation != true) return Task.FromResult<IReadOnlyList<AppliedModifierDto>>(Array.Empty<AppliedModifierDto>());
            IReadOnlyList<AppliedModifierDto> list = new[]
            {
                new AppliedModifierDto
                {
                    Source = Name, Name = "Variation (±10%)", Scope = nameof(ProductionModifierScope.Output),
                    Effect = nameof(ProductionModifierEffect.Multiply), Value = Factor(ctx.Line.Id, ctx.At),
                },
            };
            return Task.FromResult(list);
        }

        /// <summary>In [0.9, 1.1], fixed per line and UTC hour.</summary>
        public static decimal Factor(int lineId, DateTime at)
        {
            long hour = at.Ticks / TimeSpan.TicksPerHour;
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"variation:{lineId}:{hour}"));
            uint n = BitConverter.ToUInt32(hash, 0);
            decimal unit = n / (decimal)uint.MaxValue; // [0, 1]
            return Math.Round(1m - Spread + 2m * Spread * unit, 4);
        }
    }
}
