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

## Priority 2 and 3 (KNG-139, 2026-10-19)

Converted from the dev DB's own configurations (the `knk_uiux_seed` dump of 2026-10-07; the alpha DB had the same
42 configurations) and then reworked: every field checked against the current model, metadata and service rules,
steps regrouped per the authoring guide, a description on every step and field that explains the concept behind the
property, and the forms' known bugs fixed (listed below). Each seed is named "{Entity} - Default (seed v1)" or
"- Join Entry (seed v1)". Its `configurationGuid` is uuid5(namespace = the source configuration's GUID, "seed-v1"),
and every kept step/field GUID is uuid5(namespace = the source configuration's GUID, the old GUID). So a seed never
collides with the configuration it came from and imports next to it as a non-default; make it the default after the
live review.

| Group | Seeds (import the `.join` ones first) | Source configs |
|---|---|---|
| Domain | `Town`, `District`, `Street`, `Structure`, `Location` | 1, 2, 7, 10, 4 |
| People | `User`, `PermissionGroup`, `PermissionGrant`, `BannerLayer`, `BannerDesign`, `Clan` | 11, 22, 43, 23, 24, 25 |
| Items | `CategoryTag.join`, `ItemBlueprintDefaultEnchantment.join`, `ItemBlueprintOrigin.join`, `ItemBlueprintTag.join`, then `Grade`, `Tag`, `Category`, `EnchantmentDefinition`, `ItemBlueprint` | 16, 18, 19, 20, 13, 14, 17, 15, 21 |
| Kits and lootboxes | `KitContent.join`, `LootboxTypeGradeWeight.join`, `LootboxPoolEntry.join`, `LootboxEnchantRoll.join`, `LootboxSpawnAreaType.join`, then `Kit`, `LootboxType`, `LootboxSpecialEntry`, `LootboxSpawnArea` | 34, 36-39, 35, 40-42 |

The older non-default configurations 5 (ItemBlueprint), 6 (ItemBlueprintDefaultEnchantment) and 8
(EnchantmentDefinition) have no seed; ideas from them were folded into the defaults.

Fixes against the source forms, besides the text:

- Required foreign keys are authored under their FK name (District `TownId`, Structure `StreetId`/`DistrictId`,
  PermissionGrant `HolderId` as a player-or-group picker) so creating from the form can't miss them.
- Structure house number is required (the service refuses ≤ 0; the old default 0 made every create fail);
  District's duplicate `Location` field removed; dev-only `world_KNK-DEV`/`world` defaults removed (KNG-118 D9).
- Grade gets drop chance and enchantment cap divisor; ItemBlueprintOrigin gets its sequence number (a second origin
  failed with "Duplicate SequenceNumber"); EnchantmentDefinition shows the vanilla picker only for non-custom ones.
- User: ledger-owned Coins/Gems/XP, PasswordHash and a hard-coded CreatedAt removed; Gender, freeze state and
  account origin carried (read-only where staff must not edit) so an edit no longer resets them.
- PermissionGroup split into six steps, parent group as a picker, the hidden default holder (dev id 28) and the
  `"default"` expiry default on PermissionGrant removed.
- LootboxSpawnArea's region world task used the non-existent type `WgRegionId`; now `ReagionCreate` (same plugin
  handler, picked by field name).
- Kit, LootboxType, LootboxSpecialEntry and LootboxSpawnArea split into smaller steps; descriptions state the real
  API/DB limits.

The gate seeds were written from the docs and the models; no export of the dev DB's configurations 9/12 was
available. The siege seeds convert `siege-minigame/PHASE_3_FORMCONFIGS.md` (configurations 23-33). What changed
against those forms, and what was left off on purpose: knk-workspace
`docs/reports/2026-10-11-form-wizard-chain.md`, link 7.
