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
    public interface ICrownLandService
    {
        Task<IEnumerable<CrownLandDto>> GetAllAsync();
        Task<CrownLandDto?> GetByIdAsync(int id);
        Task<CrownLandDto> CreateAsync(CrownLandDto dto);
        Task UpdateAsync(int id, CrownLandDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<CrownLandListDto>> SearchAsync(PagedQueryDto query);
    }

    /// <summary>
    /// Crown land (territory DESIGN § 2, G10): a domain with a WorldGuard region owned by a kingdom, saved like a
    /// district: the world is resolved from the request, the region task or the location (KNG-111), and a region drawn
    /// through the world task gets its final name <c>domain_&lt;id&gt;</c> after the save. It has no parent region.
    /// </summary>
    public sealed class CrownLandService : ICrownLandService
    {
        private readonly KnKDbContext _db;
        private readonly IDomainWorldResolver _worlds;
        private readonly IDomainRegionNameFinalizer _regionNames;

        public CrownLandService(KnKDbContext db, IDomainWorldResolver worlds, IDomainRegionNameFinalizer regionNames)
        {
            _db = db;
            _worlds = worlds;
            _regionNames = regionNames;
        }

        public async Task<IEnumerable<CrownLandDto>> GetAllAsync()
        {
            var rows = await _db.CrownLands.AsNoTracking().Include(c => c.Kingdom).Include(c => c.SubRegions)
                .OrderBy(c => c.Name).ToListAsync();
            return rows.Select(ToDto);
        }

        public async Task<CrownLandDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var c = await _db.CrownLands.AsNoTracking().Include(x => x.Kingdom).Include(x => x.SubRegions)
                .FirstOrDefaultAsync(x => x.Id == id);
            return c == null ? null : ToDto(c);
        }

        public async Task<CrownLandDto> CreateAsync(CrownLandDto dto)
        {
            await ValidateAsync(dto);
            string world = await _worlds.ResolveAsync(WorldRequest(dto, null));
            var c = new CrownLand { CreatedAt = DateTime.UtcNow };
            Apply(c, dto, world);
            _db.CrownLands.Add(c);
            await _db.SaveChangesAsync();
            await _regionNames.FinalizeAsync(c);
            await _db.Entry(c).Reference(x => x.Kingdom).LoadAsync();
            return ToDto(c);
        }

        public async Task UpdateAsync(int id, CrownLandDto dto)
        {
            var c = await _db.CrownLands.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new KeyNotFoundException($"Crown land with id {id} not found.");
            await ValidateAsync(dto);
            string previousRegionId = c.WgRegionId;
            string world = await _worlds.ResolveAsync(WorldRequest(dto, id));
            Apply(c, dto, world);
            await _db.SaveChangesAsync();
            await _regionNames.FinalizeAsync(c, previousRegionId);
        }

        public async Task DeleteAsync(int id)
        {
            var c = await _db.CrownLands.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new KeyNotFoundException($"Crown land with id {id} not found.");
            _db.CrownLands.Remove(c);
            await _db.SaveChangesAsync();
        }

        public async Task<PagedResultDto<CrownLandListDto>> SearchAsync(PagedQueryDto query)
        {
            query ??= new PagedQueryDto();
            var q = _db.CrownLands.AsNoTracking();
            if (TerritoryPaging.Term(query) is { } term) q = q.Where(c => c.Name.ToLower().Contains(term));
            if (TerritoryPaging.IntFilter(query, "kingdomId") is { } kingdomId) q = q.Where(c => c.KingdomId == kingdomId);
            q = (query.SortBy?.ToLowerInvariant()) switch
            {
                "name" => query.SortDescending ? q.OrderByDescending(c => c.Name) : q.OrderBy(c => c.Name),
                _ => query.SortDescending ? q.OrderByDescending(c => c.Id) : q.OrderBy(c => c.Id),
            };
            return await TerritoryPaging.PageAsync(q.Select(c => new CrownLandListDto
            {
                Id = c.Id,
                Name = c.Name,
                WgRegionId = c.WgRegionId,
                WorldName = c.WorldName,
                KingdomId = c.KingdomId,
                KingdomName = c.Kingdom.Name,
                Range = c.Range,
            }), query);
        }

        private async Task ValidateAsync(CrownLandDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            DomainNameRules.Normalize(dto, "Crown land", nameof(dto));
            dto.Description ??= string.Empty;
            if (string.IsNullOrWhiteSpace(dto.WgRegionId)) throw new ArgumentException("WgRegionId is required.", nameof(dto));
            if (dto.Range < 0 || dto.Range > CrownLand.MaxRange)
                throw new ArgumentException($"Range must be between 0 and {CrownLand.MaxRange} blocks.", nameof(dto));
            if (dto.KingdomId <= 0 || !await _db.Kingdoms.AnyAsync(k => k.Id == dto.KingdomId))
                throw new ArgumentException($"Kingdom with id {dto.KingdomId} not found: crown land belongs to a kingdom.", nameof(dto));
            if (dto.LocationId.HasValue && !await _db.Locations.AnyAsync(l => l.Id == dto.LocationId.Value))
                throw new ArgumentException($"Location with id {dto.LocationId} not found.", nameof(dto));
        }

        private static void Apply(CrownLand c, CrownLandDto dto, string world)
        {
            c.Name = dto.Name;
            c.Description = dto.Description;
            c.AllowEntry = dto.AllowEntry ?? true;
            c.AllowExit = dto.AllowExit ?? true;
            c.WgRegionId = dto.WgRegionId.Trim();
            c.WorldName = world;
            c.LocationId = dto.LocationId;
            c.KingdomId = dto.KingdomId;
            c.Range = dto.Range;
        }

        private static DomainWorldRequest WorldRequest(CrownLandDto dto, int? id) => new()
        {
            DomainId = id,
            RequestedWorld = dto.WorldName,
            WgRegionId = dto.WgRegionId,
            LocationId = dto.LocationId,
        };

        private static CrownLandDto ToDto(CrownLand c) => new()
        {
            Id = c.Id,
            Name = c.Name,
            Description = c.Description,
            CreatedAt = c.CreatedAt,
            AllowEntry = c.AllowEntry,
            AllowExit = c.AllowExit,
            WgRegionId = c.WgRegionId,
            WorldName = c.WorldName,
            LocationId = c.LocationId,
            KingdomId = c.KingdomId,
            Range = c.Range,
            Kingdom = c.Kingdom == null ? null : new TerritoryNavDto { Id = c.Kingdom.Id, Name = c.Kingdom.Name },
            SubRegions = c.SubRegions?.OrderBy(r => r.Id)
                .Select(r => new DomainSubRegionDto { Id = r.Id, DomainId = r.DomainId, WgRegionId = r.WgRegionId, Name = r.Name })
                .ToList(),
        };
    }
}
