using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Arenas, battlegrounds and spectator areas (arena duels KNG-210, docs/specs/arena-duels/DESIGN.md §4.1–4.2).
    /// Validates the duel settings, keeps the eligible kits in order, resolves each domain's world and renames its
    /// temp region like the other domain services (District → Arena → its battlegrounds and spectator areas).
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

        public ArenaService(
            IArenaRepository repo,
            IMapper mapper,
            ITeleportDestinationService teleportDestinations,
            IDomainRegionNameFinalizer regionNames,
            IDomainWorldResolver worlds)
        {
            _repo = repo;
            _mapper = mapper;
            _teleportDestinations = teleportDestinations;
            _regionNames = regionNames;
            _worlds = worlds;
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
            var lobby = await EnsureLocationAsync(dto.LobbyLocationId, "LobbyLocationId");
            var exit = await EnsureLocationAsync(dto.ExitLocationId, "ExitLocationId");

            // The settings as they will be: a null DTO value keeps the current one (the default on create).
            var s = new ArenaSettings(arena);
            s.Overlay(dto);
            if (s.StakeMinTitleBracketId is int bracketId && !await _repo.TitleBracketExistsAsync(bracketId))
                throw new ArgumentException($"Title bracket with id {bracketId} not found.", nameof(dto));

            List<ArenaKit>? kits = null;
            if (dto.EligibleKits != null)
            {
                kits = await ResolveKitsAsync(dto.EligibleKits);
            }
            int eligibleKitCount = kits?.Count ?? arena.EligibleKits.Count;
            ValidateSettings(s, eligibleKitCount, dto.LobbyLocationId);

            string world = await _worlds.ResolveAsync(new DomainWorldRequest
            {
                DomainId = isNew ? null : arena.Id,
                RequestedWorld = dto.WorldName,
                WgRegionId = regionId,
                LocationId = dto.LocationId,
                ParentDomainId = districtId
            });
            EnsureSameWorld(world, lobby, "lobby location");
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
            arena.LobbyLocationId = dto.LobbyLocationId;
            arena.ExitLocationId = dto.ExitLocationId;
            s.WriteTo(arena);
            DomainTeleportSettings.Apply(arena, dto);
            DomainNavigationDefaults.Apply(arena, dto);
            if (kits != null) SyncKits(arena, kits);
        }

        /// <summary>DESIGN.md §4.2 (and the plan's Phase 1 list): ranges, at least one gear mode, a kit mode needs an
        /// eligible kit, the default mode must be allowed. An arena open for duels needs its lobby.</summary>
        public static void ValidateSettings(ArenaSettings s, int eligibleKitCount, int? lobbyLocationId)
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
            if (s.IsDuelEnabled && lobbyLocationId is not > 0)
                errors.Add("An arena open for duels needs a lobby location (or turn duels off until it is set).");

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

        // ===================================================================== Battlegrounds

        public async Task<List<ArenaBattlegroundDto>> GetBattlegroundsAsync(int? arenaId) =>
            _mapper.Map<List<ArenaBattlegroundDto>>(await _repo.GetBattlegroundsAsync(arenaId));

        public async Task<ArenaBattlegroundDto?> GetBattlegroundAsync(int id)
        {
            var battleground = id > 0 ? await _repo.GetBattlegroundAsync(id) : null;
            return battleground == null ? null : _mapper.Map<ArenaBattlegroundDto>(battleground);
        }

        public async Task<ArenaBattlegroundDto> CreateBattlegroundAsync(ArenaBattlegroundDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var battleground = new ArenaBattleground { CreatedAt = DateTime.UtcNow };
            await ApplyBattlegroundAsync(battleground, dto, isNew: true);
            await _repo.AddAsync(battleground);
            await _regionNames.FinalizeAsync(battleground);
            return _mapper.Map<ArenaBattlegroundDto>(await _repo.GetBattlegroundAsync(battleground.Id) ?? battleground);
        }

        public async Task UpdateBattlegroundAsync(int id, ArenaBattlegroundDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var battleground = (id > 0 ? await _repo.GetBattlegroundAsync(id) : null)
                ?? throw new KeyNotFoundException($"Battleground with id {id} not found.");
            string previousRegionId = battleground.WgRegionId;
            await ApplyBattlegroundAsync(battleground, dto, isNew: false);
            await _repo.SaveChangesAsync();
            await _regionNames.FinalizeAsync(battleground, previousRegionId);
        }

        public async Task DeleteBattlegroundAsync(int id)
        {
            var battleground = (id > 0 ? await _repo.GetBattlegroundAsync(id) : null)
                ?? throw new KeyNotFoundException($"Battleground with id {id} not found.");
            await _repo.DeleteDomainAsync(battleground);
        }

        public async Task<PagedResultDto<ArenaBattlegroundDto>> SearchBattlegroundsAsync(PagedQueryDto query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            var result = await _repo.SearchBattlegroundsAsync(_mapper.Map<PagedQuery>(query));
            return _mapper.Map<PagedResultDto<ArenaBattlegroundDto>>(result);
        }

        private async Task ApplyBattlegroundAsync(ArenaBattleground battleground, ArenaBattlegroundDto dto, bool isNew)
        {
            string name = Required(dto.Name, "Name");
            string regionId = Required(dto.WgRegionId, "WgRegionId");
            string key = NormalizeKey(dto.Key);
            int arenaId = await EnsureArenaAsync(dto.ArenaId);
            if (await _repo.BattlegroundKeyTakenAsync(arenaId, key, isNew ? null : battleground.Id))
                throw new ArgumentException($"The arena already has a battleground with the key '{key}'.", nameof(dto));
            int sortOrder = dto.SortOrder ?? (isNew ? 0 : battleground.SortOrder);
            bool isEnabled = dto.IsEnabled ?? (isNew || battleground.IsEnabled);

            await _teleportDestinations.ValidateSettingsAsync(dto);
            await EnsureLocationAsync(dto.LocationId, "LocationId");
            var spawnA = await EnsureLocationAsync(dto.SpawnALocationId, "SpawnALocationId");
            var spawnB = await EnsureLocationAsync(dto.SpawnBLocationId, "SpawnBLocationId");
            if (dto.SpawnALocationId is > 0 && dto.SpawnALocationId == dto.SpawnBLocationId)
                throw new ArgumentException("Spawn A and spawn B must be two different locations.", nameof(dto));
            if (isEnabled && (spawnA == null || spawnB == null))
                throw new ArgumentException("An enabled battleground needs spawn A and spawn B (or leave it disabled until they are set).", nameof(dto));

            string world = await _worlds.ResolveAsync(new DomainWorldRequest
            {
                DomainId = isNew ? null : battleground.Id,
                RequestedWorld = dto.WorldName,
                WgRegionId = regionId,
                LocationId = dto.LocationId,
                ParentDomainId = arenaId
            });
            EnsureSameWorld(world, spawnA, "spawn A");
            EnsureSameWorld(world, spawnB, "spawn B");

            battleground.Name = name;
            battleground.Description = dto.Description ?? string.Empty;
            battleground.WgRegionId = regionId;
            battleground.WorldName = world;
            // D16: entry off by default; the duel runtime's exemption lets the two duelists in.
            battleground.AllowEntry = dto.AllowEntry ?? (!isNew && battleground.AllowEntry);
            battleground.AllowExit = dto.AllowExit ?? (isNew || battleground.AllowExit);
            battleground.LocationId = dto.LocationId;
            battleground.ArenaId = arenaId;
            battleground.Key = key;
            battleground.SortOrder = sortOrder;
            battleground.IsEnabled = isEnabled;
            battleground.SpawnALocationId = dto.SpawnALocationId;
            battleground.SpawnBLocationId = dto.SpawnBLocationId;
            DomainTeleportSettings.Apply(battleground, dto);
            DomainNavigationDefaults.Apply(battleground, dto);
        }

        // ===================================================================== Spectator areas

        public async Task<List<ArenaSpectatorAreaDto>> GetSpectatorAreasAsync(int? arenaId) =>
            _mapper.Map<List<ArenaSpectatorAreaDto>>(await _repo.GetSpectatorAreasAsync(arenaId));

        public async Task<ArenaSpectatorAreaDto?> GetSpectatorAreaAsync(int id)
        {
            var area = id > 0 ? await _repo.GetSpectatorAreaAsync(id) : null;
            return area == null ? null : _mapper.Map<ArenaSpectatorAreaDto>(area);
        }

        public async Task<ArenaSpectatorAreaDto> CreateSpectatorAreaAsync(ArenaSpectatorAreaDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var area = new ArenaSpectatorArea { CreatedAt = DateTime.UtcNow };
            await ApplySpectatorAreaAsync(area, dto, isNew: true);
            await _repo.AddAsync(area);
            await _regionNames.FinalizeAsync(area);
            return _mapper.Map<ArenaSpectatorAreaDto>(await _repo.GetSpectatorAreaAsync(area.Id) ?? area);
        }

        public async Task UpdateSpectatorAreaAsync(int id, ArenaSpectatorAreaDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var area = (id > 0 ? await _repo.GetSpectatorAreaAsync(id) : null)
                ?? throw new KeyNotFoundException($"Spectator area with id {id} not found.");
            string previousRegionId = area.WgRegionId;
            await ApplySpectatorAreaAsync(area, dto, isNew: false);
            await _repo.SaveChangesAsync();
            await _regionNames.FinalizeAsync(area, previousRegionId);
        }

        public async Task DeleteSpectatorAreaAsync(int id)
        {
            var area = (id > 0 ? await _repo.GetSpectatorAreaAsync(id) : null)
                ?? throw new KeyNotFoundException($"Spectator area with id {id} not found.");
            await _repo.DeleteDomainAsync(area);
        }

        public async Task<PagedResultDto<ArenaSpectatorAreaDto>> SearchSpectatorAreasAsync(PagedQueryDto query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            var result = await _repo.SearchSpectatorAreasAsync(_mapper.Map<PagedQuery>(query));
            return _mapper.Map<PagedResultDto<ArenaSpectatorAreaDto>>(result);
        }

        private async Task ApplySpectatorAreaAsync(ArenaSpectatorArea area, ArenaSpectatorAreaDto dto, bool isNew)
        {
            string name = Required(dto.Name, "Name");
            string regionId = Required(dto.WgRegionId, "WgRegionId");
            int arenaId = await EnsureArenaAsync(dto.ArenaId);
            await _teleportDestinations.ValidateSettingsAsync(dto);
            await EnsureLocationAsync(dto.LocationId, "LocationId");

            string world = await _worlds.ResolveAsync(new DomainWorldRequest
            {
                DomainId = isNew ? null : area.Id,
                RequestedWorld = dto.WorldName,
                WgRegionId = regionId,
                LocationId = dto.LocationId,
                ParentDomainId = arenaId
            });

            area.Name = name;
            area.Description = dto.Description ?? string.Empty;
            area.WgRegionId = regionId;
            area.WorldName = world;
            area.AllowEntry = dto.AllowEntry ?? (isNew || area.AllowEntry);
            area.AllowExit = dto.AllowExit ?? (isNew || area.AllowExit);
            area.LocationId = dto.LocationId;
            area.ArenaId = arenaId;
            DomainTeleportSettings.Apply(area, dto);
            DomainNavigationDefaults.Apply(area, dto);
        }

        // ===================================================================== Legacy seed (arena_2)

        public async Task<ArenaLegacySeedResultDto> SeedLegacyArenaAsync(ArenaLegacySeedDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            string key = NormalizeKey(string.IsNullOrWhiteSpace(dto.Key) ? "arena_2" : dto.Key);
            var regionIds = (dto.BattlegroundRegionIds ?? new List<string>())
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => r.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (regionIds.Count == 0)
                throw new ArgumentException("List the arena's battleground regions (its '-battleground' children in WorldGuard).", nameof(dto));

            var result = new ArenaLegacySeedResultDto();
            var arena = await _repo.GetByKeyAsync(key);
            if (arena == null)
            {
                // Duels stay off until staff set the lobby; everything else keeps the design defaults.
                var created = await CreateAsync(new ArenaDto
                {
                    Key = key,
                    Name = string.IsNullOrWhiteSpace(dto.Name) ? "Arena" : dto.Name.Trim(),
                    Description = "The v1 duel arena (seeded).",
                    WgRegionId = string.IsNullOrWhiteSpace(dto.WgRegionId) ? key : dto.WgRegionId.Trim(),
                    WorldName = dto.WorldName,
                    DistrictId = dto.DistrictId,
                    StreetId = dto.StreetId,
                    HouseNumber = dto.HouseNumber,
                    IsDuelEnabled = false
                });
                arena = await _repo.GetByIdAsync(created.Id!.Value) ?? throw new InvalidOperationException("The seeded arena was not saved.");
                result.ArenaCreated = true;
            }

            int nextOrder = arena.Battlegrounds.Count == 0 ? 0 : arena.Battlegrounds.Max(b => b.SortOrder) + 1;
            int number = arena.Battlegrounds.Count;
            foreach (var regionId in regionIds)
            {
                if (arena.Battlegrounds.Any(b => b.WgRegionId.Equals(regionId, StringComparison.OrdinalIgnoreCase)))
                {
                    result.BattlegroundsExisting.Add(regionId);
                    continue;
                }
                number++;
                string bgKey = $"bg{number}";
                while (arena.Battlegrounds.Any(b => b.Key.Equals(bgKey, StringComparison.OrdinalIgnoreCase)))
                    bgKey = $"bg{++number}";
                // Disabled until staff set both spawns; entry off (D16); discovery and /navigate are off by type.
                await CreateBattlegroundAsync(new ArenaBattlegroundDto
                {
                    ArenaId = arena.Id,
                    Key = bgKey,
                    Name = $"{arena.Name} battleground {number}",
                    Description = $"Seeded from the v1 region {regionId}.",
                    WgRegionId = regionId,
                    WorldName = arena.WorldName,
                    SortOrder = nextOrder++,
                    IsEnabled = false,
                    AllowEntry = false
                });
                result.BattlegroundsCreated.Add(regionId);
                arena = await _repo.GetByIdAsync(arena.Id) ?? arena;
            }

            result.Arena = _mapper.Map<ArenaDto>(await _repo.GetByIdAsync(arena.Id) ?? arena);
            return result;
        }

        // ===================================================================== Helpers

        private async Task<int> EnsureArenaAsync(int? arenaId)
        {
            if (arenaId is not > 0) throw new ArgumentException("ArenaId is required.");
            if (await _repo.GetByIdAsync(arenaId.Value) == null)
                throw new ArgumentException($"Arena with id {arenaId} not found.");
            return arenaId.Value;
        }

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
