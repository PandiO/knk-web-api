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
/// presence (DESIGN §6.4), since WT2 (#181) <see cref="WorldTaskOfferResult"/> and since S3 (#182) <see cref="ReportWorlds"/>, since overview map L1 (#735) <see cref="ReportRegionOutlines"/>, since L4 (#738) <see cref="ReportMapPlayers"/>, since L7 (#741) <see cref="GateCommandResult"/>, since #988 <see cref="RegionGeometryResult"/> and <see cref="SpotSiteCheckResult"/>; nothing else is written over the hub
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

    /// <summary>
    /// Overview map L1 (PandiO/knk-workspace#735, OVERVIEW_MAP_DESIGN § 6.2.6, O28): domain region outlines. The plugin
    /// sends them only with its <c>map.reporting.enabled</c> on: every domain region after its startup reconcile, one
    /// region after it is created, redefined or renamed (the old name in <c>Removed</c>). Access: like every method on
    /// this hub, only a connection holding the plugin key (<see cref="RealtimeSetup.GameServerPolicy"/>) and a valid
    /// server id reaches it. Nothing is answered; a failure is logged and the next report repeats the data.
    /// </summary>
    public async Task ReportRegionOutlines(RegionOutlineReportDto? report)
    {
        if (ServerId is not { } serverId || report == null) return;
        var outlines = _services?.GetService<knkwebapi_v2.Services.Map.IMapRegionOutlineService>();
        if (outlines == null) return;
        try
        {
            var result = await outlines.ApplyReportAsync(report);
            _logger.LogInformation("Game server {ServerId} reported region outlines: {Stored} stored, {Unchanged} unchanged, {Removed} removed, {Rejected} rejected",
                serverId, result.Stored, result.Unchanged, result.Removed, result.Rejected.Count);
            foreach (var reason in result.Rejected.Take(10))
            {
                _logger.LogWarning("Region outline from game server {ServerId} not stored: {Reason}", serverId, reason);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Storing the region outlines reported by game server {ServerId} failed", serverId);
        }
    }

    /// <summary>
    /// Overview map L4 (PandiO/knk-workspace#738, OVERVIEW_MAP_DESIGN § 5 rule 6, § 6.2.6): the live player report. The
    /// plugin sends it (only with its <c>map.reporting.enabled</c> on) every <c>map.reporting.interval-ticks</c>. Kept in
    /// <see cref="knkwebapi_v2.Services.Map.MapPlayerRegistry"/> only while <c>Map:LivePlayers:Enabled</c> is on;
    /// otherwise ignored, so nothing is held. Access: like every method on this hub, only a connection holding the
    /// plugin key reaches it. Nothing is answered.
    /// </summary>
    public Task ReportMapPlayers(MapPlayersReportDto? report)
    {
        if (ServerId is not { } serverId || report == null) return Task.CompletedTask;
        var options = _services?.GetService<IOptionsMonitor<knkwebapi_v2.Configuration.MapOptions>>();
        if (options?.CurrentValue.LivePlayers.Enabled != true) return Task.CompletedTask;
        var registry = _services?.GetService<knkwebapi_v2.Services.Map.MapPlayerRegistry>();
        if (registry == null) return Task.CompletedTask;
        var stored = registry.Apply(serverId, report);
        _logger.LogDebug("Game server {ServerId} reported {Stored} of {Count} map player(s)", serverId, stored,
            report.Players?.Count ?? 0);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Overview map L7 (PandiO/knk-workspace#741, OVERVIEW_MAP_DESIGN § 6.2.5–6.2.6): the game server's answer to a
    /// <c>GateCommand</c>. Handed to the waiting <c>POST api/GateStructures/{id}/commands</c> request through
    /// <see cref="knkwebapi_v2.Services.Map.GateCommandWaiter"/>; the first answer per command id wins, a late one (after the
    /// timeout) or one for an unknown id is ignored. Ignored while <c>Map:GateCommands:Enabled</c> is off. Access: like
    /// every method on this hub, only a connection holding the plugin key reaches it. Nothing is answered.
    /// </summary>
    public Task GateCommandResult(GateCommandResultDto? result)
    {
        if (ServerId is not { } serverId || result == null) return Task.CompletedTask;
        var options = _services?.GetService<IOptionsMonitor<knkwebapi_v2.Configuration.MapOptions>>();
        if (options?.CurrentValue.GateCommands.Enabled != true) return Task.CompletedTask;
        var waiter = _services?.GetService<knkwebapi_v2.Services.Map.GateCommandWaiter>();
        var matched = waiter?.Complete(result) == true;
        _logger.LogInformation("Game server {ServerId} answered gate command {CommandId}: {Result}{Ignored}", serverId,
            result.CommandId, result.Result, matched ? string.Empty : " (nothing waits for it; ignored)");
        return Task.CompletedTask;
    }

    /// <summary>
    /// PandiO/knk-workspace#988: the game server's answer to a <c>RegionGeometryQuery</c>. Handed to the waiting query
    /// through <see cref="knkwebapi_v2.Services.Regions.RegionQueryWaiter{TAnswer}"/>; the first answer per query id wins, a
    /// late or unknown one is ignored. Ignored while <c>RegionQueries:Enabled</c> is off. Access: like every method on this
    /// hub, only a connection holding the plugin key reaches it. Nothing is answered.
    /// </summary>
    public Task RegionGeometryResult(RegionGeometryResultDto? result) =>
        CompleteRegionQuery(result, RegionQueryEventTypes.RegionGeometryQuery, result?.Result);

    /// <summary>
    /// PandiO/knk-workspace#988: the game server's answer to a <c>SpotSiteCheckQuery</c> (siege weapon spot floor and
    /// space), as <see cref="RegionGeometryResult"/>. Only a game server with <c>siege.weapons.enabled</c> answers.
    /// </summary>
    public Task SpotSiteCheckResult(SpotSiteCheckResultDto? result) =>
        CompleteRegionQuery(result, RegionQueryEventTypes.SpotSiteCheckQuery, result?.Result);

    private Task CompleteRegionQuery<TAnswer>(TAnswer? result, string type, string? outcome)
        where TAnswer : class, IRegionQueryAnswer
    {
        if (ServerId is not { } serverId || result == null) return Task.CompletedTask;
        var options = _services?.GetService<IOptionsMonitor<knkwebapi_v2.Configuration.RegionQueryOptions>>();
        if (options?.CurrentValue.Enabled != true) return Task.CompletedTask;
        var waiter = _services?.GetService<knkwebapi_v2.Services.Regions.RegionQueryWaiter<TAnswer>>();
        var matched = waiter?.Complete(result) == true;
        _logger.LogDebug("Game server {ServerId} answered {Type} {QueryId}: {Result}{Ignored}", serverId, type,
            result.QueryId, outcome, matched ? string.Empty : " (nothing waits for it; ignored)");
        return Task.CompletedTask;
    }

    public async Task PlayerLeft(PlayerLeftDto? player)
    {
        if (ServerId is not { } serverId || player == null) return;
        // Overview map L4 (#738, spec § 5 rule 6): the player leaves the map at once.
        _services?.GetService<knkwebapi_v2.Services.Map.MapPlayerRegistry>()?.Remove(player.Uuid);
        if (_presence.PlayerLeft(serverId, player.Uuid))
        {
            await _store.SetOfflineAsync(new[] { player.Uuid }, Now);
        }
    }

    private string? ServerId => Context.Items.TryGetValue(ServerIdItem, out var value) ? value as string : null;

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    private static RosterEntry ToEntry(RosterPlayerDto dto) => new(dto.Uuid, dto.Name, dto.World, dto.UserId);
}
