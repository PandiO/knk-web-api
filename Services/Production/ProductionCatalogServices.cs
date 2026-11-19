using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Production
{
    // Admin CRUD of the production engine (P3, PandiO/knk-workspace#86). Works while Production:Enabled is off (DESIGN
    // § 18.2): it starts no background work. Validation errors are ArgumentException (400), missing rows
    // KeyNotFoundException (404), rows in use InvalidOperationException (409).

    internal static class ProductionInput
    {
        public static TEnum Parse<TEnum>(string? value, string field) where TEnum : struct, Enum
        {
            if (string.IsNullOrWhiteSpace(value) || !Enum.TryParse<TEnum>(value.Trim(), true, out var parsed) || !Enum.IsDefined(parsed)
                || int.TryParse(value.Trim(), out _))
                throw new ArgumentException($"{field} must be one of {string.Join(", ", Enum.GetNames<TEnum>())}.");
            return parsed;
        }

        public static string Name(string? value, int max, string field = "name")
        {
            var name = value?.Trim();
            if (string.IsNullOrEmpty(name)) throw new ArgumentException($"{field} is required.");
            if (name.Length > max) throw new ArgumentException($"{field} must be at most {max} characters.");
            return name;
        }

        public static string? Text(string? value, int max, string field)
        {
            var text = value?.Trim();
            if (string.IsNullOrEmpty(text)) return null;
            if (text.Length > max) throw new ArgumentException($"{field} must be at most {max} characters.");
            return text;
        }

        public static int? OptionalPositive(int value, string field)
        {
            if (value < 0) throw new ArgumentException($"{field} can't be negative.");
            return value > 0 ? value : null;
        }

        public static IQueryable<T> Sort<T, TKey>(IQueryable<T> q, bool descending, System.Linq.Expressions.Expression<Func<T, TKey>> key) =>
            descending ? q.OrderByDescending(key) : q.OrderBy(key);

        public static bool? Flag(PagedQueryDto q, string name) =>
            q.Filters != null && q.Filters.TryGetValue(name, out var v) && bool.TryParse(v, out var b) ? b : null;

        public static string? Filter(PagedQueryDto q, string name) =>
            q.Filters != null && q.Filters.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
    }

    // ===================================================================== Kinds

    public interface IProductionKindService
    {
        Task<IEnumerable<ProductionKindDto>> GetAllAsync();
        Task<ProductionKindDto?> GetByIdAsync(int id);
        Task<ProductionKindDto> CreateAsync(ProductionKindDto dto);
        Task UpdateAsync(int id, ProductionKindDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<ProductionKindListDto>> SearchAsync(PagedQueryDto query);
    }

    /// <summary>One default storage of a kind (PRODUCTION_KINDS § 2 <c>DefaultStorages</c>).</summary>
    public sealed class ProductionKindStorageSpec
    {
        public string? Purpose { get; set; }
        public string? Name { get; set; }
        public int? MaxUnits { get; set; }
    }

    public sealed class ProductionKindService : IProductionKindService
    {
        private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
        public const int MaxYieldRows = 500;
        public const int MaxPrimaryOutputs = 50;
        private readonly KnKDbContext _db;
        private readonly IProductionEngine? _engine;
        private readonly IProductionYieldService? _yield;

        public ProductionKindService(KnKDbContext db, IProductionEngine? engine = null, IProductionYieldService? yield = null)
        {
            _db = db;
            _engine = engine;
            _yield = yield;
        }

        /// <summary>Parses and checks a kind's default storages; Output is always created and may not be listed.</summary>
        public static List<ProductionKindStorageSpec> ParseStorages(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new();
            List<ProductionKindStorageSpec>? specs;
            try { specs = JsonSerializer.Deserialize<List<ProductionKindStorageSpec>>(json, Json); }
            catch (JsonException) { throw new ArgumentException("defaultStoragesJson must be a JSON list of {purpose, name, maxUnits}."); }
            specs ??= new();
            foreach (var s in specs)
            {
                var purpose = ProductionInput.Parse<StoragePurpose>(s.Purpose, "defaultStoragesJson purpose");
                if (purpose == StoragePurpose.Output)
                    throw new ArgumentException("defaultStoragesJson: the Output storage is always created; don't list it.");
                s.Purpose = purpose.ToString();
                s.Name = ProductionInput.Name(s.Name, 40, "defaultStoragesJson name");
                if (s.MaxUnits is < 0) throw new ArgumentException("defaultStoragesJson maxUnits can't be negative.");
            }
            if (specs.Count > 10) throw new ArgumentException("defaultStoragesJson: at most 10 storages.");
            return specs;
        }

        private IQueryable<ProductionKind> Query() =>
            _db.ProductionKinds.AsNoTracking().Include(k => k.Levels).ThenInclude(l => l.Items).ThenInclude(i => i.ItemBlueprint)
                .Include(k => k.YieldTable).ThenInclude(r => r.ItemBlueprint).Include(k => k.PrimaryOutputs).ThenInclude(p => p.ItemBlueprint);

        public async Task<IEnumerable<ProductionKindDto>> GetAllAsync() =>
            (await Query().OrderBy(k => k.Name).ToListAsync()).Select(ToDto).ToList();

        public async Task<ProductionKindDto?> GetByIdAsync(int id)
        {
            var kind = await Query().FirstOrDefaultAsync(k => k.Id == id);
            return kind == null ? null : ToDto(kind);
        }

        public async Task<ProductionKindDto> CreateAsync(ProductionKindDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var kind = new ProductionKind { CreatedAt = DateTime.UtcNow };
            if (dto.AreaModel == null) throw new ArgumentException("areaModel is required.");
            if (dto.ResourceCategory == null) throw new ArgumentException("resourceCategory is required.");
            await ApplyAsync(kind, dto, isCreate: true);
            for (int level = 1; level <= ProductionKind.LevelCount; level++)
                kind.Levels.Add(ProductionCatalogSeed.DefaultLevel(0, level));
            _db.ProductionKinds.Add(kind);
            await _db.SaveChangesAsync();
            return (await GetByIdAsync(kind.Id))!;
        }

        public async Task UpdateAsync(int id, ProductionKindDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (!await _db.ProductionKinds.AnyAsync(k => k.Id == id)) throw new KeyNotFoundException($"ProductionKind with id {id} not found.");
            // A yield table, primary or level change applies from now (P4): settle the kind's survey-driven structures first.
            bool yieldChange = dto.YieldTable != null || dto.PrimaryOutputs != null || dto.PrimaryMultiplier.HasValue;
            var surveyed = !yieldChange || _engine == null ? new List<int>()
                : await _db.ProductionStructures.AsNoTracking()
                    .Where(p => p.ProductionKindId == id && p.YieldSource != ProductionRateSource.Configured).Select(p => p.Id).ToListAsync();
            foreach (var sid in surveyed) await _engine!.SettleStructureAsync(sid);
            _db.ChangeTracker.Clear();
            var kind = await _db.ProductionKinds.Include(k => k.YieldTable).Include(k => k.PrimaryOutputs).FirstAsync(k => k.Id == id);
            await ApplyAsync(kind, dto, isCreate: false);
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
            if (dto.YieldTable != null && _yield != null)
                foreach (var sid in surveyed) await _yield.MaterializeAsync(sid);
        }

        private async Task ApplyAsync(ProductionKind kind, ProductionKindDto dto, bool isCreate)
        {
            if (isCreate || dto.Name != null)
            {
                var name = ProductionInput.Name(dto.Name, ProductionKind.NameMaxLength);
                if (await _db.ProductionKinds.AnyAsync(k => k.Id != kind.Id && k.Name == name))
                    throw new ArgumentException($"A production kind named '{name}' already exists.");
                kind.Name = name;
            }
            if (dto.Description != null) kind.Description = ProductionInput.Text(dto.Description, ProductionKind.DescriptionMaxLength, "description");
            if (dto.Icon != null) kind.Icon = ProductionInput.Text(dto.Icon, ProductionKind.IconMaxLength, "icon");
            if (dto.AreaModel != null)
            {
                var model = ProductionInput.Parse<ProductionAreaModel>(dto.AreaModel, "areaModel");
                if (isCreate && dto.ExclusiveAreas == null)
                    kind.ExclusiveAreas = model is ProductionAreaModel.BorderedField or ProductionAreaModel.ExclusiveVolume or ProductionAreaModel.Pasture;
                kind.AreaModel = model;
            }
            if (dto.ExclusiveAreas.HasValue) kind.ExclusiveAreas = dto.ExclusiveAreas.Value;
            if (dto.AllowsDerived.HasValue) kind.AllowsDerived = dto.AllowsDerived.Value;
            if (dto.AllowsConfigured.HasValue) kind.AllowsConfigured = dto.AllowsConfigured.Value;
            if (dto.AllowsHybrid.HasValue) kind.AllowsHybrid = dto.AllowsHybrid.Value;
            if (!kind.AllowsDerived && !kind.AllowsConfigured && !kind.AllowsHybrid)
                throw new ArgumentException("A kind must allow at least one yield source.");
            if (dto.ResourceCategory != null) kind.ResourceCategory = ProductionInput.Parse<ResourceCategory>(dto.ResourceCategory, "resourceCategory");
            if (dto.DefaultWorkers.HasValue)
            {
                if (dto.DefaultWorkers.Value is < 0 or > ProductionKind.MaxDefaultWorkers)
                    throw new ArgumentException($"defaultWorkers must be 0 to {ProductionKind.MaxDefaultWorkers}.");
                kind.DefaultWorkers = dto.DefaultWorkers.Value;
            }
            if (dto.CycleVariation.HasValue) kind.CycleVariation = dto.CycleVariation.Value;
            if (dto.DefaultStoragesJson != null)
            {
                var specs = ParseStorages(dto.DefaultStoragesJson);
                kind.DefaultStoragesJson = specs.Count == 0 ? null : JsonSerializer.Serialize(specs, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                });
                if (kind.DefaultStoragesJson?.Length > ProductionKind.DefaultStoragesMaxLength)
                    throw new ArgumentException("defaultStoragesJson is too long.");
            }
            if (dto.IsActive.HasValue) kind.IsActive = dto.IsActive.Value;
            await ApplyYieldAsync(kind, dto);
        }

        /// <summary>P4 (#138): survey profile, primary multiplier, yield table and primary outputs.</summary>
        private async Task ApplyYieldAsync(ProductionKind kind, ProductionKindDto dto)
        {
            if (dto.SurveyProfileJson != null)
            {
                string? profile = null;
                if (!string.IsNullOrWhiteSpace(dto.SurveyProfileJson))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(dto.SurveyProfileJson);
                        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
                        profile = JsonSerializer.Serialize(doc.RootElement);
                    }
                    catch (JsonException) { throw new ArgumentException("surveyProfileJson must be a JSON object."); }
                    if (profile.Length > ProductionKind.SurveyProfileMaxLength) throw new ArgumentException("surveyProfileJson is too long.");
                }
                if (profile != kind.SurveyProfileJson)
                {
                    kind.SurveyProfileJson = profile;
                    if (kind.Id != 0) kind.SurveyProfileVersion++;
                }
            }
            if (dto.PrimaryMultiplier.HasValue)
            {
                if (dto.PrimaryMultiplier.Value is < 0 or > ProductionKind.MaxPrimaryMultiplier)
                    throw new ArgumentException($"primaryMultiplier must be 0 to {ProductionKind.MaxPrimaryMultiplier}.");
                kind.PrimaryMultiplier = Math.Round(dto.PrimaryMultiplier.Value, 4);
            }
            if (dto.YieldTable != null)
            {
                if (dto.YieldTable.Count > MaxYieldRows) throw new ArgumentException($"A yield table has at most {MaxYieldRows} rows.");
                var keep = new List<ProductionYieldRow>();
                foreach (var r in dto.YieldTable)
                {
                    var key = SurveyUnitKeys.NormalizePattern(r.UnitKey, "yieldTable unitKey");
                    if (!await _db.ItemBlueprints.AnyAsync(b => b.Id == r.ItemBlueprintId))
                        throw new ArgumentException($"yieldTable: ItemBlueprint with id {r.ItemBlueprintId} not found.");
                    if (r.RatePerUnitPerHour < 0 || r.RatePerUnitPerHour > ProductionLine.MaxRatePerHour)
                        throw new ArgumentException($"yieldTable {key}: ratePerUnitPerHour must be 0 to {ProductionLine.MaxRatePerHour}.");
                    decimal chance = r.ByproductChance ?? 1m;
                    if (chance is < 0 or > 1) throw new ArgumentException($"yieldTable {key}: byproductChance must be 0 to 1.");
                    int minLevel = r.MinLevel ?? 1;
                    if (minLevel is < ProductionStructure.MinLevel or > ProductionStructure.MaxLevel)
                        throw new ArgumentException($"yieldTable {key}: minLevel must be {ProductionStructure.MinLevel} to {ProductionStructure.MaxLevel}.");
                    if (keep.Any(k => k.UnitKey == key && k.ItemBlueprintId == r.ItemBlueprintId))
                        throw new ArgumentException($"yieldTable: {key} → blueprint {r.ItemBlueprintId} is listed twice.");
                    var row = kind.YieldTable.FirstOrDefault(x => x.UnitKey == key && x.ItemBlueprintId == r.ItemBlueprintId)
                        ?? new ProductionYieldRow { UnitKey = key, ItemBlueprintId = r.ItemBlueprintId };
                    row.RatePerUnitPerHour = Math.Round(r.RatePerUnitPerHour, ProductionRate.Decimals);
                    row.ByproductChance = Math.Round(chance, 6);
                    row.MinLevel = minLevel;
                    keep.Add(row);
                }
                foreach (var gone in kind.YieldTable.Where(x => !keep.Contains(x)).ToList()) _db.ProductionYieldRows.Remove(gone);
                kind.YieldTable = keep;
            }
            if (dto.PrimaryOutputs != null)
            {
                if (dto.PrimaryOutputs.Count > MaxPrimaryOutputs) throw new ArgumentException($"A kind has at most {MaxPrimaryOutputs} primary outputs.");
                var keep = new List<ProductionKindPrimaryOutput>();
                foreach (var p in dto.PrimaryOutputs)
                {
                    if (!await _db.ItemBlueprints.AnyAsync(b => b.Id == p.ItemBlueprintId))
                        throw new ArgumentException($"primaryOutputs: ItemBlueprint with id {p.ItemBlueprintId} not found.");
                    if (keep.Any(k => k.ItemBlueprintId == p.ItemBlueprintId))
                        throw new ArgumentException($"primaryOutputs: blueprint {p.ItemBlueprintId} is listed twice.");
                    keep.Add(kind.PrimaryOutputs.FirstOrDefault(x => x.ItemBlueprintId == p.ItemBlueprintId)
                        ?? new ProductionKindPrimaryOutput { ItemBlueprintId = p.ItemBlueprintId });
                }
                foreach (var gone in kind.PrimaryOutputs.Where(x => !keep.Contains(x)).ToList()) _db.ProductionKindPrimaryOutputs.Remove(gone);
                kind.PrimaryOutputs = keep;
            }
        }

        public async Task DeleteAsync(int id)
        {
            // Levels and their items go with the kind (Cascade); loaded so the delete is the same without a database cascade.
            var kind = await _db.ProductionKinds.Include(k => k.Levels).ThenInclude(l => l.Items).Include(k => k.YieldTable)
                .Include(k => k.PrimaryOutputs).FirstOrDefaultAsync(k => k.Id == id)
                ?? throw new KeyNotFoundException($"ProductionKind with id {id} not found.");
            int used = await _db.ProductionStructures.CountAsync(p => p.ProductionKindId == id);
            if (used > 0) throw new InvalidOperationException($"{used} production structure(s) use this kind; give them another kind first.");
            if (await _db.ProductionModifiers.AnyAsync(m => m.TargetProductionKindId == id))
                throw new InvalidOperationException("A production modifier targets this kind; change or delete it first.");
            _db.ProductionKinds.Remove(kind);
            await _db.SaveChangesAsync();
        }

        public async Task<PagedResultDto<ProductionKindListDto>> SearchAsync(PagedQueryDto query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            IQueryable<ProductionKind> q = _db.ProductionKinds.AsNoTracking();
            if (TerritoryPaging.Term(query) is { } term) q = q.Where(k => k.Name.ToLower().Contains(term));
            if (ProductionInput.Filter(query, "areaModel") is { } m && Enum.TryParse<ProductionAreaModel>(m, true, out var model))
                q = q.Where(k => k.AreaModel == model);
            if (ProductionInput.Filter(query, "resourceCategory") is { } c && Enum.TryParse<ResourceCategory>(c, true, out var cat))
                q = q.Where(k => k.ResourceCategory == cat);
            if (ProductionInput.Flag(query, "isActive") is { } active) q = q.Where(k => k.IsActive == active);
            q = (query.SortBy ?? "name").ToLowerInvariant() switch
            {
                "id" => ProductionInput.Sort(q, query.SortDescending, k => k.Id),
                _ => ProductionInput.Sort(q, query.SortDescending, k => k.Name),
            };
            var structures = _db.ProductionStructures;
            var page = await TerritoryPaging.PageAsync(q.Select(k => new ProductionKindListDto
            {
                Id = k.Id, Name = k.Name, AreaModel = k.AreaModel.ToString(), ResourceCategory = k.ResourceCategory.ToString(),
                DefaultWorkers = k.DefaultWorkers, IsActive = k.IsActive,
                StructureCount = structures.Count(p => p.ProductionKindId == k.Id),
            }), query);
            return page;
        }

        public static ProductionKindDto ToDto(ProductionKind k) => new()
        {
            Id = k.Id,
            Name = k.Name,
            Description = k.Description,
            Icon = k.Icon,
            AreaModel = k.AreaModel.ToString(),
            ExclusiveAreas = k.ExclusiveAreas,
            AllowsDerived = k.AllowsDerived,
            AllowsConfigured = k.AllowsConfigured,
            AllowsHybrid = k.AllowsHybrid,
            ResourceCategory = k.ResourceCategory.ToString(),
            DefaultWorkers = k.DefaultWorkers,
            CycleVariation = k.CycleVariation,
            DefaultStoragesJson = k.DefaultStoragesJson,
            IsActive = k.IsActive,
            CreatedAt = k.CreatedAt,
            Levels = k.Levels.OrderBy(l => l.Level).Select(l => ProductionKindLevelService.ToDto(l, k)).ToList(),
            SurveyProfileJson = k.SurveyProfileJson,
            SurveyProfileVersion = k.SurveyProfileVersion,
            PrimaryMultiplier = k.PrimaryMultiplier,
            YieldTable = k.YieldTable.OrderBy(r => r.UnitKey).ThenBy(r => r.ItemBlueprintId).Select(r => new ProductionYieldRowDto
            {
                Id = r.Id, UnitKey = r.UnitKey, ItemBlueprintId = r.ItemBlueprintId,
                ItemBlueprint = r.ItemBlueprint == null ? null : new NamedRefDto { Id = r.ItemBlueprintId, Name = r.ItemBlueprint.Name },
                RatePerUnitPerHour = r.RatePerUnitPerHour, ByproductChance = r.ByproductChance, MinLevel = r.MinLevel,
            }).ToList(),
            PrimaryOutputs = k.PrimaryOutputs.OrderBy(p => p.ItemBlueprintId).Select(p => new ProductionKindPrimaryOutputDto
            {
                Id = p.Id, ItemBlueprintId = p.ItemBlueprintId,
                ItemBlueprint = p.ItemBlueprint == null ? null : new NamedRefDto { Id = p.ItemBlueprintId, Name = p.ItemBlueprint.Name },
            }).ToList(),
        };
    }

    // ===================================================================== Levels

    public interface IProductionKindLevelService
    {
        Task<IEnumerable<ProductionKindLevelDto>> GetAllAsync();
        Task<ProductionKindLevelDto?> GetByIdAsync(int id);
        Task UpdateAsync(int id, ProductionKindLevelDto dto);
        Task<PagedResultDto<ProductionKindLevelListDto>> SearchAsync(PagedQueryDto query);
    }

    /// <summary>A kind's ten levels (G8): edited, never created or deleted on their own (they come and go with the kind).</summary>
    public sealed class ProductionKindLevelService : IProductionKindLevelService
    {
        public const decimal MaxMultiplier = 100m;
        private readonly KnKDbContext _db;

        public ProductionKindLevelService(KnKDbContext db) => _db = db;

        private IQueryable<ProductionKindLevel> Query() => _db.ProductionKindLevels.AsNoTracking()
            .Include(l => l.ProductionKind).Include(l => l.Items).ThenInclude(i => i.ItemBlueprint);

        public async Task<IEnumerable<ProductionKindLevelDto>> GetAllAsync() =>
            (await Query().OrderBy(l => l.ProductionKindId).ThenBy(l => l.Level).ToListAsync()).Select(l => ToDto(l, l.ProductionKind)).ToList();

        public async Task<ProductionKindLevelDto?> GetByIdAsync(int id)
        {
            var level = await Query().FirstOrDefaultAsync(l => l.Id == id);
            return level == null ? null : ToDto(level, level.ProductionKind);
        }

        public async Task UpdateAsync(int id, ProductionKindLevelDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var level = await _db.ProductionKindLevels.Include(l => l.Items).FirstOrDefaultAsync(l => l.Id == id)
                ?? throw new KeyNotFoundException($"ProductionKindLevel with id {id} not found.");
            if (dto.YieldMultiplier.HasValue)
            {
                if (dto.YieldMultiplier.Value is < 0 or > MaxMultiplier) throw new ArgumentException($"yieldMultiplier must be 0 to {MaxMultiplier}.");
                level.YieldMultiplier = dto.YieldMultiplier.Value;
            }
            if (dto.CycleSpeedMultiplier.HasValue)
            {
                if (dto.CycleSpeedMultiplier.Value is <= 0 or > MaxMultiplier)
                    throw new ArgumentException($"cycleSpeedMultiplier must be above 0 and at most {MaxMultiplier}.");
                level.CycleSpeedMultiplier = dto.CycleSpeedMultiplier.Value;
            }
            if (dto.MaxAreaSize.HasValue) level.MaxAreaSize = ProductionInput.OptionalPositive(dto.MaxAreaSize.Value, "maxAreaSize");
            if (dto.OutputMaxUnits.HasValue) level.OutputMaxUnits = ProductionInput.OptionalPositive(dto.OutputMaxUnits.Value, "outputMaxUnits");
            if (dto.UnitsPerSlot.HasValue) level.UnitsPerSlot = ProductionInput.OptionalPositive(dto.UnitsPerSlot.Value, "unitsPerSlot");
            if (dto.TransportCapacity.HasValue) level.TransportCapacity = ProductionInput.OptionalPositive(dto.TransportCapacity.Value, "transportCapacity");
            if (dto.UpkeepReduction.HasValue)
            {
                if (dto.UpkeepReduction.Value is < 0 or > 1) throw new ArgumentException("upkeepReduction must be 0 to 1 (it has no effect in V1).");
                level.UpkeepReduction = dto.UpkeepReduction.Value;
            }
            if (dto.UpgradeCoins.HasValue)
            {
                if (dto.UpgradeCoins.Value < 0) throw new ArgumentException("upgradeCoins can't be negative.");
                level.UpgradeCoins = dto.UpgradeCoins.Value;
            }
            if (dto.UpgradeDurationMinutes.HasValue)
                level.UpgradeDurationMinutes = ProductionInput.OptionalPositive(dto.UpgradeDurationMinutes.Value, "upgradeDurationMinutes");
            if (dto.Items != null)
            {
                var items = new List<ProductionKindLevelItem>();
                foreach (var i in dto.Items)
                {
                    var role = ProductionInput.Parse<ProductionKindLevelItemRole>(i.Role, "items role");
                    if (!await _db.ItemBlueprints.AnyAsync(b => b.Id == i.ItemBlueprintId))
                        throw new ArgumentException($"ItemBlueprint with id {i.ItemBlueprintId} not found.");
                    if (role == ProductionKindLevelItemRole.UpgradeCost && i.Amount <= 0)
                        throw new ArgumentException("An upgrade cost needs an amount above 0.");
                    if (items.Any(x => x.Role == role && x.ItemBlueprintId == i.ItemBlueprintId))
                        throw new ArgumentException($"Blueprint {i.ItemBlueprintId} is listed twice as {role}.");
                    items.Add(new ProductionKindLevelItem
                    {
                        Role = role, ItemBlueprintId = i.ItemBlueprintId,
                        Amount = role == ProductionKindLevelItemRole.UpgradeCost ? i.Amount : 0,
                    });
                }
                _db.ProductionKindLevelItems.RemoveRange(level.Items);
                level.Items = items;
            }
            await _db.SaveChangesAsync();
        }

        public async Task<PagedResultDto<ProductionKindLevelListDto>> SearchAsync(PagedQueryDto query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            IQueryable<ProductionKindLevel> q = _db.ProductionKindLevels.AsNoTracking();
            if (TerritoryPaging.Term(query) is { } term) q = q.Where(l => l.ProductionKind.Name.ToLower().Contains(term));
            if (TerritoryPaging.IntFilter(query, "productionKindId") is { } kindId) q = q.Where(l => l.ProductionKindId == kindId);
            q = query.SortDescending
                ? q.OrderByDescending(l => l.ProductionKind.Name).ThenByDescending(l => l.Level)
                : q.OrderBy(l => l.ProductionKind.Name).ThenBy(l => l.Level);
            return await TerritoryPaging.PageAsync(q.Select(l => new ProductionKindLevelListDto
            {
                Id = l.Id, Name = l.ProductionKind.Name + " level " + l.Level, ProductionKindId = l.ProductionKindId,
                ProductionKindName = l.ProductionKind.Name, Level = l.Level, YieldMultiplier = l.YieldMultiplier,
                CycleSpeedMultiplier = l.CycleSpeedMultiplier, UpgradeCoins = l.UpgradeCoins,
            }), query);
        }

        public static ProductionKindLevelDto ToDto(ProductionKindLevel l, ProductionKind? kind) => new()
        {
            Id = l.Id,
            ProductionKindId = l.ProductionKindId,
            ProductionKind = kind == null ? null : new ProductionKindNavDto { Id = kind.Id, Name = kind.Name },
            Level = l.Level,
            YieldMultiplier = l.YieldMultiplier,
            CycleSpeedMultiplier = l.CycleSpeedMultiplier,
            MaxAreaSize = l.MaxAreaSize,
            OutputMaxUnits = l.OutputMaxUnits,
            UnitsPerSlot = l.UnitsPerSlot,
            TransportCapacity = l.TransportCapacity,
            UpkeepReduction = l.UpkeepReduction,
            UpgradeCoins = l.UpgradeCoins,
            UpgradeDurationMinutes = l.UpgradeDurationMinutes,
            Items = l.Items.OrderBy(i => i.Role).ThenBy(i => i.Id).Select(i => new ProductionKindLevelItemDto
            {
                Id = i.Id, Role = i.Role.ToString(), ItemBlueprintId = i.ItemBlueprintId, ItemBlueprintName = i.ItemBlueprint?.Name,
                ItemBlueprint = i.ItemBlueprint == null ? null : new NamedRefDto { Id = i.ItemBlueprint.Id, Name = i.ItemBlueprint.Name },
                Amount = i.Amount,
            }).ToList(),
        };
    }

    // ===================================================================== Lines

    public interface IProductionLineService
    {
        Task<IEnumerable<ProductionLineDto>> GetAllAsync();
        Task<ProductionLineDto?> GetByIdAsync(int id);
        Task<ProductionLineDto> CreateAsync(ProductionLineDto dto);
        Task UpdateAsync(int id, ProductionLineDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<ProductionLineListDto>> SearchAsync(PagedQueryDto query);
    }

    /// <summary>
    /// Production lines (one per output of a structure). A new line starts its cycle grid now. An update settles the
    /// line first while the feature is on, so the time before the change runs at the old rate.
    /// </summary>
    public sealed class ProductionLineService : IProductionLineService
    {
        public const int MaxInputs = 10;
        private readonly KnKDbContext _db;
        private readonly IProductionSettingsReader _settings;
        private readonly IProductionEngine _engine;
        private readonly TimeProvider _clock;

        public ProductionLineService(KnKDbContext db, IProductionSettingsReader settings, IProductionEngine engine, TimeProvider? clock = null)
        {
            _db = db;
            _settings = settings;
            _engine = engine;
            _clock = clock ?? TimeProvider.System;
        }

        private IQueryable<ProductionLine> Query() => _db.ProductionLines.AsNoTracking().Include(l => l.ProductionStructure)
            .Include(l => l.ItemBlueprint).Include(l => l.Inputs).ThenInclude(i => i.ItemBlueprint);

        public async Task<IEnumerable<ProductionLineDto>> GetAllAsync() =>
            (await Query().OrderBy(l => l.Id).ToListAsync()).Select(l => ProductionLineMapping.ToDto(l)).ToList();

        public async Task<ProductionLineDto?> GetByIdAsync(int id)
        {
            var line = await Query().FirstOrDefaultAsync(l => l.Id == id);
            return line == null ? null : ProductionLineMapping.ToDto(line);
        }

        public async Task<ProductionLineDto> CreateAsync(ProductionLineDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (dto.ProductionStructureId is not > 0) throw new ArgumentException("productionStructureId is required.");
            if (dto.ItemBlueprintId is not > 0) throw new ArgumentException("itemBlueprintId is required.");
            var structure = await _db.ProductionStructures.AsNoTracking().FirstOrDefaultAsync(p => p.Id == dto.ProductionStructureId)
                ?? throw new ArgumentException($"ProductionStructure with id {dto.ProductionStructureId} not found.");
            var now = _clock.GetUtcNow().UtcDateTime;
            var settings = (await _settings.GetAsync()).Engine;
            var level = await ProductionLevels.FindAsync(_db, structure.ProductionKindId, structure.Level);
            var line = new ProductionLine
            {
                ProductionStructureId = structure.Id,
                AnchorAt = now,
                LastSettledAt = now,
                NextSettleAt = now + ProductionRate.Cycle(settings, level),
                CreatedAt = now,
            };
            await ApplyAsync(line, dto, isCreate: true);
            _db.ProductionLines.Add(line);
            await _db.SaveChangesAsync();
            return (await GetByIdAsync(line.Id))!;
        }

        public async Task UpdateAsync(int id, ProductionLineDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (!await _db.ProductionLines.AnyAsync(l => l.Id == id)) throw new KeyNotFoundException($"ProductionLine with id {id} not found.");
            await _engine.SettleLineAsync(id);
            _db.ChangeTracker.Clear();
            var line = await _db.ProductionLines.Include(l => l.Inputs).FirstAsync(l => l.Id == id);
            await ApplyAsync(line, dto, isCreate: false);
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
            // A line switched back on resumes from now, not at the next sweep.
            if (dto.Enabled == true) await _engine.SettleLineAsync(id);
        }

        private async Task ApplyAsync(ProductionLine line, ProductionLineDto dto, bool isCreate)
        {
            if (dto.ProductionStructureId.HasValue && !isCreate && dto.ProductionStructureId != line.ProductionStructureId)
                throw new ArgumentException("A line can't move to another structure; delete it and create one there.");
            if (isCreate || (dto.ItemBlueprintId.HasValue && dto.ItemBlueprintId != line.ItemBlueprintId))
            {
                int bp = dto.ItemBlueprintId!.Value;
                if (!await _db.ItemBlueprints.AnyAsync(b => b.Id == bp)) throw new ArgumentException($"ItemBlueprint with id {bp} not found.");
                if (await _db.ProductionLines.AnyAsync(l => l.Id != line.Id && l.ProductionStructureId == line.ProductionStructureId && l.ItemBlueprintId == bp))
                    throw new ArgumentException("The structure already has a line for this blueprint.");
                line.ItemBlueprintId = bp;
            }
            if (dto.RateSource != null)
            {
                var source = ProductionInput.Parse<ProductionRateSource>(dto.RateSource, "rateSource");
                if (source != ProductionRateSource.Configured)
                {
                    // P4 (#138): a survey line follows its structure's yield source (materialization makes them).
                    var yieldSource = await _db.ProductionStructures.AsNoTracking().Where(p => p.Id == line.ProductionStructureId)
                        .Select(p => p.YieldSource).FirstAsync();
                    if (source != yieldSource)
                        throw new ArgumentException($"rateSource: a {source} line needs a {source} structure (the structure is {yieldSource}).");
                }
                line.RateSource = source;
            }
            if (dto.ConfiguredRatePerHour.HasValue)
            {
                if (dto.ConfiguredRatePerHour.Value < 0 || dto.ConfiguredRatePerHour.Value > ProductionLine.MaxRatePerHour)
                    throw new ArgumentException($"configuredRatePerHour must be 0 to {ProductionLine.MaxRatePerHour}.");
                line.ConfiguredRatePerHour = Math.Round(dto.ConfiguredRatePerHour.Value, ProductionRate.Decimals);
            }
            if (dto.ResourceCategory != null)
                line.ResourceCategory = string.IsNullOrWhiteSpace(dto.ResourceCategory) ? null
                    : ProductionInput.Parse<ResourceCategory>(dto.ResourceCategory, "resourceCategory");
            if (dto.Enabled.HasValue) line.Enabled = dto.Enabled.Value;
            if (dto.Inputs != null)
            {
                if (dto.Inputs.Count > MaxInputs) throw new ArgumentException($"A line has at most {MaxInputs} inputs.");
                var keep = new List<ProductionLineInput>();
                foreach (var i in dto.Inputs)
                {
                    if (i.AmountPerHour <= 0 || i.AmountPerHour > ProductionLine.MaxRatePerHour)
                        throw new ArgumentException($"An input's amountPerHour must be above 0 and at most {ProductionLine.MaxRatePerHour}.");
                    if (!await _db.ItemBlueprints.AnyAsync(b => b.Id == i.ItemBlueprintId))
                        throw new ArgumentException($"ItemBlueprint with id {i.ItemBlueprintId} not found.");
                    if (keep.Any(k => k.ItemBlueprintId == i.ItemBlueprintId)) throw new ArgumentException($"Input blueprint {i.ItemBlueprintId} is listed twice.");
                    var existing = line.Inputs.FirstOrDefault(x => x.ItemBlueprintId == i.ItemBlueprintId);
                    if (existing != null)
                    {
                        existing.AmountPerHour = Math.Round(i.AmountPerHour, ProductionRate.Decimals);
                        keep.Add(existing);
                    }
                    else keep.Add(new ProductionLineInput { ItemBlueprintId = i.ItemBlueprintId, AmountPerHour = Math.Round(i.AmountPerHour, ProductionRate.Decimals) });
                }
                foreach (var gone in line.Inputs.Where(x => !keep.Contains(x)).ToList()) _db.ProductionLineInputs.Remove(gone);
                line.Inputs = keep;
            }
        }

        public async Task DeleteAsync(int id)
        {
            var line = await _db.ProductionLines.FirstOrDefaultAsync(l => l.Id == id)
                ?? throw new KeyNotFoundException($"ProductionLine with id {id} not found.");
            if (await _db.ProductionModifiers.AnyAsync(m => m.Scope == ProductionModifierScope.Output && m.ScopeRefId == id))
                throw new InvalidOperationException("A production modifier is scoped to this line; change or delete it first.");
            _db.ProductionLines.Remove(line);
            await _db.SaveChangesAsync();
        }

        public async Task<PagedResultDto<ProductionLineListDto>> SearchAsync(PagedQueryDto query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            IQueryable<ProductionLine> q = _db.ProductionLines.AsNoTracking();
            if (TerritoryPaging.Term(query) is { } term)
                q = q.Where(l => l.ItemBlueprint.Name.ToLower().Contains(term) || l.ProductionStructure.Name.ToLower().Contains(term));
            if (TerritoryPaging.IntFilter(query, "productionStructureId") is { } sid) q = q.Where(l => l.ProductionStructureId == sid);
            if (TerritoryPaging.IntFilter(query, "itemBlueprintId") is { } bid) q = q.Where(l => l.ItemBlueprintId == bid);
            if (ProductionInput.Filter(query, "status") is { } st && Enum.TryParse<ProductionLineStatus>(st, true, out var status))
                q = q.Where(l => l.Status == status);
            q = (query.SortBy ?? "id").ToLowerInvariant() switch
            {
                "nextsettleat" => ProductionInput.Sort(q, query.SortDescending, l => l.NextSettleAt),
                "name" => ProductionInput.Sort(q, query.SortDescending, l => l.ItemBlueprint.Name),
                _ => ProductionInput.Sort(q, query.SortDescending, l => l.Id),
            };
            return await TerritoryPaging.PageAsync(q.Select(l => new ProductionLineListDto
            {
                Id = l.Id, Name = l.ProductionStructure.Name + ": " + l.ItemBlueprint.Name, ProductionStructureId = l.ProductionStructureId,
                ProductionStructureName = l.ProductionStructure.Name, ItemBlueprintId = l.ItemBlueprintId,
                ItemBlueprintName = l.ItemBlueprint.Name, RateSource = l.RateSource.ToString(),
                ConfiguredRatePerHour = l.ConfiguredRatePerHour, Status = l.Status.ToString(), NextSettleAt = l.NextSettleAt,
            }), query);
        }
    }

    // ===================================================================== Modifiers

    public interface IProductionModifierService
    {
        Task<IEnumerable<ProductionModifierDto>> GetAllAsync();
        Task<ProductionModifierDto?> GetByIdAsync(int id);
        Task<ProductionModifierDto> CreateAsync(ProductionModifierDto dto);
        Task UpdateAsync(int id, ProductionModifierDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<ProductionModifierListDto>> SearchAsync(PagedQueryDto query);
    }

    public sealed class ProductionModifierService : IProductionModifierService
    {
        public const decimal MaxValue = 1_000_000m;
        private readonly KnKDbContext _db;

        public ProductionModifierService(KnKDbContext db) => _db = db;

        public async Task<IEnumerable<ProductionModifierDto>> GetAllAsync() =>
            (await _db.ProductionModifiers.AsNoTracking().OrderBy(m => m.Id).ToListAsync()).Select(ToDto).ToList();

        public async Task<ProductionModifierDto?> GetByIdAsync(int id)
        {
            var m = await _db.ProductionModifiers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            return m == null ? null : ToDto(m);
        }

        public async Task<ProductionModifierDto> CreateAsync(ProductionModifierDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (dto.Scope == null) throw new ArgumentException("scope is required.");
            if (dto.Effect == null) throw new ArgumentException("effect is required.");
            if (dto.Value == null) throw new ArgumentException("value is required.");
            var m = new ProductionModifier { CreatedAt = DateTime.UtcNow, Target = ProductionModifierTarget.Any };
            await ApplyAsync(m, dto, isCreate: true);
            _db.ProductionModifiers.Add(m);
            await _db.SaveChangesAsync();
            return ToDto(m);
        }

        public async Task UpdateAsync(int id, ProductionModifierDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var m = await _db.ProductionModifiers.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new KeyNotFoundException($"ProductionModifier with id {id} not found.");
            await ApplyAsync(m, dto, isCreate: false);
            await _db.SaveChangesAsync();
        }

        private async Task ApplyAsync(ProductionModifier m, ProductionModifierDto dto, bool isCreate)
        {
            if (isCreate || dto.Name != null) m.Name = ProductionInput.Name(dto.Name, ProductionModifier.NameMaxLength);
            if (dto.Scope != null) m.Scope = ProductionInput.Parse<ProductionModifierScope>(dto.Scope, "scope");
            if (dto.ScopeRefId.HasValue) m.ScopeRefId = dto.ScopeRefId.Value > 0 ? dto.ScopeRefId.Value : null;
            if (m.Scope is ProductionModifierScope.Global or ProductionModifierScope.MapArea) m.ScopeRefId = null;
            else
            {
                if (m.ScopeRefId == null) throw new ArgumentException($"scopeRefId is required for scope {m.Scope}.");
                int r = m.ScopeRefId.Value;
                bool exists = m.Scope switch
                {
                    ProductionModifierScope.Kingdom => await _db.Kingdoms.AnyAsync(k => k.Id == r),
                    ProductionModifierScope.Town or ProductionModifierScope.Territory => await _db.Towns.AnyAsync(t => t.Id == r),
                    ProductionModifierScope.Structure => await _db.ProductionStructures.AnyAsync(p => p.Id == r),
                    ProductionModifierScope.Output => await _db.ProductionLines.AnyAsync(l => l.Id == r),
                    _ => false,
                };
                if (!exists) throw new ArgumentException($"scopeRefId {r} is not an existing {(m.Scope == ProductionModifierScope.Output ? "production line" : m.Scope.ToString().ToLowerInvariant())}.");
            }

            if (dto.Target != null) m.Target = ProductionInput.Parse<ProductionModifierTarget>(dto.Target, "target");
            if (dto.TargetItemBlueprintId.HasValue) m.TargetItemBlueprintId = dto.TargetItemBlueprintId.Value > 0 ? dto.TargetItemBlueprintId : null;
            if (dto.TargetCategory != null)
                m.TargetCategory = string.IsNullOrWhiteSpace(dto.TargetCategory) ? null : ProductionInput.Parse<ResourceCategory>(dto.TargetCategory, "targetCategory");
            if (dto.TargetProductionKindId.HasValue) m.TargetProductionKindId = dto.TargetProductionKindId.Value > 0 ? dto.TargetProductionKindId : null;
            switch (m.Target)
            {
                case ProductionModifierTarget.Blueprint:
                    if (m.TargetItemBlueprintId == null || !await _db.ItemBlueprints.AnyAsync(b => b.Id == m.TargetItemBlueprintId))
                        throw new ArgumentException("target Blueprint needs an existing targetItemBlueprintId.");
                    m.TargetCategory = null; m.TargetProductionKindId = null;
                    break;
                case ProductionModifierTarget.Category:
                    if (m.TargetCategory == null) throw new ArgumentException("target Category needs a targetCategory.");
                    m.TargetItemBlueprintId = null; m.TargetProductionKindId = null;
                    break;
                case ProductionModifierTarget.Kind:
                    if (m.TargetProductionKindId == null || !await _db.ProductionKinds.AnyAsync(k => k.Id == m.TargetProductionKindId))
                        throw new ArgumentException("target Kind needs an existing targetProductionKindId.");
                    m.TargetItemBlueprintId = null; m.TargetCategory = null;
                    break;
                default:
                    m.TargetItemBlueprintId = null; m.TargetCategory = null; m.TargetProductionKindId = null;
                    break;
            }

            if (dto.Effect != null) m.Effect = ProductionInput.Parse<ProductionModifierEffect>(dto.Effect, "effect");
            if (dto.Value.HasValue) m.Value = dto.Value.Value;
            if (Math.Abs(m.Value) > MaxValue) throw new ArgumentException($"value must be within ±{MaxValue}.");
            if (m.Effect == ProductionModifierEffect.Multiply && m.Value < 0) throw new ArgumentException("A multiplying modifier can't be negative.");
            if (dto.ClearWindow == true) { m.ActiveFrom = null; m.ActiveUntil = null; }
            if (dto.ActiveFrom.HasValue) m.ActiveFrom = DateTime.SpecifyKind(dto.ActiveFrom.Value.ToUniversalTime(), DateTimeKind.Utc);
            if (dto.ActiveUntil.HasValue) m.ActiveUntil = DateTime.SpecifyKind(dto.ActiveUntil.Value.ToUniversalTime(), DateTimeKind.Utc);
            if (m.ActiveFrom.HasValue && m.ActiveUntil.HasValue && m.ActiveUntil <= m.ActiveFrom)
                throw new ArgumentException("activeUntil must be after activeFrom.");
            if (dto.Enabled.HasValue) m.Enabled = dto.Enabled.Value;
        }

        public async Task DeleteAsync(int id)
        {
            var m = await _db.ProductionModifiers.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new KeyNotFoundException($"ProductionModifier with id {id} not found.");
            _db.ProductionModifiers.Remove(m);
            await _db.SaveChangesAsync();
        }

        public async Task<PagedResultDto<ProductionModifierListDto>> SearchAsync(PagedQueryDto query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            IQueryable<ProductionModifier> q = _db.ProductionModifiers.AsNoTracking();
            if (TerritoryPaging.Term(query) is { } term) q = q.Where(m => m.Name.ToLower().Contains(term));
            if (ProductionInput.Filter(query, "scope") is { } s && Enum.TryParse<ProductionModifierScope>(s, true, out var scope))
                q = q.Where(m => m.Scope == scope);
            if (TerritoryPaging.IntFilter(query, "scopeRefId") is { } refId) q = q.Where(m => m.ScopeRefId == refId);
            if (ProductionInput.Flag(query, "enabled") is { } enabled) q = q.Where(m => m.Enabled == enabled);
            q = (query.SortBy ?? "name").ToLowerInvariant() switch
            {
                "id" => ProductionInput.Sort(q, query.SortDescending, m => m.Id),
                _ => ProductionInput.Sort(q, query.SortDescending, m => m.Name),
            };
            return await TerritoryPaging.PageAsync(q.Select(m => new ProductionModifierListDto
            {
                Id = m.Id, Name = m.Name, Source = m.Source, Scope = m.Scope.ToString(), ScopeRefId = m.ScopeRefId,
                Target = m.Target.ToString(), Effect = m.Effect.ToString(), Value = m.Value, ActiveFrom = m.ActiveFrom,
                ActiveUntil = m.ActiveUntil, Enabled = m.Enabled,
            }), query);
        }

        public static ProductionModifierDto ToDto(ProductionModifier m) => new()
        {
            Id = m.Id, Name = m.Name, Source = m.Source, Scope = m.Scope.ToString(), ScopeRefId = m.ScopeRefId,
            Target = m.Target.ToString(), TargetItemBlueprintId = m.TargetItemBlueprintId, TargetCategory = m.TargetCategory?.ToString(),
            TargetProductionKindId = m.TargetProductionKindId, Effect = m.Effect.ToString(), Value = m.Value,
            ActiveFrom = m.ActiveFrom, ActiveUntil = m.ActiveUntil, Enabled = m.Enabled, CreatedAt = m.CreatedAt,
        };
    }

    // ===================================================================== Climate zones and weather types

    public interface IClimateZoneService
    {
        Task<IEnumerable<ClimateZoneDto>> GetAllAsync();
        Task<ClimateZoneDto?> GetByIdAsync(int id);
        Task<ClimateZoneDto> CreateAsync(ClimateZoneDto dto);
        Task UpdateAsync(int id, ClimateZoneDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<ClimateZoneListDto>> SearchAsync(PagedQueryDto query);
        Task<PagedResultDto<WeatherRollDto>> SearchRollsAsync(PagedQueryDto query);
    }

    public sealed class ClimateZoneService : IClimateZoneService
    {
        public const int RecentRollCount = 14;
        private readonly KnKDbContext _db;

        public ClimateZoneService(KnKDbContext db) => _db = db;

        private IQueryable<ClimateZone> Query() => _db.ClimateZones.AsNoTracking()
            .Include(z => z.WeatherOdds).ThenInclude(o => o.WeatherType).Include(z => z.Towns).ThenInclude(t => t.Town);

        public async Task<IEnumerable<ClimateZoneDto>> GetAllAsync() =>
            (await Query().OrderBy(z => z.Name).ToListAsync()).Select(z => ToDto(z, null)).ToList();

        public async Task<ClimateZoneDto?> GetByIdAsync(int id)
        {
            var zone = await Query().FirstOrDefaultAsync(z => z.Id == id);
            if (zone == null) return null;
            var rolls = await _db.WeatherRolls.AsNoTracking().Include(r => r.WeatherType).Where(r => r.ClimateZoneId == id)
                .OrderByDescending(r => r.Day).Take(RecentRollCount).ToListAsync();
            return ToDto(zone, rolls.Select(r => RollDto(r, zone.Name)).ToList());
        }

        public async Task<ClimateZoneDto> CreateAsync(ClimateZoneDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var zone = new ClimateZone { CreatedAt = DateTime.UtcNow };
            await ApplyAsync(zone, dto, isCreate: true);
            _db.ClimateZones.Add(zone);
            await _db.SaveChangesAsync();
            return (await GetByIdAsync(zone.Id))!;
        }

        public async Task UpdateAsync(int id, ClimateZoneDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var zone = await _db.ClimateZones.Include(z => z.WeatherOdds).Include(z => z.Towns).FirstOrDefaultAsync(z => z.Id == id)
                ?? throw new KeyNotFoundException($"ClimateZone with id {id} not found.");
            await ApplyAsync(zone, dto, isCreate: false);
            await _db.SaveChangesAsync();
        }

        private async Task ApplyAsync(ClimateZone zone, ClimateZoneDto dto, bool isCreate)
        {
            if (isCreate || dto.Name != null)
            {
                var name = ProductionInput.Name(dto.Name, ClimateZone.NameMaxLength);
                if (await _db.ClimateZones.AnyAsync(z => z.Id != zone.Id && z.Name == name))
                    throw new ArgumentException($"A climate zone named '{name}' already exists.");
                zone.Name = name;
            }
            if (dto.Description != null) zone.Description = ProductionInput.Text(dto.Description, ClimateZone.DescriptionMaxLength, "description");
            if (dto.IsDefault == true && !zone.IsDefault)
            {
                foreach (var other in await _db.ClimateZones.Where(z => z.IsDefault && z.Id != zone.Id).ToListAsync()) other.IsDefault = false;
                zone.IsDefault = true;
            }
            else if (dto.IsDefault == false && zone.IsDefault)
                throw new ArgumentException("This is the default zone; make another zone the default instead.");
            else if (isCreate && !await _db.ClimateZones.AnyAsync(z => z.IsDefault))
                zone.IsDefault = true;

            if (dto.WeatherOdds != null)
            {
                var odds = new List<ClimateZoneWeather>();
                foreach (var o in dto.WeatherOdds)
                {
                    if (o.ChancePercent is < 0 or > 100) throw new ArgumentException("chancePercent must be 0 to 100.");
                    if (!await _db.WeatherTypes.AnyAsync(w => w.Id == o.WeatherTypeId))
                        throw new ArgumentException($"WeatherType with id {o.WeatherTypeId} not found.");
                    if (odds.Any(x => x.WeatherTypeId == o.WeatherTypeId)) throw new ArgumentException($"Weather type {o.WeatherTypeId} is listed twice.");
                    odds.Add(new ClimateZoneWeather { WeatherTypeId = o.WeatherTypeId, ChancePercent = o.ChancePercent });
                }
                if (odds.Sum(o => o.ChancePercent) > 100) throw new ArgumentException("The weather odds add up to more than 100%.");
                _db.ClimateZoneWeathers.RemoveRange(zone.WeatherOdds);
                zone.WeatherOdds = odds;
            }
            if (dto.Towns != null)
            {
                var townIds = dto.Towns.Select(t => t.TownId).Distinct().ToList();
                var known = await _db.Towns.Where(t => townIds.Contains(t.Id)).Select(t => t.Id).ToListAsync();
                var missing = townIds.Except(known).ToList();
                if (missing.Count > 0) throw new ArgumentException($"Town {string.Join(", ", missing)} not found.");
                // A town is in one zone: it moves here from its previous zone.
                var elsewhere = await _db.ClimateZoneTowns.Where(t => townIds.Contains(t.TownId) && t.ClimateZoneId != zone.Id).ToListAsync();
                _db.ClimateZoneTowns.RemoveRange(elsewhere);
                foreach (var gone in zone.Towns.Where(t => !townIds.Contains(t.TownId)).ToList()) _db.ClimateZoneTowns.Remove(gone);
                foreach (var townId in townIds.Where(id => zone.Towns.All(t => t.TownId != id)))
                    zone.Towns.Add(new ClimateZoneTown { TownId = townId });
            }
        }

        public async Task DeleteAsync(int id)
        {
            var zone = await _db.ClimateZones.FirstOrDefaultAsync(z => z.Id == id)
                ?? throw new KeyNotFoundException($"ClimateZone with id {id} not found.");
            if (zone.IsDefault) throw new InvalidOperationException("The default climate zone can't be deleted; make another zone the default first.");
            _db.ClimateZones.Remove(zone); // its towns fall back to the default zone; its rolls go with it
            await _db.SaveChangesAsync();
        }

        public async Task<PagedResultDto<ClimateZoneListDto>> SearchAsync(PagedQueryDto query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            IQueryable<ClimateZone> q = _db.ClimateZones.AsNoTracking();
            if (TerritoryPaging.Term(query) is { } term) q = q.Where(z => z.Name.ToLower().Contains(term));
            q = (query.SortBy ?? "name").ToLowerInvariant() switch
            {
                "id" => ProductionInput.Sort(q, query.SortDescending, z => z.Id),
                _ => ProductionInput.Sort(q, query.SortDescending, z => z.Name),
            };
            return await TerritoryPaging.PageAsync(q.Select(z => new ClimateZoneListDto
            {
                Id = z.Id, Name = z.Name, IsDefault = z.IsDefault, TownCount = z.Towns.Count, WeatherTypeCount = z.WeatherOdds.Count,
            }), query);
        }

        public async Task<PagedResultDto<WeatherRollDto>> SearchRollsAsync(PagedQueryDto query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            IQueryable<WeatherRoll> q = _db.WeatherRolls.AsNoTracking().Include(r => r.WeatherType).Include(r => r.ClimateZone);
            if (TerritoryPaging.IntFilter(query, "climateZoneId") is { } zoneId) q = q.Where(r => r.ClimateZoneId == zoneId);
            q = query.SortDescending || query.SortBy == null ? q.OrderByDescending(r => r.Day).ThenBy(r => r.ClimateZoneId)
                : q.OrderBy(r => r.Day).ThenBy(r => r.ClimateZoneId);
            var page = await TerritoryPaging.PageAsync(q, query);
            return new PagedResultDto<WeatherRollDto>
            {
                Items = page.Items.Select(r => RollDto(r, r.ClimateZone?.Name)).ToList(),
                TotalCount = page.TotalCount, PageNumber = page.PageNumber, PageSize = page.PageSize,
            };
        }

        public static WeatherRollDto RollDto(WeatherRoll r, string? zoneName) => new()
        {
            Id = r.Id, ClimateZoneId = r.ClimateZoneId, ClimateZoneName = zoneName, Day = r.Day, DayStartsAt = r.DayStartsAt,
            DayEndsAt = r.DayEndsAt, WeatherTypeId = r.WeatherTypeId, WeatherTypeName = r.WeatherType?.Name ?? "Clear",
            WeatherType = r.WeatherType == null ? null : new NamedRefDto { Id = r.WeatherType.Id, Name = r.WeatherType.Name },
            RollValue = r.RollValue, RolledAt = r.RolledAt,
        };

        private static ClimateZoneDto ToDto(ClimateZone z, List<WeatherRollDto>? rolls) => new()
        {
            Id = z.Id, Name = z.Name, Description = z.Description, IsDefault = z.IsDefault, CreatedAt = z.CreatedAt,
            WeatherOdds = z.WeatherOdds.OrderBy(o => o.WeatherTypeId).Select(o => new ClimateZoneWeatherDto
            {
                Id = o.Id, WeatherTypeId = o.WeatherTypeId, WeatherTypeName = o.WeatherType?.Name, ChancePercent = o.ChancePercent,
                WeatherType = o.WeatherType == null ? null : new NamedRefDto { Id = o.WeatherType.Id, Name = o.WeatherType.Name },
            }).ToList(),
            Towns = z.Towns.OrderBy(t => t.TownId).Select(t => new ClimateZoneTownDto
            {
                TownId = t.TownId, TownName = t.Town?.Name, Town = t.Town == null ? null : new NamedRefDto { Id = t.Town.Id, Name = t.Town.Name },
            }).ToList(),
            Rolls = rolls,
        };
    }

    public interface IWeatherTypeService
    {
        Task<IEnumerable<WeatherTypeDto>> GetAllAsync();
        Task<WeatherTypeDto?> GetByIdAsync(int id);
        Task<WeatherTypeDto> CreateAsync(WeatherTypeDto dto);
        Task UpdateAsync(int id, WeatherTypeDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<WeatherTypeListDto>> SearchAsync(PagedQueryDto query);
    }

    public sealed class WeatherTypeService : IWeatherTypeService
    {
        private readonly KnKDbContext _db;

        public WeatherTypeService(KnKDbContext db) => _db = db;

        public async Task<IEnumerable<WeatherTypeDto>> GetAllAsync() =>
            (await _db.WeatherTypes.AsNoTracking().Include(w => w.Effects).OrderBy(w => w.Name).ToListAsync()).Select(ToDto).ToList();

        public async Task<WeatherTypeDto?> GetByIdAsync(int id)
        {
            var w = await _db.WeatherTypes.AsNoTracking().Include(x => x.Effects).FirstOrDefaultAsync(x => x.Id == id);
            return w == null ? null : ToDto(w);
        }

        public async Task<WeatherTypeDto> CreateAsync(WeatherTypeDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var w = new WeatherType { CreatedAt = DateTime.UtcNow };
            await ApplyAsync(w, dto, isCreate: true);
            _db.WeatherTypes.Add(w);
            await _db.SaveChangesAsync();
            return (await GetByIdAsync(w.Id))!;
        }

        public async Task UpdateAsync(int id, WeatherTypeDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var w = await _db.WeatherTypes.Include(x => x.Effects).FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new KeyNotFoundException($"WeatherType with id {id} not found.");
            await ApplyAsync(w, dto, isCreate: false);
            await _db.SaveChangesAsync();
        }

        private async Task ApplyAsync(WeatherType w, WeatherTypeDto dto, bool isCreate)
        {
            if (isCreate || dto.Name != null)
            {
                var name = ProductionInput.Name(dto.Name, WeatherType.NameMaxLength);
                if (string.Equals(name, "Clear", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("'Clear' is the name of no weather (the rest of a zone's odds).");
                if (await _db.WeatherTypes.AnyAsync(x => x.Id != w.Id && x.Name == name))
                    throw new ArgumentException($"A weather type named '{name}' already exists.");
                w.Name = name;
            }
            if (dto.Description != null) w.Description = ProductionInput.Text(dto.Description, WeatherType.DescriptionMaxLength, "description");
            if (dto.Effects != null)
            {
                var effects = new List<WeatherTypeEffect>();
                foreach (var e in dto.Effects)
                {
                    var category = ProductionInput.Parse<ResourceCategory>(e.Category, "effects category");
                    var effect = ProductionInput.Parse<ProductionModifierEffect>(e.Effect, "effects effect");
                    if (effect == ProductionModifierEffect.Multiply && e.Value < 0) throw new ArgumentException("A multiplying effect can't be negative.");
                    if (Math.Abs(e.Value) > ProductionModifierService.MaxValue) throw new ArgumentException("An effect value is too large.");
                    if (effects.Any(x => x.Category == category)) throw new ArgumentException($"Category {category} is listed twice.");
                    effects.Add(new WeatherTypeEffect { Category = category, Effect = effect, Value = e.Value });
                }
                _db.WeatherTypeEffects.RemoveRange(w.Effects);
                w.Effects = effects;
            }
            await Task.CompletedTask;
        }

        public async Task DeleteAsync(int id)
        {
            var w = await _db.WeatherTypes.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new KeyNotFoundException($"WeatherType with id {id} not found.");
            if (await _db.ClimateZoneWeathers.AnyAsync(o => o.WeatherTypeId == id))
                throw new InvalidOperationException("A climate zone has odds for this weather type; remove them first.");
            _db.WeatherTypes.Remove(w); // past rolls keep their day and read as "Clear" (SetNull)
            await _db.SaveChangesAsync();
        }

        public async Task<PagedResultDto<WeatherTypeListDto>> SearchAsync(PagedQueryDto query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            IQueryable<WeatherType> q = _db.WeatherTypes.AsNoTracking();
            if (TerritoryPaging.Term(query) is { } term) q = q.Where(w => w.Name.ToLower().Contains(term));
            q = (query.SortBy ?? "name").ToLowerInvariant() switch
            {
                "id" => ProductionInput.Sort(q, query.SortDescending, w => w.Id),
                _ => ProductionInput.Sort(q, query.SortDescending, w => w.Name),
            };
            return await TerritoryPaging.PageAsync(q.Select(w => new WeatherTypeListDto { Id = w.Id, Name = w.Name, EffectCount = w.Effects.Count }), query);
        }

        public static WeatherTypeDto ToDto(WeatherType w) => new()
        {
            Id = w.Id, Name = w.Name, Description = w.Description, CreatedAt = w.CreatedAt,
            Effects = w.Effects.OrderBy(e => e.Category).Select(e => new WeatherTypeEffectDto
            {
                Id = e.Id, Category = e.Category.ToString(), Effect = e.Effect.ToString(), Value = e.Value,
            }).ToList(),
        };
    }

    // ===================================================================== Cycle records

    public interface IProductionCycleRecordService
    {
        Task<ProductionCycleRecordDto?> GetByIdAsync(long id);
        Task<PagedResultDto<ProductionCycleRecordDto>> SearchAsync(PagedQueryDto query);
    }

    /// <summary>The cycle trace (read only, newest first).</summary>
    public sealed class ProductionCycleRecordService : IProductionCycleRecordService
    {
        private readonly KnKDbContext _db;

        public ProductionCycleRecordService(KnKDbContext db) => _db = db;

        public async Task<ProductionCycleRecordDto?> GetByIdAsync(long id)
        {
            var r = await _db.ProductionCycleRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (r == null) return null;
            var name = await _db.ItemBlueprints.AsNoTracking().Where(b => b.Id == r.ItemBlueprintId).Select(b => b.Name).FirstOrDefaultAsync();
            return ProductionCycleRecordMapping.ToDto(r, name);
        }

        public async Task<PagedResultDto<ProductionCycleRecordDto>> SearchAsync(PagedQueryDto query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            IQueryable<ProductionCycleRecord> q = _db.ProductionCycleRecords.AsNoTracking();
            if (TerritoryPaging.IntFilter(query, "productionStructureId") is { } sid) q = q.Where(r => r.ProductionStructureId == sid);
            if (TerritoryPaging.IntFilter(query, "productionLineId") is { } lid) q = q.Where(r => r.ProductionLineId == lid);
            q = query.SortBy != null && !query.SortDescending
                ? q.OrderBy(r => r.SettledAt).ThenBy(r => r.Id)
                : q.OrderByDescending(r => r.SettledAt).ThenByDescending(r => r.Id);
            var page = await TerritoryPaging.PageAsync(q, query);
            var ids = page.Items.Select(r => r.ItemBlueprintId).Distinct().ToList();
            var names = await _db.ItemBlueprints.AsNoTracking().Where(b => ids.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name);
            return new PagedResultDto<ProductionCycleRecordDto>
            {
                Items = page.Items.Select(r => ProductionCycleRecordMapping.ToDto(r, names.GetValueOrDefault(r.ItemBlueprintId))).ToList(),
                TotalCount = page.TotalCount, PageNumber = page.PageNumber, PageSize = page.PageSize,
            };
        }
    }
}
