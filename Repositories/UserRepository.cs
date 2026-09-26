using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly KnKDbContext _context;

        public UserRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<User>> GetAllAsync()
        {
            return await _context.Users.ToListAsync();
        }

        public async Task<User?> GetByIdAsync(int id)
        {
            return await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
        }

        public async Task<User?> GetByUuidAsync(string uuid)
        {
            return await _context.Users.FirstOrDefaultAsync(u => u.Uuid == uuid);
        }

        public async Task<User?> GetByUsernameAsync(string username)
        {
            return await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
        }

        public async Task AddUserAsync(User user)
        {
            await _context.Users.AddAsync(user);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateUserAsync(User user)
        {
            // A user loaded through this context is already tracked, so SaveChanges writes only
            // the columns that changed. _context.Users.Update() would mark every column modified
            // and write back the whole (possibly stale) row, which is how a presence or profile
            // write used to erase a salary payout or refund a kit purchase (DESIGN.md §1.4 A2).
            var entry = _context.Entry(user);
            if (entry.State == EntityState.Detached)
            {
                _context.Users.Update(user);
            }
            foreach (var column in BalanceColumns)
            {
                entry.Property(column).IsModified = false;
            }
            await _context.SaveChangesAsync();
        }

        /// <summary>Written only by the locked balance paths (SaveBalancesAsync).</summary>
        private static readonly string[] BalanceColumns =
        {
            nameof(User.Coins), nameof(User.Gems), nameof(User.ExperiencePoints), nameof(User.LastSalaryPayoutAt)
        };

        public async Task SaveBalancesAsync(User user)
        {
            if (_context.Entry(user).State == EntityState.Detached)
            {
                _context.Users.Update(user);
            }
            await _context.SaveChangesAsync();
        }

        public async Task RunWithUsersLockedAsync(IEnumerable<int> userIds, Func<Task> work)
        {
            var ids = userIds.Distinct().OrderBy(id => id).ToList();
            var isRelational = _context.Database.IsRelational();
            var ownsTransaction = isRelational && _context.Database.CurrentTransaction == null;

            await using var transaction = ownsTransaction
                ? await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted)
                : null;
            try
            {
                if (isRelational)
                {
                    await LockUsersAsync(ids);
                    // Anything read before the lock may be stale: re-read it now that no other
                    // balance write can run in between.
                    foreach (var entry in TrackedUsers(ids))
                    {
                        await entry.ReloadAsync();
                    }
                }

                await work();

                if (transaction != null)
                {
                    await transaction.CommitAsync();
                }
            }
            catch
            {
                if (transaction != null)
                {
                    await transaction.RollbackAsync();
                }
                // Changes the rolled-back work made to these users (saved or not) must not ride
                // along on a later SaveChanges in the same request.
                foreach (var entry in TrackedUsers(ids))
                {
                    if (entry.State == EntityState.Modified)
                    {
                        entry.CurrentValues.SetValues(entry.OriginalValues);
                        entry.State = EntityState.Unchanged;
                    }
                    if (isRelational)
                    {
                        try { await entry.ReloadAsync(); } catch { /* keep the original error */ }
                    }
                }
                throw;
            }
        }

        public async Task LockUsersAsync(IEnumerable<int> userIds)
        {
            var ids = userIds.Distinct().OrderBy(id => id).ToList();
            if (ids.Count == 0 || !_context.Database.IsRelational()) return;
            // ints only, so the joined list can't inject anything.
#pragma warning disable EF1002
            await _context.Database.ExecuteSqlRawAsync(
                $"SELECT Id FROM users WHERE Id IN ({string.Join(",", ids)}) ORDER BY Id FOR UPDATE");
#pragma warning restore EF1002
        }

        private List<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<User>> TrackedUsers(List<int> ids) =>
            _context.ChangeTracker.Entries<User>().Where(e => ids.Contains(e.Entity.Id)).ToList();

        public async Task UpdateGatePassThroughMethodAsync(int id, GatePassThroughMethod method)
        {
            var user = await _context.Users.FindAsync(id);
            if (user != null)
            {
                user.GatePassThroughMethodDefault = method;
                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdateActiveModeAsync(int id, ActiveMode mode)
        {
            var user = await _context.Users.FindAsync(id);
            if (user != null)
            {
                user.ActiveMode = mode;
                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdatePresenceAsync(int id, bool isOnline)
        {
            var user = await _context.Users.FindAsync(id);
            if (user != null)
            {
                user.IsOnline = isOnline;
                user.LastSeenAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }

        public async Task<IEnumerable<User>> SearchByGroupAsync(int groupId, bool? onlineOnly = null)
        {
            var queryable = _context.UserPermissionGroups
                .Where(m => m.PermissionGroupId == groupId)
                .Select(m => m.User);

            if (onlineOnly.HasValue)
            {
                queryable = queryable.Where(u => u.IsOnline == onlineOnly.Value);
            }

            return await queryable.Distinct().ToListAsync();
        }

        public async Task DeleteUserAsync(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user != null)
            {
                _context.Users.Remove(user);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<PagedResult<User>> SearchAsync(PagedQuery query)
        {
            var queryable = _context.Users.AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(u => u.Username.ToLower().Contains(searchLower) ||
                                                  (u.Email != null && u.Email.ToLower().Contains(searchLower)) ||
                                                  (u.Uuid != null && u.Uuid.ToLower().Contains(searchLower)));
            }

            if (query.Filters != null)
            {
                if (query.Filters.TryGetValue("id", out var idStr) && int.TryParse(idStr, out var id))
                {
                    queryable = queryable.Where(u => u.Id == id);
                }

                if (query.Filters.TryGetValue("uuid", out var uuid) && uuid != null)
                {
                    queryable = queryable.Where(u => u.Uuid != null && u.Uuid.ToLower().Contains(uuid.ToLower()));
                }

                if (query.Filters.TryGetValue("username", out var username) && username != null)
                {
                    queryable = queryable.Where(u => u.Username.ToLower().Contains(username.ToLower()));
                }

                if (query.Filters.TryGetValue("email", out var email) && email != null)
                {
                    queryable = queryable.Where(u => u.Email != null && u.Email.ToLower().Contains(email.ToLower()));
                }
            }

            queryable = ApplySorting(queryable, query.SortBy, query.SortDescending);

            var totalCount = await queryable.CountAsync();

            var items = await queryable
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();

            return new PagedResult<User>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
        }

        private IQueryable<User> ApplySorting(IQueryable<User> queryable, string? sortBy, bool sortDescending)
        {
            if (string.IsNullOrWhiteSpace(sortBy))
                return queryable.OrderBy(u => u.Username);

            return sortBy.ToLower() switch
            {
                "username" => sortDescending ? queryable.OrderByDescending(u => u.Username) : queryable.OrderBy(u => u.Username),
                "id" => sortDescending ? queryable.OrderByDescending(u => u.Id) : queryable.OrderBy(u => u.Id),
                "email" => sortDescending ? queryable.OrderByDescending(u => u.Email) : queryable.OrderBy(u => u.Email),
                "uuid" => sortDescending ? queryable.OrderByDescending(u => u.Uuid) : queryable.OrderBy(u => u.Uuid),
                "coins" => sortDescending ? queryable.OrderByDescending(u => u.Coins) : queryable.OrderBy(u => u.Coins),
                "createdat" => sortDescending ? queryable.OrderByDescending(u => u.CreatedAt) : queryable.OrderBy(u => u.CreatedAt),
                _ => queryable.OrderBy(u => u.Username)
            };
        }

        // ===== NEW METHODS: UNIQUE CONSTRAINT CHECKS =====

        public async Task<bool> IsUsernameTakenAsync(string username, int? excludeUserId = null)
        {
            var query = _context.Users.Where(u => u.Username.ToLower() == username.ToLower());
            if (excludeUserId.HasValue)
            {
                query = query.Where(u => u.Id != excludeUserId.Value);
            }
            return await query.AnyAsync();
        }

        public async Task<bool> IsEmailTakenAsync(string email, int? excludeUserId = null)
        {
            if (string.IsNullOrEmpty(email))
                return false;

            var query = _context.Users.Where(u => u.Email != null && u.Email.ToLower() == email.ToLower());
            if (excludeUserId.HasValue)
            {
                query = query.Where(u => u.Id != excludeUserId.Value);
            }
            return await query.AnyAsync();
        }

        public async Task<bool> IsUuidTakenAsync(string uuid, int? excludeUserId = null)
        {
            if (string.IsNullOrEmpty(uuid))
                return false;

            var query = _context.Users.Where(u => u.Uuid == uuid);
            if (excludeUserId.HasValue)
            {
                query = query.Where(u => u.Id != excludeUserId.Value);
            }
            return await query.AnyAsync();
        }

        // ===== NEW METHODS: FIND BY MULTIPLE CRITERIA =====

        public async Task<User?> GetByEmailAsync(string email)
        {
            return await _context.Users.FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == email.ToLower());
        }

        public async Task<User?> GetByUuidAndUsernameAsync(string uuid, string username)
        {
            return await _context.Users.FirstOrDefaultAsync(u => 
                u.Uuid == uuid && u.Username.ToLower() == username.ToLower());
        }

        // ===== NEW METHODS: CREDENTIALS & EMAIL UPDATES =====

        public async Task UpdatePasswordHashAsync(int id, string passwordHash)
        {
            var user = await _context.Users.FindAsync(id);
            if (user != null)
            {
                user.PasswordHash = passwordHash;
                user.LastPasswordChangeAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdateEmailAsync(int id, string email)
        {
            var user = await _context.Users.FindAsync(id);
            if (user != null)
            {
                user.Email = email;
                user.LastEmailChangeAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }

        // ===== NEW METHODS: MERGE & CONFLICT RESOLUTION =====

        public async Task<User?> FindDuplicateAsync(string uuid, string username)
        {
            return await _context.Users.FirstOrDefaultAsync(u => 
                u.Uuid == uuid && u.Username.ToLower() == username.ToLower());
        }

        public async Task MergeUsersAsync(int primaryUserId, int secondaryUserId)
        {
            using (var transaction = await _context.Database.BeginTransactionAsync())
            {
                try
                {
                    var primaryUser = await _context.Users.FindAsync(primaryUserId);
                    var secondaryUser = await _context.Users.FindAsync(secondaryUserId);

                    if (primaryUser == null || secondaryUser == null)
                        throw new InvalidOperationException("One or both users not found.");

                    // Soft delete the secondary user
                    secondaryUser.IsActive = false;
                    secondaryUser.DeletedAt = DateTime.UtcNow;
                    secondaryUser.DeletedReason = $"Merged with user {primaryUserId}";
                    secondaryUser.ArchiveUntil = DateTime.UtcNow.AddDays(90);
                    await _context.SaveChangesAsync();

                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
        }

        // ===== NEW METHODS: LINK CODE OPERATIONS =====

        public async Task<LinkCode> CreateLinkCodeAsync(LinkCode linkCode)
        {
            await _context.LinkCodes.AddAsync(linkCode);
            await _context.SaveChangesAsync();
            return linkCode;
        }

        public async Task<LinkCode?> GetLinkCodeByCodeAsync(string code)
        {
            return await _context.LinkCodes.FirstOrDefaultAsync(lc => lc.Code == code);
        }

        public async Task UpdateLinkCodeStatusAsync(int linkCodeId, LinkCodeStatus status)
        {
            var linkCode = await _context.LinkCodes.FindAsync(linkCodeId);
            if (linkCode != null)
            {
                linkCode.Status = status;
                if (status == LinkCodeStatus.Used)
                {
                    linkCode.UsedAt = DateTime.UtcNow;
                }
                _context.LinkCodes.Update(linkCode);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<IEnumerable<LinkCode>> GetExpiredLinkCodesAsync()
        {
            return await _context.LinkCodes
                .Where(lc => lc.ExpiresAt < DateTime.UtcNow)
                .ToListAsync();
        }
    }
}
