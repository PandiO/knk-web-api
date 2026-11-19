using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services
{
    public interface IProvinceService
    {
        Task<IEnumerable<ProvinceDto>> GetAllAsync();
        Task<ProvinceDto?> GetByIdAsync(int id);
        Task<ProvinceDto> CreateAsync(ProvinceDto dto);
        Task UpdateAsync(int id, ProvinceDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<ProvinceListDto>> SearchAsync(PagedQueryDto query);
    }

    /// <summary>
    /// Provinces (territory DESIGN § 2): optional, inside one kingdom. Moving a province to another kingdom is refused
    /// while it has towns (their kingdom would no longer match); delete is refused while it has towns.
    /// </summary>
    public sealed class ProvinceService : IProvinceService
    {
        private readonly KnKDbContext _db;

        public ProvinceService(KnKDbContext db) => _db = db;

        public async Task<IEnumerable<ProvinceDto>> GetAllAsync()
        {
            var rows = await _db.Provinces.AsNoTracking().Include(p => p.Kingdom).OrderBy(p => p.Name).ToListAsync();
            return rows.Select(p => ToDto(p, false));
        }

        public async Task<ProvinceDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var p = await _db.Provinces.AsNoTracking().Include(x => x.Kingdom).Include(x => x.Towns)
                .FirstOrDefaultAsync(x => x.Id == id);
            return p == null ? null : ToDto(p, true);
        }

        public async Task<ProvinceDto> CreateAsync(ProvinceDto dto)
        {
            var name = await ValidateAsync(dto, null);
            var p = new Province { Name = name, Description = Clean(dto.Description), KingdomId = dto.KingdomId, CreatedAt = DateTime.UtcNow };
            _db.Provinces.Add(p);
            await _db.SaveChangesAsync();
            await _db.Entry(p).Reference(x => x.Kingdom).LoadAsync();
            return ToDto(p, false);
        }

        public async Task UpdateAsync(int id, ProvinceDto dto)
        {
            var p = await _db.Provinces.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new KeyNotFoundException($"Province with id {id} not found.");
            var name = await ValidateAsync(dto, id);
            if (p.KingdomId != dto.KingdomId && await _db.Towns.AnyAsync(t => t.ProvinceId == id))
                throw new ArgumentException("A province with towns can't move to another kingdom: move or detach its towns first.", nameof(dto));
            p.Name = name;
            p.Description = Clean(dto.Description);
            p.KingdomId = dto.KingdomId;
            await _db.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var p = await _db.Provinces.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new KeyNotFoundException($"Province with id {id} not found.");
            int towns = await _db.Towns.CountAsync(t => t.ProvinceId == id);
            if (towns > 0) throw new InvalidOperationException($"This province can't be deleted: it still has {towns} town(s).");
            _db.Provinces.Remove(p);
            await _db.SaveChangesAsync();
        }

        public async Task<PagedResultDto<ProvinceListDto>> SearchAsync(PagedQueryDto query)
        {
            query ??= new PagedQueryDto();
            var q = _db.Provinces.AsNoTracking();
            if (TerritoryPaging.Term(query) is { } term) q = q.Where(p => p.Name.ToLower().Contains(term));
            if (TerritoryPaging.IntFilter(query, "kingdomId") is { } kingdomId) q = q.Where(p => p.KingdomId == kingdomId);
            q = (query.SortBy?.ToLowerInvariant()) switch
            {
                "name" => query.SortDescending ? q.OrderByDescending(p => p.Name) : q.OrderBy(p => p.Name),
                _ => query.SortDescending ? q.OrderByDescending(p => p.Id) : q.OrderBy(p => p.Id),
            };
            return await TerritoryPaging.PageAsync(q.Select(p => new ProvinceListDto
            {
                Id = p.Id,
                Name = p.Name,
                KingdomId = p.KingdomId,
                KingdomName = p.Kingdom.Name,
                TownCount = p.Towns.Count,
            }), query);
        }

        private async Task<string> ValidateAsync(ProvinceDto dto, int? id)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var name = dto.Name?.Trim();
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Name is required.", nameof(dto));
            if (name.Length > Province.NameMaxLength) throw new ArgumentException($"Name is at most {Province.NameMaxLength} characters.", nameof(dto));
            if ((dto.Description?.Length ?? 0) > Province.DescriptionMaxLength)
                throw new ArgumentException($"Description is at most {Province.DescriptionMaxLength} characters.", nameof(dto));
            if (dto.KingdomId <= 0 || !await _db.Kingdoms.AnyAsync(k => k.Id == dto.KingdomId))
                throw new ArgumentException($"Kingdom with id {dto.KingdomId} not found: a province belongs to a kingdom.", nameof(dto));
            var lower = name.ToLower();
            if (await _db.Provinces.AnyAsync(p => p.Id != (id ?? 0) && p.KingdomId == dto.KingdomId && p.Name.ToLower() == lower))
                throw new ArgumentException($"The kingdom already has a province named '{name}'.", nameof(dto));
            return name;
        }

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static ProvinceDto ToDto(Province p, bool withTowns) => new()
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            KingdomId = p.KingdomId,
            CreatedAt = p.CreatedAt,
            Kingdom = p.Kingdom == null ? null : new TerritoryNavDto { Id = p.Kingdom.Id, Name = p.Kingdom.Name },
            Towns = withTowns ? p.Towns.OrderBy(t => t.Name).Select(t => new TerritoryNavDto { Id = t.Id, Name = t.Name }).ToList() : null,
        };
    }
}
