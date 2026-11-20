using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Stock
{
    public interface IStorageService
    {
        Task<IEnumerable<StorageListDto>> GetAllAsync();
        Task<StorageDto?> GetByIdAsync(int id);
        Task<StorageDto> CreateAsync(StorageDto dto);
        Task UpdateAsync(int id, StorageDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<StorageListDto>> SearchAsync(PagedQueryDto query);
    }

    /// <summary>
    /// Storage CRUD (production DESIGN § 4.1, stock core P1 #97). Admin CRUD works while <c>Production:Enabled</c> is off
    /// (DESIGN § 18.2). Quantities never change here: only <see cref="IStockService"/> moves stock. The five system
    /// storages are seeded and can't be created, edited or deleted. A storage with stock or ledger history can't be
    /// deleted, and its kind and holder can't change (409).
    /// </summary>
    public sealed class StorageService : IStorageService
    {
        public const int MaxDetailLines = 1000;

        private readonly KnKDbContext _db;
        private readonly TimeProvider _clock;

        public StorageService(KnKDbContext db, TimeProvider? clock = null)
        {
            _db = db;
            _clock = clock ?? TimeProvider.System;
        }

        public async Task<IEnumerable<StorageListDto>> GetAllAsync() =>
            await ListProjection(_db.Storages.AsNoTracking().OrderBy(s => s.Id)).ToListAsync();

        public async Task<StorageDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var s = await _db.Storages.AsNoTracking()
                .Include(x => x.Domain).Include(x => x.User).Include(x => x.Clan)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (s == null) return null;
            var lines = await _db.StockLines.AsNoTracking().Where(l => l.StorageId == id)
                .OrderBy(l => l.ItemBlueprint.Name).ThenBy(l => l.ItemBlueprintId).Take(MaxDetailLines)
                .Select(l => new StockLineDto
                {
                    Id = l.Id,
                    StorageId = l.StorageId,
                    ItemBlueprintId = l.ItemBlueprintId,
                    ItemBlueprintName = l.ItemBlueprint.Name,
                    ItemBlueprint = new NamedRefDto { Id = l.ItemBlueprintId, Name = l.ItemBlueprint.Name },
                    Amount = l.Amount,
                    StorageWeight = l.ItemBlueprint.StorageWeight,
                    UpdatedAt = l.UpdatedAt,
                }).ToListAsync();
            var units = await _db.StockUnits.AsNoTracking().Where(u => u.StorageId == id)
                .OrderBy(u => u.ItemInstanceId).Take(MaxDetailLines)
                .Select(u => new StockUnitDto
                {
                    Id = u.Id,
                    StorageId = u.StorageId,
                    ItemInstanceId = u.ItemInstanceId,
                    ItemBlueprintId = u.ItemInstance.ItemBlueprintId,
                    ItemBlueprintName = u.ItemInstance.ItemBlueprint.Name,
                    CustomDisplayName = u.ItemInstance.CustomDisplayName,
                    AddedAt = u.AddedAt,
                }).ToListAsync();
            decimal used = await _db.StockLines.Where(l => l.StorageId == id).SumAsync(l => l.Amount * l.ItemBlueprint.StorageWeight)
                + await _db.StockUnits.Where(u => u.StorageId == id).SumAsync(u => u.ItemInstance.ItemBlueprint.StorageWeight);
            var dto = ToDto(s);
            dto.Lines = lines;
            dto.Units = units;
            dto.CapacityUsed = used;
            return dto;
        }

        public async Task<StorageDto> CreateAsync(StorageDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var kind = dto.Kind ?? throw new ArgumentException("Kind is required.", nameof(dto));
            if (kind == StorageKind.Virtual)
                throw new ArgumentException("System (virtual) storages are seeded; they can't be created.", nameof(dto));
            var s = new Storage { Kind = kind, CreatedAt = Now() };
            await ApplyHolderAsync(s, dto);
            Apply(s, dto);
            _db.Storages.Add(s);
            await _db.SaveChangesAsync();
            return (await GetByIdAsync(s.Id))!;
        }

        public async Task UpdateAsync(int id, StorageDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            bool relational = _db.Database.IsRelational();
            await using var tx = relational && _db.Database.CurrentTransaction == null
                ? await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted)
                : null;
            // The same row lock as a move, so an edit never writes over totals a move just changed.
            await StockLocks.LockStoragesAsync(_db, new[] { id }, default);
            var tracked = _db.ChangeTracker.Entries<Storage>().FirstOrDefault(e => e.Entity.Id == id);
            if (tracked != null) await tracked.ReloadAsync();
            var s = await _db.Storages.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new KeyNotFoundException($"Storage with id {id} not found.");
            if (s.IsVirtual) throw new InvalidOperationException("System storages can't be edited.");
            if (dto.RowVersion.HasValue && dto.RowVersion.Value != s.RowVersion)
                throw new InvalidOperationException("The storage changed since it was read; reload it and try again.");

            // Null kind or holder id = keep (a form that leaves them out doesn't change them).
            var kind = dto.Kind ?? s.Kind;
            bool holderChanges = kind != s.Kind
                || (kind == StorageKind.Domain && dto.DomainId.HasValue && dto.DomainId != s.DomainId)
                || (kind == StorageKind.Player && dto.UserId.HasValue && dto.UserId != s.UserId)
                || (kind == StorageKind.Clan && dto.ClanId.HasValue && dto.ClanId != s.ClanId);
            if (holderChanges)
            {
                if (kind == StorageKind.Virtual)
                    throw new ArgumentException("A storage can't become a system storage.", nameof(dto));
                if (await HasStockOrHistoryAsync(id))
                    throw new InvalidOperationException("Kind and holder can't change once the storage has stock or ledger history.");
                s.Kind = kind;
                await ApplyHolderAsync(s, dto);
            }
            Apply(s, dto);
            // P6a (#683): with bound containers and units per slot the cap is derived (slots × units per slot).
            await StorageContainerCapacity.ApplyAsync(_db, s);
            s.RowVersion++;
            await _db.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var s = await _db.Storages.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new KeyNotFoundException($"Storage with id {id} not found.");
            if (s.IsVirtual) throw new InvalidOperationException("System storages can't be deleted.");
            if (await HasStockOrHistoryAsync(id))
                throw new InvalidOperationException(
                    "This storage can't be deleted: it holds stock or has ledger history (the ledger keeps every storage it names).");
            _db.Storages.Remove(s);
            await _db.SaveChangesAsync();
        }

        public async Task<PagedResultDto<StorageListDto>> SearchAsync(PagedQueryDto query)
        {
            query ??= new PagedQueryDto();
            var q = _db.Storages.AsNoTracking();
            if (TerritoryPaging.Term(query) is { } term)
                q = q.Where(s => s.Name.ToLower().Contains(term)
                    || (s.Domain != null && s.Domain.Name.ToLower().Contains(term))
                    || (s.User != null && s.User.Username.ToLower().Contains(term))
                    || (s.Clan != null && s.Clan.Name.ToLower().Contains(term))
                    || (s.SystemKey != null && s.SystemKey.ToLower().Contains(term)));
            if (Filter(query, "kind") is { } kindText && Enum.TryParse<StorageKind>(kindText, true, out var kind))
                q = q.Where(s => s.Kind == kind);
            if (Filter(query, "purpose") is { } purposeText && Enum.TryParse<StoragePurpose>(purposeText, true, out var purpose))
                q = q.Where(s => s.Purpose == purpose);
            if (TerritoryPaging.IntFilter(query, "domainId") is { } domainId) q = q.Where(s => s.DomainId == domainId);
            if (TerritoryPaging.IntFilter(query, "userId") is { } userId) q = q.Where(s => s.UserId == userId);
            if (TerritoryPaging.IntFilter(query, "clanId") is { } clanId) q = q.Where(s => s.ClanId == clanId);
            if (Filter(query, "locked") is { } lockedText && bool.TryParse(lockedText, out var locked))
            {
                var now = Now();
                q = locked ? q.Where(s => s.LockedUntil > now) : q.Where(s => s.LockedUntil == null || s.LockedUntil <= now);
            }
            q = (query.SortBy?.ToLowerInvariant()) switch
            {
                "name" => query.SortDescending ? q.OrderByDescending(s => s.Name) : q.OrderBy(s => s.Name),
                "kind" => query.SortDescending ? q.OrderByDescending(s => s.Kind) : q.OrderBy(s => s.Kind),
                "totalunits" => query.SortDescending ? q.OrderByDescending(s => s.TotalUnits) : q.OrderBy(s => s.TotalUnits),
                _ => query.SortDescending ? q.OrderByDescending(s => s.Id) : q.OrderBy(s => s.Id),
            };
            return await TerritoryPaging.PageAsync(ListProjection(q), query);
        }

        // ===== Helpers =====

        private static IQueryable<StorageListDto> ListProjection(IQueryable<Storage> q) => q.Select(s => new StorageListDto
        {
            Id = s.Id,
            Name = s.Name,
            Kind = s.Kind,
            Purpose = s.Purpose,
            HolderName = s.Domain != null ? s.Domain.Name
                : s.User != null ? s.User.Username
                : s.Clan != null ? s.Clan.Name
                : s.SystemKey != null ? s.SystemKey
                : "Transit",
            DomainId = s.DomainId,
            UserId = s.UserId,
            ClanId = s.ClanId,
            SystemKey = s.SystemKey,
            TotalUnits = s.TotalUnits,
            DistinctItems = s.DistinctItems,
            MaxUnits = s.MaxUnits,
            MaxDistinct = s.MaxDistinct,
            LockedUntil = s.LockedUntil,
        });

        private async Task ApplyHolderAsync(Storage s, StorageDto dto)
        {
            s.DomainId = null;
            s.UserId = null;
            s.ClanId = null;
            switch (s.Kind)
            {
                case StorageKind.Domain:
                    RequireOnly(dto, nameof(StorageDto.DomainId));
                    var domainId = dto.DomainId ?? throw new ArgumentException("A domain storage needs a domain (DomainId).");
                    if (!await _db.Domains.AnyAsync(d => d.Id == domainId)) throw new ArgumentException($"Domain {domainId} not found.");
                    s.DomainId = domainId;
                    break;
                case StorageKind.Player:
                    RequireOnly(dto, nameof(StorageDto.UserId));
                    var userId = dto.UserId ?? throw new ArgumentException("A player storage needs a user (UserId).");
                    if (!await _db.Users.AnyAsync(u => u.Id == userId)) throw new ArgumentException($"User {userId} not found.");
                    s.UserId = userId;
                    break;
                case StorageKind.Clan:
                    RequireOnly(dto, nameof(StorageDto.ClanId));
                    var clanId = dto.ClanId ?? throw new ArgumentException("A clan storage needs a clan (ClanId).");
                    if (!await _db.Clans.AnyAsync(c => c.Id == clanId)) throw new ArgumentException($"Clan {clanId} not found.");
                    s.ClanId = clanId;
                    break;
                case StorageKind.Transit:
                    RequireOnly(dto, null);
                    break;
                default:
                    throw new ArgumentException($"Unknown storage kind {s.Kind}.");
            }
        }

        /// <summary>Exactly the holder id of the kind may be set (the check constraint, with a readable message).</summary>
        private static void RequireOnly(StorageDto dto, string? allowed)
        {
            var set = new List<string>();
            if (dto.DomainId.HasValue) set.Add(nameof(StorageDto.DomainId));
            if (dto.UserId.HasValue) set.Add(nameof(StorageDto.UserId));
            if (dto.ClanId.HasValue) set.Add(nameof(StorageDto.ClanId));
            var extra = set.Where(n => n != allowed).ToList();
            if (extra.Count > 0)
                throw new ArgumentException($"A {dto.Kind} storage has {(allowed == null ? "no holder" : "only " + allowed)}; remove {string.Join(", ", extra)}.");
        }

        private void Apply(Storage s, StorageDto dto)
        {
            var name = dto.Name?.Trim();
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Name is required.");
            if (name.Length > Storage.NameMaxLength) throw new ArgumentException($"Name is at most {Storage.NameMaxLength} characters.");
            s.Name = name;
            if (dto.Purpose.HasValue)
            {
                if (!Enum.IsDefined(dto.Purpose.Value)) throw new ArgumentException("Unknown purpose.");
                s.Purpose = dto.Purpose.Value;
            }
            if (dto.MaxUnits < 0) throw new ArgumentException("Max units can't be negative.");
            if (dto.MaxDistinct < 0) throw new ArgumentException("Max distinct items can't be negative.");
            if (dto.UnitsPerSlot < 0) throw new ArgumentException("Units per slot can't be negative.");
            if (!s.IsCapped && (dto.MaxUnits.HasValue || dto.MaxDistinct.HasValue))
                throw new ArgumentException("Transit storages are not capped (a transport order's carrier bounds them).");
            // Lowering a cap below the stock is allowed: the storage then only accepts removals (DESIGN § 4.4).
            s.MaxUnits = dto.MaxUnits;
            s.MaxDistinct = dto.MaxDistinct;
            s.UnitsPerSlot = dto.UnitsPerSlot;
            var reason = string.IsNullOrWhiteSpace(dto.LockReason) ? null : dto.LockReason.Trim();
            if (reason?.Length > Storage.LockReasonMaxLength)
                throw new ArgumentException($"Lock reason is at most {Storage.LockReasonMaxLength} characters.");
            s.LockedUntil = dto.LockedUntil.HasValue ? DateTime.SpecifyKind(dto.LockedUntil.Value.ToUniversalTime(), DateTimeKind.Utc) : null;
            s.LockReason = s.LockedUntil.HasValue ? reason : null;
        }

        private async Task<bool> HasStockOrHistoryAsync(int id) =>
            await _db.StockLines.AnyAsync(l => l.StorageId == id)
            || await _db.StockUnits.AnyAsync(u => u.StorageId == id)
            || await _db.StockMoves.AnyAsync(m => m.FromStorageId == id || m.ToStorageId == id)
            || await _db.StockMoveRollups.AnyAsync(r => r.FromStorageId == id || r.ToStorageId == id);

        private static string? Filter(PagedQueryDto q, string name) =>
            q.Filters != null && q.Filters.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        private DateTime Now() => _clock.GetUtcNow().UtcDateTime;

        private StorageDto ToDto(Storage s) => new()
        {
            Id = s.Id,
            Name = s.Name,
            Kind = s.Kind,
            Purpose = s.Purpose,
            DomainId = s.DomainId,
            Domain = s.Domain == null ? null : new StockNavDto { Id = s.Domain.Id, Name = s.Domain.Name },
            UserId = s.UserId,
            User = s.User == null ? null : new StockNavDto { Id = s.User.Id, Name = s.User.Username },
            ClanId = s.ClanId,
            Clan = s.Clan == null ? null : new StockNavDto { Id = s.Clan.Id, Name = s.Clan.Name },
            SystemKey = s.SystemKey,
            HolderName = s.Domain?.Name ?? s.User?.Username ?? s.Clan?.Name ?? s.SystemKey ?? "Transit",
            MaxUnits = s.MaxUnits,
            MaxDistinct = s.MaxDistinct,
            UnitsPerSlot = s.UnitsPerSlot,
            TotalUnits = s.TotalUnits,
            DistinctItems = s.DistinctItems,
            RowVersion = s.RowVersion,
            LockedUntil = s.LockedUntil,
            LockReason = s.LockReason,
            IsLocked = s.IsLockedAt(Now()),
            CreatedAt = s.CreatedAt,
        };
    }
}
