using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class BannerDesignRepository : IBannerDesignRepository
    {
        private readonly KnKDbContext _context;

        public BannerDesignRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<BannerDesign>> GetAllAsync()
        {
            return await _context.BannerDesigns
                .Include(d => d.Layers)
                .OrderBy(d => d.Name)
                .ToListAsync();
        }

        public async Task<BannerDesign?> GetByIdAsync(int id)
        {
            return await _context.BannerDesigns
                .Include(d => d.Layers)
                .FirstOrDefaultAsync(d => d.Id == id);
        }

        public async Task AddAsync(BannerDesign entity)
        {
            await _context.BannerDesigns.AddAsync(entity);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(BannerDesign entity)
        {
            // Loaded entities are already tracked; Update() would needlessly mark the whole
            // loaded graph Modified.
            if (_context.Entry(entity).State == EntityState.Detached) _context.BannerDesigns.Update(entity);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _context.BannerDesigns.FindAsync(id);
            if (entity != null)
            {
                _context.BannerDesigns.Remove(entity);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<PagedResult<BannerDesign>> SearchAsync(PagedQuery query)
        {
            var queryable = _context.BannerDesigns.AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(d => d.Name.ToLower().Contains(searchLower));
            }

            var totalCount = await queryable.CountAsync();

            queryable = query.SortBy switch
            {
                "name" => query.SortDescending ? queryable.OrderByDescending(d => d.Name) : queryable.OrderBy(d => d.Name),
                _ => query.SortDescending ? queryable.OrderByDescending(d => d.Id) : queryable.OrderBy(d => d.Id)
            };

            var items = await queryable
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .Include(d => d.Layers)
                .ToListAsync();

            return new PagedResult<BannerDesign>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
        }

        public async Task<bool> IsReferencedByClanAsync(int bannerDesignId)
        {
            return await _context.Clans.AnyAsync(c => c.BannerDesignId == bannerDesignId);
        }

        public async Task<bool> IsReferencedBySiegeTeamAsync(int bannerDesignId)
        {
            return await _context.SiegeTeams.AnyAsync(t => t.BannerDesignId == bannerDesignId);
        }

        public async Task<BannerLayer?> GetLayerByIdAsync(int id)
        {
            return await _context.BannerLayers.FirstOrDefaultAsync(l => l.Id == id);
        }

        public async Task<List<BannerLayer>> GetLayersAsync(int bannerDesignId)
        {
            return await _context.BannerLayers
                .Where(l => l.BannerDesignId == bannerDesignId)
                .OrderBy(l => l.SortOrder).ThenBy(l => l.Id)
                .ToListAsync();
        }

        public async Task AddLayerAsync(BannerLayer layer)
        {
            await _context.BannerLayers.AddAsync(layer);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateLayerAsync(BannerLayer layer)
        {
            // Loaded entities are already tracked; Update() would needlessly mark the whole
            // loaded graph Modified.
            if (_context.Entry(layer).State == EntityState.Detached) _context.BannerLayers.Update(layer);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteLayerAsync(BannerLayer layer)
        {
            _context.BannerLayers.Remove(layer);
            await _context.SaveChangesAsync();
        }
    }
}
