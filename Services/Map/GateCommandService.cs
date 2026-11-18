using System.Text.Json;
using System.Text.Json.Serialization;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Realtime;
using knkwebapi_v2.Realtime.Contracts;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.Map;

/// <summary>Why a gate command was refused before it reached a game server; the controller turns it into a status.</summary>
public enum GateCommandRefusal
{
    /// <summary>404 <c>GateCommandsOff</c>: <c>Map:GateCommands:Enabled</c> is off (O27).</summary>
    Off,
    /// <summary>400 <c>ValidationFailed</c>: the action is not open, close, toggle or repair.</summary>
    InvalidAction,
    /// <summary>404 <c>GateNotFound</c>.</summary>
    GateNotFound,
    /// <summary>400 <c>DoorNotOfGate</c>: the door is unknown or belongs to another gate.</summary>
    ForeignDoor,
    /// <summary>409 <c>GateInSiege</c>: a siege match runs on the gate (spec § 5 rule 11).</summary>
    Siege,
    /// <summary>503 <c>NoGameServerConnected</c>.</summary>
    NoGameServer,
}

public sealed class GateCommandException : Exception
{
    public GateCommandException(GateCommandRefusal refusal, string message) : base(message)
    {
        Refusal = refusal;
    }

    public GateCommandRefusal Refusal { get; }
}

public interface IGateCommandService
{
    /// <summary>
    /// Sends a gate command to the game servers and waits for the first result (spec § 6.2.5). Throws
    /// <see cref="GateCommandException"/> for a refusal decided here; returns the result (done, refused, siege or timeout)
    /// otherwise. Every command that passed validation is audit-logged (<see cref="AuditAction.GateCommandSent"/>).
    /// </summary>
    Task<GateCommandResponseDto> SendAsync(int gateId, GateCommandRequestDto request, int actorUserId,
        CancellationToken ct = default);
}

/// <summary>
/// Overview map L7 (PandiO/knk-workspace#741, OVERVIEW_MAP_DESIGN § 5 rule 11, § 6.2.5, O17, O27): live gate commands
/// from the web app. The realtime channel is a doorbell without an outbox (realtime DESIGN §4.3), so, as for web kick
/// and ban, the API refuses (503) while no game server is connected. A siege match on the gate refuses with 409 (the
/// same "under siege" test as the public panel, § 6.2.8); the game server checks its own lockdown again before it acts.
/// </summary>
public sealed class GateCommandService : IGateCommandService
{
    private static readonly JsonSerializerOptions AuditJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly KnKDbContext _db;
    private readonly IAuditLogService _audit;
    private readonly IUserRepository _users;
    private readonly IRealtimeEventSink? _realtime;
    private readonly GateCommandWaiter _waiter;
    private readonly IOptionsMonitor<MapOptions> _options;
    private readonly Func<int> _connectedGameServers;
    private readonly TimeProvider _time;
    private readonly ILogger<GateCommandService>? _logger;

    public GateCommandService(KnKDbContext db, IAuditLogService audit, IUserRepository users, GateCommandWaiter waiter,
        IOptionsMonitor<MapOptions> options, RealtimeConnections connections, IRealtimeEventSink? realtime = null,
        TimeProvider? time = null, ILogger<GateCommandService>? logger = null)
        : this(db, audit, users, waiter, options, () => connections.Count(RealtimeHubKind.Game), realtime, time, logger)
    {
    }

    /// <param name="connectedGameServers">How many game servers are connected to the hub now (tests).</param>
    public GateCommandService(KnKDbContext db, IAuditLogService audit, IUserRepository users, GateCommandWaiter waiter,
        IOptionsMonitor<MapOptions> options, Func<int> connectedGameServers, IRealtimeEventSink? realtime = null,
        TimeProvider? time = null, ILogger<GateCommandService>? logger = null)
    {
        _db = db;
        _audit = audit;
        _users = users;
        _waiter = waiter;
        _options = options;
        _connectedGameServers = connectedGameServers;
        _realtime = realtime;
        _time = time ?? TimeProvider.System;
        _logger = logger;
    }

