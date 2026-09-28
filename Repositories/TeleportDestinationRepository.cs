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

        public Task<bool> IsFeeKeyVoidAsync(string idempotencyKey) =>
            _context.TeleportFeeVoids.AsNoTracking().AnyAsync(v => v.IdempotencyKey == idempotencyKey);

        public async Task<bool> VoidFeeKeyAsync(TeleportFeeVoid marker)
        {
            if (await IsFeeKeyVoidAsync(marker.IdempotencyKey))
            {
                return false;
            }
            _context.TeleportFeeVoids.Add(marker);
            try
            {
                await _context.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException)
            {
                // The primary key: another request voided it in between. Anything else is real.
                _context.Entry(marker).State = EntityState.Detached;
                if (await IsFeeKeyVoidAsync(marker.IdempotencyKey))
                {
                    return false;
                }
                throw;
            }
        }

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
