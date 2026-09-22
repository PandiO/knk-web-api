using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Responsibility guidance (best practices):
/// - Repository: encapsulates data access only (CRUD, queries, EF Core usage). 
///   Do NOT contain business rules, orchestration, or HTTP concerns here.
/// - Service: contains business logic, validation beyond simple model validation, transaction orchestration,
///   and coordinates multiple repositories when needed.
/// - Controller: handles HTTP layer responsibilities (request/response mapping, model validation, auth),
///   calls services (not repositories) and shapes HTTP responses.
/// </summary>
namespace knkwebapi_v2.Repositories
{
    public class CategoryRepository : ICategoryRepository
    {
        private readonly KnKDbContext _context;

        public CategoryRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Category>> GetAllAsync()
        {
            return await _context.Categories
                .Include(c => c.ParentCategory)
                .Include(c => c.IconMaterialRef)
                .ToListAsync();
        }
        public async Task<Category?> GetByIdAsync(int id)
        {
            return await _context.Categories
                .Include(c => c.ParentCategory).ThenInclude(pc => pc.IconMaterialRef)
                .Include(c => c.ChildCategories).ThenInclude(cc => cc.IconMaterialRef)
                .Include(c => c.IconMaterialRef)
                .Include(c => c.Tags).ThenInclude(ct => ct.Tag)
                .FirstOrDefaultAsync(c => c.Id == id);
        }
        public async Task AddCategoryAsync(Category category)
        {
            await _context.Categories.AddAsync(category);
            await _context.SaveChangesAsync();
        }
        public async Task UpdateCategoryAsync(Category category)
        {
            _context.Categories.Update(category);
            await _context.SaveChangesAsync();
        }
        public async Task DeleteCategoryAsync(int id)
        {
            var category = await _context.Categories.FindAsync(id);
            if (category != null)
            {
                _context.Categories.Remove(category);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<bool> HasChildrenAsync(int id)
        {
            return await _context.Categories.AnyAsync(c => c.ParentCategoryId == id);
        }

        public async Task<IEnumerable<Category>> GetChildrenAsync(int id)
        {
            return await _context.Categories
                .Where(c => c.ParentCategoryId == id)
                .Include(c => c.IconMaterialRef)
                .ToListAsync();
        }

        public async Task<PagedResult<Category>> SearchAsync(PagedQuery query)
        {
            var queryable = _context.Categories.AsQueryable();

            // Apply search term filter
            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(c => c.Name.ToLower().Contains(searchLower));
            }

            // Apply filters from dictionary
            if (query.Filters != null)
            {
                if (query.Filters.TryGetValue("parentCategoryId", out var parentCategoryIdStr))
                {
                    if (int.TryParse(parentCategoryIdStr, out var parentCategoryId))
                    {
                        queryable = queryable.Where(c => c.ParentCategoryId == parentCategoryId);
                    }
                }

                if (query.Filters.TryGetValue("iconMaterialRefId", out var iconMaterialRefIdStr))
                {
                    if (int.TryParse(iconMaterialRefIdStr, out var iconMaterialRefId))
                    {
                        queryable = queryable.Where(c => c.IconMaterialRefId == iconMaterialRefId);
                    }
                }
                if (query.Filters.TryGetValue("excludeIds", out var excludeIdsStr))
                {
                    var excludeIds = excludeIdsStr.Split(',').Select(idStr => 
                    {
                        if (int.TryParse(idStr, out var id))
                            return id;
                        return (int?)null;
                    })
                    .Where(id => id.HasValue)
                    .Select(id => id!.Value)
                    .ToList();

                    if (excludeIds.Any())
                    {
                        queryable = queryable.Where(c => !excludeIds.Contains(c.Id));
                    }
                }
            }

            // Apply sorting
            queryable = ApplySorting(queryable, query.SortBy, query.SortDescending);

            // Get total count before paging
            var totalCount = await queryable.CountAsync();

            // Apply paging
            var items = await queryable
                .Include(c => c.ParentCategory)
                .Include(c => c.IconMaterialRef)
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();

            return new PagedResult<Category>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
        }

        private IQueryable<Category> ApplySorting(IQueryable<Category> queryable, string? sortBy, bool sortDescending)
        {
            if (string.IsNullOrWhiteSpace(sortBy))
            {
                return queryable.OrderBy(c => c.Name);
            }

            return sortBy.ToLower() switch
            {
                "name" => sortDescending ? queryable.OrderByDescending(c => c.Name) : queryable.OrderBy(c => c.Name),
                "id" => sortDescending ? queryable.OrderByDescending(c => c.Id) : queryable.OrderBy(c => c.Id),
                "iconmaterialrefid" => sortDescending ? queryable.OrderByDescending(c => c.IconMaterialRefId) : queryable.OrderBy(c => c.IconMaterialRefId),
                "parentcategoryid" => sortDescending ? queryable.OrderByDescending(c => c.ParentCategoryId) : queryable.OrderBy(c => c.ParentCategoryId),
                _ => queryable.OrderBy(c => c.Name)
            };
        }
    }
}