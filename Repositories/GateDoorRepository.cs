using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class GateDoorRepository : IGateDoorRepository
    {
        private readonly KnKDbContext _context;

        public GateDoorRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<GateDoor>> GetByStructureIdAsync(int gateStructureId)
        {
            return await BuildDoorQuery()
                .Where(d => d.GateStructureId == gateStructureId)
                .ToListAsync();
        }

        public async Task<GateDoor?> GetByIdAsync(int id)
        {
            return await BuildDoorQuery()
                .FirstOrDefaultAsync(d => d.Id == id);
        }

        public async Task<GateDoor?> GetByIdWithSnapshotsAsync(int id)
        {
            return await BuildDoorQuery()
                .Include(d => d.BlockSnapshots)
                .Include(d => d.OpenedBlockSnapshots)
                .FirstOrDefaultAsync(d => d.Id == id);
        }

        public async Task AddAsync(GateDoor gateDoor)
        {
            await _context.Set<GateDoor>().AddAsync(gateDoor);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(GateDoor gateDoor)
        {
            _context.Set<GateDoor>().Update(gateDoor);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var gateDoor = await _context.Set<GateDoor>().FindAsync(id);
            if (gateDoor != null)
            {
                _context.Set<GateDoor>().Remove(gateDoor);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<IEnumerable<GateDoor>> GetActiveDoorsAsync()
        {
            return await BuildDoorQuery()
                .Where(d => d.IsActive)
                .ToListAsync();
        }

        public async Task<bool> IsDoorNameUniqueAsync(int gateStructureId, string name, int? excludeId = null)
        {
            var query = _context.Set<GateDoor>()
                .Where(d => d.GateStructureId == gateStructureId && d.Name == name);

            if (excludeId.HasValue)
            {
                query = query.Where(d => d.Id != excludeId.Value);
            }

            return !await query.AnyAsync();
        }

        public async Task<GateDoor?> FindDoorByRegionAsync(string regionId)
        {
            return await BuildDoorQuery()
                .FirstOrDefaultAsync(d =>
                    d.RegionClosedId == regionId ||
                    d.RegionOpenedId == regionId);
        }

        public async Task UpdateHealthAsync(int id, double newHealth)
        {
            var door = await _context.Set<GateDoor>().FindAsync(id);
            if (door != null)
            {
                door.HealthCurrent = newHealth;
                if (newHealth <= 0)
                {
                    door.IsDestroyed = true;
                }
                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdateStateAsync(int id, GateDoorOpenState openedState, bool isDestroyed)
        {
            var door = await _context.Set<GateDoor>().FindAsync(id);
            if (door != null)
            {
                // Respawning is the destroyed -> not destroyed transition; restore full health.
                bool isRespawning = door.IsDestroyed && !isDestroyed;

                door.OpenedState = openedState;
                door.IsDestroyed = isDestroyed;

                if (isRespawning)
                {
                    door.HealthCurrent = door.HealthMax;
                }

                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdateOperationalSettingsAsync(int id, bool isActive, bool isInvincible)
        {
            var door = await _context.Set<GateDoor>().FindAsync(id);
            if (door != null)
            {
                door.IsActive = isActive;
                door.IsInvincible = isInvincible;
                await _context.SaveChangesAsync();
            }
        }

        // Block snapshot operations
        public async Task<IEnumerable<GateBlockSnapshot>> GetBlockSnapshotsByDoorIdAsync(int gateDoorId)
        {
            return await _context.Set<GateBlockSnapshot>()
                .Where(bs => bs.GateDoorId == gateDoorId)
                .OrderBy(bs => bs.SortOrder)
                .ToListAsync();
        }

        public async Task AddBlockSnapshotAsync(GateBlockSnapshot snapshot)
        {
            await _context.Set<GateBlockSnapshot>().AddAsync(snapshot);
            await _context.SaveChangesAsync();
        }

        public async Task AddBlockSnapshotsAsync(IEnumerable<GateBlockSnapshot> snapshots)
        {
            await _context.Set<GateBlockSnapshot>().AddRangeAsync(snapshots);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteBlockSnapshotsByDoorIdAsync(int gateDoorId)
        {
            var snapshots = await _context.Set<GateBlockSnapshot>()
                .Where(bs => bs.GateDoorId == gateDoorId)
                .ToListAsync();

            _context.Set<GateBlockSnapshot>().RemoveRange(snapshots);
            await _context.SaveChangesAsync();
        }

        // Opened-block snapshot operations - mirrors the block snapshot operations above
        // exactly, for the separately-scanned fully-open shape. See ROTATION_GAP_FILL_DESIGN.md.
        public async Task<IEnumerable<GateOpenedBlockSnapshot>> GetOpenedBlockSnapshotsByDoorIdAsync(int gateDoorId)
        {
            return await _context.Set<GateOpenedBlockSnapshot>()
                .Where(bs => bs.GateDoorId == gateDoorId)
                .OrderBy(bs => bs.SortOrder)
                .ToListAsync();
        }

        public async Task AddOpenedBlockSnapshotAsync(GateOpenedBlockSnapshot snapshot)
        {
            await _context.Set<GateOpenedBlockSnapshot>().AddAsync(snapshot);
            await _context.SaveChangesAsync();
        }

        public async Task AddOpenedBlockSnapshotsAsync(IEnumerable<GateOpenedBlockSnapshot> snapshots)
        {
            await _context.Set<GateOpenedBlockSnapshot>().AddRangeAsync(snapshots);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteOpenedBlockSnapshotsByDoorIdAsync(int gateDoorId)
        {
            var snapshots = await _context.Set<GateOpenedBlockSnapshot>()
                .Where(bs => bs.GateDoorId == gateDoorId)
                .ToListAsync();

            _context.Set<GateOpenedBlockSnapshot>().RemoveRange(snapshots);
            await _context.SaveChangesAsync();
        }

        private IQueryable<GateDoor> BuildDoorQuery()
        {
            return _context.Set<GateDoor>()
                .Include(d => d.AnchorPoint)
                .Include(d => d.OpenAnchorPoint)
                .Include(d => d.ReferencePoint1)
                .Include(d => d.ReferencePoint2)
                .Include(d => d.HingeAxis)
                .Include(d => d.LeftDoorSeedBlock)
                .Include(d => d.RightDoorSeedBlock)
                .Include(d => d.InfoDisplayLocation)
                .Include(d => d.FallbackMaterial);
        }
    }
}
