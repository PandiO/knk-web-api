using knkwebapi_v2.Models;
using knkwebapi_v2.Realtime.Contracts;

namespace knkwebapi_v2.Realtime;

/// <summary>What happened to a tracked entity in one save.</summary>
public enum RealtimeEntityState
{
    Added,
    Modified,
    Deleted,
}

/// <summary>
/// One entity written by a successful save, as <see cref="RealtimeEntityChangeInterceptor"/> saw it: its model type,
/// what happened, the properties that changed (Modified only) and the few values the events need, read after the save
/// (so a new row's id is known).
/// </summary>
/// <param name="EntityType">The model's CLR type (a <c>Town</c> is a <c>Town</c>, not a <c>Domain</c>).</param>
/// <param name="Values">Values of the properties in <see cref="RealtimeEntityChangeMap.ValueNames"/> the type has.</param>
public sealed record RealtimeEntityChange(Type EntityType, RealtimeEntityState State, IReadOnlySet<string> Modified,
    IReadOnlyDictionary<string, object?> Values)
{
    public object? Value(string name) => Values.TryGetValue(name, out var value) ? value : null;
}

/// <summary>
/// S3 (PandiO/knk-workspace#182, DESIGN §6.2–§6.3, decision Q3 option A): turns the entities one save wrote into the
/// realtime events for the game servers. Pure and deterministic, so the rules are tested without a database:
/// <list type="bullet">
///   <item><c>EntityChanged</c> for the DESIGN §6.3 allow-list (types the plugin caches); a save that touches more than
///     <see cref="CollapseAbove"/> entities of one type sends one event without an id ("drop all of that type").</item>
///   <item><c>DomainAccessChanged</c> when a domain's AllowEntry/AllowExit or region id, or a sub-region, changed.</item>
///   <item><c>GameSettingsChanged</c>, <c>SiegeConfigChanged</c>, <c>LootboxConfigChanged</c>,
///     <c>TeleportDestinationsChanged</c>, <c>NavigationSettingsChanged</c>, <c>RoadNetworkChanged</c>,
///     <c>TelemetryConfigChanged</c> for the configuration entities DESIGN §6.2 names.</item>
/// </list>
/// Changes the game server makes itself and needs no echo of are left out by property: presence/last-seen/password
/// columns of a user, the runtime worlds report of the game settings, a road tile's dirty flag. <c>GateChanged</c> is
/// not derived here: the gate services send it for definition edits only (see <see cref="GateRealtimeEvents"/>).
/// Duplicates within a save are sent once.
/// </summary>
public static class RealtimeEntityChangeMap
{
    /// <summary>More entities of one allow-listed type than this in one save → one <c>EntityChanged</c> without an id.</summary>
    public const int CollapseAbove = 50;

    /// <summary>The values the rules read; the interceptor copies those the entity has.</summary>
    public static readonly IReadOnlyList<string> ValueNames = new[]
    {
        "Id", "Uuid", "WgRegionId", "Key", "DomainId", "World", "TeleportEnabled",
    };

    /// <summary>The DESIGN §6.3 allow-list: type name → the value used as <see cref="EntityChangedPayload.Key"/> (or null).</summary>
    public static readonly IReadOnlyDictionary<Type, string?> EntityTypes = new Dictionary<Type, string?>
    {
        [typeof(User)] = "Uuid",
        [typeof(Town)] = "WgRegionId",
        [typeof(District)] = "WgRegionId",
        [typeof(Structure)] = "WgRegionId",
        [typeof(DomainSubRegion)] = "WgRegionId",
        [typeof(ItemBlueprint)] = null,
        [typeof(EnchantmentDefinition)] = null,
        [typeof(Kit)] = null,
        [typeof(MenuTemplate)] = "Key",
        [typeof(MinecraftMaterialRef)] = null,
        [typeof(Grade)] = null,
        [typeof(Tag)] = null,
        [typeof(TitleBracket)] = null,
        [typeof(PermissionGroup)] = null,
    };

