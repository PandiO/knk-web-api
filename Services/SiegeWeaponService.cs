using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Regions;

namespace knkwebapi_v2.Services
{
    // Siege weapons phase 1 (knk-workspace docs/specs/siege-minigame/SIEGE_WEAPONS.md § 3, § 9; PandiO/knk-workspace#782):
    // save-time rules for templates, ammo types, a scenario's weapon spots and player unlocks. Data only: nothing here
    // changes a match (the plugin owns weapon state, W63). Same error convention as the other siege services
    // (ArgumentException -> 400, KeyNotFoundException -> 404, InvalidOperationException -> 409).
    public class SiegeWeaponService : ISiegeWeaponService
    {
        // W66: a shot never flies shorter than this.
        public const double MinFlightTimeSeconds = 1.5;
        private const double MaxRadius = 64;

        private readonly ISiegeWeaponRepository _repo;
        private readonly IMapper _mapper;
        private readonly IGameRegionQueryClient? _regions;
        private readonly IDomainRegionNameFinalizer? _regionNames;
        private readonly ILogger<SiegeWeaponService>? _logger;

        /// <param name="regions">#988: asks the game server for a spot region's bounds; null = never asked (the typed class stays).</param>
        /// <param name="regionNames">#988: gives a spot region drawn in the form its final name on save; null = not renamed.</param>
        public SiegeWeaponService(ISiegeWeaponRepository repo, IMapper mapper, IGameRegionQueryClient? regions = null,
            IDomainRegionNameFinalizer? regionNames = null, ILogger<SiegeWeaponService>? logger = null)
        {
            _repo = repo;
            _mapper = mapper;
            _regions = regions;
            _regionNames = regionNames;
            _logger = logger;
        }

        // ==== Templates ====

        public async Task<List<SiegeWeaponTemplateReadDto>> GetTemplatesAsync() =>
            _mapper.Map<List<SiegeWeaponTemplateReadDto>>(await _repo.GetTemplatesAsync());

        public async Task<SiegeWeaponTemplateReadDto?> GetTemplateByIdAsync(int id)
        {
            if (id <= 0) return null;
            var template = await _repo.GetTemplateByIdAsync(id);
            return template == null ? null : _mapper.Map<SiegeWeaponTemplateReadDto>(template);
        }

        public async Task<SiegeWeaponTemplateReadDto> CreateTemplateAsync(SiegeWeaponTemplateUpsertDto dto)
        {
            var template = new SiegeWeaponTemplate();
            await ApplyTemplateAsync(template, dto, existingId: null);
            await _repo.AddTemplateAsync(template);
            var created = await _repo.GetTemplateByIdAsync(template.Id);
            return _mapper.Map<SiegeWeaponTemplateReadDto>(created ?? template);
        }

        public async Task UpdateTemplateAsync(int id, SiegeWeaponTemplateUpsertDto dto)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var template = await _repo.GetTemplateByIdAsync(id)
                ?? throw new KeyNotFoundException($"SiegeWeaponTemplate with id {id} not found.");
            await ApplyTemplateAsync(template, dto, existingId: id);
            await _repo.UpdateTemplateAsync(template);
        }

