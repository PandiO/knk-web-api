using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Stock;

namespace knkwebapi_v2.Attributes
{
    /// <summary>
    /// The label and detail line per dashboard type for <c>api/Lookup</c> (knk-workspace
    /// docs/specs/dashboard-access-model/DESIGN.md § 4.6.3, PandiO/knk-workspace#280 AM2). Labels and details are read
    /// by property name from whatever the type's search or get returns (list and detail DTOs share the names, the case
    /// differs), so one entry serves both. Details are short and non-sensitive; users get their username only.
    /// Types left out (link rows, snapshots, other view-only entries) have no lookup.
    /// </summary>
    internal static class DashboardLookups
    {
        private static readonly IReadOnlyDictionary<string, DashboardLookup> ByType = new Dictionary<string, DashboardLookup>(StringComparer.Ordinal)
        {
            // Content.
            ["Category"] = Of<ICategoryService>((s, id) => s.GetByIdAsync(id), Name, o => Text(o, "ParentCategoryName", "ParentCategory"),
                filters: new[] { IdFilter("parentCategoryId") }),
            ["Tag"] = Of<ITagService>((s, id) => s.GetByIdAsync(id), Name),
            ["Grade"] = Of<IGradeService>((s, id) => s.GetByIdAsync(id), Name, o => Text(o, "Stars") is { } stars ? $"{stars} stars" : null),
            ["ItemBlueprint"] = Of<IItemBlueprintService>((s, id) => s.GetByIdAsync(id), o => Text(o, "Name", "DefaultDisplayName"),
                o => Join(Text(o, "CategoryName", "Category"), Text(o, "GradeName", "Grade")), filters: new[] { IdFilter("CategoryId") }),
            ["EnchantmentDefinition"] = Of<IEnchantmentDefinitionService>((s, id) => s.GetByIdAsync(id), o => Text(o, "DisplayName", "Key"),
                o => Text(o, "Key")),
            ["BannerDesign"] = Of<IBannerDesignService>((s, id) => s.GetByIdAsync(id), Name, o => Text(o, "BaseColor")),
            ["BannerLayer"] = Of<IBannerDesignService>((s, id) => s.GetLayerByIdAsync(id), o => Text(o, "PatternKey"), o => Text(o, "Color")),
            ["Clan"] = Of<IClanService>((s, id) => s.GetByIdAsync(id), Name, o => Text(o, "DefaultForTownName"),
                filters: new[] { PreferFilter("preferTownId") }),
            ["MinecraftBlockRef"] = Of<IMinecraftBlockRefService>((s, id) => s.GetByIdAsync(id), o => Text(o, "DisplayName", "NamespaceKey"),
                o => Text(o, "NamespaceKey")),
            ["MinecraftMaterialRef"] = Of<IMinecraftMaterialRefService>((s, id) => s.GetByIdAsync(id), o => Text(o, "DisplayName", "NamespaceKey"),
                o => Text(o, "NamespaceKey")),
            ["MinecraftEnchantmentRef"] = Of<IMinecraftEnchantmentRefService>((s, id) => s.GetByIdAsync(id),
                o => Text(o, "DisplayName", "NamespaceKey"), o => Text(o, "NamespaceKey")),

            // World.
            ["Domain"] = Of<IDomainService>((s, id) => s.GetByIdAsync(id), Name,
                o => Join(Text(o, "ParentDomainName", "ParentDomain"), Text(o, "WorldName"))),
            ["Town"] = Of<ITownService>((s, id) => s.GetByIdAsync(id), Name, o => Text(o, "WorldName")),
            ["District"] = Of<IDistrictService>((s, id) => s.GetByIdAsync(id), Name, o => Text(o, "TownName", "Town"),
                filters: new[] { IdFilter("townId") }),
            ["Structure"] = Of<IStructureService>((s, id) => s.GetByIdAsync(id), Name,
                o => Join(Text(o, "StreetName", "Street"), Text(o, "HouseNumber"), Text(o, "DistrictName", "District"), Text(o, "Town")),
                filters: new[] { IdFilter("streetId"), IdFilter("districtId"), IdFilter("townId") }),
            // Production world P2 (#91).
            ["Warehouse"] = Of<knkwebapi_v2.Services.Production.IWarehouseService>((s, id) => s.GetByIdAsync(id), Name,
                o => Join(Text(o, "District"), Text(o, "Town")),
                filters: new[] { IdFilter("districtId"), IdFilter("townId"), FlagFilter("acceptsDeliveries") }),
            ["ProductionStructure"] = Of<knkwebapi_v2.Services.Production.IProductionStructureService>((s, id) => s.GetByIdAsync(id), Name,
                o => Join(Text(o, "District"), Text(o, "Town")),
                filters: new[] { IdFilter("districtId"), IdFilter("townId"), IdFilter("productionKindId") }),
            // Production engine P3 (#86): pickers for the structure form, lines, modifiers and zones.
            ["ProductionKind"] = Of<knkwebapi_v2.Services.Production.IProductionKindService>((s, id) => s.GetByIdAsync(id), Name,
                o => Join(Text(o, "AreaModel"), Text(o, "ResourceCategory"))),
            ["ProductionKindLevel"] = Of<knkwebapi_v2.Services.Production.IProductionKindLevelService>((s, id) => s.GetByIdAsync(id),
                o => Join(Text(o, "ProductionKind", "ProductionKindName"), "level " + Text(o, "Level")), o => Text(o, "YieldMultiplier"),
                filters: new[] { IdFilter("productionKindId") }),
            ["ProductionLine"] = Of<knkwebapi_v2.Services.Production.IProductionLineService>((s, id) => s.GetByIdAsync(id),
                o => Text(o, "Name") ?? Join(Text(o, "ProductionStructure"), Text(o, "ItemBlueprint")), o => Text(o, "Status"),
                filters: new[] { IdFilter("productionStructureId"), IdFilter("itemBlueprintId") }),
            ["ProductionModifier"] = Of<knkwebapi_v2.Services.Production.IProductionModifierService>((s, id) => s.GetByIdAsync(id), Name,
                o => Join(Text(o, "Scope"), Text(o, "Effect"), Text(o, "Value"))),
            ["ClimateZone"] = Of<knkwebapi_v2.Services.Production.IClimateZoneService>((s, id) => s.GetByIdAsync(id), Name),
            ["WeatherType"] = Of<knkwebapi_v2.Services.Production.IWeatherTypeService>((s, id) => s.GetByIdAsync(id), Name),
            // Yield areas P4 (#138): areas and Hybrid overrides per structure.
            ["ProductionArea"] = Of<knkwebapi_v2.Services.Production.IProductionAreaService>((s, id) => s.GetByIdAsync(id), Name,
                o => Join(Text(o, "ProductionStructure"), Text(o, "Shape")), filters: new[] { IdFilter("productionStructureId") }),
            ["ProductionYieldOverride"] = Of<knkwebapi_v2.Services.Production.IProductionYieldOverrideService>((s, id) => s.GetByIdAsync(id),
                o => Text(o, "Name") ?? Join(Text(o, "Kind"), Text(o, "ItemBlueprint")), o => Text(o, "Value"),
                filters: new[] { IdFilter("productionStructureId"), IdFilter("itemBlueprintId") }),
            // #838: cycle records have their own dashboard list.
            ["ProductionCycleRecord"] = Of<knkwebapi_v2.Services.Production.IProductionCycleRecordService>((s, id) => s.GetByIdAsync(id),
                o => Join(Text(o, "ItemBlueprintName"), Text(o, "SettledAt")), o => Text(o, "StatusAfter"),
                filters: new[] { IdFilter("productionStructureId"), IdFilter("productionLineId") }),
            // Transport core P5 (#79): orders (labels for events and links) and lanes.
            ["TransportOrder"] = Of<knkwebapi_v2.Services.Production.ITransportService>((s, id) => s.GetByIdAsync(id),
                o => "Transport #" + Text(o, "Id"), o => Join(Text(o, "Origin"), Text(o, "Status")),
                filters: new[] { IdFilter("sourceStructureId"), IdFilter("structureId") }),
            ["TransportLane"] = Of<knkwebapi_v2.Services.Production.ITransportLaneService>((s, id) => s.GetByIdAsync(id),
                o => Join(Text(o, "FromStructure"), Text(o, "ToStructure")), o => Text(o, "RoadDistance"),
                filters: new[] { IdFilter("structureId") }),
            // Terminals and menus P6a (#683): containers per storage.
            ["StorageContainer"] = Of<knkwebapi_v2.Services.Stock.IStorageContainerService>((s, id) => s.GetByIdAsync(id),
                o => Text(o, "Label") ?? Join(Text(o, "Mode"), Text(o, "Storage")), o => Join(Text(o, "WorldName"), Text(o, "X"), Text(o, "Y"), Text(o, "Z")),
                filters: new[] { IdFilter("storageId") }),
            ["Kingdom"] = Of<IKingdomService>((s, id) => s.GetByIdAsync(id), Name),
            ["Province"] = Of<IProvinceService>((s, id) => s.GetByIdAsync(id), Name, o => Text(o, "Kingdom"),
                filters: new[] { IdFilter("kingdomId") }),
            ["CrownLand"] = Of<ICrownLandService>((s, id) => s.GetByIdAsync(id), Name,
                o => Join(Text(o, "Kingdom"), Text(o, "WorldName")), filters: new[] { IdFilter("kingdomId") }),
            ["TerritoryConflict"] = Of<ITerritoryConflictService>((s, id) => s.GetByIdAsync(id),
                o => Join(Text(o, "HolderA"), Text(o, "HolderB")), o => Text(o, "World")),
            // Stock core P1 (#97): pickers for the move and adjust dialogs.
            ["Storage"] = Of<IStorageService>((s, id) => s.GetByIdAsync(id), Name, o => Join(Text(o, "Kind"), Text(o, "HolderName")),
                filters: new[] { IdFilter("domainId"), IdFilter("userId"), IdFilter("clanId") }),
            ["Street"] = Of<IStreetService>((s, id) => s.GetByIdAsync(id), Name, o => Text(o, "TownNames", "DistrictNames", "Districts")),
            ["Location"] = Of<ILocationService>((s, id) => s.GetByIdAsync(id), o => Text(o, "Name") ?? Coordinates(o), Coordinates),
            ["GateStructure"] = Of<IGateStructureService>((s, id) => s.GetByIdAsync(id), Name,
                o => Join(Text(o, "DistrictName", "District"), Text(o, "TownName")),
                filters: new[]
                {
                    IdFilter("streetId"), IdFilter("districtId"), IdFilter("townId"), IdFilter("siegeScenarioId"),
                    FlagFilter("isActive"), FlagFilter("isOpened"),
                }),
            ["GateDoor"] = Of<IGateDoorService>((s, id) => s.GetByIdAsync(id), Name, o => Text(o, "GateStructureName"),
                filters: new[] { IdFilter("gateStructureId"), FlagFilter("isActive") }),
            ["Landmark"] = Of<ILandmarkService>((s, id) => s.GetByIdAsync(id), Name,
                o => Join(Text(o, "DomainName"), Text(o, "World")), filters: new[] { IdFilter("domainId") }),
            ["DomainSubRegion"] = Of<IDomainSubRegionService>((s, id) => s.GetByIdAsync(id), o => Text(o, "Name", "WgRegionId"),
                o => Text(o, "WgRegionId")),

            // Kits, lootboxes, siege, roads.
            ["Kit"] = Of<IKitService>((s, id) => s.GetByIdAsync(id), Name),
            ["LootboxType"] = Of<ILootboxTypeService>((s, id) => s.GetByIdAsync(id), Name, o => Text(o, "Category")),
            ["LootboxSpecialEntry"] = Of<ILootboxSpecialEntryService>((s, id) => s.GetByIdAsync(id), o => Text(o, "ItemBlueprint"),
                o => Text(o, "LootboxType")),
            ["LootboxSpawnArea"] = Of<ILootboxSpawnAreaService>((s, id) => s.GetByIdAsync(id), Name, o => Text(o, "World")),
            ["SiegeScenario"] = Of<ISiegeScenarioService>((s, id) => s.GetByIdAsync(id), Name, o => Text(o, "TownName"),
                filters: new[] { IdFilter("townId") }),
            ["SiegeTeam"] = Of<ISiegeScenarioService>((s, id) => s.GetTeamByIdAsync(id), o => Text(o, "ResolvedName", "Name", "ClanName"),
                o => Text(o, "Role"), filters: new[] { IdFilter("siegeScenarioId") }),
            ["SiegeLobby"] = Of<ISiegeLobbyService>((s, id) => s.GetByIdAsync(id), Name, o => Text(o, "Key")),
            ["SiegeObjective"] = Of<ISiegeScenarioService>((s, id) => s.GetObjectiveByIdAsync(id), Name, o => Text(o, "GateStructureName")),
            ["SiegeSpawnpoint"] = Of<ISiegeScenarioService>((s, id) => s.GetSpawnpointByIdAsync(id), Name),
            ["SiegeWeaponTemplate"] = Of<ISiegeWeaponService>((s, id) => s.GetTemplateByIdAsync(id), Name, o => Text(o, "Kind")),
            ["SiegeAmmoType"] = Of<ISiegeWeaponService>((s, id) => s.GetAmmoTypeByIdAsync(id), Name, o => Text(o, "Effect")),
            ["SiegeWeaponSpot"] = Of<ISiegeWeaponService>((s, id) => s.GetSpotByIdAsync(id), Name, o => Text(o, "SiegeTeamName")),
            ["UserSiegeWeaponUnlock"] = Of<ISiegeWeaponService>((s, id) => s.GetUnlockByIdAsync(id),
                o => Text(o, "SiegeWeaponTemplateName"), o => Text(o, "Username")),
            // Siege shop catalogue (PandiO/knk-workspace#867).
            ["SiegeShopCategory"] = Of<ISiegeShopService>((s, id) => s.GetCategoryByIdAsync(id), Name, o => Text(o, "Key")),
            ["SiegeShopEntry"] = Of<ISiegeShopService>((s, id) => s.GetEntryByIdAsync(id),
                o => Text(o, "Name", "DisplayName", "ItemBlueprintName", "SiegeWeaponTemplateName"), o => Text(o, "GameMode"),
                filters: new[] { IdFilter("siegeScenarioId"), IdFilter("categoryId"), FlagFilter("defaultsOnly") }),
            ["SiegeScenarioShopOverride"] = Of<ISiegeShopService>((s, id) => s.GetOverrideByIdAsync(id),
                o => Text(o, "SiegeShopEntryName"), o => Text(o, "Hidden")),
            ["RoadProfile"] = Of<IRoadNetworkService>((s, id) => s.GetProfileAsync(id), Name, o => Text(o, "RoadClass")),

            // Players: the username and nothing else; the search matches the username only (never the email).
            ["User"] = Of<IUserService>((s, id) => s.GetByIdAsync(id), o => Text(o, "Username"), null, UsernameOnly),
            ["PermissionGroup"] = Of<IPermissionGroupService>((s, id) => s.GetByIdAsync(id), Name,
                o => Text(o, "Weight") is { } weight ? $"weight {weight}" : null,
                filters: new[] { IdFilter("parentGroupId"), FlagFilter("isPremiumTier") }),
            ["PermissionHolder"] = Of<IPermissionHolderService>((s, id) => s.GetByIdAsync(id), Name, o => Text(o, "HolderType")),
        };

