using System.Text.RegularExpressions;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Realtime.Contracts;
using knkwebapi_v2.Realtime.Presence;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Realtime.Hubs;

/// <summary>
/// <c>/api/hubs/game</c> (DESIGN §4.1): one connection per game server, authenticated with the plugin key
/// (<c>X-API-Key</c> = <c>Security:PluginApiKey</c>, the <see cref="RealtimeSetup.GameServerPolicy"/> on the endpoint)
/// and named by <c>X-Knk-Server-Id</c>. Groups <c>servers</c> and <c>server:{id}</c>. Client-to-server methods are
/// presence (DESIGN §6.4), since WT2 (#181) <see cref="WorldTaskOfferResult"/> and since S3 (#182) <see cref="ReportWorlds"/>; nothing else is written over the hub
/// (P1). A joining player's waiting notifications are pushed to their server (S2, #179), and tasks still offered or queued
/// for them are offered again (WT2, DESIGN §8.4).
/// </summary>
public sealed partial class GameServerHub : Hub
{
    public const string ServerIdHeader = "X-Knk-Server-Id";
    private const string ServerIdItem = "knk.serverId";

    private readonly RealtimeConnections _connections;
    private readonly PresenceRegistry _presence;
    private readonly IPresenceStore _store;
    private readonly IRealtimePublisher _publisher;
    private readonly RealtimeOptions _options;
    private readonly ILogger<GameServerHub> _logger;
    private readonly TimeProvider _time;
    private readonly HubPlayerNotificationQueue? _notifications;
    private readonly IServiceProvider? _services;

