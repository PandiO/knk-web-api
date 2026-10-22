# Seed review notes — KNG-139 priority 2/3 form seeds and display seeds (2026-10-19)

Working notes from authoring the seeds in `Data/FormConfigurationSeeds/` and `Data/DisplayConfigurationSeeds/`: what changed per step, properties left off on purpose, and open questions for the developer. Review before importing; move to knk-workspace `docs/reports/` when the branch is merged.

---

## Group "domain": Town, District, Street, Structure, Location

All five pass the static checks. Configuration GUIDs and names are unchanged. Every kept step and field keeps its GUID. New
steps and fields use uuid5(configurationGuid, "step:<name>" / "field:<fieldName>").

These shared behaviours were checked in code before any change:
- **Location stays navigation-authored (`Location`, not `LocationId`).** The plugin resolves a world-task handler by
  task type first, then by field name (`WorldTaskHandlerRegistry.getHandler(taskType, fieldName)`). Only
  `LocationSelection` is registered by type. `LocationCapture`, `ReagionCreate` and `RegionClaim` only work through
  the field-name fallback (`Location` → LocationTaskHandler, `WgRegionId` → WgRegionIdTaskHandler). The web app also
  handles both names (`normalizeFormSubmission.handleSingleObjectRelationship` sends coordinate objects as
  `Location`, picks as `LocationId`). Nothing is gained by renaming, so the fields keep their names.
- **The Location seed stays one step with Name first.** A Location child form opened from a world-task field inherits
  the task onto its first visible field and spreads the captured x/y/z/yaw/pitch/world over the same-named fields
  (`FormWizard.tsx` ~2830, `spreadTaskValueOntoFields`). The capture never overwrites Name (KNG-167). No world task was
  added to the Location form, because that would disable the inheritance (`hasAnyFieldLevelWorldTask`).
- **The teleport fields have no display conditions.** `DomainTeleportSettings.Apply` writes all five fields whenever
  `TeleportEnabled` is in the DTO. Hidden fields are sent as null, so switching "Warp destination" off would wipe the
  price and requirements.
- **New `NavigationDefaultOverride` and `RoadAccessOverride` fields are `Enum` fields** (`enumType`
  NavigationDestinationMode / RoadAccessRule, snapshot `enumValues`, no default). The wizard's empty option is sent
  as `""`, which `DomainNavigationDefaults.Apply` reads as "follow the type default". A value sets the override.

## Town

| Step (was) | Now | Changes |
|---|---|---|
| General Information | General information | Descriptions added. |
| Region and Location | Region and access | Region (ReagionCreate) kept. AllowEntry and AllowExit moved here from "Rules", with default True and no longer required (the model default is true; a required checkbox without a default could block Next). **New:** Entry rule on roads (`RoadAccessOverride`). |
| Rules | (merged into Region and access) | Step removed. |
| (new) | Spawn point | Location (LocationSelection, still required) moved here from "Teleportation". **New:** Navigate to (`NavigationDefaultOverride`). |
| Teleportation | Warp destination | The five teleport fields, relabelled. TeleportPriceGems is no longer required (it has a model default; null → 0). TeleportMinPremiumGroup gets `pickerFilters {isPremiumTier: "true"}` because the API refuses non-premium groups. |

Validation rule LocationInsideRegion (Location → WgRegionId): the logic and dependsOn are unchanged. The message no
longer uses `{townName}`, which LocationInsideRegion never fills in. RegionRuleMessages appends the region name.

## District

| Step (was) | Now | Changes |
|---|---|---|
| General Information | General information | Town moved here from "Region and Location" and **renamed `Town` → `TownId`** (same GUID; fixes the test failure). |
| Region and Location | Region and access | Region kept. AllowEntry and AllowExit moved here (default True, optional). **New:** Entry rule on roads. The first `Location` field (d44328d1, LocationCapture) was **removed**: the form had two fields named `Location`. |
| Rules | (merged) | Removed. |
| (new) | Spawn point | Location 03a72776 (LocationSelection, still required). **New:** Navigate to. |
| Teleportation | Warp destination | As for Town. The "TeleportPriceGems" label is fixed. |

Validation rule RegionContainment (WgRegionId → Town): `dependsOn.fieldName` is now `TownId` (same GUID).
`dependencyPath` `Town.WgRegionId` and configJson are unchanged, as in GateStructure (`DistrictId` +
`District.WgRegionId`). Only the messages were reworded.

## Structure

| Step (was) | Now | Changes |
|---|---|---|
| General Information | General information | Now only Name and Description. |
| (new) | Address | District and Street moved here and **renamed `District` → `DistrictId`, `Street` → `StreetId`** (same GUIDs; fixes the test failures). **HouseNumber is now required with no default.** `StructureService` refuses ≤ 0, so the old optional field with default `0` made every create fail. |
| Region and Rules | Region and access | Region keeps `RegionClaim` verbatim (see Q7). AllowEntry and AllowExit are optional with default True. **New:** Entry rule on roads. |
| Locations | Spawn point | Location (LocationSelection, optional, as before). **New:** Navigate to. |
| (new) | Warp destination | **New:** all five teleport fields (teleport DESIGN §3.7.1 names Structure; `StructureDto` carries them). TeleportRequiresDiscovery defaults to True, like Town and District. |

## Street

| Step (was) | Now | Changes |
|---|---|---|
| Street Information | Street | Districts is **no longer required**: the model has a collection default, and streets are also created from the Roads page with only a name. The junk placeholder `new Collection()` was removed. |
| Road | Road | `Id` with `displayPanel: streetRoad` kept verbatim. Label changed "Road Id" → "Road", and a description was added. |

## Location

| Step (was) | Now | Changes |
|---|---|---|
| General Information | Position | Still a single step with Name first (see above). World moved up to second. **World's default `world_KNK-DEV` was removed**: it was the dev server's world name, and KNG-118 allows no silent world defaults. The capture fills World. The placeholder is "e.g. world". Yaw and Pitch default to `0`. Labels and descriptions are written for the child-form context (capture first, F3 values otherwise). |

## Properties left off (and why)

- **All domains:** `Id` and `CreatedAt` (audit). `WorldName` is set by `DomainWorldResolver` from the region task, the
  Location or the parent, and the wizard asks for it when no source has one (`FormWizardPage` `askForWorld`); a free
  text field could only create conflicts. `IsHub` is not in the Town, District or Structure DTOs; it is set from Game
  Settings (`HubSettingsService.SetEligibleAsync`). `LocationId`, `TeleportMinTitleBracketId` and
  `TeleportMinPremiumGroupId` are on the form under their navigation names. Discovery reward overrides live in their
  own table (`domain_discovery_overrides`, edited in the discovery admin), not on Domain.
- **Town:** `Streets` and `Districts` collections. Districts need a TownId and are created on the District form;
  streets are linked from the District, Street and Roads side.
- **District:** `Town` (navigation; `TownId` is on the form), `Streets`, `Structures`.
- **Structure:** `Street` and `District` navigations (the FKs are on the form).
- **Street:** `Structures` (structures set their own StreetId).
- **Location:** `Id`, `CreatedAt` (retention grace period, set by the API, not in LocationDto).

## NotOnFormByDesign

None for this group. District.TownId, Structure.StreetId and Structure.DistrictId are now on the forms under their
FK names.

## Open questions / decisions for the developer

1. **Structure spawn point capture is lost (API gap, not a seed problem).** `StructureDto` has only `locationId`, with
   no embedded `location`, and `StructureService` has no `HandleLocationAsync` like Town, District and GateStructure.
   A Location captured in game (or typed via Create New) is sent as `location: {…}` and silently dropped. Only picking
   an existing Location works. Fix: add `Location` to StructureDto and resolve it as TownService does. The field stays
   on the form.
