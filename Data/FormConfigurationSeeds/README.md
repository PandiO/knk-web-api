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
| `GateDoor` | GateDoor - Default (seed v3) | — (its rules read its own Gate field) |
| `SiegeScenarioDistrict.join` | SiegeScenarioDistrict - Join Entry (seed v1) | — |
| `SiegeScenarioGate.join` | SiegeScenarioGate - Join Entry (seed v1) | — |
| `SiegeLobbyScenario.join` | SiegeLobbyScenario - Join Entry (seed v1) | — |
| `SiegeScenario` | SiegeScenario - Default (seed v2) | `SiegeScenarioDistrict.join`, `SiegeScenarioGate.join` |
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

**GateDoor seed v3 (KNG-268/KNG-270, 2026-10-23):** a new `configurationGuid` and name, so it imports next to the
imported v2; make it the default and delete or keep v2 by hand. Changes: "Open-door anchor" may stay empty when the
opened door stays inside the door box (the open-door scan then starts at the anchor, plugin and API of the same
issues); "Open-door scan" is shown for plane grid and region doors (not only once the open-door anchor is set) and
explains when a scan is needed, that a door which only rises and loses decoration needs none, and that a region door
needs `/gatedoor capture <door> opened` first; "Closed-door scan" names the closed capture. GUIDs: configuration
uuid5(v2 configuration GUID, "seed-v3"), every step/field uuid5(v2 configuration GUID, its v2 GUID).

**GateStructure seed v2 (KNG-229, 2026-10-21):** a new `configurationGuid` and name, so it imports next to the
imported v1; make it the default and delete or keep v1 by hand. Adds the domain settings the Structure form has:
"Entry rule on roads" (`RoadAccessOverride`) on Region and access, "Navigate to" (`NavigationDefaultOverride`) on Spawn
point, and a new "Warp destination" step (`TeleportEnabled`, `TeleportPriceGems`, `TeleportMinTitleBracketId`,
`TeleportMinPremiumGroupId` limited to premium tiers, `TeleportRequiresDiscovery` default on). Needs the knk-web-api
change of the same issue: before it, `GateStructureService` ignored the warp settings. A gate saved without the warp
step (the plugin, v1) keeps its settings. GUIDs: configuration uuid5(v1 configuration GUID, "seed-v2"), every kept
step/field uuid5(v1 configuration GUID, its v1 GUID), new ones uuid5(v2 configuration GUID, "step:"/"field:" + name).

**Domain sub-regions (KNG-254, 2026-10-23):** new seed `DomainSubRegion` (seed v1: the domain, a name and the region
world task) and new versions of `Town`, `District`, `Structure` (seed v2) and `GateStructure` (seed v3) with a last
"Sub-regions" step: an owned `SubRegions` list of `DomainSubRegion`, usable once the domain is saved (like Gate doors).
Each has a new `configurationGuid` and name, so it imports next to the previous version; import `DomainSubRegion`
first, then make the new versions the defaults. GUIDs: configuration uuid5(previous configuration GUID, "seed-v<n>"),
every kept step/field uuid5(previous configuration GUID, its previous GUID), the new step and field uuid5(new
configuration GUID, "step:Sub-regions"/"field:SubRegions"); `DomainSubRegion` uses uuid5(URL namespace,
"knk-formconfig:DomainSubRegion:seed-v1") and uuid5(that, "step:"/"field:" + name). Design: knk-workspace
`docs/specs/domain-subregions/DESIGN.md` §4.3.

