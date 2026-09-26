using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class TeleportDestinationRepository : ITeleportDestinationRepository
    {
        private readonly KnKDbContext _context;

        public TeleportDestinationRepository(KnKDbContext context)
        {
            _context = context;
        }

        public Task<List<Domain>> GetEnabledAsync() =>
            WithRequirements().Where(d => d.TeleportEnabled).ToListAsync();

        public Task<Domain?> GetByIdAsync(int domainId) =>
            WithRequirements().FirstOrDefaultAsync(d => d.Id == domainId);

        // Domain is TPT, so a query on the base set materializes Towns, Districts, Structures and
        // GateStructures as their own types.
        private IQueryable<Domain> WithRequirements() =>
            _context.Domains
                .AsNoTracking()
                .Include(d => d.Location)
                .Include(d => d.TeleportMinTitleBracket)
                .Include(d => d.TeleportMinPremiumGroup);
    }
}
