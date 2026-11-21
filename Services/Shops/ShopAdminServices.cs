using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Stock;
using knkwebapi_v2.Services.Production;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Shops
{
    // Admin CRUD of shops P10 (PandiO/knk-workspace#248): shop types with accepted categories, their levels, shops. Works
    // while Production:Enabled is off (DESIGN § 18.2). Errors: ArgumentException (400), KeyNotFoundException (404),
    // InvalidOperationException (409).

    internal static class ShopEnum
    {
        public static T Parse<T>(string? value, string field) where T : struct, Enum
        {
            if (value == null || int.TryParse(value.Trim(), out _) || !Enum.TryParse<T>(value.Trim(), true, out var parsed) || !Enum.IsDefined(parsed))
                throw new ArgumentException($"{field} must be one of {string.Join(", ", Enum.GetNames<T>())}.");
            return parsed;
        }

        public static string Text(string? value, string field, int max, bool required)
        {
            var t = value?.Trim();
            if (string.IsNullOrEmpty(t))
            {
                if (required) throw new ArgumentException($"{field} is required.");
                return string.Empty;
            }
            if (t.Length > max) throw new ArgumentException($"{field} may be at most {max} characters.");
            return t;
        }

        public static decimal Multiplier(decimal value, string field, decimal max = 100m)
        {
            if (value <= 0m || value > max) throw new ArgumentException($"{field} must be above 0 and at most {max}.");
            return value;
        }

        public static PagedResultDto<T> Page<T>(List<T> items, int total, PagedQueryDto q) => new()
        {
            Items = items, TotalCount = total, PageNumber = Math.Max(1, q.PageNumber), PageSize = Size(q),
        };

        public static int Size(PagedQueryDto q) => q.PageSize is > 0 and <= 200 ? q.PageSize : 20;

        public static int Skip(PagedQueryDto q) => (Math.Max(1, q.PageNumber) - 1) * Size(q);
    }

    /// <summary>A shop type's level caps on its shop's storages (shops DESIGN § 5): Merchandise and Input caps, units per
    /// slot, the worker's capacity. A null value leaves the current one.</summary>
    public static class ShopLevels
    {
        public static Task<ShopTypeLevel?> FindAsync(KnKDbContext db, int? shopTypeId, int level, CancellationToken ct = default) =>
            shopTypeId == null ? Task.FromResult<ShopTypeLevel?>(null)
                : db.Set<ShopTypeLevel>().AsNoTracking().FirstOrDefaultAsync(l => l.ShopTypeId == shopTypeId && l.Level == level, ct);

        public static async Task ApplyAsync(KnKDbContext db, Shop shop, ShopTypeLevel? level, CancellationToken ct = default)
        {
            if (level == null || shop.Id == 0) return;
            var storages = await db.Storages.Where(s => s.Kind == StorageKind.Domain && s.DomainId == shop.Id
                && (s.Purpose == StoragePurpose.Merchandise || s.Purpose == StoragePurpose.Input)).ToListAsync(ct);
            foreach (var s in storages)
            {
                long? cap = s.Purpose == StoragePurpose.Merchandise ? level.MerchandiseMaxUnits : level.InputMaxUnits;
                if (cap.HasValue) s.MaxUnits = cap.Value;
                if (level.UnitsPerSlot.HasValue) s.UnitsPerSlot = level.UnitsPerSlot.Value;
                if (cap.HasValue || level.UnitsPerSlot.HasValue)
                    await knkwebapi_v2.Services.Stock.StorageContainerCapacity.ApplyAsync(db, s, ct);
            }
        }

        /// <summary>The level as the production engine reads it (cycle speed and yield), for a shop's processing lines.</summary>
        public static ProductionKindLevel? AsEngineLevel(ShopTypeLevel? level) => level == null ? null : new ProductionKindLevel
        {
            Level = level.Level, CycleSpeedMultiplier = level.CycleSpeedMultiplier, YieldMultiplier = level.YieldMultiplier,
        };
    }

    // ================= Shop types and levels =================

    public interface IShopTypeService
    {
        Task<ShopTypeDto?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<PagedResultDto<ShopTypeListDto>> SearchAsync(PagedQueryDto query, CancellationToken ct = default);
        Task<ShopTypeDto> CreateAsync(ShopTypeDto dto, CancellationToken ct = default);
        Task<ShopTypeDto> UpdateAsync(int id, ShopTypeDto dto, CancellationToken ct = default);
        /// <summary>409 while a shop uses the type.</summary>
        Task DeleteAsync(int id, CancellationToken ct = default);

        Task<ShopTypeLevelDto?> GetLevelAsync(int id, CancellationToken ct = default);
        Task<PagedResultDto<ShopTypeLevelListDto>> SearchLevelsAsync(PagedQueryDto query, CancellationToken ct = default);
        Task<ShopTypeLevelDto> CreateLevelAsync(ShopTypeLevelDto dto, CancellationToken ct = default);
        /// <summary>Also writes the new caps to the storages of the type's shops at that level.</summary>
        Task<ShopTypeLevelDto> UpdateLevelAsync(int id, ShopTypeLevelDto dto, CancellationToken ct = default);
        Task DeleteLevelAsync(int id, CancellationToken ct = default);
    }

    public sealed class ShopTypeService : IShopTypeService
    {
        private readonly KnKDbContext _db;

        public ShopTypeService(KnKDbContext db) => _db = db;

        public async Task<ShopTypeDto?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            var t = await _db.Set<ShopType>().AsNoTracking().Include(x => x.AcceptedCategories).ThenInclude(c => c.Category)
                .Include(x => x.Levels).FirstOrDefaultAsync(x => x.Id == id, ct);
            return t == null ? null : ToDto(t);
        }

        private static ShopTypeDto ToDto(ShopType t) => new()
        {
            Id = t.Id, Name = t.Name, Description = t.Description, DefaultKind = t.DefaultKind.ToString(), IsActive = t.IsActive,
            CreatedAt = t.CreatedAt,
            AcceptedCategoryIds = t.AcceptedCategories.OrderBy(c => c.CategoryId).Select(c => c.CategoryId).ToList(),
            AcceptedCategories = t.AcceptedCategories.OrderBy(c => c.CategoryId)
                .Select(c => new NamedRefDto { Id = c.CategoryId, Name = c.Category?.Name ?? string.Empty }).ToList(),
            Levels = t.Levels.OrderBy(l => l.Level).Select(l => LevelDto(l, t)).ToList(),
        };

        public async Task<PagedResultDto<ShopTypeListDto>> SearchAsync(PagedQueryDto query, CancellationToken ct = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            IQueryable<ShopType> q = _db.Set<ShopType>().AsNoTracking();
            if (TerritoryPaging.Term(query) is { } term) q = q.Where(t => t.Name.ToLower().Contains(term));
            if (query.Filters != null && query.Filters.TryGetValue("isActive", out var a) && bool.TryParse(a, out var active))
                q = q.Where(t => t.IsActive == active);
            int total = await q.CountAsync(ct);
            var items = await q.OrderBy(t => t.Name).ThenBy(t => t.Id).Skip(ShopEnum.Skip(query)).Take(ShopEnum.Size(query))
                .Select(t => new ShopTypeListDto
                {
                    Id = t.Id, Name = t.Name, DefaultKind = t.DefaultKind.ToString(), IsActive = t.IsActive,
                    AcceptedCategoryCount = t.AcceptedCategories.Count, LevelCount = t.Levels.Count,
                }).ToListAsync(ct);
            return ShopEnum.Page(items, total, query);
        }

        public async Task<ShopTypeDto> CreateAsync(ShopTypeDto dto, CancellationToken ct = default)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var t = new ShopType();
            await ApplyAsync(t, dto, isCreate: true, ct);
            _db.Set<ShopType>().Add(t);
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
            return (await GetByIdAsync(t.Id, ct))!;
        }

        public async Task<ShopTypeDto> UpdateAsync(int id, ShopTypeDto dto, CancellationToken ct = default)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var t = await _db.Set<ShopType>().Include(x => x.AcceptedCategories).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new KeyNotFoundException($"ShopType with id {id} not found.");
            await ApplyAsync(t, dto, isCreate: false, ct);
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
            return (await GetByIdAsync(id, ct))!;
        }

        private async Task ApplyAsync(ShopType t, ShopTypeDto dto, bool isCreate, CancellationToken ct)
        {
            if (isCreate || dto.Name != null)
            {
                var name = ShopEnum.Text(dto.Name, "name", ShopType.NameMaxLength, required: true);
                if (await _db.Set<ShopType>().AnyAsync(x => x.Name == name && x.Id != t.Id, ct))
                    throw new InvalidOperationException($"A shop type named '{name}' already exists.");
                t.Name = name;
            }
            if (dto.Description != null)
            {
                var d = ShopEnum.Text(dto.Description, "description", ShopType.DescriptionMaxLength, required: false);
                t.Description = d.Length == 0 ? null : d;
            }
            if (dto.DefaultKind != null) t.DefaultKind = ShopEnum.Parse<ShopKind>(dto.DefaultKind, "defaultKind");
            if (dto.IsActive.HasValue) t.IsActive = dto.IsActive.Value;
            if (dto.AcceptedCategoryIds != null)
            {
                var ids = dto.AcceptedCategoryIds.Distinct().ToList();
                var known = await _db.Categories.AsNoTracking().Where(c => ids.Contains(c.Id)).Select(c => c.Id).ToListAsync(ct);
                var missing = ids.Except(known).ToList();
                if (missing.Count > 0) throw new ArgumentException($"Category {string.Join(", ", missing)} not found.");
                foreach (var gone in t.AcceptedCategories.Where(c => !ids.Contains(c.CategoryId)).ToList()) t.AcceptedCategories.Remove(gone);
                foreach (var add in ids.Where(i => t.AcceptedCategories.All(c => c.CategoryId != i)))
                    t.AcceptedCategories.Add(new ShopTypeCategory { CategoryId = add });
            }
        }

        public async Task DeleteAsync(int id, CancellationToken ct = default)
        {
            var t = await _db.Set<ShopType>().FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new KeyNotFoundException($"ShopType with id {id} not found.");
            int used = await _db.Set<Shop>().CountAsync(s => s.ShopTypeId == id, ct);
            if (used > 0) throw new InvalidOperationException($"{used} shop(s) use this shop type; give them another type first.");
            _db.Set<ShopType>().Remove(t);
            await _db.SaveChangesAsync(ct);
        }

        // ---- levels ----

        private static ShopTypeLevelDto LevelDto(ShopTypeLevel l, ShopType? t) => new()
        {
            Id = l.Id, ShopTypeId = l.ShopTypeId, ShopType = t == null ? null : new NamedRefDto { Id = t.Id, Name = t.Name },
            Level = l.Level, CycleSpeedMultiplier = l.CycleSpeedMultiplier, YieldMultiplier = l.YieldMultiplier,
            InputCostMultiplier = l.InputCostMultiplier, PriceMultiplier = l.PriceMultiplier,
            MerchandiseMaxUnits = l.MerchandiseMaxUnits, InputMaxUnits = l.InputMaxUnits, UnitsPerSlot = l.UnitsPerSlot,
            ReorderPointUnits = l.ReorderPointUnits, TargetUnits = l.TargetUnits,
        };

        public async Task<ShopTypeLevelDto?> GetLevelAsync(int id, CancellationToken ct = default)
        {
            var l = await _db.Set<ShopTypeLevel>().AsNoTracking().Include(x => x.ShopType).FirstOrDefaultAsync(x => x.Id == id, ct);
            return l == null ? null : LevelDto(l, l.ShopType);
        }

        public async Task<PagedResultDto<ShopTypeLevelListDto>> SearchLevelsAsync(PagedQueryDto query, CancellationToken ct = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            IQueryable<ShopTypeLevel> q = _db.Set<ShopTypeLevel>().AsNoTracking();
            if (TerritoryPaging.IntFilter(query, "shopTypeId") is { } typeId) q = q.Where(l => l.ShopTypeId == typeId);
            int total = await q.CountAsync(ct);
            var items = await q.OrderBy(l => l.ShopTypeId).ThenBy(l => l.Level).Skip(ShopEnum.Skip(query)).Take(ShopEnum.Size(query))
                .Select(l => new ShopTypeLevelListDto
                {
                    Id = l.Id, ShopTypeId = l.ShopTypeId, ShopTypeName = l.ShopType.Name, Level = l.Level,
                    PriceMultiplier = l.PriceMultiplier, CycleSpeedMultiplier = l.CycleSpeedMultiplier,
                    YieldMultiplier = l.YieldMultiplier, MerchandiseMaxUnits = l.MerchandiseMaxUnits,
                }).ToListAsync(ct);
            return ShopEnum.Page(items, total, query);
        }

        public async Task<ShopTypeLevelDto> CreateLevelAsync(ShopTypeLevelDto dto, CancellationToken ct = default)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (dto.ShopTypeId is not > 0 || !await _db.Set<ShopType>().AnyAsync(t => t.Id == dto.ShopTypeId, ct))
                throw new ArgumentException("shopTypeId must name an existing shop type.");
            var l = new ShopTypeLevel { ShopTypeId = dto.ShopTypeId.Value, Level = dto.Level ?? 0 };
            await ApplyLevelAsync(l, dto, ct);
            _db.Set<ShopTypeLevel>().Add(l);
            await _db.SaveChangesAsync(ct);
            await ApplyToShopsAsync(l, ct);
            _db.ChangeTracker.Clear();
            return (await GetLevelAsync(l.Id, ct))!;
        }

        public async Task<ShopTypeLevelDto> UpdateLevelAsync(int id, ShopTypeLevelDto dto, CancellationToken ct = default)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var l = await _db.Set<ShopTypeLevel>().FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new KeyNotFoundException($"ShopTypeLevel with id {id} not found.");
            if (dto.ShopTypeId.HasValue && dto.ShopTypeId != l.ShopTypeId)
                throw new ArgumentException("A level stays with its shop type; create a level on the other type instead.");
            if (dto.Level.HasValue) l.Level = dto.Level.Value;
            await ApplyLevelAsync(l, dto, ct);
            await _db.SaveChangesAsync(ct);
            await ApplyToShopsAsync(l, ct);
            _db.ChangeTracker.Clear();
            return (await GetLevelAsync(id, ct))!;
        }

        private async Task ApplyLevelAsync(ShopTypeLevel l, ShopTypeLevelDto dto, CancellationToken ct)
        {
            if (l.Level is < 1 or > ShopTypeLevel.MaxLevel) throw new ArgumentException($"level must be 1 to {ShopTypeLevel.MaxLevel}.");
            if (await _db.Set<ShopTypeLevel>().AnyAsync(x => x.ShopTypeId == l.ShopTypeId && x.Level == l.Level && x.Id != l.Id, ct))
                throw new InvalidOperationException($"The shop type already has level {l.Level}.");
            if (dto.CycleSpeedMultiplier.HasValue) l.CycleSpeedMultiplier = ShopEnum.Multiplier(dto.CycleSpeedMultiplier.Value, "cycleSpeedMultiplier");
            if (dto.YieldMultiplier.HasValue) l.YieldMultiplier = ShopEnum.Multiplier(dto.YieldMultiplier.Value, "yieldMultiplier");
            if (dto.InputCostMultiplier.HasValue) l.InputCostMultiplier = ShopEnum.Multiplier(dto.InputCostMultiplier.Value, "inputCostMultiplier");
            if (dto.PriceMultiplier.HasValue) l.PriceMultiplier = ShopEnum.Multiplier(dto.PriceMultiplier.Value, "priceMultiplier");
            static long? Cap(long? v, string field) => v switch
            {
                null => throw new InvalidOperationException(),
                < 0 => throw new ArgumentException($"{field} can't be negative."),
                0 => null,
                _ => v,
            };
            if (dto.MerchandiseMaxUnits.HasValue) l.MerchandiseMaxUnits = Cap(dto.MerchandiseMaxUnits, "merchandiseMaxUnits");
            if (dto.InputMaxUnits.HasValue) l.InputMaxUnits = Cap(dto.InputMaxUnits, "inputMaxUnits");
            if (dto.UnitsPerSlot.HasValue) l.UnitsPerSlot = (int?)Cap(dto.UnitsPerSlot, "unitsPerSlot");
            if (dto.ReorderPointUnits.HasValue) l.ReorderPointUnits = Cap(dto.ReorderPointUnits, "reorderPointUnits");
            if (dto.TargetUnits.HasValue) l.TargetUnits = Cap(dto.TargetUnits, "targetUnits");
            if (l.ReorderPointUnits.HasValue && l.TargetUnits.HasValue && l.TargetUnits < l.ReorderPointUnits)
                throw new ArgumentException("targetUnits must be at least reorderPointUnits.");
        }

        private async Task ApplyToShopsAsync(ShopTypeLevel l, CancellationToken ct)
        {
            var shops = await _db.Set<Shop>().Where(s => s.ShopTypeId == l.ShopTypeId && s.Level == l.Level).ToListAsync(ct);
            foreach (var s in shops) await ShopLevels.ApplyAsync(_db, s, l, ct);
            await _db.SaveChangesAsync(ct);
        }

        public async Task DeleteLevelAsync(int id, CancellationToken ct = default)
        {
            var l = await _db.Set<ShopTypeLevel>().FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new KeyNotFoundException($"ShopTypeLevel with id {id} not found.");
            _db.Set<ShopTypeLevel>().Remove(l);
            await _db.SaveChangesAsync(ct);
        }
    }

    // ================= Shops =================

    public interface IShopService
    {
        Task<IEnumerable<ShopDto>> GetAllAsync();
        Task<ShopDto?> GetByIdAsync(int id);
        Task<ShopDto> CreateAsync(ShopDto dto);
        Task<ShopDto> UpdateAsync(int id, ShopDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<ProductionWorldListDto>> SearchAsync(PagedQueryDto query);
    }

    /// <summary>
    /// Shops (shops DESIGN § 2.1): an Input and a Merchandise storage on create, capped by the shop type's level; the shop's
    /// own worker (default 1); processing lines on the production engine for a Processing shop. A shop is not surveyed
    /// (rates Configured) and never ships its goods away (no automatic transport): its worker only collects.
    /// </summary>
    public sealed class ShopService : StructureSubtypeService<Shop, ShopDto>, IShopService
    {
        public const int DefaultWorkers = 1;

        private readonly IProductionEngine? _engine;
        private ShopTypeLevel? _createLevel;

        public ShopService(KnKDbContext db, StructureBaseWriter writer, IStorageService storages, IMapper mapper,
            IProductionEngine? engine = null)
            : base(db, writer, storages, mapper)
        {
            _engine = engine;
        }

        protected override string TypeName => nameof(Shop);
        protected override DbSet<Shop> Set => Db.Set<Shop>();

        protected override IQueryable<Shop> WithDetails(IQueryable<Shop> q) => q.Include(s => s.ShopType);

        protected override async Task BeforeUpdateAsync(int id, ShopDto dto)
        {
            // Processing up to now counts at the old level, workers and switch.
            if (_engine != null && (dto.Level.HasValue || dto.ShopTypeId.HasValue || dto.WorkersTotal.HasValue
                    || dto.ProductionEnabled.HasValue || dto.Kind != null))
                await _engine.SettleStructureAsync(id);
        }

        protected override async Task AfterUpdateAsync(int id, ShopDto dto)
        {
            if (_engine != null && dto.ProductionEnabled == true) await _engine.SettleStructureAsync(id);
        }

        protected override async Task ApplyOwnAsync(Shop target, ShopDto dto, bool isCreate)
        {
            int? previousType = target.ShopTypeId;
            int previousLevel = target.Level;
            ShopType? type = null;
            if (dto.ShopTypeId.HasValue)
            {
                if (dto.ShopTypeId.Value > 0)
                {
                    type = await Db.Set<ShopType>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == dto.ShopTypeId.Value)
                        ?? throw new ArgumentException($"ShopType with id {dto.ShopTypeId} not found.", nameof(dto));
                    target.ShopTypeId = type.Id;
                }
                else target.ShopTypeId = null;
            }
            if (dto.Kind != null) target.Kind = ShopEnum.Parse<ShopKind>(dto.Kind, "kind");
            else if (isCreate && type != null) target.Kind = type.DefaultKind;
            if (dto.Level.HasValue)
            {
                if (dto.Level.Value is < 1 or > ShopTypeLevel.MaxLevel)
                    throw new ArgumentException($"level must be 1 to {ShopTypeLevel.MaxLevel}.", nameof(dto));
                target.Level = dto.Level.Value;
            }
            if (dto.TaxModifier.HasValue) target.TaxModifier = ShopEnum.Multiplier(dto.TaxModifier.Value, "taxModifier", Shop.MaxTaxModifier);
            if (dto.BuyBackPercent.HasValue)
            {
                if (dto.BuyBackPercent.Value is < 0 or > 100) throw new ArgumentException("buyBackPercent must be 0 to 100.", nameof(dto));
                target.BuyBackPercent = dto.BuyBackPercent.Value;
            }
            if (dto.ShopkeeperLocationId.HasValue)
            {
                if (dto.ShopkeeperLocationId.Value > 0)
                {
                    if (!await Db.Locations.AnyAsync(l => l.Id == dto.ShopkeeperLocationId.Value))
                        throw new ArgumentException($"Location with id {dto.ShopkeeperLocationId} not found.", nameof(dto));
                    target.ShopkeeperLocationId = dto.ShopkeeperLocationId.Value;
                }
                else target.ShopkeeperLocationId = null;
            }
            if (dto.HideSoldOut.HasValue) target.HideSoldOut = dto.HideSoldOut.Value;
            if (dto.ProductionEnabled.HasValue) target.ProductionEnabled = dto.ProductionEnabled.Value;
            if (dto.TransportCapacity.HasValue)
            {
                if (dto.TransportCapacity.Value < 0) throw new ArgumentException("transportCapacity can't be negative.", nameof(dto));
                target.TransportCapacity = dto.TransportCapacity.Value > 0 ? dto.TransportCapacity.Value : null;
            }
            if (isCreate)
            {
                // A shop's worker only collects (TOWN_LOGISTICS § 5) and its rates are configured, never surveyed.
                target.AutomaticTransport = false;
                target.YieldSource = ProductionRateSource.Configured;
                target.ProductionKindId = null;
                target.WorkersTotal = DefaultWorkers;
                target.WorkersPresent = DefaultWorkers;
            }
            if (dto.WorkersTotal.HasValue)
            {
                if (dto.WorkersTotal.Value is < 0 or > ProductionStructure.MaxWorkers)
                    throw new ArgumentException($"workersTotal must be 0 to {ProductionStructure.MaxWorkers}.", nameof(dto));
                target.WorkersTotal = dto.WorkersTotal.Value;
                if (isCreate || target.WorkersPresent > target.WorkersTotal) target.WorkersPresent = target.WorkersTotal;
            }
            if (isCreate) _createLevel = await ShopLevels.FindAsync(Db, target.ShopTypeId, target.Level);
            else if (target.ShopTypeId != previousType || target.Level != previousLevel)
                await ShopLevels.ApplyAsync(Db, target, await ShopLevels.FindAsync(Db, target.ShopTypeId, target.Level));
        }

        protected override IEnumerable<StorageDto> DefaultStorages(Shop created)
        {
            yield return new StorageDto
            {
                Name = StorageName(created.Name, "merchandise"), Purpose = StoragePurpose.Merchandise,
                MaxUnits = _createLevel?.MerchandiseMaxUnits, UnitsPerSlot = _createLevel?.UnitsPerSlot,
            };
            yield return new StorageDto
            {
                Name = StorageName(created.Name, "input"), Purpose = StoragePurpose.Input,
                MaxUnits = _createLevel?.InputMaxUnits, UnitsPerSlot = _createLevel?.UnitsPerSlot,
            };
        }

        protected override async Task FillStoragesAsync(int id, ShopDto dto)
        {
            dto.Storages = await StoragesOfAsync(id);
            dto.CatalogueEntryCount = await Db.Set<ShopCatalogueEntry>().CountAsync(e => e.ShopId == id);
        }

        protected override void Fill(Shop entity, ShopDto dto) => dto.EffectiveTownId = entity.District?.TownId ?? entity.TownId;

        protected override IQueryable<Shop> IncludeForList(IQueryable<Shop> q) => q.Include(s => s.ShopType);

        protected override IQueryable<Shop> Filter(IQueryable<Shop> q, PagedQueryDto query)
        {
            if (TerritoryPaging.IntFilter(query, "shopTypeId") is { } typeId) q = q.Where(s => s.ShopTypeId == typeId);
            if (query.Filters != null && query.Filters.TryGetValue("kind", out var k) && Enum.TryParse<ShopKind>(k, true, out var kind))
                q = q.Where(s => s.Kind == kind);
            return q;
        }

        protected override async Task BeforeDeleteAsync(Shop entity)
        {
            if (await Db.TransportOrders.AnyAsync(o => o.CarrierStructureId == entity.Id && o.NextActionAt != null))
                throw new InvalidOperationException("The shop's worker is carrying goods; delete it once the collection is done or cancelled.");
        }
    }
}
