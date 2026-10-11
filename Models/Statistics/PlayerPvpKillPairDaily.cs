using System;

namespace knkwebapi_v2.Models;

/// <summary>PvP kills of one victim by one killer per local day and context — the input of the
/// leaderboard repeat-victim cap (DESIGN.md §F.11; read in link 5).</summary>
public class PlayerPvpKillPairDaily
{
    public int KillerUserId { get; set; }

    public int VictimUserId { get; set; }

    public DateOnly Day { get; set; }

    public string ContextKey { get; set; } = "";

    public int Count { get; set; }
}
