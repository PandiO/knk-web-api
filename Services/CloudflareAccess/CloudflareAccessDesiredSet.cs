using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.CloudflareAccess
{
    /// <summary>Who should be able to open the site, from the database (plan § 7.1).</summary>
    public interface ICloudflareAccessDesiredSet
    {
        /// <summary>Lower-cased, trimmed emails.</summary>
        Task<HashSet<string>> GetAsync(DateTime now, CancellationToken ct = default);
    }

    /// <summary>
    /// The desired set: the emails of personal invites that can still be used (not revoked, not
    /// used up, not expired), plus the active, non-deleted accounts with an email that were
    /// admitted (grandfathered or by staff) or came in through an invitation. Frozen players stay
    /// in: freezing is an in-game sanction (DESIGN.md § 5.8).
    /// </summary>
    public sealed class CloudflareAccessDesiredSet : ICloudflareAccessDesiredSet
    {
        private readonly KnKDbContext _db;

        public CloudflareAccessDesiredSet(KnKDbContext db)
        {
            _db = db;
        }

        public async Task<HashSet<string>> GetAsync(DateTime now, CancellationToken ct = default)
        {
            var invites = await _db.Invitations.AsNoTracking()
                .Where(i => i.Kind == InvitationKind.Personal && i.Email != null && i.RevokedAt == null
                    && i.UseCount < i.MaxUses && i.ExpiresAt > now)
                .Select(i => i.Email!)
                .ToListAsync(ct);
            var users = await _db.Users.AsNoTracking()
                .Where(u => u.IsActive && u.DeletedAt == null && u.Email != null
                    && (u.AdmittedAt != null || u.InvitedViaInvitationId != null))
                .Select(u => u.Email!)
                .ToListAsync(ct);

            var desired = new HashSet<string>(StringComparer.Ordinal);
            foreach (var email in invites.Concat(users))
            {
                var normalized = CloudflareAccessSync.Normalize(email);
                if (normalized.Length > 0) desired.Add(normalized);
            }
            return desired;
        }
    }
}
