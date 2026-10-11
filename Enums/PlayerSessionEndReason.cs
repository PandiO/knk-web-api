namespace knkwebapi_v2.Enums;

/// <summary>Why a statistics session ended (DESIGN.md §F.2, §F.3). <see cref="Timeout"/> is set
/// by the API when no heartbeat arrived for Statistics:SessionTimeoutMinutes.</summary>
public enum PlayerSessionEndReason : byte
{
    Quit = 0,
    ServerStop = 1,
    Kick = 2,
    Timeout = 3
}