2. **District has no LocationInsideRegion rule for its spawn point** (Town has a blocking one). **Structure has no
   RegionContainment rule** (GateStructure has a non-blocking one with `requireFullContainment: true`), nor a
   LocationInsideRegion rule. No rules were added, because only the existing logic was kept. Should they be?
3. The District RegionContainment rule has `requireFullContainment: false`: any overlap with the town passes.
   GateStructure uses `true`. Kept as is.
4. Location stays **required** on Town and District, as the current forms have it, although `LocationId` is nullable.
   /warp needs it, and /navigate falls back to the region without it. It stays optional on Structure. Keep it that
   way?
5. `TeleportRequiresDiscovery` defaults to **True** on all three forms (the current Town and District choice). The
   model default is false.
6. `TeleportMinPremiumGroup` gets a new picker filter `isPremiumTier=true` (PermissionGroupRepository supports it).
   Please confirm in the live test that the picker lists only premium tiers.
7. Structure WgRegionId keeps world task `RegionClaim`. No component handles that type specially: the plugin falls
   back to the `WgRegionId` field handler and the web app to the `regionId` output, exactly as for `ReagionCreate`.
   Switch it to `ReagionCreate` for consistency?
8. In District, the dropped duplicate `Location` field was the `LocationCapture` one. `LocationSelection` is the type
   the plugin registers by name, and Town, Structure and GateStructure use it too.
9. HouseNumber has no default. Should the API allow 0 for structures without a number, as gates have? The dev DB's
   four structures are all gates with 0.
10. Town and District children (districts, streets) are not shown on the parent forms. An owned-child list (like
    GateStructure's doors) could be added later if wanted.

---

## Group "people": User, PermissionGroup, PermissionGrant, BannerLayer, BannerDesign, Clan

All six pass the static checks. Configuration GUIDs and names are unchanged. Every kept field keeps its starter GUID,
also where the field was renamed (camelCase to PascalCase, `Holder` to `HolderId`). New steps and fields use uuid5 per
the brief. Every step and field now has a description.

Important for every form below: on edit the wizard sends **only the fields on the form**
(`FormWizard.tsx` final submit, `flattenVisibleStepsData`), so a DTO property the service copies but the form lacks is
reset to its DTO default. This decided several "keep on the form" choices.

## User (`User.json`)

| Step | Was | Now |
|---|---|---|
| Account (GUID of "Login Information") | Username, Uuid (ro), Email, PasswordHash (ro), CreatedAt (required, hard-coded default `05/09/2026 15:21:35`), IsActive | Username, Minecraft UUID (ro), Email, Account active |
| Game preferences (GUID of "Game Statistics") | Coins, Gems, ExperiencePoints, GatePassThroughMethodDefault | **Gender (new)**, Gate pass-through |
| Account status (new) | - | **Created via, Email verified, Frozen, Freeze reason** (new, all read-only) |

Why the new fields: `UserService.UpdateAsync` runs `_mapper.Map(UserDto, existing)`. `UserMappingProfile` maps
`EmailVerified`, `AccountCreatedVia`, `IsActive` and `GatePassThroughMethodDefault` explicitly, and AutoMapper also maps
`Gender`, `IsFrozen` and `FrozenReason` implicitly (same names on `UserDto`; not ignored, default AutoMapper config). Without
them on the form, every edit reset Gender to null, EmailVerified to false, AccountCreatedVia to WebApp and **unfroze a
frozen player** without an audit entry. Gender is editable (admin profile data); the other four are read-only so the loaded
value goes back unchanged (read-only fields are only disabled in `FieldRenderers.tsx`, their values are still submitted).

Left off, with the owner:

| Property | Why |
|---|---|
| Coins, Gems, ExperiencePoints | Currency ledger only (`ICurrencyService`, `PropertySaveBehavior.Ignore`); ignored by the update mapping; `PUT api/users/{id}/balances` |
| PersonalSalaryMultiplier, PersonalGemBonusMultiplier, PersonalExpBonusMultiplier | Ignored by the update mapping; `PUT api/users/{id}/multipliers` (validated, audited) |
| PasswordHash, LastPasswordChangeAt, LastEmailChangeAt, TokenVersion, RefreshTokens, LinkCodes | Credentials, auth audit, session state (`AuthService`); never in a form |
| CreatedAt | Audit column: set by the API on create, restored by `UpdateAsync` on edit. The old hard-coded default was wrong |
| ActiveMode | `PUT api/users/{id}/active-mode` / `/staffmode`, `/ownermode` in game |
| IsOnline, LastSeenAt | Presence, `PUT api/users/{id}/presence` from the plugin |
| LastSalaryPayoutAt | `SalaryService` only |
| FrozenByUserId, FrozenAt (and editing IsFrozen/FrozenReason) | `PUT api/users/{id}/freeze` / `unfreeze` (audited); shown read-only only |
| TransferLockReason, TransferLockedAt | Currency admin route |
| FirstJoinKitsGrantedAt | Kit service, under row lock |
| DeletedAt, DeletedReason, ArchiveUntil | Soft delete / merge / privacy erasure flows |
| ChatPrefix, ChatSuffix (from PermissionHolder) | Not on `UserDto`, so not writable through this endpoint; not rendered in game either |
| PermissionGroupMemberships, Grants | `POST/DELETE api/users/{id}/groups`, `POST/DELETE api/users/{id}/grants` (admin page) |
| IsFullAccount | Computed |

## PermissionGroup (`PermissionGroup.json`) - 29 fields, 3 steps -> 6 steps

| Step | Fields | Change |
|---|---|---|
| Group (GUID of "General Information") | Name, Weight, Inherits from, Premium rank | camelCase names -> PascalCase (binding is case-insensitive); `parentGroupId` Integer -> **`ParentGroupId` Object/PermissionGroup** (a picker instead of a raw id); Weight default 0 |
| Chat and tab list (new) | Chat colour, Chat bracket colour, Tab-list colour, Chat prefix, Chat suffix | Moved out of step 1; descriptions say what each colour paints (RANK_DISPLAY.md §2-4) and that prefix/suffix aren't rendered; `minecraft-text-color` setting kept; `&e`-style placeholders |
| Salary and bonuses (GUID of "Game multipliers") | Salary multiplier, Title gem bonus multiplier, Title XP bonus multiplier | Salary moved here from step 1. The step-level "IsPremiumTier = true" condition became a field-level condition on the two bonus fields, so salary stays editable for every group and the old behaviour (bonuses only for premium ranks) is kept. Bonuses no longer `isRequired` (model default 1.0; omitted keeps the stored value) |
| Teleport requests (GUID of "Teleport") | /tpa price mode, multiplier, coins, gems, XP, cooldown | Split the 17-field Teleport step per kind; conditions unchanged |
| Warp teleports (new) | /warp: same six | as above |
| Spawn teleport (new) | /spawn: mode, coins, gems, XP, cooldown | Mode options limited to None/Fixed (`enumValuesSubset`): the API refuses Multiplier for /spawn |

Fixed on the way: the old Teleport step description said groups are checked "from the highest Weight down"; since the
2026-10-12 decision (`PermissionGroupPrecedence`) a held group's own children come first, then weight. Descriptions now
say that. Note for whoever edits this form: each `Teleport*PriceMode` field must stay on the form (it is the switch that
makes `PermissionGroupTeleportSettings.Apply` write that kind; KNG41_FORMCONFIGS.md), and so must the three colour fields
(`UpdateAsync` copies them unconditionally).

Left off: ChildGroups, UserMemberships (navigation collections; memberships via the player's admin page), Grants (see
PermissionGrant).

## PermissionGrant (`PermissionGrant.json`)

| Step | Was | Now |
|---|---|---|
| Grant (GUID of "Permission information") | Holder (navigation, with a `valueProjection` that wrote the constant **28** into HolderId when empty), Node, Enable, Expiry date (default `"default"`) | **HolderId** (Object/PermissionHolder, required, `canCreate: false`), Permission node (placeholder `knk.teleport.warp`), Allow, Expires (no default) |

**HolderId.** A holder is the TPT base `PermissionHolder`: either a `User` or a `PermissionGroup` (they share its id;
`PermissionGrant.HolderId` is a real FK, user-features DESIGN.md §2.1). Grants are normally created through
`PermissionGrantsController` (POST upserts by holder+node, KNG-59), the player admin page's
`POST api/users/{id}/grants`, or in game `/knk user perm` (by-node routes). There is no grants step on the group form.
The web app already supports the field: `PermissionHoldersController` is a read-only union lookup "used by FormWizard
relationship fields targeting PermissionHolder", wired in `entityApiMapping.ts` (search + getById, rows show name and
holderType). So HolderId is now on the form under its FK name (test failure fixed), keeping the old field's GUID.
`canCreate: false` because PermissionHolder is abstract (no create endpoint). The hard-coded constant 28 (the dev DB's
"Default" group id) was removed: not portable, and it silently assigned unpicked grants to Default.

