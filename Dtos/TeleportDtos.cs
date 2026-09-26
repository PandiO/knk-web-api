using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// Body for POST /api/users/{id}/teleport-audit (docs/specs/teleport/DESIGN.md §3.10): one
    /// staff teleport made in-game, recorded as a PlayerTeleported audit entry on user {id} - the
    /// visited player for "/tp &lt;player&gt;", otherwise the moved player. The acting staff member
    /// is not part of the body: like every other plugin write, it comes from the X-Acting-User-Id
    /// header (honoured only with the plugin key when Security:PluginApiKey is set).
    /// </summary>
    public class TeleportAuditDto
    {
        /// <summary>The plugin's TeleportKind name, e.g. "STAFF" (see UserService.TeleportAuditKinds).</summary>
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = null!;

        /// <summary>The player who moved.</summary>
        [JsonPropertyName("subjectUserId")]
        public int SubjectUserId { get; set; }

        /// <summary>The player whose location was the destination, when there was one.</summary>
        [JsonPropertyName("visitedUserId")]
        public int? VisitedUserId { get; set; }

        [JsonPropertyName("from")]
        public TeleportAuditPointDto From { get; set; } = null!;

        [JsonPropertyName("to")]
        public TeleportAuditPointDto To { get; set; } = null!;

        /// <summary>The destination domain (warps, Phase 5); null for player/coordinate teleports.</summary>
        [JsonPropertyName("domainId")]
        public int? DomainId { get; set; }

        /// <summary>Whether the moved/visited player was left un-notified (-s, or a vanished staff member).</summary>
        [JsonPropertyName("silent")]
        public bool Silent { get; set; }

        /// <summary>Optional free-text reason given by the staff member.</summary>
        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        /// <summary>How it was started: "command" (a staff member in game) or "console". Defaults to "command".</summary>
        [JsonPropertyName("via")]
        public string? Via { get; set; }
    }

    public class TeleportAuditPointDto
    {
        [JsonPropertyName("world")]
        public string World { get; set; } = null!;

        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }

        [JsonPropertyName("z")]
        public double Z { get; set; }
    }
}