    /// <summary>
    /// Properties whose change alone sends no <c>EntityChanged</c>: written by the game server or the auth flow,
    /// not cached by the plugin, or already pushed by their own S2 event.
    /// </summary>
    private static readonly IReadOnlyDictionary<Type, HashSet<string>> IgnoredProperties = new Dictionary<Type, HashSet<string>>
    {
        [typeof(User)] = new(StringComparer.Ordinal)
        {
            nameof(User.IsOnline), nameof(User.LastSeenAt), nameof(User.PasswordHash), nameof(User.LastPasswordChangeAt),
            nameof(User.LastEmailChangeAt), nameof(User.TokenVersion), nameof(User.LastSalaryPayoutAt),
            nameof(User.EmailVerified), nameof(User.EmailVerifiedAt), nameof(User.Email),
        },
    };

    private static readonly HashSet<string> DomainAccessProperties = new(StringComparer.Ordinal)
    {
        nameof(Domain.AllowEntry), nameof(Domain.AllowExit), nameof(Domain.WgRegionId),
    };

    private static readonly HashSet<string> SubRegionAccessProperties = new(StringComparer.Ordinal)
    {
        nameof(DomainSubRegion.WgRegionId), nameof(DomainSubRegion.DomainId),
    };

    private static readonly HashSet<string> TeleportProperties = new(StringComparer.Ordinal)
    {
        nameof(Domain.TeleportEnabled), nameof(Domain.TeleportPriceGems), nameof(Domain.TeleportMinTitleBracketId),
        nameof(Domain.TeleportMinPremiumGroupId), nameof(Domain.TeleportRequiresDiscovery), nameof(Domain.LocationId),
        nameof(Domain.Name), nameof(Domain.WorldName),
    };

    private static readonly HashSet<string> NavigationProperties = new(StringComparer.Ordinal)
    {
        nameof(Domain.NavigationDefaultOverride), nameof(Domain.RoadAccessOverride),
    };

    private static readonly HashSet<string> GameSettingsRuntimeProperties = new(StringComparer.Ordinal)
    {
        nameof(GameSettings.RuntimeWorldsJson), nameof(GameSettings.RuntimeWorldsLastUpdatedAt), nameof(GameSettings.UpdatedAt),
    };

    private static readonly HashSet<string> RoadTileRuntimeProperties = new(StringComparer.Ordinal)
    {
        nameof(RoadTile.Dirty),
    };

    /// <summary>Configuration entity → (event type, scope) for the "refresh this section" events (DESIGN §6.2).</summary>
    private static readonly IReadOnlyDictionary<Type, (string Type, string Scope)> ConfigEntities =
        new Dictionary<Type, (string, string)>
        {
            [typeof(SiegeConfiguration)] = (ConfigEventTypes.SiegeConfigChanged, ConfigScopes.Configuration),
            [typeof(SiegeLobby)] = (ConfigEventTypes.SiegeConfigChanged, ConfigScopes.Lobby),
            [typeof(SiegeLobbyScenario)] = (ConfigEventTypes.SiegeConfigChanged, ConfigScopes.Lobby),
            [typeof(SiegeScenario)] = (ConfigEventTypes.SiegeConfigChanged, ConfigScopes.Scenario),
            [typeof(SiegeScenarioDistrict)] = (ConfigEventTypes.SiegeConfigChanged, ConfigScopes.Scenario),
            [typeof(SiegeScenarioGate)] = (ConfigEventTypes.SiegeConfigChanged, ConfigScopes.Scenario),
            [typeof(SiegeTeam)] = (ConfigEventTypes.SiegeConfigChanged, ConfigScopes.Scenario),
            [typeof(SiegeSpawnpoint)] = (ConfigEventTypes.SiegeConfigChanged, ConfigScopes.Scenario),
            [typeof(SiegeObjective)] = (ConfigEventTypes.SiegeConfigChanged, ConfigScopes.Scenario),
            [typeof(LootboxConfiguration)] = (ConfigEventTypes.LootboxConfigChanged, ConfigScopes.Configuration),
            [typeof(LootboxType)] = (ConfigEventTypes.LootboxConfigChanged, ConfigScopes.Type),
            [typeof(LootboxTypeGradeWeight)] = (ConfigEventTypes.LootboxConfigChanged, ConfigScopes.Type),
            [typeof(LootboxPoolEntry)] = (ConfigEventTypes.LootboxConfigChanged, ConfigScopes.Type),
            [typeof(LootboxEnchantRoll)] = (ConfigEventTypes.LootboxConfigChanged, ConfigScopes.Type),
            [typeof(LootboxSpawnArea)] = (ConfigEventTypes.LootboxConfigChanged, ConfigScopes.SpawnArea),
            [typeof(LootboxSpawnAreaType)] = (ConfigEventTypes.LootboxConfigChanged, ConfigScopes.SpawnArea),
            [typeof(LootboxSpecialEntry)] = (ConfigEventTypes.LootboxConfigChanged, ConfigScopes.SpecialEntry),
            [typeof(NavigationCompassSettings)] = (ConfigEventTypes.NavigationSettingsChanged, ConfigScopes.Compass),
            [typeof(DomainNavigationDefault)] = (ConfigEventTypes.NavigationSettingsChanged, ConfigScopes.Defaults),
            [typeof(TelemetryEnhancedTarget)] = (ConfigEventTypes.TelemetryConfigChanged, ConfigScopes.EnhancedTargets),
            [typeof(TelemetryTestRun)] = (ConfigEventTypes.TelemetryConfigChanged, ConfigScopes.TestRuns),
        };

