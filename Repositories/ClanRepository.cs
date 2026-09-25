using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class ClanRepository : IClanRepository
    {
        private readonly KnKDbContext _context;

        public ClanRepository(KnKDbContext context)
        {
            _context = context;
        }

        private IQueryable<Clan> WithGraph() => _context.Clans
            .Include(c => c.BannerDesign)
                .ThenInclude(d => d.Layers)
            .Include(c => c.DefaultForTown);

        public async Task<IEnumerable<Clan>> GetAllAsync()
        {
            return await WithGraph().OrderBy(c => c.Name).ToListAsync();
        }

        public async Task<Clan?> GetByIdAsync(int id)
        {
            return await WithGraph().FirstOrDefaultAsync(c => c.Id == id);
        }

        public async Task<Clan?> GetDefaultForTownAsync(int townId)
        {
            return await WithGraph().FirstOrDefaultAsync(c => c.DefaultForTownId == townId);
        }

        public async Task AddAsync(Clan entity)
        {
            await _context.Clans.AddAsync(entity);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(Clan entity)
        {
            // Loaded entities are already tracked - Update() would also mark the shared
            // related rows (banner, town) Modified.
            if (_context.Entry(entity).State == EntityState.Detached) _context.Clans.Update(entity);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _context.Clans.FindAsync(id);
            if (entity != null)
            {
                _context.Clans.Remove(entity);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<PagedResult<Clan>> SearchAsync(PagedQuery query)
        {
            var queryable = _context.Clans.AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(c => c.Name.ToLower().Contains(searchLower));
            }

            var totalCount = await queryable.CountAsync();

            queryable = query.SortBy switch
            {
                "name" => query.SortDescending ? queryable.OrderByDescending(c => c.Name) : queryable.OrderBy(c => c.Name),
                _ => query.SortDescending ? queryable.OrderByDescending(c => c.Id) : queryable.OrderBy(c => c.Id)
            };

            var items = await queryable
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .Include(c => c.BannerDesign)
                .Include(c => c.DefaultForTown)
                .ToListAsync();

            return new PagedResult<Clan>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
        }

        public async Task<bool> TownExistsAsync(int townId)
        {
            return await _context.Towns.AnyAsync(t => t.Id == townId);
        }

        public async Task<bool> BannerDesignExistsAsync(int bannerDesignId)
        {
            return await _context.BannerDesigns.AnyAsync(d => d.Id == bannerDesignId);
        }

        public async Task<bool> IsUsedBySiegeTeamAsync(int clanId)
        {
            return await _context.SiegeTeams.AnyAsync(t => t.ClanId == clanId);
        }
    }
}
