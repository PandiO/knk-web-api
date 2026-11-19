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
    public interface IKingdomService
    {
        Task<IEnumerable<KingdomDto>> GetAllAsync();
        Task<KingdomDto?> GetByIdAsync(int id);
        Task<KingdomDto> CreateAsync(KingdomDto dto);
        Task UpdateAsync(int id, KingdomDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<KingdomListDto>> SearchAsync(PagedQueryDto query);
    }

    /// <summary>Kingdoms (territory DESIGN § 2). Delete is refused while towns, provinces or crown land belong to it.</summary>
    public sealed class KingdomService : IKingdomService
    {
        private readonly KnKDbContext _db;

        public KingdomService(KnKDbContext db) => _db = db;

        public async Task<IEnumerable<KingdomDto>> GetAllAsync()
        {
            var rows = await _db.Kingdoms.AsNoTracking().OrderBy(k => k.Name).ToListAsync();
            return rows.Select(k => ToDto(k, null));
        }

        public async Task<KingdomDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var k = await _db.Kingdoms.AsNoTracking()
                .Include(x => x.Towns).Include(x => x.Provinces).Include(x => x.CrownLands)
                .FirstOrDefaultAsync(x => x.Id == id);
            return k == null ? null : ToDto(k, k);
        }

        public async Task<KingdomDto> CreateAsync(KingdomDto dto)
        {
            var name = await ValidateAsync(dto, null);
            var k = new Kingdom { Name = name, Description = Clean(dto.Description), CreatedAt = DateTime.UtcNow };
            _db.Kingdoms.Add(k);
            await _db.SaveChangesAsync();
            return ToDto(k, null);
        }

        public async Task UpdateAsync(int id, KingdomDto dto)
        {
            var k = await _db.Kingdoms.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new KeyNotFoundException($"Kingdom with id {id} not found.");
            k.Name = await ValidateAsync(dto, id);
            k.Description = Clean(dto.Description);
            await _db.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var k = await _db.Kingdoms.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new KeyNotFoundException($"Kingdom with id {id} not found.");
            int towns = await _db.Towns.CountAsync(t => t.KingdomId == id);
            int provinces = await _db.Provinces.CountAsync(p => p.KingdomId == id);
            int crown = await _db.CrownLands.CountAsync(c => c.KingdomId == id);
            if (towns + provinces + crown > 0)
                throw new InvalidOperationException(
                    $"This kingdom can't be deleted: it still has {towns} town(s), {provinces} province(s) and {crown} crown land(s).");
            _db.Kingdoms.Remove(k);
            await _db.SaveChangesAsync();
        }

        public async Task<PagedResultDto<KingdomListDto>> SearchAsync(PagedQueryDto query)
        {
            query ??= new PagedQueryDto();
            var q = _db.Kingdoms.AsNoTracking();
            if (TerritoryPaging.Term(query) is { } term) q = q.Where(k => k.Name.ToLower().Contains(term));
            q = (query.SortBy?.ToLowerInvariant()) switch
            {
                "name" => query.SortDescending ? q.OrderByDescending(k => k.Name) : q.OrderBy(k => k.Name),
                _ => query.SortDescending ? q.OrderByDescending(k => k.Id) : q.OrderBy(k => k.Id),
            };
            return await TerritoryPaging.PageAsync(q.Select(k => new KingdomListDto
            {
                Id = k.Id,
                Name = k.Name,
                Description = k.Description,
                TownCount = k.Towns.Count,
                CrownLandCount = k.CrownLands.Count,
            }), query);
        }

        private async Task<string> ValidateAsync(KingdomDto dto, int? id)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var name = dto.Name?.Trim();
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Name is required.", nameof(dto));
            if (name.Length > Kingdom.NameMaxLength) throw new ArgumentException($"Name is at most {Kingdom.NameMaxLength} characters.", nameof(dto));
            if ((dto.Description?.Length ?? 0) > Kingdom.DescriptionMaxLength)
                throw new ArgumentException($"Description is at most {Kingdom.DescriptionMaxLength} characters.", nameof(dto));
            var lower = name.ToLower();
            if (await _db.Kingdoms.AnyAsync(k => k.Id != (id ?? 0) && k.Name.ToLower() == lower))
                throw new ArgumentException($"A kingdom named '{name}' already exists.", nameof(dto));
            return name;
        }

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static KingdomDto ToDto(Kingdom k, Kingdom? withRelations) => new()
        {
            Id = k.Id,
            Name = k.Name,
            Description = k.Description,
            CreatedAt = k.CreatedAt,
            Towns = withRelations?.Towns.OrderBy(t => t.Name).Select(t => new TerritoryNavDto { Id = t.Id, Name = t.Name }).ToList(),
            Provinces = withRelations?.Provinces.OrderBy(p => p.Name).Select(p => new TerritoryNavDto { Id = p.Id, Name = p.Name }).ToList(),
            CrownLands = withRelations?.CrownLands.OrderBy(c => c.Name).Select(c => new TerritoryNavDto { Id = c.Id, Name = c.Name }).ToList(),
        };
    }
}
