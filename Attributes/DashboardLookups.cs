using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

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
            ["Category"] = Of<ICategoryService>((s, id) => s.GetByIdAsync(id), Name, o => Text(o, "ParentCategoryName", "ParentCategory")),
            ["Tag"] = Of<ITagService>((s, id) => s.GetByIdAsync(id), Name),
            ["Grade"] = Of<IGradeService>((s, id) => s.GetByIdAsync(id), Name, o => Text(o, "Stars") is { } stars ? $"{stars} stars" : null),
            ["ItemBlueprint"] = Of<IItemBlueprintService>((s, id) => s.GetByIdAsync(id), o => Text(o, "Name", "DefaultDisplayName"),
                o => Join(Text(o, "CategoryName", "Category"), Text(o, "GradeName", "Grade"))),
            ["EnchantmentDefinition"] = Of<IEnchantmentDefinitionService>((s, id) => s.GetByIdAsync(id), o => Text(o, "DisplayName", "Key"),
                o => Text(o, "Key")),
            ["BannerDesign"] = Of<IBannerDesignService>((s, id) => s.GetByIdAsync(id), Name, o => Text(o, "BaseColor")),
            ["BannerLayer"] = Of<IBannerDesignService>((s, id) => s.GetLayerByIdAsync(id), o => Text(o, "PatternKey"), o => Text(o, "Color")),
            ["Clan"] = Of<IClanService>((s, id) => s.GetByIdAsync(id), Name, o => Text(o, "DefaultForTownName")),
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
            ["District"] = Of<IDistrictService>((s, id) => s.GetByIdAsync(id), Name, o => Text(o, "TownName", "Town")),
            ["Structure"] = Of<IStructureService>((s, id) => s.GetByIdAsync(id), Name,
                o => Join(Text(o, "StreetName", "Street"), Text(o, "HouseNumber"), Text(o, "DistrictName", "District"))),
            ["Street"] = Of<IStreetService>((s, id) => s.GetByIdAsync(id), Name, o => Text(o, "TownNames", "DistrictNames", "Districts")),
            ["Location"] = Of<ILocationService>((s, id) => s.GetByIdAsync(id), o => Text(o, "Name") ?? Coordinates(o), Coordinates),
            ["GateStructure"] = Of<IGateStructureService>((s, id) => s.GetByIdAsync(id), Name,
                o => Join(Text(o, "DistrictName", "District"), Text(o, "TownName"))),
            ["GateDoor"] = Of<IGateDoorService>((s, id) => s.GetByIdAsync(id), Name, o => Text(o, "GateStructureName")),
            ["DomainSubRegion"] = Of<IDomainSubRegionService>((s, id) => s.GetByIdAsync(id), o => Text(o, "Name", "WgRegionId"),
                o => Text(o, "WgRegionId")),

            // Kits, lootboxes, siege, roads.
            ["Kit"] = Of<IKitService>((s, id) => s.GetByIdAsync(id), Name),
            ["LootboxType"] = Of<ILootboxTypeService>((s, id) => s.GetByIdAsync(id), Name, o => Text(o, "Category")),
            ["LootboxSpecialEntry"] = Of<ILootboxSpecialEntryService>((s, id) => s.GetByIdAsync(id), o => Text(o, "ItemBlueprint"),
                o => Text(o, "LootboxType")),
            ["LootboxSpawnArea"] = Of<ILootboxSpawnAreaService>((s, id) => s.GetByIdAsync(id), Name, o => Text(o, "World")),
            ["SiegeScenario"] = Of<ISiegeScenarioService>((s, id) => s.GetByIdAsync(id), Name, o => Text(o, "TownName")),
            ["SiegeTeam"] = Of<ISiegeScenarioService>((s, id) => s.GetTeamByIdAsync(id), o => Text(o, "ResolvedName", "Name", "ClanName"),
                o => Text(o, "Role")),
            ["SiegeLobby"] = Of<ISiegeLobbyService>((s, id) => s.GetByIdAsync(id), Name, o => Text(o, "Key")),
            ["SiegeObjective"] = Of<ISiegeScenarioService>((s, id) => s.GetObjectiveByIdAsync(id), Name, o => Text(o, "GateStructureName")),
            ["SiegeSpawnpoint"] = Of<ISiegeScenarioService>((s, id) => s.GetSpawnpointByIdAsync(id), Name),
            ["RoadProfile"] = Of<IRoadNetworkService>((s, id) => s.GetProfileAsync(id), Name, o => Text(o, "RoadClass")),

            // Players: the username and nothing else; the search matches the username only (never the email).
            ["User"] = Of<IUserService>((s, id) => s.GetByIdAsync(id), o => Text(o, "Username"), null, UsernameOnly),
            ["PermissionGroup"] = Of<IPermissionGroupService>((s, id) => s.GetByIdAsync(id), Name,
                o => Text(o, "Weight") is { } weight ? $"weight {weight}" : null),
            ["PermissionHolder"] = Of<IPermissionHolderService>((s, id) => s.GetByIdAsync(id), Name, o => Text(o, "HolderType")),
        };

        public static DashboardLookup? For(string typeName) => ByType.GetValueOrDefault(typeName);

        /// <summary>The types with a lookup, for the registry test.</summary>
        public static IEnumerable<string> Types => ByType.Keys;

        private static DashboardLookup Of<TService>(Func<TService, int, Task> get, Func<object, string?> label,
            Func<object, string?>? detail = null, Func<string?, PagedQueryDto, PagedQueryDto>? query = null) where TService : notnull =>
            new(async (services, id) =>
            {
                var task = get(services.GetRequiredService<TService>(), id);
                await task;
                return task.GetType().GetProperty(nameof(Task<object>.Result))?.GetValue(task);
            }, label, detail, query);

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
