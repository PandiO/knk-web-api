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

namespace knkwebapi_v2.Services.Shops
{
    public interface IShopDisplaySpotService
    {
        Task<ShopDisplaySpotDto?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<PagedResultDto<ShopDisplaySpotListDto>> SearchAsync(PagedQueryDto query, CancellationToken ct = default);
        Task<ShopDisplaySpotDto> CreateAsync(ShopDisplaySpotDto dto, int? actorUserId, CancellationToken ct = default);
        Task<ShopDisplaySpotDto> UpdateAsync(int id, ShopDisplaySpotDto dto, CancellationToken ct = default);
        Task DeleteAsync(int id, CancellationToken ct = default);
    }

    /// <summary>
    /// Display spots of shops (display stalls P12, PandiO/knk-workspace#250, shops DESIGN § 7): registered explicitly per
    /// shop, one per block, never found by scanning. What a spot shows is chosen by the game server from the shop's offer
    /// and trade stats; this service only keeps the spots. Works while <c>Production:Enabled</c> is off (admin CRUD, as the
    /// catalogue). Errors: <see cref="ArgumentException"/> (400), <see cref="KeyNotFoundException"/> (404),
    /// <see cref="InvalidOperationException"/> (409: the block is taken, the shop has its maximum).
    /// </summary>
    public sealed class ShopDisplaySpotService : IShopDisplaySpotService
    {
        private readonly KnKDbContext _db;

        public ShopDisplaySpotService(KnKDbContext db) => _db = db;