    public GameServerHub(RealtimeConnections connections, PresenceRegistry presence, IPresenceStore store,
        IRealtimePublisher publisher, IOptions<RealtimeOptions> options, ILogger<GameServerHub> logger, TimeProvider? time = null,
        HubPlayerNotificationQueue? notifications = null, IServiceProvider? services = null)
    {
        _services = services;
        _notifications = notifications;
        _connections = connections;
        _presence = presence;
        _store = store;
        _publisher = publisher;
        _options = options.Value;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex ServerIdPattern();

    /// <summary>A usable server id (1–64 of letters, digits, '.', '_', '-'), or null.</summary>
    public static string? NormalizeServerId(string? raw)
    {
        var trimmed = raw?.Trim();
        return string.IsNullOrEmpty(trimmed) || !ServerIdPattern().IsMatch(trimmed) ? null : trimmed;
    }

    public override async Task OnConnectedAsync()
    {
        var raw = Context.GetHttpContext()?.Request.Headers[ServerIdHeader].ToString();
        var serverId = NormalizeServerId(raw);
        if (serverId == null)
        {
            _logger.LogWarning("Game hub connection {ConnectionId} refused: missing or invalid {Header} header",
                Context.ConnectionId, ServerIdHeader);
            Context.Abort();
            return;
        }
        if (_options.AllowedServerIds.Length > 0
            && !_options.AllowedServerIds.Contains(serverId, StringComparer.Ordinal))
        {
            _logger.LogWarning("Game hub connection {ConnectionId} refused: server id {ServerId} is not in Realtime:AllowedServerIds",
                Context.ConnectionId, serverId);
            Context.Abort();
            return;
        }

        Context.Items[ServerIdItem] = serverId;
        _connections.Add(new RealtimeConnection(Context.ConnectionId, RealtimeHubKind.Game,
            new[] { RealtimeGroups.Servers, RealtimeGroups.Server(serverId) }, serverId: serverId));
        var reconnect = _presence.ServerConnected(serverId, Context.ConnectionId);
        _logger.LogInformation("Game server {ServerId} connected to the realtime hub ({ConnectionId}{Reconnect})",
            serverId, Context.ConnectionId, reconnect ? ", reconnect" : string.Empty);
        _publisher.PublishToConnection(RealtimeHubKind.Game, Context.ConnectionId, RealtimeEventTypes.Welcome,
            new WelcomePayload(serverId, _options.PresenceGraceSeconds), RealtimeActor.System);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _connections.Remove(Context.ConnectionId);
        if (ServerId is { } serverId)
        {
            _presence.ServerDisconnected(serverId, Context.ConnectionId);
            _logger.LogInformation("Game server {ServerId} disconnected from the realtime hub ({ConnectionId}): {Reason}; players stay online for {Grace}s",
                serverId, Context.ConnectionId, exception?.Message ?? "closed", _options.PresenceGraceSeconds);
        }
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// The full list of online players, sent by the plugin on every (re)connect. Replaces this server's players and
    /// reconciles <c>User.IsOnline</c> with everyone online on any server (DESIGN §5).
    /// </summary>
    public async Task ReportRoster(List<RosterPlayerDto>? players)
    {
        if (ServerId is not { } serverId) return;
        var change = _presence.ReplaceRoster(serverId, (players ?? new List<RosterPlayerDto>()).Select(ToEntry));
        var changed = await _store.ReconcileAsync(_presence.AllOnlineUuids(), Now);
        _logger.LogInformation("Game server {ServerId} roster: {Online} online; {Changed} presence row(s) corrected",
            serverId, change.Online.Count, changed);
        // WT2 (#181, DESIGN §8.4 resync): tasks offered to, handed back from or queued for these players are offered again.
        await OfferWaitingAsync(change.Online);
    }

    public async Task PlayerJoined(RosterPlayerDto? player)
    {
        if (ServerId is not { } serverId || player == null) return;
        var uuid = _presence.PlayerJoined(serverId, ToEntry(player));
        if (uuid.Length > 0)
        {
            await _store.SetOnlineAsync(new[] { uuid }, Now);
            // S2 (#179): what waited for this player (and for the server) is shown now instead of on the next poll.
            _notifications?.PushPendingTo(serverId, uuid);
            // WT2 (#181, DESIGN §8.6): "Player quits mid-task → release; offer again on rejoin".
            await OfferWaitingAsync(new[] { uuid });
        }
    }

    /// <summary>
    /// WT2 (#181, DESIGN §6.4): what a player (or the plugin's checks) answered to a <c>WorldTaskOffered</c>: declined,
    /// timed-out, busy, offline or no-permission. "accepted" needs nothing (the offer claim over REST recorded it).
    /// </summary>
    public async Task WorldTaskOfferResult(WorldTaskOfferResultDto? result)
    {
        if (ServerId is not { } serverId || result == null || result.TaskId <= 0) return;
        var tasks = _services?.GetService<IWorldTaskService>();
        if (tasks == null) return;
        try
        {
            var recorded = await tasks.RecordOfferResultAsync(result.TaskId, result.Result, result.Uuid);
            _logger.LogInformation("Game server {ServerId}: offer of world task {TaskId} answered {Result}{Ignored}",
                serverId, result.TaskId, result.Result, recorded ? string.Empty : " (no open offer; ignored)");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Recording the offer answer for world task {TaskId} failed", result.TaskId);
        }
    }

    private async Task OfferWaitingAsync(IReadOnlyCollection<string> uuids)
    {
        if (uuids.Count == 0) return;
        var tasks = _services?.GetService<IWorldTaskService>();
        if (tasks == null) return;
        try
        {
            var offered = await tasks.OfferWaitingForPlayersAsync(uuids);
            if (offered > 0)
            {
                _logger.LogInformation("Offered {Count} waiting world task(s) to players on {ServerId}", offered, ServerId);
            }
        }
        catch (Exception ex)
        {
            // Presence is what this call is for; a failed offer leaves the task code (P5).
            _logger.LogWarning(ex, "Offering waiting world tasks failed");
        }
    }

    /// <summary>
    /// S3 (#182, DESIGN §6.4): the loaded worlds, the same body and rules as <c>PUT api/GameSettings/runtime-worlds</c>
    /// (a report replaces only its <c>serverId</c>'s worlds; null = the API's default id, as over REST). The plugin
    /// sends this instead of the REST report while connected. Nothing is answered: a world that adds a new world
    /// settings entry sends <c>GameSettingsChanged</c> through the entity interceptor.
    /// </summary>
    public async Task ReportWorlds(GameSettingsRuntimeWorldsUpdateDto? report)
    {
        if (ServerId is not { } serverId || report == null) return;
        var settings = _services?.GetService<IGameSettingsService>();
        if (settings == null) return;
        try
        {
            await settings.UpdateRuntimeWorldsAsync(report);
            _logger.LogDebug("Game server {ServerId} reported {Count} loaded world(s) over the hub", serverId,
                report.RuntimeWorlds?.Count ?? 0);
        }
        catch (Exception ex)
        {
            // The REST report is the fallback: the plugin reports there again while the hub is down.
            _logger.LogWarning(ex, "Recording the worlds reported by game server {ServerId} failed", serverId);
        }
    }

    public async Task PlayerLeft(PlayerLeftDto? player)
    {
        if (ServerId is not { } serverId || player == null) return;
        if (_presence.PlayerLeft(serverId, player.Uuid))
        {
            await _store.SetOfflineAsync(new[] { player.Uuid }, Now);
        }
    }

    private string? ServerId => Context.Items.TryGetValue(ServerIdItem, out var value) ? value as string : null;

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    private static RosterEntry ToEntry(RosterPlayerDto dto) => new(dto.Uuid, dto.Name, dto.World, dto.UserId);
}
