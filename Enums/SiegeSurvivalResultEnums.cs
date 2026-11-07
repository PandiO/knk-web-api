namespace knkwebapi_v2.Enums;

/// <summary>
/// Siege Survival phase 5 (knk-workspace docs/specs/siege-survival/DESIGN.md §9, §10; PandiO/knk-workspace#41,
/// link S8a): who wrote a match's durable result row. <c>Plugin</c> = the game server posted the S4
/// <c>SurvivalMatchResultDto</c> at match end; <c>Abort</c> = the API wrote it itself when the Siege match was aborted
/// (admin stop, restart, API outage) before the plugin posted one, so history is complete (S61, S62: nothing settled).
/// </summary>
public enum SurvivalMatchResultSource
{
    Plugin,
    Abort
}
