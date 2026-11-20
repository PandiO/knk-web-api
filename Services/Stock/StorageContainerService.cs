using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Production;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Stock
{
    /// <summary>Thrown when the caller may not see or use a storage (403).</summary>
    public sealed class StorageAccessDeniedException : Exception
    {
        public StorageAccessDeniedException(string message) : base(message) { }
    }

    /// <summary>Thrown when <c>Production:Enabled</c> is off for an in-game action (409, A21).</summary>
    public sealed class ProductionDisabledException : InvalidOperationException
    {
        public ProductionDisabledException(string message) : base(message) { }
    }

    public interface IStorageContainerService
    {
        Task<StorageContainerDto?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<StorageContainerDto?> GetByBlockAsync(string world, int x, int y, int z, CancellationToken ct = default);
        Task<PagedResultDto<StorageContainerListDto>> SearchAsync(PagedQueryDto query, CancellationToken ct = default);
        Task<StorageContainerDto> CreateAsync(StorageContainerDto dto, int? actorUserId, CancellationToken ct = default);
        Task<StorageContainerDto> UpdateAsync(int id, StorageContainerDto dto, CancellationToken ct = default);
        Task DeleteAsync(int id, CancellationToken ct = default);

        /// <summary>One page of a Terminal container's storage, for a viewer who may see it.</summary>
        Task<TerminalViewDto> TerminalViewAsync(int id, ProductionViewer viewer, int page, int pageSize, CancellationToken ct = default);

        /// <summary>A put (<c>SYS_WORLD</c> → storage, <see cref="StockReason.Deposit"/>) or take (storage →
        /// <c>SYS_WORLD</c>, <see cref="StockReason.Withdraw"/>) through a Terminal container.</summary>
        Task<StockResult> TerminalMoveAsync(int id, bool put, TerminalMoveDto dto, ProductionViewer viewer, CancellationToken ct = default);

        /// <summary>What a Display container shows.</summary>
        Task<DisplaySampleDto> DisplaySampleAsync(int id, CancellationToken ct = default);
    }

    /// <summary>
    /// Containers bound to a storage (P6a, PandiO/knk-workspace#683, production DESIGN § 6). Admin CRUD works while
    /// <c>Production:Enabled</c> is off (DESIGN § 18.2); terminal take and put are in-game actions and need it on (A21).
    /// Binding, moving or unbinding a container rewrites the storage's container-derived cap
    /// (<see cref="StorageContainerCapacity"/>).
    /// </summary>
    public sealed class StorageContainerService : IStorageContainerService
    {
        public const int MaxPageSize = 54;
        public const int MaxEntries = 2000;
        public const int MinY = -64;
        public const int MaxY = 320;

        private readonly KnKDbContext _db;
        private readonly IStockService _stock;
        private readonly IProductionSettingsReader _settings;
        private readonly IProductionViewerResolver _viewers;
        private readonly TimeProvider _clock;

        public StorageContainerService(KnKDbContext db, IStockService stock, IProductionSettingsReader settings,
            IProductionViewerResolver viewers, TimeProvider? clock = null)
        {
            _db = db;
            _stock = stock;
            _settings = settings;
            _viewers = viewers;
            _clock = clock ?? TimeProvider.System;
        }

        private DateTime Now => _clock.GetUtcNow().UtcDateTime;

        // ================= CRUD =================

        public async Task<StorageContainerDto?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            var c = await _db.StorageContainers.AsNoTracking().Include(x => x.Storage).FirstOrDefaultAsync(x => x.Id == id, ct);
            return c == null ? null : await ToDtoAsync(c, ct);
        }

        public async Task<StorageContainerDto?> GetByBlockAsync(string world, int x, int y, int z, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(world)) return null;
            var w = world.Trim();
            var c = await _db.StorageContainers.AsNoTracking().Include(s => s.Storage)
                .FirstOrDefaultAsync(s => s.WorldName == w && s.X == x && s.Y == y && s.Z == z, ct);
            return c == null ? null : await ToDtoAsync(c, ct);
        }

        public async Task<PagedResultDto<StorageContainerListDto>> SearchAsync(PagedQueryDto query, CancellationToken ct = default)
        {
            query ??= new PagedQueryDto();
            var q = _db.StorageContainers.AsNoTracking();
            if (TerritoryPaging.Term(query) is { } term)
                q = q.Where(c => (c.Label != null && c.Label.ToLower().Contains(term)) || c.Storage.Name.ToLower().Contains(term)
                    || c.WorldName.ToLower().Contains(term));
            if (TerritoryPaging.IntFilter(query, "storageId") is { } storageId) q = q.Where(c => c.StorageId == storageId);
            if (Filter(query, "mode") is { } m && Enum.TryParse<StorageContainerMode>(m, true, out var mode)) q = q.Where(c => c.Mode == mode);
            if (Filter(query, "worldName") is { } world) q = q.Where(c => c.WorldName == world);
            q = (query.SortBy?.ToLowerInvariant()) switch
            {
                "label" => query.SortDescending ? q.OrderByDescending(c => c.Label) : q.OrderBy(c => c.Label),
                "storage" => query.SortDescending ? q.OrderByDescending(c => c.Storage.Name) : q.OrderBy(c => c.Storage.Name),
                _ => query.SortDescending ? q.OrderByDescending(c => c.Id) : q.OrderBy(c => c.Id),
            };
            return await TerritoryPaging.PageAsync(q.Select(c => new StorageContainerListDto
            {
                Id = c.Id,
                StorageId = c.StorageId,
                StorageName = c.Storage.Name,
                Mode = c.Mode,
                ContainerType = c.ContainerType,
                IsDouble = c.IsDouble,
                Slots = c.Slots,
                WorldName = c.WorldName,
                X = c.X,
                Y = c.Y,
                Z = c.Z,
                Label = c.Label,
            }), query);
        }

        public async Task<StorageContainerDto> CreateAsync(StorageContainerDto dto, int? actorUserId, CancellationToken ct = default)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var storageId = dto.StorageId ?? throw new ArgumentException("storageId is required.");
            var c = new StorageContainer { CreatedAt = Now, CreatedByUserId = actorUserId };
            Apply(c, dto, isCreate: true);
            await using var tx = await BeginAsync(ct);
            await StockLocks.LockStoragesAsync(_db, new[] { storageId }, ct);
            var storage = await LoadBindableStorageAsync(storageId, ct);
            c.StorageId = storage.Id;
            await EnsureBlockFreeAsync(c, null, ct);
            _db.StorageContainers.Add(c);
            await _db.SaveChangesAsync(ct);
            await StorageContainerCapacity.ApplyAsync(_db, storage, ct);
            await _db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            _db.ChangeTracker.Clear();
            return (await GetByIdAsync(c.Id, ct))!;
        }

        public async Task<StorageContainerDto> UpdateAsync(int id, StorageContainerDto dto, CancellationToken ct = default)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var current = await _db.StorageContainers.AsNoTracking().Where(x => x.Id == id).Select(x => (int?)x.StorageId).FirstOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException($"StorageContainer with id {id} not found.");
            var target = dto.StorageId ?? current;
            await using var tx = await BeginAsync(ct);
            await StockLocks.LockStoragesAsync(_db, new[] { current, target }, ct);
            var c = await _db.StorageContainers.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new KeyNotFoundException($"StorageContainer with id {id} not found.");
            Apply(c, dto, isCreate: false);
            Storage? old = null;
            if (target != c.StorageId)
            {
                old = await _db.Storages.FirstAsync(s => s.Id == c.StorageId, ct);
                c.StorageId = (await LoadBindableStorageAsync(target, ct)).Id;
            }
            await EnsureBlockFreeAsync(c, c.Id, ct);
            c.UpdatedAt = Now;
            await _db.SaveChangesAsync(ct);
            var storage = await _db.Storages.FirstAsync(s => s.Id == c.StorageId, ct);
            await StorageContainerCapacity.ApplyAsync(_db, storage, ct);
            if (old != null) await StorageContainerCapacity.ApplyAsync(_db, old, ct);
            await _db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            _db.ChangeTracker.Clear();
            return (await GetByIdAsync(id, ct))!;
        }

        public async Task DeleteAsync(int id, CancellationToken ct = default)
        {
            var storageId = await _db.StorageContainers.AsNoTracking().Where(x => x.Id == id).Select(x => (int?)x.StorageId).FirstOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException($"StorageContainer with id {id} not found.");
            await using var tx = await BeginAsync(ct);
            await StockLocks.LockStoragesAsync(_db, new[] { storageId }, ct);
            var c = await _db.StorageContainers.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new KeyNotFoundException($"StorageContainer with id {id} not found.");
            _db.StorageContainers.Remove(c);
            await _db.SaveChangesAsync(ct);
            var storage = await _db.Storages.FirstAsync(s => s.Id == storageId, ct);
            await StorageContainerCapacity.ApplyAsync(_db, storage, ct);
            await _db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            _db.ChangeTracker.Clear();
        }

        // ================= Terminal and display =================

        public async Task<TerminalViewDto> TerminalViewAsync(int id, ProductionViewer viewer, int page, int pageSize, CancellationToken ct = default)
        {
            var c = await LoadForUseAsync(id, StorageContainerMode.Terminal, viewer, ct);
            int size = Math.Clamp(pageSize <= 0 ? MaxPageSize : pageSize, 1, MaxPageSize);
            int number = Math.Max(1, page);
            var entries = await _db.StockLines.AsNoTracking().Where(l => l.StorageId == c.StorageId)
                .OrderBy(l => l.ItemBlueprint.Name).ThenBy(l => l.ItemBlueprintId).Take(MaxEntries)
                .Select(l => new TerminalEntryDto { ItemBlueprintId = l.ItemBlueprintId, ItemBlueprintName = l.ItemBlueprint.Name, Amount = l.Amount })
                .ToListAsync(ct);
            entries.AddRange(await _db.StockUnits.AsNoTracking().Where(u => u.StorageId == c.StorageId)
                .OrderBy(u => u.ItemInstanceId).Take(MaxEntries)
                .Select(u => new TerminalEntryDto
                {
                    ItemBlueprintId = u.ItemInstance.ItemBlueprintId,
                    ItemBlueprintName = u.ItemInstance.ItemBlueprint.Name,
                    Amount = 1,
                    ItemInstanceId = u.ItemInstanceId,
                    CustomDisplayName = u.ItemInstance.CustomDisplayName,
                }).ToListAsync(ct));
            return new TerminalViewDto
            {
                ContainerId = c.Id,
                StorageId = c.StorageId,
                StorageName = c.Storage.Name,
                TotalUnits = c.Storage.TotalUnits,
                CapacityUsed = await UsedAsync(c.StorageId, ct),
                MaxUnits = c.Storage.MaxUnits,
                IsLocked = c.Storage.IsLockedAt(Now),
                Page = number,
                PageSize = size,
                TotalCount = entries.Count,
                Entries = entries.Skip((number - 1) * size).Take(size).ToList(),
            };
        }

        public async Task<StockResult> TerminalMoveAsync(int id, bool put, TerminalMoveDto dto, ProductionViewer viewer, CancellationToken ct = default)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (!(await _settings.GetAsync()).Enabled)
                throw new ProductionDisabledException("Production is switched off (Production:Enabled); terminals don't move stock.");
            var key = dto.IdempotencyKey?.Trim();
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("idempotencyKey is required (one per click, so a retry applies once).");
            if (key.Length > 100) throw new ArgumentException("idempotencyKey is at most 100 characters.");
            if (dto.ItemInstanceId.HasValue == dto.ItemBlueprintId.HasValue)
                throw new ArgumentException("Send exactly one of itemBlueprintId (an amount) and itemInstanceId (a unique item).");
            if (dto.ItemBlueprintId.HasValue && dto.Amount <= 0) throw new ArgumentException("amount must be at least 1.");
            var c = await LoadForUseAsync(id, StorageContainerMode.Terminal, viewer, ct);
            var world = await _stock.GetSystemStorageIdAsync(Storage.SysWorld, ct);
            var (from, to) = put ? (world, c.StorageId) : (c.StorageId, world);
            var line = dto.ItemInstanceId is { } instance
                ? StockLineRequest.Unique(from, to, instance)
                : StockLineRequest.Fungible(from, to, dto.ItemBlueprintId!.Value, dto.Amount);
            var ctx = new StockContext
            {
                Reason = put ? StockReason.Deposit : StockReason.Withdraw,
                ActorKind = StockActorKind.PluginServer,
                ActorUserId = viewer.UserId,
                ActorComponent = "terminal",
                SourceType = "StorageContainer",
                SourceRef = c.Id.ToString(),
                IdempotencyKey = "terminal:" + key,
            };
            return await _stock.MoveAsync(line, ctx, dto.Mode ?? StockMoveMode.Partial, ct);
        }

        public async Task<DisplaySampleDto> DisplaySampleAsync(int id, CancellationToken ct = default)
        {
            var c = await _db.StorageContainers.AsNoTracking().Include(x => x.Storage).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new KeyNotFoundException($"StorageContainer with id {id} not found.");
            if (c.Mode != StorageContainerMode.Display) throw new InvalidOperationException("This container is not a display.");
            var used = await UsedAsync(c.StorageId, ct);
            decimal fill = c.Storage.MaxUnits is > 0 ? Math.Min(1m, used / c.Storage.MaxUnits.Value) : used > 0 ? 1m : 0m;
            int filled = used > 0 ? Math.Max(1, (int)Math.Round(fill * c.Slots, MidpointRounding.AwayFromZero)) : 0;
            var lines = await _db.StockLines.AsNoTracking().Where(l => l.StorageId == c.StorageId && l.Amount > 0)
                .OrderByDescending(l => l.Amount).ThenBy(l => l.ItemBlueprintId).Take(c.Slots)
                .Select(l => new { l.ItemBlueprintId, l.ItemBlueprint.Name, l.Amount }).ToListAsync(ct);
            var sample = new DisplaySampleDto { ContainerId = c.Id, StorageId = c.StorageId, Slots = c.Slots, FillRatio = Math.Round(fill, 4) };
            if (filled == 0 || lines.Count == 0) return sample;
            // Largest remainder over the filled slots, every shown blueprint at least one stack.
            long total = lines.Sum(l => l.Amount);
            var shown = lines.Take(filled).ToList();
            var stacks = shown.Select(l => Math.Max(1, (int)(filled * l.Amount / total))).ToList();
            int i = 0;
            while (stacks.Sum() < filled) { stacks[i % stacks.Count]++; i++; }
            while (stacks.Sum() > filled) { int k = stacks.IndexOf(stacks.Max()); stacks[k]--; }
            for (int k = 0; k < shown.Count; k++)
                if (stacks[k] > 0)
                    sample.Items.Add(new DisplaySampleItemDto { ItemBlueprintId = shown[k].ItemBlueprintId, ItemBlueprintName = shown[k].Name, Stacks = stacks[k] });
            sample.FilledSlots = sample.Items.Sum(x => x.Stacks);
            return sample;
        }

        // ================= Helpers =================

        private async Task<StorageContainer> LoadForUseAsync(int id, StorageContainerMode mode, ProductionViewer viewer, CancellationToken ct)
        {
            var c = await _db.StorageContainers.AsNoTracking().Include(x => x.Storage).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new KeyNotFoundException($"StorageContainer with id {id} not found.");
            if (c.Mode != mode) throw new InvalidOperationException($"This container is a {c.Mode}, not a {mode}.");
            if (!await _viewers.SeesStorageAsync(viewer, c.Storage, ct))
                throw new StorageAccessDeniedException("Only members of the storage's town (or its player) and staff may use this terminal.");
            return c;
        }

        private async Task<Storage> LoadBindableStorageAsync(int storageId, CancellationToken ct)
        {
            var s = await _db.Storages.FirstOrDefaultAsync(x => x.Id == storageId, ct)
                ?? throw new ArgumentException($"Storage {storageId} not found.");
            if (s.Kind is StorageKind.Virtual or StorageKind.Transit)
                throw new ArgumentException("System and transit storages can't have containers.");
            return s;
        }

        private async Task EnsureBlockFreeAsync(StorageContainer c, int? self, CancellationToken ct)
        {
            var taken = await _db.StorageContainers.AsNoTracking()
                .Where(x => x.WorldName == c.WorldName && x.X == c.X && x.Y == c.Y && x.Z == c.Z && x.Id != (self ?? 0))
                .Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
            if (taken != null) throw new InvalidOperationException($"That block is already bound (container {taken}).");
        }

        private static void Apply(StorageContainer c, StorageContainerDto dto, bool isCreate)
        {
            if (dto.Mode.HasValue)
            {
                if (!Enum.IsDefined(dto.Mode.Value)) throw new ArgumentException("Unknown mode.");
                c.Mode = dto.Mode.Value;
            }
            if (dto.ContainerType.HasValue)
            {
                if (!Enum.IsDefined(dto.ContainerType.Value)) throw new ArgumentException("Unknown container type.");
                c.ContainerType = dto.ContainerType.Value;
            }
            if (dto.IsDouble.HasValue) c.IsDouble = dto.IsDouble.Value;
            if (c.IsDouble && c.ContainerType == StorageContainerType.Barrel)
                throw new ArgumentException("A barrel can't be double; only chests and trapped chests are.");
            c.Slots = StorageContainer.SlotsFor(c.ContainerType, c.IsDouble);

            var world = dto.WorldName?.Trim();
            if (!string.IsNullOrEmpty(world)) c.WorldName = world;
            else if (isCreate) throw new ArgumentException("worldName is required.");
            if (c.WorldName.Length > StorageContainer.WorldNameMaxLength)
                throw new ArgumentException($"worldName is at most {StorageContainer.WorldNameMaxLength} characters.");
            if (isCreate && (!dto.X.HasValue || !dto.Y.HasValue || !dto.Z.HasValue))
                throw new ArgumentException("x, y and z are required (the block's position).");
            if (dto.X.HasValue) c.X = dto.X.Value;
            if (dto.Y.HasValue) c.Y = dto.Y.Value;
            if (dto.Z.HasValue) c.Z = dto.Z.Value;
            if (c.Y < MinY || c.Y > MaxY) throw new ArgumentException($"y must be {MinY} to {MaxY}.");
            if (dto.Label != null)
            {
                var label = dto.Label.Trim();
                if (label.Length > StorageContainer.LabelMaxLength)
                    throw new ArgumentException($"label is at most {StorageContainer.LabelMaxLength} characters.");
                c.Label = label.Length == 0 ? null : label;
            }
        }

        private async Task<decimal> UsedAsync(int storageId, CancellationToken ct) =>
            await _db.StockLines.Where(l => l.StorageId == storageId).SumAsync(l => l.Amount * l.ItemBlueprint.StorageWeight, ct)
            + await _db.StockUnits.Where(u => u.StorageId == storageId).SumAsync(u => u.ItemInstance.ItemBlueprint.StorageWeight, ct);

        private async Task<StorageContainerDto> ToDtoAsync(StorageContainer c, CancellationToken ct) => new()
        {
            Id = c.Id,
            StorageId = c.StorageId,
            Storage = new StockNavDto { Id = c.StorageId, Name = c.Storage?.Name },
            Mode = c.Mode,
            ContainerType = c.ContainerType,
            IsDouble = c.IsDouble,
            Slots = c.Slots,
            WorldName = c.WorldName,
            X = c.X,
            Y = c.Y,
            Z = c.Z,
            Label = c.Label,
            CreatedByUserId = c.CreatedByUserId,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt,
            StorageContainerSlots = await _db.StorageContainers.Where(x => x.StorageId == c.StorageId).SumAsync(x => x.Slots, ct),
            StorageUnitsPerSlot = c.Storage?.UnitsPerSlot,
            StorageMaxUnits = c.Storage?.MaxUnits,
        };

        private static string? Filter(PagedQueryDto q, string name) =>
            q.Filters != null && q.Filters.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginAsync(CancellationToken ct) =>
            _db.Database.IsRelational() && _db.Database.CurrentTransaction == null
                ? await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
                : null;
    }

    /// <summary>
    /// Container-derived capacity (DESIGN § 4.4 rev. 3): a storage with bound containers and <c>UnitsPerSlot</c> set is
    /// capped at Σ slots × <c>UnitsPerSlot</c>, written to <c>MaxUnits</c> so every reader (stock moves, the engine's full
    /// check, transport room) sees it without a second rule. Without bound containers, or without <c>UnitsPerSlot</c>,
    /// the storage keeps its fixed cap (unbinding the last container leaves the last value, nothing is trimmed). Call it
    /// under the storage's row lock, after the container change is saved.
    /// </summary>
    public static class StorageContainerCapacity
    {
        public static async Task<bool> ApplyAsync(KnKDbContext db, Storage storage, CancellationToken ct = default)
        {
            if (!storage.IsCapped || storage.UnitsPerSlot is not > 0) return false;
            int slots = await db.StorageContainers.Where(c => c.StorageId == storage.Id).SumAsync(c => c.Slots, ct);
            if (slots == 0) return false;
            long cap = (long)slots * storage.UnitsPerSlot.Value;
            if (storage.MaxUnits == cap) return false;
            storage.MaxUnits = cap;
            storage.RowVersion++;
            return true;
        }
    }
}