## BannerLayer, BannerDesign, Clan

| Seed | Change |
|---|---|
| BannerLayer | Step "Layer" kept; descriptions for pattern (key format, unknown keys skipped), colour, order (empty = on top, duplicates allowed) and the read-only banner link; `enumType: BannerDyeColor` added |
| BannerDesign | "General" -> "Banner" (Name, Base colour), "Layers" kept; Name described as picker-only; Layers says Create New / save first / 16 and 6 layer limits; `enumType` added |
| Clan | "General" -> "Name and colour" (Name, Colour, NPC clan), "Identity" -> "Banner and town"; IsNpc default `false` -> `False`; colour and banner descriptions say where they show (siege menus, messages, capture effects, objectives; `knk-paper` siege code); NPC flag described as informational (no behaviour yet) |

## NotOnFormByDesign

None for this group. `PermissionGrant.HolderId` is on the form now. `BannerLayer.BannerDesignId` was already on its form
(read-only, prefilled by the child modal).

## Open questions / decisions for the developer

1. **User update mapping (API bug, recommend fixing in the API).** `UserMappingProfile` (UserDto -> User) does not ignore
   `IsFrozen` / `FrozenReason`, although `User.cs` says they are "ignored by the generic UserDto update path". The seed works
   around it with read-only fields; adding `.ForMember(d => d.IsFrozen, o => o.Ignore())` (and FrozenReason) would make the
   workaround unnecessary. `EmailVerified`/`AccountCreatedVia` are likewise better ignored on update.
2. **User create vs edit.** Update refuses an empty Email ("Email is required"), and create refuses an Email without a
   Password, which no form field can supply (User has no Password property). So this form can create only a
   username-only account and can only edit accounts that have an email. Minecraft-only players can't be edited here at
   all. Email stays optional (safe for create). Decide whether the generic edit should allow an empty email.
3. **Gender empty option.** Re-selecting the empty option of an Enum sends `""`; the API has no tolerant nullable-enum
   converter (`Json/NullableNumberConverters.cs` covers numbers/bool only), so the whole body fails to bind and the
   controller answers 400 "User data is required". An untouched field sends null and is fine. Suggest a
   `NullableEnumConverter` or have the wizard send null for an empty Enum.
4. **Bonus multipliers only for premium ranks.** Kept the old behaviour, but the model applies Gem/XP bonus multipliers
   from every active group. Drop the condition if staff or other groups should set them.
5. **ChatPrefix / ChatSuffix** are on the form (they were before) but nothing in the plugin renders them. Keep, or
   remove from the form until chat uses them?
6. **Grants from the group form.** An owned "Grants" step on PermissionGroup would be natural but doesn't work today:
   `findParentLinkField` matches the child's objectType (`PermissionHolder`) to the parent type (`PermissionGroup`), and
   `PermissionGroupDto` has no grants list. Needs a web-app change (match subtypes) plus grants on the DTO if wanted.
7. **Weight description** quotes the current ranks (0 Default to 40 Staff, from the dump). Drop the numbers if they
   are expected to change.

---

## Items group: Grade, Tag, Category (+ CategoryTag.join), EnchantmentDefinition, ItemBlueprint (+ 3 joins)