        public static DashboardLookup? For(string typeName) => ByType.GetValueOrDefault(typeName);

        /// <summary>The types with a lookup, for the registry test.</summary>
        public static IEnumerable<string> Types => ByType.Keys;

        private static DashboardLookup Of<TService>(Func<TService, int, Task> get, Func<object, string?> label,
            Func<object, string?>? detail = null, Func<string?, PagedQueryDto, PagedQueryDto>? query = null,
            IReadOnlyList<LookupFilter>? filters = null) where TService : notnull =>
            new(async (services, id) =>
            {
                var task = get(services.GetRequiredService<TService>(), id);
                await task;
                return task.GetType().GetProperty(nameof(Task<object>.Result))?.GetValue(task);
            }, label, detail, query, filters);

        // Lookup filters (PandiO/knk-workspace#288): only keys the type's own search reads, spelled as it reads them
        // (its repository's Filters lookups), and only ones that narrow by a parent or a flag. Never a key that matches
        // on something a label doesn't show (a user's email or UUID).
        private static LookupFilter IdFilter(string key) => new(key, LookupFilterKind.Id);

        private static LookupFilter FlagFilter(string key) => new(key, LookupFilterKind.Flag);

        private static LookupFilter PreferFilter(string key) => new(key, LookupFilterKind.Prefer);

