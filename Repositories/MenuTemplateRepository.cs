using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class MenuTemplateRepository : IMenuTemplateRepository
    {
        private readonly KnKDbContext _context;

        public MenuTemplateRepository(KnKDbContext context)
        {
            _context = context;
        }

        private IQueryable<MenuTemplate> FullTree() =>
            _context.MenuTemplates
                .Include(m => m.Sections).ThenInclude(s => s.VariableBindings)
                .Include(m => m.Sections).ThenInclude(s => s.Items).ThenInclude(i => i.VariableBindings)
                .Include(m => m.Sections).ThenInclude(s => s.Items).ThenInclude(i => i.Actions).ThenInclude(a => a.Conditions)
                .Include(m => m.Sections).ThenInclude(s => s.Items).ThenInclude(i => i.Conditions);

        public async Task<IEnumerable<MenuTemplate>> GetAllAsync()
        {
            return await FullTree().ToListAsync();
        }

        public async Task<MenuTemplate?> GetByIdAsync(int id)
        {
            return await FullTree().FirstOrDefaultAsync(m => m.Id == id);
        }

        public async Task<MenuTemplate?> GetByKeyAsync(string key)
        {
            return await FullTree().FirstOrDefaultAsync(m => m.Key == key);
        }

        public async Task AddAsync(MenuTemplate entity)
        {
            await _context.MenuTemplates.AddAsync(entity);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(MenuTemplate entity)
        {
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _context.MenuTemplates.FindAsync(id);
            if (entity != null)
            {
                _context.MenuTemplates.Remove(entity);
                await _context.SaveChangesAsync();
            }
        }
    }
}
