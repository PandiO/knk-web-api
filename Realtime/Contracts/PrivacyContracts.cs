namespace knkwebapi_v2.Realtime.Contracts;

/// <summary>
/// Realtime S5 (PandiO/knk-workspace#184, DESIGN §7.4): the GDPR erasure push to the game servers. At most once like
/// every hub message, so the same rows are persisted (<c>privacy_erasure_events</c>) and served by
/// <c>GET api/privacy/erasures?since={erasureId}</c>, which the game server calls on every (re)connect.
/// </summary>
public static class PrivacyEventTypes
{
    /// <summary>
    /// An account was erased (pseudonymized) by an executed data-deletion request; one event per erased account (the
    /// player and every account merged into them). The game server kicks the player if online and deletes everything
    /// it keeps about them. Payload <see cref="AccountErasedPayload"/>. To every game server.
    /// </summary>
    public const string AccountErased = "AccountErased";
}

/// <param name="UserId">The erased account (its row stays, pseudonymized).</param>
/// <param name="FormerUuid">Its Minecraft UUID before the erasure; null when it had none.</param>
/// <param name="ErasureId">The feed row's id (the sequence of <c>GET api/privacy/erasures</c>).</param>
public sealed record AccountErasedPayload(int UserId, string? FormerUuid, long ErasureId);
