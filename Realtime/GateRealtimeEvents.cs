using knkwebapi_v2.Realtime.Contracts;

namespace knkwebapi_v2.Realtime;

/// <summary>
/// S3 (PandiO/knk-workspace#182, DESIGN §6.2): <c>GateChanged</c>, sent by <c>GateStructureService</c> and
/// <c>GateDoorService</c> for definition edits (create, update, delete, overrides, region data, block snapshots). The
/// runtime writes the game server makes itself (health, open state, operational toggles) and the siege lockdown/restore
/// (<c>SiegeMatchGateService</c>) send nothing: the server already holds that state, and a reload in the middle of a
/// match would undo it. The plugin applies an event for a gate a running match holds after the match (decision Q12).
/// </summary>
public static class GateRealtimeEvents
{
    public static void GateChanged(this IRealtimeEventSink? sink, int gateStructureId, string op)
    {
        if (sink == null || gateStructureId <= 0) return;
        sink.Publish(new RealtimeEvent(ConfigEventTypes.GateChanged, new GateChangedPayload(gateStructureId, op)));
    }
}
