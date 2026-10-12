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
| `GateStructure` | GateStructure - Default (seed v1) | — |
| `GateDoor` | GateDoor - Default (seed v1) | — (its rules read its own Gate field) |
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

The gate seeds were written from the docs and the models; no export of the dev DB's configurations 9/12 was
available. The siege seeds convert `siege-minigame/PHASE_3_FORMCONFIGS.md` (configurations 23-33). What changed
against those forms, and what was left off on purpose: knk-workspace
`docs/reports/2026-10-11-form-wizard-chain.md`, link 7.
