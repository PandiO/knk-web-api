using System.Text.Json;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Realtime;
using knkwebapi_v2.Realtime.Contracts;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    /// <summary>Why a kick or ban was refused; the controller turns it into a status code.</summary>
    public enum PlayerModerationRefusal
    {
        /// <summary>400: the reason is missing (ban) or too long.</summary>
        InvalidReason,
        /// <summary>404.</summary>
        UserNotFound,
        /// <summary>400: staff can't kick or ban themselves.</summary>
        Self,
        /// <summary>400: a web-only account has nothing on a game server to kick or ban.</summary>
        NoMinecraftAccount,
        /// <summary>403: the player ranks at or above the staff member (<see cref="IPermissionEscalationGuard.CanModerateUserAsync"/>).</summary>
        Rank,
        /// <summary>409 (kick only): the player is not online, so there is nothing to kick.</summary>
        NotOnline,
        /// <summary>503: no game server is connected to the realtime hub, so nothing would carry it out.</summary>
        NoGameServer,
    }

    public sealed class PlayerModerationException : Exception
    {
        public PlayerModerationException(PlayerModerationRefusal refusal, string message) : base(message)
        {
            Refusal = refusal;
        }

        public PlayerModerationRefusal Refusal { get; }
    }

    /// <summary>Kick and ban from the web app (PandiO/knk-workspace#565).</summary>
    public interface IPlayerModerationService
    {
        /// <summary>Kicks an online player: audits <see cref="AuditAction.PlayerKicked"/> and publishes
        /// <see cref="RealtimeEventTypes.PlayerKick"/>. Throws <see cref="PlayerModerationException"/> on a refusal.</summary>
        Task<PlayerModerationResultDto> KickAsync(int userId, string? reason, int actorUserId);

        /// <summary>Bans a player (online or not), permanently, like in-game <c>/ban</c>: audits
        /// <see cref="AuditAction.PlayerBanned"/> and publishes <see cref="RealtimeEventTypes.PlayerBan"/>.</summary>
        Task<PlayerModerationResultDto> BanAsync(int userId, string? reason, int actorUserId);
    }

    /// <summary>
    /// Kick and ban from the web app (PandiO/knk-workspace#565, developer decisions 2026-11-15). The API keeps no ban
    /// state: the game server carries the action out (Paper's kick, Paper's ban list) when the realtime event reaches
    /// it (knk-plugin, #574). The realtime channel is a doorbell without an outbox (realtime DESIGN §4.3, P2): an
    /// event is sent to the game servers connected now and not replayed after a reconnect. So the API refuses (503)
    /// while no game server is connected instead of answering "done" for an action nothing would carry out, and audits
    /// only what it handed over.
    /// </summary>
    public sealed class PlayerModerationService : IPlayerModerationService
    {
        /// <summary>Longest reason (as the web app's freeze reason; the game shows it on the kick/ban screen).</summary>
        public const int MaxReasonLength = 200;

        private readonly IUserRepository _users;
        private readonly IAuditLogService _audit;
        private readonly IPermissionEscalationGuard _guard;
        private readonly IRealtimeEventSink? _realtime;
        private readonly Func<int> _connectedGameServers;
        private readonly TimeProvider _time;

        public PlayerModerationService(IUserRepository users, IAuditLogService audit, IPermissionEscalationGuard guard,
            RealtimeConnections connections, IRealtimeEventSink? realtime = null, TimeProvider? time = null)
            : this(users, audit, guard, () => connections.Count(RealtimeHubKind.Game), realtime, time)
        {
        }

        /// <param name="connectedGameServers">How many game servers are connected to the hub now (tests).</param>
        public PlayerModerationService(IUserRepository users, IAuditLogService audit, IPermissionEscalationGuard guard,
            Func<int> connectedGameServers, IRealtimeEventSink? realtime = null, TimeProvider? time = null)
        {
            _users = users;
            _audit = audit;
            _guard = guard;
            _connectedGameServers = connectedGameServers;
            _realtime = realtime;
            _time = time ?? TimeProvider.System;
        }

        public Task<PlayerModerationResultDto> KickAsync(int userId, string? reason, int actorUserId) =>
            RunAsync(userId, reason, actorUserId, ban: false);

        public Task<PlayerModerationResultDto> BanAsync(int userId, string? reason, int actorUserId) =>
            RunAsync(userId, reason, actorUserId, ban: true);

        private async Task<PlayerModerationResultDto> RunAsync(int userId, string? reason, int actorUserId, bool ban)
        {
            var verb = ban ? "ban" : "kick";
            var trimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            if (ban && trimmed == null)
                throw new PlayerModerationException(PlayerModerationRefusal.InvalidReason, "A reason is required to ban a player.");
            if (trimmed?.Length > MaxReasonLength)
                throw new PlayerModerationException(PlayerModerationRefusal.InvalidReason,
                    $"The reason must be at most {MaxReasonLength} characters.");

            var target = userId > 0 ? await _users.GetByIdAsync(userId) : null;
            if (target == null)
                throw new PlayerModerationException(PlayerModerationRefusal.UserNotFound, $"User with ID {userId} not found");
            if (target.Id == actorUserId)
                throw new PlayerModerationException(PlayerModerationRefusal.Self, $"You can't {verb} yourself.");
            if (string.IsNullOrWhiteSpace(target.Uuid))
                throw new PlayerModerationException(PlayerModerationRefusal.NoMinecraftAccount,
                    $"{target.Username} has no Minecraft account, so there is nothing to {verb} in game.");

            var rank = await _guard.CanModerateUserAsync(actorUserId, target.Id);
            if (!rank.Allowed)
                throw new PlayerModerationException(PlayerModerationRefusal.Rank, rank.Message ?? PermissionEscalationGuard.ModerateRankMessage);

            if (!ban && !target.IsOnline)
                throw new PlayerModerationException(PlayerModerationRefusal.NotOnline,
                    $"{target.Username} is not online, so there is nothing to kick.");

            var servers = _connectedGameServers();
            if (servers <= 0)
                throw new PlayerModerationException(PlayerModerationRefusal.NoGameServer,
                    $"No game server is connected, so the {verb} can't be carried out now. Try again when the game server is up, or use /{verb} in game.");

            var actor = await _users.GetByIdAsync(actorUserId);
            var staffName = actor?.Username ?? $"User #{actorUserId}";
            var actionId = Guid.NewGuid().ToString();
            var at = _time.GetUtcNow().UtcDateTime;

            await _audit.RecordAsync(actorUserId, target.Id, ban ? AuditAction.PlayerBanned : AuditAction.PlayerKicked,
                JsonSerializer.Serialize(new
                {
                    reason = trimmed,
                    actionId,
                    uuid = target.Uuid,
                    username = target.Username,
                    permanent = ban ? true : (bool?)null,
                    gameServers = servers,
                }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }));

            _realtime?.Publish(new RealtimeEvent(ban ? RealtimeEventTypes.PlayerBan : RealtimeEventTypes.PlayerKick,
                new PlayerModerationPayload(actionId, target.Id, target.Uuid!, target.Username, trimmed, actorUserId, staffName, at),
                actorUserId));

            return new PlayerModerationResultDto { ActionId = actionId, UserId = target.Id, GameServers = servers };
        }
    }
}
