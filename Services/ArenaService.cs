using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Arenas and their areas (arena duels KNG-210, docs/specs/arena-duels/DESIGN.md §4.1–4.2, rev. 6). Validates the duel
    /// settings, keeps the eligible kits in order, resolves the arena's world and renames its temp region like the other
    /// domain services. Battlegrounds, spectator areas and the lobby are <see cref="ArenaArea"/>s with their own regions,
    /// renamed on save to <c>arena_&lt;arenaId&gt;_&lt;kind&gt;_&lt;id&gt;</c>.
    /// Invalid input throws <see cref="ArgumentException"/> (400), a missing row <see cref="KeyNotFoundException"/>.
    /// </summary>
    public class ArenaService : IArenaService
    {
        public const int MinOffers = 1;
        public const int MaxOffersLimit = 6;
        public const int MaxFeePercent = 20;

        private static readonly Regex KeyPattern = new("^[A-Za-z0-9_.,\\-]{1,64}$", RegexOptions.Compiled);

        private readonly IArenaRepository _repo;
        private readonly IMapper _mapper;
        private readonly ITeleportDestinationService _teleportDestinations;
        private readonly IDomainRegionNameFinalizer _regionNames;
        private readonly IDomainWorldResolver _worlds;
        private readonly KnKDbContext _context;

        public ArenaService(
            IArenaRepository repo,
            IMapper mapper,
            ITeleportDestinationService teleportDestinations,
            IDomainRegionNameFinalizer regionNames,
            IDomainWorldResolver worlds,
            KnKDbContext context)
        {
            _repo = repo;
            _mapper = mapper;
            _teleportDestinations = teleportDestinations;
            _regionNames = regionNames;
            _worlds = worlds;
            _context = context;
        }

        // ===================================================================== Arenas

        public async Task<List<ArenaDto>> GetAllAsync() => _mapper.Map<List<ArenaDto>>(await _repo.GetAllAsync());

        public async Task<ArenaDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var arena = await _repo.GetByIdAsync(id);
            return arena == null ? null : _mapper.Map<ArenaDto>(arena);
        }

        public async Task<ArenaDto?> GetByKeyAsync(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            var arena = await _repo.GetByKeyAsync(key);
            return arena == null ? null : _mapper.Map<ArenaDto>(arena);
        }

        public async Task<ArenaDto> CreateAsync(ArenaDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var arena = new Arena { CreatedAt = DateTime.UtcNow };
            await ApplyAsync(arena, dto, isNew: true);
            await _repo.AddAsync(arena);
            await _regionNames.FinalizeAsync(arena);
            return _mapper.Map<ArenaDto>(await _repo.GetByIdAsync(arena.Id) ?? arena);
        }

        public async Task UpdateAsync(int id, ArenaDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var arena = await _repo.GetByIdAsync(id) ?? throw new KeyNotFoundException($"Arena with id {id} not found.");
            string previousRegionId = arena.WgRegionId;
            await ApplyAsync(arena, dto, isNew: false);
            await _repo.SaveChangesAsync();
            await _regionNames.FinalizeAsync(arena, previousRegionId);
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var arena = await _repo.GetByIdAsync(id) ?? throw new KeyNotFoundException($"Arena with id {id} not found.");
            await _repo.DeleteArenaAsync(arena);
        }

        public async Task<PagedResultDto<ArenaListDto>> SearchAsync(PagedQueryDto query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            var result = await _repo.SearchAsync(_mapper.Map<PagedQuery>(query));
            return _mapper.Map<PagedResultDto<ArenaListDto>>(result);
        }

        /// <summary>Validates <paramref name="dto"/> against the arena as it will be and writes it. Nothing is written
        /// when a check fails.</summary>
        private async Task ApplyAsync(Arena arena, ArenaDto dto, bool isNew)
        {
            string name = Required(dto.Name, "Name");
            string regionId = Required(dto.WgRegionId, "WgRegionId");
            string key = NormalizeKey(dto.Key);
            if (await _repo.KeyTakenAsync(key, isNew ? null : arena.Id))
                throw new ArgumentException($"Another arena already has the key '{key}'.", nameof(dto));

            int streetId = dto.StreetId ?? 0;
            int districtId = dto.DistrictId ?? 0;
            if (streetId <= 0) throw new ArgumentException("StreetId is required.", nameof(dto));
            if (districtId <= 0) throw new ArgumentException("DistrictId is required.", nameof(dto));
            if ((isNew || arena.StreetId != streetId) && !await _repo.StreetExistsAsync(streetId))
                throw new ArgumentException($"Street with id {streetId} not found.", nameof(dto));
            if ((isNew || arena.DistrictId != districtId) && !await _repo.DistrictExistsAsync(districtId))
                throw new ArgumentException($"District with id {districtId} not found.", nameof(dto));
            int houseNumber = dto.HouseNumber ?? (isNew ? 0 : arena.HouseNumber);
            if (houseNumber < 0) throw new ArgumentException("HouseNumber can't be negative.", nameof(dto));

            await _teleportDestinations.ValidateSettingsAsync(dto);
            await EnsureLocationAsync(dto.LocationId, "LocationId");
            var exit = await EnsureLocationAsync(dto.ExitLocationId, "ExitLocationId");

            // The settings as they will be: a null DTO value keeps the current one (the default on create).
            var s = new ArenaSettings(arena);
            s.Overlay(dto);
            // Rev. 6: the lobby is an area added after the arena exists, so a new arena starts closed for duels unless
            // the caller says otherwise (and then the lobby check below refuses it).
            if (isNew && dto.IsDuelEnabled == null) s.IsDuelEnabled = false;
            if (s.StakeMinTitleBracketId is int bracketId && !await _repo.TitleBracketExistsAsync(bracketId))
                throw new ArgumentException($"Title bracket with id {bracketId} not found.", nameof(dto));

            List<ArenaKit>? kits = null;
            if (dto.EligibleKits != null)
            {
                kits = await ResolveKitsAsync(dto.EligibleKits);
            }
            int eligibleKitCount = kits?.Count ?? arena.EligibleKits.Count;
            ValidateSettings(s, eligibleKitCount, !isNew && HasUsableLobby(arena.Areas));

            string world = await _worlds.ResolveAsync(new DomainWorldRequest
            {
                DomainId = isNew ? null : arena.Id,
                RequestedWorld = dto.WorldName,
                WgRegionId = regionId,
                LocationId = dto.LocationId,
                ParentDomainId = districtId
            });
            EnsureSameWorld(world, exit, "exit location");

            // All checks passed: write.
            arena.Name = name;
            arena.Description = dto.Description ?? string.Empty;
            arena.WgRegionId = regionId;
            arena.WorldName = world;
            arena.AllowEntry = dto.AllowEntry ?? (isNew || arena.AllowEntry);
            arena.AllowExit = dto.AllowExit ?? (isNew || arena.AllowExit);
            arena.LocationId = dto.LocationId;
            arena.StreetId = streetId;
            arena.DistrictId = districtId;
            arena.HouseNumber = houseNumber;
            arena.Key = key;
            arena.ExitLocationId = dto.ExitLocationId;
            s.WriteTo(arena);
            DomainTeleportSettings.Apply(arena, dto);
            DomainNavigationDefaults.Apply(arena, dto);
            if (kits != null) SyncKits(arena, kits);
        }

        /// <summary>DESIGN.md §4.2 (and the plan's Phase 1 list): ranges, at least one gear mode, a kit mode needs an
        /// eligible kit, the default mode must be allowed. An arena open for duels needs its lobby: rev. 6, an enabled
        /// LOBBY area with an arrival location (<paramref name="hasUsableLobby"/>).</summary>
        public static void ValidateSettings(ArenaSettings s, int eligibleKitCount, bool hasUsableLobby)
        {
            var errors = new List<string>();
            if (s.MaxOffers < MinOffers || s.MaxOffers > MaxOffersLimit) errors.Add($"MaxOffers must be {MinOffers}-{MaxOffersLimit}.");
            if (s.FeePercent < 0 || s.FeePercent > MaxFeePercent) errors.Add($"FeePercent must be 0-{MaxFeePercent}.");
            if (s.MinStakeRatioPercent < 0 || s.MinStakeRatioPercent > 100) errors.Add("MinStakeRatioPercent must be 0-100.");
            if (s.FightTimeLimitSeconds < 0) errors.Add("FightTimeLimitSeconds can't be negative (0 = no limit).");
            if (s.OfferSeconds < 1) errors.Add("OfferSeconds must be at least 1.");
            if (s.MaxCoinStake < 0 || s.MaxCoinStake > BalanceLimits.MaxCoins) errors.Add($"MaxCoinStake must be 0-{BalanceLimits.MaxCoins}.");
            NonNegative(errors, s.MaxQueuedMatches, "MaxQueuedMatches");
            NonNegative(errors, s.MaxItemStacks, "MaxItemStacks");
            NonNegative(errors, s.MaxStakedDuelsPerPairPerDay, "MaxStakedDuelsPerPairPerDay");
            NonNegative(errors, s.PairCooldownMinutes, "PairCooldownMinutes");
            NonNegative(errors, s.DailyCoinWinCap, "DailyCoinWinCap");
            NonNegative(errors, s.WarmupSeconds, "WarmupSeconds");
            NonNegative(errors, s.CountdownSeconds, "CountdownSeconds");
            NonNegative(errors, s.MaxWaitSeconds, "MaxWaitSeconds");
            NonNegative(errors, s.WinnerLingerSeconds, "WinnerLingerSeconds");
            if (s.StakeMinAccountAgeHours is < 0) errors.Add("StakeMinAccountAgeHours can't be negative.");

            if (!s.AllowOwnGearKeep && !s.AllowOwnGearDrop && !s.AllowFixedKit && !s.AllowKitChoice)
                errors.Add("Allow at least one gear mode.");
            if ((s.AllowFixedKit || s.AllowKitChoice) && eligibleKitCount == 0)
                errors.Add("A kit mode (fixed kit or kit choice) needs at least one eligible kit.");
            if (!s.IsAllowed(s.DefaultGearMode))
                errors.Add($"The default gear mode {s.DefaultGearMode} is not one of the allowed modes.");
            if (s.IsDuelEnabled && !hasUsableLobby)
                errors.Add("An arena open for duels needs an enabled lobby area with an arrival location (save the arena with duels off, add the lobby in \"Battlegrounds, spectator areas and lobby\", then turn duels on).");

            if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors));
        }

        private async Task<List<ArenaKit>> ResolveKitsAsync(List<ArenaKitDto> rows)
        {
            var ids = new List<int>();
            foreach (var row in rows.Where(r => r != null))
            {
                int kitId = row.KitId ?? 0;
                if (kitId <= 0) throw new ArgumentException("Every eligible kit needs a kit.");
                if (ids.Contains(kitId)) throw new ArgumentException($"Kit {kitId} is listed twice.");
                ids.Add(kitId);
            }
            var existing = await _repo.ExistingKitIdsAsync(ids);
            var missing = ids.Except(existing).ToList();
            if (missing.Count > 0) throw new ArgumentException($"Kit not found: {string.Join(", ", missing)}.");
            return ids.Select((kitId, index) => new ArenaKit { KitId = kitId, SequenceNumber = index }).ToList();
        }

        /// <summary>Replace-set in the given order: removed kits go, kept ones get their new place.</summary>
        private static void SyncKits(Arena arena, List<ArenaKit> wanted)
        {
            var wantedIds = wanted.Select(k => k.KitId).ToHashSet();
            foreach (var stale in arena.EligibleKits.Where(k => !wantedIds.Contains(k.KitId)).ToList())
                arena.EligibleKits.Remove(stale);
            foreach (var want in wanted)
            {
                var row = arena.EligibleKits.FirstOrDefault(k => k.KitId == want.KitId);
                if (row == null)
                {
                    arena.EligibleKits.Add(new ArenaKit { ArenaId = arena.Id, KitId = want.KitId, SequenceNumber = want.SequenceNumber });
                }
                else
                {
                    row.SequenceNumber = want.SequenceNumber;
                }
            }
        }

        // ===================================================================== Areas (rev. 6)

        /// <summary>An enabled LOBBY area with an arrival location: what an arena open for duels needs.</summary>
        public static bool HasUsableLobby(IEnumerable<ArenaArea> areas) =>
            areas.Any(a => a.Kind == ArenaAreaKind.LOBBY && a.IsEnabled && a.ArrivalLocationId is > 0);

        public async Task<List<ArenaAreaDto>> GetAreasAsync(int? arenaId) =>
            _mapper.Map<List<ArenaAreaDto>>(await _repo.GetAreasAsync(arenaId));

        public async Task<ArenaAreaDto?> GetAreaAsync(int id)
        {
            var area = id > 0 ? await _repo.GetAreaAsync(id) : null;
            return area == null ? null : _mapper.Map<ArenaAreaDto>(area);
        }

        public async Task<ArenaAreaDto> CreateAreaAsync(ArenaAreaDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var area = new ArenaArea { CreatedAt = DateTime.UtcNow };
            await ApplyAreaAsync(area, dto, isNew: true);
            await _repo.AddAsync(area);
            return _mapper.Map<ArenaAreaDto>(await _repo.GetAreaAsync(area.Id) ?? area);
        }

        public async Task<ArenaAreaDto> UpdateAreaAsync(int id, ArenaAreaDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var area = (id > 0 ? await _repo.GetAreaAsync(id) : null)
                ?? throw new KeyNotFoundException($"Arena area with id {id} not found.");
            await ApplyAreaAsync(area, dto, isNew: false);
            await _repo.SaveChangesAsync();
            return _mapper.Map<ArenaAreaDto>(await _repo.GetAreaAsync(area.Id) ?? area);
        }

        public async Task DeleteAreaAsync(int id)
        {
            var area = (id > 0 ? await _repo.GetAreaAsync(id) : null)
                ?? throw new KeyNotFoundException($"Arena area with id {id} not found.");
            var arena = await _repo.GetByIdAsync(area.ArenaId);
            if (arena != null && arena.IsDuelEnabled && area.Kind == ArenaAreaKind.LOBBY
                && !HasUsableLobby(arena.Areas.Where(a => a.Id != area.Id)))
                throw new ArgumentException($"{arena.Name} is open for duels and needs its lobby: turn duels off first.");
            // The regions go with the area; their WorldGuard regions become orphans (DESIGN.md §4.1).
            await _repo.RemoveAsync(area);
        }

        public async Task<PagedResultDto<ArenaAreaDto>> SearchAreasAsync(PagedQueryDto query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            var result = await _repo.SearchAreasAsync(_mapper.Map<PagedQuery>(query));
            return _mapper.Map<PagedResultDto<ArenaAreaDto>>(result);
        }

        /// <summary>DESIGN.md §4.1: kind fields, unique key, one lobby, locations in a region's world, an enabled
        /// battleground has both spawns and a region, the lobby of an arena open for duels stays usable.</summary>
        private async Task ApplyAreaAsync(ArenaArea area, ArenaAreaDto dto, bool isNew)
        {
            int arenaId = isNew ? (dto.ArenaId ?? 0) : area.ArenaId;
            if (!isNew && dto.ArenaId is > 0 && dto.ArenaId != area.ArenaId)
                throw new ArgumentException("An area can't move to another arena: create it there instead.", nameof(dto));
            if (arenaId <= 0) throw new ArgumentException("ArenaId is required.", nameof(dto));
            var arena = await _repo.GetByIdAsync(arenaId) ?? throw new ArgumentException($"Arena with id {arenaId} not found.");

            ArenaAreaKind kind = ParseAreaKind(dto.Kind) ?? (isNew
                ? throw new ArgumentException($"Kind is required: one of {string.Join(", ", Enum.GetNames<ArenaAreaKind>())}.", nameof(dto))
                : area.Kind);
            if (!isNew && kind != area.Kind && area.Regions.Count > 0)
                throw new ArgumentException("Remove the area's regions before changing its kind (their names carry the kind), or create a new area.", nameof(dto));
            string key = NormalizeKey(dto.Key);
            if (await _repo.AreaKeyTakenAsync(arenaId, key, isNew ? null : area.Id))
                throw new ArgumentException($"{arena.Name} already has an area with the key '{key}'.", nameof(dto));
            if (kind == ArenaAreaKind.LOBBY && await _repo.OtherLobbyExistsAsync(arenaId, isNew ? null : area.Id))
                throw new ArgumentException($"{arena.Name} already has a lobby; an arena has at most one.", nameof(dto));
            string? name = string.IsNullOrWhiteSpace(dto.Name) ? null : dto.Name.Trim();
            if (name != null && name.Length > 64) throw new ArgumentException("Name is longer than 64 characters.", nameof(dto));
            int sortOrder = dto.SortOrder ?? (isNew ? 0 : area.SortOrder);
            bool isEnabled = dto.IsEnabled ?? (!isNew && area.IsEnabled);

            // Fields of another kind are cleared (DESIGN.md §4.1).
            bool battleground = kind == ArenaAreaKind.BATTLEGROUND;
            bool spectator = kind == ArenaAreaKind.SPECTATOR;
            int? spawnAId = battleground && dto.SpawnALocationId is > 0 ? dto.SpawnALocationId : null;
            int? spawnBId = battleground && dto.SpawnBLocationId is > 0 ? dto.SpawnBLocationId : null;
            int? arrivalId = !battleground && dto.ArrivalLocationId is > 0 ? dto.ArrivalLocationId : null;
            int? titleId = spectator && dto.TeleportMinTitleBracketId is > 0 ? dto.TeleportMinTitleBracketId : null;
            int? priceGems = spectator && dto.TeleportPriceGems is > 0 ? dto.TeleportPriceGems : null;
            if (spectator && dto.TeleportPriceGems is < 0) throw new ArgumentException("TeleportPriceGems can't be negative.", nameof(dto));
            if (titleId is int bracketId && !await _repo.TitleBracketExistsAsync(bracketId))
                throw new ArgumentException($"Title bracket with id {bracketId} not found.", nameof(dto));

            var spawnA = await EnsureLocationAsync(spawnAId, "SpawnALocationId");
            var spawnB = await EnsureLocationAsync(spawnBId, "SpawnBLocationId");
            var arrival = await EnsureLocationAsync(arrivalId, "ArrivalLocationId");
            if (spawnAId != null && spawnAId == spawnBId)
                throw new ArgumentException("Spawn A and spawn B must be two different locations.", nameof(dto));
            int regionCount = isNew ? 0 : area.Regions.Count;
            if (battleground && isEnabled && (spawnA == null || spawnB == null || regionCount == 0))
                throw new ArgumentException("An enabled battleground needs spawn A, spawn B and at least one region (save it disabled, add its regions, then enable it).", nameof(dto));

            // Locations lie in the world of one of the area's regions; the arena's world while it has none.
            var worlds = isNew || area.Regions.Count == 0
                ? new[] { arena.WorldName }.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w!.Trim()).ToList()
                : area.Regions.Select(r => r.WorldName).Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            EnsureInWorlds(worlds, spawnA, "spawn A");
            EnsureInWorlds(worlds, spawnB, "spawn B");
            EnsureInWorlds(worlds, arrival, "arrival location");

            if (arena.IsDuelEnabled)
            {
                var after = arena.Areas.Where(a => a.Id != area.Id).Append(new ArenaArea
                {
                    Kind = kind, IsEnabled = isEnabled, ArrivalLocationId = arrivalId
                });
                if (!HasUsableLobby(after))
                    throw new ArgumentException($"{arena.Name} is open for duels and needs an enabled lobby with an arrival location: turn duels off first.", nameof(dto));
            }

            area.ArenaId = arenaId;
            area.Kind = kind;
            area.Key = key;
            area.Name = name;
            area.SortOrder = sortOrder;
            area.IsEnabled = isEnabled;
            area.SpawnALocationId = spawnAId;
            area.SpawnBLocationId = spawnBId;
            area.ArrivalLocationId = arrivalId;
            area.TeleportMinTitleBracketId = titleId;
            area.TeleportPriceGems = priceGems;
        }

        public static ArenaAreaKind? ParseAreaKind(string? value) =>
            Enum.TryParse<ArenaAreaKind>(value?.Trim(), ignoreCase: true, out var kind) && Enum.IsDefined(kind) ? kind : null;

        // ===================================================================== Area regions (rev. 6)

        public async Task<List<ArenaAreaRegionDto>> GetAreaRegionsAsync(int? areaId) =>
            _mapper.Map<List<ArenaAreaRegionDto>>(await _repo.GetAreaRegionsAsync(areaId));

        public async Task<ArenaAreaRegionDto?> GetAreaRegionAsync(int id)
        {
            var region = id > 0 ? await _repo.GetAreaRegionAsync(id) : null;
            return region == null ? null : _mapper.Map<ArenaAreaRegionDto>(region);
        }

        public async Task<ArenaAreaRegionDto?> FindAreaRegionAsync(string regionId, string? world)
        {
            if (string.IsNullOrWhiteSpace(regionId)) return null;
            var region = await _repo.FindAreaRegionAsync(regionId, world);
            return region == null ? null : _mapper.Map<ArenaAreaRegionDto>(region);
        }

        public async Task<ArenaAreaRegionDto> CreateAreaRegionAsync(ArenaAreaRegionDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            int areaId = dto.ArenaAreaId ?? 0;
            if (areaId <= 0) throw new ArgumentException("ArenaAreaId is required.", nameof(dto));
            var area = await _repo.GetAreaAsync(areaId) ?? throw new KeyNotFoundException($"Arena area with id {areaId} not found.");
            var region = new ArenaAreaRegion { ArenaAreaId = area.Id, CreatedAt = DateTime.UtcNow };
            await ApplyAreaRegionAsync(region, area, dto, null);
            await _repo.AddAsync(region);
            await _regionNames.FinalizeArenaAreaRegionAsync(region, null);
            return _mapper.Map<ArenaAreaRegionDto>(await _repo.GetAreaRegionAsync(region.Id) ?? region);
        }

        public async Task<ArenaAreaRegionDto> UpdateAreaRegionAsync(int id, ArenaAreaRegionDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var region = (id > 0 ? await _repo.GetAreaRegionAsync(id) : null)
                ?? throw new KeyNotFoundException($"Arena area region with id {id} not found.");
            if (dto.ArenaAreaId is > 0 && dto.ArenaAreaId != region.ArenaAreaId)
                throw new ArgumentException("A region can't move to another area: remove it and add it there.", nameof(dto));
            var area = await _repo.GetAreaAsync(region.ArenaAreaId) ?? throw new KeyNotFoundException($"Arena area with id {region.ArenaAreaId} not found.");
            string previousRegionId = region.WgRegionId;
            await ApplyAreaRegionAsync(region, area, dto, region.Id);
            await _repo.SaveChangesAsync();
            // A re-run of its world task (KNG-103): the new region replaces the old one under the final name.
            await _regionNames.FinalizeArenaAreaRegionAsync(region, previousRegionId);
            return _mapper.Map<ArenaAreaRegionDto>(await _repo.GetAreaRegionAsync(region.Id) ?? region);
        }

        public async Task DeleteAreaRegionAsync(int id)
        {
            var region = (id > 0 ? await _repo.GetAreaRegionAsync(id) : null)
                ?? throw new KeyNotFoundException($"Arena area region with id {id} not found.");
            var area = await _repo.GetAreaAsync(region.ArenaAreaId);
            if (area != null && area.Kind == ArenaAreaKind.BATTLEGROUND && area.IsEnabled && area.Regions.Count(r => r.Id != region.Id) == 0)
                throw new ArgumentException("This is the last region of an enabled battleground: disable the battleground first.");
            // The WorldGuard region stays: the game server takes it for an orphan and deletes it after the retention
            // period (DESIGN.md §4.1); until then it can be adopted again with "select".
            await _repo.RemoveAsync(region);
        }

        public async Task<PagedResultDto<ArenaAreaRegionDto>> SearchAreaRegionsAsync(PagedQueryDto query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            var result = await _repo.SearchAreaRegionsAsync(_mapper.Map<PagedQuery>(query));
            return _mapper.Map<PagedResultDto<ArenaAreaRegionDto>>(result);
        }

        /// <summary>The region lies in the arena's world (a world task's region was drawn there) and no domain, domain
        /// sub-region or other area region in that world uses its id.</summary>
        private async Task ApplyAreaRegionAsync(ArenaAreaRegion region, ArenaArea area, ArenaAreaRegionDto dto, int? selfId)
        {
            string regionId = Required(dto.WgRegionId, "WgRegionId");
            if (regionId.Length > 128) throw new ArgumentException("The region id is longer than 128 characters.", nameof(dto));
            var arena = area.Arena ?? await _repo.GetByIdAsync(area.ArenaId) ?? throw new ArgumentException($"Arena with id {area.ArenaId} not found.");
            string? arenaWorld = string.IsNullOrWhiteSpace(arena.WorldName) ? null : arena.WorldName.Trim();
            string? requested = string.IsNullOrWhiteSpace(dto.WorldName) ? null : dto.WorldName.Trim();
            string? drawnIn = await RegionTaskWorldAsync(regionId);
            string? world = arenaWorld ?? drawnIn ?? requested;
            if (world == null)
                throw new DomainWorldException(DomainWorldException.WorldRequired, "The region's world is unknown: give the arena its world first.");
            foreach (var other in new[] { drawnIn, requested })
            {
                if (other != null && !other.Equals(world, StringComparison.OrdinalIgnoreCase))
                    throw new DomainWorldException(DomainWorldException.WorldConflict,
                        $"The region is in world '{other}', but {arena.Name} is in world '{world}': an area's regions are in the arena's world.");
            }
            if (regionId.Equals(arena.WgRegionId, StringComparison.OrdinalIgnoreCase))
                throw new DomainWorldException(DomainWorldException.RegionTaken, $"Region '{regionId}' is the arena's own region.");
            bool changed = !regionId.Equals(region.WgRegionId ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            if (changed)
            {
                string? takenBy = await DomainRegionOwnership.TakenByAsync(_context, regionId, world, exceptAreaRegionId: selfId);
                if (takenBy != null) throw new DomainWorldException(DomainWorldException.RegionTaken, takenBy);
            }
            region.WgRegionId = regionId;
            region.WorldName = world;
        }

        private async Task<string?> RegionTaskWorldAsync(string regionId)
        {
            if (!DomainRegionNameFinalizer.IsTemporary(regionId)) return null;
            if (!int.TryParse(regionId.Substring(DomainRegionNameFinalizer.TempRegionPrefix.Length), out int taskId) || taskId <= 0) return null;
            string? output = await _context.WorldTasks.AsNoTracking()
                .Where(t => t.Id == taskId)
                .Select(t => t.OutputJson)
                .FirstOrDefaultAsync();
            return DomainWorldResolver.WorldNameFromTaskOutput(output);
        }

        // ===================================================================== Helpers

        private async Task<Location?> EnsureLocationAsync(int? locationId, string field)
        {
            if (locationId is not > 0) return null;
            return await _repo.GetLocationAsync(locationId.Value)
                ?? throw new ArgumentException($"{field}: Location with id {locationId} not found.");
        }

        private static void EnsureSameWorld(string world, Location? location, string what)
        {
            if (location == null || string.IsNullOrWhiteSpace(location.World)) return;
            if (!location.World.Trim().Equals(world, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"The {what} is in world '{location.World}', not in the domain's world '{world}'.");
        }

        private static void EnsureInWorlds(IReadOnlyList<string> worlds, Location? location, string what)
        {
            if (location == null || string.IsNullOrWhiteSpace(location.World) || worlds.Count == 0) return;
            if (!worlds.Any(w => w.Equals(location.World.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"The {what} is in world '{location.World}', not in the world of the area's regions ({string.Join(", ", worlds)}).");
        }

        private static string Required(string? value, string field)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{field} is required.");
            return value.Trim();
        }

        private static string NormalizeKey(string? key)
        {
            string trimmed = Required(key, "Key");
            if (!KeyPattern.IsMatch(trimmed))
                throw new ArgumentException("Key may only use letters, digits and _ . , - (at most 64 characters).");
            return trimmed;
        }

        private static void NonNegative(List<string> errors, int value, string field)
        {
            if (value < 0) errors.Add($"{field} can't be negative.");
        }

        public static DuelGearMode? ParseGearMode(string? value) =>
            Enum.TryParse<DuelGearMode>(value?.Trim(), ignoreCase: true, out var mode) && Enum.IsDefined(mode) ? mode : null;
    }

    /// <summary>An arena's duel settings while they are checked: the stored values with a DTO laid over them.</summary>
    public sealed class ArenaSettings
    {
        public bool IsDuelEnabled, AllowCoinStakes, AllowItemStakes, AllowOwnGearKeep, AllowOwnGearDrop, AllowFixedKit, AllowKitChoice,
            AnnounceMatches, AnnounceFriendly;
        public int MaxQueuedMatches, MaxOffers, OfferSeconds, MaxCoinStake, MaxItemStacks, MinStakeRatioPercent, FeePercent,
            MaxStakedDuelsPerPairPerDay, PairCooldownMinutes, DailyCoinWinCap, WarmupSeconds, CountdownSeconds,
            FightTimeLimitSeconds, MaxWaitSeconds, WinnerLingerSeconds;
        public int? StakeMinAccountAgeHours, StakeMinTitleBracketId;
        public DuelGearMode DefaultGearMode;

        public ArenaSettings(Arena a)
        {
            IsDuelEnabled = a.IsDuelEnabled; MaxQueuedMatches = a.MaxQueuedMatches;
            MaxOffers = a.MaxOffers; OfferSeconds = a.OfferSeconds;
            AllowCoinStakes = a.AllowCoinStakes; AllowItemStakes = a.AllowItemStakes; MaxCoinStake = a.MaxCoinStake;
            MaxItemStacks = a.MaxItemStacks; MinStakeRatioPercent = a.MinStakeRatioPercent; FeePercent = a.FeePercent;
            StakeMinAccountAgeHours = a.StakeMinAccountAgeHours; StakeMinTitleBracketId = a.StakeMinTitleBracketId;
            MaxStakedDuelsPerPairPerDay = a.MaxStakedDuelsPerPairPerDay; PairCooldownMinutes = a.PairCooldownMinutes;
            DailyCoinWinCap = a.DailyCoinWinCap;
            AllowOwnGearKeep = a.AllowOwnGearKeep; AllowOwnGearDrop = a.AllowOwnGearDrop; AllowFixedKit = a.AllowFixedKit;
            AllowKitChoice = a.AllowKitChoice; DefaultGearMode = a.DefaultGearMode;
            WarmupSeconds = a.WarmupSeconds; CountdownSeconds = a.CountdownSeconds; FightTimeLimitSeconds = a.FightTimeLimitSeconds;
            MaxWaitSeconds = a.MaxWaitSeconds; WinnerLingerSeconds = a.WinnerLingerSeconds;
            AnnounceMatches = a.AnnounceMatches; AnnounceFriendly = a.AnnounceFriendly;
        }

        /// <summary>
        /// Lays the DTO over the stored values. The eligibility overrides are nullable on purpose (null = the /pay
        /// rules), so the DTO's value always replaces them; a form without those fields sends null and clears them,
        /// which is the default anyway.
        /// </summary>
        public void Overlay(ArenaDto d)
        {
            IsDuelEnabled = d.IsDuelEnabled ?? IsDuelEnabled; MaxQueuedMatches = d.MaxQueuedMatches ?? MaxQueuedMatches;
            MaxOffers = d.MaxOffers ?? MaxOffers; OfferSeconds = d.OfferSeconds ?? OfferSeconds;
            AllowCoinStakes = d.AllowCoinStakes ?? AllowCoinStakes; AllowItemStakes = d.AllowItemStakes ?? AllowItemStakes;
            MaxCoinStake = d.MaxCoinStake ?? MaxCoinStake; MaxItemStacks = d.MaxItemStacks ?? MaxItemStacks;
            MinStakeRatioPercent = d.MinStakeRatioPercent ?? MinStakeRatioPercent; FeePercent = d.FeePercent ?? FeePercent;
            StakeMinAccountAgeHours = d.StakeMinAccountAgeHours;
            StakeMinTitleBracketId = d.StakeMinTitleBracketId is > 0 ? d.StakeMinTitleBracketId : null;
            MaxStakedDuelsPerPairPerDay = d.MaxStakedDuelsPerPairPerDay ?? MaxStakedDuelsPerPairPerDay;
            PairCooldownMinutes = d.PairCooldownMinutes ?? PairCooldownMinutes; DailyCoinWinCap = d.DailyCoinWinCap ?? DailyCoinWinCap;
            AllowOwnGearKeep = d.AllowOwnGearKeep ?? AllowOwnGearKeep; AllowOwnGearDrop = d.AllowOwnGearDrop ?? AllowOwnGearDrop;
            AllowFixedKit = d.AllowFixedKit ?? AllowFixedKit; AllowKitChoice = d.AllowKitChoice ?? AllowKitChoice;
            if (!string.IsNullOrWhiteSpace(d.DefaultGearMode))
            {
                DefaultGearMode = ArenaService.ParseGearMode(d.DefaultGearMode)
                    ?? throw new ArgumentException($"DefaultGearMode must be one of {string.Join(", ", Enum.GetNames<DuelGearMode>())}, not '{d.DefaultGearMode}'.");
            }
            WarmupSeconds = d.WarmupSeconds ?? WarmupSeconds; CountdownSeconds = d.CountdownSeconds ?? CountdownSeconds;
            FightTimeLimitSeconds = d.FightTimeLimitSeconds ?? FightTimeLimitSeconds; MaxWaitSeconds = d.MaxWaitSeconds ?? MaxWaitSeconds;
            WinnerLingerSeconds = d.WinnerLingerSeconds ?? WinnerLingerSeconds;
            AnnounceMatches = d.AnnounceMatches ?? AnnounceMatches; AnnounceFriendly = d.AnnounceFriendly ?? AnnounceFriendly;
        }

        public bool IsAllowed(DuelGearMode mode) => mode switch
        {
            DuelGearMode.OWN_GEAR_KEEP => AllowOwnGearKeep,
            DuelGearMode.OWN_GEAR_DROP => AllowOwnGearDrop,
            DuelGearMode.FIXED_KIT => AllowFixedKit,
            DuelGearMode.KIT_CHOICE => AllowKitChoice,
            _ => false
        };

        public void WriteTo(Arena a)
        {
            a.IsDuelEnabled = IsDuelEnabled; a.MaxQueuedMatches = MaxQueuedMatches;
            a.MaxOffers = MaxOffers; a.OfferSeconds = OfferSeconds;
            a.AllowCoinStakes = AllowCoinStakes; a.AllowItemStakes = AllowItemStakes; a.MaxCoinStake = MaxCoinStake;
            a.MaxItemStacks = MaxItemStacks; a.MinStakeRatioPercent = MinStakeRatioPercent; a.FeePercent = FeePercent;
            a.StakeMinAccountAgeHours = StakeMinAccountAgeHours; a.StakeMinTitleBracketId = StakeMinTitleBracketId;
            a.MaxStakedDuelsPerPairPerDay = MaxStakedDuelsPerPairPerDay; a.PairCooldownMinutes = PairCooldownMinutes;
            a.DailyCoinWinCap = DailyCoinWinCap;
            a.AllowOwnGearKeep = AllowOwnGearKeep; a.AllowOwnGearDrop = AllowOwnGearDrop; a.AllowFixedKit = AllowFixedKit;
            a.AllowKitChoice = AllowKitChoice; a.DefaultGearMode = DefaultGearMode;
            a.WarmupSeconds = WarmupSeconds; a.CountdownSeconds = CountdownSeconds; a.FightTimeLimitSeconds = FightTimeLimitSeconds;
            a.MaxWaitSeconds = MaxWaitSeconds; a.WinnerLingerSeconds = WinnerLingerSeconds;
            a.AnnounceMatches = AnnounceMatches; a.AnnounceFriendly = AnnounceFriendly;
        }
    }
}
