namespace knkwebapi_v2.Realtime.Contracts;

/// <summary>
/// S3 (PandiO/knk-workspace#182, DESIGN §6.2–§6.4): configuration and entity changes the game server follows, and the
/// worlds report it sends. All API → game server events go to every server. Payloads say <i>what</i> changed; the plugin
/// re-reads through the refresh paths it already has (P1, P3).
/// </summary>
public static class ConfigEventTypes
{
    /// <summary>
    /// An entity of a type the plugin caches was created, changed or deleted (DESIGN §6.3, the allow-list in
    /// <see cref="RealtimeEntityChangeMap"/>). Payload <see cref="EntityChangedPayload"/>.
    /// </summary>
    public const string EntityChanged = "EntityChanged";

    /// <summary>
    /// A gate structure, one of its doors or their block snapshots changed in the web app (definition edits only; the
    /// game server's own runtime writes - health, open state, operational toggles, siege lockdown - send nothing).
    /// Payload <see cref="GateChangedPayload"/>.
    /// </summary>
    public const string GateChanged = "GateChanged";

    /// <summary>A domain's AllowEntry/AllowExit, region id or sub-regions changed. Payload <see cref="DomainAccessChangedPayload"/>.</summary>
    public const string DomainAccessChanged = "DomainAccessChanged";

    /// <summary>Game settings (incl. hub settings) changed. Payload <see cref="ConfigChangedPayload"/>.</summary>
    public const string GameSettingsChanged = "GameSettingsChanged";

    /// <summary>Siege configuration, a lobby or a scenario (teams, spawnpoints, objectives) changed. Payload <see cref="ConfigChangedPayload"/>.</summary>
    public const string SiegeConfigChanged = "SiegeConfigChanged";

    /// <summary>Lootbox configuration, a type, a spawn area or a special entry changed. Payload <see cref="ConfigChangedPayload"/>.</summary>
    public const string LootboxConfigChanged = "LootboxConfigChanged";

    /// <summary>A domain's teleport settings (warp) changed. Payload <see cref="ConfigChangedPayload"/>.</summary>
    public const string TeleportDestinationsChanged = "TeleportDestinationsChanged";

    /// <summary>Navigation compass settings, type defaults or a domain's navigation/road override changed. Payload <see cref="ConfigChangedPayload"/>.</summary>
    public const string NavigationSettingsChanged = "NavigationSettingsChanged";

    /// <summary>Road tiles of a world, or the road profiles, changed. Payload <see cref="RoadNetworkChangedPayload"/>.</summary>
    public const string RoadNetworkChanged = "RoadNetworkChanged";

    /// <summary>The owner's telemetry settings (enhanced targets, test runs) changed. Payload <see cref="ConfigChangedPayload"/>.</summary>
    public const string TelemetryConfigChanged = "TelemetryConfigChanged";

    // ---- game server → API (hub invocation) ----

    /// <summary>
    /// <c>GameServerHub.ReportWorlds</c>: the loaded worlds, the same body as <c>PUT api/GameSettings/runtime-worlds</c>;
    /// replaces the plugin's REST report while the hub is connected (REST stays the fallback).
    /// </summary>
    public const string ReportWorlds = "ReportWorlds";
}

/// <summary>What happened to an entity in <see cref="EntityChangedPayload"/> and <see cref="GateChangedPayload"/>.</summary>
public static class EntityChangeOps
{
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string Deleted = "Deleted";
}

/// <param name="Type">The entity type name (e.g. <c>Town</c>, <c>ItemBlueprint</c>).</param>
/// <param name="Id">Its id; null when many entities of the type changed at once (one save over
/// <see cref="RealtimeEntityChangeMap.CollapseAbove"/>): the receiver drops everything it caches of that type.</param>
/// <param name="Op">See <see cref="EntityChangeOps"/>.</param>
/// <param name="Key">The natural key the plugin caches some types by, when there is one: a user's Minecraft UUID, a
/// domain's or sub-region's WorldGuard region id, a menu template's key.</param>
public sealed record EntityChangedPayload(string Type, int? Id, string Op, string? Key = null);

/// <param name="GateStructureId">The gate structure whose definition (or one of whose doors) changed.</param>
/// <param name="Op"><see cref="EntityChangeOps.Deleted"/> when the structure itself was deleted, else Created/Updated.</param>
public sealed record GateChangedPayload(int GateStructureId, string Op);

/// <param name="DomainId">The domain (town, district, structure, ...) whose access rules or regions changed.</param>
public sealed record DomainAccessChangedPayload(int DomainId);

/// <param name="Scope">Which part changed (informational; the handler refreshes the whole section). See <see cref="ConfigScopes"/>.</param>
public sealed record ConfigChangedPayload(string Scope);

/// <param name="World">The world whose road tiles changed; null = road profiles or every world.</param>
public sealed record RoadNetworkChangedPayload(string? World);

/// <summary>Values of <see cref="ConfigChangedPayload.Scope"/>.</summary>
public static class ConfigScopes
{
    public const string Game = "game";
    public const string Hub = "hub";
    public const string Configuration = "configuration";
    public const string Lobby = "lobby";
    public const string Scenario = "scenario";
    public const string Type = "type";
    public const string SpawnArea = "spawnArea";
    public const string SpecialEntry = "specialEntry";
    public const string Domain = "domain";
    public const string Compass = "compass";
    public const string Defaults = "defaults";
    public const string EnhancedTargets = "enhancedTargets";
    public const string TestRuns = "testRuns";
}
