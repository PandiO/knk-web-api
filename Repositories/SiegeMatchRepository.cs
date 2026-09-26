using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    // Siege Phase 6 (docs/specs/siege-minigame/DESIGN.md §3.10, §7.6): match history rows written by
    // the plugin's lifecycle checkpoints.
    public class SiegeMatchRepository : ISiegeMatchRepository
    {
        private readonly KnKDbContext _context;

        public SiegeMatchRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<SiegeMatch?> GetByIdAsync(int id, bool includeUsers = true)
        {
            var query = _context.SiegeMatches
                .Include(m => m.SiegeLobby)
                .Include(m => m.SiegeScenario)
                .Include(m => m.ObjectiveResults)
                .AsQueryable();
            query = includeUsers
                ? query.Include(m => m.Participants).ThenInclude(p => p.User)
                : query.Include(m => m.Participants);
            return await query.AsSplitQuery().FirstOrDefaultAsync(m => m.Id == id);
        }

        public async Task<SiegeScenario?> GetScenarioAsync(int siegeScenarioId)
        {
            return await _context.SiegeScenarios
                .Include(s => s.Teams)
                .Include(s => s.Objectives)
                .AsSplitQuery()
                .FirstOrDefaultAsync(s => s.Id == siegeScenarioId);
        }

        public async Task<bool> LobbyExistsAsync(int siegeLobbyId)
        {
            return await _context.SiegeLobbies.AnyAsync(l => l.Id == siegeLobbyId);
        }

        public async Task<List<User>> GetUsersAsync(IEnumerable<int> userIds)
        {
            var ids = userIds.Distinct().ToList();
            if (ids.Count == 0) return new List<User>();
            return await _context.Users.Where(u => ids.Contains(u.Id)).ToListAsync();
        }

        public async Task<HashSet<int>> GetExistingUserIdsAsync(IEnumerable<int> userIds)
        {
            var ids = userIds.Distinct().ToList();
            if (ids.Count == 0) return new HashSet<int>();
            var found = await _context.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => u.Id).ToListAsync();
            return found.ToHashSet();
        }

        public async Task<List<SiegeMatch>> QueryAsync(int? userId, int? siegeLobbyId, SiegeMatchStatus? status, int limit)
        {
            var query = _context.SiegeMatches.AsQueryable();
            if (userId.HasValue) query = query.Where(m => m.Participants.Any(p => p.UserId == userId.Value));
            if (siegeLobbyId.HasValue) query = query.Where(m => m.SiegeLobbyId == siegeLobbyId.Value);
            if (status.HasValue) query = query.Where(m => m.Status == status.Value);

            return await query
                .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
                .Take(limit)
                .Include(m => m.SiegeLobby)
                .Include(m => m.SiegeScenario)
                .Include(m => m.Participants).ThenInclude(p => p.User)
                .AsSplitQuery()
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<List<int>> GetUnfinishedIdsAsync()
        {
            return await _context.SiegeMatches
                .Where(m => m.Status == SiegeMatchStatus.Created || m.Status == SiegeMatchStatus.InProgress)
                .OrderBy(m => m.Id)
                .Select(m => m.Id)
                .ToListAsync();
        }

        public async Task AddAsync(SiegeMatch match)
        {
            await _context.SiegeMatches.AddAsync(match);
            await _context.SaveChangesAsync();
        }

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }

        public async Task<T> RunLockedAsync<T>(int matchId, Func<Task<T>> work)
        {
            if (!_context.Database.IsRelational())
            {
                return await work();
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            try
            {
                // Taken before anything in the transaction reads the match, so the status read
                // below is the committed one and a concurrent call waits here.
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT Id FROM siege_matches WHERE Id = {matchId} FOR UPDATE");
                var result = await work();
                await transaction.CommitAsync();
                return result;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<List<GateStructure>> GetGateStructuresWithDoorsAsync(IEnumerable<int> gateStructureIds)
        {
            var ids = gateStructureIds.Distinct().ToList();
            if (ids.Count == 0) return new List<GateStructure>();
            return await _context.GateStructures.Include(g => g.GateDoors).Where(g => ids.Contains(g.Id)).ToListAsync();
        }

        public async Task<List<GateStructure>> GetGatesInSiegeAsync()
        {
            return await _context.GateStructures.Include(g => g.GateDoors).Where(g => g.CurrentSiegeId != null).ToListAsync();
        }

        public async Task<List<SiegeMatchGateSnapshot>> GetGateSnapshotsAsync(int? siegeMatchId)
        {
            var query = _context.SiegeMatchGateSnapshots.AsQueryable();
            if (siegeMatchId.HasValue) query = query.Where(s => s.SiegeMatchId == siegeMatchId.Value);
            return await query.OrderBy(s => s.Id).ToListAsync();
        }

        public async Task<Dictionary<int, SiegeMatchStatus>> GetMatchStatusesAsync(IEnumerable<int> siegeMatchIds)
        {
            var ids = siegeMatchIds.Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<int, SiegeMatchStatus>();
            return await _context.SiegeMatches.Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Status);
        }

        public void AddGateSnapshot(SiegeMatchGateSnapshot snapshot)
        {
            _context.SiegeMatchGateSnapshots.Add(snapshot);
        }

        public void RemoveGateSnapshots(IEnumerable<SiegeMatchGateSnapshot> snapshots)
        {
            _context.SiegeMatchGateSnapshots.RemoveRange(snapshots);
        }

        public async Task LockUsersAsync(IEnumerable<int> userIds)
        {
            var ids = userIds.Distinct().OrderBy(id => id).ToList();
            if (ids.Count == 0 || !_context.Database.IsRelational()) return;
            // ints only, so the joined list can't inject anything.
#pragma warning disable EF1002
            await _context.Database.ExecuteSqlRawAsync(
                $"SELECT Id FROM users WHERE Id IN ({string.Join(",", ids)}) FOR UPDATE");
#pragma warning restore EF1002
        }
    }
}