**SiegeScenario seed v2 (alpha test run 1, PandiO/knk-workspace#165/#166, 2026-10-27):** a new `configurationGuid`
and name, so it imports next to the imported v1; make it the default. The Rewards step gets the gate damage reward
(`GateDamageRewardStep`, `CoinRewardGateDamage`, `ExpRewardGateDamage`) and the participation reward
(`CoinRewardParticipation`, `ExpRewardParticipation`, `ParticipationMinActivePercent`); needs the knk-web-api change of
the same issues. The `SiegeObjective` and `SiegeSpawnpoint` seeds' location rules now depend on v2's Town field (their
own GUIDs are unchanged, so a database that imported them already keeps its copies, whose rules still read v1's Town
field: keep v1 while they do, or point those rules at v2's Town field in the form editor before deleting v1). GUIDs:
configuration uuid5(v1 configuration GUID, "seed-v2"), every kept step/field uuid5(v1 configuration GUID, its v1 GUID),
the new fields uuid5(v2 configuration GUID, "field:" + name).

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

Per-step change tables, properties left off and the developer's decisions (smoke test 2026-10-22): knk-workspace
`docs/reports/2026-10-19-kng-139-seed-review-notes.md` (it was `Data/SEED_REVIEW_NOTES.md` here until the merge).

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

**NavigationItem field (KNG-234, 2026-10-23):** `SiegeScenario.json` (step "Hub and entry") and `SiegeLobby.json` (step
"General") each gained the optional-by-default Enum field `NavigationItem` ("Navigation compass": `Inherit`, `Give`,
`DoNotGive`; default `Inherit`) in place, without a new `configurationGuid`, because other seeds refer to the scenario
configuration by GUID. A fresh import has the field; a database that imported these seeds earlier gets it by adding the
same field in the form designer (import never overwrites). Field GUIDs: uuid5 of the configuration GUID and
`<step>/NavigationItem`.


**EnchantmentDefinition seed v2 (PandiO/knk-workspace#151, 2026-10-26):** a new `configurationGuid` and name, so it
imports next to the imported v1; make it the default and delete or keep v1 by hand. Adds a last step "Combat values",
shown for custom enchantments only: chance at level I and per extra level (percent), cooldown on/off and length
(seconds), duration at level I (seconds) and the level multiplier. Empty = the default (PandiO/knk-workspace#156: the
API writes it into empty fields at startup). GUIDs:
configuration uuid5(v1 configuration GUID, "seed-v2"), every kept step/field uuid5(v1 configuration GUID, its v1 GUID),
the new step and fields uuid5(v2 configuration GUID, "step:Combat values"/"field:" + name).

**EnchantmentDefinition seed v3 (PandiO/knk-workspace#201, 2026-11-02):** a new `configurationGuid` and name, so it
imports next to v2; make it the default and delete or keep v2 by hand. Adds two fields at the end of "Combat values",
read by Flash Chaos only: "Effect level" (`EffectLevel`, its Slowness level, 1-10, default 1) and "Second effect
duration at level I" (`SecondaryEffectDurationSeconds`, its Nausea, default 12.5 s); its Slowness duration is the
existing "Duration at level I" (default 5 s). The duration, level multiplier and step descriptions say so. GUIDs:
configuration uuid5(v2 configuration GUID, "seed-v3"), every kept step/field uuid5(v2 configuration GUID, its v2 GUID),
the new fields uuid5(v3 configuration GUID, "field:" + name).

**EnchantmentDefinition seed v4 (PandiO/knk-workspace#223, 2026-11-03):** a new `configurationGuid` and name, so it
imports next to v3; make it the default and delete or keep v3 by hand. Adds four fields at the end of "Combat values",
read by Chaos and Flash Chaos only: "Area damage at level I" (`AreaDamage`, 0-1000, default 40 / 60), "Gate damage at
level I" (`GateDamage`, 0-10000, default the same as the area damage), "Damage radius" (`DamageRadiusBlocks`, 0-32,
default 3) and "Knockback radius" (`EffectRadiusBlocks`, 0-32, default 5). Both damages grow per level with the level
multiplier; the step and level multiplier descriptions say so. GUIDs: configuration uuid5(v3 configuration GUID,
"seed-v4"), every kept step/field uuid5(v3 configuration GUID, its v3 GUID), the new fields uuid5(v4 configuration
GUID, "field:" + name).

**EnchantmentDefinition seed v5 (PandiO/knk-workspace#259, 2026-11-05):** a new `configurationGuid` and name, so it
imports next to v4; make it the default and delete or keep v4 by hand. Adds four fields at the end of "Combat values",
read by Cleaving only (they replace knk-plugin's config.yml `gates.cleaving`): "Gate damage bonus per level"
(`GateDamageBonusPerLevel`, 0-10, default 0.25), "Charged strike at level I" (`StrikeMultiplier`, 0-100, default 5),
"Charged strike per extra level" (`StrikeMultiplierPerLevel`, -100 to 100, default 1) and "Strike cooldown change per
extra level" (`CooldownPerLevelSeconds`, -86400 to 86400, default -10); the strike's level-I cooldown is the existing
"Cooldown length" (default 60). The step and cooldown descriptions say so. GUIDs: configuration uuid5(v4 configuration
GUID, "seed-v5"), every kept step/field uuid5(v4 configuration GUID, its v4 GUID), the new fields uuid5(v5
configuration GUID, "field:" + name).

**LootboxSpawnArea seed v3 (PandiO/knk-workspace#487, 2026-11-14):** a new `configurationGuid` and name, so it imports
next to the imported v2; make it the default and delete or keep v2 by hand. Only descriptions change: a drawn region
is still renamed to `lootbox_area_<id>` on save, but keeps exactly the outline and Y range it was drawn with (no longer
stretched to full build height), so the "Area and region" step and the Region field tell admins to include the layer
of air just above the ground, where boxes stand. Needs the knk-web-api and knk-plugin change of the same issue. GUIDs:
configuration uuid5(v2 configuration GUID, "seed-v3"), every step/field uuid5(v2 configuration GUID, its v2 GUID).

**LootboxSpawnArea seed v2 (PandiO/knk-workspace#34, 2026-11-05):** a new `configurationGuid` and name, so it imports
next to the imported v1; make it the default and delete or keep v1 by hand. Only descriptions change: a region drawn
with Send to Minecraft is now renamed to `lootbox_area_<id>` on save and stretched to full build height (knk-web-api and
knk-plugin of the same issue), so the "Area and region" step and the Region field no longer steer admins to
`select <region>` or `/knk lootbox area create`; the Name field says `area_<number>` is reserved. Needs that API and
plugin change: before it, a drawn region keeps its temp name and the server removes it after 14 days. GUIDs:
configuration uuid5(v1 configuration GUID, "seed-v2"), every step/field uuid5(v1 configuration GUID, its v1 GUID).

**Landmark seed v1 (off-road navigation phase 5.1, PandiO/knk-workspace#708, 2026-11-18):** new seed `Landmark` (no
dependency, import any time; it becomes the default when Landmark has none). Steps: "Landmark" (`Name`, `DomainId`,
`NavigationEnabled`), "Location" (`LocationId`, captured in game with Send to Minecraft) and "Area"
(`ExtentKind` None/Region/Radius, `RegionId`, `Radius`, `Preference`). The API checks the rules (name unique per world,
region id for Region, radius above 0 and at most 512 for Radius, preference 0.25-4.0). GUIDs: configuration uuid5(URL
namespace, "knk-formconfig:Landmark:seed-v1"), every step/field uuid5(that, "step:"/"field:" + name).

**Landmark seed v1, region world task (PandiO/knk-workspace#944/#945, 2026-11-21):** an in-place change, without a new
`configurationGuid` (the seed is only on `next`, not yet released): the "Area" step's `RegionId` field gets the
region world task (`ReagionCreate`, which the plugin now runs on any field) and its label and description say that a
drawn region becomes `landmark_<id>` on save, keeps the outline and Y range it was drawn with and is replaced by a
re-run, while an existing region id is used as it is; the step description says the same in short. Needs the
knk-web-api and knk-plugin change of the same issues. A fresh import has it; a database that imported the seed earlier
keeps its configuration (import never overwrites), so add the world task to the field in the form designer there.

**Siege weapons phase 1 (PandiO/knk-workspace#782, 2026-11-19):** new seeds `SiegeAmmoType`,
`SiegeWeaponTemplateAmmoType.join`, `SiegeWeaponTemplate`, `SiegeWeaponSpot` and `UserSiegeWeaponUnlock` (seed v1 each),
and two in-place additions, without a new `configurationGuid`, because other seeds refer to these configurations by
GUID (the NavigationItem precedent above): `SiegeScenario.json` gets a "Siege weapons" step before "Readiness"
(`FriendlyFire`, `WeaponSpotCooldownOverrideSeconds` and the owned `WeaponSpots` list of `SiegeWeaponSpot`), and
`SiegeTeam.json` gets `ActiveWeaponSpots` at the end of "Role and alliance". A fresh import has them; a database that
imported these two seeds earlier gets them by adding the same step and fields in the form designer (import never
overwrites). Import order: `SiegeAmmoType`, `SiegeWeaponTemplateAmmoType.join`, `SiegeWeaponTemplate`,
`SiegeWeaponSpot`, `UserSiegeWeaponUnlock`. The template's "Spread and flight" step shows for Indirect weapons only and
"Firing cone" for Direct ones. Spot bounds are typed in until the spot world task of phase 2 (#783) fills them; the
schematic references wait for the schematic pipeline (#614) and are not on the form. GUIDs: new seeds use
configuration uuid5(URL namespace, "knk-formconfig:<Entity>:seed-v1") and uuid5(that, "step:"/"field:" + name); the
in-place additions uuid5(the configuration GUID, "step:Siege weapons" or "<step>/<Field>").

**SiegeWeaponSpot world task (PandiO/knk-workspace#799, 2026-11-19):** an in-place change of `SiegeWeaponSpot` seed v1
(no new `configurationGuid`: the seed is not on `main` yet, so no database has imported it outside `next` test runs).
`MinX` on "Place and size" gets the world task `SiegeWeaponSpot` (`settingsJson` `worldTask`), which the game server
handles only with `siege.weapons.enabled`; the completed task fills the six bounds, `Facing`, `SizeClass` and
`WgRegionId` and shows warnings. The step and field descriptions say so. A database that imported the seed earlier
gets the task by turning on "Requires in-game action" on Min X with type `SiegeWeaponSpot` in the form designer.

**SiegeWeaponSpot as a WorldGuard region (PandiO/knk-workspace#988, 2026-11-22):** a second in-place change of
`SiegeWeaponSpot` seed v1 (still only in `next`, so still no new `configurationGuid`). It replaces the #799 task above:
the spot keeps only its region. On "Place and size", `WgRegionId` (label "Region", now required) gets the normal region
world task `ReagionCreate` (`settingsJson` `worldTask`, as `LootboxSpawnArea.json`) and tells the admin to draw a box;
the six bounds fields `MinX` to `MaxZ` are gone; `Facing` stays an Enum field picked in the form (like `GateDoor`'s
`FaceDirection`); `SizeClass` is read-only, because the API works it out from the region on save. Two validation rules
sit on `WgRegionId`: `SiegeWeaponSpotSize` (depends on `Facing`, blocking: shows the class or "too small") and
`SiegeWeaponSpotSite` (warning: the floor under the region isn't solid or its space isn't clear). Both ask the game
server over the game hub (`RegionQueries:Enabled`); one that can't be asked says so and doesn't block the save; the site
check is answered only with the plugin's `siege.weapons.enabled` on. A database that imported the seed earlier
(a `next` test database) either re-imports it (delete the old "SiegeWeaponSpot - Default (seed v1)" first) or, in the
form designer: delete the fields Min X to Max Z; on WorldGuard region turn on "Requires in-game action" with type
`ReagionCreate` and make it required; make Size class read-only; add the two rules on the region field
(`SiegeWeaponSpotSize`, dependency Facing, blocking, message `{sizeReason}`; `SiegeWeaponSpotSite`, not blocking,
message `{siteReason}`).

| Seed | Configuration | Import after |
|---|---|---|
| `SiegeAmmoType` | SiegeAmmoType - Default (seed v1) | — |
| `SiegeWeaponTemplateAmmoType.join` | SiegeWeaponTemplateAmmoType - Join Entry (seed v1) | — |
| `SiegeWeaponTemplate` | SiegeWeaponTemplate - Default (seed v1) | `SiegeWeaponTemplateAmmoType.join` |
| `SiegeWeaponSpot` | SiegeWeaponSpot - Default (seed v1) | — |
| `UserSiegeWeaponUnlock` | UserSiegeWeaponUnlock - Default (seed v1) | — |

**Territory V1 seeds v1 (PandiO/knk-workspace#326, 2026-11-19):** new seeds `Kingdom`, `Province`, `CrownLand` and
`TerritoryConflict`, each with a display seed `<Entity>.display.json` in `DisplayConfigurationSeeds/` (G17). Import
`Kingdom` first: `Province` and `CrownLand` pick a kingdom (`KingdomId`). `CrownLand` captures its anchor region with
Send to Minecraft like a district (`WgRegionId`, task `ReagionCreate`) and has a `Range` in blocks (0-10000).
`TerritoryConflict` is read only (the game server's territory report writes conflicts; they are resolved on the
conflict's page through `POST api/TerritoryConflicts/{id}/resolve`). The town's power, kingdom and province and the
district's capital flag are not on the Town and District seeds yet (the web app link #690 adds them to those forms).
GUIDs: configuration uuid5(URL namespace, "knk-formconfig:<Entity>:seed-v1"), every step/field uuid5(that,
"step:"/"field:" + name).

**Stock core P1 seeds v1 (PandiO/knk-workspace#97, 2026-11-19):** new seeds `Storage` and `StockTransaction`, each with
a display seed `<Entity>.display.json` in `DisplayConfigurationSeeds/` (G17). Import in any order.

| Seed | Configuration | Import after |
|---|---|---|
| `Storage` | Storage - Default (seed v1) | — |
| `StockTransaction` | StockTransaction - Default (seed v1) | — |

`Storage` picks a kind (`Domain`, `Player`, `Clan`, `Transit`; the five system storages are seeded by the migration)
and fills only the holder field of that kind (`DomainId`, `UserId` or `ClanId`); capacity (`MaxUnits`, `MaxDistinct`,
`UnitsPerSlot`) and the lock are on step 2. Stock is never typed into the form: it moves through
`api/StockTransactions` (adjust and move). `StockTransaction` is read only (the ledger is append-only; a correction is
`POST api/StockTransactions/{id}/reverse`). Stock lines, units and moves are listed on their storage's and
transaction's pages and have no form of their own; the roll-up rows (G3) have no seeds (aggregate rows, like
`item_conversion_stats`). GUIDs: configuration uuid5(URL namespace, "knk-formconfig:<Entity>:seed-v1"), every
step/field uuid5(that, "step:"/"field:" + name).

**Production world P2 seeds v1 (PandiO/knk-workspace#91, 2026-11-19):** new seeds `Warehouse` and
`ProductionStructure`, each with a display seed `<Entity>.display.json` in `DisplayConfigurationSeeds/` (G17). Import in
any order.

| Seed | Configuration | Import after |
|---|---|---|
| `Warehouse` | Warehouse - Default (seed v1) | — |
| `ProductionStructure` | ProductionStructure - Default (seed v1) | — (its preferred warehouse is a picker, not a rule) |

Both have a "Placement" step: a district with street and house number, or only a town for a rural structure (exactly
one of District and Town; the API refuses both or neither). The region field carries the placement rules of production
DESIGN § 3.4, both seeded as warnings (`isBlocking: false`, R2); an admin makes either blocking on the form
configuration, and the API then refuses a save that breaks it: **P-1** `RegionContainment` against the district's
region and **P-2** `InsideTerritory` against the town's territory (a lookup on the game server). Each rule only runs when
its own dependency is filled (`requiresDependencyFilled: false`), so a rural structure skips P-1 and a district one P-2.
The production kind is not on the form yet (P3 adds kinds); the default storages (a delivery storage for a warehouse,
an output storage for a production structure) are created by the API, not the form. The generic `Structure` seed is
unchanged (district only); the API accepts a rural `townId` there too. GUIDs: configuration uuid5(URL namespace,
"knk-formconfig:<Entity>:seed-v1"); steps and fields uuid5(that, "step:"/"field:" + name), the steps copied from the
`Structure` seed included.

**Production engine P3 seeds v1 (PandiO/knk-workspace#86, 2026-11-19):** new seeds for the six major P3 entities, each
with a display seed `<Entity>.display.json` in `DisplayConfigurationSeeds/` (G17). Import in any order.

| Seed | Configuration | Import after |
|---|---|---|
| `ProductionKind` | ProductionKind - Default (seed v1) | — |
| `ProductionKindLevel` | ProductionKindLevel - Default (seed v1) | — (edit only: a kind's ten levels are created with it) |
| `ProductionLine` | ProductionLine - Default (seed v1) | — |
| `ProductionModifier` | ProductionModifier - Default (seed v1) | — |
| `ClimateZone` | ClimateZone - Default (seed v1) | — |
| `WeatherType` | WeatherType - Default (seed v1) | — |

The migration `AddProductionEngine` seeds the catalogue (eight kinds with ten levels each, Hunter deferred), one default
climate zone without odds (clear weather) and the weather types Rain, Storm and Drought with example effects. The child
lists are not on the forms (G17 clarification of 2026-11-19: join-like rows may ship without seeds): a level's unlocks
and upgrade costs (`items`), a line's inputs (`inputs`), a zone's weather odds and towns (`weatherOdds`, `towns`) and a
weather type's effects (`effects`) are sent as lists in the entity's API body (the web app link #678 builds their
editors); the display seeds list them. GUIDs: configuration uuid5(URL namespace, "knk-formconfig:<Entity>:seed-v1"),
every step/field uuid5(that, "step:"/"field:" + name).

**ItemBlueprint seed v2 (PandiO/knk-workspace#819, 2026-11-19):** a new `configurationGuid` and name, so it imports
next to an imported v1; make it the default and delete or keep v1 by hand. Adds "Storage weight" (`StorageWeight`,
Decimal, default 1) after "Max stack size" on "Item in game": the capacity units one item takes in a storage (stock
core P1, DESIGN § 4.4). GUIDs: configuration uuid5(v1 configuration GUID, "seed-v2"), every kept step/field
uuid5(v1 configuration GUID, its v1 GUID), the new field uuid5(v2 configuration GUID, "field:StorageWeight").

**ProductionStructure seed v2 (PandiO/knk-workspace#86, 2026-11-19):** a new `configurationGuid` and name, so it imports
next to an imported v1; make it the default and delete or keep v1 by hand. Adds "Production kind" (`ProductionKindId`,
a `ProductionKind` picker) before "Level" on the "Production" step: a new structure gets the kind's default workers and
storages, and its level caps follow the kind's levels. GUIDs as for ItemBlueprint v2 (new field
"field:ProductionKindId").

**Yield areas P4 seeds (PandiO/knk-workspace#138, 2026-11-19):** new seeds for the two major P4 entities staff edit,
each with a display seed in `DisplayConfigurationSeeds/` (G17); `AreaSurvey` has only a display seed (the game server
reports surveys, nobody types them). Import in any order.

| Seed | Configuration | Import after |
|---|---|---|
| `ProductionArea` | ProductionArea - Default (seed v1) | — |
| `ProductionYieldOverride` | ProductionYieldOverride - Default (seed v1) | — |

`ProductionArea` asks for the structure, a name and the shape, then the shape's fields (region id and bounds, two
cuboid corners, or a radius with an optional centre); the API computes the bounding box and the size and refuses an
area over the level's cap or an exclusive area that overlaps another (a polygon's points come from the in-game capture
or the API). New versions, each with a new `configurationGuid` and name so it imports next to the older one; make it
the default and delete or keep the older one by hand:

1. `ProductionKind` seed v2: a "Yield" step with the survey profile (JSON) and the primary multiplier. The yield table
   and the primary outputs are lists in the kind's API body (`yieldTable`, `primaryOutputs`), edited by the web app
   link #680, like the P3 child lists.
2. `ProductionStructure` seed v3: "Yield source" (Hybrid, Derived or Configured) after "Production kind".
3. `Structure` seed v3 (PandiO/knk-workspace#831): the "Address" step becomes "Placement" with an optional "Town
   (rural)" picker; district, street and house number are no longer required, so a rural plain structure can be made
   (exactly one of District and Town, as the API checks).

GUIDs: new seeds uuid5(URL namespace, "knk-formconfig:<Entity>:seed-v1"), steps and fields uuid5(that, "step:"/"field:"
+ name); new versions uuid5(previous configuration GUID, "seed-vN"), every kept step/field uuid5(previous configuration
GUID, its previous GUID) with the validation rules' references remapped, new fields uuid5(new configuration GUID,
"field:" + name).

**DefaultRouteMode field (PandiO/knk-workspace#852, 2026-11-20):** `PermissionGroup.json` (step "Group") gained the
optional String field `DefaultRouteMode` ("Default route mode") in place, without a new `configurationGuid`, as with
`NavigationItem` above: a dropdown of `shortest`, `efficient`, `preferred` (`settingsJson.enumValues`) whose empty
option reads "None (default: preferred)" (`settingsJson.emptyOptionLabel`, a web app setting of the same issue) and
sends "", which the API stores as no default. A fresh import has the field; a database that imported the seed earlier
gets it by adding the field in the form designer with the same field name, type and settings JSON
`{"enumValues": ["shortest", "efficient", "preferred"], "emptyOptionLabel": "None (default: preferred)"}`. Without it
the form leaves the stored value alone (an omitted value is kept). Field GUID: uuid5 of the configuration GUID and
`Group/DefaultRouteMode`.

**DefaultRouteMode as an Enum field (PandiO/knk-workspace#852 follow-up, developer decision 2026-11-21):** the
entity property is now the enum `RouteMode` (`Shortest`, `Efficient`, `Preferred`), so the field is an **Enum** field
(the publish check compares the field type with the entity's). Same field GUID, step and label, in
place. Settings JSON: `{"enumValues": ["Shortest", "Efficient", "Preferred"], "emptyOptionLabel": "None (default:
Preferred)"}`; the values must be the enum names, because an edit pre-selects the stored name. A database that added
the String field by hand changes its type to Enum and its settings to these in the form designer.

**Transport core P5 seeds v1 (PandiO/knk-workspace#79, 2026-11-20):** new seeds for the two major P5 entities, each
with a display seed in `DisplayConfigurationSeeds/` (G17). Import in any order.

| Seed | Configuration | Import after |
|---|---|---|
| `TransportOrder` | TransportOrder - Default (seed v1) | — (read only) |
| `TransportLane` | TransportLane - Default (seed v1) | — |

`TransportOrder` is read only, like `StockTransaction`: the transport service writes orders and moves them along.
Staff make a manual order with the web app's manual order form (#682) or `POST api/TransportOrders` (dry run:
`POST api/TransportOrders/dry-run`), and cancel with `POST api/TransportOrders/{id}/cancel`. Stops, stop lines, cargo
lines, carrier assignments and events are listed on the order's page (view only, no seeds). `TransportLane` asks for
the two structures and the road distance; the game server's lane measurer (#681) fills lanes in on its own. GUIDs:
uuid5(URL namespace, "knk-formconfig:<Entity>:seed-v1"), steps and fields uuid5(that, "step:"/"field:" + name).

**Siege shop catalogue (PandiO/knk-workspace#867, 2026-11-20):** new seeds `SiegeShopCategory`, `SiegeShopEntry` and
`SiegeScenarioShopOverride` (seed v1 each), and two in-place additions without a new `configurationGuid` (the siege
weapons precedent above): `SiegeScenario.json` gets a "Siege shop" step after "Siege weapons" with the owned
`ShopOverrides` list (`SiegeScenarioShopOverride`) and `ShopEntries` list (scenario-only `SiegeShopEntry`), and
`ItemBlueprint.json` gets `PotionType` after the armour trim material. A database that imported those two seeds earlier
gets them by adding the same step and fields in the form designer (import never overwrites). The entry's "Item" step
shows for kind Item only and "Siege weapon" for kind SiegeWeapon. The override's entry picker lists the modes' default
entries only (`pickerFilters` `defaultsOnly: true`). GUIDs: new seeds use configuration uuid5(URL namespace,
"knk-formconfig:<Entity>:seed-v1") and uuid5(that, "step:"/"field:" + name); the in-place additions uuid5(the
configuration GUID, "step:Siege shop", "Siege shop/<Field>" or "<step>/PotionType").

| Seed | Configuration | Import after |
|---|---|---|
| `SiegeShopCategory` | SiegeShopCategory - Default (seed v1) | — |
| `SiegeShopEntry` | SiegeShopEntry - Default (seed v1) | — |
| `SiegeScenarioShopOverride` | SiegeScenarioShopOverride - Default (seed v1) | `SiegeShopEntry` |

**Terminals and menus P6a seeds (PandiO/knk-workspace#683, 2026-11-20):** a new seed `StorageContainer` with a display
seed in `DisplayConfigurationSeeds/` (G17), and two new versions (#881, #877). Import in any order.

| Seed | Configuration | Import after |
|---|---|---|
| `StorageContainer` | StorageContainer - Default (seed v1) | — |
| `StockTransaction` | StockTransaction - Default (seed v2) | — (replaces v1) |
| `ProductionArea` | ProductionArea - Default (seed v2) | — (replaces v1) |

`StorageContainer` binds a block to a storage: the storage and the mode (Terminal or Display) on step 1, the world,
block position, block type and the double-chest flag on step 2. In game, staff bind with `/knk storage bind` instead
(the plugin link #74). The slots are worked out by the API (27, or 54 for a double chest), and every bound container
adds its slots × the storage's `UnitsPerSlot` to the storage's cap. New versions, each with a new `configurationGuid`
and name so it imports next to the older one; make it the default and delete or keep the older one by hand:

1. `StockTransaction` seed v2 (#881): the Reason list has `Upgrade` (level upgrades, P3) before `Correction`.
2. `ProductionArea` seed v2 (#877): "Region" (`WgRegionId`) captures a region in game (world task `RegionClaim`), and
   "Radius" captures a centre and radius (world task `CaptureRadius`, a known type now; the plugin handler comes with a
   later plugin link). A Region-shaped area whose bounds are left empty takes them from the region's stored outline.

GUIDs: the new seed uuid5(URL namespace, "knk-formconfig:<Entity>:seed-v1"), steps and fields uuid5(that,
"step:"/"field:" + name); new versions uuid5(previous configuration GUID, "seed-v2"), every kept step/field
uuid5(previous configuration GUID, its previous GUID).

**Item delivery P7 seed v1 (PandiO/knk-workspace#60, 2026-11-20):** a new seed `ItemDelivery` with a display seed in
`DisplayConfigurationSeeds/` (G17), and `ProductionArea` seed v3 (#883: placement rule P-3). Import in any order.

| Seed | Configuration | Import after |
|---|---|---|
| `ItemDelivery` | ItemDelivery - Default (seed v1) | — |
| `ProductionArea` | ProductionArea - Default (seed v3) | — (replaces v2) |

`ItemDelivery` is read only, like `StockTransaction`: the delivery service writes it (`POST api/ItemDeliveries`, from
the game server or staff with `knk.economy.admin`). Its lines (`ItemDeliveryLine`, the inbox lots) are listed on its
page and have no seed of their own (child rows, developer clarification 2026-11-19). `ProductionArea` seed v3 adds
placement rule P-3 (`ProductionAreaPlacement` on the structure field, warning by default, R2): the area stays inside
its structure's district region (as P-1) or, for a rural structure, its town's territory (as P-2). A Region area is
checked as a whole region, a Cuboid by its four corners, a Radius by its centre and the four points one radius away;
a Polygon is not checked by this rule. The API also runs it on save and answers `placementWarnings`. GUIDs: as for the
P6a seeds above.

**Player harvesting P8a seeds (PandiO/knk-workspace#685, 2026-11-21):** two new seeds `HarvestRule` and `ResourcePool`
with display seeds in `DisplayConfigurationSeeds/` (G17), and `ProductionKind` seed v3 (the pool floor). Import in any
order.

| Seed | Configuration | Import after |
|---|---|---|
| `HarvestRule` | HarvestRule - Default (seed v1) | — |
| `ResourcePool` | ResourcePool - Default (seed v1) | — |
| `ProductionKind` | ProductionKind - Default (seed v3) | — (replaces v2) |

`HarvestRule` has four steps: the kind and targets (a JSON list of material or entity names, `*` allowed at either
end), who may harvest (permission node, lowest title, tool, tool tier, cooldown; the game server checks these), the
yield (blueprint, units per break, fortune bonus, worker's share, pool cost) and the regeneration (placeholder,
placeholder block, seconds, slower when the pool is low). `ResourcePool` names the area and, optionally, the capacity,
units left and regeneration per hour; left empty, the capacity is the latest survey's total units (else the game
setting `production.harvest.defaultPoolCapacity`). The first harvest in an area makes its pool, so a pool seed is only
needed to set one up beforehand. `PlayerHarvest` (the harvest history) is read only and has a display seed only;
`HarvestAccrual` (the unpaid fractions per player and rule) has no seed (a counter table, developer clarification
2026-11-19). `ProductionKind` seed v3 adds "Pool floor" (`PoolFloor`, default 0.5) to the Yield step. GUIDs: the new
seeds uuid5(URL namespace, "knk-formconfig:<Entity>:seed-v1"), steps and fields uuid5(that, "step:"/"field:" + name);
v3 uuid5(v2 configuration GUID, "seed-v3"), every kept step/field uuid5(v2 configuration GUID, its v2 GUID), the new
field uuid5(v3 configuration GUID, "field:PoolFloor").

**Town logistics P9 seeds (PandiO/knk-workspace#127, 2026-11-21):** five new seeds with display seeds in
`DisplayConfigurationSeeds/` (G17). Import in any order.

| Seed | Configuration | Import after |
|---|---|---|
| `LogisticsPolicy` | LogisticsPolicy - Default (seed v1) | — |
| `ConsumerReplenishment` | ConsumerReplenishment - Default (seed v1) | — |
| `LogisticsRule` | LogisticsRule - Default (seed v1) | — (read only) |
| `LogisticsReservation` | LogisticsReservation - Default (seed v1) | — (read only) |
| `LogisticsPlannerRun` | LogisticsPlannerRun - Default (seed v1) | — (read only) |

`LogisticsPolicy` asks for the name and scope (Town, Warehouse or Template) with the town or warehouse, and for templates
the specialization (a resource category), town size and the default flag. A policy is versioned: every save is a new
version, so its rules are edited in the web app's policy editor (#686) or with `PUT api/LogisticsPolicies/{id}` and the
whole rule list, not in this form; `LogisticsRule` is read only. `ConsumerReplenishment` asks for a structure's Input or
Merchandise storage, the blueprint, min (reorder point), max (target level), an optional priority class and the switch;
the structure is taken from the storage. Reservations and planner runs are written by the planner, so their forms are
read only; `LogisticsPlanEntry` (a run's explanation lines) is listed on the run's page and has no seed (child rows,
developer clarification 2026-11-19). GUIDs: uuid5(URL namespace, "knk-formconfig:<Entity>:seed-v1"), steps and fields
uuid5(that, "step:"/"field:" + name).

**Shops P10 seeds (PandiO/knk-workspace#248, 2026-11-21):** six new seeds with display seeds in
`DisplayConfigurationSeeds/` (G17). Import `ShopType` before `ShopTypeLevel` and `Shop`, `Shop` before
`ShopCatalogueEntry`.

| Seed | Configuration | Import after |
|---|---|---|
| `ShopType` | ShopType - Default (seed v1) | — |
| `ShopTypeLevel` | ShopTypeLevel - Default (seed v1) | `ShopType` |
| `Shop` | Shop - Default (seed v1) | `ShopType` |
| `ShopCatalogueEntry` | ShopCatalogueEntry - Default (seed v1) | `Shop` |
| `ShopTradeStat` | ShopTradeStat - Default (seed v1) | — (read only) |
| `TownAccount` | TownAccount - Default (seed v1) | — (read only) |

`Shop` copies the `Warehouse` form's general, placement, region, spawn and sub-region steps (with its two placement
rules) and replaces the warehouse step by the shop step: type, kind (empty = the type's default), level, tax modifier,
buy-back percentage, workers, processing switch, hide sold-out and the shopkeeper spawn location. A shop type's
accepted categories are a join list (`ShopTypeCategory`, no seed, developer clarification 2026-11-19): they are set
with `acceptedCategoryIds` on `api/ShopTypes` (the web app's shop type editor, #688). `ShopCatalogueEntry` asks for the
shop, a blueprint or a unique item instance, the static price (empty = the formula), Stocked or Unlimited, pin, order
and the switch. Trade stats are rolled up from the ledger and town accounts change only through the currency ledger,
so both forms are read only. GUIDs: uuid5(namespace `6f1d2c58-3b0e-5a8e-9c49-2f2b8a4d0248`, "form/<Entity>"), steps
and fields uuid5(that namespace, "form/<Entity>/<step>[/<field>]"); the copied `Shop` steps and fields
uuid5(namespace, "form/Shop/<Warehouse GUID>").
