using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class GradeRepository : IGradeRepository
    {
        private readonly KnKDbContext _context;

        public GradeRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Grade>> GetAllAsync()
        {
            return await _context.Grades.ToListAsync();
        }

        public async Task<Grade?> GetByIdAsync(int id)
        {
            return await _context.Grades.FirstOrDefaultAsync(g => g.Id == id);
        }

        public async Task AddAsync(Grade entity)
        {
            await _context.Grades.AddAsync(entity);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(Grade entity)
        {
            _context.Grades.Update(entity);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _context.Grades.FindAsync(id);
            if (entity != null)
            {
                _context.Grades.Remove(entity);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<PagedResult<Grade>> SearchAsync(PagedQuery query)
        {
            var queryable = _context.Grades.AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(g => g.Name.ToLower().Contains(searchLower));
            }

            var totalCount = await queryable.CountAsync();

            queryable = query.SortBy switch
            {
                "name" => query.SortDescending ? queryable.OrderByDescending(g => g.Name) : queryable.OrderBy(g => g.Name),
                "stars" => query.SortDescending ? queryable.OrderByDescending(g => g.Stars) : queryable.OrderBy(g => g.Stars),
                _ => query.SortDescending ? queryable.OrderByDescending(g => g.Id) : queryable.OrderBy(g => g.Id)
            };

            var items = await queryable
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();

            return new PagedResult<Grade>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
        }
    }
}