    /// <summary>The model types a save must be watched for (the interceptor skips everything else cheaply).</summary>
    public static bool IsWatched(Type entityType) =>
        EntityTypes.ContainsKey(entityType) || ConfigEntities.ContainsKey(entityType)
        || typeof(Domain).IsAssignableFrom(entityType) || entityType == typeof(GameSettings)
        || entityType == typeof(RoadTile) || entityType == typeof(RoadNode) || entityType == typeof(RoadEdge)
        || entityType == typeof(RoadProfile);

    /// <summary>The events for one save, in a stable order (entity events first, then configuration events), without duplicates.</summary>
    public static IReadOnlyList<RealtimeEvent> Map(IEnumerable<RealtimeEntityChange> changes, int? actorUserId = null)
    {
        var entityEvents = new List<EntityChangedPayload>();
        var other = new List<(string Type, object Payload)>();

        foreach (var change in changes)
        {
            AddEntityChanged(change, entityEvents);
            AddConfigEvents(change, other);
        }

        var result = new List<RealtimeEvent>();
        foreach (var group in entityEvents.GroupBy(e => e.Type, StringComparer.Ordinal))
        {
            var distinct = group.GroupBy(e => e.Id).Select(g => Strongest(g)).ToList();
            if (distinct.Count > CollapseAbove)
            {
                result.Add(new RealtimeEvent(ConfigEventTypes.EntityChanged,
                    new EntityChangedPayload(group.Key, null, EntityChangeOps.Updated), actorUserId));
                continue;
            }
            result.AddRange(distinct.Select(p => new RealtimeEvent(ConfigEventTypes.EntityChanged, p, actorUserId)));
        }

        var seen = new HashSet<(string, object)>();
        foreach (var (type, payload) in other)
        {
            if (seen.Add((type, payload)))
            {
                result.Add(new RealtimeEvent(type, payload, actorUserId));
            }
        }
        return result;
    }

    /// <summary>Deleted beats Created beats Updated for one entity written twice in a save; the last key wins.</summary>
    private static EntityChangedPayload Strongest(IEnumerable<EntityChangedPayload> payloads)
    {
        var list = payloads.ToList();
        var op = list.Any(p => p.Op == EntityChangeOps.Deleted) ? EntityChangeOps.Deleted
            : list.Any(p => p.Op == EntityChangeOps.Created) ? EntityChangeOps.Created : EntityChangeOps.Updated;
        var last = list[^1];
        return last with { Op = op, Key = list.LastOrDefault(p => p.Key != null)?.Key };
    }