        private static string? Name(object row) => Text(row, "Name");

        private static PagedQueryDto UsernameOnly(string? term, PagedQueryDto query)
        {
            query.SearchTerm = null;
            query.Filters = string.IsNullOrWhiteSpace(term) ? null : new Dictionary<string, string> { ["username"] = term.Trim() };
            return query;
        }

        private static string? Coordinates(object row)
        {
            var world = Text(row, "World");
            var x = Number(row, "X");
            var y = Number(row, "Y");
            var z = Number(row, "Z");
            var at = x != null && y != null && z != null ? $"{x}, {y}, {z}" : null;
            return Join(world, at);
        }

        // ===== Reading a row by property name =====

        private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> Properties = new();

        private static readonly string[] NestedLabels = { "Name", "DisplayName", "Username", "Key" };

        private static PropertyInfo? Property(Type type, string name) =>
            Properties.GetOrAdd((type, name), key => key.Item1.GetProperty(key.Item2,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase));

        /// <summary>The row's id (<c>Id</c> or <c>id</c>), or null.</summary>
        public static int? Id(object row) => Property(row.GetType(), "Id")?.GetValue(row) switch
        {
            int id => id,
            long id when id is > 0 and <= int.MaxValue => (int)id,
            string id when int.TryParse(id, out var parsed) => parsed,
            _ => null
        };

