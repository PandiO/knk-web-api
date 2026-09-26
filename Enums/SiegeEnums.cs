namespace knkwebapi_v2.Enums;

// Siege Phase 2 enums (docs/specs/siege-minigame/DESIGN.md §3.4, §3.8, §3.10, §8.5). Stored as
// strings (HasConversion<string>()) and serialized by name by the global JsonStringEnumConverter.

// Drives defaults only (initial objective holder, timeout fallback, UI wording) - scoring is
// holder-relative (DESIGN §3.4, §7.1).
public enum SiegeTeamRole
{
    Defender,
    Attacker
}

// DESIGN D1. MVP ships Continuous; Scheduled is Phase 10 (ScheduleJson is reserved for it).
public enum SiegeLobbyMode
{
    Continuous,
    Scheduled
}

public enum SiegeMatchStatus
{
    Created,
    InProgress,
    Completed,
    Aborted
}

public enum SiegeMatchEndReason
{
    InstantVictory,
    TimeExpired,
    TeamEliminated,
    NotEnoughPlayers,
    AdminStopped,
    ServerRestart
}

// DESIGN §8.5 degrade switch: PreLockdownView = per-player fake blocks + virtual collision +
// temporary pass-through; PassThroughOnly = no fake rendering, only the TELEPORT pass-through on
// gates that were open before the lockdown.
public enum SiegeNonMemberGateView
{
    PreLockdownView,
    PassThroughOnly
}
