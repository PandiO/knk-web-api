using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    // Siege Survival phase 5 (PandiO/knk-workspace#41, link S8a): the result, player result and checkpoint rows.
    public class SurvivalMatchResultRepository : ISurvivalMatchResultRepository
    {
        private readonly KnKDbContext _context;

        public SurvivalMatchResultRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<SiegeSurvivalSettings?> GetSurvivalSettingsAsync(int siegeScenarioId) =>
            await _context.SiegeSurvivalSettings.AsNoTracking().FirstOrDefaultAsync(s => s.SiegeScenarioId == siegeScenarioId);

        public async Task<SurvivalMatchResult?> GetResultAsync(int siegeMatchId) =>
            await _context.SurvivalMatchResults.Include(r => r.Players).FirstOrDefaultAsync(r => r.SiegeMatchId == siegeMatchId);

        public async Task<(List<SurvivalMatchResult> Items, int TotalCount)> QueryResultsAsync(int? siegeScenarioId, int? userId, int page, int pageSize)
        {
            var query = _context.SurvivalMatchResults.AsNoTracking().AsQueryable();
            if (siegeScenarioId.HasValue) query = query.Where(r => r.SiegeScenarioId == siegeScenarioId.Value);
            if (userId.HasValue) query = query.Where(r => r.Players.Any(p => p.UserId == userId.Value));
            var total = await query.CountAsync();
            var items = await query
                .Include(r => r.Players)
                .OrderByDescending(r => r.EndedAt).ThenByDescending(r => r.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .ToListAsync();
            return (items, total);
        }

        public async Task AddResultAsync(SurvivalMatchResult result)
        {
            _context.SurvivalMatchResults.Add(result);
            await _context.SaveChangesAsync();
        }

        public async Task<SurvivalMatchCheckpoint?> GetCheckpointAsync(int siegeMatchId) =>
            await _context.SurvivalMatchCheckpoints.FirstOrDefaultAsync(c => c.SiegeMatchId == siegeMatchId);

        public async Task AddCheckpointAsync(SurvivalMatchCheckpoint checkpoint)
        {
            _context.SurvivalMatchCheckpoints.Add(checkpoint);
            await _context.SaveChangesAsync();
        }

        public Task SaveChangesAsync() => _context.SaveChangesAsync();
    }
}
