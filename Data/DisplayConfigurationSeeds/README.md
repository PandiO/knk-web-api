# `knk-displayconfig/1` — portable DisplayConfiguration files

Status: implemented (2026-10-19): `Services/DisplayConfigurationPortabilityService.cs`, the export/import/seeds
endpoints on `DisplayConfigurationsController`, and Export / Import / Seeds on the web app's display configuration
list. §6 lists what the implementation settled that this spec left open. It is deliberately parallel to
`knk-formconfig/1` (`Dtos/FormConfigurationPortableDtos.cs`, `DEFAULT_FORM_SEEDS.md` §3.2/§3.4): same envelope,
same importer rules, same seed workflow. Where the two differ, the reason is a difference in the display model or
renderer, and it is called out.

Sources this is based on (API = knk-web-api at origin/master, WEB = knk-web-app/src):
`API/Models/DisplayConfiguration.cs`, `DisplaySection.cs`, `DisplayField.cs`, `API/Dtos/DisplayDtos.cs`,
`API/Services/DisplayConfigurationService.cs`, `DisplaySectionService.cs`, `API/Repositories/DisplayConfigurationRepository.cs`,
`API/Properties/KnKDbContext.cs` (display block), `API/Services/MetadataService.cs`;
`WEB/components/DisplayWizard/*` (renderer), `WEB/components/DisplayConfigBuilder/*` (builder),
`WEB/pages/DisplayWizardPage.tsx`, `WEB/utils/entityApiMapping.ts`.

---

## 1. How the display model works today (what the format has to carry)

| Concept | Database / API | What the web app does with it |
|---|---|---|
| Configuration | `DisplayConfiguration`: `ConfigurationGuid`, `Name`, `EntityTypeName`, `Description`, `IsDefault`, `IsDraft`, `SectionOrderJson` (JSON array of section GUIDs) | `/display/:entity/:id` loads **the default, non-draft** config (`GET entity/{name}?includeDrafts=false`). A draft or non-default config is never shown there. Name and description are the page heading. |
| Section order | `SectionOrderJson` = GUID strings of the **top-level** sections | When the list is non-empty, only sections whose GUID is listed are rendered, in list order. Unlisted sections are invisible. |
| Section | `DisplaySection`: `SectionGuid`, `SectionName`, `Description`, `FieldOrderJson`, `RelatedEntityPropertyName`, `RelatedEntityTypeName`, `IsCollection`, `ActionButtonsConfigJson`, `IsReusable`, `SourceSectionId`, `IsLinkedToSource`, `ParentSectionId`, `DisplayConfigurationId` | Heading + description, then fields in a 2-column grid. With `RelatedEntityPropertyName` the section's data is `entity[prop]` (tries the name as given, PascalCase and camelCase). With `IsCollection` that value is an array and every item is a card. |
| Nesting | Top-level sections have `DisplayConfigurationId` set; child sections have `DisplayConfigurationId = NULL` and `ParentSectionId` = parent's id. The repository loads exactly two levels (`Sections → SubSections → Fields`). | Sub-sections are used **only** by a collection section **without fields of its own**: its *first* sub-section is the item template. Everywhere else sub-sections are not rendered. |
| Field order | `FieldOrderJson` per section = field GUIDs | Same rule as sections: if non-empty, only listed fields render, in order. (The builder recomputes it only for top-level sections on save.) |
| Field | `DisplayField`: `FieldGuid`, `FieldName`, `Label`, `Description`, `TemplateText`, `FieldType`, `RelatedEntityPropertyName`, `RelatedEntityTypeName`, `IsEditableInDisplay`, `IsReusable`, `SourceFieldId`, `IsLinkedToSource` | See §1.1. |
| Action buttons | `ActionButtonsConfigJson`, keys `showViewButton`, `showEditButton`, `showCreateButton`, `showSelectButton`, `showUnlinkButton`, `showAddButton`, `showRemoveButton` (`ActionButtonsConfigDto`) | Only rendered when the section has a `RelatedEntityTypeName` (root sections never show buttons). Select/Unlink/Add/Remove are hidden (`RELATIONSHIP_ACTIONS_AVAILABLE = false`, KNG-148). View → `/display/<RelatedEntityTypeName>/<item.id>` (needs a fetch-by-id in `entityApiMapping.ts` **and** a published default display config for that type). Edit → `/forms/<RelatedEntityTypeName>/edit/<item.id>`. Create: single section → form with parent context (`parentEntityTypeName`, `parentEntityId`, `relationshipFieldName`); collection section → plain create form without parent context. Buttons need the item to have an `id`. |
| Reuse | `IsReusable` marks a section/field as a template offered by the builder's "add from template". `SourceSectionId`/`SourceFieldId` record where a copy came from. "Link" mode (`IsLinkedToSource`) creates a section with only name + field order and no fields; nothing resolves the link at read time, so a linked section renders empty. | Builder only. |
| Display conditions | `DisplayConditionGroup.TargetType` is `FormStep` or `FormField` only, and `DisplayCondition.SourceFormFieldId` is a required `FormField` FK. | **Display sections and fields cannot have conditions today.** |
| Draft | Create always stores `IsDraft = true`; update preserves `IsDraft`; only `POST {id}/publish` clears it (and only checks that `SectionOrderJson` is not empty). | The builder's "draft" checkbox has no effect on save. |
| Default | `IsDefault`; create/update refuse a second default for the same entity (409). The builder unsets the old default first. | — |

