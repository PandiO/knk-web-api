using knkwebapi_v2.Models;
using knkwebapi_v2.Realtime.Contracts;

namespace knkwebapi_v2.Realtime;

/// <summary>
/// The S2 user-state events (DESIGN §6.1, PandiO/knk-workspace#179), raised by the services that change user state
/// right after their write. Every method is a no-op without a sink (unit tests that build services by hand) and for a
/// user without a Minecraft UUID (a web-only account has nothing on a game server to update).
/// </summary>
public static class UserStateEvents
{
    /// <summary>Memberships or direct grants of <paramref name="user"/> changed.</summary>
    public static void PermissionsChanged(this IRealtimeEventSink? sink, User? user, string reason, int? actorUserId = null)
    {
        if (sink == null || !HasUuid(user)) return;
        sink.Publish(new RealtimeEvent(RealtimeEventTypes.UserPermissionsChanged,
            new UserPermissionsChangedPayload(user!.Id, user.Uuid!, reason), actorUserId));
    }

    /// <summary>A permission group (its fields, parent or node grants) changed; the game server fans out to members.</summary>
    public static void PermissionGroupChanged(this IRealtimeEventSink? sink, int groupId, string op, int? actorUserId = null)
    {
        if (sink == null || groupId <= 0) return;
        sink.Publish(new RealtimeEvent(RealtimeEventTypes.PermissionGroupChanged,
            new PermissionGroupChangedPayload(groupId, op), actorUserId));
    }

    /// <summary>The committed balances of <paramref name="userId"/>.</summary>
    public static void BalancesChanged(this IRealtimeEventSink? sink, int userId, string? uuid, long coins, long gems, long xp,
        int? actorUserId = null)
    {
        if (sink == null || string.IsNullOrWhiteSpace(uuid)) return;
        sink.Publish(new RealtimeEvent(RealtimeEventTypes.UserBalancesChanged,
            new UserBalancesChangedPayload(userId, uuid, coins, gems, xp), actorUserId));
    }

    public static void FrozenChanged(this IRealtimeEventSink? sink, User? user, int? actorUserId = null)
    {
        if (sink == null || !HasUuid(user)) return;
        sink.Publish(new RealtimeEvent(RealtimeEventTypes.UserFrozenChanged,
            new UserFrozenChangedPayload(user!.Id, user.Uuid!, user.IsFrozen, user.IsFrozen ? user.FrozenReason : null,
                user.IsFrozen ? user.FrozenByUserId : null), actorUserId));
    }

    public static void ModeChanged(this IRealtimeEventSink? sink, User? user, ActiveMode mode, int? actorUserId = null)
    {
        if (sink == null || !HasUuid(user)) return;
        sink.Publish(new RealtimeEvent(RealtimeEventTypes.UserModeChanged,
            new UserModeChangedPayload(user!.Id, user.Uuid!, mode.ToString()), actorUserId));
    }

    /// <summary>Other cached fields of <paramref name="user"/> changed; see <see cref="UserChangedPayload.Fields"/>.</summary>
    public static void UserChanged(this IRealtimeEventSink? sink, User? user, IReadOnlyList<string> fields, int? actorUserId = null)
    {
        if (sink == null || !HasUuid(user)) return;
        sink.Publish(new RealtimeEvent(RealtimeEventTypes.UserChanged,
            new UserChangedPayload(user!.Id, user.Uuid!, fields), actorUserId));
    }

    private static bool HasUuid(User? user) => user != null && !string.IsNullOrWhiteSpace(user.Uuid);
}

/// <summary>Field names for <see cref="UserChangedPayload.Fields"/>.</summary>
public static class UserChangedFields
{
    public const string Multipliers = "multipliers";
    public const string GatePassThroughMethod = "gatePassThroughMethod";
    public const string Link = "link";
    public const string Merge = "merge";
}
