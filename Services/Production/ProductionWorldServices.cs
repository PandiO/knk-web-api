using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Stock;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Production
{
    public interface IWarehouseService
    {
        Task<IEnumerable<WarehouseDto>> GetAllAsync();
        Task<WarehouseDto?> GetByIdAsync(int id);
        Task<WarehouseDto> CreateAsync(WarehouseDto dto);
        /// <summary>Returns the saved structure with the placement rules' warnings, as create does (#831).</summary>
        Task<WarehouseDto> UpdateAsync(int id, WarehouseDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<ProductionWorldListDto>> SearchAsync(PagedQueryDto query);
    }

    public interface IProductionStructureService
    {
        Task<IEnumerable<ProductionStructureDto>> GetAllAsync();
        Task<ProductionStructureDto?> GetByIdAsync(int id);
        Task<ProductionStructureDto> CreateAsync(ProductionStructureDto dto);
        /// <summary>Returns the saved structure with the placement rules' warnings, as create does (#831).</summary>
        Task<ProductionStructureDto> UpdateAsync(int id, ProductionStructureDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<ProductionWorldListDto>> SearchAsync(PagedQueryDto query);
        /// <summary>The game server's road measurement (DESIGN § 7.6); not gated by <c>Production:Enabled</c>.</summary>
        Task<ProductionStructureDto> SetRoadConnectionAsync(int id, RoadConnectionUpdateDto dto);
    }

    /// <summary>
    /// CRUD shared by the P2 structure subtypes (PandiO/knk-workspace#91). Create saves the structure and its default
    /// storages (L4 note: <see cref="IStorageService.CreateAsync"/> with <c>DomainId</c> = the structure) in one
    /// transaction; delete removes the structure's storages first and answers 409 while one holds stock or ledger
    /// history. Admin CRUD works while <c>Production:Enabled</c> is off (DESIGN § 18.2): it starts no background work.
    /// </summary>
    public abstract class StructureSubtypeService<TEntity, TDto>
        where TEntity : Structure, new()
        where TDto : StructureDto
    {
        protected readonly KnKDbContext Db;
        protected readonly StructureBaseWriter Writer;
        protected readonly IStorageService Storages;
        protected readonly IMapper Mapper;

        protected StructureSubtypeService(KnKDbContext db, StructureBaseWriter writer, IStorageService storages, IMapper mapper)
        {
            Db = db;
            Writer = writer;
            Storages = storages;
            Mapper = mapper;
        }

        protected abstract string TypeName { get; }
        protected abstract DbSet<TEntity> Set { get; }
        protected virtual IQueryable<TEntity> WithDetails(IQueryable<TEntity> q) => q;

        /// <summary>Checks and applies the subtype's own fields (after the Structure part).</summary>
        protected abstract Task ApplyOwnAsync(TEntity target, TDto dto, bool isCreate);

        /// <summary>The storages a new structure gets (Kind Domain; the holder is set by the caller).</summary>
        protected abstract IEnumerable<StorageDto> DefaultStorages(TEntity created);

        /// <summary>After the default storages exist (e.g. remember the default delivery storage).</summary>
        protected virtual void OnStoragesCreated(TEntity created, IReadOnlyList<StorageDto> storages) { }

        protected virtual void Fill(TEntity entity, TDto dto) { }

        private IQueryable<TEntity> Query() => WithDetails(Set
            .Include(s => s.Location).Include(s => s.Street).Include(s => s.Town)
            .Include(s => s.District).ThenInclude(d => d!.Town)
            .Include(s => s.SubRegions));

        public async Task<IEnumerable<TDto>> GetAllAsync()
        {
            var rows = await Query().AsNoTracking().OrderBy(s => s.Id).ToListAsync();
            return rows.Select(r => Mapper.Map<TDto>(r)).ToList();
        }

        public async Task<TDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var entity = await Query().AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
            if (entity == null) return null;
            var dto = Mapper.Map<TDto>(entity);
            Fill(entity, dto);
            await FillStoragesAsync(id, dto);
            return dto;
        }

        protected virtual Task FillStoragesAsync(int id, TDto dto) => Task.CompletedTask;

        protected Task<List<StructureStorageNavDto>> StoragesOfAsync(int id) =>
            Db.Storages.AsNoTracking().Where(s => s.Kind == StorageKind.Domain && s.DomainId == id).OrderBy(s => s.Id)
                .Select(s => new StructureStorageNavDto { Id = s.Id, Name = s.Name, Purpose = s.Purpose.ToString() })
                .ToListAsync();

        public async Task<TDto> CreateAsync(TDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var entity = new TEntity();
            await Writer.ApplyAsync(entity, dto, TypeName, isCreate: true);
            await ApplyOwnAsync(entity, dto, isCreate: true);
            var warnings = await Writer.Placement.CheckRulesAsync(TypeName, entity);

            await using (var tx = await BeginAsync())
            {
                Set.Add(entity);
                await Db.SaveChangesAsync();
                var created = new List<StorageDto>();
                foreach (var storage in DefaultStorages(entity))
                {
                    storage.Kind = StorageKind.Domain;
                    storage.DomainId = entity.Id;
                    created.Add(await Storages.CreateAsync(storage));
                }
                OnStoragesCreated(entity, created);
                await Db.SaveChangesAsync();
                if (tx != null) await tx.CommitAsync();
            }

            await Writer.FinalizeRegionAsync(entity);
            var result = (await GetByIdAsync(entity.Id))!;
            result.PlacementWarnings = warnings;
            return result;
        }

        public async Task<TDto> UpdateAsync(int id, TDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            await BeforeUpdateAsync(id, dto);
            var existing = await Set.Include(s => s.District).Include(s => s.Town).FirstOrDefaultAsync(s => s.Id == id)
                ?? throw new KeyNotFoundException($"{TypeName} with id {id} not found.");
            string previousRegionId = existing.WgRegionId;
            await Writer.ApplyAsync(existing, dto, TypeName, isCreate: false);
            await ApplyOwnAsync(existing, dto, isCreate: false);
            var warnings = await Writer.Placement.CheckRulesAsync(TypeName, existing);
            await Db.SaveChangesAsync();
            await Writer.FinalizeRegionAsync(existing, previousRegionId);
            await AfterUpdateAsync(id, dto);
            // The placement warnings go back to the caller, as on create (PandiO/knk-workspace#831).
            Db.ChangeTracker.Clear();
            var result = (await GetByIdAsync(id))!;
            result.PlacementWarnings = warnings;
            return result;
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await Set.FirstOrDefaultAsync(s => s.Id == id)
                ?? throw new KeyNotFoundException($"{TypeName} with id {id} not found.");
            await using var tx = await BeginAsync();
            await BeforeDeleteAsync(existing);
            var storageIds = await Db.Storages.Where(s => s.Kind == StorageKind.Domain && s.DomainId == id)
                .Select(s => s.Id).ToListAsync();
            foreach (var storageId in storageIds)
                await Storages.DeleteAsync(storageId); // 409 while it holds stock or has ledger history
            Set.Remove(existing);
            await Db.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();
        }

        protected virtual Task BeforeDeleteAsync(TEntity entity) => Task.CompletedTask;

        /// <summary>Before an update loads the entity (P3: settle production at the old values).</summary>
        protected virtual Task BeforeUpdateAsync(int id, TDto dto) => Task.CompletedTask;

        /// <summary>After an update is saved (P3: a structure switched back on resumes its lines from now).</summary>
        protected virtual Task AfterUpdateAsync(int id, TDto dto) => Task.CompletedTask;

        public async Task<PagedResultDto<ProductionWorldListDto>> SearchAsync(PagedQueryDto query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            IQueryable<TEntity> q = Set.AsNoTracking()
                .Include(s => s.Street).Include(s => s.Town).Include(s => s.District).ThenInclude(d => d!.Town);
            if (TerritoryPaging.Term(query) is { } term)
                q = q.Where(s => s.Name.ToLower().Contains(term) || s.Description.ToLower().Contains(term));
            if (TerritoryPaging.IntFilter(query, "districtId") is { } districtId) q = q.Where(s => s.DistrictId == districtId);
            if (TerritoryPaging.IntFilter(query, "townId") is { } townId)
                q = q.Where(s => s.TownId == townId || (s.District != null && s.District.TownId == townId));
            if (query.Filters != null && query.Filters.TryGetValue("isRural", out var r) && bool.TryParse(r, out var rural))
                q = rural ? q.Where(s => s.DistrictId == null) : q.Where(s => s.DistrictId != null);
            if (query.Filters != null && query.Filters.TryGetValue("territoryFlag", out var f)
                && Enum.TryParse<StructureTerritoryFlag>(f, true, out var flag))
                q = q.Where(s => s.TerritoryFlag == flag);
            q = Filter(q, query);
            q = (query.SortBy ?? "name").ToLowerInvariant() switch
            {
                "id" => query.SortDescending ? q.OrderByDescending(s => s.Id) : q.OrderBy(s => s.Id),
                "createdat" => query.SortDescending ? q.OrderByDescending(s => s.CreatedAt) : q.OrderBy(s => s.CreatedAt),
                _ => query.SortDescending ? q.OrderByDescending(s => s.Name) : q.OrderBy(s => s.Name),
            };
            var page = await TerritoryPaging.PageAsync(q, query);
            return new PagedResultDto<ProductionWorldListDto>
            {
                Items = page.Items.Select(e => Mapper.Map<ProductionWorldListDto>(e)).ToList(),
                TotalCount = page.TotalCount,
                PageNumber = page.PageNumber,
                PageSize = page.PageSize,
            };
        }

        protected virtual IQueryable<TEntity> Filter(IQueryable<TEntity> q, PagedQueryDto query) => q;

        private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginAsync() =>
            Db.Database.IsRelational() && Db.Database.CurrentTransaction == null
                ? await Db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted)
                : null;

        protected static string StorageName(string structureName, string suffix)
        {
            var name = $"{structureName} - {suffix}";
            return name.Length <= Storage.NameMaxLength ? name : name[..Storage.NameMaxLength];
        }
    }

    /// <summary>Warehouses (DESIGN § 3.3): a default delivery storage on create.</summary>
    public sealed class WarehouseService : StructureSubtypeService<Warehouse, WarehouseDto>, IWarehouseService
    {
        public WarehouseService(KnKDbContext db, StructureBaseWriter writer, IStorageService storages, IMapper mapper)
            : base(db, writer, storages, mapper) { }

        protected override string TypeName => nameof(Warehouse);
        protected override DbSet<Warehouse> Set => Db.Warehouses;

        protected override async Task ApplyOwnAsync(Warehouse target, WarehouseDto dto, bool isCreate)
        {
            if (dto.AcceptsDeliveries.HasValue) target.AcceptsDeliveries = dto.AcceptsDeliveries.Value;
            if (dto.Priority.HasValue)
            {
                if (dto.Priority.Value is < 0 or > Warehouse.MaxPriority)
                    throw new ArgumentException($"priority must be 0 to {Warehouse.MaxPriority}.", nameof(dto));
                target.Priority = dto.Priority.Value;
            }
            if (!isCreate && dto.DefaultStorageId.HasValue && dto.DefaultStorageId != target.DefaultStorageId)
            {
                bool held = await Db.Storages.AnyAsync(s => s.Id == dto.DefaultStorageId.Value
                    && s.Kind == StorageKind.Domain && s.DomainId == target.Id);
                if (!held) throw new ArgumentException("defaultStorageId must be a storage this warehouse holds.", nameof(dto));
                target.DefaultStorageId = dto.DefaultStorageId;
            }
        }

        protected override IEnumerable<StorageDto> DefaultStorages(Warehouse created)
        {
            yield return new StorageDto { Name = StorageName(created.Name, "deliveries"), Purpose = StoragePurpose.Delivery };
        }

        protected override void OnStoragesCreated(Warehouse created, IReadOnlyList<StorageDto> storages) =>
            created.DefaultStorageId = storages.FirstOrDefault()?.Id;

        protected override async Task FillStoragesAsync(int id, WarehouseDto dto) => dto.Storages = await StoragesOfAsync(id);

        protected override IQueryable<Warehouse> Filter(IQueryable<Warehouse> q, PagedQueryDto query)
        {
            if (query.Filters != null && query.Filters.TryGetValue("acceptsDeliveries", out var a) && bool.TryParse(a, out var accepts))
                q = q.Where(w => w.AcceptsDeliveries == accepts);
            return q;
        }

        protected override async Task BeforeDeleteAsync(Warehouse entity)
        {
            // Production structures that prefer it fall back to the nearest warehouse (FK SetNull; the preference too).
            var preferring = await Db.ProductionStructures.Where(p => p.PreferredWarehouseId == entity.Id).ToListAsync();
            foreach (var p in preferring)
            {
                p.PreferredWarehouseId = null;
                p.WarehousePreference = WarehousePreference.Nearest;
            }
            // The default storage FK is SetNull; clear it first so the storage can be deleted in the same save.
            entity.DefaultStorageId = null;
            await Db.SaveChangesAsync();
        }
    }

    /// <summary>Production structures (DESIGN § 3.3): an Output storage on create plus the kind's default storages
    /// (P3); the kind's default workers and the level's caps (PRODUCTION_KINDS § 4.5, § 5).</summary>
    public sealed class ProductionStructureService : StructureSubtypeService<ProductionStructure, ProductionStructureDto>, IProductionStructureService
    {
        private readonly IProductionEngine? _engine;
        private readonly IProductionYieldService? _yield;
        private ProductionKind? _createKind;
        private ProductionKindLevel? _createLevel;
        private ProductionRateSource? _previousYieldSource;

        public ProductionStructureService(KnKDbContext db, StructureBaseWriter writer, IStorageService storages, IMapper mapper,
            IProductionEngine? engine = null, IProductionYieldService? yield = null)
            : base(db, writer, storages, mapper)
        {
            _engine = engine;
            _yield = yield;
        }

        protected override async Task BeforeUpdateAsync(int id, ProductionStructureDto dto)
        {
            // A level, kind, worker, switch or yield source change applies from now: settle the time before at the old values.
            if (_engine != null && (dto.Level.HasValue || dto.ProductionKindId.HasValue || dto.WorkersTotal.HasValue
                    || dto.ProductionEnabled.HasValue || dto.YieldSource != null))
                await _engine.SettleStructureAsync(id);
            _previousYieldSource = await Db.ProductionStructures.AsNoTracking().Where(p => p.Id == id)
                .Select(p => (ProductionRateSource?)p.YieldSource).FirstOrDefaultAsync();
        }

        protected override async Task AfterUpdateAsync(int id, ProductionStructureDto dto)
        {
            // Lines paused by the structure's switch resume from the moment it is switched on, not at the next sweep.
            if (_engine != null && dto.ProductionEnabled == true) await _engine.SettleStructureAsync(id);
            // P4 (#138): a new yield source (or a new level, which can unlock yield rows) re-materializes the lines.
            var now = await Db.ProductionStructures.AsNoTracking().Where(p => p.Id == id).Select(p => p.YieldSource).FirstAsync();
            if (_yield != null && (now != _previousYieldSource
                    || (now != ProductionRateSource.Configured && (dto.Level.HasValue || dto.ProductionKindId.HasValue))))
            {
                Db.ChangeTracker.Clear();
                await _yield.MaterializeAsync(id);
            }
        }

        protected override string TypeName => nameof(ProductionStructure);
        protected override DbSet<ProductionStructure> Set => Db.ProductionStructures;

        protected override IQueryable<ProductionStructure> WithDetails(IQueryable<ProductionStructure> q) =>
            q.Include(p => p.PreferredWarehouse).Include(p => p.ProductionKind);

        protected override async Task ApplyOwnAsync(ProductionStructure target, ProductionStructureDto dto, bool isCreate)
        {
            int? previousKind = target.ProductionKindId;
            int previousLevel = target.Level;
            ProductionKind? kind = null;
            if (dto.ProductionKindId.HasValue)
            {
                if (dto.ProductionKindId.Value > 0)
                {
                    kind = await Db.ProductionKinds.AsNoTracking().FirstOrDefaultAsync(k => k.Id == dto.ProductionKindId.Value)
                        ?? throw new ArgumentException($"ProductionKind with id {dto.ProductionKindId} not found.", nameof(dto));
                    target.ProductionKindId = kind.Id;
                }
                else target.ProductionKindId = null;
            }
            await ApplyYieldSourceAsync(target, dto, kind, isCreate);
            if (dto.Level.HasValue)
            {
                if (dto.Level.Value is < ProductionStructure.MinLevel or > ProductionStructure.MaxLevel)
                    throw new ArgumentException($"level must be {ProductionStructure.MinLevel} to {ProductionStructure.MaxLevel}.", nameof(dto));
                target.Level = dto.Level.Value;
            }
            if (isCreate && kind != null && !dto.WorkersTotal.HasValue)
            {
                target.WorkersTotal = kind.DefaultWorkers;
                target.WorkersPresent = kind.DefaultWorkers;
            }
            if (isCreate)
            {
                _createKind = kind;
                _createLevel = await ProductionLevels.FindAsync(Db, target.ProductionKindId, target.Level);
                if (_createLevel?.TransportCapacity is { } cap && !dto.TransportCapacity.HasValue) target.TransportCapacity = cap;
            }
            else if (target.ProductionKindId != previousKind || target.Level != previousLevel)
                await ProductionLevels.ApplyAsync(Db, target, await ProductionLevels.FindAsync(Db, target.ProductionKindId, target.Level));
            if (dto.ProductionEnabled.HasValue) target.ProductionEnabled = dto.ProductionEnabled.Value;
            if (dto.AutomaticTransport.HasValue) target.AutomaticTransport = dto.AutomaticTransport.Value;
            if (dto.TransportThreshold.HasValue)
            {
                if (dto.TransportThreshold.Value < 0) throw new ArgumentException("transportThreshold can't be negative.", nameof(dto));
                target.TransportThreshold = dto.TransportThreshold.Value > 0 ? dto.TransportThreshold.Value : null;
            }
            if (dto.TransportCapacity.HasValue)
            {
                if (dto.TransportCapacity.Value < 0) throw new ArgumentException("transportCapacity can't be negative.", nameof(dto));
                target.TransportCapacity = dto.TransportCapacity.Value > 0 ? dto.TransportCapacity.Value : null;
            }
            if (dto.WorkersTotal.HasValue)
            {
                if (dto.WorkersTotal.Value is < 0 or > ProductionStructure.MaxWorkers)
                    throw new ArgumentException($"workersTotal must be 0 to {ProductionStructure.MaxWorkers}.", nameof(dto));
                target.WorkersTotal = dto.WorkersTotal.Value;
                if (isCreate || target.WorkersPresent > target.WorkersTotal) target.WorkersPresent = target.WorkersTotal;
            }
            if (dto.PreferredWarehouseId.HasValue)
            {
                if (dto.PreferredWarehouseId.Value > 0)
                {
                    if (!await Db.Warehouses.AnyAsync(w => w.Id == dto.PreferredWarehouseId.Value))
                        throw new ArgumentException($"Warehouse with id {dto.PreferredWarehouseId} not found.", nameof(dto));
                    target.PreferredWarehouseId = dto.PreferredWarehouseId.Value;
                }
                else target.PreferredWarehouseId = null;
            }
            if (dto.WarehousePreference != null)
            {
                if (!Enum.TryParse<WarehousePreference>(dto.WarehousePreference.Trim(), true, out var pref) || !Enum.IsDefined(pref))
                    throw new ArgumentException("warehousePreference must be Nearest or Preferred.", nameof(dto));
                target.WarehousePreference = pref;
            }
            if (target.WarehousePreference == WarehousePreference.Preferred && target.PreferredWarehouseId == null)
                throw new ArgumentException("warehousePreference Preferred needs a preferredWarehouseId.", nameof(dto));
        }

        /// <summary>P4 (#138, PRODUCTION_KINDS § 4.1): Derived, Configured or Hybrid (the default, P-D1), one the kind allows.
        /// Without a choice on create, the first the kind allows of Hybrid, Derived, Configured.</summary>
        private async Task ApplyYieldSourceAsync(ProductionStructure target, ProductionStructureDto dto, ProductionKind? kind, bool isCreate)
        {
            kind ??= target.ProductionKindId == null ? null
                : await Db.ProductionKinds.AsNoTracking().FirstOrDefaultAsync(k => k.Id == target.ProductionKindId);
            static bool Allows(ProductionKind k, ProductionRateSource s) => s switch
            {
                ProductionRateSource.Derived => k.AllowsDerived,
                ProductionRateSource.Configured => k.AllowsConfigured,
                _ => k.AllowsHybrid,
            };
            if (dto.YieldSource != null)
            {
                if (!Enum.TryParse<ProductionRateSource>(dto.YieldSource.Trim(), true, out var source) || !Enum.IsDefined(source)
                    || int.TryParse(dto.YieldSource.Trim(), out _))
                    throw new ArgumentException("yieldSource must be Derived, Configured or Hybrid.");
                target.YieldSource = source;
            }
            else if (isCreate && kind != null && !Allows(kind, target.YieldSource))
                target.YieldSource = new[] { ProductionRateSource.Hybrid, ProductionRateSource.Derived, ProductionRateSource.Configured }
                    .First(s => Allows(kind, s));
            if (kind != null && !Allows(kind, target.YieldSource))
                throw new ArgumentException($"yieldSource: {kind.Name} doesn't allow {target.YieldSource}.");
        }

        protected override IEnumerable<StorageDto> DefaultStorages(ProductionStructure created)
        {
            yield return new StorageDto
            {
                Name = StorageName(created.Name, "output"), Purpose = StoragePurpose.Output,
                MaxUnits = _createLevel?.OutputMaxUnits, UnitsPerSlot = _createLevel?.UnitsPerSlot,
            };
            // The kind's own storages (PRODUCTION_KINDS § 2 DefaultStorages), next to the Output storage (L6 note).
            foreach (var spec in ProductionKindService.ParseStorages(_createKind?.DefaultStoragesJson))
                yield return new StorageDto
                {
                    Name = StorageName(created.Name, spec.Name!),
                    Purpose = Enum.Parse<StoragePurpose>(spec.Purpose!),
                    MaxUnits = spec.MaxUnits is > 0 ? spec.MaxUnits : null,
                };
        }

        protected override void Fill(ProductionStructure entity, ProductionStructureDto dto) => dto.Warnings = Warnings(entity);

        protected override async Task FillStoragesAsync(int id, ProductionStructureDto dto)
        {
            dto.Storages = await StoragesOfAsync(id);
            dto.Areas = await Db.ProductionAreas.AsNoTracking().Where(a => a.ProductionStructureId == id).OrderBy(a => a.Id)
                .Select(a => new ProductionAreaListDto
                {
                    Id = a.Id, Name = a.Name, ProductionStructureId = a.ProductionStructureId, ProductionStructureName = dto.Name ?? string.Empty,
                    Model = a.Model.ToString(), Shape = a.Shape.ToString(), Exclusive = a.Exclusive, Size = a.Size, Enabled = a.Enabled,
                    LastSurveyedAt = a.LastSurveyedAt,
                }).ToListAsync();
        }

        /// <summary>The dashboard warnings of DESIGN § 3.4 (R3) and § 7.6 (D5). A full output storage and blocked lines are
        /// in the production view (<c>GET api/ProductionStructures/{id}/production</c>, P3).</summary>
        public static List<string> Warnings(ProductionStructure p)
        {
            var list = new List<string>();
            if (p.DistrictId == null && p.RoadConnection == RoadConnectionState.Unconnected)
                list.Add("Not connected to its town's road network: automatic transport is held.");
            if (p.TerritoryFlag == StructureTerritoryFlag.Contested)
                list.Add("Its land is contested: it keeps its town and keeps producing.");
            if (p.TerritoryFlag == StructureTerritoryFlag.OutsideTerritory)
                list.Add("Outside its town's territory: it keeps its town and keeps producing.");
            return list;
        }

        protected override IQueryable<ProductionStructure> Filter(IQueryable<ProductionStructure> q, PagedQueryDto query)
        {
            if (TerritoryPaging.IntFilter(query, "productionKindId") is { } kindId) q = q.Where(p => p.ProductionKindId == kindId);
            if (query.Filters != null && query.Filters.TryGetValue("roadConnection", out var rc)
                && Enum.TryParse<RoadConnectionState>(rc, true, out var state))
                q = q.Where(p => p.RoadConnection == state);
            return q;
        }

        public async Task<ProductionStructureDto> SetRoadConnectionAsync(int id, RoadConnectionUpdateDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (!Enum.TryParse<RoadConnectionState>(dto.RoadConnection?.Trim(), true, out var state) || !Enum.IsDefined(state))
                throw new ArgumentException("roadConnection must be Unmeasured, Connected or Unconnected.", nameof(dto));
            var p = await Db.ProductionStructures.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new KeyNotFoundException($"ProductionStructure with id {id} not found.");
            p.RoadConnection = state;
            p.RoadConnectionMeasuredAt = state == RoadConnectionState.Unmeasured ? null : DateTime.UtcNow;
            await Db.SaveChangesAsync();
            return (await GetByIdAsync(id))!;
        }
    }
}
