using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// The rules of a landmark (off-road DESIGN § 6.3, PandiO/knk-workspace#708): a name of at most 100 characters,
    /// unique per world (the world of the pin, ignoring case); a pin (an existing Location, or one embedded in the request
    /// and created or updated here, like a domain's spawn point); an optional domain that must exist; the extent
    /// (<c>Region</c> needs a region id, <c>Radius</c> a radius above 0 and at most <see cref="MaxRadius"/>, and the other
    /// extent field is cleared); a preference from 0.25 to 4.0; the kind <c>General</c>. Everything is checked before an
    /// embedded Location is written, so a refused request leaves nothing behind. Deleting a landmark leaves its Location to
    /// the Location retention check (KNG-80), as deleting a domain does.
    /// </summary>
    public class LandmarkService : ILandmarkService
    {
        /// <summary>The largest radius extent in blocks (an agent default; a castle or a town square fits well inside).</summary>
        public const double MaxRadius = 512;

        private readonly ILandmarkRepository _repo;
        private readonly ILocationRepository _locationRepo;
        private readonly ILocationService _locationService;
        private readonly IMapper _mapper;

        public LandmarkService(ILandmarkRepository repo, ILocationRepository locationRepo, ILocationService locationService, IMapper mapper)
        {
            _repo = repo;
            _locationRepo = locationRepo;
            _locationService = locationService;
            _mapper = mapper;
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
            await CheckDomainAsync(dto.DomainId);
            var extent = Extent(dto, null);
            var preference = Preference(dto.Preference, Landmark.DefaultPreference);
            var kind = Kind(dto.Kind, LandmarkKind.General);

            var entity = new Landmark
            {
                Name = name,
                LocationId = await SavePinAsync(dto, pin, name),
                DomainId = dto.DomainId is > 0 ? dto.DomainId : null,
                ExtentKind = extent.Kind,
                RegionId = extent.RegionId,
                Radius = extent.Radius,
                Preference = preference,
                NavigationEnabled = dto.NavigationEnabled ?? true,
                Kind = kind,
            };
            await _repo.AddAsync(entity);

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
            await CheckDomainAsync(dto.DomainId);
            var extent = Extent(dto, existing);
            var preference = Preference(dto.Preference, existing.Preference);
            var kind = Kind(dto.Kind, existing.Kind);

            existing.LocationId = await SavePinAsync(dto, pin, name);
            existing.Name = name;
            existing.DomainId = dto.DomainId is > 0 ? dto.DomainId : null;
            existing.ExtentKind = extent.Kind;
            existing.RegionId = extent.RegionId;
            existing.Radius = extent.Radius;
            existing.Preference = preference;
            existing.NavigationEnabled = dto.NavigationEnabled ?? existing.NavigationEnabled;
            existing.Kind = kind;

            await _repo.UpdateAsync(existing);
            return (await GetByIdAsync(id))!;
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
            var trimmed = name?.Trim();
            if (string.IsNullOrEmpty(trimmed)) throw new ArgumentException("Name is required.", nameof(name));
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

            if (existing != null)
            {
                return new Pin(existing.LocationId, existing.Location?.World ?? string.Empty);
            }

            throw new ArgumentException("A landmark needs a Location (locationId or location).", nameof(dto));
        }

        /// <summary>Writes an embedded Location (create without id, update with id) and returns the id to store.</summary>
        private async Task<int> SavePinAsync(LandmarkCreateDto dto, Pin pin, string landmarkName)
        {
            if (dto.Location == null) return pin.ExistingId!.Value;

            var location = dto.Location;
            if (string.IsNullOrWhiteSpace(location.Name)) location.Name = landmarkName;
            location.World = pin.World;
            if (pin.ExistingId == null)
            {
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

        private async Task CheckDomainAsync(int? domainId)
        {
            if (domainId is not > 0) return;
            if (await _repo.GetDomainAsync(domainId.Value) == null)
                throw new ArgumentException($"Domain with id {domainId} not found.", nameof(domainId));
        }

        private sealed record ExtentValue(LandmarkExtentKind Kind, string? RegionId, double? Radius);

        private static ExtentValue Extent(LandmarkCreateDto dto, Landmark? existing)
        {
            if (string.IsNullOrWhiteSpace(dto.ExtentKind))
            {
                // Create: no extent. Update: keep the stored one.
                return existing == null
                    ? new ExtentValue(LandmarkExtentKind.None, null, null)
                    : new ExtentValue(existing.ExtentKind, existing.RegionId, existing.Radius);
            }

            if (!Enum.TryParse<LandmarkExtentKind>(dto.ExtentKind.Trim(), true, out var kind) || !Enum.IsDefined(kind))
                throw new ArgumentException("Extent must be None, Region or Radius.", nameof(dto));

            switch (kind)
            {
                case LandmarkExtentKind.Region:
                    var regionId = dto.RegionId?.Trim();
                    if (string.IsNullOrEmpty(regionId)) throw new ArgumentException("A region extent needs a region id.", nameof(dto));
                    if (regionId.Length > Landmark.RegionIdMaxLength)
                        throw new ArgumentException($"The region id can be at most {Landmark.RegionIdMaxLength} characters.", nameof(dto));
                    return new ExtentValue(kind, regionId, null);
                case LandmarkExtentKind.Radius:
                    if (dto.Radius is not { } radius || double.IsNaN(radius) || radius <= 0 || radius > MaxRadius)
                        throw new ArgumentException($"A radius extent needs a radius above 0 and at most {MaxRadius} blocks.", nameof(dto));
                    return new ExtentValue(kind, null, radius);
                default:
                    return new ExtentValue(LandmarkExtentKind.None, null, null);
            }
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
