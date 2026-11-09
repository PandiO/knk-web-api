using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Attributes
{
    /// <summary>The dashboard areas of the access model (knk-workspace docs/specs/dashboard-access-model/DESIGN.md § 4.2).</summary>
    public enum DashboardArea
    {
        Content,
        World,
        Kits,
        Lootboxes,
        Siege,
        Roads,
        Players,
    }

    /// <summary>One page of a type's own search, as the dashboard table shows it.</summary>
    public sealed record DashboardPage(IReadOnlyList<object> Items, int Total);

    /// <summary>
    /// One dashboard type in <see cref="DashboardAccess"/>: its area and nodes, what may be done with it, and the
    /// delegates to the type's own services that the dashboard endpoints use.
    /// </summary>
    public sealed class DashboardTypeAccess
    {
        internal DashboardTypeAccess(string typeName, DashboardArea area, string areaNode, string writeNode,
            IReadOnlyList<string> readNodes, Func<IServiceProvider, int, Task>? delete, bool bulkDeletable,
            Func<IServiceProvider, PagedQueryDto, Task<DashboardPage>>? search, DashboardLookup? lookup = null)
        {
            Lookup = lookup;
            TypeName = typeName;
            Area = area;
            AreaNode = areaNode;
            WriteNode = writeNode;
            ReadNodes = readNodes;
            Delete = delete;
            BulkDeletable = delete != null && bulkDeletable;
            Search = search;
        }

        /// <summary>The CLR type name, as the metadata and the realtime events name it (<c>ItemBlueprint</c>).</summary>
        public string TypeName { get; }

        public DashboardArea Area { get; }

        /// <summary>The area node: view, create and edit, and the realtime audience.</summary>
        public string AreaNode { get; }

        /// <summary>The node create and edit need: the area node, except permission groups (<c>knk.admin.user.perm</c>).</summary>
        public string WriteNode { get; }

        /// <summary>Who may read the type in full: the area node first, then the cross-area page nodes (§ 4.6.2; enforced from AM2).</summary>
        public IReadOnlyList<string> ReadNodes { get; }

        /// <summary>The type's own delete (the same service call as its <c>DELETE {id}</c>); null = view-only.</summary>
        public Func<IServiceProvider, int, Task>? Delete { get; }

        public bool Deletable => Delete != null;

        /// <summary>Allowed in <c>POST api/Dashboard/bulk-delete</c> (every deletable type but users and permission groups).</summary>
        public bool BulkDeletable { get; }

        /// <summary>The type's own search (the same service call as its <c>POST …/search</c>); null = no list to export.</summary>
        public Func<IServiceProvider, PagedQueryDto, Task<DashboardPage>>? Search { get; }

        public bool Exportable => Search != null;

        /// <summary>How <c>api/Lookup</c> labels the type (AM2, DESIGN.md § 4.6.3); null = no lookup.</summary>
        public DashboardLookup? Lookup { get; }

        /// <summary><c>POST api/Lookup/{type}/search</c> works: a search and a label.</summary>
        public bool LookupSearchable => Search != null && Lookup != null;

        /// <summary><c>GET api/Lookup/{type}/{id}</c> works: a get by id and a label.</summary>
        public bool LookupGettable => Lookup?.Get != null;
    }

    /// <summary>
    /// The lookup side of a registry entry (DESIGN.md § 4.6.3): a get by id through the type's own service, a label
    /// and one short detail line, read from the row (list or detail DTO alike, property names match without case).
    /// Never an email: a user's label is the username and nothing else.
    /// </summary>
    public sealed class DashboardLookup
    {
        internal DashboardLookup(Func<IServiceProvider, int, Task<object?>>? get, Func<object, string?> label,
            Func<object, string?>? detail, Func<string?, PagedQueryDto, PagedQueryDto>? query)
        {
            Get = get;
            Label = label;
            Detail = detail;
            Query = query;
        }

        /// <summary>The type's own get by id (the same service call as its <c>GET {id}</c>); null = search only.</summary>
        public Func<IServiceProvider, int, Task<object?>>? Get { get; }

        public Func<object, string?> Label { get; }

        public Func<object, string?>? Detail { get; }

        /// <summary>
        /// Turns the search term into the type's own query when the plain term would match more than the label (users:
        /// the username filter, so a lookup can't find a user by an email fragment). Null = the term as is.
        /// </summary>
        public Func<string?, PagedQueryDto, PagedQueryDto>? Query { get; }
    }

    /// <summary>
    /// The single source of truth for who may do what with each dashboard type (knk-workspace
    /// docs/specs/dashboard-access-model/DESIGN.md § 4.2, PandiO/knk-workspace#280, model A). One entry per
    /// <c>[FormConfigurableEntity]</c> type (pinned by DashboardAccessTests), plus <c>RoadProfile</c>, which the roads
    /// page shows without a form configuration. Used by <c>DashboardController</c> (bulk delete, export), the
    /// metadata endpoint (<c>area</c>, <c>accessNode</c>, … on <see cref="EntityMetadataDto"/>) and
    /// <c>RealtimeWebChangeMap.NodeFor</c> (live updates follow the area).
    /// <list type="bullet">
    ///   <item>Delete one = the type's area (or write) node AND <see cref="StaffPermissions.Delete"/> (attributes on each
    ///     <c>DELETE {id}</c>, pinned by DeleteAccessRulesTests).</item>
    ///   <item>Bulk delete = area node AND <see cref="StaffPermissions.Delete"/> AND <see cref="StaffPermissions.DeleteBulk"/>.</item>
    ///   <item>Export = area node AND <see cref="StaffPermissions.Export"/>.</item>
    /// </list>
    /// </summary>
    public static class DashboardAccess
    {
        private const string Content = StaffPermissions.ManageContent;
        private const string World = StaffPermissions.ManageWorld;

        /// <summary>The nodes a page outside an area reads it with (§ 4.6.2), by type.</summary>
        private static readonly IReadOnlyDictionary<string, string[]> ExtraReadNodes = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Location"] = new[] { StaffPermissions.ServerConfig },
            ["Town"] = new[] { StaffPermissions.ServerConfig, StaffPermissions.RoadManage },
            ["District"] = new[] { StaffPermissions.ServerConfig },
            ["Structure"] = new[] { StaffPermissions.ServerConfig },
            ["Domain"] = new[] { StaffPermissions.ManageDiscovery },
            ["Street"] = new[] { StaffPermissions.RoadManage },
            ["MinecraftMaterialRef"] = new[] { StaffPermissions.RoadManage },
            ["ItemBlueprint"] = new[] { StaffPermissions.ManageKits, StaffPermissions.ManageLootboxes },
            ["EnchantmentDefinition"] = new[] { StaffPermissions.ManageKits, StaffPermissions.ManageLootboxes },
            ["Category"] = new[] { StaffPermissions.ManageKits, StaffPermissions.ManageLootboxes },
            ["Grade"] = new[] { StaffPermissions.ManageKits, StaffPermissions.ManageLootboxes },
            ["Kit"] = new[] { StaffPermissions.ManageLootboxes, StaffPermissions.ManageUsers },
        };

        private static readonly IReadOnlyList<DashboardTypeAccess> Entries = new List<DashboardTypeAccess>
        {
            // Content: items, reference data and their link rows.
            Type("Category", DashboardArea.Content, Content,
                Del<ICategoryService>((s, id) => s.DeleteAsync(id)), Lists<ICategoryService, CategoryListDto>((s, q) => s.SearchAsync(q))),
            Type("Tag", DashboardArea.Content, Content,
                Del<ITagService>((s, id) => s.DeleteAsync(id)), Lists<ITagService, TagListDto>((s, q) => s.SearchAsync(q))),
            Type("Grade", DashboardArea.Content, Content,
                Del<IGradeService>((s, id) => s.DeleteAsync(id)), Lists<IGradeService, GradeListDto>((s, q) => s.SearchAsync(q))),
            Type("ItemBlueprint", DashboardArea.Content, Content,
                Del<IItemBlueprintService>((s, id) => s.DeleteAsync(id)), Lists<IItemBlueprintService, ItemBlueprintListDto>((s, q) => s.SearchAsync(q))),
            Type("EnchantmentDefinition", DashboardArea.Content, Content,
                Del<IEnchantmentDefinitionService>((s, id) => s.DeleteAsync(id)),
                Lists<IEnchantmentDefinitionService, EnchantmentDefinitionListDto>((s, q) => s.SearchAsync(q))),
            Type("BannerDesign", DashboardArea.Content, Content,
                Del<IBannerDesignService>((s, id) => s.DeleteAsync(id)), Lists<IBannerDesignService, BannerDesignListDto>((s, q) => s.SearchAsync(q))),
            Type("BannerLayer", DashboardArea.Content, Content, Del<IBannerDesignService>((s, id) => s.DeleteLayerAsync(id))),
            Type("Clan", DashboardArea.Content, Content,
                Del<IClanService>((s, id) => s.DeleteAsync(id)), Lists<IClanService, ClanListDto>((s, q) => s.SearchAsync(q))),
            Type("MinecraftBlockRef", DashboardArea.Content, Content,
                Del<IMinecraftBlockRefService>((s, id) => s.DeleteAsync(id)),
                Lists<IMinecraftBlockRefService, MinecraftBlockRefListDto>((s, q) => s.SearchAsync(q))),
            Type("MinecraftMaterialRef", DashboardArea.Content, Content,
                Del<IMinecraftMaterialRefService>((s, id) => s.DeleteAsync(id)),
                Lists<IMinecraftMaterialRefService, MinecraftMaterialRefListDto>((s, q) => s.SearchAsync(q))),
            Type("MinecraftEnchantmentRef", DashboardArea.Content, Content,
                Del<IMinecraftEnchantmentRefService>((s, id) => s.DeleteAsync(id)),
                Lists<IMinecraftEnchantmentRefService, MinecraftEnchantmentRefListDto>((s, q) => s.SearchAsync(q))),
            ViewOnly("CategoryTag", DashboardArea.Content, Content),
            ViewOnly("ItemBlueprintDefaultEnchantment", DashboardArea.Content, Content),
            ViewOnly("ItemBlueprintOrigin", DashboardArea.Content, Content),
            ViewOnly("ItemBlueprintTag", DashboardArea.Content, Content),
            ViewOnly("AbilityDefinition", DashboardArea.Content, Content),
            ViewOnly("TitleBracket", DashboardArea.Content, Content),

            // World: domains and their subtypes, locations, gates.
            Type("Domain", DashboardArea.World, World,
                Del<IDomainService>((s, id) => s.DeleteAsync(id)), Lists<IDomainService, DomainListDto>((s, q) => s.SearchAsync(q))),
            Type("Town", DashboardArea.World, World,
                Del<ITownService>((s, id) => s.DeleteAsync(id)), Lists<ITownService, TownListDto>((s, q) => s.SearchAsync(q))),
            Type("District", DashboardArea.World, World,
                Del<IDistrictService>((s, id) => s.DeleteAsync(id)), Lists<IDistrictService, DistrictListDto>((s, q) => s.SearchAsync(q))),
            Type("Structure", DashboardArea.World, World,
                Del<IStructureService>((s, id) => s.DeleteAsync(id)), Lists<IStructureService, StructureListDto>((s, q) => s.SearchAsync(q))),
            Type("Street", DashboardArea.World, World,
                Del<IStreetService>((s, id) => s.DeleteAsync(id)), Lists<IStreetService, StreetListDto>((s, q) => s.SearchAsync(q))),
            Type("Location", DashboardArea.World, World,
                Del<ILocationService>((s, id) => s.DeleteAsync(id)), Lists<ILocationService, LocationDto>((s, q) => s.SearchAsync(q))),
            Type("GateStructure", DashboardArea.World, World,
                Del<IGateStructureService>((s, id) => s.DeleteAsync(id)), Lists<IGateStructureService, GateStructureListDto>((s, q) => s.SearchAsync(q))),
            Type("GateDoor", DashboardArea.World, World,
                Del<IGateDoorService>((s, id) => s.DeleteAsync(id)), Lists<IGateDoorService, GateDoorListDto>((s, q) => s.SearchAsync(q))),
            Type("DomainSubRegion", DashboardArea.World, World, Del<IDomainSubRegionService>((s, id) => s.DeleteAsync(id))),
            ViewOnly("GateBlockSnapshot", DashboardArea.World, World),
            ViewOnly("GateOpenedBlockSnapshot", DashboardArea.World, World),

            // Kits.
            Type("Kit", DashboardArea.Kits, StaffPermissions.ManageKits,
                Del<IKitService>((s, id) => s.DeleteAsync(id)), Lists<IKitService, KitDto>((s, q) => s.SearchAsync(q))),
            ViewOnly("KitContent", DashboardArea.Kits, StaffPermissions.ManageKits),

            // Lootboxes.
            Type("LootboxType", DashboardArea.Lootboxes, StaffPermissions.ManageLootboxes,
                Del<ILootboxTypeService>((s, id) => s.DeleteAsync(id)), Lists<ILootboxTypeService, LootboxTypeDto>((s, q) => s.SearchAsync(q))),
            Type("LootboxSpecialEntry", DashboardArea.Lootboxes, StaffPermissions.ManageLootboxes,
                Del<ILootboxSpecialEntryService>((s, id) => s.DeleteAsync(id)),
                Lists<ILootboxSpecialEntryService, LootboxSpecialEntryDto>((s, q) => s.SearchAsync(q))),
            Type("LootboxSpawnArea", DashboardArea.Lootboxes, StaffPermissions.ManageLootboxes,
                Del<ILootboxSpawnAreaService>((s, id) => s.DeleteAsync(id)),
                Lists<ILootboxSpawnAreaService, LootboxSpawnAreaDto>((s, q) => s.SearchAsync(q))),
            ViewOnly("LootboxEnchantRoll", DashboardArea.Lootboxes, StaffPermissions.ManageLootboxes),
            ViewOnly("LootboxPoolEntry", DashboardArea.Lootboxes, StaffPermissions.ManageLootboxes),
            ViewOnly("LootboxSpawnAreaType", DashboardArea.Lootboxes, StaffPermissions.ManageLootboxes),
            ViewOnly("LootboxTypeGradeWeight", DashboardArea.Lootboxes, StaffPermissions.ManageLootboxes),

            // Siege setup.
            Type("SiegeScenario", DashboardArea.Siege, StaffPermissions.SiegeManage,
                Del<ISiegeScenarioService>((s, id) => s.DeleteAsync(id)),
                Lists<ISiegeScenarioService, SiegeScenarioListDto>((s, q) => s.SearchAsync(q))),
            Type("SiegeTeam", DashboardArea.Siege, StaffPermissions.SiegeManage,
                Del<ISiegeScenarioService>((s, id) => s.DeleteTeamAsync(id)),
                Lists<ISiegeScenarioService, SiegeTeamReadDto>((s, q) => s.SearchTeamsAsync(q))),
            Type("SiegeLobby", DashboardArea.Siege, StaffPermissions.SiegeManage,
                Del<ISiegeLobbyService>((s, id) => s.DeleteAsync(id)), Lists<ISiegeLobbyService, SiegeLobbyListDto>((s, q) => s.SearchAsync(q))),
            Type("SiegeObjective", DashboardArea.Siege, StaffPermissions.SiegeManage,
                Del<ISiegeScenarioService>((s, id) => s.DeleteObjectiveAsync(id))),
            Type("SiegeSpawnpoint", DashboardArea.Siege, StaffPermissions.SiegeManage,
                Del<ISiegeScenarioService>((s, id) => s.DeleteSpawnpointAsync(id))),
            ViewOnly("SiegeLobbyScenario", DashboardArea.Siege, StaffPermissions.SiegeManage),
            ViewOnly("SiegeScenarioDistrict", DashboardArea.Siege, StaffPermissions.SiegeManage),
            ViewOnly("SiegeScenarioGate", DashboardArea.Siege, StaffPermissions.SiegeManage),

            // Roads: not a [FormConfigurableEntity]; the roads page lists profiles itself (no search to export).
            Type("RoadProfile", DashboardArea.Roads, StaffPermissions.RoadManage,
                Del<IRoadNetworkService>(async (s, id) =>
                {
                    if (!await s.DeleteProfileAsync(id)) throw new KeyNotFoundException($"Road profile {id} not found.");
                })),

            // Players: one delete at a time only (decision 6); group writes need knk.admin.user.perm.
            Type("User", DashboardArea.Players, StaffPermissions.ManageUsers,
                Del<IUserService>((s, id) => s.DeleteAsync(id)), Lists<IUserService, UserListDto>((s, q) => s.SearchAsync(q)), bulk: false),
            Type("PermissionGroup", DashboardArea.Players, StaffPermissions.ManageUsers,
                Del<IPermissionGroupService>((s, id) => s.DeleteAsync(id)),
                Lists<IPermissionGroupService, PermissionGroupListDto>((s, q) => s.SearchAsync(q)), bulk: false,
                writeNode: StaffPermissions.UserPermissions),
            Type("PermissionHolder", DashboardArea.Players, StaffPermissions.ManageUsers, null,
                Lists<IPermissionHolderService, PermissionHolderListDto>((s, q) => s.SearchAsync(q)), bulk: false,
                writeNode: StaffPermissions.UserPermissions),
            ViewOnly("PermissionGrant", DashboardArea.Players, StaffPermissions.ManageUsers, StaffPermissions.UserPermissions),
            ViewOnly("UserPermissionGroup", DashboardArea.Players, StaffPermissions.ManageUsers, StaffPermissions.UserGroups),
        };

        private static readonly IReadOnlyDictionary<string, DashboardTypeAccess> ByName =
            Entries.ToDictionary(e => e.TypeName, StringComparer.OrdinalIgnoreCase);

        /// <summary>Every entry, in the order above.</summary>
        public static IReadOnlyList<DashboardTypeAccess> All => Entries;

        /// <summary>The entry for a type name (case-insensitive), or null for a type outside the dashboard.</summary>
        public static DashboardTypeAccess? Find(string? typeName) =>
            typeName != null && ByName.TryGetValue(typeName.Trim(), out var entry) ? entry : null;

        /// <summary>Every distinct area node, in area order (who may open the dashboard, decision 8).</summary>
        public static IReadOnlyList<string> AreaNodes { get; } = Entries.Select(e => e.AreaNode).Distinct(StringComparer.Ordinal).ToList();

        /// <summary>The area node of an area (records of an area that aren't a dashboard type read with it).</summary>
        public static string AreaNodeOf(DashboardArea area) => Entries.First(e => e.Area == area).AreaNode;

        private static DashboardTypeAccess Type(string name, DashboardArea area, string areaNode,
            Func<IServiceProvider, int, Task>? delete, Func<IServiceProvider, PagedQueryDto, Task<DashboardPage>>? search = null,
            bool bulk = true, string? writeNode = null) =>
            new(name, area, areaNode, writeNode ?? areaNode, ReadNodesOf(name, areaNode), delete, bulk, search,
                DashboardLookups.For(name));

        private static DashboardTypeAccess ViewOnly(string name, DashboardArea area, string areaNode, string? writeNode = null) =>
            new(name, area, areaNode, writeNode ?? areaNode, ReadNodesOf(name, areaNode), null, false, null);

        private static IReadOnlyList<string> ReadNodesOf(string name, string areaNode) =>
            new[] { areaNode }.Concat(ExtraReadNodes.GetValueOrDefault(name, Array.Empty<string>())).ToList();

        private static Func<IServiceProvider, int, Task> Del<TService>(Func<TService, int, Task> delete) where TService : notnull =>
            (services, id) => delete(services.GetRequiredService<TService>(), id);

        private static Func<IServiceProvider, PagedQueryDto, Task<DashboardPage>> Lists<TService, TItem>(
            Func<TService, PagedQueryDto, Task<PagedResultDto<TItem>>> search) where TService : notnull =>
            async (services, query) =>
            {
                var page = await search(services.GetRequiredService<TService>(), query);
                return new DashboardPage(page.Items.Cast<object>().ToList(), page.TotalCount);
            };
    }
}
