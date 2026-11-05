using System.Collections.Concurrent;
using System.Reflection;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Models;
using knkwebapi_v2.Realtime.Contracts;

namespace knkwebapi_v2.Realtime;

/// <summary>
/// S4 (PandiO/knk-workspace#183, DESIGN §6.5, §9): turns the entities one save wrote into the web hub's live-update
/// events. Pure, like <see cref="RealtimeEntityChangeMap"/> (which stays the game servers' map, unchanged):
/// <list type="bullet">
///   <item><c>EntityChanged {type, id, op, actorUserId, actorName}</c> for every type the dashboard can show (a model
///     with <c>[FormConfigurableEntity]</c>), also for each such base type (a <c>Town</c> is also a <c>Domain</c>). More
///     than <see cref="CollapseAbove"/> of one type in one save → one event without an id.</item>
///   <item><c>PresenceChanged {userId, online, serverId, lastSeenAt}</c> when a user's <c>IsOnline</c> changed (the hub's
///     presence store and the REST <c>PUT api/Users/{id}/presence</c> both save through EF). More than
///     <see cref="CollapseAbove"/> in one save (a roster reconcile) → one event without a user id.</item>
/// </list>
/// Each event names the node a receiver must hold (<see cref="RealtimeEvent.WebNode"/>), the node that already gates
/// the same data over REST: <c>knk.admin.content</c> (the dashboard and detail page) by default,
/// <c>knk.admin.user.manage</c> for users and their memberships, grants and audit log (the moderation list and the
/// player profile; also presence), and nothing at all for types whose REST reads are the game server's or a ledger
/// (<see cref="NotSent"/>).
/// </summary>
public static class RealtimeWebChangeMap
{
    /// <summary>More entities of one type than this in one save → one event without an id.</summary>
    public const int CollapseAbove = 50;

    /// <summary>The node for every type not listed below: the dashboard's and the detail page's.</summary>
    public const string DefaultNode = StaffPermissions.ManageContent;

    /// <summary>Types read over REST only with <c>knk.admin.user.manage</c>.</summary>
    private static readonly IReadOnlySet<Type> UserManagedTypes = new HashSet<Type>
    {
        typeof(User), typeof(UserPermissionGroup), typeof(PermissionGrant), typeof(PermissionHolder), typeof(AuditLogEntry),
    };

    /// <summary>
    /// Types never sent to the web: read only by the game server (<c>UserIgnore</c>), a player's ledger or purchases
    /// (<c>CurrencyTransaction</c>, <c>KitPurchase</c>, <c>KitClaim</c>), or behind another node (<c>ItemInstance</c>:
    /// lootbox managers).
    /// </summary>
    public static readonly IReadOnlySet<Type> NotSent = new HashSet<Type>
    {
        typeof(UserIgnore), typeof(CurrencyTransaction), typeof(KitPurchase), typeof(KitClaim), typeof(ItemInstance),
    };

    /// <summary>
    /// Columns whose change alone sends no web <c>EntityChanged</c> for a user: presence (its own event), and the auth
    /// columns nobody's table shows.
    /// </summary>
    private static readonly HashSet<string> UserQuietProperties = new(StringComparer.Ordinal)
    {
        nameof(User.IsOnline), nameof(User.LastSeenAt), nameof(User.PasswordHash), nameof(User.LastPasswordChangeAt),
        nameof(User.TokenVersion), nameof(User.LastEmailChangeAt),
    };

    /// <summary>The values the rules read besides <see cref="RealtimeEntityChangeMap.ValueNames"/>.</summary>
    public static readonly IReadOnlyList<string> ValueNames = new[] { nameof(User.IsOnline), nameof(User.LastSeenAt) };

    private static readonly ConcurrentDictionary<Type, IReadOnlyList<string>> DashboardNames = new();

    /// <summary>The dashboard type names a CLR type is shown under: itself and its base types with <c>[FormConfigurableEntity]</c>.</summary>
    public static IReadOnlyList<string> DashboardTypeNames(Type entityType) => DashboardNames.GetOrAdd(entityType, type =>
    {
        var names = new List<string>();
        for (var current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            if (current.GetCustomAttribute<FormConfigurableEntityAttribute>(inherit: false) != null)
            {
                names.Add(current.Name);
            }
        }
        return names;
    });

    /// <summary>Whether a save must be watched for this type for the web (the interceptor skips everything else).</summary>
    public static bool IsWatched(Type entityType) =>
        entityType == typeof(User) || (!NotSent.Contains(entityType) && DashboardTypeNames(entityType).Count > 0);

