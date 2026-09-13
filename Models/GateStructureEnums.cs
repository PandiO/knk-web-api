namespace knkwebapi_v2.Models;

public enum GateType
{
    SLIDING,
    TRAP,
    DRAWBRIDGE,
    DOUBLE_DOORS
}

public enum GeometryDefinitionMode
{
    PLANE_GRID,
    FLOOD_FILL,
    REGION
}

public enum MotionType
{
    VERTICAL,
    LATERAL,
    ROTATION
}

public enum TileEntityPolicy
{
    NONE,
    DECORATIVE_ONLY,
    ALL
}

public enum HealthDisplayMode
{
    ALWAYS,
    DAMAGED_ONLY,
    NEVER,
    SIEGE_ONLY
}

public enum GateInfoDisplayMode
{
    ALWAYS,
    NEVER,
    SIEGE_ONLY
}

// Replaces the old GateStructure.IsOpened + IsJammed boolean pair (see GateDoor.OpenedState).
// Mirrors the plugin's in-memory AnimationState (knk-core/.../domain/gates/AnimationState.java)
// plus a distinct JAMMED state, since a jammed door is stuck mid-motion rather than cleanly
// open or closed - two independent booleans allowed that nonsensical combination.
public enum GateDoorOpenState
{
    CLOSED,
    OPENING,
    OPEN,
    CLOSING,
    JAMMED
}

// Replaces the old free-form FaceDirection string (validated ad hoc against a hardcoded
// lowercase-hyphenated allowlist in GateDoorsController) with a proper enum, for compile-time
// type safety and a single source of truth for valid values.
public enum GateFaceDirection
{
    NORTH,
    NORTH_EAST,
    EAST,
    SOUTH_EAST,
    SOUTH,
    SOUTH_WEST,
    WEST,
    NORTH_WEST
}