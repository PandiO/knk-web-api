# Bundled FormConfiguration seeds (KNG-138)

One portable `knk-formconfig/1` file per configuration, named `{EntityTypeName}.json` (or a more specific
name such as `SiegeScenarioDistrict.join.json`). The API lists them at `GET api/FormConfigurations/seeds` and
imports one on request with `POST api/FormConfigurations/seeds/{name}/import?dryRun=&makeDefault=` (the web app's
"Import / seeds" panel on the form configuration list). There is no startup seeder.

- Import never overwrites: a file whose `configurationGuid` is already in the database writes nothing. To ship a
  changed seed, give it a new `configurationGuid` and name.
- A seed becomes the entity's default only when the entity has none, or when "make default" is asked.
- Join entries and child forms must be imported before the configurations that refer to them
  (`subConfigurationRef`), and before validation rules whose `dependsOn` names another configuration.
- Every file here should pass the dry run in a test (see `FormConfigurationSeedFilesTests`).

Format and authoring guide: knk-workspace `docs/specs/form-configurations/DEFAULT_FORM_SEEDS.md` §3-4.

## What is here (KNG-139, 2026-10-12)

| Seed | Configuration | Import after |
|---|---|---|
| `GateStructure` | GateStructure - Default (seed v2) | — |
| `GateDoor` | GateDoor - Default (seed v2) | — (its rules read its own Gate field) |
| `SiegeScenarioDistrict.join` | SiegeScenarioDistrict - Join Entry (seed v1) | — |
| `SiegeScenarioGate.join` | SiegeScenarioGate - Join Entry (seed v1) | — |
| `SiegeLobbyScenario.join` | SiegeLobbyScenario - Join Entry (seed v1) | — |
| `SiegeScenario` | SiegeScenario - Default (seed v1) | `SiegeScenarioDistrict.join`, `SiegeScenarioGate.join` |
| `SiegeTeam` | SiegeTeam - Default (seed v1) | — |
| `SiegeSpawnpoint` | SiegeSpawnpoint - Default (seed v1) | `SiegeScenario` (location rule reads the scenario's Town) |
| `SiegeObjective` | SiegeObjective - Default (seed v1) | `SiegeScenario` (same) |
| `SiegeLobby` | SiegeLobby - Default (seed v1) | `SiegeLobbyScenario.join` |

So one working order is: the three `.join` seeds, `SiegeScenario`, `SiegeTeam`, `SiegeSpawnpoint`,
`SiegeObjective`, `SiegeLobby`, then `GateStructure` and `GateDoor`. The dry run names the missing seed if the
order is wrong.

**GateDoor seed v2 (KNG-193, 2026-10-19):** a new `configurationGuid` and name, so it imports next to an already
imported v1 (never over it); make it the default and delete or keep v1 by hand. Changes: the travel distance field is
also shown for rotating doors and sets a drawbridge's deck length in blocks when there is no open-door scan (design
decision D2); a new optional "Door materials" field (`ScanMaterialWhitelist`) on region and flood-fill doors, needed
to capture an arched door with `//sel convex` without the wall inside the selection (D8); "Shape from" says the plane
grid is for rectangular doors only. Every step and field GUID of v2 is derived from its v1 GUID (uuid5, namespace =
the v1 configuration GUID), so the two never share one.

**GateStructure seed v2 (KNG-229, 2026-10-21):** a new `configurationGuid` and name, so it imports next to the
imported v1; make it the default and delete or keep v1 by hand. Adds the domain settings the Structure form has:
"Entry rule on roads" (`RoadAccessOverride`) on Region and access, "Navigate to" (`NavigationDefaultOverride`) on Spawn
point, and a new "Warp destination" step (`TeleportEnabled`, `TeleportPriceGems`, `TeleportMinTitleBracketId`,
`TeleportMinPremiumGroupId` limited to premium tiers, `TeleportRequiresDiscovery` default on). Needs the knk-web-api
change of the same issue: before it, `GateStructureService` ignored the warp settings. A gate saved without the warp
step (the plugin, v1) keeps its settings. GUIDs: configuration uuid5(v1 configuration GUID, "seed-v2"), every kept
step/field uuid5(v1 configuration GUID, its v1 GUID), new ones uuid5(v2 configuration GUID, "step:"/"field:" + name).

The gate seeds were written from the docs and the models; no export of the dev DB's configurations 9/12 was
available. The siege seeds convert `siege-minigame/PHASE_3_FORMCONFIGS.md` (configurations 23-33). What changed
against those forms, and what was left off on purpose: knk-workspace
`docs/reports/2026-10-11-form-wizard-chain.md`, link 7.
