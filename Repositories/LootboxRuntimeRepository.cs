using System.Data;
using System.Globalization;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    // Spawns and claims (docs/specs/lootboxes/IMPLEMENTATION_PLAN.md Phase 2). The claim rules live in
    // LootboxRuntimeService; this class only reads and writes rows.
    public class LootboxRuntimeRepository : ILootboxRuntimeRepository
    {
        private readonly KnKDbContext _context;

        public LootboxRuntimeRepository(KnKDbContext context)
        {
            _context = context;
        }

        // ===== Transactions =====

        public async Task<T> InTransactionAsync<T>(Func<Task<T>> work)
        {
            var database = _context.Database;
            if (!database.IsRelational() || database.CurrentTransaction != null)
            {
                return await work();
            }

            await using var transaction = await database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            try
            {
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

        public void DiscardChanges() => _context.ChangeTracker.Clear();

        public Task SaveChangesAsync() => _context.SaveChangesAsync();

        // ===== Configuration, types, grades, areas =====

        public async Task<LootboxConfiguration?> GetConfigurationAsync()
        {
            return await _context.LootboxConfigurations.FirstOrDefaultAsync(c => c.Id == "global");
        }

        public async Task LockConfigurationAsync()
        {
            if (!_context.Database.IsRelational()) return;
            await _context.Database.ExecuteSqlRawAsync("SELECT Id FROM lootbox_configurations WHERE Id = 'global' FOR UPDATE");
        }

        private IQueryable<LootboxType> TypesWithIncludes() => _context.LootboxTypes
            .Include(t => t.Category)
                .ThenInclude(c => c.IconMaterialRef)
            .Include(t => t.DisplayMaterial)
            .Include(t => t.GradeWeights);

        public async Task<List<LootboxType>> GetEnabledTypesAsync()
        {
            return await TypesWithIncludes().Where(t => t.Enabled).OrderBy(t => t.Name).ToListAsync();
        }

        public async Task<LootboxType?> GetTypeAsync(int id)
        {
            return await TypesWithIncludes().FirstOrDefaultAsync(t => t.Id == id);
        }

        public async Task<List<Grade>> GetGradesAsync()
        {
            return await _context.Grades.AsNoTracking().OrderBy(g => g.Stars).ThenBy(g => g.Id).ToListAsync();
        }

        public async Task<List<LootboxSpawnArea>> GetAreasAsync()
        {
            return await _context.LootboxSpawnAreas.Include(a => a.AllowedTypes).OrderBy(a => a.Name).ToListAsync();
        }

        public async Task<LootboxSpawnArea?> GetAreaAsync(int id)
        {
            return await _context.LootboxSpawnAreas.Include(a => a.AllowedTypes).FirstOrDefaultAsync(a => a.Id == id);
        }

        // ===== Spawns =====

        public async Task<int> ExpireDueAsync(DateTime now)
        {
            var due = _context.LootboxSpawns.Where(s => s.Status == LootboxSpawnStatus.Active && s.ExpiresAt <= now);
            if (_context.Database.IsRelational())
            {
                // One UPDATE … WHERE Status='Active' AND ExpiresAt<=now; a claim racing it loses on its own
                // Status concurrency check.
                return await due.ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, LootboxSpawnStatus.Expired));
            }

            var rows = await due.ToListAsync();
            foreach (var spawn in rows) spawn.Status = LootboxSpawnStatus.Expired;
            await _context.SaveChangesAsync();
            return rows.Count;
        }

        public async Task<int> CountActiveAsync(int? areaId = null)
        {
            return await _context.LootboxSpawns.CountAsync(s =>
                s.Status == LootboxSpawnStatus.Active && (areaId == null || s.SpawnAreaId == areaId));
        }

        public async Task<Dictionary<int, int>> CountActiveByAreaAsync()
        {
            return await _context.LootboxSpawns
                .Where(s => s.Status == LootboxSpawnStatus.Active && s.SpawnAreaId != null)
                .GroupBy(s => s.SpawnAreaId!.Value)
                .Select(g => new { AreaId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.AreaId, x => x.Count);
        }

        private IQueryable<LootboxSpawn> SpawnsWithIncludes() => _context.LootboxSpawns
            .Include(s => s.LootboxType)
                .ThenInclude(t => t.Category)
            .Include(s => s.BoxGrade)
            .Include(s => s.SpawnArea);

        public async Task<List<LootboxSpawn>> GetActiveSpawnsAsync()
        {
            return await SpawnsWithIncludes().AsNoTracking()
                .Where(s => s.Status == LootboxSpawnStatus.Active)
                .OrderBy(s => s.SpawnedAt).ThenBy(s => s.Id)
                .ToListAsync();
        }

        public async Task<LootboxSpawn?> GetSpawnAsync(int id)
        {
            return await SpawnsWithIncludes().FirstOrDefaultAsync(s => s.Id == id);
        }

        public async Task<LootboxSpawnStatus?> GetSpawnStatusAsync(int id)
        {
            return await _context.LootboxSpawns.AsNoTracking()
                .Where(s => s.Id == id)
                .Select(s => (LootboxSpawnStatus?)s.Status)
                .FirstOrDefaultAsync();
        }

        public async Task AddSpawnAsync(LootboxSpawn spawn)
        {
            await _context.LootboxSpawns.AddAsync(spawn);
            await _context.SaveChangesAsync();
        }

        // ===== Claims =====

        private IQueryable<LootboxClaim> ClaimsWithIncludes() => _context.LootboxClaims
            .Include(c => c.User)
            .Include(c => c.LootboxType)
            .Include(c => c.BoxGrade)
            .Include(c => c.ItemGrade)
            .Include(c => c.ItemBlueprint)
                .ThenInclude(b => b.DefaultEnchantments)
                    .ThenInclude(e => e.EnchantmentDefinition)
            .Include(c => c.ItemInstance)
                .ThenInclude(i => i!.Enchantments)
                    .ThenInclude(e => e.EnchantmentDefinition)
            .Include(c => c.LootboxToken)
            .AsSplitQuery();

        public async Task<LootboxClaim?> GetClaimAsync(int id)
        {
            return await ClaimsWithIncludes().AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        }

        public async Task<LootboxClaim?> GetClaimByIdempotencyKeyAsync(string idempotencyKey)
        {
            return await _context.LootboxClaims.AsNoTracking().FirstOrDefaultAsync(c => c.IdempotencyKey == idempotencyKey);
        }

        public async Task<bool> SpawnHasClaimAsync(int spawnId)
        {
            return await _context.LootboxClaims.AnyAsync(c => c.LootboxSpawnId == spawnId);
        }

        public async Task<int> CountClaimsAsync(int userId, DateTime from, DateTime to, int? lootboxTypeId = null)
        {
            return await _context.LootboxClaims.CountAsync(c =>
                c.UserId == userId
                && (c.LootboxSpawnId != null || c.LootboxTokenId != null)
                && c.ClaimedAt >= from && c.ClaimedAt < to
                && (lootboxTypeId == null || c.LootboxTypeId == lootboxTypeId));
        }

        public void AddClaim(LootboxClaim claim)
        {
            _context.LootboxClaims.Add(claim);
        }

        public async Task<LootboxClaim?> GetClaimForUpdateAsync(int id)
        {
            return await _context.LootboxClaims.FirstOrDefaultAsync(c => c.Id == id);
        }

        public async Task<List<LootboxClaim>> GetPendingAsync(int userId, DateTime claimedAtOrBefore)
        {
            return await ClaimsWithIncludes().AsNoTracking()
                .Where(c => c.UserId == userId && c.DeliveredAt == null && c.ClaimedAt <= claimedAtOrBefore)
                .OrderBy(c => c.ClaimedAt).ThenBy(c => c.Id)
                .ToListAsync();
        }

        public async Task<PagedResult<LootboxClaim>> SearchClaimsAsync(PagedQuery query)
        {
            var queryable = _context.LootboxClaims.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(c =>
                    c.User.Username.ToLower().Contains(searchLower) ||
                    (c.ItemBlueprint.Name != null && c.ItemBlueprint.Name.ToLower().Contains(searchLower)));
            }

            if (query.Filters != null)
            {
                if (TryInt(query.Filters, "userId", out var userId))
                    queryable = queryable.Where(c => c.UserId == userId);
                if (TryInt(query.Filters, "lootboxTypeId", out var typeId))
                    queryable = queryable.Where(c => c.LootboxTypeId == typeId);
                if (TryInt(query.Filters, "itemGradeId", out var itemGradeId))
                    queryable = queryable.Where(c => c.ItemGradeId == itemGradeId);
                if (TryInt(query.Filters, "boxGradeId", out var boxGradeId))
                    queryable = queryable.Where(c => c.BoxGradeId == boxGradeId);
                if (TryBool(query.Filters, "isSpecial", out var isSpecial))
                    queryable = queryable.Where(c => c.IsSpecial == isSpecial);
                if (TryBool(query.Filters, "delivered", out var delivered))
                    queryable = delivered ? queryable.Where(c => c.DeliveredAt != null) : queryable.Where(c => c.DeliveredAt == null);
                if (TryBool(query.Filters, "adminGive", out var adminGive))
                    queryable = adminGive
                        ? queryable.Where(c => c.LootboxSpawnId == null && c.LootboxTokenId == null)
                        : queryable.Where(c => c.LootboxSpawnId != null || c.LootboxTokenId != null);
                if (query.Filters.TryGetValue("source", out var source))
                {
                    queryable = source?.Trim().ToLowerInvariant() switch
                    {
                        // A world box is picked up as a token (DESIGN.md §3.8) and opened later: still a world drop.
                        "world" => queryable.Where(c => c.LootboxSpawnId != null
                            || (c.LootboxToken != null && c.LootboxToken.IssuedReason == LootboxTokenReason.WorldPickup)),
                        "token" => queryable.Where(c => c.LootboxToken != null && c.LootboxToken.IssuedReason != LootboxTokenReason.WorldPickup),
                        "admingive" => queryable.Where(c => c.LootboxSpawnId == null && c.LootboxTokenId == null),
                        _ => queryable,
                    };
                }
                if (TryDate(query.Filters, "from", out var from))
                    queryable = queryable.Where(c => c.ClaimedAt >= from);
                if (TryDate(query.Filters, "to", out var to))
                    queryable = queryable.Where(c => c.ClaimedAt < to);
            }

            var totalCount = await queryable.CountAsync();

            // Newest first unless asked otherwise: the drop log is read from the top.
            queryable = query.SortBy switch
            {
                "userId" => query.SortDescending ? queryable.OrderByDescending(c => c.UserId) : queryable.OrderBy(c => c.UserId),
                "lootboxTypeId" => query.SortDescending ? queryable.OrderByDescending(c => c.LootboxTypeId) : queryable.OrderBy(c => c.LootboxTypeId),
                "id" => query.SortDescending ? queryable.OrderByDescending(c => c.Id) : queryable.OrderBy(c => c.Id),
                "claimedAt" when !query.SortDescending => queryable.OrderBy(c => c.ClaimedAt).ThenBy(c => c.Id),
                _ => queryable.OrderByDescending(c => c.ClaimedAt).ThenByDescending(c => c.Id)
            };

            var items = await queryable
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .Include(c => c.User)
                .Include(c => c.LootboxType)
                .Include(c => c.BoxGrade)
                .Include(c => c.ItemGrade)
                .Include(c => c.ItemBlueprint)
                .Include(c => c.LootboxToken)
                .ToListAsync();

            return new PagedResult<LootboxClaim>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
        }

        private static bool TryInt(Dictionary<string, string> filters, string key, out int value)
        {
            value = 0;
            return filters.TryGetValue(key, out var raw) && int.TryParse(raw, out value);
        }

        private static bool TryBool(Dictionary<string, string> filters, string key, out bool value)
        {
            value = false;
            return filters.TryGetValue(key, out var raw) && bool.TryParse(raw, out value);
        }

        private static bool TryDate(Dictionary<string, string> filters, string key, out DateTime value)
        {
            value = default;
            if (!filters.TryGetValue(key, out var raw)) return false;
            if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out value))
                return false;
            value = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
            return true;
        }

        // ===== Token items (Phase 5) =====

        private IQueryable<LootboxToken> TokensWithIncludes() => _context.LootboxTokens
            .Include(t => t.LootboxType)
                .ThenInclude(lt => lt.Category)
            .Include(t => t.BoxGrade)
            .Include(t => t.IssuedToUser)
            .Include(t => t.RedeemedByUser)
            .Include(t => t.Claim);

        public async Task<LootboxToken?> GetTokenBySourceSpawnAsync(int spawnId)
        {
            return await TokensWithIncludes().AsNoTracking().FirstOrDefaultAsync(t => t.SourceSpawnId == spawnId);
        }

        public async Task<int> CountPickupsAsync(int userId, DateTime from, DateTime to, int? lootboxTypeId = null)
        {
            return await _context.LootboxTokens.CountAsync(t =>
                t.IssuedToUserId == userId
                && t.IssuedReason == LootboxTokenReason.WorldPickup
                && t.IssuedAt >= from && t.IssuedAt < to
                && (lootboxTypeId == null || t.LootboxTypeId == lootboxTypeId));
        }

        public async Task<Dictionary<Guid, LootboxTokenStatus>> GetTokenStatusesAsync(IEnumerable<Guid> tokens)
        {
            var list = tokens.Distinct().ToList();
            return await _context.LootboxTokens.AsNoTracking()
                .Where(t => list.Contains(t.Token))
                .ToDictionaryAsync(t => t.Token, t => t.Status);
        }

        public void AddTokens(IEnumerable<LootboxToken> tokens)
        {
            _context.LootboxTokens.AddRange(tokens);
        }

        public async Task<LootboxToken?> GetTokenAsync(Guid token)
        {
            return await TokensWithIncludes().FirstOrDefaultAsync(t => t.Token == token);
        }

        public async Task<int?> FindTokenIdAsync(Guid token)
        {
            return await _context.LootboxTokens.AsNoTracking()
                .Where(t => t.Token == token)
                .Select(t => (int?)t.Id)
                .FirstOrDefaultAsync();
        }

        public async Task<LootboxTokenStatus?> GetTokenStatusAsync(int id)
        {
            return await _context.LootboxTokens.AsNoTracking()
                .Where(t => t.Id == id)
                .Select(t => (LootboxTokenStatus?)t.Status)
                .FirstOrDefaultAsync();
        }

        public async Task<bool> TokenHasClaimAsync(int tokenId)
        {
            return await _context.LootboxClaims.AnyAsync(c => c.LootboxTokenId == tokenId);
        }

        public async Task<List<LootboxToken>> GetTokensByIssueKeyAsync(string issueKey)
        {
            return await TokensWithIncludes().AsNoTracking()
                .Where(t => t.IssueKey == issueKey)
                .OrderBy(t => t.IssueIndex)
                .ToListAsync();
        }

        public async Task<List<LootboxToken>> GetTokensAsync(IEnumerable<int> ids)
        {
            var idList = ids.ToList();
            return await TokensWithIncludes().AsNoTracking()
                .Where(t => idList.Contains(t.Id))
                .OrderBy(t => t.Id)
                .ToListAsync();
        }

        public async Task<List<LootboxToken>> GetUndeliveredTokensAsync(int userId)
        {
            return await TokensWithIncludes().AsNoTracking()
                .Where(t => t.IssuedToUserId == userId && t.Status == LootboxTokenStatus.Issued && t.DeliveredAt == null)
                .OrderBy(t => t.IssuedAt).ThenBy(t => t.Id)
                .ToListAsync();
        }

        public async Task<List<LootboxToken>> GetTokensForDeliveryAsync(int userId, IEnumerable<Guid> tokens)
        {
            var list = tokens.Distinct().ToList();
            return await _context.LootboxTokens
                .Where(t => t.IssuedToUserId == userId && t.DeliveredAt == null && list.Contains(t.Token))
                .ToListAsync();
        }

        public async Task<PagedResult<LootboxToken>> SearchTokensAsync(PagedQuery query)
        {
            var queryable = _context.LootboxTokens.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var term = query.SearchTerm.Trim();
                var searchLower = term.ToLower();
                queryable = Guid.TryParse(term, out var exact)
                    ? queryable.Where(t => t.Token == exact)
                    : queryable.Where(t =>
                        (t.IssuedToUser != null && t.IssuedToUser.Username.ToLower().Contains(searchLower)) ||
                        (t.RedeemedByUser != null && t.RedeemedByUser.Username.ToLower().Contains(searchLower)));
            }

            if (query.Filters != null)
            {
                if (TryInt(query.Filters, "userId", out var userId))
                    queryable = queryable.Where(t => t.IssuedToUserId == userId || t.RedeemedByUserId == userId);
                if (TryInt(query.Filters, "lootboxTypeId", out var typeId))
                    queryable = queryable.Where(t => t.LootboxTypeId == typeId);
                if (query.Filters.TryGetValue("status", out var rawStatus) && Enum.TryParse<LootboxTokenStatus>(rawStatus, true, out var status))
                    queryable = queryable.Where(t => t.Status == status);
                if (query.Filters.TryGetValue("reason", out var rawReason) && Enum.TryParse<LootboxTokenReason>(rawReason, true, out var reason))
                    queryable = queryable.Where(t => t.IssuedReason == reason);
                if (TryBool(query.Filters, "delivered", out var delivered))
                    queryable = delivered ? queryable.Where(t => t.DeliveredAt != null) : queryable.Where(t => t.DeliveredAt == null);
            }

            var totalCount = await queryable.CountAsync();
            queryable = query.SortBy switch
            {
                "issuedAt" when !query.SortDescending => queryable.OrderBy(t => t.IssuedAt).ThenBy(t => t.Id),
                "redeemedAt" => query.SortDescending
                    ? queryable.OrderByDescending(t => t.RedeemedAt).ThenByDescending(t => t.Id)
                    : queryable.OrderBy(t => t.RedeemedAt).ThenBy(t => t.Id),
                _ => queryable.OrderByDescending(t => t.IssuedAt).ThenByDescending(t => t.Id)
            };

            var items = await queryable
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .Include(t => t.LootboxType)
                    .ThenInclude(lt => lt.Category)
                .Include(t => t.BoxGrade)
                .Include(t => t.IssuedToUser)
                .Include(t => t.RedeemedByUser)
                .Include(t => t.Claim)
                .ToListAsync();

            return new PagedResult<LootboxToken>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
        }

        public async Task<ItemBlueprint?> GetBlueprintAsync(int id)
        {
            return await _context.ItemBlueprints.AsNoTracking()
                .Include(b => b.DefaultEnchantments)
                    .ThenInclude(e => e.EnchantmentDefinition)
                .FirstOrDefaultAsync(b => b.Id == id);
        }
    }
}
