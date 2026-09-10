using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// Well-known WorldTask.TaskType values that are handled without a player (headless).
    /// </summary>
    public static class WorldTaskTypes
    {
        public const string GateBlockScan = "GateBlockScan";

        // Sibling of GateBlockScan: same scan geometry, anchored at the gate's
        // OpenAnchorPoint instead of AnchorPoint, and posts to GateOpenedBlockSnapshot
        // instead of GateBlockSnapshot. See ROTATION_GAP_FILL_DESIGN.md.
        public const string GateOpenedBlockScan = "GateOpenedBlockScan";
    }

    /// <summary>
    /// InputJson payload for a GateBlockScan WorldTask.
    /// The plugin re-fetches full gate geometry via GateStructuresApi.getById(GateStructureId);
    /// this payload only needs to route the task to the right gate.
    /// </summary>
    public class GateBlockScanRequestDto
    {
        [JsonPropertyName("gateStructureId")]
        public int GateStructureId { get; set; }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum GateBlockScanStatus
    {
        Success,
        Warning,
        Failed
    }

    /// <summary>
    /// OutputJson payload produced by the plugin once a GateBlockScan WorldTask finishes.
    /// </summary>
    public class GateBlockScanResultDto
    {
        [JsonPropertyName("status")]
        public GateBlockScanStatus Status { get; set; }

        [JsonPropertyName("blockCount")]
        public int BlockCount { get; set; }

        [JsonPropertyName("snapshots")]
        public List<GateBlockSnapshotCreateDto> Snapshots { get; set; } = new();

        [JsonPropertyName("warnings")]
        public List<string> Warnings { get; set; } = new();

        [JsonPropertyName("errorMessage")]
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// OutputJson payload produced by the plugin once a GateOpenedBlockScan WorldTask finishes.
    /// Mirrors GateBlockScanResultDto exactly - same scan geometry, different target table.
    /// </summary>
    public class GateOpenedBlockScanResultDto
    {
        [JsonPropertyName("status")]
        public GateBlockScanStatus Status { get; set; }

        [JsonPropertyName("blockCount")]
        public int BlockCount { get; set; }

        [JsonPropertyName("snapshots")]
        public List<GateOpenedBlockSnapshotCreateDto> Snapshots { get; set; } = new();

        [JsonPropertyName("warnings")]
        public List<string> Warnings { get; set; } = new();

        [JsonPropertyName("errorMessage")]
        public string? ErrorMessage { get; set; }
    }
}
