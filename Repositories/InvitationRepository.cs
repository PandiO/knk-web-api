using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public interface IInvitationRepository
    {
        Task<Invitation?> GetByIdAsync(int id);
        Task<Invitation?> GetByHashAsync(InvitationKind kind, string codeHash);
        Task<List<Invitation>> GetActivePersonalForEmailAsync(string email, DateTime now);
        Task<(List<Invitation> Items, int Total)> SearchAsync(InvitationStatus? status, InvitationKind? kind, DateTime now, int skip, int take);
        Task<List<InvitationRedemption>> GetRedemptionsAsync(int invitationId);
        Task AddAsync(Invitation invitation);
        Task AddRedemptionAsync(InvitationRedemption redemption);
        Task SaveAsync();

        /// <summary>Adds a permanent membership unless the user already has one in that group.</summary>
        Task AddMembershipAsync(int userId, int groupId);

        /// <summary>
        /// Takes one use in a single conditional UPDATE (DESIGN.md § 3.4): only while the invite is not
        /// revoked, paused, expired or exhausted. False when another sign-up took the last use first.
        /// </summary>
        Task<bool> TryTakeUseAsync(int invitationId, DateTime now);

        /// <summary>Runs <paramref name="work"/> in one transaction (commit on return, roll back on throw);
        /// joins a transaction that is already open.</summary>
        Task<T> RunInTransactionAsync<T>(Func<Task<T>> work);
    }

    public class InvitationRepository : IInvitationRepository
    {
        private readonly KnKDbContext _context;

        public InvitationRepository(KnKDbContext context)
        {
            _context = context;
        }

        public Task<Invitation?> GetByIdAsync(int id) =>
            _context.Invitations.Include(i => i.GrantGroup).FirstOrDefaultAsync(i => i.Id == id);

        public Task<Invitation?> GetByHashAsync(InvitationKind kind, string codeHash) =>
            _context.Invitations.Include(i => i.GrantGroup).FirstOrDefaultAsync(i => i.Kind == kind && i.CodeHash == codeHash);

        public Task<List<Invitation>> GetActivePersonalForEmailAsync(string email, DateTime now) =>
            _context.Invitations
                .Where(i => i.Kind == InvitationKind.Personal && i.Email == email && i.RevokedAt == null
                    && i.UseCount < i.MaxUses && i.ExpiresAt > now)
                .ToListAsync();

        public async Task<(List<Invitation> Items, int Total)> SearchAsync(InvitationStatus? status, InvitationKind? kind, DateTime now, int skip, int take)
        {
            var query = _context.Invitations.AsNoTracking().Include(i => i.GrantGroup).AsQueryable();
            if (kind.HasValue)
            {
                query = query.Where(i => i.Kind == kind.Value);
            }
            // The same precedence as Invitation.StatusAt.
            query = status switch
            {
                InvitationStatus.Revoked => query.Where(i => i.RevokedAt != null),
                InvitationStatus.Exhausted => query.Where(i => i.RevokedAt == null && i.UseCount >= i.MaxUses),
                InvitationStatus.Expired => query.Where(i => i.RevokedAt == null && i.UseCount < i.MaxUses && i.ExpiresAt <= now),
                InvitationStatus.Paused => query.Where(i => i.RevokedAt == null && i.UseCount < i.MaxUses && i.ExpiresAt > now && i.PausedAt != null),
                InvitationStatus.Active => query.Where(i => i.RevokedAt == null && i.UseCount < i.MaxUses && i.ExpiresAt > now && i.PausedAt == null),
                _ => query
            };
            var total = await query.CountAsync();
            var items = await query.OrderByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id).Skip(skip).Take(take).ToListAsync();
            return (items, total);
        }

        public Task<List<InvitationRedemption>> GetRedemptionsAsync(int invitationId) =>
            _context.InvitationRedemptions.AsNoTracking()
                .Where(r => r.InvitationId == invitationId)
                .OrderByDescending(r => r.RedeemedAt)
                .ToListAsync();

        public async Task AddAsync(Invitation invitation)
        {
            _context.Invitations.Add(invitation);
            await _context.SaveChangesAsync();
        }

        public async Task AddRedemptionAsync(InvitationRedemption redemption)
        {
            _context.InvitationRedemptions.Add(redemption);
            await _context.SaveChangesAsync();
        }

        public Task SaveAsync() => _context.SaveChangesAsync();

        public async Task AddMembershipAsync(int userId, int groupId)
        {
            if (await _context.UserPermissionGroups.AnyAsync(m => m.UserId == userId && m.PermissionGroupId == groupId))
            {
                return;
            }
            _context.UserPermissionGroups.Add(new UserPermissionGroup { UserId = userId, PermissionGroupId = groupId, ExpiresAt = null });
            await _context.SaveChangesAsync();
        }

        public async Task<bool> TryTakeUseAsync(int invitationId, DateTime now)
        {
            if (_context.Database.IsRelational())
            {
                // UPDATE ... SET UseCount = UseCount + 1 WHERE <still usable>: the database decides who
                // wins, the same pattern as LinkCodeRepository.TryMarkUsedAsync.
                var affected = await _context.Invitations
                    .Where(i => i.Id == invitationId && i.UseCount < i.MaxUses && i.RevokedAt == null
                        && i.PausedAt == null && i.ExpiresAt > now)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.UseCount, i => i.UseCount + 1));
                if (affected == 1)
                {
                    // A tracked copy would otherwise keep the old count for the rest of the request.
                    var tracked = _context.Invitations.Local.FirstOrDefault(i => i.Id == invitationId);
                    if (tracked != null)
                    {
                        await _context.Entry(tracked).ReloadAsync();
                    }
                }
                return affected == 1;
            }

            // EF InMemory (tests) has no ExecuteUpdate; it is single-threaded there anyway.
            var invitation = await _context.Invitations.FirstOrDefaultAsync(i => i.Id == invitationId);
            if (invitation == null || invitation.StatusAt(now) != InvitationStatus.Active)
            {
                return false;
            }
            invitation.UseCount++;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<T> RunInTransactionAsync<T>(Func<Task<T>> work)
        {
            if (!_context.Database.IsRelational() || _context.Database.CurrentTransaction != null)
            {
                return await work();
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            try
            {
                var result = await work();
                await transaction.CommitAsync();
                return result;
            }
            catch
            {
                await transaction.RollbackAsync();
                _context.ChangeTracker.Clear();
                throw;
            }
        }
    }
}
