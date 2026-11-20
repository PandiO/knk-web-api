using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Stock;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace knkwebapi_v2.Services.Production
{
    /// <summary>Everything a modifier source or base-rate provider may read about one line at one moment.</summary>
    public sealed class ProductionSettleContext
    {
        public required ProductionStructure Structure { get; init; }
        public required ProductionLine Line { get; init; }
        public ProductionKindLevel? LevelRow { get; init; }
        public int? EffectiveTownId { get; init; }
        public int? KingdomId { get; init; }
        public ResourceCategory? Category { get; init; }
        public required DateTime At { get; init; }
        public required ProductionEngineSettingsDto Settings { get; init; }
    }

    /// <summary>
    /// A modifier source (PRODUCTION_KINDS § 4.2): new factors are new sources, the engine does not change. V1: the
    /// structure level, the weather and the stored (staff) modifiers.
    /// </summary>
    public interface IProductionModifierSource
    {
        string Name { get; }
        Task<IReadOnlyList<AppliedModifierDto>> CollectAsync(ProductionSettleContext ctx, CancellationToken ct = default);
    }

    /// <summary>Where a line's base rate comes from (§ 4.1). P3: Configured; P4 adds Derived and Hybrid (P-D2).</summary>
    public interface IProductionBaseRateProvider
    {
        ProductionRateSource Source { get; }
        Task<decimal> BaseRatePerHourAsync(ProductionSettleContext ctx, CancellationToken ct = default);
    }

    public sealed class ConfiguredBaseRateProvider : IProductionBaseRateProvider
    {
        public ProductionRateSource Source => ProductionRateSource.Configured;

        public Task<decimal> BaseRatePerHourAsync(ProductionSettleContext ctx, CancellationToken ct = default) =>
            Task.FromResult(ctx.Line.ConfiguredRatePerHour);
    }

    /// <summary>The level modifier: the kind level's yield multiplier (§ 5).</summary>
    public sealed class LevelModifierSource : IProductionModifierSource
    {
        public string Name => "Level";

        public Task<IReadOnlyList<AppliedModifierDto>> CollectAsync(ProductionSettleContext ctx, CancellationToken ct = default)
        {
            IReadOnlyList<AppliedModifierDto> list = ctx.LevelRow == null
                ? Array.Empty<AppliedModifierDto>()
                : new[]
                {
                    new AppliedModifierDto
                    {
                        Source = Name, Name = $"Level {ctx.LevelRow.Level}", Scope = nameof(ProductionModifierScope.Structure),
                        Effect = nameof(ProductionModifierEffect.Multiply), Value = ctx.LevelRow.YieldMultiplier,
                    },
                };
            return Task.FromResult(list);
        }
    }

    /// <summary>The weather modifier: the zone's stored roll for the day, its effects on the line's category (§ 4.4).</summary>
    public sealed class WeatherModifierSource : IProductionModifierSource
    {
        private readonly KnKDbContext _db;
        private readonly IProductionWeatherService _weather;

        public WeatherModifierSource(KnKDbContext db, IProductionWeatherService weather)
        {
            _db = db;
            _weather = weather;
        }

        public string Name => "Weather";

        public async Task<IReadOnlyList<AppliedModifierDto>> CollectAsync(ProductionSettleContext ctx, CancellationToken ct = default)
        {
            if (ctx.Category == null) return Array.Empty<AppliedModifierDto>();
            var zone = await _weather.ZoneForTownAsync(ctx.EffectiveTownId, ct);
            if (zone == null) return Array.Empty<AppliedModifierDto>();
            int dayMinutes = ctx.Settings.WeatherDayMinutes;
            var roll = await _weather.GetOrRollAsync(zone.Id, _weather.DayOf(ctx.At, dayMinutes), dayMinutes, ct);
            if (roll.WeatherTypeId == null) return Array.Empty<AppliedModifierDto>();
            var effects = await _db.WeatherTypeEffects.AsNoTracking()
                .Where(e => e.WeatherTypeId == roll.WeatherTypeId && e.Category == ctx.Category).ToListAsync(ct);
            string typeName = roll.WeatherType?.Name ?? $"Weather {roll.WeatherTypeId}";
            return effects.Select(e => new AppliedModifierDto
            {
                Source = Name, Name = $"{typeName} in {zone.Name} ({e.Category})", Scope = nameof(ProductionModifierScope.Town),
                Effect = e.Effect.ToString(), Value = e.Value,
            }).ToList();
        }
    }

    /// <summary>Staff-made modifiers (<see cref="ProductionModifier"/>) whose scope, target and window apply.</summary>
    public sealed class StoredModifierSource : IProductionModifierSource
    {
        private readonly KnKDbContext _db;

        public StoredModifierSource(KnKDbContext db) => _db = db;

        public string Name => "Stored";

        public async Task<IReadOnlyList<AppliedModifierDto>> CollectAsync(ProductionSettleContext ctx, CancellationToken ct = default)
        {
            var rows = await _db.ProductionModifiers.AsNoTracking().Where(m => m.Enabled).OrderBy(m => m.Id).ToListAsync(ct);
            return rows.Where(m => Applies(m, ctx)).Select(m => new AppliedModifierDto
            {
                Source = m.Source, Name = m.Name, Scope = m.Scope.ToString(), Effect = m.Effect.ToString(), Value = m.Value,
                ModifierId = m.Id,
            }).ToList();
        }

        /// <summary>Scope, target and active window (start inclusive, end exclusive) matching.</summary>
        public static bool Applies(ProductionModifier m, ProductionSettleContext ctx)
        {
            if (m.ActiveFrom.HasValue && ctx.At < m.ActiveFrom.Value) return false;
            if (m.ActiveUntil.HasValue && ctx.At >= m.ActiveUntil.Value) return false;
            bool scope = m.Scope switch
            {
                ProductionModifierScope.Global => true,
                ProductionModifierScope.Kingdom => ctx.KingdomId.HasValue && m.ScopeRefId == ctx.KingdomId,
                ProductionModifierScope.Town => ctx.EffectiveTownId.HasValue && m.ScopeRefId == ctx.EffectiveTownId,
                ProductionModifierScope.Territory => ctx.Structure.DistrictId == null && ctx.Structure.TownId.HasValue
                    && m.ScopeRefId == ctx.Structure.TownId,
                ProductionModifierScope.Structure => m.ScopeRefId == ctx.Structure.Id,
                ProductionModifierScope.Output => m.ScopeRefId == ctx.Line.Id,
                _ => false, // MapArea: the painted map is Later
            };
            if (!scope) return false;
            return m.Target switch
            {
                ProductionModifierTarget.Any => true,
                ProductionModifierTarget.Blueprint => m.TargetItemBlueprintId == ctx.Line.ItemBlueprintId,
                ProductionModifierTarget.Category => ctx.Category.HasValue && m.TargetCategory == ctx.Category,
                ProductionModifierTarget.Kind => ctx.Structure.ProductionKindId.HasValue
                    && m.TargetProductionKindId == ctx.Structure.ProductionKindId,
                _ => false,
            };
        }
    }

    /// <summary>The rate formula (PRODUCTION_KINDS § 4.2, F8): additions to the base first, then multiplications, then
    /// the built-in steps (unlock, workers; primary and pool come with P4 and P8). Never negative.</summary>
    public static class ProductionRate
    {
        public const int Decimals = 6;

        public static decimal Compute(decimal baseRate, IEnumerable<AppliedModifierDto> modifiers, decimal unlock, decimal workers)
        {
            var list = modifiers.ToList();
            decimal rate = baseRate;
            foreach (var m in list.Where(m => m.Effect == nameof(ProductionModifierEffect.Add))) rate += m.Value;
            foreach (var m in list.Where(m => m.Effect == nameof(ProductionModifierEffect.Multiply))) rate *= m.Value;
            rate *= unlock * workers;
            return rate < 0 ? 0 : Math.Round(rate, Decimals);
        }

        /// <summary>Additions first, then multiplications: the order the trace lists them in.</summary>
        public static List<AppliedModifierDto> Ordered(IEnumerable<AppliedModifierDto> modifiers)
        {
            var list = modifiers.ToList();
            return list.Where(m => m.Effect == nameof(ProductionModifierEffect.Add))
                .Concat(list.Where(m => m.Effect == nameof(ProductionModifierEffect.Multiply))).ToList();
        }

        public static decimal WorkerFactor(int present, int total) =>
            total <= 0 ? 0m : Math.Round(Math.Clamp(present, 0, total) / (decimal)total, Decimals);

        /// <summary>The cycle length: base cycle ÷ the level's cycle speed (at least one second).</summary>
        public static TimeSpan Cycle(ProductionEngineSettingsDto settings, ProductionKindLevel? level)
        {
            decimal speed = level?.CycleSpeedMultiplier > 0 ? level.CycleSpeedMultiplier : 1m;
            var seconds = (double)(settings.BaseCycleMinutes * 60m / speed);
            return TimeSpan.FromSeconds(Math.Max(1, Math.Round(seconds)));
        }

        /// <summary>The first due time on the line's grid strictly after <paramref name="now"/> (no drift, V2 defect 5).</summary>
        public static DateTime NextDue(DateTime anchor, TimeSpan cycle, DateTime now)
        {
            long since = now.Ticks - anchor.Ticks;
            long k = since < 0 ? 1 : since / cycle.Ticks + 1;
            return DateTime.SpecifyKind(new DateTime(anchor.Ticks + k * cycle.Ticks), DateTimeKind.Utc);
        }
    }

    public interface IProductionEngine
    {
        /// <summary>The sweep: rolls today's weather and settles the due lines (all lines with <paramref name="all"/>).
        /// Does nothing while <c>Production:Enabled</c> is off.</summary>
        Task<ProductionSweepResultDto> SweepAsync(bool all = false, CancellationToken ct = default);

        /// <summary>Settles one line up to now (no-op while the feature is off); null when the line doesn't exist.</summary>
        Task<ProductionCycleRecordDto?> SettleLineAsync(int lineId, CancellationToken ct = default);

        /// <summary>Settles every line of a structure up to now (no-op while the feature is off).</summary>
        Task SettleStructureAsync(int structureId, CancellationToken ct = default);

        /// <summary>The structure's engine view; settles it first while the feature is on (lazy settle on read).</summary>
        Task<ProductionOverviewDto?> OverviewAsync(int structureId, CancellationToken ct = default);

        /// <summary>Raises the level by one, paying the next level's resources and coins in one transaction.</summary>
        Task<ProductionUpgradeResultDto> UpgradeAsync(int structureId, ProductionUpgradeRequestDto request, int? actorUserId,
            CancellationToken ct = default);

        /// <summary>Sets the workers present (settling first, so the time before counts at the old factor).</summary>
        Task<ProductionOverviewDto> SetWorkersPresentAsync(int structureId, int workersPresent, CancellationToken ct = default);
    }

    /// <summary>
    /// The production engine (PRODUCTION_KINDS § 4.3, PandiO/knk-workspace#86). Each line settles on its anchored grid:
    /// <c>Accrued += rate × elapsed</c>, emit the whole units into the structure's Output storage as one stock move from
    /// <c>SYS_PRODUCTION</c> (idempotency key per line and settle), keep the fraction. Elapsed time is capped (catch-up
    /// cap); a full Output storage stops the line without accruing (D4) and it resumes, with no catch-up, once there is
    /// room. Inputs are taken from the structure's Input storage while the line runs (V2 defect 12). Every settle that
    /// accrues or changes the status writes a <see cref="ProductionCycleRecord"/> with each modifier. Each line settles
    /// in its own database transaction with its row locked, so two sweeps never settle the same period twice.
    /// </summary>
    public sealed class ProductionEngine : IProductionEngine
    {
        public const int SweepBatchSize = 500;
        public const int RecentCycleCount = 20;
        public const string Component = "ProductionEngine";

        private static readonly JsonSerializerOptions Json = new();

        private readonly KnKDbContext _db;
        private readonly IStockService _stock;
        private readonly IProductionSettingsReader _settings;
        private readonly IProductionWeatherService _weather;
        private readonly IReadOnlyList<IProductionModifierSource> _sources;
        private readonly IReadOnlyDictionary<ProductionRateSource, IProductionBaseRateProvider> _rates;
        private readonly ICurrencyService? _currency;
        private readonly IPlayerNotificationQueue? _notifications;
        private readonly TimeProvider _clock;
        private readonly ILogger _logger;

        public ProductionEngine(KnKDbContext db, IStockService stock, IProductionSettingsReader settings,
            IProductionWeatherService weather, IEnumerable<IProductionModifierSource> sources,
            IEnumerable<IProductionBaseRateProvider> rates, ICurrencyService? currency = null,
            IPlayerNotificationQueue? notifications = null, TimeProvider? clock = null, ILogger<ProductionEngine>? logger = null)
        {
            _db = db;
            _stock = stock;
            _settings = settings;
            _weather = weather;
            _sources = sources.ToList();
            _rates = rates.ToDictionary(r => r.Source);
            _currency = currency;
            _notifications = notifications;
            _clock = clock ?? TimeProvider.System;
            _logger = (ILogger?)logger ?? NullLogger.Instance;
        }

        private DateTime Now => _clock.GetUtcNow().UtcDateTime;

        // ================= Sweep =================

        public async Task<ProductionSweepResultDto> SweepAsync(bool all = false, CancellationToken ct = default)
        {
            var settings = await _settings.GetAsync();
            var now = Now;
            var result = new ProductionSweepResultDto { FeatureEnabled = settings.Enabled, RanAt = now };
            if (!settings.Enabled) return result;

            result.WeatherRolled = await _weather.RollAllAsync(now, settings.Engine.WeatherDayMinutes, ct);
            var due = _db.ProductionLines.AsNoTracking();
            if (!all) due = due.Where(l => l.NextSettleAt <= now);
            var ids = await due.OrderBy(l => l.NextSettleAt).ThenBy(l => l.Id).Select(l => l.Id).Take(SweepBatchSize).ToListAsync(ct);
            foreach (var id in ids)
            {
                try
                {
                    var record = await SettleCoreAsync(id, now, settings.Engine, ct);
                    result.LinesSettled++;
                    if (record != null)
                    {
                        result.UnitsEmitted += record.Emitted;
                        result.UnitsAccepted += record.Accepted;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    result.LinesFailed++;
                    _db.ChangeTracker.Clear();
                    _logger.LogWarning(ex, "Production settle failed for line {LineId}", id);
                }
            }
            return result;
        }

        public async Task<ProductionCycleRecordDto?> SettleLineAsync(int lineId, CancellationToken ct = default)
        {
            var settings = await _settings.GetAsync();
            if (!settings.Enabled) return null;
            if (!await _db.ProductionLines.AnyAsync(l => l.Id == lineId, ct)) return null;
            var record = await SettleCoreAsync(lineId, Now, settings.Engine, ct);
            return record == null ? null : await RecordDtoAsync(record, ct);
        }

        public async Task SettleStructureAsync(int structureId, CancellationToken ct = default)
        {
            var settings = await _settings.GetAsync();
            if (!settings.Enabled) return;
            var now = Now;
            var ids = await _db.ProductionLines.AsNoTracking().Where(l => l.ProductionStructureId == structureId)
                .OrderBy(l => l.Id).Select(l => l.Id).ToListAsync(ct);
            foreach (var id in ids) await SettleCoreAsync(id, now, settings.Engine, ct);
        }

        // ================= Settle =================

        private sealed record Loaded(ProductionLine Line, ProductionStructure Structure, ProductionKindLevel? LevelRow,
            int? TownId, int? KingdomId, ResourceCategory? Category);

        private async Task<Loaded?> LoadAsync(int lineId, bool tracking, CancellationToken ct)
        {
            IQueryable<ProductionLine> q = _db.ProductionLines.Include(l => l.Inputs).Include(l => l.ItemBlueprint);
            if (!tracking) q = q.AsNoTracking();
            var line = await q.FirstOrDefaultAsync(l => l.Id == lineId, ct);
            if (line == null) return null;
            IQueryable<ProductionStructure> sq = _db.ProductionStructures.Include(s => s.ProductionKind)
                .Include(s => s.District).Include(s => s.Town);
            if (!tracking) sq = sq.AsNoTracking();
            var structure = await sq.FirstAsync(s => s.Id == line.ProductionStructureId, ct);
            return await CompleteAsync(line, structure, ct);
        }

        private async Task<Loaded> CompleteAsync(ProductionLine line, ProductionStructure structure, CancellationToken ct)
        {
            ProductionKindLevel? level = structure.ProductionKindId == null ? null
                : await _db.ProductionKindLevels.AsNoTracking()
                    .FirstOrDefaultAsync(l => l.ProductionKindId == structure.ProductionKindId && l.Level == structure.Level, ct);
            int? townId = structure.TownId ?? structure.District?.TownId;
            int? kingdomId = townId == null ? null
                : await _db.Towns.AsNoTracking().Where(t => t.Id == townId).Select(t => t.KingdomId).FirstOrDefaultAsync(ct);
            ResourceCategory? category = line.ResourceCategory ?? structure.ProductionKind?.ResourceCategory;
            return new Loaded(line, structure, level, townId, kingdomId, category);
        }

        private ProductionSettleContext Context(Loaded l, DateTime at, ProductionEngineSettingsDto settings) => new()
        {
            Structure = l.Structure, Line = l.Line, LevelRow = l.LevelRow, EffectiveTownId = l.TownId, KingdomId = l.KingdomId,
            Category = l.Category, At = at, Settings = settings,
        };

        private sealed record RatePlan(decimal BaseRate, List<AppliedModifierDto> Modifiers, decimal Unlock, decimal Workers,
            decimal Effective, int? UnlockLevel);

        private async Task<RatePlan> PlanAsync(Loaded l, DateTime at, ProductionEngineSettingsDto settings, CancellationToken ct)
        {
            var ctx = Context(l, at, settings);
            decimal baseRate = _rates.TryGetValue(l.Line.RateSource, out var provider) ? await provider.BaseRatePerHourAsync(ctx, ct) : 0m;
            var modifiers = new List<AppliedModifierDto>();
            foreach (var source in _sources) modifiers.AddRange(await source.CollectAsync(ctx, ct));
            modifiers = ProductionRate.Ordered(modifiers);
            int? unlockLevel = await UnlockLevelAsync(l.Structure.ProductionKindId, l.Line.ItemBlueprintId, ct);
            decimal unlock = unlockLevel.HasValue && l.Structure.Level < unlockLevel.Value ? 0m : 1m;
            decimal workers = ProductionRate.WorkerFactor(l.Structure.WorkersPresent, l.Structure.WorkersTotal);
            return new RatePlan(baseRate, modifiers, unlock, workers, ProductionRate.Compute(baseRate, modifiers, unlock, workers), unlockLevel);
        }

        /// <summary>The lowest level of the kind that lists the blueprint as an unlock; null = produced from level 1.</summary>
        private async Task<int?> UnlockLevelAsync(int? kindId, int blueprintId, CancellationToken ct)
        {
            if (kindId == null) return null;
            var levels = await _db.ProductionKindLevelItems.AsNoTracking()
                .Where(i => i.Role == ProductionKindLevelItemRole.Unlock && i.ItemBlueprintId == blueprintId
                    && i.ProductionKindLevel.ProductionKindId == kindId)
                .Select(i => i.ProductionKindLevel.Level).ToListAsync(ct);
            return levels.Count == 0 ? null : levels.Min();
        }

        private Task<Storage?> StorageOfAsync(int structureId, StoragePurpose purpose, CancellationToken ct) =>
            _db.Storages.AsNoTracking().Where(s => s.Kind == StorageKind.Domain && s.DomainId == structureId && s.Purpose == purpose)
                .OrderBy(s => s.Id).FirstOrDefaultAsync(ct);

        /// <summary>Whether one more unit of the blueprint fits (lock, distinct cap, raw units × storage weight).</summary>
        private async Task<string?> NoRoomReasonAsync(Storage storage, int blueprintId, DateTime now, CancellationToken ct)
        {
            if (storage.IsLockedAt(now)) return $"The Output storage is locked: {storage.LockReason ?? "no reason given"}.";
            bool hasLine = await _db.StockLines.AsNoTracking().AnyAsync(l => l.StorageId == storage.Id && l.ItemBlueprintId == blueprintId, ct);
            if (!hasLine && storage.MaxDistinct.HasValue && storage.DistinctItems >= storage.MaxDistinct.Value)
                return "The Output storage holds as many different items as it may (distinct cap).";
            if (!storage.MaxUnits.HasValue) return null;
            decimal lines = await _db.StockLines.AsNoTracking().Where(l => l.StorageId == storage.Id)
                .SumAsync(l => (decimal?)(l.Amount * l.ItemBlueprint.StorageWeight), ct) ?? 0m;
            decimal units = await _db.StockUnits.AsNoTracking().Where(u => u.StorageId == storage.Id)
                .SumAsync(u => (decimal?)u.ItemInstance.ItemBlueprint.StorageWeight, ct) ?? 0m;
            decimal weight = await _db.ItemBlueprints.AsNoTracking().Where(b => b.Id == blueprintId)
                .Select(b => b.StorageWeight).FirstOrDefaultAsync(ct);
            if (weight <= 0) weight = ItemBlueprint.DefaultStorageWeight;
            return lines + units + weight > storage.MaxUnits.Value
                ? $"The Output storage is full ({lines + units:0.##} of {storage.MaxUnits.Value} units)."
                : null;
        }

        private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginAsync(CancellationToken ct) =>
            _db.Database.IsRelational() && _db.Database.CurrentTransaction == null
                ? await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
                : null;

        private async Task LockLineAsync(int lineId, CancellationToken ct)
        {
            if (!_db.Database.IsRelational()) return;
            await _db.Database.ExecuteSqlRawAsync($"SELECT Id FROM production_lines WHERE Id = {lineId} FOR UPDATE", ct);
        }

        /// <summary>Settles one line up to <paramref name="now"/>; returns the cycle record written, if any.</summary>
        private async Task<ProductionCycleRecord?> SettleCoreAsync(int lineId, DateTime now, ProductionEngineSettingsDto settings,
            CancellationToken ct)
        {
            ProductionLineStatusNotificationDto? notify = null;
            ProductionCycleRecord? record;
            await using (var tx = await BeginAsync(ct))
            {
                await LockLineAsync(lineId, ct);
                var loaded = await LoadAsync(lineId, tracking: true, ct);
                if (loaded == null) return null;
                var (line, structure) = (loaded.Line, loaded.Structure);
                var cycle = ProductionRate.Cycle(settings, loaded.LevelRow);
                // Nothing elapsed: only a stopped line is looked at again (switched back on, room again).
                if (now < line.LastSettledAt || (now == line.LastSettledAt && line.Status == ProductionLineStatus.Running))
                {
                    line.NextSettleAt = ProductionRate.NextDue(line.AnchorAt, cycle, line.LastSettledAt);
                    await _db.SaveChangesAsync(ct);
                    if (tx != null) await tx.CommitAsync(ct);
                    return null;
                }

                var previous = line.Status;
                record = await SettleLoadedAsync(loaded, now, settings, ct);
                line.LastSettledAt = now;
                line.NextSettleAt = ProductionRate.NextDue(line.AnchorAt, cycle, now);
                if (record != null) _db.ProductionCycleRecords.Add(record);
                await _db.SaveChangesAsync(ct);
                if (tx != null) await tx.CommitAsync(ct);

                if (previous != line.Status && (Blocked(line.Status) || Blocked(previous)))
                    notify = new ProductionLineStatusNotificationDto
                    {
                        ProductionStructureId = structure.Id, ProductionStructureName = structure.Name,
                        ProductionLineId = line.Id, ItemBlueprintId = line.ItemBlueprintId,
                        ItemBlueprintName = line.ItemBlueprint?.Name ?? string.Empty,
                        PreviousStatus = previous.ToString(), Status = line.Status.ToString(), Reason = line.StatusReason,
                    };
            }
            _db.ChangeTracker.Clear();
            if (notify != null) _notifications?.EnqueueProductionLineStatus(notify);
            return record;
        }

        private static bool Blocked(ProductionLineStatus s) =>
            s is ProductionLineStatus.BlockedFull or ProductionLineStatus.BlockedInputs;

        private ProductionCycleRecord NewRecord(Loaded l, DateTime now, long elapsed, long skipped) => new()
        {
            ProductionLineId = l.Line.Id,
            ProductionStructureId = l.Structure.Id,
            ItemBlueprintId = l.Line.ItemBlueprintId,
            SettledAt = now,
            PeriodStart = l.Line.LastSettledAt,
            PeriodEnd = now,
            ElapsedSeconds = elapsed,
            SkippedSeconds = skipped,
            RateSource = l.Line.RateSource,
            WorkersPresent = l.Structure.WorkersPresent,
            WorkersTotal = l.Structure.WorkersTotal,
            WorkerFactor = ProductionRate.WorkerFactor(l.Structure.WorkersPresent, l.Structure.WorkersTotal),
            AccruedBefore = l.Line.Accrued,
            AccruedAfter = l.Line.Accrued,
        };

        /// <summary>
        /// One settle of a loaded, locked line. Gates first (switched off, paused, no Output storage, no room); a line that
        /// was not running resumes from now without catch-up; a running line accrues over the capped elapsed time.
        /// Returns a record when it accrued or its status changed.
        /// </summary>
        private async Task<ProductionCycleRecord?> SettleLoadedAsync(Loaded l, DateTime now, ProductionEngineSettingsDto settings,
            CancellationToken ct)
        {
            var line = l.Line;
            var previous = line.Status;
            long rawSeconds = (long)(now - line.LastSettledAt).TotalSeconds;

            ProductionCycleRecord? Skip(ProductionLineStatus status, string? reason)
            {
                bool changed = status != line.Status || reason != line.StatusReason;
                line.Status = status;
                line.StatusReason = reason;
                if (!changed) return null;
                var r = NewRecord(l, now, 0, rawSeconds);
                r.StatusAfter = status;
                r.ShortfallReason = reason;
                r.WorkerFactor = 0m;
                return r;
            }

            if (!l.Structure.ProductionEnabled) return Skip(ProductionLineStatus.Paused, "The structure's production is switched off.");
            if (!line.Enabled) return Skip(ProductionLineStatus.Paused, "The line is paused.");
            if (!_rates.ContainsKey(line.RateSource))
                return Skip(ProductionLineStatus.Error, $"Rate source {line.RateSource} is not available yet (P4).");
            var output = await StorageOfAsync(l.Structure.Id, StoragePurpose.Output, ct);
            if (output == null) return Skip(ProductionLineStatus.Error, "The structure has no Output storage.");
            var noRoom = await NoRoomReasonAsync(output, line.ItemBlueprintId, now, ct);
            if (noRoom != null) return Skip(ProductionLineStatus.BlockedFull, noRoom);

            var input = line.Inputs.Count > 0 ? await StorageOfAsync(l.Structure.Id, StoragePurpose.Input, ct) : null;
            if (previous == ProductionLineStatus.BlockedInputs)
            {
                var missing = await MissingInputAsync(line, input, i => 1, ct);
                if (missing != null) return Skip(ProductionLineStatus.BlockedInputs, missing);
            }
            if (previous != ProductionLineStatus.Running)
                return Skip(ProductionLineStatus.Running, null); // resumes from now: no catch-up for the stopped time

            // Running: accrue over the capped elapsed time.
            long capSeconds = settings.CatchUpCapHours * 3600L;
            long elapsed = Math.Min(rawSeconds, capSeconds);
            var plan = await PlanAsync(l, now, settings, ct);
            decimal hours = elapsed / 3600m;
            var record = NewRecord(l, now, elapsed, rawSeconds - elapsed);
            record.BaseRatePerHour = plan.BaseRate;
            record.ModifiersJson = JsonSerializer.Serialize(plan.Modifiers, Json);
            record.UnlockFactor = plan.Unlock;
            record.WorkerFactor = plan.Workers;
            record.EffectiveRatePerHour = plan.Effective;

            // Inputs, consumed while the line runs (scaled like the workers and the unlock).
            var takes = new Dictionary<ProductionLineInput, (decimal Total, long Take)>();
            foreach (var i in line.Inputs)
            {
                decimal total = Math.Round(i.Accrued + i.AmountPerHour * hours * plan.Workers * plan.Unlock, ProductionRate.Decimals);
                takes[i] = (total, (long)Math.Floor(total));
            }
            if (takes.Values.Any(t => t.Take > 0))
            {
                var missing = await MissingInputAsync(line, input, i => takes[i].Take, ct);
                if (missing != null)
                {
                    line.Status = ProductionLineStatus.BlockedInputs;
                    line.StatusReason = missing;
                    record.ElapsedSeconds = 0;
                    record.SkippedSeconds = rawSeconds;
                    record.EffectiveRatePerHour = 0;
                    record.StatusAfter = line.Status;
                    record.ShortfallReason = missing;
                    return record;
                }
            }

            line.Accrued = Math.Round(line.Accrued + plan.Effective * hours, ProductionRate.Decimals);
            long emit = (long)Math.Floor(line.Accrued);
            string keyBase = $"prod:{line.Id}:{now.Ticks}";

            var consumed = new List<ConsumedInputDto>();
            long? txId = null;
            var inputLines = takes.Where(t => t.Value.Take > 0)
                .Select(t => StockLineRequest.Fungible(input!.Id, 0, t.Key.ItemBlueprintId, t.Value.Take)).ToList();
            if (inputLines.Count > 0)
            {
                int sink = await _stock.GetSystemStorageIdAsync(Storage.SysConsumption, ct);
                inputLines = inputLines.Select(r => r with { ToStorageId = sink }).ToList();
                var taken = await _stock.MoveManyAsync(inputLines, StockContext.ForSystem(Component, StockReason.ProductionInput, keyBase + ":in"),
                    StockMoveMode.AllOrNothing, ct);
                if (taken.Status != StockResultStatus.Applied)
                {
                    line.Accrued = record.AccruedBefore;
                    line.Status = ProductionLineStatus.BlockedInputs;
                    line.StatusReason = "The inputs could not be taken from the Input storage.";
                    record.ElapsedSeconds = 0;
                    record.SkippedSeconds = rawSeconds;
                    record.EffectiveRatePerHour = 0;
                    record.AccruedAfter = line.Accrued;
                    record.StatusAfter = line.Status;
                    record.ShortfallReason = line.StatusReason;
                    return record;
                }
                txId = taken.TransactionId;
            }
            foreach (var (i, t) in takes)
            {
                i.Accrued = t.Total - t.Take;
                if (t.Take > 0) consumed.Add(new ConsumedInputDto { ItemBlueprintId = i.ItemBlueprintId, Consumed = t.Take });
            }

            long accepted = 0;
            if (emit > 0)
            {
                int source = await _stock.GetSystemStorageIdAsync(Storage.SysProduction, ct);
                var moved = await _stock.MoveAsync(StockLineRequest.Fungible(source, output.Id, line.ItemBlueprintId, emit),
                    StockContext.ForSystem(Component, StockReason.Production, keyBase + ":out"), StockMoveMode.Partial, ct);
                accepted = moved.Accepted;
                txId = moved.TransactionId ?? txId;
            }
            line.Accrued -= emit; // a rejected part stays in SYS_PRODUCTION: it is never produced (D4)
            if (accepted < emit)
            {
                line.Status = ProductionLineStatus.BlockedFull;
                line.StatusReason = $"The Output storage is full: {accepted} of {emit} accepted.";
                record.ShortfallReason = line.StatusReason;
            }
            else
            {
                line.Status = ProductionLineStatus.Running;
                line.StatusReason = null;
                if (rawSeconds > elapsed)
                    record.ShortfallReason = $"Catch-up capped at {settings.CatchUpCapHours} h: {rawSeconds - elapsed} s not produced.";
            }
            record.AccruedAfter = line.Accrued;
            record.Emitted = emit;
            record.Accepted = accepted;
            record.InputsJson = consumed.Count > 0 ? JsonSerializer.Serialize(consumed, Json) : null;
            record.StatusAfter = line.Status;
            record.StockTransactionId = txId;
            return record;
        }

        /// <summary>The first input the Input storage lacks, as a reason; null when every input is there.</summary>
        private async Task<string?> MissingInputAsync(ProductionLine line, Storage? input, Func<ProductionLineInput, long> need,
            CancellationToken ct)
        {
            if (line.Inputs.Count == 0) return null;
            if (input == null) return "The line needs inputs but the structure has no Input storage.";
            foreach (var i in line.Inputs.OrderBy(i => i.Id))
            {
                long n = need(i);
                if (n <= 0) continue;
                long have = await _db.StockLines.AsNoTracking()
                    .Where(s => s.StorageId == input.Id && s.ItemBlueprintId == i.ItemBlueprintId).Select(s => s.Amount).FirstOrDefaultAsync(ct);
                if (have < n)
                {
                    var name = await _db.ItemBlueprints.AsNoTracking().Where(b => b.Id == i.ItemBlueprintId).Select(b => b.Name).FirstOrDefaultAsync(ct);
                    return $"Not enough {name ?? $"blueprint {i.ItemBlueprintId}"} in the Input storage: needs {n}, has {have}.";
                }
            }
            return null;
        }

        // ================= Overview =================

        public async Task<ProductionOverviewDto?> OverviewAsync(int structureId, CancellationToken ct = default)
        {
            var settings = await _settings.GetAsync();
            if (!await _db.ProductionStructures.AnyAsync(s => s.Id == structureId, ct)) return null;
            if (settings.Enabled) await SettleStructureAsync(structureId, ct);
            var now = Now;

            var structure = await _db.ProductionStructures.AsNoTracking().Include(s => s.ProductionKind)
                .Include(s => s.District).Include(s => s.Town).FirstAsync(s => s.Id == structureId, ct);
            var output = await StorageOfAsync(structureId, StoragePurpose.Output, ct);
            var input = await StorageOfAsync(structureId, StoragePurpose.Input, ct);
            var dto = new ProductionOverviewDto
            {
                ProductionStructureId = structureId,
                FeatureEnabled = settings.Enabled,
                ProductionEnabled = structure.ProductionEnabled,
                ProductionKind = structure.ProductionKind == null ? null
                    : new ProductionKindNavDto { Id = structure.ProductionKind.Id, Name = structure.ProductionKind.Name },
                Level = structure.Level,
                WorkersPresent = structure.WorkersPresent,
                WorkersTotal = structure.WorkersTotal,
                OutputStorageId = output?.Id,
                InputStorageId = input?.Id,
            };
            if (!settings.Enabled) dto.NotProducingReasons.Add("Production is turned off for the whole server (Production:Enabled).");
            if (!structure.ProductionEnabled) dto.NotProducingReasons.Add("The structure's production is switched off.");
            if (output == null) dto.NotProducingReasons.Add("The structure has no Output storage.");

            int? townId = structure.TownId ?? structure.District?.TownId;
            var zone = await _weather.ZoneForTownAsync(townId, ct);
            if (zone != null)
            {
                dto.ClimateZone = zone.Name;
                long day = _weather.DayOf(now, settings.Engine.WeatherDayMinutes);
                var roll = await _db.WeatherRolls.AsNoTracking().Include(r => r.WeatherType)
                    .FirstOrDefaultAsync(r => r.ClimateZoneId == zone.Id && r.Day == day, ct);
                dto.WeatherToday = roll == null ? null : roll.WeatherType?.Name ?? "Clear";
            }

            var lines = await _db.ProductionLines.AsNoTracking().Include(l => l.Inputs).ThenInclude(i => i.ItemBlueprint)
                .Include(l => l.ItemBlueprint).Where(l => l.ProductionStructureId == structureId).OrderBy(l => l.Id).ToListAsync(ct);
            foreach (var line in lines)
            {
                var loaded = await CompleteAsync(line, structure, ct);
                // Only stored rolls are read here: a read never rolls (that is the sweep's job).
                var plan = await PlanWithoutRollingAsync(loaded, now, settings.Engine, ct);
                var lo = new ProductionLineOverviewDto
                {
                    Line = ProductionLineMapping.ToDto(line, structure),
                    BaseRatePerHour = plan.BaseRate,
                    Modifiers = plan.Modifiers,
                    UnlockFactor = plan.Unlock,
                    WorkerFactor = plan.Workers,
                    EffectiveRatePerHour = plan.Effective,
                };
                if (!line.Enabled) lo.NotProducingReasons.Add("The line is paused.");
                if (line.Status is ProductionLineStatus.BlockedFull or ProductionLineStatus.BlockedInputs or ProductionLineStatus.Error
                    && line.StatusReason != null)
                    lo.NotProducingReasons.Add(line.StatusReason);
                if (plan.Unlock == 0m) lo.NotProducingReasons.Add($"Unlocked at level {plan.UnlockLevel} (the structure is level {structure.Level}).");
                if (structure.WorkersTotal <= 0) lo.NotProducingReasons.Add("The structure has no workers.");
                else if (plan.Workers == 0m) lo.NotProducingReasons.Add($"All workers are away (0 of {structure.WorkersTotal} present).");
                else if (plan.Workers < 1m)
                    lo.NotProducingReasons.Add($"Only {structure.WorkersPresent} of {structure.WorkersTotal} workers present: {plan.Workers * 100m:0.#}% output.");
                if (plan.BaseRate <= 0m) lo.NotProducingReasons.Add("The base rate is 0.");
                else if (plan.Effective <= 0m && plan.Unlock > 0m && plan.Workers > 0m)
                    lo.NotProducingReasons.Add("The modifiers bring the rate to 0.");
                dto.Lines.Add(lo);
            }

            var records = await _db.ProductionCycleRecords.AsNoTracking().Where(r => r.ProductionStructureId == structureId)
                .OrderByDescending(r => r.SettledAt).ThenByDescending(r => r.Id).Take(RecentCycleCount).ToListAsync(ct);
            foreach (var r in records) dto.RecentCycles.Add(await RecordDtoAsync(r, ct));
            return dto;
        }

        private async Task<RatePlan> PlanWithoutRollingAsync(Loaded l, DateTime at, ProductionEngineSettingsDto settings, CancellationToken ct)
        {
            // The weather source rolls a missing day; with the feature off nothing may be written, so skip it then.
            if ((await _settings.GetAsync()).Enabled) return await PlanAsync(l, at, settings, ct);
            var ctx = Context(l, at, settings);
            decimal baseRate = _rates.TryGetValue(l.Line.RateSource, out var provider) ? await provider.BaseRatePerHourAsync(ctx, ct) : 0m;
            var modifiers = new List<AppliedModifierDto>();
            foreach (var source in _sources.Where(s => s is not WeatherModifierSource)) modifiers.AddRange(await source.CollectAsync(ctx, ct));
            modifiers = ProductionRate.Ordered(modifiers);
            int? unlockLevel = await UnlockLevelAsync(l.Structure.ProductionKindId, l.Line.ItemBlueprintId, ct);
            decimal unlock = unlockLevel.HasValue && l.Structure.Level < unlockLevel.Value ? 0m : 1m;
            decimal workers = ProductionRate.WorkerFactor(l.Structure.WorkersPresent, l.Structure.WorkersTotal);
            return new RatePlan(baseRate, modifiers, unlock, workers, ProductionRate.Compute(baseRate, modifiers, unlock, workers), unlockLevel);
        }

        private async Task<ProductionCycleRecordDto> RecordDtoAsync(ProductionCycleRecord r, CancellationToken ct)
        {
            var ids = ProductionCycleRecordMapping.BlueprintIds(r).Distinct().ToList();
            var names = await _db.ItemBlueprints.AsNoTracking().Where(b => ids.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name, ct);
            return ProductionCycleRecordMapping.WithNames(r, names,
                await ProductionCycleRecordMapping.StructureNamesAsync(_db, new[] { r.ProductionStructureId }, ct));
        }

        // ================= Workers and upgrades =================

        public async Task<ProductionOverviewDto> SetWorkersPresentAsync(int structureId, int workersPresent, CancellationToken ct = default)
        {
            var structure = await _db.ProductionStructures.AsNoTracking().FirstOrDefaultAsync(s => s.Id == structureId, ct)
                ?? throw new KeyNotFoundException($"ProductionStructure with id {structureId} not found.");
            if (workersPresent < 0 || workersPresent > structure.WorkersTotal)
                throw new ArgumentException($"workersPresent must be 0 to {structure.WorkersTotal} (the structure's workers).");
            await SettleStructureAsync(structureId, ct);
            var tracked = await _db.ProductionStructures.FirstAsync(s => s.Id == structureId, ct);
            tracked.WorkersPresent = workersPresent;
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
            return (await OverviewAsync(structureId, ct))!;
        }

        public async Task<ProductionUpgradeResultDto> UpgradeAsync(int structureId, ProductionUpgradeRequestDto request, int? actorUserId,
            CancellationToken ct = default)
        {
            request ??= new ProductionUpgradeRequestDto();
            var current = await _db.ProductionStructures.AsNoTracking().FirstOrDefaultAsync(s => s.Id == structureId, ct)
                ?? throw new KeyNotFoundException($"ProductionStructure with id {structureId} not found.");
            if (current.ProductionKindId == null)
                throw new InvalidOperationException("The structure has no production kind, so it has no levels to upgrade to.");
            if (current.Level >= ProductionStructure.MaxLevel)
                throw new InvalidOperationException($"The structure is at the highest level ({ProductionStructure.MaxLevel}).");
            int to = current.Level + 1;
            var next = await _db.ProductionKindLevels.AsNoTracking().Include(l => l.Items)
                .FirstOrDefaultAsync(l => l.ProductionKindId == current.ProductionKindId && l.Level == to, ct)
                ?? throw new InvalidOperationException($"The kind has no level {to}.");
            string key = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? $"upgrade:{structureId}:{to}" : request.IdempotencyKey.Trim();
            if (key.Length > CurrencyClientKeys.MaxLength) throw new ArgumentException("idempotencyKey is too long.");

            if (next.UpgradeCoins > 0 && request.PayerUserId is not > 0)
                throw new ArgumentException($"payerUserId is required: level {to} costs {next.UpgradeCoins} coins.");
            if (next.UpgradeCoins > 0 && _currency == null) throw new InvalidOperationException("The currency service is not available.");

            // The time before the upgrade counts at the old level.
            await SettleStructureAsync(structureId, ct);

            var result = new ProductionUpgradeResultDto { ProductionStructureId = structureId, FromLevel = current.Level, ToLevel = to };
            await using var tx = await BeginAsync(ct);
            var structure = await _db.ProductionStructures.FirstAsync(s => s.Id == structureId, ct);
            if (structure.Level != current.Level) throw new InvalidOperationException("The structure's level changed meanwhile; try again.");

            var costs = next.Items.Where(i => i.Role == ProductionKindLevelItemRole.UpgradeCost && i.Amount > 0).OrderBy(i => i.Id).ToList();
            if (costs.Count > 0)
            {
                int from = request.PayFromStorageId ?? (await StorageOfAsync(structureId, StoragePurpose.Output, ct))?.Id
                    ?? throw new ArgumentException("payFromStorageId is required: the structure has no Output storage.");
                int sink = await _stock.GetSystemStorageIdAsync(Storage.SysConsumption, ct);
                var paid = await _stock.MoveManyAsync(costs.Select(c => StockLineRequest.Fungible(from, sink, c.ItemBlueprintId, c.Amount)).ToList(),
                    new StockContext
                    {
                        Reason = StockReason.Upgrade,
                        ActorKind = actorUserId.HasValue ? StockActorKind.User : StockActorKind.System,
                        ActorUserId = actorUserId,
                        ActorComponent = Component,
                        SourceType = nameof(ProductionStructure),
                        SourceRef = structureId.ToString(),
                        Note = $"Upgrade to level {to}",
                        IdempotencyKey = key,
                    }, StockMoveMode.AllOrNothing, ct);
                if (paid.Status != StockResultStatus.Applied)
                {
                    var why = string.Join("; ", paid.Lines.Where(l => l.Rejected > 0)
                        .Select(l => $"blueprint {l.ItemBlueprintId}: {l.RejectReason} ({l.Accepted} of {l.Requested})"));
                    throw new InvalidOperationException($"The upgrade cost could not be paid: {why}.");
                }
                result.StockTransactionId = paid.TransactionId;
            }

            if (next.UpgradeCoins > 0)
            {
                var posting = await _currency!.SpendAsync(request.PayerUserId!.Value, Currency.Coins, next.UpgradeCoins, new CurrencyContext
                {
                    IdempotencyKey = key,
                    ReasonCode = CurrencyReasons.ProductionUpgrade,
                    Initiator = actorUserId.HasValue ? CurrencyInitiator.Admin : CurrencyInitiator.System,
                    InitiatorUserId = actorUserId,
                    InitiatorComponent = actorUserId.HasValue ? null : Component,
                    SourceType = nameof(ProductionStructure),
                    SourceRef = structureId.ToString(),
                }, ct);
                result.CoinsPaid = next.UpgradeCoins;
                result.CurrencyTransactionId = posting.PublicId;
            }

            structure.Level = to;
            await ProductionLevels.ApplyAsync(_db, structure, next, ct);
            await _db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            _db.ChangeTracker.Clear();
            return result;
        }
    }

    /// <summary>Writes a kind level's caps to a structure (PRODUCTION_KINDS § 5): Output storage caps and transport
    /// capacity. A null cap leaves the current value.</summary>
    public static class ProductionLevels
    {
        public static async Task ApplyAsync(KnKDbContext db, ProductionStructure structure, ProductionKindLevel? level,
            CancellationToken ct = default)
        {
            if (level == null) return;
            if (level.TransportCapacity.HasValue) structure.TransportCapacity = level.TransportCapacity.Value;
            if (!level.OutputMaxUnits.HasValue && !level.UnitsPerSlot.HasValue) return;
            if (structure.Id == 0) return; // storages are created after the structure (they get the caps there)
            var output = await db.Storages.Where(s => s.Kind == StorageKind.Domain && s.DomainId == structure.Id
                    && s.Purpose == StoragePurpose.Output).OrderBy(s => s.Id).FirstOrDefaultAsync(ct);
            if (output == null) return;
            if (level.OutputMaxUnits.HasValue) output.MaxUnits = level.OutputMaxUnits.Value;
            if (level.UnitsPerSlot.HasValue) output.UnitsPerSlot = level.UnitsPerSlot.Value;
            // P6a (#683): an Output storage with bound containers takes its cap from them (slots × units per slot).
            await knkwebapi_v2.Services.Stock.StorageContainerCapacity.ApplyAsync(db, output, ct);
        }

        public static Task<ProductionKindLevel?> FindAsync(KnKDbContext db, int? kindId, int level, CancellationToken ct = default) =>
            kindId == null ? Task.FromResult<ProductionKindLevel?>(null)
                : db.ProductionKindLevels.AsNoTracking().FirstOrDefaultAsync(l => l.ProductionKindId == kindId && l.Level == level, ct);
    }

    public static class ProductionLineMapping
    {
        public static ProductionLineDto ToDto(ProductionLine l, ProductionStructure? s = null) => new()
        {
            Id = l.Id,
            ProductionStructureId = l.ProductionStructureId,
            ProductionStructure = s == null && l.ProductionStructure == null ? null
                : new ProductionStructureNavDto { Id = l.ProductionStructureId, Name = (s ?? l.ProductionStructure)!.Name },
            ItemBlueprintId = l.ItemBlueprintId,
            ItemBlueprint = l.ItemBlueprint == null ? null : new ItemBlueprintNavDto { Id = l.ItemBlueprint.Id, Name = l.ItemBlueprint.Name },
            RateSource = l.RateSource.ToString(),
            ConfiguredRatePerHour = l.ConfiguredRatePerHour,
            ResourceCategory = l.ResourceCategory?.ToString(),
            Enabled = l.Enabled,
            Status = l.Status.ToString(),
            StatusReason = l.StatusReason,
            Accrued = l.Accrued,
            AnchorAt = l.AnchorAt,
            LastSettledAt = l.LastSettledAt,
            NextSettleAt = l.NextSettleAt,
            CreatedAt = l.CreatedAt,
            Inputs = l.Inputs.OrderBy(i => i.Id).Select(i => new ProductionLineInputDto
            {
                Id = i.Id, ItemBlueprintId = i.ItemBlueprintId, ItemBlueprintName = i.ItemBlueprint?.Name,
                ItemBlueprint = i.ItemBlueprint == null ? null : new NamedRefDto { Id = i.ItemBlueprint.Id, Name = i.ItemBlueprint.Name },
                AmountPerHour = i.AmountPerHour, Accrued = i.Accrued,
            }).ToList(),
        };
    }

    public static class ProductionCycleRecordMapping
    {
        /// <summary>The blueprints a record names: its output and its consumed inputs (#838: the inputs carry names).</summary>
        public static IEnumerable<int> BlueprintIds(ProductionCycleRecord r) =>
            new[] { r.ItemBlueprintId }.Concat(Inputs(r).Select(i => i.ItemBlueprintId));

        private static List<ConsumedInputDto> Inputs(ProductionCycleRecord r) =>
            string.IsNullOrEmpty(r.InputsJson) ? new() : JsonSerializer.Deserialize<List<ConsumedInputDto>>(r.InputsJson) ?? new();

        public static ProductionCycleRecordDto WithNames(ProductionCycleRecord r, IReadOnlyDictionary<int, string> names,
            IReadOnlyDictionary<int, string>? structureNames = null)
        {
            var dto = ToDto(r, names.GetValueOrDefault(r.ItemBlueprintId), names);
            dto.ProductionStructureName = structureNames?.GetValueOrDefault(r.ProductionStructureId);
            return dto;
        }

        /// <summary>The names of the records' structures (#890).</summary>
        public static Task<Dictionary<int, string>> StructureNamesAsync(KnKDbContext db, IEnumerable<int> structureIds,
            CancellationToken ct = default)
        {
            var ids = structureIds.Distinct().ToList();
            return db.ProductionStructures.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        }

        public static ProductionCycleRecordDto ToDto(ProductionCycleRecord r, string? blueprintName,
            IReadOnlyDictionary<int, string>? names = null) => new()
        {
            Id = r.Id,
            ProductionLineId = r.ProductionLineId,
            ProductionStructureId = r.ProductionStructureId,
            ItemBlueprintId = r.ItemBlueprintId,
            ItemBlueprintName = blueprintName,
            SettledAt = r.SettledAt,
            PeriodStart = r.PeriodStart,
            PeriodEnd = r.PeriodEnd,
            ElapsedSeconds = r.ElapsedSeconds,
            SkippedSeconds = r.SkippedSeconds,
            RateSource = r.RateSource.ToString(),
            BaseRatePerHour = r.BaseRatePerHour,
            Modifiers = JsonSerializer.Deserialize<List<AppliedModifierDto>>(r.ModifiersJson) ?? new(),
            UnlockFactor = r.UnlockFactor,
            WorkersPresent = r.WorkersPresent,
            WorkersTotal = r.WorkersTotal,
            WorkerFactor = r.WorkerFactor,
            EffectiveRatePerHour = r.EffectiveRatePerHour,
            AccruedBefore = r.AccruedBefore,
            AccruedAfter = r.AccruedAfter,
            Emitted = r.Emitted,
            Accepted = r.Accepted,
            Inputs = Inputs(r).Select(i => new ConsumedInputDto
            {
                ItemBlueprintId = i.ItemBlueprintId, Consumed = i.Consumed,
                ItemBlueprintName = names?.GetValueOrDefault(i.ItemBlueprintId) ?? i.ItemBlueprintName,
            }).ToList(),
            StatusAfter = r.StatusAfter.ToString(),
            ShortfallReason = r.ShortfallReason,
            StockTransactionId = r.StockTransactionId,
        };
    }
}