    public async Task<GateCommandResponseDto> SendAsync(int gateId, GateCommandRequestDto request, int actorUserId,
        CancellationToken ct = default)
    {
        var options = _options.CurrentValue.GateCommands;
        if (!options.Enabled)
            throw new GateCommandException(GateCommandRefusal.Off, "Gate commands are switched off (Map:GateCommands:Enabled).");

        var action = GateCommandActions.Normalize(request?.Action);
        if (action == null)
            throw new GateCommandException(GateCommandRefusal.InvalidAction,
                "action must be one of: " + string.Join(", ", GateCommandActions.All) + ".");

        var gate = gateId > 0
            ? await _db.GateStructures.AsNoTracking().Where(g => g.Id == gateId)
                .Select(g => new { g.Id, g.Name, g.CurrentSiegeId }).FirstOrDefaultAsync(ct)
            : null;
        if (gate == null)
            throw new GateCommandException(GateCommandRefusal.GateNotFound, $"Gate structure {gateId} not found.");

        var doorId = request!.DoorId;
        if (doorId.HasValue)
        {
            var doorGate = await _db.GateDoors.AsNoTracking().Where(d => d.Id == doorId.Value)
                .Select(d => (int?)d.GateStructureId).FirstOrDefaultAsync(ct);
            if (doorGate != gate.Id)
                throw new GateCommandException(GateCommandRefusal.ForeignDoor,
                    $"Door {doorId.Value} is not a door of gate {gate.Id}.");
        }

        var commandId = Guid.NewGuid().ToString();
        var response = new GateCommandResponseDto { CommandId = commandId, GateId = gate.Id, DoorId = doorId, Action = action };

        if (await UnderSiegeAsync(gate.CurrentSiegeId, ct))
        {
            const string siegeMessage = "Gate is in a running siege.";
            await AuditAsync(actorUserId, response, GateCommandOutcomes.Siege, siegeMessage, servers: null);
            throw new GateCommandException(GateCommandRefusal.Siege, siegeMessage);
        }

        var servers = _connectedGameServers();
        if (servers <= 0)
        {
            const string noServer = "No game server is connected, so the command can't be carried out now.";
            await AuditAsync(actorUserId, response, GateCommandOutcomes.NoGameServer, noServer, servers: 0);
            throw new GateCommandException(GateCommandRefusal.NoGameServer, noServer);
        }

        var actor = await _users.GetByIdAsync(actorUserId);
        var staffName = actor?.Username ?? $"User #{actorUserId}";

        using (var pending = _waiter.Register(commandId))
        {
            _realtime?.Publish(new RealtimeEvent(GateCommandEventTypes.GateCommand,
                new GateCommandPayload(commandId, gate.Id, doorId, action, actorUserId, staffName), actorUserId));
            try
            {
                var result = await pending.Result.WaitAsync(options.EffectiveTimeout, _time, ct);
                response.Result = NormalizeOutcome(result.Result);
                response.Message = Trim(result.Message);
                response.Doors = result.Doors?.Where(d => d != null).Take(64).ToList() ?? new List<GateCommandDoorStateDto>();
            }
            catch (TimeoutException)
            {
                response.Result = GateCommandOutcomes.Timeout;
                response.Message = $"No answer from the game server within {options.EffectiveTimeout.TotalSeconds:0} s; "
                    + "it may still carry the command out.";
            }
        }

        await AuditAsync(actorUserId, response, response.Result, response.Message, servers);
        _logger?.LogInformation("Gate command {CommandId}: {Action} gate {GateId} door {DoorId} by user {UserId}: {Result}",
            commandId, action, gate.Id, doorId?.ToString() ?? "all", actorUserId, response.Result);
        return response;
    }

    /// <summary>"Under siege" as the public panel (§ 6.2.8): the gate's current siege is a match created or in progress.</summary>
    private async Task<bool> UnderSiegeAsync(int? currentSiegeId, CancellationToken ct) =>
        currentSiegeId.HasValue && await _db.SiegeMatches.AsNoTracking()
            .AnyAsync(m => m.Id == currentSiegeId.Value
                && (m.Status == SiegeMatchStatus.Created || m.Status == SiegeMatchStatus.InProgress), ct);

    /// <summary>The game server's outcome; anything unknown is reported as refused (fail closed).</summary>
    private static string NormalizeOutcome(string? outcome) => outcome?.Trim().ToLowerInvariant() switch
    {
        GateCommandOutcomes.Done => GateCommandOutcomes.Done,
        GateCommandOutcomes.Siege => GateCommandOutcomes.Siege,
        _ => GateCommandOutcomes.Refused,
    };

    private static string? Trim(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Length <= 300 ? text.Trim() : text[..300].Trim();

    private Task AuditAsync(int actorUserId, GateCommandResponseDto command, string outcome, string? message, int? servers) =>
        _audit.RecordForSubjectAsync(actorUserId, AuditSubjectTypes.GateStructure, command.GateId, AuditAction.GateCommandSent,
            JsonSerializer.Serialize(new
            {
                commandId = command.CommandId,
                action = command.Action,
                doorId = command.DoorId,
                result = outcome,
                message,
                gameServers = servers,
            }, AuditJson));
}