    private static void AddEntityChanged(RealtimeEntityChange change, List<EntityChangedPayload> into)
    {
        if (!EntityTypes.TryGetValue(change.EntityType, out var keyName)) return;
        if (change.State == RealtimeEntityState.Modified && OnlyIgnored(change)) return;
        if (AsInt(change.Value("Id")) is not { } id || id <= 0) return;
        var key = keyName == null ? null : change.Value(keyName) as string;
        into.Add(new EntityChangedPayload(change.EntityType.Name, id, Op(change.State),
            string.IsNullOrWhiteSpace(key) ? null : key));
    }

    private static void AddConfigEvents(RealtimeEntityChange change, List<(string Type, object Payload)> into)
    {
        var type = change.EntityType;
        if (ConfigEntities.TryGetValue(type, out var config))
        {
            into.Add((config.Type, new ConfigChangedPayload(config.Scope)));
            return;
        }

        if (typeof(Domain).IsAssignableFrom(type))
        {
            var id = AsInt(change.Value("Id"));
            var whole = change.State != RealtimeEntityState.Modified;
            if (id is > 0 && (whole || Touches(change, DomainAccessProperties)))
            {
                into.Add((ConfigEventTypes.DomainAccessChanged, new DomainAccessChangedPayload(id.Value)));
            }
            var teleportEnabled = change.Value("TeleportEnabled") as bool? == true;
            if ((whole && teleportEnabled) || (!whole && Touches(change, TeleportProperties)
                    && (teleportEnabled || change.Modified.Contains(nameof(Domain.TeleportEnabled)))))
            {
                into.Add((ConfigEventTypes.TeleportDestinationsChanged, new ConfigChangedPayload(ConfigScopes.Domain)));
            }
            if (!whole && Touches(change, NavigationProperties))
            {
                into.Add((ConfigEventTypes.NavigationSettingsChanged, new ConfigChangedPayload(ConfigScopes.Domain)));
            }
            if (!whole && change.Modified.Contains(nameof(Domain.IsHub)))
            {
                into.Add((ConfigEventTypes.GameSettingsChanged, new ConfigChangedPayload(ConfigScopes.Hub)));
            }
            return;
        }

        if (type == typeof(DomainSubRegion))
        {
            if ((change.State != RealtimeEntityState.Modified || Touches(change, SubRegionAccessProperties))
                && AsInt(change.Value("DomainId")) is int domainId && domainId > 0)
            {
                into.Add((ConfigEventTypes.DomainAccessChanged, new DomainAccessChangedPayload(domainId)));
            }
            return;
        }

        if (type == typeof(GameSettings))
        {
            if (change.State != RealtimeEntityState.Modified || change.Modified.Any(p => !GameSettingsRuntimeProperties.Contains(p)))
            {
                into.Add((ConfigEventTypes.GameSettingsChanged, new ConfigChangedPayload(ConfigScopes.Game)));
            }
            return;
        }

        if (type == typeof(RoadTile) || type == typeof(RoadNode) || type == typeof(RoadEdge))
        {
            if (type == typeof(RoadTile) && change.State == RealtimeEntityState.Modified
                && change.Modified.All(RoadTileRuntimeProperties.Contains))
            {
                return;
            }
            var world = change.Value("World") as string;
            into.Add((ConfigEventTypes.RoadNetworkChanged, new RoadNetworkChangedPayload(string.IsNullOrWhiteSpace(world) ? null : world)));
            return;
        }

        if (type == typeof(RoadProfile))
        {
            into.Add((ConfigEventTypes.RoadNetworkChanged, new RoadNetworkChangedPayload(null)));
        }
    }

    private static bool OnlyIgnored(RealtimeEntityChange change) =>
        change.Modified.Count == 0
        || (IgnoredProperties.TryGetValue(change.EntityType, out var ignored) && change.Modified.All(ignored.Contains));

    private static bool Touches(RealtimeEntityChange change, HashSet<string> properties) =>
        change.Modified.Any(properties.Contains);

    private static string Op(RealtimeEntityState state) => state switch
    {
        RealtimeEntityState.Added => EntityChangeOps.Created,
        RealtimeEntityState.Deleted => EntityChangeOps.Deleted,
        _ => EntityChangeOps.Updated,
    };

    private static int? AsInt(object? value) => value switch
    {
        int i => i,
        long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
        _ => null,
    };
}
