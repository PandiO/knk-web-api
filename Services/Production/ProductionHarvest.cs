using System;
using System.Collections.Generic;
using System.Data;
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
using knkwebapi_v2.Services.Stock;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Production
{
    // Player harvesting P8a (PandiO/knk-workspace#685, PRODUCTION_KINDS.md § 6-7, decision record § 3.6, § 11 F9-F10).
    // Validation errors are ArgumentException (400), missing rows KeyNotFoundException (404), conflicts
    // InvalidOperationException (409), the feature switched off ProductionOffException (409 "ProductionDisabled").

    /// <summary>The pool arithmetic (PRODUCTION_KINDS § 6.3). Pure functions, so the engine, the harvest service and the
    /// net-output check agree.</summary>
    public static class ResourcePoolMath
    {
        public const int Decimals = 6;

        /// <summary>The units in the pool at <paramref name="at"/>: the stored value plus regeneration since it was written,
        /// at most the capacity. Nothing is written.</summary>
        public static decimal CurrentAt(ResourcePool pool, DateTime at)
        {
            if (pool.Capacity <= 0) return 0m;
            decimal hours = at > pool.LastRegenAt ? (decimal)(at - pool.LastRegenAt).TotalHours : 0m;
            decimal current = pool.Current + Math.Max(0m, pool.RegenPerHour) * hours;
            return Math.Round(Math.Clamp(current, 0m, pool.Capacity), Decimals);
        }

        /// <summary>Current ÷ Capacity in [0, 1]; a pool without capacity counts as full (it limits nothing).</summary>
        public static decimal Fill(decimal current, decimal capacity) =>
            capacity <= 0 ? 1m : Math.Round(Math.Clamp(current / capacity, 0m, 1m), Decimals);

        /// <summary><c>floor + (1 − floor) × fill</c> (P-D6: floor 0.5).</summary>
        public static decimal Factor(decimal floor, decimal fill)
        {
            floor = Math.Clamp(floor, 0m, 1m);
            return Math.Round(floor + (1m - floor) * Math.Clamp(fill, 0m, 1m), Decimals);
        }

        /// <summary>The structure's pool factor from all its pools: their combined fill (Σ current ÷ Σ capacity) through the
        /// kind's floor; 1 when it has no pool with capacity.</summary>
        public static decimal StructureFactor(IEnumerable<ResourcePool> pools, decimal floor, DateTime at)
        {
            decimal current = 0m, capacity = 0m;
            foreach (var p in pools.Where(p => p.Capacity > 0))
            {
                current += CurrentAt(p, at);
                capacity += p.Capacity;
            }
            return capacity <= 0 ? 1m : Factor(floor, Fill(current, capacity));
        }

        /// <summary>Hours until full at the regeneration rate; null when full or not regenerating.</summary>
        public static decimal? HoursToFull(decimal current, decimal capacity, decimal regenPerHour) =>
            capacity <= 0 || current >= capacity || regenPerHour <= 0 ? null : Math.Round((capacity - current) / regenPerHour, 2);

        /// <summary>
        /// The net-output check (§ 6.3, F9): the production lost through the lower pool factor until regeneration refills
        /// one break's pool cost, <c>Σ base × (1 − floor) × (cost / capacity) × (cost / regen) / 2</c>; null when the pool
        /// never refills it (no capacity or no regeneration).
        /// </summary>
        public static decimal? LostPerBreak(decimal baseRatePerHour, decimal floor, decimal poolCost, decimal capacity, decimal regenPerHour)
        {
            if (poolCost <= 0) return 0m;
            if (capacity <= 0 || regenPerHour <= 0) return null;
            decimal lost = baseRatePerHour * (1m - Math.Clamp(floor, 0m, 1m)) * (poolCost / capacity) * (poolCost / regenPerHour) / 2m;
            return Math.Round(lost, Decimals);
        }

        /// <summary>The regeneration time handed to the game server: the rule's, up to twice as long at an empty pool when
        /// the rule scales with the pool (§ 7).</summary>
        public static int RegenSeconds(HarvestRule rule, decimal fillAfter) =>
            rule.RegenScalesWithPool ? (int)Math.Round(rule.RegenSeconds * (2m - Math.Clamp(fillAfter, 0m, 1m))) : rule.RegenSeconds;

        /// <summary>Breaks × yield per break × (1 + fortune bonus × fortune level, the level counted up to IV).</summary>
        public static decimal Yield(HarvestRule rule, int breaks, int fortuneLevel)
        {
            int fortune = Math.Clamp(fortuneLevel, 0, HarvestRule.MaxFortuneLevel);
            return Math.Round(breaks * rule.YieldPerBreak * (1m + rule.FortuneBonusPerLevel * fortune), Decimals);
        }
    }

    // ===================================================================== Harvest rules

    public interface IHarvestRuleService
    {
        Task<IEnumerable<HarvestRuleDto>> GetAllAsync();
        Task<HarvestRuleDto?> GetByIdAsync(int id);
        Task<HarvestRuleDto> CreateAsync(HarvestRuleDto dto);
        Task UpdateAsync(int id, HarvestRuleDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<HarvestRuleListDto>> SearchAsync(PagedQueryDto query);

        /// <summary>The net-output check (§ 6.3, F9) per area of the rule's kind.</summary>
        Task<HarvestRuleBalanceDto> BalanceAsync(int id, CancellationToken ct = default);
    }

    /// <summary>Harvest rules per kind (PRODUCTION_KINDS § 6.1). CRUD works while <c>Production:Enabled</c> is off.</summary>
    public sealed class HarvestRuleService : IHarvestRuleService
    {
        private static readonly Regex TargetPattern = new("^\\*?[A-Z0-9_]+\\*?$", RegexOptions.Compiled);
        private static readonly Regex MaterialPattern = new("^[A-Z0-9_]+$", RegexOptions.Compiled);
        private static readonly Regex PermissionPattern = new("^[a-z0-9_.*\\-]+$", RegexOptions.Compiled);

        private readonly KnKDbContext _db;
        private readonly IProductionSettingsReader _settings;
        private readonly IProductionYieldCalculator _yield;
        private readonly TimeProvider _clock;

        public HarvestRuleService(KnKDbContext db, IProductionSettingsReader settings, IProductionYieldCalculator yield,
            TimeProvider? clock = null)
        {
            _db = db;
            _settings = settings;
            _yield = yield;
            _clock = clock ?? TimeProvider.System;
        }

        private IQueryable<HarvestRule> Query() => _db.Set<HarvestRule>().AsNoTracking()
            .Include(r => r.ProductionKind).Include(r => r.YieldItemBlueprint).Include(r => r.MinTitleBracket);

        public static HarvestRuleDto ToDto(HarvestRule r) => new()
        {
            Id = r.Id,
            Name = r.Name,
            ProductionKindId = r.ProductionKindId,
            ProductionKind = r.ProductionKind == null ? null : new ProductionKindNavDto { Id = r.ProductionKindId, Name = r.ProductionKind.Name },
            Enabled = r.Enabled,
            TargetKind = r.TargetKind.ToString(),
            Targets = Targets(r.TargetsJson),
            MinBlockAge = r.MinBlockAge,
            RequiredPermission = r.RequiredPermission,
            MinTitleBracketId = r.MinTitleBracketId,
            MinTitleBracket = r.MinTitleBracket == null ? null : new NamedRefDto { Id = r.MinTitleBracket.Id, Name = r.MinTitleBracket.MaleName },
            ToolClass = r.ToolClass.ToString(),
            MinToolTier = r.MinToolTier.ToString(),
            YieldItemBlueprintId = r.YieldItemBlueprintId,
            YieldItemBlueprint = r.YieldItemBlueprint == null ? null : new NamedRefDto { Id = r.YieldItemBlueprintId, Name = r.YieldItemBlueprint.Name },
            YieldPerBreak = r.YieldPerBreak,
            FortuneBonusPerLevel = r.FortuneBonusPerLevel,
            WorkerShare = r.WorkerShare,
            PoolCost = r.PoolCost,
            CooldownSeconds = r.CooldownSeconds,
            Placeholder = r.Placeholder.ToString(),
            PlaceholderMaterial = r.PlaceholderMaterial,
            RegenSeconds = r.RegenSeconds,
            RegenScalesWithPool = r.RegenScalesWithPool,
            Note = r.Note,
            CreatedAt = r.CreatedAt,
        };

        public static List<string> Targets(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<string>();
            try
            {
                return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            catch (JsonException)
            {
                return new List<string>();
            }
        }

        public async Task<IEnumerable<HarvestRuleDto>> GetAllAsync() =>
            (await Query().OrderBy(r => r.ProductionKindId).ThenBy(r => r.Name).ToListAsync()).Select(ToDto).ToList();

        public async Task<HarvestRuleDto?> GetByIdAsync(int id)
        {
            var r = await Query().FirstOrDefaultAsync(x => x.Id == id);
            return r == null ? null : ToDto(r);
        }

        public async Task<HarvestRuleDto> CreateAsync(HarvestRuleDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var rule = new HarvestRule { CreatedAt = _clock.GetUtcNow().UtcDateTime };
            await ApplyAsync(rule, dto, isCreate: true);
            _db.Set<HarvestRule>().Add(rule);
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
            return (await GetByIdAsync(rule.Id))!;
        }

        public async Task UpdateAsync(int id, HarvestRuleDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var rule = await _db.Set<HarvestRule>().FirstOrDefaultAsync(r => r.Id == id)
                ?? throw new KeyNotFoundException($"HarvestRule with id {id} not found.");
            await ApplyAsync(rule, dto, isCreate: false);
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
        }

        private async Task ApplyAsync(HarvestRule r, HarvestRuleDto dto, bool isCreate)
        {
            if (isCreate || dto.ProductionKindId.HasValue)
            {
                if (dto.ProductionKindId is not > 0) throw new ArgumentException("productionKindId is required.");
                if (!isCreate && dto.ProductionKindId != r.ProductionKindId)
                    throw new ArgumentException("A harvest rule can't move to another kind; make a new rule.");
                if (!await _db.ProductionKinds.AnyAsync(k => k.Id == dto.ProductionKindId))
                    throw new ArgumentException($"ProductionKind with id {dto.ProductionKindId} not found.");
                r.ProductionKindId = dto.ProductionKindId.Value;
            }
            if (isCreate || dto.Name != null)
            {
                var name = ProductionInput.Name(dto.Name, HarvestRule.NameMaxLength);
                if (await _db.Set<HarvestRule>().AnyAsync(x => x.Id != r.Id && x.ProductionKindId == r.ProductionKindId && x.Name == name))
                    throw new ArgumentException($"The kind already has a harvest rule named \"{name}\".");
                r.Name = name;
            }
            if (dto.Enabled.HasValue) r.Enabled = dto.Enabled.Value;
            if (dto.TargetKind != null) r.TargetKind = ProductionInput.Parse<HarvestTargetKind>(dto.TargetKind, "targetKind");
            if (dto.Targets != null) r.TargetsJson = JsonSerializer.Serialize(NormalizeTargets(dto.Targets));
            else if (isCreate) throw new ArgumentException("targets is required: the materials or entity types players may harvest.");
            if (dto.MinBlockAge.HasValue)
            {
                if (dto.MinBlockAge.Value is < 0 or > 25) throw new ArgumentException("minBlockAge must be 0 to 25 (0 clears it).");
                r.MinBlockAge = dto.MinBlockAge.Value > 0 ? dto.MinBlockAge.Value : null;
            }
            if (dto.RequiredPermission != null)
            {
                var node = ProductionInput.Text(dto.RequiredPermission, HarvestRule.PermissionMaxLength, "requiredPermission")?.ToLowerInvariant();
                if (node != null && !PermissionPattern.IsMatch(node))
                    throw new ArgumentException("requiredPermission is a permission node such as knk.harvest.mine (letters, digits, . _ - *).");
                r.RequiredPermission = node;
            }
            if (dto.MinTitleBracketId.HasValue)
            {
                int? bracket = ProductionInput.OptionalPositive(dto.MinTitleBracketId.Value, "minTitleBracketId");
                if (bracket.HasValue && !await _db.TitleBrackets.AnyAsync(b => b.Id == bracket))
                    throw new ArgumentException($"TitleBracket with id {bracket} not found.");
                r.MinTitleBracketId = bracket;
            }
            if (dto.ToolClass != null) r.ToolClass = ProductionInput.Parse<HarvestToolClass>(dto.ToolClass, "toolClass");
            if (dto.MinToolTier != null) r.MinToolTier = ProductionInput.Parse<HarvestToolTier>(dto.MinToolTier, "minToolTier");
            if (isCreate || dto.YieldItemBlueprintId.HasValue)
            {
                if (dto.YieldItemBlueprintId is not > 0) throw new ArgumentException("yieldItemBlueprintId is required: what a break yields.");
                if (!await _db.ItemBlueprints.AnyAsync(b => b.Id == dto.YieldItemBlueprintId))
                    throw new ArgumentException($"ItemBlueprint with id {dto.YieldItemBlueprintId} not found.");
                r.YieldItemBlueprintId = dto.YieldItemBlueprintId.Value;
            }
            if (dto.YieldPerBreak.HasValue)
            {
                if (dto.YieldPerBreak.Value is <= 0 or > HarvestRule.MaxYieldPerBreak)
                    throw new ArgumentException($"yieldPerBreak must be above 0 and at most {HarvestRule.MaxYieldPerBreak}.");
                r.YieldPerBreak = Math.Round(dto.YieldPerBreak.Value, ResourcePoolMath.Decimals);
            }
            if (dto.FortuneBonusPerLevel.HasValue)
            {
                if (dto.FortuneBonusPerLevel.Value is < 0 or > HarvestRule.MaxFortuneBonusPerLevel)
                    throw new ArgumentException($"fortuneBonusPerLevel must be 0 to {HarvestRule.MaxFortuneBonusPerLevel}.");
                r.FortuneBonusPerLevel = Math.Round(dto.FortuneBonusPerLevel.Value, ResourcePoolMath.Decimals);
            }
            if (dto.WorkerShare.HasValue)
            {
                if (dto.WorkerShare.Value is < 0 or > 1) throw new ArgumentException("workerShare must be 0 to 1 (suggested 0.25).");
                r.WorkerShare = Math.Round(dto.WorkerShare.Value, ResourcePoolMath.Decimals);
            }
            if (dto.PoolCost.HasValue)
            {
                if (dto.PoolCost.Value is < 0 or > HarvestRule.MaxPoolCost)
                    throw new ArgumentException($"poolCost must be 0 to {HarvestRule.MaxPoolCost}.");
                r.PoolCost = Math.Round(dto.PoolCost.Value, ResourcePoolMath.Decimals);
            }
            if (dto.CooldownSeconds.HasValue)
            {
                if (dto.CooldownSeconds.Value is < 0 or > HarvestRule.MaxCooldownSeconds)
                    throw new ArgumentException($"cooldownSeconds must be 0 to {HarvestRule.MaxCooldownSeconds} (0 clears it).");
                r.CooldownSeconds = dto.CooldownSeconds.Value > 0 ? dto.CooldownSeconds.Value : null;
            }
            if (dto.Placeholder != null) r.Placeholder = ProductionInput.Parse<HarvestPlaceholder>(dto.Placeholder, "placeholder");
            if (dto.PlaceholderMaterial != null)
            {
                var m = ProductionInput.Text(dto.PlaceholderMaterial, HarvestRule.MaterialMaxLength, "placeholderMaterial")?.ToUpperInvariant();
                if (m != null && !MaterialPattern.IsMatch(m)) throw new ArgumentException("placeholderMaterial is a material name such as STONE.");
                r.PlaceholderMaterial = m;
            }
            if (dto.RegenSeconds.HasValue)
            {
                if (dto.RegenSeconds.Value is < 1 or > HarvestRule.MaxRegenSeconds)
                    throw new ArgumentException($"regenSeconds must be 1 to {HarvestRule.MaxRegenSeconds}.");
                r.RegenSeconds = dto.RegenSeconds.Value;
            }
            if (dto.RegenScalesWithPool.HasValue) r.RegenScalesWithPool = dto.RegenScalesWithPool.Value;
            if (dto.Note != null) r.Note = ProductionInput.Text(dto.Note, HarvestRule.NoteMaxLength, "note");
        }

        private static List<string> NormalizeTargets(List<string> targets)
        {
            var list = new List<string>();
            foreach (var t in targets)
            {
                var v = t?.Trim().ToUpperInvariant();
                if (string.IsNullOrEmpty(v)) continue;
                if (v.Length > HarvestRule.TargetMaxLength || !TargetPattern.IsMatch(v))
                    throw new ArgumentException($"targets: \"{t}\" is not a material or entity type name (letters, digits and _, may start or end with *).");
                if (!list.Contains(v)) list.Add(v);
            }
            if (list.Count == 0) throw new ArgumentException("targets needs at least one material or entity type.");
            if (list.Count > HarvestRule.MaxTargets) throw new ArgumentException($"targets has at most {HarvestRule.MaxTargets} names.");
            return list;
        }

        public async Task DeleteAsync(int id)
        {
            var rule = await _db.Set<HarvestRule>().FirstOrDefaultAsync(r => r.Id == id)
                ?? throw new KeyNotFoundException($"HarvestRule with id {id} not found.");
            if (await _db.Set<PlayerHarvest>().AnyAsync(h => h.HarvestRuleId == id))
                throw new InvalidOperationException("The rule has booked harvests (history); switch it off instead of deleting it.");
            _db.Set<HarvestRule>().Remove(rule);
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
        }

        public async Task<PagedResultDto<HarvestRuleListDto>> SearchAsync(PagedQueryDto query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            IQueryable<HarvestRule> q = _db.Set<HarvestRule>().AsNoTracking();
            if (TerritoryPaging.Term(query) is { } term)
                q = q.Where(r => r.Name.ToLower().Contains(term) || r.ProductionKind.Name.ToLower().Contains(term)
                    || r.YieldItemBlueprint.Name.ToLower().Contains(term));
            if (TerritoryPaging.IntFilter(query, "productionKindId") is { } kid) q = q.Where(r => r.ProductionKindId == kid);
            if (ProductionInput.Flag(query, "enabled") is { } enabled) q = q.Where(r => r.Enabled == enabled);
            q = (query.SortBy ?? "name").ToLowerInvariant() switch
            {
                "id" => ProductionInput.Sort(q, query.SortDescending, r => r.Id),
                "productionkindname" => ProductionInput.Sort(q, query.SortDescending, r => r.ProductionKind.Name),
                _ => ProductionInput.Sort(q, query.SortDescending, r => r.Name),
            };
            return await TerritoryPaging.PageAsync(q.Select(r => new HarvestRuleListDto
            {
                Id = r.Id, Name = r.Name, ProductionKindId = r.ProductionKindId, ProductionKindName = r.ProductionKind.Name,
                Enabled = r.Enabled, TargetKind = r.TargetKind.ToString(), YieldItemBlueprintId = r.YieldItemBlueprintId,
                YieldItemBlueprintName = r.YieldItemBlueprint.Name, YieldPerBreak = r.YieldPerBreak, WorkerShare = r.WorkerShare,
                PoolCost = r.PoolCost,
            }), query);
        }

        public async Task<HarvestRuleBalanceDto> BalanceAsync(int id, CancellationToken ct = default)
        {
            var rule = await _db.Set<HarvestRule>().AsNoTracking().Include(r => r.ProductionKind).FirstOrDefaultAsync(r => r.Id == id, ct)
                ?? throw new KeyNotFoundException($"HarvestRule with id {id} not found.");
            var harvest = (await _settings.GetAsync()).Harvest ?? new ProductionHarvestSettingsDto();
            var now = _clock.GetUtcNow().UtcDateTime;
            decimal townShare = Math.Round(rule.YieldPerBreak * (1m - rule.WorkerShare), ResourcePoolMath.Decimals);
            decimal floor = rule.ProductionKind?.PoolFloor ?? ProductionKind.DefaultPoolFloor;
            var dto = new HarvestRuleBalanceDto { HarvestRuleId = rule.Id, HarvestRuleName = rule.Name, TownSharePerBreak = townShare, NetPositive = true };

            var structures = await _db.ProductionStructures.AsNoTracking().Where(s => s.ProductionKindId == rule.ProductionKindId)
                .OrderBy(s => s.Id).Select(s => new { s.Id, s.Name }).ToListAsync(ct);
            foreach (var s in structures)
            {
                decimal baseRate = await StructureBaseRateAsync(s.Id, ct);
                var areas = await _db.ProductionAreas.AsNoTracking().Where(a => a.ProductionStructureId == s.Id).OrderBy(a => a.Id).ToListAsync(ct);
                foreach (var a in areas)
                {
                    var pool = await _db.Set<ResourcePool>().AsNoTracking().FirstOrDefaultAsync(p => p.ProductionAreaId == a.Id, ct);
                    var (capacity, regen) = pool != null ? (pool.Capacity, pool.RegenPerHour)
                        : await ResourcePoolService.DefaultsAsync(_db, a, harvest, ct);
                    decimal? lost = ResourcePoolMath.LostPerBreak(baseRate, floor, rule.PoolCost, capacity, regen);
                    bool ok = lost.HasValue && lost.Value < townShare;
                    dto.Areas.Add(new HarvestAreaBalanceDto
                    {
                        ProductionAreaId = a.Id, ProductionAreaName = a.Name, ProductionStructureId = s.Id, ProductionStructureName = s.Name,
                        BaseRatePerHour = baseRate, PoolFloor = floor, PoolCapacity = capacity, RegenPerHour = regen, PoolExists = pool != null,
                        TownSharePerBreak = townShare, LostPerBreak = lost, NetPositive = ok,
                    });
                    if (!ok)
                    {
                        dto.NetPositive = false;
                        dto.Warnings.Add(lost.HasValue
                            ? $"{s.Name} / {a.Name}: a break costs the structure about {lost.Value:0.####} units of production until the pool refills, more than the town's share of {townShare:0.####}."
                            : $"{s.Name} / {a.Name}: the pool does not regenerate (or has no capacity), so harvesting lowers production for good.");
                    }
                }
            }
            return dto;
        }

        /// <summary>Σ over the structure's lines of the base rate per hour (Configured: the configured rate; Derived and
        /// Hybrid: the yield calculation), before modifiers and built-in steps (§ 6.3 <c>Σo base(o)</c>).</summary>
        private async Task<decimal> StructureBaseRateAsync(int structureId, CancellationToken ct)
        {
            var lines = await _db.ProductionLines.AsNoTracking().Where(l => l.ProductionStructureId == structureId && l.Enabled)
                .Select(l => new { l.ItemBlueprintId, l.RateSource, l.ConfiguredRatePerHour }).ToListAsync(ct);
            if (lines.Count == 0) return 0m;
            YieldComputation? derived = null, hybrid = null;
            decimal sum = 0m;
            foreach (var l in lines)
            {
                switch (l.RateSource)
                {
                    case ProductionRateSource.Configured:
                        sum += l.ConfiguredRatePerHour;
                        break;
                    case ProductionRateSource.Derived:
                        derived ??= await _yield.ComputeAsync(structureId, hybrid: false, ct);
                        sum += derived.BaseRate(l.ItemBlueprintId);
                        break;
                    case ProductionRateSource.Hybrid:
                        hybrid ??= await _yield.ComputeAsync(structureId, hybrid: true, ct);
                        sum += hybrid.BaseRate(l.ItemBlueprintId);
                        break;
                }
            }
            return Math.Round(sum, ResourcePoolMath.Decimals);
        }
    }

    // ===================================================================== Resource pools

    public interface IResourcePoolService
    {
        Task<IEnumerable<ResourcePoolDto>> GetAllAsync();
        Task<ResourcePoolDto?> GetByIdAsync(int id);
        Task<ResourcePoolDto> CreateAsync(ResourcePoolDto dto);
        Task UpdateAsync(int id, ResourcePoolDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<ResourcePoolListDto>> SearchAsync(PagedQueryDto query);
    }

    /// <summary>
    /// Resource pools (PRODUCTION_KINDS § 6.3). One per area; the first harvest makes it (capacity from the latest
    /// survey's total units, else the default; full; regenerating its capacity in the default hours), staff may make or
    /// tune it. A change settles the structure first, so the time before counts at the old pool factor. CRUD works while
    /// <c>Production:Enabled</c> is off.
    /// </summary>
    public sealed class ResourcePoolService : IResourcePoolService
    {
        private readonly KnKDbContext _db;
        private readonly IProductionSettingsReader _settings;
        private readonly IProductionEngine _engine;
        private readonly TimeProvider _clock;

        public ResourcePoolService(KnKDbContext db, IProductionSettingsReader settings, IProductionEngine engine, TimeProvider? clock = null)
        {
            _db = db;
            _settings = settings;
            _engine = engine;
            _clock = clock ?? TimeProvider.System;
        }

        private DateTime Now => _clock.GetUtcNow().UtcDateTime;

        /// <summary>A new pool's capacity and regeneration for an area: the latest survey's total units (else the default
        /// capacity) and capacity ÷ the default hours to full.</summary>
        public static async Task<(decimal Capacity, decimal RegenPerHour)> DefaultsAsync(KnKDbContext db, ProductionArea area,
            ProductionHarvestSettingsDto settings, CancellationToken ct = default)
        {
            long total = area.LastSurveyId == null ? 0
                : await db.AreaSurveys.AsNoTracking().Where(s => s.Id == area.LastSurveyId).Select(s => s.TotalUnits).FirstOrDefaultAsync(ct);
            decimal capacity = total > 0 ? total : settings.DefaultPoolCapacity;
            decimal hours = settings.DefaultRegenHoursToFull > 0 ? settings.DefaultRegenHoursToFull : 12m;
            return (capacity, Math.Round(capacity / hours, ResourcePoolMath.Decimals));
        }

        /// <summary>Makes the area's pool (unsaved) with the defaults, full.</summary>
        public static async Task<ResourcePool> NewPoolAsync(KnKDbContext db, ProductionArea area, ProductionHarvestSettingsDto settings,
            DateTime now, CancellationToken ct = default)
        {
            var (capacity, regen) = await DefaultsAsync(db, area, settings, ct);
            return new ResourcePool
            {
                ProductionAreaId = area.Id, ProductionStructureId = area.ProductionStructureId, Capacity = capacity, Current = capacity,
                RegenPerHour = regen, CapacityFromSurvey = true, CapacitySurveyId = area.LastSurveyId, LastRegenAt = now,
                CreatedAt = now, UpdatedAt = now,
            };
        }

        /// <summary>A new survey of the area (§ 6.3 "Capacity, derived from the survey"): with <c>CapacityFromSurvey</c> on,
        /// the capacity becomes the survey's total units; the fill and the hours to full are kept. A survey of 0 units
        /// changes nothing. Called by the survey report on the tracked pool.</summary>
        public static void ApplySurvey(ResourcePool pool, long totalUnits, long surveyId, DateTime now)
        {
            if (!pool.CapacityFromSurvey || totalUnits <= 0) return;
            decimal current = ResourcePoolMath.CurrentAt(pool, now);
            decimal fill = ResourcePoolMath.Fill(current, pool.Capacity);
            decimal capacity = Math.Min(totalUnits, ResourcePool.MaxCapacity);
            if (pool.Capacity > 0) pool.RegenPerHour = Math.Round(pool.RegenPerHour * capacity / pool.Capacity, ResourcePoolMath.Decimals);
            pool.Capacity = capacity;
            pool.Current = Math.Round(fill * capacity, ResourcePoolMath.Decimals);
            pool.CapacitySurveyId = surveyId;
            pool.LastRegenAt = now;
            pool.UpdatedAt = now;
        }

        private IQueryable<ResourcePool> Query() =>
            _db.Set<ResourcePool>().AsNoTracking().Include(p => p.ProductionArea).ThenInclude(a => a.ProductionStructure)
                .ThenInclude(s => s.ProductionKind);

        private ResourcePoolDto ToDto(ResourcePool p)
        {
            var now = Now;
            decimal current = ResourcePoolMath.CurrentAt(p, now);
            decimal fill = ResourcePoolMath.Fill(current, p.Capacity);
            decimal floor = p.ProductionArea?.ProductionStructure?.ProductionKind?.PoolFloor ?? ProductionKind.DefaultPoolFloor;
            return new ResourcePoolDto
            {
                Id = p.Id,
                ProductionAreaId = p.ProductionAreaId,
                ProductionArea = p.ProductionArea == null ? null : new NamedRefDto { Id = p.ProductionAreaId, Name = p.ProductionArea.Name },
                ProductionStructureId = p.ProductionStructureId,
                ProductionStructure = p.ProductionArea?.ProductionStructure == null ? null
                    : new ProductionStructureNavDto { Id = p.ProductionStructureId, Name = p.ProductionArea.ProductionStructure.Name },
                Capacity = p.Capacity,
                Current = current,
                RegenPerHour = p.RegenPerHour,
                CapacityFromSurvey = p.CapacityFromSurvey,
                CapacitySurveyId = p.CapacitySurveyId,
                Fill = fill,
                PoolFloor = floor,
                PoolFactor = p.Capacity > 0 ? ResourcePoolMath.Factor(floor, fill) : 1m,
                HoursToFull = ResourcePoolMath.HoursToFull(current, p.Capacity, p.RegenPerHour),
                LastRegenAt = p.LastRegenAt,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt,
            };
        }

        public async Task<IEnumerable<ResourcePoolDto>> GetAllAsync() =>
            (await Query().OrderBy(p => p.Id).ToListAsync()).Select(ToDto).ToList();

        public async Task<ResourcePoolDto?> GetByIdAsync(int id)
        {
            var p = await Query().FirstOrDefaultAsync(x => x.Id == id);
            return p == null ? null : ToDto(p);
        }

        public async Task<ResourcePoolDto> CreateAsync(ResourcePoolDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (dto.ProductionAreaId is not > 0) throw new ArgumentException("productionAreaId is required.");
            var area = await _db.ProductionAreas.AsNoTracking().FirstOrDefaultAsync(a => a.Id == dto.ProductionAreaId)
                ?? throw new ArgumentException($"ProductionArea with id {dto.ProductionAreaId} not found.");
            if (await _db.Set<ResourcePool>().AnyAsync(p => p.ProductionAreaId == area.Id))
                throw new InvalidOperationException("The area already has a resource pool (one per area).");
            var harvest = (await _settings.GetAsync()).Harvest ?? new ProductionHarvestSettingsDto();
            await _engine.SettleStructureAsync(area.ProductionStructureId);
            _db.ChangeTracker.Clear();
            var now = Now;
            var pool = await NewPoolAsync(_db, area, harvest, now);
            bool typedCapacity = dto.Capacity.HasValue;
            Apply(pool, dto, now);
            if (typedCapacity && !dto.Current.HasValue) pool.Current = pool.Capacity;
            if (typedCapacity && !dto.RegenPerHour.HasValue)
                pool.RegenPerHour = Math.Round(pool.Capacity / (harvest.DefaultRegenHoursToFull > 0 ? harvest.DefaultRegenHoursToFull : 12m),
                    ResourcePoolMath.Decimals);
            if (typedCapacity && !dto.CapacityFromSurvey.HasValue) pool.CapacityFromSurvey = false;
            _db.Set<ResourcePool>().Add(pool);
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
            return (await GetByIdAsync(pool.Id))!;
        }

        public async Task UpdateAsync(int id, ResourcePoolDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var existing = await _db.Set<ResourcePool>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == id)
                ?? throw new KeyNotFoundException($"ResourcePool with id {id} not found.");
            if (dto.ProductionAreaId.HasValue && dto.ProductionAreaId != existing.ProductionAreaId)
                throw new ArgumentException("A pool can't move to another area.");
            await _engine.SettleStructureAsync(existing.ProductionStructureId);
            _db.ChangeTracker.Clear();
            var pool = await _db.Set<ResourcePool>().FirstAsync(p => p.Id == id);
            var now = Now;
            // Bring the stored value to now first, so a changed rate or capacity applies from now on.
            pool.Current = ResourcePoolMath.CurrentAt(pool, now);
            pool.LastRegenAt = now;
            Apply(pool, dto, now);
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
        }

        private static void Apply(ResourcePool pool, ResourcePoolDto dto, DateTime now)
        {
            if (dto.Capacity.HasValue)
            {
                if (dto.Capacity.Value is <= 0 or > ResourcePool.MaxCapacity)
                    throw new ArgumentException($"capacity must be above 0 and at most {ResourcePool.MaxCapacity}.");
                pool.Capacity = Math.Round(dto.Capacity.Value, ResourcePoolMath.Decimals);
            }
            if (dto.RegenPerHour.HasValue)
            {
                if (dto.RegenPerHour.Value is < 0 or > ResourcePool.MaxCapacity)
                    throw new ArgumentException($"regenPerHour must be 0 to {ResourcePool.MaxCapacity}.");
                pool.RegenPerHour = Math.Round(dto.RegenPerHour.Value, ResourcePoolMath.Decimals);
            }
            if (dto.Current.HasValue)
            {
                if (dto.Current.Value < 0) throw new ArgumentException("current can't be negative.");
                pool.Current = Math.Round(dto.Current.Value, ResourcePoolMath.Decimals);
            }
            if (dto.CapacityFromSurvey.HasValue) pool.CapacityFromSurvey = dto.CapacityFromSurvey.Value;
            if (pool.Current > pool.Capacity) pool.Current = pool.Capacity;
            pool.UpdatedAt = now;
        }

        public async Task DeleteAsync(int id)
        {
            var existing = await _db.Set<ResourcePool>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == id)
                ?? throw new KeyNotFoundException($"ResourcePool with id {id} not found.");
            await _engine.SettleStructureAsync(existing.ProductionStructureId);
            _db.ChangeTracker.Clear();
            _db.Set<ResourcePool>().Remove(await _db.Set<ResourcePool>().FirstAsync(p => p.Id == id));
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
        }

        public async Task<PagedResultDto<ResourcePoolListDto>> SearchAsync(PagedQueryDto query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            IQueryable<ResourcePool> q = _db.Set<ResourcePool>().AsNoTracking();
            if (TerritoryPaging.Term(query) is { } term)
                q = q.Where(p => p.ProductionArea.Name.ToLower().Contains(term) || p.ProductionArea.ProductionStructure.Name.ToLower().Contains(term));
            if (TerritoryPaging.IntFilter(query, "productionStructureId") is { } sid) q = q.Where(p => p.ProductionStructureId == sid);
            if (TerritoryPaging.IntFilter(query, "productionAreaId") is { } aid) q = q.Where(p => p.ProductionAreaId == aid);
            q = (query.SortBy ?? "id").ToLowerInvariant() switch
            {
                "name" => ProductionInput.Sort(q, query.SortDescending, p => p.ProductionArea.ProductionStructure.Name),
                "capacity" => ProductionInput.Sort(q, query.SortDescending, p => p.Capacity),
                _ => ProductionInput.Sort(q, query.SortDescending, p => p.Id),
            };
            return await TerritoryPaging.PageAsync(q.Select(p => new ResourcePoolListDto
            {
                Id = p.Id, Name = p.ProductionArea.ProductionStructure.Name + " / " + p.ProductionArea.Name,
                ProductionAreaId = p.ProductionAreaId, ProductionAreaName = p.ProductionArea.Name,
                ProductionStructureId = p.ProductionStructureId, ProductionStructureName = p.ProductionArea.ProductionStructure.Name,
                Capacity = p.Capacity, Current = p.Current, RegenPerHour = p.RegenPerHour, LastRegenAt = p.LastRegenAt,
            }), query);
        }
    }

    // ===================================================================== Harvest reports and the catalogue

    public interface IHarvestService
    {
        /// <summary>Books a batch of harvests (each entry once per idempotency key). 409 while <c>Production:Enabled</c> is off.</summary>
        Task<HarvestReportResultDto> ReportAsync(HarvestReportDto report, CancellationToken ct = default);

        /// <summary>The enabled rules and the areas they apply to (empty while the feature is off).</summary>
        Task<HarvestCatalogDto> CatalogAsync(CancellationToken ct = default);

        Task<PlayerHarvestDto?> GetAsync(long id, CancellationToken ct = default);

        Task<PagedResultDto<PlayerHarvestListDto>> SearchAsync(PagedQueryDto query, CancellationToken ct = default);
    }

    /// <summary>
    /// Player harvesting (PRODUCTION_KINDS § 6.2-6.3). Each reported entry, in its own transaction with the area row and
    /// the player's row locked:
    /// <list type="number">
    ///   <item>settles the structure first (the time before counts at the old pool factor);</item>
    ///   <item>regenerates the area's pool to now (made on the first harvest) and draws <c>PoolCost × breaks</c>, not below 0;</item>
    ///   <item>splits the yield at once: the worker's share (plus the player's carried fraction) as whole units
    ///   <c>SYS_PRODUCTION → SYS_WORLD</c>, the rest <c>SYS_PRODUCTION → Output</c> (both <see cref="StockReason.PlayerHarvest"/>);
    ///   what Output cannot take stays in <c>SYS_PRODUCTION</c>, lost to the town, while the player is still paid (F10);</item>
    ///   <item>stores a <see cref="PlayerHarvest"/> row (unique key) with the pool before and after.</item>
    /// </list>
    /// An entry the API cannot book (unknown rule or area, a rule of another kind, a switched-off rule or structure) is
    /// Rejected with a reason; the others are still booked.
    /// </summary>
    public sealed class HarvestService : IHarvestService
    {
        public const int MaxBreaksPerEntry = 10_000;
        public const int MaxKeyLength = PlayerHarvest.KeyMaxLength;
        public const string Component = "player-harvest";
        public static readonly TimeSpan MaxFutureSkew = TimeSpan.FromMinutes(10);
        private static readonly Regex KeyPattern = new("^[A-Za-z0-9:_.\\-]{1," + MaxKeyLength + "}$", RegexOptions.Compiled);

        private readonly KnKDbContext _db;
        private readonly IStockService _stock;
        private readonly IProductionSettingsReader _settings;
        private readonly IProductionEngine _engine;
        private readonly TimeProvider _clock;

        public HarvestService(KnKDbContext db, IStockService stock, IProductionSettingsReader settings, IProductionEngine engine,
            TimeProvider? clock = null)
        {
            _db = db;
            _stock = stock;
            _settings = settings;
            _engine = engine;
            _clock = clock ?? TimeProvider.System;
        }

        private DateTime Now => _clock.GetUtcNow().UtcDateTime;

        public async Task<HarvestReportResultDto> ReportAsync(HarvestReportDto report, CancellationToken ct = default)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            var all = await _settings.GetAsync();
            if (!all.Enabled)
                throw new ProductionOffException("Production is switched off (Production:Enabled): harvests are not booked and players may not harvest.");
            var settings = all.Harvest ?? new ProductionHarvestSettingsDto();
            var entries = report.Entries ?? new List<HarvestReportEntryDto>();
            if (entries.Count == 0) throw new ArgumentException("entries is required: at least one harvest.");
            if (entries.Count > settings.MaxEntriesPerReport)
                throw new ArgumentException($"A report has at most {settings.MaxEntriesPerReport} entries; send the rest in another report.");
            var keys = entries.Select(e => Key(e.IdempotencyKey)).ToList();
            if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Count)
                throw new ArgumentException("Each entry needs its own idempotencyKey.");

            var result = new HarvestReportResultDto { FeatureEnabled = true };
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                var entryResult = new HarvestEntryResultDto
                {
                    IdempotencyKey = keys[i], UserId = e.UserId, HarvestRuleId = e.HarvestRuleId, ProductionAreaId = e.ProductionAreaId,
                };
                try
                {
                    await BookAsync(e, keys[i], settings, entryResult, ct);
                }
                catch (HarvestRejectedException ex)
                {
                    _db.ChangeTracker.Clear();
                    entryResult.Status = nameof(PlayerHarvestStatus.Rejected);
                    entryResult.Error = ex.Message;
                }
                result.Entries.Add(entryResult);
            }
            return result;
        }

        private sealed class HarvestRejectedException : Exception
        {
            public HarvestRejectedException(string message) : base(message) { }
        }

        private static string Key(string? raw)
        {
            var key = raw?.Trim();
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Each entry needs an idempotencyKey (so a retried report books it once).");
            if (!KeyPattern.IsMatch(key))
                throw new ArgumentException($"idempotencyKey is 1 to {MaxKeyLength} characters of letters, digits and : _ . -");
            return key;
        }

        private static string Hash(HarvestReportEntryDto e, int breaks, int fortune) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                e.UserId, e.HarvestRuleId, e.ProductionAreaId, Breaks = breaks, Fortune = fortune,
            })))).ToLowerInvariant();

        private async Task<bool> ReplayAsync(string key, string hash, HarvestEntryResultDto into, CancellationToken ct)
        {
            var h = await _db.Set<PlayerHarvest>().AsNoTracking().FirstOrDefaultAsync(x => x.IdempotencyKey == key, ct);
            if (h == null) return false;
            if (h.RequestHash != hash) throw new HarvestRejectedException($"Idempotency key '{key}' was already used for a different harvest.");
            Fill(into, h);
            into.Status = nameof(PlayerHarvestStatus.Replayed);
            return true;
        }

        private static void Fill(HarvestEntryResultDto into, PlayerHarvest h)
        {
            into.PlayerHarvestId = h.Id;
            into.ProductionStructureId = h.ProductionStructureId;
            into.ItemBlueprintId = h.ItemBlueprintId;
            into.YieldUnits = h.YieldUnits;
            into.WorkerUnits = h.WorkerUnits;
            into.TownUnits = h.TownUnits;
            into.TownAccepted = h.TownAccepted;
            into.TownLost = h.TownUnits - h.TownAccepted;
            into.PoolBefore = h.PoolBefore;
            into.PoolAfter = h.PoolAfter;
            into.PoolCapacity = h.PoolCapacity;
            into.RegenSeconds = h.RegenSeconds;
            into.ShortfallReason = h.ShortfallReason;
        }

        private async Task BookAsync(HarvestReportEntryDto e, string key, ProductionHarvestSettingsDto settings, HarvestEntryResultDto into,
            CancellationToken ct)
        {
            int breaks = e.Breaks ?? 1;
            int fortune = e.FortuneLevel ?? 0;
            if (breaks is < 1 or > MaxBreaksPerEntry) throw new HarvestRejectedException($"breaks must be 1 to {MaxBreaksPerEntry}.");
            if (fortune is < 0 or > 255) throw new HarvestRejectedException("fortuneLevel must be 0 to 255.");
            var hash = Hash(e, breaks, fortune);
            if (await ReplayAsync(key, hash, into, ct)) return;

            var now = Now;
            var harvestedAt = e.HarvestedAt?.ToUniversalTime() ?? now;
            if (harvestedAt > now + MaxFutureSkew) throw new HarvestRejectedException("harvestedAt is in the future.");

            var rule = await _db.Set<HarvestRule>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == e.HarvestRuleId, ct)
                ?? throw new HarvestRejectedException($"HarvestRule {e.HarvestRuleId} not found.");
            if (!rule.Enabled) throw new HarvestRejectedException($"Harvest rule \"{rule.Name}\" is switched off.");
            var area = await _db.ProductionAreas.AsNoTracking().Include(a => a.ProductionStructure)
                .FirstOrDefaultAsync(a => a.Id == e.ProductionAreaId, ct)
                ?? throw new HarvestRejectedException($"ProductionArea {e.ProductionAreaId} not found.");
            var structure = area.ProductionStructure;
            if (structure.ProductionKindId != rule.ProductionKindId)
                throw new HarvestRejectedException($"Harvest rule \"{rule.Name}\" belongs to another production kind than {structure.Name}.");
            if (!structure.ProductionEnabled) throw new HarvestRejectedException($"{structure.Name}'s production is switched off.");
            if (!await _db.Users.AnyAsync(u => u.Id == e.UserId, ct)) throw new HarvestRejectedException($"User {e.UserId} not found.");
            into.ProductionStructureId = structure.Id;
            into.ItemBlueprintId = rule.YieldItemBlueprintId;

            // The time before this harvest counts at the old pool factor.
            await _engine.SettleStructureAsync(structure.Id, ct);
            _db.ChangeTracker.Clear();

            int sysProduction = await _stock.GetSystemStorageIdAsync(Storage.SysProduction, ct);
            int sysWorld = await _stock.GetSystemStorageIdAsync(Storage.SysWorld, ct);
            var output = await _db.Storages.AsNoTracking()
                .Where(s => s.Kind == StorageKind.Domain && s.DomainId == structure.Id && s.Purpose == StoragePurpose.Output)
                .OrderBy(s => s.Id).FirstOrDefaultAsync(ct);

            await using var tx = await BeginAsync(ct);
            await LockAsync("production_areas", area.Id, ct);
            await LockAsync("users", e.UserId, ct);
            if (await ReplayAsync(key, hash, into, ct))
            {
                if (tx != null) await tx.CommitAsync(ct);
                return;
            }

            var pool = await _db.Set<ResourcePool>().FirstOrDefaultAsync(p => p.ProductionAreaId == area.Id, ct);
            if (pool == null)
            {
                pool = await ResourcePoolService.NewPoolAsync(_db, area, settings, now, ct);
                _db.Set<ResourcePool>().Add(pool);
            }
            decimal before = ResourcePoolMath.CurrentAt(pool, now);
            decimal draw = Math.Round(rule.PoolCost * breaks, ResourcePoolMath.Decimals);
            decimal after = Math.Max(0m, Math.Round(before - draw, ResourcePoolMath.Decimals));
            pool.Current = after;
            pool.LastRegenAt = now;
            pool.UpdatedAt = now;

            var accrual = await _db.Set<HarvestAccrual>().FirstOrDefaultAsync(a => a.UserId == e.UserId && a.HarvestRuleId == rule.Id, ct);
            if (accrual == null)
            {
                accrual = new HarvestAccrual { UserId = e.UserId, HarvestRuleId = rule.Id };
                _db.Set<HarvestAccrual>().Add(accrual);
            }
            decimal yield = ResourcePoolMath.Yield(rule, breaks, fortune);
            decimal worker = Math.Round(yield * rule.WorkerShare + accrual.WorkerAccrued, ResourcePoolMath.Decimals);
            decimal town = Math.Round(yield * (1m - rule.WorkerShare) + accrual.TownAccrued, ResourcePoolMath.Decimals);
            long workerUnits = (long)Math.Floor(worker);
            long townUnits = (long)Math.Floor(town);
            accrual.WorkerAccrued = worker - workerUnits;
            accrual.TownAccrued = town - townUnits;
            accrual.UpdatedAt = now;

            var ctxBase = new { Source = nameof(PlayerHarvest), Ref = key };
            StockContext Context(string suffix, string note) => new()
            {
                Reason = StockReason.PlayerHarvest,
                ActorKind = StockActorKind.PluginServer,
                ActorUserId = e.UserId,
                ActorComponent = Component,
                SourceType = ctxBase.Source,
                SourceRef = ctxBase.Ref,
                Note = note,
                IdempotencyKey = "harvest:" + key + suffix,
            };

            long? workerTx = null, townTx = null;
            if (workerUnits > 0)
            {
                var paid = await _stock.MoveAsync(StockLineRequest.Fungible(sysProduction, sysWorld, rule.YieldItemBlueprintId, workerUnits),
                    Context(":worker", $"Worker's share of {rule.Name}"), StockMoveMode.AllOrNothing, ct);
                if (paid.Status != StockResultStatus.Applied)
                    throw new InvalidOperationException("The worker's share could not be booked; try again with the same key.");
                workerTx = paid.TransactionId;
            }
            long accepted = 0;
            string? shortfall = null;
            if (townUnits > 0)
            {
                if (output == null)
                {
                    shortfall = $"{structure.Name} has no Output storage: the town's share is lost.";
                }
                else
                {
                    var moved = await _stock.MoveAsync(StockLineRequest.Fungible(sysProduction, output.Id, rule.YieldItemBlueprintId, townUnits),
                        Context(":town", $"Town's share of {rule.Name}"), StockMoveMode.Partial, ct);
                    accepted = moved.Accepted;
                    townTx = moved.TransactionId;
                    if (accepted < townUnits)
                        shortfall = $"The Output storage took {accepted} of {townUnits}: the rest is lost to the town (F10).";
                }
            }

            var row = new PlayerHarvest
            {
                IdempotencyKey = key, RequestHash = hash, UserId = e.UserId, HarvestRuleId = rule.Id, ProductionAreaId = area.Id,
                ProductionStructureId = structure.Id, ItemBlueprintId = rule.YieldItemBlueprintId, Breaks = breaks, FortuneLevel = fortune,
                HarvestedAt = harvestedAt, ReceivedAt = now, YieldUnits = yield, WorkerShare = rule.WorkerShare, WorkerUnits = workerUnits,
                TownUnits = townUnits, TownAccepted = accepted, PoolBefore = before, PoolAfter = after, PoolCapacity = pool.Capacity,
                RegenSeconds = ResourcePoolMath.RegenSeconds(rule, ResourcePoolMath.Fill(after, pool.Capacity)),
                WorkerTransactionId = workerTx, TownTransactionId = townTx, ShortfallReason = shortfall,
            };
            _db.Set<PlayerHarvest>().Add(row);
            await _db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            _db.ChangeTracker.Clear();
            Fill(into, row);
            into.Status = nameof(PlayerHarvestStatus.Applied);
        }

        private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginAsync(CancellationToken ct) =>
            _db.Database.IsRelational() && _db.Database.CurrentTransaction == null
                ? await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
                : null;

        private async Task LockAsync(string table, int id, CancellationToken ct)
        {
            if (!_db.Database.IsRelational()) return;
#pragma warning disable EF1002 // a constant table name and an int only
            await _db.Database.ExecuteSqlRawAsync($"SELECT Id FROM {table} WHERE Id = {id} FOR UPDATE", ct);
#pragma warning restore EF1002
        }

        public async Task<HarvestCatalogDto> CatalogAsync(CancellationToken ct = default)
        {
            var now = Now;
            var dto = new HarvestCatalogDto { FeatureEnabled = (await _settings.GetAsync()).Enabled, GeneratedAt = now };
            if (!dto.FeatureEnabled) return dto;
            var rules = await _db.Set<HarvestRule>().AsNoTracking().Include(r => r.ProductionKind).Include(r => r.YieldItemBlueprint)
                .Include(r => r.MinTitleBracket).Where(r => r.Enabled).OrderBy(r => r.ProductionKindId).ThenBy(r => r.Id).ToListAsync(ct);
            dto.Rules = rules.Select(HarvestRuleService.ToDto).ToList();
            var kinds = rules.Select(r => r.ProductionKindId).Distinct().ToList();
            if (kinds.Count == 0) return dto;
            var areas = await _db.ProductionAreas.AsNoTracking().Include(a => a.ProductionStructure)
                .Where(a => a.ProductionStructure.ProductionKindId != null && kinds.Contains(a.ProductionStructure.ProductionKindId.Value))
                .OrderBy(a => a.Id).ToListAsync(ct);
            var ids = areas.Select(a => a.Id).ToList();
            var pools = await _db.Set<ResourcePool>().AsNoTracking().Where(p => ids.Contains(p.ProductionAreaId))
                .ToDictionaryAsync(p => p.ProductionAreaId, ct);
            foreach (var a in areas)
            {
                decimal fill = pools.TryGetValue(a.Id, out var p) ? ResourcePoolMath.Fill(ResourcePoolMath.CurrentAt(p, now), p.Capacity) : 1m;
                dto.Areas.Add(new HarvestCatalogAreaDto
                {
                    ProductionAreaId = a.Id, Name = a.Name, ProductionStructureId = a.ProductionStructureId,
                    ProductionStructureName = a.ProductionStructure.Name, ProductionKindId = a.ProductionStructure.ProductionKindId!.Value,
                    ProductionEnabled = a.ProductionStructure.ProductionEnabled, WorldName = a.WorldName, Shape = a.Shape.ToString(),
                    WgRegionId = a.WgRegionId, Points = AreaGeometry.Points(a.PointsJson), CenterX = a.CenterX, CenterY = a.CenterY,
                    CenterZ = a.CenterZ, Radius = a.Radius, MinX = a.MinX, MinY = a.MinY, MinZ = a.MinZ, MaxX = a.MaxX, MaxY = a.MaxY,
                    MaxZ = a.MaxZ, PoolFill = fill,
                });
            }
            return dto;
        }

        public async Task<PlayerHarvestDto?> GetAsync(long id, CancellationToken ct = default)
        {
            var h = await _db.Set<PlayerHarvest>().AsNoTracking().Include(x => x.User).Include(x => x.HarvestRule)
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            if (h == null) return null;
            var area = await _db.ProductionAreas.AsNoTracking().Where(a => a.Id == h.ProductionAreaId).Select(a => a.Name).FirstOrDefaultAsync(ct);
            var structure = await _db.ProductionStructures.AsNoTracking().Where(s => s.Id == h.ProductionStructureId).Select(s => s.Name)
                .FirstOrDefaultAsync(ct);
            var blueprint = await _db.ItemBlueprints.AsNoTracking().Where(b => b.Id == h.ItemBlueprintId).Select(b => b.Name).FirstOrDefaultAsync(ct);
            return new PlayerHarvestDto
            {
                Id = h.Id, IdempotencyKey = h.IdempotencyKey, UserId = h.UserId,
                User = h.User == null ? null : new NamedRefDto { Id = h.UserId, Name = h.User.Username ?? string.Empty },
                HarvestRuleId = h.HarvestRuleId,
                HarvestRule = h.HarvestRule == null ? null : new NamedRefDto { Id = h.HarvestRuleId, Name = h.HarvestRule.Name },
                ProductionAreaId = h.ProductionAreaId, ProductionAreaName = area, ProductionStructureId = h.ProductionStructureId,
                ProductionStructureName = structure, ItemBlueprintId = h.ItemBlueprintId,
                ItemBlueprint = blueprint == null ? null : new NamedRefDto { Id = h.ItemBlueprintId, Name = blueprint },
                Breaks = h.Breaks, FortuneLevel = h.FortuneLevel, HarvestedAt = h.HarvestedAt, ReceivedAt = h.ReceivedAt,
                YieldUnits = h.YieldUnits, WorkerShare = h.WorkerShare, WorkerUnits = h.WorkerUnits, TownUnits = h.TownUnits,
                TownAccepted = h.TownAccepted, PoolBefore = h.PoolBefore, PoolAfter = h.PoolAfter, PoolCapacity = h.PoolCapacity,
                RegenSeconds = h.RegenSeconds, WorkerTransactionId = h.WorkerTransactionId, TownTransactionId = h.TownTransactionId,
                ShortfallReason = h.ShortfallReason,
            };
        }

        public async Task<PagedResultDto<PlayerHarvestListDto>> SearchAsync(PagedQueryDto query, CancellationToken ct = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            IQueryable<PlayerHarvest> q = _db.Set<PlayerHarvest>().AsNoTracking();
            if (TerritoryPaging.Term(query) is { } term)
                q = q.Where(h => h.User.Username!.ToLower().Contains(term) || h.HarvestRule.Name.ToLower().Contains(term)
                    || h.IdempotencyKey.ToLower().Contains(term));
            if (TerritoryPaging.IntFilter(query, "userId") is { } uid) q = q.Where(h => h.UserId == uid);
            if (TerritoryPaging.IntFilter(query, "harvestRuleId") is { } rid) q = q.Where(h => h.HarvestRuleId == rid);
            if (TerritoryPaging.IntFilter(query, "productionStructureId") is { } sid) q = q.Where(h => h.ProductionStructureId == sid);
            if (TerritoryPaging.IntFilter(query, "productionAreaId") is { } aid) q = q.Where(h => h.ProductionAreaId == aid);
            q = (query.SortBy ?? "receivedat").ToLowerInvariant() switch
            {
                "id" => ProductionInput.Sort(q, query.SortDescending, h => h.Id),
                "name" => ProductionInput.Sort(q, query.SortDescending, h => h.HarvestRule.Name),
                _ => query.SortBy == null ? q.OrderByDescending(h => h.ReceivedAt).ThenByDescending(h => h.Id)
                    : ProductionInput.Sort(q, query.SortDescending, h => h.ReceivedAt),
            };
            return await TerritoryPaging.PageAsync(q.Select(h => new PlayerHarvestListDto
            {
                Id = h.Id, Name = h.User.Username + ": " + h.HarvestRule.Name, UserId = h.UserId, Username = h.User.Username ?? string.Empty,
                HarvestRuleId = h.HarvestRuleId, HarvestRuleName = h.HarvestRule.Name, ProductionStructureId = h.ProductionStructureId,
                ProductionAreaId = h.ProductionAreaId, Breaks = h.Breaks, WorkerUnits = h.WorkerUnits, TownUnits = h.TownUnits,
                TownAccepted = h.TownAccepted, ReceivedAt = h.ReceivedAt,
            }), query);
        }
    }
}
