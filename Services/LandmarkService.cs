using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// The rules of a landmark, a <see cref="Domain"/> subtype (landmark-domain DESIGN § 5, PandiO/knk-workspace#983; first
    /// built by off-road #708):
    /// <list type="number">
    ///   <item>Name: at most 100 characters, unique per world among landmarks (the world of the pin, ignoring case), on top
    ///     of the domain name rules (LD18).</item>
    ///   <item>Pin: an existing Location, or one embedded in the request and created or updated here. A Location another
    ///     domain already has (a town's spawn) is copied, because a Location belongs to one domain (LD17). The domain's
    ///     world is the pin's world.</item>
    ///   <item>Parent (LD09, LD23): any existing domain, never the landmark itself or one of its descendants; no
    ///     containment check.</item>
    ///   <item>Area (LD06–LD08, LD16): <c>None</c> (no region), <c>OwnRegion</c> (a region no other domain holds, under
    ///     the one-owner rule; a drawn one becomes <c>domain_&lt;id&gt;</c> on save, LD14) or <c>LinkedDomain</c> (needs a
    ///     parent with a region; the landmark owns none). Leaving <c>OwnRegion</c> drops the sub-region rows, so their
    ///     regions fall under the orphan rules (rule 3.4).</item>
    ///   <item>Access, messages and warp: entry and exit allowed, messages off, warp off by default (LD24); the warp
    ///     settings are checked like a domain's.</item>
    ///   <item>Preference 0.25 to 4.0, kind <c>General</c>.</item>
    /// </list>
    /// Everything is checked before an embedded Location is written, so a refused request leaves nothing behind. Deleting a
    /// landmark leaves its Location to the Location retention check (KNG-80), as deleting a domain does.
    /// <para>
    /// Compatibility (agent decision of L1): until knk-plugin moves to this model (L3, #985), the #708 fields are still
    /// read when the new ones are not sent: <c>domainId</c> for the parent, <c>extentKind</c> <c>None</c> or
    /// <c>Region</c> with <c>regionId</c> for the area (a <c>Region</c> that names the area's current main region keeps
    /// the stored area, so the plugin's read-modify-write of a <c>LinkedDomain</c> landmark changes nothing), and a
    /// radius is refused (LD08).
    /// </para>
    /// </summary>
    public class LandmarkService : ILandmarkService
    {
        private readonly ILandmarkRepository _repo;
        private readonly ILocationRepository _locationRepo;
        private readonly ILocationService _locationService;
        private readonly IMapper _mapper;

        // Optional so the rules can be tested without them: the region rename on save (LD14) and the warp checks.
        private readonly IDomainRegionNameFinalizer? _regionNames;
        private readonly ITeleportDestinationService? _teleports;

        public LandmarkService(ILandmarkRepository repo, ILocationRepository locationRepo, ILocationService locationService, IMapper mapper,
            IDomainRegionNameFinalizer? regionNames = null, ITeleportDestinationService? teleports = null)
        {
            _repo = repo;
            _locationRepo = locationRepo;
            _locationService = locationService;
            _mapper = mapper;
            _regionNames = regionNames;
            _teleports = teleports;
        }

        public async Task<IReadOnlyList<LandmarkReadDto>> GetAllAsync(string? world = null)
        {
            var items = await _repo.GetAllAsync(world);
            return _mapper.Map<List<LandmarkReadDto>>(items);
        }

        public async Task<LandmarkReadDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var entity = await _repo.GetByIdAsync(id);
            return entity == null ? null : _mapper.Map<LandmarkReadDto>(entity);
        }

        public async Task<LandmarkReadDto> CreateAsync(LandmarkCreateDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            var name = ValidName(dto.Name);
            var pin = await PinAsync(dto, null);
            await CheckNameFreeAsync(name, pin.World, null);
            var parent = await ParentAsync(RequestedParentId(dto), null);
            var area = await AreaAsync(dto, null, parent, pin.World);
            var preference = Preference(dto.Preference, Landmark.DefaultPreference);
            var kind = Kind(dto.Kind, LandmarkKind.General);
            if (_teleports != null) await _teleports.ValidateSettingsAsync(dto);

            var entity = new Landmark
            {
                Name = name,
                Description = dto.Description?.Trim() ?? string.Empty,
                CreatedAt = DateTime.UtcNow,
                WorldName = pin.World,
                ParentDomainId = parent?.Id,
                AreaKind = area.Kind,
                WgRegionId = area.RegionId,
                AllowEntry = dto.AllowEntry ?? true,
                AllowExit = dto.AllowExit ?? true,
                ShowEnterLeaveMessages = dto.ShowEnterLeaveMessages ?? false,
                Preference = preference,
                NavigationEnabled = dto.NavigationEnabled ?? true,
                Kind = kind,
            };
            DomainTeleportSettings.Apply(entity, dto);
            entity.LocationId = await SavePinAsync(dto, pin, name, null);

            await _repo.AddAsync(entity);
            await FinalizeDrawnRegionAsync(entity, null);

            return (await GetByIdAsync(entity.Id))!;
        }

        public async Task<LandmarkReadDto> UpdateAsync(int id, LandmarkUpdateDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"Landmark with id {id} not found.");

            var name = ValidName(dto.Name);
            var pin = await PinAsync(dto, existing);
            await CheckNameFreeAsync(name, pin.World, id);
            var parent = await ParentAsync(RequestedParentId(dto), id);
            var area = await AreaAsync(dto, existing, parent, pin.World);
            var preference = Preference(dto.Preference, existing.Preference);
            var kind = Kind(dto.Kind, existing.Kind);
            if (_teleports != null) await _teleports.ValidateSettingsAsync(dto);
            // KNG-103: the region before this save, replaced when the region task was run again.
            var previousRegionId = existing.WgRegionId;
            bool leavesOwnRegion = existing.AreaKind == LandmarkAreaKind.OwnRegion && area.Kind != LandmarkAreaKind.OwnRegion;

            existing.LocationId = await SavePinAsync(dto, pin, name, id);
            existing.Name = name;
            if (dto.Description != null) existing.Description = dto.Description.Trim();
            existing.WorldName = pin.World;
            existing.ParentDomainId = parent?.Id;
            existing.ParentDomain = parent == null ? null : existing.ParentDomain?.Id == parent.Id ? existing.ParentDomain : null;
            existing.AreaKind = area.Kind;
            existing.WgRegionId = area.RegionId;
            existing.AllowEntry = dto.AllowEntry ?? existing.AllowEntry;
            existing.AllowExit = dto.AllowExit ?? existing.AllowExit;
            existing.ShowEnterLeaveMessages = dto.ShowEnterLeaveMessages ?? existing.ShowEnterLeaveMessages;
            DomainTeleportSettings.Apply(existing, dto);
            existing.Preference = preference;
            existing.NavigationEnabled = dto.NavigationEnabled ?? existing.NavigationEnabled;
            existing.Kind = kind;

            await _repo.UpdateAsync(existing);
            // Rule 3.4: away from its own region, the landmark's sub-regions become orphans (domain sub-regions § 8).
            if (leavesOwnRegion) await _repo.RemoveSubRegionsAsync(id);
            await FinalizeDrawnRegionAsync(existing, previousRegionId);
            return (await GetByIdAsync(id))!;
        }

        public async Task<LandmarkReadDto> UpdateFromDomainAsync(int id, Domain domain)
        {
            if (domain == null) throw new ArgumentNullException(nameof(domain));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"Landmark with id {id} not found.");

            // LD19: the domain dashboard's edit sends the domain fields only. The region it sends is the area's: a region
            // keeps (or makes) it OwnRegion; none keeps LinkedDomain and makes anything else a point.
            string? region = string.IsNullOrWhiteSpace(domain.WgRegionId) ? null : domain.WgRegionId.Trim();
            string areaKind = region != null
                ? nameof(LandmarkAreaKind.OwnRegion)
                : existing.AreaKind == LandmarkAreaKind.LinkedDomain ? nameof(LandmarkAreaKind.LinkedDomain) : nameof(LandmarkAreaKind.None);
            return await UpdateAsync(id, new LandmarkUpdateDto
            {
                Name = domain.Name,
                Description = domain.Description,
                LocationId = domain.LocationId is > 0 ? domain.LocationId : existing.LocationId,
                ParentDomainId = existing.ParentDomainId,
                AreaKind = areaKind,
                WgRegionId = region,
                AllowEntry = domain.AllowEntry,
                AllowExit = domain.AllowExit,
            });
        }

        /// <summary>
        /// LD14: a region drawn through the form's region task is still <c>tempregion_worldtask_&lt;n&gt;</c>, which the
        /// plugin's 14-day cleanup would delete; it becomes <c>domain_&lt;id&gt;</c> now, like every domain's
        /// (managed-worldguard-regions.md § 7), and its sub-regions <c>domain_&lt;id&gt;_sub_&lt;n&gt;</c> through the
        /// sub-region path. An existing region id (any other name) is kept as it is. A failed rename never fails the save:
        /// the landmark keeps the temp name, which the cleanup keeps, and <c>POST api/Regions/finalize-temp-names</c> or the
        /// next save tries again.
        /// </summary>
        private async Task FinalizeDrawnRegionAsync(Landmark landmark, string? previousRegionId)
        {
            if (_regionNames == null || landmark.AreaKind != LandmarkAreaKind.OwnRegion
                || !DomainRegionNameFinalizer.IsTemporary(landmark.WgRegionId)) return;
            await _regionNames.FinalizeAsync(landmark, previousRegionId);
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"Landmark with id {id} not found.");

            await _repo.DeleteAsync(existing);
        }

        public async Task<PagedResultDto<LandmarkListDto>> SearchAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));

            var query = _mapper.Map<PagedQuery>(queryDto);
            var result = await _repo.SearchAsync(query);
            return _mapper.Map<PagedResultDto<LandmarkListDto>>(result);
        }

        private static string ValidName(string? name)
        {
            var trimmed = DomainNameRules.RequireName(name, nameof(Landmark), nameof(name));
            if (trimmed.Length > Landmark.NameMaxLength)
                throw new ArgumentException($"Name can be at most {Landmark.NameMaxLength} characters.", nameof(name));
            return trimmed;
        }

        /// <summary>The pin as the request names it: an embedded Location, a location id, or (on update) the stored one.</summary>
        private sealed record Pin(int? ExistingId, string World);

        private async Task<Pin> PinAsync(LandmarkCreateDto dto, Landmark? existing)
        {
            if (dto.Location != null)
            {
                var embeddedId = dto.Location.Id is > 0 ? dto.Location.Id : null;
                var world = dto.Location.World?.Trim();
                if (string.IsNullOrEmpty(world) && embeddedId != null)
                {
                    world = (await _locationRepo.GetByIdAsync(embeddedId.Value))?.World
                        ?? throw new ArgumentException($"Location with id {embeddedId} not found.", nameof(dto));
                }
                if (string.IsNullOrEmpty(world)) throw new ArgumentException("The landmark's Location needs a world.", nameof(dto));
                if (embeddedId == null && (!dto.Location.X.HasValue || !dto.Location.Y.HasValue || !dto.Location.Z.HasValue))
                    throw new ArgumentException("The landmark's Location needs x, y and z.", nameof(dto));
                if (embeddedId != null && await _locationRepo.GetByIdAsync(embeddedId.Value) == null)
                    throw new ArgumentException($"Location with id {embeddedId} not found.", nameof(dto));
                return new Pin(embeddedId, world);
            }

            if (dto.LocationId is > 0)
            {
                var location = await _locationRepo.GetByIdAsync(dto.LocationId.Value)
                    ?? throw new ArgumentException($"Location with id {dto.LocationId} not found.", nameof(dto));
                if (string.IsNullOrWhiteSpace(location.World)) throw new ArgumentException("The landmark's Location needs a world.", nameof(dto));
                return new Pin(location.Id, location.World.Trim());
            }

            if (existing?.LocationId is int storedId)
            {
                return new Pin(storedId, existing.Location?.World?.Trim() ?? existing.WorldName ?? string.Empty);
            }

            throw new ArgumentException("A landmark needs a Location (locationId or location).", nameof(dto));
        }

        /// <summary>
        /// Writes an embedded Location (create without id, update with id) and returns the id to store. LD17: a Location
        /// another domain has is never shared (it is unique per domain); the landmark gets a copy of it.
        /// </summary>
        private async Task<int> SavePinAsync(LandmarkCreateDto dto, Pin pin, string landmarkName, int? selfId)
        {
            bool shared = pin.ExistingId is int pinId && await _repo.LocationUsedByOtherDomainAsync(pinId, selfId);

            if (dto.Location == null)
            {
                if (!shared) return pin.ExistingId!.Value;
                var original = await _locationRepo.GetByIdAsync(pin.ExistingId!.Value)
                    ?? throw new ArgumentException($"Location with id {pin.ExistingId} not found.", nameof(dto));
                var copy = await _locationService.CreateAsync(new LocationDto
                {
                    Name = string.IsNullOrWhiteSpace(original.Name) ? landmarkName : original.Name,
                    X = original.X, Y = original.Y, Z = original.Z, Yaw = original.Yaw, Pitch = original.Pitch,
                    World = pin.World,
                });
                return copy.Id!.Value;
            }

            var location = dto.Location;
            if (string.IsNullOrWhiteSpace(location.Name)) location.Name = landmarkName;
            location.World = pin.World;
            if (pin.ExistingId == null || shared)
            {
                if (shared)
                {
                    // The embedded values on a copy; fields it leaves out come from the shared Location.
                    var original = await _locationRepo.GetByIdAsync(pin.ExistingId!.Value);
                    location.X ??= original?.X;
                    location.Y ??= original?.Y;
                    location.Z ??= original?.Z;
                    location.Yaw ??= original?.Yaw;
                    location.Pitch ??= original?.Pitch;
                }
                location.Id = null;
                var created = await _locationService.CreateAsync(location);
                return created.Id!.Value;
            }

            await _locationService.UpdateAsync(pin.ExistingId.Value, location);
            return pin.ExistingId.Value;
        }

        private async Task CheckNameFreeAsync(string name, string world, int? exceptId)
        {
            if (await _repo.NameTakenAsync(name, world, exceptId))
                throw new ArgumentException($"A landmark named \"{name}\" already exists in world {world}.", nameof(name));
        }

        /// <summary>The parent the request names: <c>parentDomainId</c>, else the #708 <c>domainId</c>; null for none.</summary>
        private static int? RequestedParentId(LandmarkCreateDto dto)
        {
            int? id = dto.ParentDomainId ?? dto.DomainId;
            return id is > 0 ? id : null;
        }

        /// <summary>LD09, LD23: the parent must exist and may not be the landmark itself or one of its descendants.</summary>
        private async Task<Domain?> ParentAsync(int? parentId, int? selfId)
        {
            if (parentId is not int id) return null;
            if (selfId == id) throw new ArgumentException("A landmark can't be its own parent.", nameof(parentId));
            var parent = await _repo.GetDomainAsync(id)
                ?? throw new ArgumentException($"Domain with id {id} not found.", nameof(parentId));
            if (selfId is int self && (await _repo.AncestorIdsAsync(id)).Contains(self))
                throw new ArgumentException($"Domain {parent.Name} (#{id}) is inside this landmark: a landmark can't be its own ancestor.", nameof(parentId));
            return parent;
        }

        private sealed record AreaValue(LandmarkAreaKind Kind, string? RegionId);

        private async Task<AreaValue> AreaAsync(LandmarkCreateDto dto, Landmark? existing, Domain? parent, string world)
        {
            var requested = RequestedArea(dto, existing);

            switch (requested.Kind)
            {
                case LandmarkAreaKind.OwnRegion:
                    var regionId = requested.RegionId?.Trim();
                    if (string.IsNullOrEmpty(regionId))
                        throw new ArgumentException("Area Own region needs a region: draw one or name an existing region.", nameof(dto));
                    if (regionId.Length > Landmark.RegionIdMaxLength)
                        throw new ArgumentException($"The region id can be at most {Landmark.RegionIdMaxLength} characters.", nameof(dto));
                    // One region belongs to at most one domain per world (DomainRegionOwnership), the landmark included.
                    var takenBy = await _repo.RegionTakenByAsync(regionId, world, existing?.Id);
                    if (takenBy != null) throw new ArgumentException(takenBy, nameof(dto));
                    return new AreaValue(LandmarkAreaKind.OwnRegion, regionId);
                case LandmarkAreaKind.LinkedDomain:
                    if (parent == null || string.IsNullOrWhiteSpace(parent.WgRegionId))
                        throw new ArgumentException("Area Linked domain's area needs a parent with a region.", nameof(dto));
                    return new AreaValue(LandmarkAreaKind.LinkedDomain, null);
                default:
                    return new AreaValue(LandmarkAreaKind.None, null);
            }
        }

        /// <summary>The area the request asks for, before the checks: the new fields first, then the #708 ones.</summary>
        private static AreaValue RequestedArea(LandmarkCreateDto dto, Landmark? existing)
        {
            if (dto.Radius != null || string.Equals(dto.ExtentKind?.Trim(), "Radius", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The radius area is gone: draw a round region with //sel cyl instead.", nameof(dto));

            if (!string.IsNullOrWhiteSpace(dto.AreaKind))
            {
                if (!Enum.TryParse<LandmarkAreaKind>(dto.AreaKind.Trim(), true, out var kind) || !Enum.IsDefined(kind))
                    throw new ArgumentException("Area must be None, OwnRegion or LinkedDomain.", nameof(dto));
                return new AreaValue(kind, dto.WgRegionId ?? dto.RegionId);
            }

            if (!string.IsNullOrWhiteSpace(dto.ExtentKind))
            {
                // Compatibility with knk-plugin before L3: "None" or "Region" with regionId.
                switch (dto.ExtentKind.Trim().ToLowerInvariant())
                {
                    case "none":
                        return new AreaValue(LandmarkAreaKind.None, null);
                    case "region":
                        var region = dto.RegionId ?? dto.WgRegionId;
                        if (existing != null && region != null
                            && string.Equals(region.Trim(), LandmarkMappingProfile.AreaMainRegion(existing), StringComparison.OrdinalIgnoreCase))
                            return new AreaValue(existing.AreaKind, existing.WgRegionId);
                        return new AreaValue(LandmarkAreaKind.OwnRegion, region);
                    default:
                        throw new ArgumentException("Extent must be None or Region.", nameof(dto));
                }
            }

            if (!string.IsNullOrWhiteSpace(dto.WgRegionId))
                return new AreaValue(LandmarkAreaKind.OwnRegion, dto.WgRegionId);

            // Create: a point. Update: keep the stored area.
            return existing == null
                ? new AreaValue(LandmarkAreaKind.None, null)
                : new AreaValue(existing.AreaKind, existing.WgRegionId);
        }

        private static double Preference(double? value, double fallback)
        {
            if (value is not { } preference) return fallback;
            if (double.IsNaN(preference) || preference < Landmark.PreferenceMin || preference > Landmark.PreferenceMax)
                throw new ArgumentException($"Preference must be from {Landmark.PreferenceMin} to {Landmark.PreferenceMax}.", nameof(value));
            return preference;
        }

        private static LandmarkKind Kind(string? value, LandmarkKind fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            if (!Enum.TryParse<LandmarkKind>(value.Trim(), true, out var kind) || !Enum.IsDefined(kind))
                throw new ArgumentException("Kind must be General.", nameof(value));
            return kind;
        }
    }
}