        public async Task<ShopDisplaySpotDto?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            var s = await _db.Set<ShopDisplaySpot>().AsNoTracking().Include(x => x.Shop)
                .Include(x => x.PinnedCatalogueEntry).ThenInclude(e => e!.ItemBlueprint)
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s == null) return null;
            return new ShopDisplaySpotDto
            {
                Id = s.Id, ShopId = s.ShopId, Shop = new NamedRefDto { Id = s.ShopId, Name = s.Shop.Name },
                WorldName = s.WorldName, X = s.X, Y = s.Y, Z = s.Z, Facing = s.Facing.ToString(),
                PinnedCatalogueEntryId = s.PinnedCatalogueEntryId,
                PinnedCatalogueEntry = s.PinnedCatalogueEntry == null ? null : new NamedRefDto
                {
                    Id = s.PinnedCatalogueEntry.Id,
                    Name = s.PinnedCatalogueEntry.ItemBlueprint.Name ?? s.PinnedCatalogueEntry.ItemBlueprint.DefaultDisplayName ?? string.Empty,
                },
                SortOrder = s.SortOrder, Label = s.Label, Enabled = s.Enabled, CreatedByUserId = s.CreatedByUserId,
                CreatedAt = s.CreatedAt, UpdatedAt = s.UpdatedAt,
            };
        }

        /// <summary>Filters shopId, enabled (true/false), worldName; search term = label or shop name.</summary>
        public async Task<PagedResultDto<ShopDisplaySpotListDto>> SearchAsync(PagedQueryDto query, CancellationToken ct = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            IQueryable<ShopDisplaySpot> q = _db.Set<ShopDisplaySpot>().AsNoTracking();
            if (TerritoryPaging.IntFilter(query, "shopId") is { } shopId) q = q.Where(s => s.ShopId == shopId);
            if (query.Filters != null && query.Filters.TryGetValue("enabled", out var en) && bool.TryParse(en, out var enabled))
                q = q.Where(s => s.Enabled == enabled);
            if (query.Filters != null && query.Filters.TryGetValue("worldName", out var world) && !string.IsNullOrWhiteSpace(world))
            {
                var w = world.Trim();
                q = q.Where(s => s.WorldName == w);
            }
            if (TerritoryPaging.Term(query) is { } term)
                q = q.Where(s => (s.Label != null && s.Label.ToLower().Contains(term)) || s.Shop.Name.ToLower().Contains(term));
            int total = await q.CountAsync(ct);
            var items = await q.OrderBy(s => s.ShopId).ThenBy(s => s.SortOrder).ThenBy(s => s.Id)
                .Skip(ShopEnum.Skip(query)).Take(ShopEnum.Size(query))
                .Select(s => new ShopDisplaySpotListDto
                {
                    Id = s.Id, ShopId = s.ShopId, ShopName = s.Shop.Name, WorldName = s.WorldName, X = s.X, Y = s.Y, Z = s.Z,
                    Facing = s.Facing.ToString(), PinnedCatalogueEntryId = s.PinnedCatalogueEntryId, SortOrder = s.SortOrder,
                    Label = s.Label, Enabled = s.Enabled,
                }).ToListAsync(ct);
            return ShopEnum.Page(items, total, query);
        }

        public async Task<ShopDisplaySpotDto> CreateAsync(ShopDisplaySpotDto dto, int? actorUserId, CancellationToken ct = default)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (dto.ShopId is not > 0 || !await _db.Set<Shop>().AnyAsync(s => s.Id == dto.ShopId, ct))
                throw new ArgumentException("shopId must name an existing shop.");
            if (dto.X == null || dto.Y == null || dto.Z == null) throw new ArgumentException("x, y and z are required.");
            if (await _db.Set<ShopDisplaySpot>().CountAsync(s => s.ShopId == dto.ShopId, ct) >= ShopDisplaySpot.MaxPerShop)
                throw new InvalidOperationException($"A shop has at most {ShopDisplaySpot.MaxPerShop} display spots.");
            var s = new ShopDisplaySpot
            {
                ShopId = dto.ShopId.Value,
                CreatedByUserId = actorUserId is > 0 ? actorUserId : dto.CreatedByUserId is > 0 ? dto.CreatedByUserId : null,
            };
            if (dto.SortOrder == null)
                s.SortOrder = (await _db.Set<ShopDisplaySpot>().Where(x => x.ShopId == s.ShopId).MaxAsync(x => (int?)x.SortOrder, ct) ?? 0) + 1;
            await ApplyAsync(s, dto, ct);
            _db.Set<ShopDisplaySpot>().Add(s);
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
            return (await GetByIdAsync(s.Id, ct))!;
        }

        public async Task<ShopDisplaySpotDto> UpdateAsync(int id, ShopDisplaySpotDto dto, CancellationToken ct = default)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var s = await _db.Set<ShopDisplaySpot>().FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new KeyNotFoundException($"ShopDisplaySpot with id {id} not found.");
            if (dto.ShopId.HasValue && dto.ShopId != s.ShopId)
                throw new ArgumentException("A spot keeps its shop; remove it and register it for the other shop instead.");
            await ApplyAsync(s, dto, ct);
            s.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
            return (await GetByIdAsync(id, ct))!;
        }

        private async Task ApplyAsync(ShopDisplaySpot s, ShopDisplaySpotDto dto, CancellationToken ct)
        {
            if (dto.WorldName != null) s.WorldName = ShopEnum.Text(dto.WorldName, "worldName", ShopDisplaySpot.WorldNameMaxLength, true);
            else if (s.WorldName == null) throw new ArgumentException("worldName is required.");
            if (dto.X.HasValue) s.X = dto.X.Value;
            if (dto.Y.HasValue)
            {
                if (dto.Y.Value is < ShopDisplaySpot.MinY or > ShopDisplaySpot.MaxY)
                    throw new ArgumentException($"y must be {ShopDisplaySpot.MinY} to {ShopDisplaySpot.MaxY}.");
                s.Y = dto.Y.Value;
            }
            if (dto.Z.HasValue) s.Z = dto.Z.Value;
            if (dto.Facing != null) s.Facing = ShopEnum.Parse<ShopDisplayFacing>(dto.Facing, "facing");
            if (dto.PinnedCatalogueEntryId.HasValue)
            {
                if (dto.PinnedCatalogueEntryId.Value <= 0) s.PinnedCatalogueEntryId = null;
                else
                {
                    var entryShop = await _db.Set<ShopCatalogueEntry>().AsNoTracking().Where(e => e.Id == dto.PinnedCatalogueEntryId)
                        .Select(e => (int?)e.ShopId).FirstOrDefaultAsync(ct);
                    if (entryShop == null) throw new ArgumentException($"Catalogue entry {dto.PinnedCatalogueEntryId} not found.");
                    if (entryShop != s.ShopId) throw new ArgumentException("pinnedCatalogueEntryId must be an entry of the spot's own shop.");
                    s.PinnedCatalogueEntryId = dto.PinnedCatalogueEntryId.Value;
                }
            }
            if (dto.SortOrder.HasValue) s.SortOrder = dto.SortOrder.Value;
            if (dto.Label != null)
            {
                var label = ShopEnum.Text(dto.Label, "label", ShopDisplaySpot.LabelMaxLength, false);
                s.Label = label.Length == 0 ? null : label;
            }
            if (dto.Enabled.HasValue) s.Enabled = dto.Enabled.Value;
            if (await _db.Set<ShopDisplaySpot>().AnyAsync(x => x.Id != s.Id && x.WorldName == s.WorldName && x.X == s.X && x.Y == s.Y && x.Z == s.Z, ct))
                throw new InvalidOperationException($"The block {s.WorldName} {s.X} {s.Y} {s.Z} is already a display spot.");
        }

        public async Task DeleteAsync(int id, CancellationToken ct = default)
        {
            var s = await _db.Set<ShopDisplaySpot>().FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new KeyNotFoundException($"ShopDisplaySpot with id {id} not found.");
            _db.Set<ShopDisplaySpot>().Remove(s);
            await _db.SaveChangesAsync(ct);
        }
    }
}
