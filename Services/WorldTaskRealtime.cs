using System.Text.Json;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Models;
using knkwebapi_v2.Realtime;
using knkwebapi_v2.Realtime.Contracts;
using knkwebapi_v2.Realtime.Presence;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// WT2 (PandiO/knk-workspace#181, DESIGN §8.3–§8.5): what the world-task state machine tells the hubs, and the presence
    /// lookup the offer needs. Every event goes through <see cref="IRealtimeEventSink"/>, so it is sent after the save it
    /// describes. Without a connected game server (realtime off) nobody is online and nothing is offered: the task code
    /// flow is unchanged (P5).
    /// </summary>
    public interface IWorldTaskRealtime
    {
        /// <summary>The connected game server <paramref name="uuid"/> plays on, or null (offline, or realtime off).</summary>
        string? ServerOf(string? uuid);

        /// <summary>Web: <c>WorldTaskUpdated</c> to <c>task:{id}</c> and <c>user:{creator}</c> (and <c>CatalogScanUpdated</c> for a scan).</summary>
        void Changed(WorldTask task);

        /// <summary>Game: <c>WorldTaskOffered</c> to <paramref name="serverId"/>.</summary>
        void Offered(WorldTask task, string serverId, string uuid, string mode, string? fromUsername);

        /// <summary>Game: <c>WorldTaskCancelled</c> to every server (a server that doesn't hold or show it ignores it).</summary>
        void Cancelled(WorldTask task, string? uuid, string? reason);

        /// <summary>Game: <c>WorldTaskOfferWithdrawn</c> (the offer is no longer open).</summary>
        void OfferWithdrawn(WorldTask task, string uuid, string? reason);

        /// <summary>Game: <c>WorldTaskCreated</c> for a headless task waiting to be claimed.</summary>
        void HeadlessPending(WorldTask task);
    }

    /// <inheritdoc />
    public sealed class WorldTaskRealtime : IWorldTaskRealtime
    {
        private readonly IRealtimeEventSink _sink;
        private readonly PresenceRegistry? _presence;
        private readonly WorldTaskOptions _options;

        public WorldTaskRealtime(IRealtimeEventSink sink, PresenceRegistry? presence = null, IOptions<WorldTaskOptions>? options = null)
        {
            _sink = sink;
            _presence = presence;
            _options = options?.Value ?? new WorldTaskOptions();
        }

        public string? ServerOf(string? uuid) => _presence?.ConnectedServerOf(uuid);

        public void Changed(WorldTask task)
        {
            var groups = new List<string> { RealtimeGroups.Task(task.Id) };
            if (task.CreatedByUserId is { } creator)
            {
                groups.Add(RealtimeGroups.User(creator));
            }
            _sink.Publish(RealtimeEvent.ToWeb(WorldTaskEventTypes.WorldTaskUpdated, ToPayload(task), groups));

            if (task.TaskType == WorldTaskTypes.MinecraftCatalogScan && task.CreatedByUserId is { } starter
                && ScanIdOf(task.InputJson) is { } scanId)
            {
                _sink.Publish(RealtimeEvent.ToWeb(WorldTaskEventTypes.CatalogScanUpdated,
                    new CatalogScanUpdatedPayload(scanId, task.Id, task.Status), new[] { RealtimeGroups.User(starter) }));
            }
        }

        public void Offered(WorldTask task, string serverId, string uuid, string mode, string? fromUsername) =>
            _sink.Publish(new RealtimeEvent(WorldTaskEventTypes.WorldTaskOffered,
                new WorldTaskOfferedPayload(task.Id, uuid, task.TaskType, task.FieldName, Label(task),
                    task.WorkflowSession?.EntityTypeName, mode, fromUsername, _options.OfferTimeoutSeconds),
                task.CreatedByUserId, serverId));

        public void Cancelled(WorldTask task, string? uuid, string? reason) =>
            _sink.Publish(new RealtimeEvent(WorldTaskEventTypes.WorldTaskCancelled,
                new WorldTaskCancelledPayload(task.Id, task.Status, uuid, reason)));

        public void OfferWithdrawn(WorldTask task, string uuid, string? reason) =>
            _sink.Publish(new RealtimeEvent(WorldTaskEventTypes.WorldTaskOfferWithdrawn,
                new WorldTaskOfferWithdrawnPayload(task.Id, uuid, reason)));

        public void HeadlessPending(WorldTask task) =>
            _sink.Publish(new RealtimeEvent(WorldTaskEventTypes.WorldTaskCreated, new WorldTaskCreatedPayload(task.Id, task.TaskType)));

        public static WorldTaskUpdatedPayload ToPayload(WorldTask task) => new(
            task.Id,
            task.Status,
            task.OfferState,
            task.TargetUserId,
            task.TargetUser?.Username,
            task.ClaimedByMinecraftUsername,
            task.ClaimedByServerId,
            task.ErrorMessage,
            task.FormSubmissionProgressId,
            task.FieldName,
            task.ExpiresAt.HasValue ? DateTime.SpecifyKind(task.ExpiresAt.Value, DateTimeKind.Utc) : null,
            task.OfferedAt.HasValue ? DateTime.SpecifyKind(task.OfferedAt.Value, DateTimeKind.Utc) : null,
            task.ReleaseCount);

        /// <summary>The chat label: <c>Town › Location</c> (as <c>/knk task</c> shows it), or the field alone.</summary>
        public static string Label(WorldTask task)
        {
            var type = task.WorkflowSession?.EntityTypeName;
            var field = task.FieldName;
            if (!string.IsNullOrWhiteSpace(type) && !string.IsNullOrWhiteSpace(field)) return type.Trim() + " › " + field.Trim();
            if (!string.IsNullOrWhiteSpace(field)) return field.Trim();
            return string.IsNullOrWhiteSpace(type) ? task.TaskType : type.Trim();
        }

        public static int? ScanIdOf(string? inputJson)
        {
            if (string.IsNullOrWhiteSpace(inputJson)) return null;
            try
            {
                using var doc = JsonDocument.Parse(inputJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("scanId", out var id) && id.TryGetInt32(out var value))
                {
                    return value;
                }
            }
            catch (JsonException)
            {
            }
            return null;
        }
    }

    /// <summary>Plugin permission nodes the world-task flow relies on (plugin.yml).</summary>
    public static class WorldTaskNodes
    {
        /// <summary>May claim and run world tasks in game; the "Hand off to…" picker lists online holders.</summary>
        public const string Tasks = "knk.tasks";
    }

    /// <summary>No hub (tests and hosts without realtime): nobody is online, nothing is sent.</summary>
    public sealed class NoWorldTaskRealtime : IWorldTaskRealtime
    {
        public static readonly NoWorldTaskRealtime Instance = new();

        public string? ServerOf(string? uuid) => null;
        public void Changed(WorldTask task) { }
        public void Offered(WorldTask task, string serverId, string uuid, string mode, string? fromUsername) { }
        public void Cancelled(WorldTask task, string? uuid, string? reason) { }
        public void OfferWithdrawn(WorldTask task, string uuid, string? reason) { }
        public void HeadlessPending(WorldTask task) { }
    }
}
