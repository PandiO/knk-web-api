using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class TitleBracketRepository : ITitleBracketRepository
    {
        private readonly KnKDbContext _context;

        public TitleBracketRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<List<TitleBracket>> GetAllOrderedByMinExperienceAsync()
        {
            return await _context.TitleBrackets
                .OrderBy(b => b.MinExperience)
                .ToListAsync();
        }
    }
}
