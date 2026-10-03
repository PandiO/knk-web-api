using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Services.Telemetry
{
    /// <summary>One known diagnostic event: its level, emitting side and the payload keys it may carry.</summary>
    public sealed record TelemetryEventDefinition(
        string Name,
        TelemetryLevel Level,
        TelemetrySource Source,
        IReadOnlySet<string> PayloadKeys,
        string Description)
    {
        /// <summary>The part before the first dot (<c>siege</c> for <c>siege.match_join</c>).</summary>
        public string Family => Name[..Name.IndexOf('.')];
    }

    /// <summary>
    /// The allowlist of diagnostic events (KNG-34, DESIGN.md §F.12, IMPLEMENTATION_PLAN.md §3.3). An
    /// event whose name is not listed is rejected; payload keys not listed for the name are dropped.
    /// Payload keys are ids, codes, counts and coarse positions only — nothing here may carry chat,
    /// command arguments, IPs, tokens, raw bodies, inventories or exception messages. Adding an event
    /// means adding a line here (and to the plugin's emitter).
    /// </summary>
    public static class TelemetryEventCatalog
    {
        public const int MaxPayloadKeys = 16;
        public const int MaxStringLength = 128;

        /// <summary>Event names: <c>&lt;family&gt;.&lt;event&gt;</c>, lower snake case.</summary>
        public static readonly Regex NamePattern = new("^[a-z][a-z0-9_]{0,30}\\.[a-z][a-z0-9_]{0,30}$", RegexOptions.Compiled);

        /// <summary>Stable codes (reason codes, feature/action/object identifiers): no spaces, no free text.</summary>
        public static readonly Regex CodePattern = new("^[A-Za-z0-9_.:@/\\-]{1,64}$", RegexOptions.Compiled);

        private static readonly IReadOnlyDictionary<string, TelemetryEventDefinition> Definitions = Build(
            // Sessions (plugin).
            Baseline("session.join", "Joined the server (after the account is loaded).", "world", "gameMode", "firstJoin"),
            Baseline("session.leave", "Left the server.", "reason", "sessionSeconds"),
            Baseline("session.afk_changed", "AFK state changed.", "afk", "manual"),
            // Menus (plugin, via MenuObserver).
            Baseline("menu.opened", "An inventory menu was opened.", "menuKey", "parentMenuKey"),
            Baseline("menu.action", "A menu action ran (action type id + outcome).", "menuKey", "actionType", "slot"),
            // Commands: label and outcome only, never arguments.
            Baseline("command.result", "A command was entered (label only).", "command", "cancelled"),
            // Siege lobby and match (plugin, via SiegeMatchObserver).
            Baseline("siege.lobby_join_attempt", "A player asked to join a Siege lobby.", "lobbyId", "phase", "members", "capacity"),
            Baseline("siege.vote_cast", "A scenario vote.", "lobbyId", "scenarioId", "previousScenarioId"),
            Baseline("siege.team_assignment", "A player was put on a team.", "lobbyId", "teamId", "allianceGroup", "cause"),
            Baseline("siege.match_join", "A player started taking part in a match.", "lobbyId", "teamId", "allianceGroup"),
            Baseline("siege.match_leave", "A player stopped taking part in a match.", "lobbyId", "teamId", "cause"),
            Baseline("siege.match_phase", "A lobby or match changed phase.", "lobbyId", "from", "to", "cause", "scenarioId", "members"),
            Baseline("siege.objective_captured", "An objective changed hands.", "lobbyId", "objectiveId", "teamId", "allianceGroup"),
            Baseline("siege.gate_destroyed", "A gate door was destroyed.", "lobbyId", "gateId"),
            // Discoveries and the ledger.
            Baseline("discovery.granted", "A domain discovery was granted.", "domainId", "domainType"),
            Baseline("currency.posting", "A ledger posting (reference only; amounts stay in the ledger).", "transactionPublicId", "reasonCode"),
            // Failures.
            Baseline("api.call_failed", "A plugin → API call failed.", "method", "route", "status", "exceptionType"),
            ApiBaseline("api.request_failed", "The API answered 5xx or threw.", "method", "route", "status", "exceptionType"),
            // Self-reporting of drops (both sides).
            Baseline("telemetry.dropped", "Diagnostic events were dropped (bounded buffer).", "dropped", "reason"),
            ApiBaseline("telemetry.queue_dropped", "The API's write queue dropped events.", "dropped", "reason"),
            // Enhanced (named players or test runs only).
            Enhanced("movement.sample", "Position sample.", "world", "x", "y", "z", "mode"),
            Enhanced("menu.click", "A menu slot was clicked.", "menuKey", "slot", "itemKey", "clickType"),
            Enhanced("combat.hit", "A hit between players or mobs.", "attackerUserId", "victimUserId", "victimType", "cause", "damage", "context"),
            Enhanced("gate.hit", "A gate door was damaged.", "gateId", "damage", "cause"));

        public static IReadOnlyCollection<TelemetryEventDefinition> All => Definitions.Values.ToList();

        public static TelemetryEventDefinition? Find(string? name) =>
            name != null && Definitions.TryGetValue(name, out var def) ? def : null;

        /// <summary>Plugin-emitted event names of a level (served to the plugin by GET config).</summary>
        public static List<string> PluginEventNames(TelemetryLevel level) => Definitions.Values
            .Where(d => d.Level == level && d.Source == TelemetrySource.Plugin)
            .Select(d => d.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        private static TelemetryEventDefinition Baseline(string name, string description, params string[] keys) =>
            new(name, TelemetryLevel.Baseline, TelemetrySource.Plugin, Keys(keys), description);

        private static TelemetryEventDefinition ApiBaseline(string name, string description, params string[] keys) =>
            new(name, TelemetryLevel.Baseline, TelemetrySource.Api, Keys(keys), description);

        private static TelemetryEventDefinition Enhanced(string name, string description, params string[] keys) =>
            new(name, TelemetryLevel.Enhanced, TelemetrySource.Plugin, Keys(keys), description);

        private static IReadOnlySet<string> Keys(string[] keys)
        {
            if (keys.Length > MaxPayloadKeys) throw new InvalidOperationException("Too many payload keys.");
            return new HashSet<string>(keys, StringComparer.Ordinal);
        }

        private static IReadOnlyDictionary<string, TelemetryEventDefinition> Build(params TelemetryEventDefinition[] definitions)
        {
            var map = new Dictionary<string, TelemetryEventDefinition>(StringComparer.Ordinal);
            foreach (var def in definitions)
            {
                if (!NamePattern.IsMatch(def.Name)) throw new InvalidOperationException($"Bad telemetry event name {def.Name}.");
                map.Add(def.Name, def);
            }
            return map;
        }
    }
}