        public async Task DeleteTemplateAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var template = await _repo.GetTemplateByIdAsync(id)
                ?? throw new KeyNotFoundException($"SiegeWeaponTemplate with id {id} not found.");
            // Unlocks were paid with personal coins (W32): refuse rather than silently take them away.
            if (await _repo.TemplateHasUnlocksAsync(id))
                throw new InvalidOperationException($"Players have unlocked '{template.Name}', so it can't be deleted.");
            await _repo.DeleteTemplateAsync(template);
        }

        public async Task<PagedResultDto<SiegeWeaponTemplateListDto>> SearchTemplatesAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));
            var result = await _repo.SearchTemplatesAsync(_mapper.Map<PagedQuery>(queryDto));
            return _mapper.Map<PagedResultDto<SiegeWeaponTemplateListDto>>(result);
        }

        private async Task ApplyTemplateAsync(SiegeWeaponTemplate target, SiegeWeaponTemplateUpsertDto dto, int? existingId)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            var name = RequireText(dto.Name, "Name", 100);
            var description = OptionalText(dto.Description, "Description", 1000);
            if (!Enum.IsDefined(dto.Kind)) throw new ArgumentException($"Unknown kind '{dto.Kind}'.");
            if (!Enum.IsDefined(dto.SizeClass)) throw new ArgumentException($"Unknown size class '{dto.SizeClass}'.");

            if (dto.MaxHealth < 1) throw new ArgumentException("maxHealth must be at least 1.");
            if (dto.BuildTimeSeconds < 0) throw new ArgumentException("buildTimeSeconds can't be negative.");
            if (dto.ReloadSeconds < 0) throw new ArgumentException("reloadSeconds can't be negative.");
            if (dto.MaxRange <= 0) throw new ArgumentException("maxRange must be above 0.");
            if (dto.MinRange is < 0) throw new ArgumentException("minRange can't be negative.");
            if (dto.MinRange.HasValue && dto.MinRange.Value >= dto.MaxRange)
                throw new ArgumentException("minRange must be below maxRange.");

            if (dto.Kind == SiegeWeaponKind.Indirect)
            {
                // W58 spread by ranging in, W66 flight time.
                if (dto.StartSpreadRadius <= 0 || dto.StartSpreadRadius > MaxRadius)
                    throw new ArgumentException($"startSpreadRadius must be above 0 and at most {MaxRadius}.");
                if (dto.MinSpreadRadius < 0) throw new ArgumentException("minSpreadRadius can't be negative.");
                if (dto.MinSpreadRadius > dto.StartSpreadRadius)
                    throw new ArgumentException("minSpreadRadius can't be larger than startSpreadRadius.");
                if (dto.RangingStep < 0) throw new ArgumentException("rangingStep can't be negative.");
                if (dto.FlightTimeAtMaxRangeSeconds < MinFlightTimeSeconds)
                    throw new ArgumentException($"flightTimeAtMaxRangeSeconds must be at least {MinFlightTimeSeconds}.");
                if (dto.ArcHeight <= 0) throw new ArgumentException("arcHeight must be above 0.");
            }
            else if (dto.ConeHalfAngleDegrees <= 0 || dto.ConeHalfAngleDegrees > 180)
            {
                // W24, W52.
                throw new ArgumentException("coneHalfAngleDegrees must be above 0 and at most 180.");
            }

            if (dto.LeaseIdleTimeoutSeconds is < 1)
                throw new ArgumentException("leaseIdleTimeoutSeconds must be at least 1 (leave it empty for the global default).");
            if (dto.GateDamageMultiplier < 0 || dto.WeaponDamageMultiplier < 0)
                throw new ArgumentException("Damage multipliers can't be negative.");
            if (dto.SpotCooldownSeconds < 0) throw new ArgumentException("spotCooldownSeconds can't be negative.");
            if (dto.MatchPrice < 0 || dto.UnlockPrice < 0) throw new ArgumentException("Prices can't be negative.");
            // Siege shop L4 (#869, P09): empty or 0 (the wizard's empty value; bracket 0 is the lowest title anyway) = no minimum.
            int? minTitle = dto.UnlockMinTitleLevel is > 0 ? dto.UnlockMinTitleLevel : null;
            if (minTitle.HasValue && !await _repo.TitleBracketExistsAsync(minTitle.Value))
                throw new ArgumentException($"TitleBracket with id {minTitle} not found.");

            var sameName = await _repo.GetTemplateByNameAsync(name);
            if (sameName != null && sameName.Id != existingId)
                throw new InvalidOperationException($"A siege weapon template named '{sameName.Name}' already exists.");

            List<int>? ammoIds = null;
            if (dto.AllowedAmmoTypes != null)
            {
                var entries = dto.AllowedAmmoTypes.Where(a => a != null).ToList();
                if (entries.Any(a => a.SiegeAmmoTypeId <= 0)) throw new ArgumentException("Every allowed ammo entry needs a siegeAmmoTypeId.");
                var duplicate = entries.GroupBy(a => a.SiegeAmmoTypeId).FirstOrDefault(g => g.Count() > 1);
                if (duplicate != null) throw new ArgumentException($"Ammo type {duplicate.Key} is listed twice.");
                ammoIds = entries.Select(a => a.SiegeAmmoTypeId).ToList();
                var missing = ammoIds.Except(await _repo.GetExistingAmmoTypeIdsAsync(ammoIds)).ToList();
                if (missing.Count > 0) throw new ArgumentException($"SiegeAmmoType with id {missing[0]} not found.");
            }

            target.Name = name;
            target.Description = description;
            target.Kind = dto.Kind;
            target.SizeClass = dto.SizeClass;
            // The wizard can send 0 for an empty optional reference - treat it as "none".
            target.ArmedSchematicId = dto.ArmedSchematicId is > 0 ? dto.ArmedSchematicId : null;
            target.FiredSchematicId = dto.FiredSchematicId is > 0 ? dto.FiredSchematicId : null;
            target.WreckSchematicId = dto.WreckSchematicId is > 0 ? dto.WreckSchematicId : null;
            target.MaxHealth = dto.MaxHealth;
            target.BuildTimeSeconds = dto.BuildTimeSeconds;
            target.ReloadSeconds = dto.ReloadSeconds;
            target.MaxRange = dto.MaxRange;
            target.MinRange = dto.MinRange;
            target.StartSpreadRadius = dto.StartSpreadRadius;
            target.MinSpreadRadius = dto.MinSpreadRadius;
            target.RangingStep = dto.RangingStep;
            target.FlightTimeAtMaxRangeSeconds = dto.FlightTimeAtMaxRangeSeconds;
            target.ArcHeight = dto.ArcHeight;
            target.LeaseIdleTimeoutSeconds = dto.LeaseIdleTimeoutSeconds;
            target.ConeHalfAngleDegrees = dto.ConeHalfAngleDegrees;
            target.GateDamageMultiplier = dto.GateDamageMultiplier;
            target.WeaponDamageMultiplier = dto.WeaponDamageMultiplier;
            target.SpotCooldownSeconds = dto.SpotCooldownSeconds;
            target.MatchPrice = dto.MatchPrice;
            target.UnlockPrice = dto.UnlockPrice;
            target.UnlockMinTitleLevel = minTitle;

            if (ammoIds != null) SyncAmmoTypes(target, ammoIds);
        }

        // Diff rather than Clear()+re-add (same composite-key tracking reason as the scenario joins).
        private static void SyncAmmoTypes(SiegeWeaponTemplate target, List<int> ammoIds)
        {
            var wanted = ammoIds.ToHashSet();
            foreach (var stale in target.AllowedAmmoTypes.Where(a => !wanted.Contains(a.SiegeAmmoTypeId)).ToList())
                target.AllowedAmmoTypes.Remove(stale);
            foreach (var id in ammoIds.Where(id => target.AllowedAmmoTypes.All(a => a.SiegeAmmoTypeId != id)))
                target.AllowedAmmoTypes.Add(new SiegeWeaponTemplateAmmoType { SiegeWeaponTemplateId = target.Id, SiegeAmmoTypeId = id });
        }

        // ==== Ammo types ====

        public async Task<List<SiegeAmmoTypeReadDto>> GetAmmoTypesAsync() =>
            _mapper.Map<List<SiegeAmmoTypeReadDto>>(await _repo.GetAmmoTypesAsync());

        public async Task<SiegeAmmoTypeReadDto?> GetAmmoTypeByIdAsync(int id)
        {
            if (id <= 0) return null;
            var ammo = await _repo.GetAmmoTypeByIdAsync(id);
            return ammo == null ? null : _mapper.Map<SiegeAmmoTypeReadDto>(ammo);
        }

        public async Task<SiegeAmmoTypeReadDto> CreateAmmoTypeAsync(SiegeAmmoTypeUpsertDto dto)
        {
            var ammo = new SiegeAmmoType();
            await ApplyAmmoTypeAsync(ammo, dto, existingId: null);
            await _repo.AddAmmoTypeAsync(ammo);
            return _mapper.Map<SiegeAmmoTypeReadDto>(ammo);
        }

        public async Task UpdateAmmoTypeAsync(int id, SiegeAmmoTypeUpsertDto dto)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var ammo = await _repo.GetAmmoTypeByIdAsync(id)
                ?? throw new KeyNotFoundException($"SiegeAmmoType with id {id} not found.");
            await ApplyAmmoTypeAsync(ammo, dto, existingId: id);
            await _repo.UpdateAmmoTypeAsync(ammo);
        }

        public async Task DeleteAmmoTypeAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var ammo = await _repo.GetAmmoTypeByIdAsync(id)
                ?? throw new KeyNotFoundException($"SiegeAmmoType with id {id} not found.");
            var templates = await _repo.GetTemplateNamesAllowingAmmoTypeAsync(id);
            if (templates.Count > 0)
                throw new InvalidOperationException(
                    $"'{ammo.Name}' is allowed on {string.Join(", ", templates.Select(n => $"'{n}'"))}; remove it there first.");
            await _repo.DeleteAmmoTypeAsync(ammo);
        }

        public async Task<PagedResultDto<SiegeAmmoTypeListDto>> SearchAmmoTypesAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));
            var result = await _repo.SearchAmmoTypesAsync(_mapper.Map<PagedQuery>(queryDto));
            return _mapper.Map<PagedResultDto<SiegeAmmoTypeListDto>>(result);
        }

        private async Task ApplyAmmoTypeAsync(SiegeAmmoType target, SiegeAmmoTypeUpsertDto dto, int? existingId)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            var name = RequireText(dto.Name, "Name", 100);
            var description = OptionalText(dto.Description, "Description", 1000);
            if (!Enum.IsDefined(dto.Effect)) throw new ArgumentException($"Unknown effect '{dto.Effect}'.");
            if (dto.Damage < 0) throw new ArgumentException("damage can't be negative.");
            if (dto.StructureDamage < 0) throw new ArgumentException("structureDamage can't be negative.");
            if (dto.BlastRadius < 0 || dto.BlastRadius > MaxRadius)
                throw new ArgumentException($"blastRadius must be between 0 and {MaxRadius}.");
            if (dto.Knockback < 0) throw new ArgumentException("knockback can't be negative.");
            if (dto.ArmorPiercing < 0 || dto.ArmorPiercing > 1)
                throw new ArgumentException("armorPiercing must be between 0 (armour counts fully) and 1 (armour is ignored).");
            if (dto.EffectRadius is < 0 or > MaxRadius)
                throw new ArgumentException($"effectRadius must be between 0 and {MaxRadius}.");
            if (dto.EffectDurationSeconds is < 0) throw new ArgumentException("effectDurationSeconds can't be negative.");
            if (dto.EffectLevel is < 1 or > 10) throw new ArgumentException("effectLevel must be between 1 and 10.");
            if (dto.PricePerShot < 0) throw new ArgumentException("pricePerShot can't be negative.");

            var sameName = await _repo.GetAmmoTypeByNameAsync(name);
            if (sameName != null && sameName.Id != existingId)
                throw new InvalidOperationException($"An ammo type named '{sameName.Name}' already exists.");

            target.Name = name;
            target.Description = description;
            target.Effect = dto.Effect;
            target.Damage = dto.Damage;
            target.StructureDamage = dto.StructureDamage;
            target.BlastRadius = dto.BlastRadius;
            target.Knockback = dto.Knockback;
            target.ArmorPiercing = dto.ArmorPiercing;
            target.ProjectileBlock = OptionalText(dto.ProjectileBlock, "projectileBlock", 100);
            target.ParticleTrail = OptionalText(dto.ParticleTrail, "particleTrail", 100);
            target.EffectRadius = dto.EffectRadius;
            target.EffectDurationSeconds = dto.EffectDurationSeconds;
            target.EffectLevel = dto.EffectLevel;
            target.PricePerShot = dto.PricePerShot;
        }

        // ==== Weapon spots (owned by a scenario) ====

        public async Task<List<SiegeWeaponSpotReadDto>> GetSpotsAsync(int siegeScenarioId)
        {
            await RequireScenarioAsync(siegeScenarioId);
            return _mapper.Map<List<SiegeWeaponSpotReadDto>>(await _repo.GetSpotsAsync(siegeScenarioId));
        }

        public async Task<SiegeWeaponSpotReadDto?> GetSpotByIdAsync(int id)
        {
            if (id <= 0) return null;
            var spot = await _repo.GetSpotByIdAsync(id);
            return spot == null ? null : _mapper.Map<SiegeWeaponSpotReadDto>(spot);
        }

        public async Task<SiegeWeaponSpotReadDto> CreateSpotAsync(int siegeScenarioId, SiegeWeaponSpotUpsertDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (dto.SiegeScenarioId is > 0 && dto.SiegeScenarioId.Value != siegeScenarioId)
                throw new ArgumentException("siegeScenarioId in the body doesn't match the scenario in the URL.");

            var scenario = await RequireScenarioAsync(siegeScenarioId);
            var spot = new SiegeWeaponSpot { SiegeScenarioId = siegeScenarioId };
            await ApplySpotAsync(spot, dto, scenario);
            spot.SortOrder = dto.SortOrder ?? NextSortOrder(scenario.WeaponSpots.Select(w => w.SortOrder));
            if (spot.SortOrder < 0) throw new ArgumentException("sortOrder can't be negative.");

            await _repo.AddSpotAsync(spot);
            await FinalizeDrawnRegionAsync(spot, null);
            var created = await _repo.GetSpotByIdAsync(spot.Id);
            return _mapper.Map<SiegeWeaponSpotReadDto>(created ?? spot);
        }

        public async Task UpdateSpotAsync(int id, SiegeWeaponSpotUpsertDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var spot = await _repo.GetSpotByIdAsync(id)
                ?? throw new KeyNotFoundException($"SiegeWeaponSpot with id {id} not found.");
            if (dto.SiegeScenarioId is > 0 && dto.SiegeScenarioId.Value != spot.SiegeScenarioId)
                throw new ArgumentException("A weapon spot can't be moved to another scenario; delete it and add it there instead.");

            var scenario = await RequireScenarioAsync(spot.SiegeScenarioId);
            // #988: the region before this save, replaced when the region task was run again.
            var previousRegionId = spot.WgRegionId;
            await ApplySpotAsync(spot, dto, scenario);
            if (dto.SortOrder.HasValue)
            {
                if (dto.SortOrder.Value < 0) throw new ArgumentException("sortOrder can't be negative.");
                spot.SortOrder = dto.SortOrder.Value;
            }
            await _repo.UpdateSpotAsync(spot);
            await FinalizeDrawnRegionAsync(spot, previousRegionId);
        }

        public async Task DeleteSpotAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var spot = await _repo.GetSpotByIdAsync(id)
                ?? throw new KeyNotFoundException($"SiegeWeaponSpot with id {id} not found.");
            await _repo.DeleteSpotAsync(spot);
        }

        private async Task ApplySpotAsync(SiegeWeaponSpot target, SiegeWeaponSpotUpsertDto dto, SiegeScenario scenario)
        {
            var name = RequireText(dto.Name, "Name", 100);
            if (dto.SiegeTeamId <= 0) throw new ArgumentException("A weapon spot needs a team.");
            if (scenario.Teams.All(t => t.Id != dto.SiegeTeamId))
                throw new ArgumentException("siegeTeamId must be a team of this scenario.");
            if (!Enum.IsDefined(dto.Facing)) throw new ArgumentException($"Unknown facing '{dto.Facing}'.");
            if (!Enum.IsDefined(dto.SizeClass)) throw new ArgumentException($"Unknown size class '{dto.SizeClass}'.");
            var region = OptionalText(dto.WgRegionId, "wgRegionId", 255);

            target.Name = name;
            target.SiegeTeamId = dto.SiegeTeamId;
            target.WgRegionId = region;
            target.Facing = dto.Facing;
            target.SizeClass = await SizeClassAsync(region, dto.Facing, dto.SizeClass, scenario.Id);
        }

        /// <summary>
        /// #988 (W70, A02, § 4.3): the size class from the region's bounds, which the game server is asked for, and the
        /// facing (width across x length along x height; a polygon counts as its bounding box). A region smaller than the
        /// small class, or one the game server says doesn't exist, is refused. Open point 1 of #988: when the game server
        /// can't be asked (off, not connected, no answer in time) the given class stays and the save goes on; L3 (#784)
        /// works the class out again from the region before it places a weapon. Without a region the given class stays.
        /// </summary>
        private async Task<SiegeWeaponSizeClass> SizeClassAsync(string? region, SiegeWeaponFacing facing,
            SiegeWeaponSizeClass given, int siegeScenarioId)
        {
            if (region == null || _regions == null) return given;
            var world = await _repo.GetScenarioWorldAsync(siegeScenarioId);
            var answer = await _regions.GetGeometryAsync(region, world);
            if (answer.Status == RegionQueryStatus.Unavailable)
            {
                _logger?.LogWarning("Weapon spot region {RegionId}: size class not checked ({Reason}); kept {SizeClass}",
                    region, answer.Message, given);
                return given;
            }
            if (answer.Status == RegionQueryStatus.NotFound || answer.Geometry == null)
                throw new ArgumentException(answer.Message);
            var g = answer.Geometry;
            var derived = SiegeWeaponSpotSizing.ClassFor(g.MinX, g.MinY, g.MinZ, g.MaxX, g.MaxY, g.MaxZ, facing);
            if (derived == null)
            {
                var (width, length, height) = SiegeWeaponSpotSizing.Footprint(g.MinX, g.MinY, g.MinZ, g.MaxX, g.MaxY, g.MaxZ, facing);
                throw new ArgumentException($"The region is too small for a weapon facing {facing}: " +
                    $"{width} wide x {length} long x {height} high, the small class needs at least " +
                    $"{SiegeWeaponSpotSizing.Describe(SiegeWeaponSizeClass.Small)}.");
            }
            return derived.Value;
        }

        /// <summary>
        /// #988: a region drawn through the spot form's region task is still <c>tempregion_worldtask_&lt;n&gt;</c>, which the
        /// plugin's temp-region cleanup would delete; it becomes <c>siege_spot_&lt;id&gt;</c> now, as lootbox spawn areas do
        /// (#34). A failed rename never fails the save: the spot keeps the temp name, which the cleanup keeps, and
        /// <c>POST api/Regions/finalize-temp-names</c> or the next save tries again. Deleting a spot leaves its region
        /// behind until the generic region lifecycle (#989).
        /// </summary>
        private async Task FinalizeDrawnRegionAsync(SiegeWeaponSpot spot, string? previousRegionId)
        {
            if (_regionNames == null || !DomainRegionNameFinalizer.IsTemporary(spot.WgRegionId)) return;
            await _regionNames.FinalizeSiegeWeaponSpotAsync(spot, previousRegionId);
        }

        private async Task<SiegeScenario> RequireScenarioAsync(int siegeScenarioId)
        {
            if (siegeScenarioId <= 0) throw new KeyNotFoundException($"SiegeScenario with id {siegeScenarioId} not found.");
            return await _repo.GetScenarioWithTeamsAndSpotsAsync(siegeScenarioId)
                ?? throw new KeyNotFoundException($"SiegeScenario with id {siegeScenarioId} not found.");
        }

        // ==== Unlocks ====

        public async Task<List<UserSiegeWeaponUnlockReadDto>> GetUnlocksForUserAsync(int userId)
        {
            if (userId <= 0) throw new KeyNotFoundException($"User with id {userId} not found.");
            return _mapper.Map<List<UserSiegeWeaponUnlockReadDto>>(await _repo.GetUnlocksForUserAsync(userId));
        }

        public async Task<UserSiegeWeaponUnlockReadDto?> GetUnlockByIdAsync(int id)
        {
            if (id <= 0) return null;
            var unlock = await _repo.GetUnlockByIdAsync(id);
            return unlock == null ? null : _mapper.Map<UserSiegeWeaponUnlockReadDto>(unlock);
        }

        public async Task<UserSiegeWeaponUnlockReadDto> CreateUnlockAsync(UserSiegeWeaponUnlockUpsertDto dto)
        {
            var unlock = new UserSiegeWeaponUnlock();
            await ApplyUnlockAsync(unlock, dto, existingId: null);
            await _repo.AddUnlockAsync(unlock);
            var created = await _repo.GetUnlockByIdAsync(unlock.Id);
            return _mapper.Map<UserSiegeWeaponUnlockReadDto>(created ?? unlock);
        }

        public async Task UpdateUnlockAsync(int id, UserSiegeWeaponUnlockUpsertDto dto)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var unlock = await _repo.GetUnlockByIdAsync(id)
                ?? throw new KeyNotFoundException($"UserSiegeWeaponUnlock with id {id} not found.");
            await ApplyUnlockAsync(unlock, dto, existingId: id);
            await _repo.UpdateUnlockAsync(unlock);
        }

        public async Task DeleteUnlockAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var unlock = await _repo.GetUnlockByIdAsync(id)
                ?? throw new KeyNotFoundException($"UserSiegeWeaponUnlock with id {id} not found.");
            await _repo.DeleteUnlockAsync(unlock);
        }

        public async Task<PagedResultDto<UserSiegeWeaponUnlockReadDto>> SearchUnlocksAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));
            var result = await _repo.SearchUnlocksAsync(_mapper.Map<PagedQuery>(queryDto));
            return _mapper.Map<PagedResultDto<UserSiegeWeaponUnlockReadDto>>(result);
        }

        private async Task ApplyUnlockAsync(UserSiegeWeaponUnlock target, UserSiegeWeaponUnlockUpsertDto dto, int? existingId)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (dto.UserId <= 0) throw new ArgumentException("An unlock needs a player.");
            if (dto.SiegeWeaponTemplateId <= 0) throw new ArgumentException("An unlock needs a weapon template.");
            if (!await _repo.UserExistsAsync(dto.UserId)) throw new ArgumentException($"User with id {dto.UserId} not found.");
            if (await _repo.GetTemplateByIdAsync(dto.SiegeWeaponTemplateId) == null)
                throw new ArgumentException($"SiegeWeaponTemplate with id {dto.SiegeWeaponTemplateId} not found.");

            // W32: one permanent unlock per player and weapon type.
            var existing = await _repo.GetUnlockAsync(dto.UserId, dto.SiegeWeaponTemplateId);
            if (existing != null && existing.Id != existingId)
                throw new InvalidOperationException("This player has already unlocked that weapon type.");

            target.UserId = dto.UserId;
            target.SiegeWeaponTemplateId = dto.SiegeWeaponTemplateId;
            target.UnlockedAt = dto.UnlockedAt?.ToUniversalTime() ?? (existingId.HasValue ? target.UnlockedAt : DateTime.UtcNow);
        }

        // ==== Helpers ====

        private static int NextSortOrder(IEnumerable<int> existing)
        {
            var list = existing.ToList();
            return list.Count == 0 ? 0 : list.Max() + 1;
        }

        private static string RequireText(string? value, string field, int maxLength)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrEmpty(trimmed)) throw new ArgumentException($"{field} is required.");
            if (trimmed.Length > maxLength) throw new ArgumentException($"{field} can be at most {maxLength} characters.");
            return trimmed;
        }

        private static string? OptionalText(string? value, string field, int maxLength)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrEmpty(trimmed)) return null;
            if (trimmed.Length > maxLength) throw new ArgumentException($"{field} can be at most {maxLength} characters.");
            return trimmed;
        }
    }
}