### 1.1 Field rendering rules (`DisplayField.tsx`, `useTemplateParser.ts`)

- **Data source.** In a single (non-collection) section: a field **with** its own `RelatedEntityPropertyName` reads
  from the **root entity** (`entity[fieldRel][fieldName]`), even inside a related section; a field **without** it reads
  from the section's data. In a collection section every field reads from the item (`item[fieldRel][fieldName]`).
- **`FieldName`** may be a dotted path (`street.name`); each segment is looked up as given, PascalCase, then camelCase.
  What is read is the **read DTO JSON** of the entity (`getFetchByIdFunctionForEntity`), not the model: names that exist
  only on the DTO work (`titleName`, `iconMaterialRef`), model names the DTO renames do not (`IconMaterial`).
- **`TemplateText`** takes precedence over `FieldName`. `${path}` placeholders are replaced from the same data
  source. Paths are **case-sensitive JSON (camelCase) names** — unlike `FieldName`, no casing fallback — and
  `${x.Count}` gives an array's length. An expression containing `+ - * /` is evaluated left to right as arithmetic
  (`${a + b}`); so a property path can never contain `-`. Text outside `${}` is literal. A missing value becomes an
  empty string (a template over an empty related object prints only its literal text).
- **`FieldType`** is a formatting hint: `DateTime`/`Date` → locale date(time), `Integer`/`Number` → locale number,
  `Boolean` → Yes/No, anything else → `String(value)`. `null` → "-". An object or array value therefore shows as
  `[object Object]`; display a property *of* the related object instead (related field or related section).
  Builder choices: `String, Integer, Boolean, DateTime, Decimal, Enum, Object, List,
  HybridMinecraftMaterialRefPicker, HybridMinecraftEnchantmentRefPicker` (WEB `utils/enums.ts`). Enums arrive as their
  name (the API uses `JsonStringEnumConverter`).