All 9 pass the static checks. Configuration GUIDs and names unchanged; every
kept step/field keeps its starter GUID. New GUIDs (uuid5 on the seed's configurationGuid):
`Grade` field:DropChance `6ef2be9a-…`, field:EnchantLevelCapDivisor `7d13a4fd-…`;
`ItemBlueprintOrigin.join` field:SequenceNumber `fc72e46f-…`. No new steps.

All seeds: every step and field now has a description; labels in admin terms; **field names changed from camelCase
(`name`, `stars`, `iconMaterialRefId`, …) to the metadata's PascalCase** (`Name`, `Stars`, `IconMaterialRefId`), like
every other seed. Everything I found matches field names case-insensitively (FormTemplateValidationService, the
wizard's edit-mode load `applyInitialFieldValuesToSteps`, ItemScan's `resolveFieldName`, value-projection targets,
ASP.NET's case-insensitive JSON binding), so nothing should change in behaviour; see Q6.

Sources: the models/DTOs/services in the API, `normalizeFormSubmission.ts`, `FormWizard.tsx` (ItemScan/KitScan
apply), `FieldRenderers.tsx`, `valueProjection.ts`, `conditionEvaluator.ts`, knk-plugin
(`ItemBlueprintBukkitMapper`, `BlueprintItemAssembler`, `ItemScanTaskHandler`), `items/IMPLEMENTATION_PLAN.md`
§3-4, `items/GRADE_DROPCHANCE.md`, `enchantment-books/ENCHANTMENT_BOOK_APPLICATION.md`, `lootboxes/DESIGN.md` §3,
`custom-enchantments/ABILITY_DEFINITION_REFERENCE.md`, the dump (154 blueprints, 10 grades, 26 enchantment
definitions, 8 tags, 9 categories, 3 origins).

## Grade

| Step | Before | After |
|---|---|---|
| Grade (was General Information) | Name, Stars (no descriptions) | Name, Stars, **Drop chance (%)** (`DropChance`, Decimal, optional, new), **Enchantment cap divisor** (`EnchantLevelCapDivisor`, Integer, optional, new) |

- The two KNG-6 columns (`AddGradeDropChanceAndEnchantCap`, 2026-09-26) were never added to the form; GRADE_DROPCHANCE.md
  §7 says the existing form must get them. Descriptions explain lootbox weighting (null weighs 0, a lootbox type can
  override) and the cap formula (max level / divisor, integer division; empty = uncapped; custom not capped).
- No defaults: every real grade sets its own values (dump: 70/60/40/25/15/8/5/1/0.5/0.05; divisors 5..1, null 6-10).
- `GradeUpdateDto` only writes the two values when the request contains them; the form always sends them now, so an
  emptied field really clears the value (intended).

## Tag

| Step | Before | After |
|---|---|---|
| Tag (was General Information) | Name | Name; description warns about the special "Lootbox Special" tag (excluded from normal pools, re-created by code if renamed) |

## Category / CategoryTag.join

| Step | Before | After |
|---|---|---|
| Category (was General Information) | name, parentCategoryId, iconMaterialRefId | Name, Parent category, Icon (same types; descriptions: catalogue filter, lootbox subcategories, no cycles) |
| Tags (M2M) | Tags | unchanged wiring; step/field descriptions (category tags are informational; rules read item tags) |
| CategoryTag.join: Tag | TagId | unchanged; descriptions |

## EnchantmentDefinition

| Step | Before | After |
|---|---|---|
| Enchantment (was General Information) | key, displayName, description, isCustom, maxLevel, minecraftEnchantmentRefId | **Custom enchantment** (`IsCustom`, default False) first; **Vanilla enchantment** (`MinecraftEnchantmentRefId`, Object picker) shown only when not custom (display condition `IsCustom NotEquals true`) with a **value projection** that fills Key ← namespaceKey, Display name ← displayName, Max level ← maxLevel (if empty); then Key (placeholder `minecraft:sharpness`), Display name, Description, Max level |

- The projection is taken from the dev DB's older "EnchantmentDefinition Template 2" (export 08), moved from its
  `HybridMinecraftEnchantmentRefPicker` onto the Object picker (see Q2 for why not the hybrid picker).
- Max level has no `defaultValue` any more: the projection's `if-empty` would not overwrite a seeded 1 with
  Sharpness's 5. It stays required.
- Hidden vanilla picker on a custom row: not submitted, and the API keeps the existing reference on update.

## ItemBlueprint

| Step | Before | After |
|---|---|---|
| Item in game (was General Information, GUID kept) | name, defaultDisplayName (ItemScan), description, defaultDisplayDescription, categoryId, iconMaterialRefId, gradeId, defaultQuantity, maxStackSize | Display name (ItemScan + `minecraft-text-color`, placeholder `&9Steel Sword`), Display description (`minecraft-text-color`), **Material** (`IconMaterialRefId`, hybrid material picker), Default quantity (default 1), Max stack size (**default 64**, model default) |
| Catalog and price (was Pricing, GUID kept) | basePriceMin, basePriceMax | Name, Description, Category, Grade, Lowest price, Highest price (prices default **0**) |
| Default enchantments (M2M) | DefaultEnchantments | unchanged wiring; descriptions (scan fills it; enchantment-book blueprints teach it; lootbox rolls never lower it) |
| Origin (M2M) | Origins | unchanged wiring; descriptions (lowest order number shown as "Origin: <place> (<type>)" lore) |
| Tags (M2M) | Tags | unchanged wiring; descriptions ("Lootbox Special") |

- **Why the scan step comes first and holds exactly these fields:** `applyItemScanResult` only fills fields of the
  step that holds the scan field (`DefaultDisplayName`, `DefaultDisplayDescription`, `IconMaterialRefId`,
  `MaxStackSize`) plus the `DefaultEnchantments` M2M step. Name/Description/Category/Grade are not scanned, so they
  moved to the second step. Holding the item and scanning is the natural first action.
- `minecraft-text-color` (preview of `&` colour codes) taken from the older export 05; display names in the dump use
  `&`/`§` codes.
- Price fields keep `isRequired: true` with default 0: an emptied Decimal is sent as null, which can't bind to the
  non-nullable `decimal` in the DTO. Same reason for quantity/stack size.
- Merged Pricing into the catalog step (2 fields alone is below the guide's 3); plan §4.1 had kept Pricing separate
  for future currency fields — easy to split again (Q7).
- **Properties added since Oct 7:** none. No migration after `20260926…` touches `item_blueprints`, so nothing new
  to add. (The Sep-26 Grade columns were the missing ones, above.)

## ItemBlueprint joins

| Seed | Before | After |
|---|---|---|
| ItemBlueprintDefaultEnchantment.join: Enchantment | EnchantmentDefinitionId, Level | same; **Level default 1** (model default); descriptions (book level; above max is applied as given) |
| ItemBlueprintOrigin.join: Origin | DomainId | Place (`DomainId`) + **Order** (`SequenceNumber`, Integer, required, default 0, new) |
| ItemBlueprintTag.join: Tag | TagId | same; descriptions |

- **SequenceNumber was missing** although plan §3.2/§4.1 names it. Without it every origin is 0, and a second origin
  on one item fails (`ItemBlueprintService.AddOriginsAsync`: "Duplicate SequenceNumber", unique index
  `(ItemBlueprintId, SequenceNumber)`). Required with default 0 because a null int in a join entry can't bind.
- Join field names stay PascalCase (`TagId`, `EnchantmentDefinitionId`, `DomainId`, `Level`): the join path matches
  them exactly (PHASE_2_FORMCONFIGS.md §4) and ItemScan writes `EnchantmentDefinitionId`/`Level`.

## Properties left off (and why)

| Entity | Property | Why |
|---|---|---|
| ItemBlueprint | `DefaultEnchantmentIds` | Legacy JSON column (`[]` in every row), never written by the service; the M2M step is the real data |
| ItemBlueprint | `IconMaterial`, `Category`, `Grade` | Navigations; the FK fields are on the form |
| Category | `ChildCategories`, `IconMaterialRef` | Inverse collection of `ParentCategoryId`; navigation of the icon FK |
| EnchantmentDefinition | `AbilityDefinition` | Custom-ability wiring has its own tooling and is (re)seeded from `AbilityDefinition.CanonicalCatalog` at API start (plan §4.2); see Q3 |
| EnchantmentDefinition | `DefaultForBlueprints`, `AppliedToInstances`, `BaseEnchantmentRef` | Inverse of the blueprint M2M; runtime (minted items); navigation of the FK on the form |
| Joins | `ItemBlueprint`, `Category`, `Tag`, `Domain`, `EnchantmentDefinition` navigations | Navigations; FKs on the form or set by the parent step |

## NotOnFormByDesign (for the test allow-list)

```
["CategoryTag"] = new() { ["CategoryId"] = "set by the category's Tags step (join entry)" },
["ItemBlueprintDefaultEnchantment"] = new() { ["ItemBlueprintId"] = "set by the blueprint's Default enchantments step (join entry)" },
["ItemBlueprintOrigin"] = new() { ["ItemBlueprintId"] = "set by the blueprint's Origin step (join entry)" },
["ItemBlueprintTag"] = new() { ["ItemBlueprintId"] = "set by the blueprint's Tags step (join entry)" },
```

## Open questions / decisions for the developer

1. **Hybrid material picker loses catalog-only materials (bug, web app/API mismatch).** `handleHybridMaterialField`
   writes `{field}Id` + `{field}NamespaceKey`, i.e. `IconMaterialRefId` + `IconMaterialRefNamespaceKey`; the
   ItemBlueprint and Category DTOs read `iconNamespaceKey`. A material that isn't persisted yet comes back from the
   hybrid search with id 0, so the API gets id 0 and no key: on create no material, on update the old one stays,
   silently. Persisted materials (listed first) and the item scan (`persistFromCatalog`) work. Fix in the API (accept
   `iconMaterialRefNamespaceKey` too) or in `normalizeFormSubmission`. The seed keeps `IconMaterialRefId`: it is the
   only name that both binds the id and is what ItemScan fills (`IconMaterial` from export 05 would send
   `IconMaterialId`, which nothing reads).
2. **`HybridMinecraftEnchantmentRefPicker` can't be used for a single FK**: `normalizeFormSubmission` only special-cases
   the material variant, so an enchantment hybrid field is copied as a raw object into the payload. Hence the plain
   Object picker (as PHASE_2 decided). But the Object picker for `MinecraftEnchantmentRef` also runs the hybrid
   search: catalog-only enchantments (only 13 are persisted in the dev DB) come back with id 0, and the link is then
   not saved (Key/name/max level are still projected, so the definition works by Key). Persist on pick, or send
   `enchantmentNamespaceKey`?
3. **Editing a custom enchantment in the web app deletes its AbilityDefinition** (API bug, not seed-fixable):
   `EnchantmentDefinitionService.UpdateAsync` sets `AbilityDefinition = null` when `IsCustom` and the request has no
   `abilityDefinition` — and no form sends one. The 12 canonical abilities are restored at the next API start; any
   other custom ability is lost. Suggest the `GradeUpdateDto` "only write what was sent" pattern.
4. **EnchantmentDefinition.Description must be required** (non-nullable, `= string.Empty` is not seen as a default),
   yet all 13 vanilla rows have `''`: an admin can't save a vanilla definition without typing a description. A
   `[DefaultValue("")]` on the model (as done for GateDoor in KNG-193) would let the form make it optional.
5. **Material is optional but needed in game**: `ItemBlueprintBukkitMapper.fromBlueprint` throws without a material,
   so such blueprints can't be handed out. 18 dev-DB trade goods have none. Kept optional (nullable column); make it
   required on the form?
6. **Field-name casing** (camelCase → PascalCase in the 5 top-level seeds): please confirm in the live review that
   create and edit still work for Grade (esp. the partial `HasDropChance` update) and ItemBlueprint (ItemScan
   auto-fill). Revert to camelCase if anything doesn't bind.
7. **ItemBlueprint step layout**: scan step first, Pricing merged into "Catalog and price". Split prices back out if
   you want the separate step plan §4.1 reserved for currency fields.
8. **No uniqueness anywhere** for `Grade.Stars`, `Tag.Name`, `Category.Name`, `EnchantmentDefinition.Key` (no index,
   no service check). Descriptions ask admins to keep them unique; the plugin takes the lowest id on duplicate stars.
9. **No cross-field checks**: `BasePriceMax ≥ BasePriceMin` and default enchantment `Level ≤ MaxLevel` are not
   validated by the API or the form. Left as descriptions; a validation rule could be added later.
10. **Base price currency**: nothing uses the price yet and no doc names the currency; descriptions say "price"
    without "coins".

---

## Loot group: kits and lootboxes (KNG-139 priority 3)

Seeds: `Kit`, `KitContent.join`, `LootboxType`, `LootboxTypeGradeWeight.join`, `LootboxPoolEntry.join`,
`LootboxEnchantRoll.join`, `LootboxSpecialEntry`, `LootboxSpawnArea`, `LootboxSpawnAreaType.join`.
All nine pass the static checks and parse. Configuration GUIDs and names are unchanged, every starter step and field
GUID is kept, and no field was dropped. New steps use `uuid5(configurationGuid, "step:" + name)`. No new fields were added.
Boolean defaults are now `"True"`/`"False"`.

Sources checked: the models, `KitService`, `LootboxTypeService`, `LootboxSpecialEntryService`,
`LootboxSpawnAreaService` and `LootboxRuntimeService` validation and runtime, `BalanceLimits`, the snapshot check
constraints (`CK_kits_CostAmount_NonNegative`, `CK_kits_PremiumPriceGems_NonNegative`), kits/lootboxes `DESIGN.md`
and `PHASE_*_FORMCONFIGS.md`, the plugin (`WorldTaskHandlerRegistry`, `WgRegionIdTaskHandler`,
`TempRegionRetentionTask`, `KitGrantPlacer`, `KitCommand`, `LootboxPresenter`, `LootboxSpawnPlanner`), the web app
(`WorldBoundFieldRenderer`, `FieldEditor`, `FormWizard.applyKitScanResult`, `formVisibility`), and the dump's real rows.

**Properties added since the form was built:** none on these entities. The Sep 26-27 migrations `AddLootboxTokens`
and `AddLootboxWorldPickup` only touch `lootbox_tokens`, `lootbox_token_grants` and `lootbox_claims`, which are not
form-configurable. `AddBalanceAndKitPriceCheckConstraints` adds only `>= 0` checks; the service also caps the price
at 999,999,999 Coins or 999,999 Gems. The descriptions state these limits. World pickup changes the meaning of three
fields, and their texts now say so: the per-type daily limit counts pickups and opens, a box's look is the token item
by default, and `LifetimeMinutes` means "until picked up".

## WgRegionId world task (LootboxSpawnArea): `WgRegionId` changed to `ReagionCreate`

- **What the field needs:** a WorldGuard region id string. It can be an existing region or a new one.
- **Plugin:** `WorldTaskHandlerRegistry.getHandler(taskType, fieldName)` tries a handler registered by task type first,
  then falls back to the field name. Only `LocationSelection`, `ItemScan` and `KitScan` are registered by task type,
  so neither `WgRegionId` nor `ReagionCreate` nor `RegionClaim` has a handler of its own. With the field name
  `WgRegionId`, all three end up in `WgRegionIdTaskHandler`. That handler offers two things: draw a WorldEdit
  selection and type `save`, which creates `tempregion_worldtask_<taskId>`, or type `select <region>` to use an
  existing region. It returns `{regionId, worldName}`.
- **Web app:** `WorldBoundFieldRenderer.TASK_OUTPUT_FIELD_MAP` maps both `ReagionCreate` and `WgRegionId` to
  `regionId`; `RegionClaim` reaches it through the generic fallback. No other code branches on these types.
- **Choice:** `ReagionCreate`. It is in the known list, explicitly mapped, and used by Town/District and by the
  GateStructure seed (L7-5 precedent), so it behaves exactly as before.
- **Found while checking (open question Q1):** a region drawn through this task is never renamed. The domain
  finalizer only covers Town, District, Structure, GateStructure and Domain. After 14 days `TempRegionRetentionTask`
  deletes any `tempregion_worldtask_*` region that no **domain** uses, so a spawn area on a drawn region silently
  stops spawning. The region is also only as high as the selection; `/knk lootbox area create` stretches its region
  to full build height. The step and field descriptions therefore steer admins to `select <existing region>`, or to
  `/knk lootbox area create <name>` in game for new regions.

## Kit (`Kit - Default (seed v1)`)

| Step | Was | Now |
|---|---|---|
| Kit | "General Information" with 10 fields | Name (KitScan stays bound here) and Description. The step text explains the scan, which fills the Equipment and Contents steps (`applyKitScanResult` finds the pickers on any step). |
| Equipment (new) | (in step 1) | Helmet, Chestplate, Leggings, Boots, Off-hand (`ShieldId`), Main hand (`HandId`). The step text describes what `KitGrantPlacer` does when a slot is taken. |
| Contents | no descriptions | Same M2M step. Label "Inventory slots". |
| Who can claim | "Access Conditions" | Minimum title, Permission group, Permission node (placeholder `knk.kit.archer`), plus **Give on first join**, moved here from step 1 because gating applies to it too. |
| Cooldown and price | "Economy" | Cooldown (moved here from step 1), Price per claim, Price currency, Premium kit (buy once), Premium price. Premium price only shows when Premium kit is ticked (display condition; hidden fields aren't submitted, so a non-premium kit saves no price). |

Fixes to wrong text:
- **Permission group:** the old text said "(or a group inheriting from it)". `CheckGatingAsync` only accepts an
  **active direct membership** of that group.
- **Equipment:** the kit's item goes to a free slot; the item the player already wears is not moved.
- **Price per claim:** a price without a currency charges nothing.
- **Premium kit:** a premium kit without a price can't be bought.

## KitContent.join

| Step | Was | Now |
|---|---|---|
| Inventory slot | "Slot", item without description | Item, Slot (0-35, one entry per slot, enforced by `KitService.BuildContentsAsync`), Quantity. The step text describes what happens on an occupied slot. |

## LootboxType

| Step | Was | Now |
|---|---|---|
| Type | "Basics" with 11 fields | Name (128 chars max, shown as "<grade> <name>"), Category (one type per category), Include subcategories, Enabled. |
| Spawning and grades (new) | (in Basics) | Spawn weight (0 = never spawns), Lowest/Highest box grade, Item grade spread. |
| Look and limits (new) | (in Basics) | Box model, Daily limit per player, Announce drops from item grade. |
| Grade weights | "Box grades" with a long label | Same M2M step, descriptions added. |
| Item pool | "Pool" with a long label | Same M2M step, descriptions added. |
| Enchantment rolls | "Enchant rolls" | Same M2M step, descriptions added. |

Fixes to wrong text:
- **Box grades:** the old text said "1-5 (no ★6+ boxes yet)". DESIGN D5 was amended on 2026-09-27, and the service
  validates against the Grade table (`LootboxRollEngine.MaxBoxStars` = 10). The dev DB already has types with max 10.
  The text now says 1-10, and that new types use 1-5.
- **Box model:** it is only used when the plugin's `lootboxes.display.model` is `type`. The default is `token`: the
  box looks like the lootbox token item (§3.8).

## Lootbox join seeds

- **LootboxTypeGradeWeight.join:**
  - Step "Weight" renamed "Grade weight".
  - Grade: grades outside the type's window have no effect, one entry per grade.
  - Weight: 0 or more, relative to the drop chances; 0 = never at this grade.
- **LootboxPoolEntry.join:** step "Pool entry" stays. Item gets a description. Weight and Drop grade are marked
  "ignored for Exclude".
- **LootboxEnchantRoll.join:** step "Enchant roll" renamed "Enchantment roll".
  - Levels: `1 <= min <= max <= definition max`.
  - Box grade: was "(1-5)", now 1-10.
  - Chance: 0-100 with 4 decimals.
  - The step says enchantment books and stackables never roll.
- **LootboxSpawnAreaType.join:** step "Type" renamed "Box type". Disabled or weight-0 types never spawn, even when
  listed.

## LootboxSpecialEntry

| Step | Was | Now |
|---|---|---|
| Item and box | "Special" with 6 fields | Item (tagged Lootbox Special on save; delete keeps the tag), Box type (empty = every type), Enabled. |
| Odds (new) | (in Special) | Chance per million (0-1,000,000), From box grade (was "1-5", now 1-10), Order (first hit wins). |

## LootboxSpawnArea

| Step | Was | Now |
|---|---|---|
| Area and region | "Area": Name, World, Region, Enabled, Excluded | Name (pattern, unique, placeholder `cinix_market`), Region (task type fixed), World (moved **after** the region so the admin can copy the world shown in the task result), Excluded regions (placeholder, 1024 chars max). |
| Spawn rules | "Limits", 6 fields | Enabled (moved here), Try every (seconds, at least 60), Spawn chance (0-100), Minimum players online. |
| Box limits (new) | (in Limits) | Maximum boxes at once (the global limit also applies), Minimum distance from players, Box lifetime (at least 1 minute; unpicked boxes crumble). |
| Allowed types | long label | Same M2M step, description added. |

**World default removed (decision, flag):** the old default was `world`. The dev server's world is `world_KNK-DEV`
(380 `'world_KNK-DEV'` against 12 `'world'` literals in the dump; the only real area row uses it), and the API refuses a spawn whose world doesn't match the area's. A wrong
default would make an area silently never spawn, and KNG-118 D9 says a missing world must never silently become
`world`. The field is now required with no default and no placeholder (the placeholder `world` was removed on 2026-10-22, developer decision; KNG-247 tracks a default from the game's own default world). The web app drops the
task's `worldName` (multiworld audit U1), so it can't be filled automatically.

## Properties not on the forms

| Entity.Property | Why |
|---|---|
| Kit.Id, LootboxType.Id, LootboxEnchantRoll.Id, LootboxSpecialEntry.Id, LootboxSpawnArea.Id | primary keys |
| Navigation properties (Helmet, Category, DisplayMaterial, GradeOverride, …) | the FK side is authored |
| LootboxSpawnArea.CreatedByUserId | set by `/knk lootbox area create`; the web form leaves it alone |
| Kit lootbox token grants (`LootboxTokenGrant.KitId`) | not form-configurable; edited on the lootbox admin page |
| LootboxConfiguration (global limits, announcements), LootboxSpawn, LootboxClaim, LootboxToken, LootboxTokenGrant, KitClaim, KitPurchase | not form entities: runtime or append-only logs, or settings on `/admin/lootboxes` |

### NotOnFormByDesign (for the test allow-list)

```
["KitContent"] = { ["KitId"] = "set by the kit's Contents step (join entry)" },
["LootboxTypeGradeWeight"] = { ["LootboxTypeId"] = "set by the lootbox type's Grade weights step (join entry)" },
["LootboxPoolEntry"] = { ["LootboxTypeId"] = "set by the lootbox type's Item pool step (join entry)" },
["LootboxEnchantRoll"] = { ["LootboxTypeId"] = "set by the lootbox type's Enchantment rolls step (join entry)" },
["LootboxSpawnAreaType"] = { ["LootboxSpawnAreaId"] = "set by the spawn area's Allowed types step (join entry)" },
```

## Open questions / decisions for the developer

- **Q1 (plugin/API, not form):** regions drawn through the spawn area's region task are named
  `tempregion_worldtask_<id>` and are deleted by the 14-day temp-region cleanup, because no domain uses them. They
  are also only as tall as the selection. Options:
  - rename them to `lootbox_<name>` on save, as the in-game command names its regions;
  - make the retention task ask the lootbox areas too;
  - or accept it: the form text now steers admins to `select <region>` or `/knk lootbox area create`.

  Either fix also lets the in-game `delete` clean such regions up.
- **Q2:** the World field lost its `world` default (see above). If production's world really is `world` and you
  want the convenience back, restore `defaultValue: "world"`.
- **Q3:** in the Kit seed, Premium price is hidden unless Premium kit is ticked. Unticking Premium and saving clears
  the price, because hidden fields aren't submitted. That seemed right to me; drop the condition if you'd rather
  keep the price.
- **Q4:** the Kit step "Kit" has only 2 fields (Name, Description), under the guide's 3-8. I kept the scan step
  separate so the equipment it fills gets its own step. The other option is one 8-field step with Name, Description
  and the six slots.
- **Q5:** the LootboxPoolEntry seed has no display conditions for Weight and Drop grade when Mode = Exclude. I
  wasn't sure display conditions work inside join-entry sub-forms, so the descriptions say "ignored for Exclude"
  instead.
- **Q6:** kit names aren't unique in the API, but `/kit get <name>` takes the first case-insensitive match. The
  description asks for unique names; a unique index could enforce it.

---

## Display configuration seeds — notes (2026-10-19)

Format spec: `Data/DisplayConfigurationSeeds/README.md` (`knk-displayconfig/1`). An offline check of the seeds against the C# models (approximating the importer rules of README §3) found 0 errors and 11 warnings, all on elements deliberately marked `metadataCheck: "warn"`; the 7 old configs had 7 errors (properties that no longer exist, an FK used as a navigation, a broken sub-section).

 configurationGuid | Sections | Fields | Old config (id, state) |
|---|---|---|---|---|
| MinecraftMaterialRef | aeb91332-aef6-566c-8847-6e006bd2b025 | 1 | 4 | 1, draft, default |
| Structure | e0242a63-86e8-53b9-9cf8-9e6d4556988a | 4 | 15 | 2, published, default |
| GateStructure | 7b10e6c7-207f-5df7-9cbf-7198e0e96ac6 | 7 | 32 | 3, published, default |
| User | 7175d815-8e24-58bf-972f-aba0e6b0cd57 | 6 | 24 | 4, published, default |
| ItemBlueprint | 2ef61874-67ee-5448-8dc0-6f01a64a0918 | 6 | 20 | 5, published, default |
| EnchantmentDefinition | 64e3bfbc-0d1f-5cc1-8bab-6e026fa8e533 | 4 | 12 | 6, draft, not default |
| ItemBlueprintDefaultEnchantment | 50fba8c3-26e4-5232-9f7f-91cc8daccbe4 | 1 | 5 | 7, published, default |

GUIDs: `configurationGuid = uuid5(old, "seed-v1")`; kept sections/fields `uuid5(old, oldGuid)`; new ones
`uuid5(new, "section:"+name)` / `uuid5(new, "field:"+key)` (key = `Rel.FieldName`, `FieldName` or the label of a
template-only field). Names are `<Entity> - Default (seed v1)`.

## What drove the changes (renderer facts, verified in code)

1. **Object/List fields print `[object Object]`.** `formatValue` only formats dates, numbers and booleans. The old
   ItemBlueprint (`Grade`, `Category`, `Origins`, `Tags`, `IconMaterial`) and GateStructure (`GateType` as Object,
   `AnchorPoint`) and EnchantmentDefinition (`AbilityDefinition`) fields could never show anything useful. Seeds show
   a property *of* the related object (related field) or use a related/collection section.
2. **The page reads the read DTO, not the model.** `IconMaterial` (model) is `iconMaterialRef` on the GateStructure and
   ItemBlueprint read DTOs, so the old "Material" field always showed "-". Computed DTO values (`titleName`,
   `district.townName`, `permissionGroups`) are available and used, marked `metadataCheck: "warn"`.
3. **Buttons on root sections are never rendered** (`ActionButtons` returns null without `relatedEntityTypeName`);
   Select/Unlink/Add/Remove are hidden until KNG-148. All such button settings were dropped. Buttons kept only where
   they work: Edit on GateStructure Doors (door ids) and Spawn point (location id), Edit on User Permission groups
   (group ids), View+Edit on EnchantmentDefinition "Default on item blueprints" (blueprint ids; ItemBlueprint has a
   published display config and a form). The join-row collections (default enchantments, tags, origins) get **no**
   buttons: their items have no `id`, so View/Edit opened `…/undefined`; Create on a collection opens the form
   without parent context.
4. **Hot edit sends the whole root entity to the update endpoint.** On User, `Coins/Gems/ExperiencePoints` were
   hot-editable but `UserMappingProfile` ignores them since KNG-22 (the save "succeeds" and changes nothing), and the
   generic update requires an email (in-game-only players have none) — **nothing on User is hot-editable now**.
   Kept: Structure/GateStructure Name, Description, Allow entry/exit, Structure House number; ItemBlueprint names, lore,
   quantities (the read DTO round-trips into `ItemBlueprintUpdateDto`). Added ItemBlueprint Name/Description.
5. **Only default + non-draft configs are shown** on `/display/:entity/:id`; MinecraftMaterialRef (draft) and
   EnchantmentDefinition (draft, not default) were never visible. "Finished" here = the importer publishes them
   (FORMAT I3) and, having no other default, they become the default (I2).
6. **Sub-sections render only as the item template of a collection section without fields.** The two sub-sections of
   ItemBlueprint "Default Enchantments" ("Item Template", "Item Template ghjfgc") were never rendered and one was
   invalid (its own `RelatedEntityPropertyName` on an item); dropped. All seeds put item fields directly on the
   collection section.
7. **Template text paths are case-sensitive camelCase** (`useTemplateParser` has no casing fallback, unlike
   `fieldName`). The model's own doc comment shows `${Districts.Count}`, which would print empty. Seeds use one
   template (`${healthCurrent} / ${healthMax}` for door health); elsewhere a template over an optional related object
   would print stray literal text when the object is empty, so plain fields were preferred.

## Per configuration

### MinecraftMaterialRef (was draft)
| Old | Seed | Why |
|---|---|---|
| Section "General Information", buttons View/Edit | "Material" (kept GUID), no buttons | Root buttons never render |
| `Id` | dropped | No database ids on the page |
| `NamespaceKey` labelled "LegacyName" | "Namespaced key" | Wrong label |
| — | + Kind (`Category`), Legacy name, Icon image (`IconUrl`) | All properties of the entity; Category tells ITEM/BLOCK/BANNER |

### Structure
| Old | Seed | Why |
|---|---|---|
| "General Information" (reusable): Name, Description, Street.Name "Name", District.Name "Name", HouseNumber | "General information" (Name, Description) + new "Address" (Street, House number, District, + Town) | Two fields were both labelled "Name"; address grouped; Town from `district.townName` |
| "Regions and Rules" (reusable): WgRegionId labelled "Regions and Rules", AllowEntry, "AllowExit" | "Region and access": Region, + World, Allow entry, Allow exit | Labels; `WorldName` (KNG-111) is needed to identify the region |
| — | + "Warp and navigation": Warp destination, Warp price (gems), Only after discovery, /navigate leads to, Road access | Admin-relevant domain settings on `StructureDto` (KNG-17/73/92) |
| `isReusable: true` on both sections | not reusable | The seed is self-contained; GateStructure has its own copies |
Left off: spawn location (`StructureDto` carries only `locationId`, no Location object), minimum title / premium
group for warps (DTO carries ids only), `IsHub` (not on `StructureDto`), `CreatedAt`.

### GateStructure
| Old | Seed | Why |
|---|---|---|
| "General Information" (copy of Structure's): Name "Gate Name", Description, Street/District "Name", `GateType` (Object), `FaceDirection` | "General information": Name, Description, + Icon (`iconMaterialRef.namespaceKey`, renders the material icon) | `GateType`/`FaceDirection` moved to GateDoor (model no longer has them) |
| — | "Address": District, + Town, Street | No house number (form seed decision 2026-10-16) |
| "Regions and Rules" (copy) | "Region and access": Region, + World, Allow entry, Allow exit | As Structure |
| "Geometry: Plane Grid": `AnchorPoint` | dropped | Moved to GateDoor |
| — | + "Spawn point" (related `Location`, X/Y/Z/World, Edit) | Where /gate tp, /warp, /navigate land |
| — | + "Warp and navigation": Warp destination, Warp price (gems), Only after discovery, /navigate leads to, Road access | Added 2026-10-22 on the developer's request (KNG-229: the gate form seed v2 edits these, and `GateStructureDto` carries the warp settings since `master` `be9f4e1`) |
| — | + "Siege behaviour": Animate during a siege, Siege may take over, Locked by siege match (`CurrentSiegeId`), Siege objective | Siege integration; runtime values labelled as set by the server |
| — | + "Door overrides": 8 structure-level overrides | Shows when a whole-gate override is in force ("-" = none) |
| — | + "Doors" collection (Door, Type, State, Health template, Active, Destroyed; Edit) | The gate's doors were not on the page at all |
Left off: guard fields (`GuardCount`, `GuardSpawnLocations`, `GuardNpcTemplateId` — "future feature"), the other six
overrides (health display mode/offset, gate name/status display modes, continuous damage + multiplier), `HouseNumber`,
`CreatedAt`.

### User
| Old | Seed | Why |
|---|---|---|
| "Login Information": `Id` labelled "Username", Username (hot edit), Uuid, Email, IsActive, CreatedAt | "Account": Username, Minecraft UUID, Email, + Email verified, + Web account, + Created via, Created, Active | Id dropped; hot edit off (see fact 4) |
| "Game Statistics": Coins, Gems, ExperiencePoints (all hot edit) | "Progress and currencies": same + Title, Prestige experience, Salary multiplier; read-only | KNG-22: balances change only via Adjust balance on the Player profile page |
| "Permissions and Settings": GatePassThroughMethodDefault | "Settings": Gate pass-through, + Staff mode, + Gender | Preferences set in game |
| — | + "Premium tier" (tier name, expiry), + "Permission groups" collection (Edit opens the group), + "Presence and moderation" (Online, Last seen, Frozen, Freeze reason) | Admin-relevant state on `UserDto` |
Left off (not on `UserDto` or sensitive): password hash and audit dates, token version, deletion fields, transfer
lock, first-join kits date, chat colours, personal gem/XP bonus multipliers.

### ItemBlueprint
| Old | Seed | Why |
|---|---|---|
| One 13-field section "ItemBlueprint Information" with 5 root buttons | "General information": Admin name, + Admin description, Display name, Lore, Material (`iconMaterialRef`, icon) | Id dropped; Material fixed (fact 2); root buttons dropped |
| Grade, Category as Object | "Classification": Category (name), Grade (name), + Stars | fact 1 |
| DefaultQuantity, MaxStackSize, BasePriceMin/Max | "Stack and price" | Grouping, labels |
| Origins, Tags as List | "Tags" and "Origins" collections (tag name; place, kind of place, order) | fact 1 |
| "Default Enchantments" collection: DisplayName/IsCustom/MaxLevel/BaseEnchantmentRef, all 7 buttons, 2 empty sub-sections | Enchantment, + Level, Max level, Custom; no buttons, no sub-sections | `BaseEnchantmentRef` is not on the nav DTO; `Level` (the point of the join) was missing; facts 3 and 6 |

### EnchantmentDefinition (was draft, not default)
| Old | Seed | Why |
|---|---|---|
| "Section 1": DisplayName, IsCustom, MaxLevel | "Enchantment": Name, + Key, + Description, Max level, Custom | All own properties |
| `MinecraftEnchantmentRefId`.DisplayName / .LegacyName | "Vanilla enchantment" (related `BaseEnchantmentRef`): Minecraft key, Minecraft name, Vanilla max level | An FK int was used as navigation (always "-"); nav DTO has no LegacyName |
| AbilityDefinition.AbilityKey, AbilityDefinition (Object) | "Custom ability" (related `AbilityDefinition`): Ability, Ability settings | fact 1 |
| — | + "Default on item blueprints" collection (View/Edit the blueprint) | Where the enchantment is used |

### ItemBlueprintDefaultEnchantment
"Section 1" → "Default enchantment": + Item blueprint, Enchantment, + Level, Max level, Custom. **This page cannot be
opened**: there is no API endpoint for a single entry (composite key, no `id`) and no fetch-by-id in
`entityApiMapping.ts`; the item blueprint is also not on `ItemBlueprintDefaultEnchantmentDto`. Authored for
completeness only.

## Open questions for the developer

1. **ItemBlueprintDefaultEnchantment display config**: delete it (recommended; the blueprint page shows the entries)
   or add `GET` for a single entry + fetch-by-id? If deleted, drop this seed.
2. **Renderer fixes worth doing** (each would let the seeds do more): (a) template paths with the same casing fallback
   as `fieldName`; (b) templates over an empty related object should render "-", not their literal text;
   (c) an icon for root sections of MinecraftMaterialRef/MinecraftBlockRef pages (today the material page can't show
   its own icon without breaking the `RelatedEntityTypeName`-needs-`RelatedEntityPropertyName` invariant);
   (d) hot edit refused in related/collection sections (today it would write the value onto the root entity —
   none of the seeds do this).
3. **User Username**: should admins be able to rename a player here (needs the update to stop requiring an email for
   in-game accounts), or is the name always taken from Minecraft? Seed: read-only.
4. **GateStructure**: show the remaining six door overrides? (The navigation/road-access overrides were added with the
   warp section, 2026-10-22.) Is there (or
   will there be) a web UI for the overrides PATCH, so the section can point to it? Guards stay hidden until built?
5. **GateDoor has no display config**, so a View button on the Doors list would fail; only Edit is offered. Add a
   GateDoor display seed next?
6. **Structure spawn location**: `StructureDto` has only `locationId`; add the `Location` object to the DTO (as
   `GateStructureDto` has) so the Structure page can show the spawn point like the gate page does?
7. **Price currency**: is `BasePriceMin/Max` in coins? Nothing in the API reads it; the description says "for trading"
   only. The plugin was not checked.
8. **Reusable sections**: the old Structure sections were marked reusable and copied into GateStructure. The seeds are
   self-contained and not reusable. Keep it that way (recommended: copies diverged anyway — labels, house number), or
   mark Structure's "Region and access" reusable?
9. **Import as draft or published by default** (FORMAT I3 proposes published, `asDraft=true` to opt out)?

## Importing these files

Implemented 2026-10-19 (branch `claude/determined-franklin-xnfh3r` in knk-web-api and knk-web-app; README §6 lists
the details): `GET api/DisplayConfigurations/{id}/export`, `POST api/DisplayConfigurations/import?dryRun=&makeDefault=&asDraft=`,
`GET api/DisplayConfigurations/seeds`, `POST api/DisplayConfigurations/seeds/{name}/import` (writes behind
`knk.admin.content`), and **Import / Seeds** plus **Export JSON** on the web app's display configuration list.
`DisplayConfigurationSeedFilesTests` dry-runs and imports every file here, checks that only "warn" elements warn,
and that exporting an imported seed gives the file back (without `notes` and `metadataCheck`).

Open questions 1 and 9 above were not decided by the implementation: the ItemBlueprintDefaultEnchantment seed stays
(it imports cleanly), and imports are published unless `asDraft=true` (the README's proposal).

Import order: no seed refers to another, so any order. Because six of the entities already have a default display
config, import with `makeDefault=true` to see the seed on `/display/...` (or import non-default, check it via the
builder, then make it default). EnchantmentDefinition and MinecraftMaterialRef become default automatically if their
old drafts are not default / are deleted first (MinecraftMaterialRef's old draft *is* the default — use
`makeDefault=true`).

---

## Smoke test and developer decisions (2026-10-22)

Smoke test by the developer on `claude/determined-franklin-xnfh3r` with `claude/kng-195-form-submit-fixes` merged
locally: every step passed or was accepted, except 2.5 (Location child form) and 5.1 (Kit scan), skipped because no
"Send to Minecraft" button showed (KNG-245). Findings filed: KNG-243 (a duplicate EnchantmentDefinition after picking a
not-yet-saved vanilla enchantment), KNG-244 (ItemScan copies custom-enchantment and grade lines into Display
description), KNG-248 (players without an email can't be edited; the developer used a placeholder email in the test).

Decisions on the open questions:
- Domain Q7: Structure keeps `RegionClaim`, which both creates and claims existing regions.
- Domain Q5: "only after discovery" stays on by default for Town, District and Structure.
- People Q4: the Gem/XP bonus multipliers stay limited to premium groups.
- Loot Q3 (Kit premium price): keep the value while the form is open, so ticking Premium again restores it; once the
  form is saved, closed or reloaded it is gone. The wizard already does this: a hidden field's value is kept in its
  in-memory hidden stash (`formVisibility.reconcileVisibility`) and not submitted. No change.
- Loot Q1: the renaming task should handle lootbox spawn area regions too, with a less obvious name than
  `lootbox_spawn_area` (KNG-246).
- Loot Q2: World gets the game's default world when one is available, otherwise no default and no placeholder
  (placeholder removed in this file's seed; KNG-247).
- Display Q5: no GateDoor display seed for now.