    /// <summary>The node a web receiver must hold for <paramref name="entityType"/>'s events; null = not sent.</summary>
    public static string? NodeFor(Type entityType)
    {
        if (NotSent.Contains(entityType)) return null;
        return UserManagedTypes.Contains(entityType) ? StaffPermissions.ManageUsers : DefaultNode;
    }

    /// <summary>The web events for one save, entity events first, then presence; without duplicates.</summary>
    /// <param name="serverOf">The game server a UUID is on (the presence registry), or null.</param>
    public static IReadOnlyList<RealtimeEvent> Map(IEnumerable<RealtimeEntityChange> changes, int? actorUserId = null,
        string? actorName = null, Func<string?, string?>? serverOf = null)
    {
        var entities = new List<(string Node, string Type, int? Id, string Op)>();
        var presence = new List<PresenceChangedPayload>();

        foreach (var change in changes)
        {
            AddEntityChanged(change, entities);
            AddPresence(change, presence, serverOf);
        }

        var result = new List<RealtimeEvent>();
        foreach (var group in entities.GroupBy(e => (e.Node, e.Type)))
        {
            var distinct = group.GroupBy(e => e.Id).Select(g => (Id: g.Key, Op: StrongestOp(g.Select(e => e.Op)))).ToList();
            if (distinct.Count > CollapseAbove || distinct.Any(d => d.Id == null))
            {
                // Many at once, or a row without a single id (a link table): "re-read the list".
                result.Add(Web(group.Key.Node, WebLiveEventTypes.EntityChanged,
                    new WebEntityChangedPayload(group.Key.Type, null, EntityChangeOps.Updated, actorUserId, actorName), actorUserId));
                continue;
            }
            result.AddRange(distinct.Select(d => Web(group.Key.Node, WebLiveEventTypes.EntityChanged,
                new WebEntityChangedPayload(group.Key.Type, d.Id, d.Op, actorUserId, actorName), actorUserId)));
        }

        var latest = presence.GroupBy(p => p.UserId).Select(g => g.Last()).ToList();
        if (latest.Count > CollapseAbove)
        {
            result.Add(Web(StaffPermissions.ManageUsers, WebLiveEventTypes.PresenceChanged,
                new PresenceChangedPayload(null, null, null, null), actorUserId));
        }
        else
        {
            result.AddRange(latest.Select(p => Web(StaffPermissions.ManageUsers, WebLiveEventTypes.PresenceChanged, p, actorUserId)));
        }
        return result;
    }

    private static RealtimeEvent Web(string node, string type, object payload, int? actorUserId) =>
        RealtimeEvent.ToWebHolders(node, type, payload, actorUserId);

    private static void AddEntityChanged(RealtimeEntityChange change, List<(string, string, int?, string)> into)
    {
        var node = NodeFor(change.EntityType);
        if (node == null) return;
        var names = DashboardTypeNames(change.EntityType);
        if (names.Count == 0) return;
        if (change.State == RealtimeEntityState.Modified && Quiet(change)) return;
        var id = AsInt(change.Value("Id"));
        if (id is <= 0) id = null;
        var op = change.State switch
        {
            RealtimeEntityState.Added => EntityChangeOps.Created,
            RealtimeEntityState.Deleted => EntityChangeOps.Deleted,
            _ => EntityChangeOps.Updated,
        };
        foreach (var name in names)
        {
            into.Add((node, name, id, op));
        }
    }

    private static void AddPresence(RealtimeEntityChange change, List<PresenceChangedPayload> into, Func<string?, string?>? serverOf)
    {
        if (change.EntityType != typeof(User) || change.State != RealtimeEntityState.Modified) return;
        if (!change.Modified.Contains(nameof(User.IsOnline))) return;
        if (AsInt(change.Value("Id")) is not { } userId || userId <= 0) return;
        var online = change.Value(nameof(User.IsOnline)) as bool? == true;
        var serverId = online ? serverOf?.Invoke(change.Value("Uuid") as string) : null;
        into.Add(new PresenceChangedPayload(userId, online, serverId, change.Value(nameof(User.LastSeenAt)) as DateTime?));
    }

    private static bool Quiet(RealtimeEntityChange change) =>
        change.Modified.Count == 0
        || (change.EntityType == typeof(User) && change.Modified.All(UserQuietProperties.Contains));

    /// <summary>Deleted beats Created beats Updated for one entity written twice in a save.</summary>
    private static string StrongestOp(IEnumerable<string> ops)
    {
        var list = ops.ToList();
        return list.Contains(EntityChangeOps.Deleted) ? EntityChangeOps.Deleted
            : list.Contains(EntityChangeOps.Created) ? EntityChangeOps.Created : EntityChangeOps.Updated;
    }

    private static int? AsInt(object? value) => value switch
    {
        int i => i,
        long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
        _ => null,
    };
}
