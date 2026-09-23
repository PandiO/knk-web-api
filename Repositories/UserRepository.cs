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
            _context.Users.Update(user);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateUserCoinsAsync(int id, int coins)
        {
            var user = await _context.Users.FindAsync(id);
            if (user != null)
            {
                user.Coins = coins;
                _context.Users.Update(user);
                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdateUserCoinsByUuidAsync(string uuid, int coins)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Uuid == uuid);
            if (user != null)
            {
                user.Coins = coins;
                _context.Users.Update(user);
                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdateGatePassThroughMethodAsync(int id, GatePassThroughMethod method)
        {
            var user = await _context.Users.FindAsync(id);
            if (user != null)
            {
                user.GatePassThroughMethodDefault = method;
                _context.Users.Update(user);
                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdateActiveModeAsync(int id, ActiveMode mode)
        {
            var user = await _context.Users.FindAsync(id);
            if (user != null)
            {
                user.ActiveMode = mode;
                _context.Users.Update(user);
                await _context.SaveChangesAsync();
            }
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
                _context.Users.Update(user);
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
                _context.Users.Update(user);
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

                    _context.Users.Update(secondaryUser);
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
