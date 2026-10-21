namespace knkwebapi_v2.Enums;

/// <summary>
/// What duelists fight with (arena duels DESIGN.md D9, §8.4). An Arena allows a subset; the proposal picks one.
/// Stored as a string.
/// </summary>
public enum DuelGearMode
{
    /// <summary>Own gear; the inventory is kept on death.</summary>
    OWN_GEAR_KEEP,
    /// <summary>Own gear; the loser's inventory goes to the winner.</summary>
    OWN_GEAR_DROP,
    /// <summary>Both duelists use the kit the proposal names.</summary>
    FIXED_KIT,
    /// <summary>Each duelist picks one of the arena's eligible kits.</summary>
    KIT_CHOICE
}