        /// <summary>The first of <paramref name="names"/> with a value, as one short line.</summary>
        public static string? Text(object row, params string[] names)
        {
            foreach (var name in names)
            {
                var value = Property(row.GetType(), name)?.GetValue(row);
                if (Format(value, nested: true) is { } text) return text;
            }
            return null;
        }

        private static string? Format(object? value, bool nested)
        {
            switch (value)
            {
                case null:
                    return null;
                case string s:
                    return string.IsNullOrWhiteSpace(s) ? null : Shorten(s.Trim());
                case bool or Enum:
                    return value.ToString();
                case IFormattable formattable:
                    return formattable.ToString(null, CultureInfo.InvariantCulture);
                case IEnumerable items when nested:
                    var parts = items.Cast<object?>().Select(i => Format(i, nested: false)).Where(t => t != null).Take(3).ToList();
                    return parts.Count == 0 ? null : string.Join(", ", parts);
                default:
                    // A related object (a category, a parent domain): its name.
                    return NestedLabel(value);
            }
        }

        private static string? NestedLabel(object value)
        {
            foreach (var name in NestedLabels)
            {
                if (Property(value.GetType(), name)?.GetValue(value) is string s && !string.IsNullOrWhiteSpace(s)) return Shorten(s.Trim());
            }
            return null;
        }

        private static string? Number(object row, string name) => Property(row.GetType(), name)?.GetValue(row) switch
        {
            double d => Math.Round(d).ToString(CultureInfo.InvariantCulture),
            float f => Math.Round(f).ToString(CultureInfo.InvariantCulture),
            decimal m => Math.Round(m).ToString(CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            _ => null
        };

        private static string? Join(params string?[] parts)
        {
            var present = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            return present.Count == 0 ? null : string.Join(" · ", present);
        }

        private const int MaxLength = 120;

        private static string Shorten(string text) => text.Length <= MaxLength ? text : text[..(MaxLength - 1)] + "…";
    }
}
