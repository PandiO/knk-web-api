using System.Text.Json.Serialization;
using knkwebapi_v2.Realtime.Contracts;

namespace knkwebapi_v2.Dtos;

/// <summary>Body of <c>POST api/GateStructures/{id}/commands</c> (PandiO/knk-workspace#741, spec § 6.2.5).</summary>
public class GateCommandRequestDto
{
    /// <summary><c>open</c>, <c>close</c>, <c>toggle</c> or <c>repair</c> (any case).</summary>
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    /// <summary>One door of the gate; left out (null) for every door of the gate.</summary>
    [JsonPropertyName("doorId")]
    public int? DoorId { get; set; }
}

/// <summary>
/// What a gate command ended with. <c>result</c> is one of <see cref="GateCommandOutcomes"/>: <c>done</c> and
/// <c>refused</c> (the game server's answer, 200), <c>siege</c> (409), <c>no-game-server</c> (503) or <c>timeout</c>
/// (504). Read the gate again for the stored state: an animation may still run when the result comes back.
/// </summary>
public class GateCommandResponseDto
{
    [JsonPropertyName("commandId")]
    public string CommandId { get; set; } = string.Empty;

    [JsonPropertyName("gateId")]
    public int GateId { get; set; }

    [JsonPropertyName("doorId")]
    public int? DoorId { get; set; }

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("result")]
    public string Result { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>The doors the game server acted on, with their state right after the command (empty without a result).</summary>
    [JsonPropertyName("doors")]
    public List<GateCommandDoorStateDto> Doors { get; set; } = new();
}