- **Icons.** A field whose `RelatedEntityTypeName` (or whose section's) is `MinecraftMaterialRef`/`MinecraftBlockRef`
  and whose source object has `iconUrl` renders the icon with `namespaceKey`. A related single section of those types
  shows the icon in its header.
- **Hot edit** (`IsEditableInDisplay`) shows a pencil only for `String/Integer/Number/Boolean` fields with a
  `FieldName` and no field-level `RelatedEntityPropertyName`. Saving sends the **whole root entity** with
  `[fieldName] = value` to the entity's update endpoint. Consequence: a hot-editable field inside a related or
  collection section would write the related value onto the root entity — never allowed (§3, rule E6).

### 1.2 Backend vs frontend: stored differently

| Stored as | Used as | In the file |
|---|---|---|
| `SectionOrderJson`, `FieldOrderJson` (GUID arrays) | render order + visibility filter | **array order**; the importer writes both lists |
| `ParentSectionId` (int) + `DisplayConfigurationId = NULL` on children | `subSections` of the DTO | **inline `subSections`** (one level) |
| `SourceSectionId`/`SourceFieldId`, `IsLinkedToSource` | builder badge only | **not carried**; content is always a materialised copy |
| `ActionButtonsConfigJson` (string) | parsed object | **`actionButtonsConfigJson` string, verbatim** (like `settingsJson` in forms); omitted = `"{}"` |
| `FieldType` free string | formatting switch (case-insensitive) | verbatim string |
| `RelatedEntityTypeName` free string | Edit/View/Create target route + metadata lookup | verbatim; must be what the **buttons** need (the item's real type) |
| ids as strings, `temp-…` ids for new rows | — | **no database ids at all** |

---

## 2. The file

One configuration per file, UTF-8, 2-space indent, nulls and `false` booleans omitted. File name in the API seed
folder: `Data/DisplayConfigurationSeeds/<EntityTypeName>.display.json` (the seed `{name}` is the file name without
`.json`, e.g. `GateStructure.display`, which matches the form seed name pattern `^[A-Za-z0-9][A-Za-z0-9_.-]*$`).

```jsonc
{
  "format": "knk-displayconfig/1",
  "configurationGuid": "…",          // stable; the key for "already imported?"
  "entityTypeName": "GateStructure",  // a MetadataService entity name
  "name": "GateStructure - Default (seed v1)",
  "description": "…",                // page subtitle; max 500
  "sections": [                       // array order = SectionOrderJson
    {
      "sectionGuid": "…",
      "sectionName": "Spawn point",     // required, max 200
      "description": "…",              // shown under the heading; max 500
      "relatedEntityPropertyName": "Location",   // optional; JSON/DTO property of the root entity
      "relatedEntityTypeName": "Location",       // required iff relatedEntityPropertyName is set
      "isCollection": false,
      "isReusable": false,              // offer this section as a builder template
      "actionButtonsConfigJson": "{\"showEditButton\":true}",   // verbatim; omitted = "{}"
      "metadataCheck": "warn",          // optional: "error" (default) | "warn"; see §3 M-rules
      "displayConditionGroups": [],     // RESERVED, must be absent or empty in /1 (§2.3)
      "fields": [                       // array order = FieldOrderJson
        {
          "fieldGuid": "…",
          "fieldName": "X",             // property path; optional when templateText is set
          "relatedEntityPropertyName": null,  // optional (see §1.1 for what it is relative to)
          "relatedEntityTypeName": null,      // required iff relatedEntityPropertyName is set
          "label": "X",                 // required, max 200
          "description": "…",           // max 500
          "templateText": null,         // e.g. "${healthCurrent} / ${healthMax}"
          "fieldType": "Decimal",
          "isEditableInDisplay": false,
          "isReusable": false,
          "metadataCheck": "warn",      // optional, as on sections
          "displayConditionGroups": []  // RESERVED
        }
      ],
      "subSections": []                 // only on a collection section; see rule S5
    }
  ],
  "notes": ["…"]                       // export notes for the reviewer; ignored by the import
}
```

### 2.1 Carried / not carried

Carried: everything above. Not carried, by design:

- `Id`, `DisplayConfigurationId`, `DisplaySectionId`, `ParentSectionId`, `CreatedAt`, `UpdatedAt` — rebuilt on import.
- `IsDefault`, `IsDraft` — decided by the importer (§3 rules I2, I3), as for forms.
- `SourceSectionId`, `SourceFieldId`, `IsLinkedToSource` — ids are not portable and link mode is not resolved at read
  time anyway. The exporter writes linked sections as copies of their source's content and says so in `notes`.
- Stand-alone reusable templates (sections/fields with `IsReusable = true` that belong to no configuration) — they
  are not part of a configuration. If ever needed, they get their own format (`knk-displaysection/1`).

### 2.2 `metadataCheck`

The renderer reads the **read DTO**, the importer can only check **model metadata** (`MetadataService`). They differ
in places (DTO-only computed values like `User.titleName`; renamed navigations like `GateStructure.iconMaterialRef` for
model `IconMaterial`; nav DTOs typed differently from the model, e.g. `EnchantmentDefinition.defaultForBlueprints` is
a list of `ItemBlueprint` summaries while the model says `ItemBlueprintDefaultEnchantment`). A section or field that
deliberately follows the DTO says `"metadataCheck": "warn"`; the importer then reports metadata mismatches for that
element as warnings instead of errors. Authors should add a short reason to the element's file-level `notes` or keep
it obvious from the description. Default (absent) is `"error"`.

### 2.3 Display conditions (reserved)

Parallel to forms the shape is reserved: `displayConditionGroups` on sections and fields, same objects as
`knk-formconfig/1` (`innerLogic`, `combineWithPreviousLogic`, `isActive`, `conditions[] { sourceFieldGuid, operator,
valueJson }`), where `sourceFieldGuid` is a **display field of this file** whose resolved value is compared.
`/1` importers **reject** a non-empty list ("display conditions are not supported by the model yet"), because
`DisplayConditionGroup` can only target `FormStep`/`FormField` and its condition source is a required `FormField` FK.
Supporting them needs `TargetType` values `DisplaySection`/`DisplayField`, nullable `TargetDisplaySectionId`/
`TargetDisplayFieldId`, a display-field source (`SourceDisplayFieldGuid`) and renderer support; then bump to
`knk-displayconfig/2` or allow it in `/1` (additive).

---

## 3. Import rules

Endpoints (writes behind `knk.admin.content`, like the rest of `DisplayConfigurationsController`):

- `GET api/DisplayConfigurations/{id}/export` → the file (§4).
- `POST api/DisplayConfigurations/import?dryRun=&makeDefault=&asDraft=` → body = the file.
- `GET api/DisplayConfigurations/seeds` → bundled files in `Data/DisplayConfigurationSeeds/` with imported state.
- `POST api/DisplayConfigurations/seeds/{name}/import?dryRun=&makeDefault=&asDraft=`.
- Result DTO and HTTP statuses exactly as for forms (`FormConfigurationImportResultDto` shape, with
  `sectionCount`/`fieldCount` instead of step/field/rule counts; `displayConfigurationId` instead of
  `formConfigurationId`): dry run passed → 200, imported → 201, already imported → 409, invalid → 400.

Importer rules (I = identity/default, S = structure, E = elements, M = metadata):

- **I1 Never overwrite.** If a `DisplayConfiguration` with the same `configurationGuid` exists, return
  `AlreadyImported` with its id and stop. A changed seed gets a new `configurationGuid` and name ("… (seed v2)").
  Section/field GUIDs are not globally unique keys in the database (no unique index); duplicates *inside the file*
  are an error, duplicates with other configurations are allowed.
- **I2 Default only when none or asked.** `IsDefault = true` when the entity has no default display configuration,
  or `makeDefault=true`; then the previous default is unset in the same transaction (`replacedDefaultId/Name` in the
  result). This replaces the 409 that `CreateAsync` throws today.
- **I3 Published unless asked.** The import writes `IsDraft = false` (a seed passed the dry run and is finished),
  unless `asDraft=true`. Note the page only shows a default *and* non-draft config, so a draft import is invisible on
  `/display`. Write through `KnKDbContext` directly (as the form importer does); `DisplayConfigurationService.CreateAsync`
  forces `IsDraft = true` and refuses a second default.
- **I4 Dry run** (`dryRun=true`) runs every check below and returns errors/warnings without writing. The import
  writes nothing unless there are no errors.
- **S1** `format == "knk-displayconfig/1"`; `configurationGuid` a non-empty GUID; `entityTypeName` and `name`
  required; at least one section.
- **S2** Every `sectionGuid`/`fieldGuid` must be a GUID and unique in the file; a missing one is generated with a
  warning (nothing can refer to it). `SectionOrderJson` = top-level `sectionGuid`s in array order; every section's
  `FieldOrderJson` (sub-sections included) = its `fieldGuid`s in array order.
- **S3** `sectionName` and field `label` required (≤ 200), descriptions ≤ 500, `relatedEntity*` ≤ 100 (section) /
  200 (field), `fieldType` ≤ 50 (model `MaxLength`s); configuration description ≤ 500.
- **S4** Top-level sections get `DisplayConfigurationId`; sub-sections get `DisplayConfigurationId = NULL` and their
  parent through the `SubSections` navigation (EF sets `ParentSectionId`), exactly like the builder produces.
- **S5** `subSections` only on a section with `isCollection: true` (error otherwise — nothing renders them), at most
  one level deep (error: the repository does not load deeper levels). More than one sub-section, or a collection
  section that has both `fields` and `subSections`, is a warning: only the first sub-section is used, and only when the
  section has no fields. Seeds should put the item template as the collection section's own `fields`.
- **E1** A field needs `fieldName` or `templateText` (error).
- **E2** `relatedEntityTypeName` requires `relatedEntityPropertyName` and vice versa, on sections and fields
  (error; model invariant "must be NULL if RelatedEntityPropertyName is NULL").
- **E3** `actionButtonsConfigJson` must parse as a JSON object; known keys are the seven `show…Button` booleans;
  an unknown key or a non-boolean value is a warning; it is stored verbatim. Buttons on a section without
  `relatedEntityTypeName` are a warning (never rendered). `showSelectButton/showUnlinkButton/showAddButton/
  showRemoveButton` are a warning while KNG-148 is open (hidden).
- **E4** `templateText`: every `${` must close; each placeholder is either a path (`[A-Za-z_][A-Za-z0-9_]*(\.…)*`,
  optional trailing `.Count`) or an arithmetic expression of paths/numbers joined by `+ - * /`. Syntax errors are
  errors. A path segment starting with an upper-case letter (other than `Count`) is a warning: template paths are
  case-sensitive camelCase JSON names.
- **E5** `fieldType`, when set, should be one of `String, Integer, Number, Boolean, DateTime, Date, Decimal, Enum,
  Object, List` (warning otherwise). `Object`/`List` on a field that resolves to an object/array is a warning
  ("renders as [object Object]").
- **E6** `isEditableInDisplay: true` is an **error** unless the field is in a top-level non-related, non-collection
  section, has a `fieldName` without dots, no `relatedEntityPropertyName`, no `templateText`, and `fieldType` is
  `String`, `Integer`, `Number` or `Boolean`. A warning when metadata says the property is computed (get-only) —
  the save would be ignored. (Rule E6 cannot know which properties the entity's update endpoint ignores; see the
  seed notes for known cases, e.g. `User.Coins`.)
- **M1** `entityTypeName` must be a `MetadataService` entity (error).
- **M2** Section `relatedEntityPropertyName`: a metadata field of the root entity with that name (case-insensitive),
  `IsRelatedEntity`, and `RelatedEntityType == relatedEntityTypeName`; `isCollection` must equal
  `FieldType == "List"`. Each mismatch: error, or warning under `metadataCheck: "warn"`.
- **M3** Field `fieldName`/`relatedEntityPropertyName` are checked against the entity they resolve on (§1.1): the root
  entity, the section's related type, or the collection's item type; the field's `relatedEntityTypeName` must equal the
  metadata `RelatedEntityType`; dotted paths are followed through `RelatedEntityType`s. Unknown names or types: error,
  or warning under `metadataCheck: "warn"`. When a related type has no metadata (not `[FormConfigurableEntity]`), the
  check stops with a warning.
- **M4** `fieldType` vs metadata (`mapFieldType` of the metadata type): a disagreement is a warning (types are hints).
- **M5** Template paths are checked like M3 where possible (case-insensitive match against metadata); unknown is a
  warning, never an error (templates usually read DTO-shaped data).
- **M6** Metadata drift fails loudly: a property that no longer exists on the model and is not marked
  `metadataCheck: "warn"` is an error, so a stale seed cannot be imported silently.

What the importer can **not** check, and the seed notes must: whether the read DTO really carries a property (the API
has no DTO metadata); whether the web app has a fetch-by-id for `entityTypeName`/button targets
(`entityApiMapping.ts`); whether button targets have a published default display config (View) or a form (Edit);
whether the entity's update endpoint accepts a hot-edited property.

---

## 4. Export rules (`GET {id}/export`)

The seeds in this folder were produced by a one-off converter over the dev DB dump that follows these rules; the C#
exporter should produce the same shape.

1. Top-level sections = sections with `DisplayConfigurationId == id`, in `SectionOrderJson` order. If the list is
   non-empty, sections not in it are left out with a note (they are invisible today); GUIDs in the list without a
   section are noted and ignored. If the list is empty or invalid, use `Id` order.
2. Fields per section in `FieldOrderJson` order, same rules.
3. Sub-sections (by `ParentSectionId`, `Id` order) inline under `subSections`, one level; deeper levels are left out
   with a note.
4. Sections/fields with `SourceSectionId`/`SourceFieldId` are exported as plain copies with a note; a linked section
   (`IsLinkedToSource`) is exported with its **source's** fields, name and settings, with a note.
5. `ActionButtonsConfigJson` verbatim, `"{}"` omitted. Empty strings → omitted. `false`/null omitted.
6. `notes[0]` states the source configuration id and its draft/default state (not carried).
7. GUIDs are lower-case strings.

---

## 5. Seed authoring conventions (same as form seeds §4, adapted)

- Seed `name`: `"<Entity> - Default (seed v1)"`; `configurationGuid = uuid5(UUID(<old configurationGuid>), "seed-v1")`.
- GUIDs of sections/fields kept from the old configuration: `uuid5(UUID(<old configurationGuid>), <old guid>)`, even
  when renamed or moved. New ones: `uuid5(UUID(<new configurationGuid>), "section:" + sectionName)` and
  `uuid5(UUID(<new configurationGuid>), "field:" + key)`, where key is the property path written as
  `<relatedEntityPropertyName>.<fieldName>` (or `fieldName`), or the label for a template-only field; when two new
  fields in one file would share a key, prefix the section name (`"<sectionName>/<key>"`).
- Section name: 1-4 words, sentence case, noun phrase. Section description: what the section shows and, for related
  or collection sections, what the buttons do or where it is changed.
- Field label in admin terms (sentence case, no "Id"); description = the concept (why/effect, units, what empty
  means), not the label again. Booleans get descriptions too.
- No database ids on the page (`Id`, `…Id` FKs): show the related object's name instead.
- Runtime state may be shown (unlike forms) but its description says who sets it and that it is read-only.
- Hot edit only where the update endpoint really stores the value (see E6) and the value has no side effects that
  need a dedicated endpoint.
- Buttons only where they work: the related items have an `id`, the target type has a form (Edit) or a published
  default display config and a fetch-by-id (View). No Create on collection sections (no parent context).

---

## 6. Implementation notes (2026-10-19)

What the importer (`DisplayConfigurationPortabilityService`) settled where §3/§4 left room:

- **Result.** `DisplayConfigurationImportResultDto` uses the form statuses (`FormConfigurationImportStatus`) and adds
  `isDraft` (what the import wrote, or would write). Export with `notes[0]` = source id and draft/default state.
- **Warnings that would have been errors** under `metadataCheck: "warn"` end in ` (metadataCheck "warn")`
  (`WarnMarker`). `DisplayConfigurationSeedFilesTests` allows only those on the bundled seeds: today exactly the 11
  of the authoring notes, 0 errors.
- **Where checks resolve.** Every element checks its own `relatedEntity*` and then goes on with the *declared*
  `relatedEntityTypeName`, so a DTO-only navigation marked "warn" still has its fields checked against the declared
  type. A sub-section's own `relatedEntity*` is ignored by the renderer (warning); its fields resolve on the
  collection's item type.
- **M2/M3 additions (same severity rules):** a `…Id` foreign key used as a navigation (the old EnchantmentDefinition
  bug), a list used as a field's `relatedEntityPropertyName`, and a dotted path through a list or a scalar.
- **M4** compares how the page formats the value: number (`Integer/Number/Decimal`), boolean, date
  (`DateTime/Date`) and text (`String/Enum`). `String` for an enum property is therefore not a mismatch.
- **E5** "[object Object]" warns whenever the property resolves to a navigation object or a list, whatever the
  `fieldType`.
- **E6**'s "computed" warning reads the model property's setter (no public setter = computed).
- Also warned: a top-level collection without `relatedEntityPropertyName`; `${x.Count}` where `x` is not a list.
- **Not added:** a unique index on `DisplayConfiguration.ConfigurationGuid` (forms have one). I1 checks before
  writing; two simultaneous imports of the same file could still both get in. Needs a migration if wanted.
- The entity name is stored with the metadata's spelling, whatever casing the file used.
