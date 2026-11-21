using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Logistics
{
    public interface IConsumerReplenishmentService
    {
        Task<ConsumerReplenishmentDto?> GetByIdAsync(int id, CancellationToken ct = default);

        /// <summary>Filters storageId, structureId, townId, itemBlueprintId, enabled; search term = structure or blueprint name.</summary>
        Task<PagedResultDto<ConsumerReplenishmentListDto>> SearchAsync(PagedQueryDto query, CancellationToken ct = default);

        Task<ConsumerReplenishmentDto> CreateAsync(ConsumerReplenishmentDto dto, CancellationToken ct = default);

        Task<ConsumerReplenishmentDto> UpdateAsync(int id, ConsumerReplenishmentDto dto, CancellationToken ct = default);

        Task DeleteAsync(int id, CancellationToken ct = default);
    }

    /// <summary>Consumer replenishment settings (TOWN_LOGISTICS § 3): min/max per Input or Merchandise storage and blueprint,
    /// and an optional priority class. Works while Production:Enabled is off.</summary>
    public sealed class ConsumerReplenishmentService : IConsumerReplenishmentService
    {
        private readonly KnKDbContext _db;
        private readonly TimeProvider _clock;

        public ConsumerReplenishmentService(KnKDbContext db, TimeProvider? clock = null)
        {
            _db = db;
            _clock = clock ?? TimeProvider.System;
        }

        public async Task<ConsumerReplenishmentDto?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            var r = await _db.Set<ConsumerReplenishment>().AsNoTracking().Include(x => x.Storage).Include(x => x.Structure).Include(x => x.ItemBlueprint)
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            return r == null ? null : new ConsumerReplenishmentDto
            {
                Id = r.Id, StorageId = r.StorageId, Storage = new NamedRefDto { Id = r.StorageId, Name = r.Storage.Name },
                StructureId = r.StructureId, Structure = new NamedRefDto { Id = r.StructureId, Name = r.Structure.Name },
                ItemBlueprintId = r.ItemBlueprintId, ItemBlueprint = new NamedRefDto { Id = r.ItemBlueprintId, Name = r.ItemBlueprint.Name },
                ReorderPoint = r.ReorderPoint, TargetLevel = r.TargetLevel, PriorityClass = r.PriorityClass?.ToString(), Enabled = r.Enabled,
                Note = r.Note, CreatedAt = r.CreatedAt, UpdatedAt = r.UpdatedAt,
            };
        }

        public async Task<PagedResultDto<ConsumerReplenishmentListDto>> SearchAsync(PagedQueryDto query, CancellationToken ct = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            IQueryable<ConsumerReplenishment> q = _db.Set<ConsumerReplenishment>().AsNoTracking();
            if (TerritoryPaging.IntFilter(query, "storageId") is { } st) q = q.Where(r => r.StorageId == st);
            if (TerritoryPaging.IntFilter(query, "structureId") is { } s) q = q.Where(r => r.StructureId == s);
            if (TerritoryPaging.IntFilter(query, "itemBlueprintId") is { } b) q = q.Where(r => r.ItemBlueprintId == b);
            if (TerritoryPaging.IntFilter(query, "townId") is { } town)
                q = q.Where(r => r.Structure.TownId == town || (r.Structure.District != null && r.Structure.District.TownId == town));
            if (query.Filters != null && query.Filters.TryGetValue("enabled", out var en) && bool.TryParse(en, out var enabled)) q = q.Where(r => r.Enabled == enabled);
            if (TerritoryPaging.Term(query) is { } term)
                q = q.Where(r => r.Structure.Name.ToLower().Contains(term) || r.ItemBlueprint.Name.ToLower().Contains(term));
            return await TerritoryPaging.PageAsync(q.OrderBy(r => r.Structure.Name).ThenBy(r => r.StorageId).ThenBy(r => r.ItemBlueprint.Name)
                .Select(r => new ConsumerReplenishmentListDto
                {
                    Id = r.Id, StorageId = r.StorageId, StorageName = r.Storage.Name, StructureId = r.StructureId, StructureName = r.Structure.Name,
                    ItemBlueprintId = r.ItemBlueprintId, ItemBlueprintName = r.ItemBlueprint.Name, ReorderPoint = r.ReorderPoint,
                    TargetLevel = r.TargetLevel, PriorityClass = r.PriorityClass != null ? r.PriorityClass.ToString() : null, Enabled = r.Enabled,
                    UpdatedAt = r.UpdatedAt,
                }), query);
        }

        public async Task<ConsumerReplenishmentDto> CreateAsync(ConsumerReplenishmentDto dto, CancellationToken ct = default)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var row = new ConsumerReplenishment { CreatedAt = Now };
            await ApplyAsync(row, dto, true, ct);
            if (await _db.Set<ConsumerReplenishment>().AnyAsync(r => r.StorageId == row.StorageId && r.ItemBlueprintId == row.ItemBlueprintId, ct))
                throw new InvalidOperationException("This storage already has a setting for that blueprint: edit it.");
            _db.Set<ConsumerReplenishment>().Add(row);
            await SaveAsync(ct);
            return (await GetByIdAsync(row.Id, ct))!;
        }

        public async Task<ConsumerReplenishmentDto> UpdateAsync(int id, ConsumerReplenishmentDto dto, CancellationToken ct = default)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var row = await _db.Set<ConsumerReplenishment>().FirstOrDefaultAsync(r => r.Id == id, ct)
                ?? throw new KeyNotFoundException($"Replenishment setting {id} not found.");
            await ApplyAsync(row, dto, false, ct);
            if (await _db.Set<ConsumerReplenishment>().AnyAsync(r => r.Id != id && r.StorageId == row.StorageId && r.ItemBlueprintId == row.ItemBlueprintId, ct))
                throw new InvalidOperationException("This storage already has a setting for that blueprint.");
            await SaveAsync(ct);
            return (await GetByIdAsync(id, ct))!;
        }

        public async Task DeleteAsync(int id, CancellationToken ct = default)
        {
            var row = await _db.Set<ConsumerReplenishment>().FirstOrDefaultAsync(r => r.Id == id, ct)
                ?? throw new KeyNotFoundException($"Replenishment setting {id} not found.");
            _db.Set<ConsumerReplenishment>().Remove(row);
            await SaveAsync(ct);
        }

        private DateTime Now => _clock.GetUtcNow().UtcDateTime;

        private async Task ApplyAsync(ConsumerReplenishment row, ConsumerReplenishmentDto dto, bool create, CancellationToken ct)
        {
            var errors = new List<string>();
            int storageId = dto.StorageId ?? (create ? 0 : row.StorageId);
            var storage = await _db.Storages.AsNoTracking().FirstOrDefaultAsync(s => s.Id == storageId, ct);
            if (storage == null) errors.Add("An existing storageId is required.");
            else
            {
                if (storage.Kind != StorageKind.Domain || storage.Purpose is not (StoragePurpose.Input or StoragePurpose.Merchandise))
                    errors.Add("The storage must be a structure's Input or Merchandise storage (a consumer storage).");
                else if (storage.DomainId is not { } holder || !await _db.Structures.AnyAsync(s => s.Id == holder, ct))
                    errors.Add("The storage must belong to a structure.");
                else if (await _db.Warehouses.AnyAsync(w => w.Id == holder, ct))
                    errors.Add("A warehouse is not a consumer.");
                else
                {
                    row.StorageId = storage.Id;
                    row.StructureId = holder;
                }
            }
            int blueprintId = dto.ItemBlueprintId ?? (create ? 0 : row.ItemBlueprintId);
            if (!await _db.ItemBlueprints.AnyAsync(b => b.Id == blueprintId, ct)) errors.Add("An existing itemBlueprintId is required.");
            else row.ItemBlueprintId = blueprintId;
            long reorder = dto.ReorderPoint ?? (create ? -1 : row.ReorderPoint);
            long target = dto.TargetLevel ?? (create ? -1 : row.TargetLevel);
            if (reorder < 0 || target <= 0 || reorder > target || target > LogisticsRule.MaxUnits)
                errors.Add($"reorderPoint (0 or more) and targetLevel (above 0, at most {LogisticsRule.MaxUnits}) are required, reorderPoint ≤ targetLevel.");
            else
            {
                row.ReorderPoint = reorder;
                row.TargetLevel = target;
            }
            if (dto.PriorityClass != null)
            {
                if (string.IsNullOrWhiteSpace(dto.PriorityClass)) row.PriorityClass = null;
                else if (Enum.TryParse<LogisticsPriorityClass>(dto.PriorityClass.Trim(), true, out var pc) && Enum.IsDefined(pc)) row.PriorityClass = pc;
                else errors.Add("priorityClass must be Critical, High, Normal, Low or empty (the policy's rules).");
            }
            else if (create) row.PriorityClass = null;
            if (dto.Enabled.HasValue) row.Enabled = dto.Enabled.Value;
            var note = dto.Note?.Trim();
            if (note?.Length > ConsumerReplenishment.NoteMaxLength) errors.Add($"note is at most {ConsumerReplenishment.NoteMaxLength} characters.");
            else row.Note = string.IsNullOrEmpty(note) ? null : note;
            if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors));
            row.UpdatedAt = Now;
        }

        private async Task SaveAsync(CancellationToken ct)
        {
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                _db.ChangeTracker.Clear();
                throw new InvalidOperationException("This storage already has a setting for that blueprint, or the setting changed meanwhile.", ex);
            }
            _db.ChangeTracker.Clear();
        }
    }
}
