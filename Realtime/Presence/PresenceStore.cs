using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Realtime.Presence;

/// <summary>
/// Writes the hub's presence into the existing <c>User.IsOnline</c>/<c>LastSeenAt</c> columns (DESIGN §5), so the
/// moderation filters and DTOs keep working. The REST <c>PUT api/users/{id}/presence</c> stays for the hub-down case.
/// Matches users by UUID; players without a user row are skipped.
/// </summary>
public interface IPresenceStore
{
    /// <summary>Marks these players online; <c>LastSeenAt</c> moves only for rows that were offline.</summary>
    Task<int> SetOnlineAsync(IReadOnlyCollection<string> uuids, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>Marks these players offline with <c>LastSeenAt = now</c> (only rows that were online).</summary>
    Task<int> SetOfflineAsync(IReadOnlyCollection<string> uuids, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>
    /// The reconcile after a roster (DESIGN §5): exactly <paramref name="onlineUuids"/> are online, every other user
    /// still marked online (a crash, a reload, an API restart) is set offline. Returns the rows changed.
    /// </summary>
    Task<int> ReconcileAsync(IReadOnlySet<string> onlineUuids, DateTime now, CancellationToken cancellationToken = default);
}

public sealed class PresenceStore : IPresenceStore
{
    private readonly KnKDbContext _db;

    public PresenceStore(KnKDbContext db)
    {
        _db = db;
    }

    public async Task<int> SetOnlineAsync(IReadOnlyCollection<string> uuids, DateTime now, CancellationToken cancellationToken = default)
    {
        var keys = Normalize(uuids);
        if (keys.Count == 0) return 0;
        var users = await _db.Users.Where(u => u.Uuid != null && keys.Contains(u.Uuid)).ToListAsync(cancellationToken);
        var changed = 0;
        foreach (var user in users.Where(u => !u.IsOnline))
        {
            user.IsOnline = true;
            user.LastSeenAt = now;
            changed++;
        }
        if (changed > 0) await _db.SaveChangesAsync(cancellationToken);
        return changed;
    }

    public async Task<int> SetOfflineAsync(IReadOnlyCollection<string> uuids, DateTime now, CancellationToken cancellationToken = default)
    {
        var keys = Normalize(uuids);
        if (keys.Count == 0) return 0;
        var users = await _db.Users.Where(u => u.IsOnline && u.Uuid != null && keys.Contains(u.Uuid)).ToListAsync(cancellationToken);
        foreach (var user in users)
        {
            user.IsOnline = false;
            user.LastSeenAt = now;
        }
        if (users.Count > 0) await _db.SaveChangesAsync(cancellationToken);
        return users.Count;
    }

    public async Task<int> ReconcileAsync(IReadOnlySet<string> onlineUuids, DateTime now, CancellationToken cancellationToken = default)
    {
        var keys = Normalize(onlineUuids);
        var users = await _db.Users
            .Where(u => u.IsOnline || (u.Uuid != null && keys.Contains(u.Uuid)))
            .ToListAsync(cancellationToken);
        var changed = 0;
        foreach (var user in users)
        {
            var shouldBeOnline = user.Uuid != null && keys.Contains(PresenceRegistry.NormalizeUuid(user.Uuid));
            if (user.IsOnline == shouldBeOnline) continue;
            user.IsOnline = shouldBeOnline;
            user.LastSeenAt = now;
            changed++;
        }
        if (changed > 0) await _db.SaveChangesAsync(cancellationToken);
        return changed;
    }

    private static List<string> Normalize(IEnumerable<string> uuids) =>
        uuids.Select(PresenceRegistry.NormalizeUuid).Where(u => u.Length > 0).Distinct(StringComparer.Ordinal).ToList();
}
