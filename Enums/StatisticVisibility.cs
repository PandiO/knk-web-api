namespace knkwebapi_v2.Enums;

/// <summary>
/// Who may see a configurable player statistic (KNG-34, knk-workspace
/// docs/specs/player-statistics/DESIGN.md §F.4). Stored as a byte in player_stat_visibility.
/// No row means <see cref="Nobody"/>. <see cref="Friends"/> fails closed (treated as Nobody)
/// until KNG-35 supplies friend relationships.
/// </summary>
public enum StatisticVisibility : byte
{
    Nobody = 0,
    Friends = 1,
    Everyone = 2
}
