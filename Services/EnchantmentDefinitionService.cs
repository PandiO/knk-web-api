using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.MinecraftCatalog;

namespace knkwebapi_v2.Services
{
    public class EnchantmentDefinitionService : IEnchantmentDefinitionService
    {
        private readonly IEnchantmentDefinitionRepository _repo;
        private readonly IMinecraftEnchantmentRefRepository _enchantmentRefRepo;
        private readonly IMapper _mapper;

        public EnchantmentDefinitionService(
            IEnchantmentDefinitionRepository repo,
            IMinecraftEnchantmentRefRepository enchantmentRefRepo,
            IMapper mapper)
        {
            _repo = repo;
            _enchantmentRefRepo = enchantmentRefRepo;
            _mapper = mapper;
        }

        public async Task<IEnumerable<EnchantmentDefinitionReadDto>> GetAllAsync()
        {
            var items = await _repo.GetAllAsync();
            return _mapper.Map<IEnumerable<EnchantmentDefinitionReadDto>>(items);
        }

        public async Task<EnchantmentDefinitionReadDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var entity = await _repo.GetByIdAsync(id);
            return _mapper.Map<EnchantmentDefinitionReadDto>(entity);
        }

        public async Task<EnchantmentDefinitionReadDto> CreateAsync(EnchantmentDefinitionCreateDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (string.IsNullOrWhiteSpace(dto.Key)) throw new ArgumentException("Key is required.", nameof(dto));
            if (string.IsNullOrWhiteSpace(dto.DisplayName)) throw new ArgumentException("DisplayName is required.", nameof(dto));

            ValidateAbilityInvariant(dto.IsCustom, dto.AbilityDefinition);
            ValidateCombatValues(dto.ProcChancePercent, dto.ProcChancePerLevelPercent, dto.CooldownSeconds,
                dto.EffectDurationSeconds, dto.LevelMultiplier);
            ValidateSecondaryCombatValues(dto.EffectLevel, dto.SecondaryEffectDurationSeconds);
            ValidateAreaCombatValues(dto.AreaDamage, dto.GateDamage, dto.DamageRadiusBlocks, dto.EffectRadiusBlocks);
            ValidateGateStrikeCombatValues(dto.GateDamageBonusPerLevel, dto.StrikeMultiplier, dto.StrikeMultiplierPerLevel,
                dto.CooldownPerLevelSeconds);

            // Ensure base enchantment ref exists
            var baseEnchantmentRefId = await EnsureEnchantmentRefAsync(dto.MinecraftEnchantmentRefId, dto.EnchantmentNamespaceKey, dto.IsCustom);

            var entity = _mapper.Map<EnchantmentDefinition>(dto);
            entity.MinecraftEnchantmentRefId = baseEnchantmentRefId;

            if (dto.AbilityDefinition != null)
            {
                entity.AbilityDefinition = _mapper.Map<AbilityDefinition>(dto.AbilityDefinition);
            }

            await _repo.AddAsync(entity);
            return _mapper.Map<EnchantmentDefinitionReadDto>(entity);
        }

        public async Task UpdateAsync(int id, EnchantmentDefinitionUpdateDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            if (string.IsNullOrWhiteSpace(dto.Key)) throw new ArgumentException("Key is required.", nameof(dto));
            if (string.IsNullOrWhiteSpace(dto.DisplayName)) throw new ArgumentException("DisplayName is required.", nameof(dto));

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"EnchantmentDefinition with id {id} not found.");

            ValidateAbilityInvariant(dto.IsCustom, dto.AbilityDefinition);
            ValidateCombatValues(dto.ProcChancePercent, dto.ProcChancePerLevelPercent, dto.CooldownSeconds,
                dto.EffectDurationSeconds, dto.LevelMultiplier);
            ValidateSecondaryCombatValues(dto.EffectLevel, dto.SecondaryEffectDurationSeconds);
            ValidateAreaCombatValues(dto.AreaDamage, dto.GateDamage, dto.DamageRadiusBlocks, dto.EffectRadiusBlocks);
            ValidateGateStrikeCombatValues(dto.GateDamageBonusPerLevel, dto.StrikeMultiplier, dto.StrikeMultiplierPerLevel,
                dto.CooldownPerLevelSeconds);

            // Ensure base enchantment ref exists
            var baseEnchantmentRefId = await EnsureEnchantmentRefAsync(dto.MinecraftEnchantmentRefId, dto.EnchantmentNamespaceKey, dto.IsCustom, existing.MinecraftEnchantmentRefId);

            // Update properties
            existing.Key = dto.Key;
            existing.DisplayName = dto.DisplayName;
            existing.Description = dto.Description;
            existing.IsCustom = dto.IsCustom;
            existing.MaxLevel = dto.MaxLevel;
            existing.MinecraftEnchantmentRefId = baseEnchantmentRefId;

            // #151: only the combat values the request contained (a form without them keeps the stored ones).
            if (dto.HasProcChancePercent) existing.ProcChancePercent = dto.ProcChancePercent;
            if (dto.HasProcChancePerLevelPercent) existing.ProcChancePerLevelPercent = dto.ProcChancePerLevelPercent;
            if (dto.HasCooldownEnabled) existing.CooldownEnabled = dto.CooldownEnabled;
            if (dto.HasCooldownSeconds) existing.CooldownSeconds = dto.CooldownSeconds;
            if (dto.HasEffectDurationSeconds) existing.EffectDurationSeconds = dto.EffectDurationSeconds;
            if (dto.HasLevelMultiplier) existing.LevelMultiplier = dto.LevelMultiplier;
            if (dto.HasEffectLevel) existing.EffectLevel = dto.EffectLevel;
            if (dto.HasSecondaryEffectDurationSeconds) existing.SecondaryEffectDurationSeconds = dto.SecondaryEffectDurationSeconds;
            if (dto.HasAreaDamage) existing.AreaDamage = dto.AreaDamage;
            if (dto.HasGateDamage) existing.GateDamage = dto.GateDamage;
            if (dto.HasDamageRadiusBlocks) existing.DamageRadiusBlocks = dto.DamageRadiusBlocks;
            if (dto.HasEffectRadiusBlocks) existing.EffectRadiusBlocks = dto.EffectRadiusBlocks;
            if (dto.HasGateDamageBonusPerLevel) existing.GateDamageBonusPerLevel = dto.GateDamageBonusPerLevel;
            if (dto.HasStrikeMultiplier) existing.StrikeMultiplier = dto.StrikeMultiplier;
            if (dto.HasStrikeMultiplierPerLevel) existing.StrikeMultiplierPerLevel = dto.StrikeMultiplierPerLevel;
            if (dto.HasCooldownPerLevelSeconds) existing.CooldownPerLevelSeconds = dto.CooldownPerLevelSeconds;

            if (!dto.IsCustom)
            {
                existing.AbilityDefinition = null;
            }
            else if (!dto.HasAbilityDefinition)
            {
                // KNG-195: not sent (the generic web form) - keep the custom enchantment's ability.
            }
            else if (dto.AbilityDefinition != null)
            {
                if (existing.AbilityDefinition == null)
                {
                    existing.AbilityDefinition = new AbilityDefinition
                    {
                        AbilityKey = dto.AbilityDefinition.AbilityKey,
                        RuntimeConfigJson = dto.AbilityDefinition.RuntimeConfigJson,
                        FutureUserAssignmentContract = dto.AbilityDefinition.FutureUserAssignmentContract
                    };
                }
                else
                {
                    existing.AbilityDefinition.AbilityKey = dto.AbilityDefinition.AbilityKey;
                    existing.AbilityDefinition.RuntimeConfigJson = dto.AbilityDefinition.RuntimeConfigJson;
                    existing.AbilityDefinition.FutureUserAssignmentContract = dto.AbilityDefinition.FutureUserAssignmentContract;
                }
            }
            else
            {
                existing.AbilityDefinition = null;
            }

            await _repo.UpdateAsync(existing);
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"EnchantmentDefinition with id {id} not found.");

            await _repo.DeleteAsync(id);
        }

        public async Task<PagedResultDto<EnchantmentDefinitionListDto>> SearchAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));

            var query = _mapper.Map<PagedQuery>(queryDto);
            var result = await _repo.SearchAsync(query);
            return _mapper.Map<PagedResultDto<EnchantmentDefinitionListDto>>(result);
        }

        private async Task<int?> EnsureEnchantmentRefAsync(int? explicitId, string? namespaceKey, bool isCustom, int? currentId = null)
        {
            // If explicit ID provided, validate and return
            if (explicitId.HasValue && explicitId > 0)
            {
                var enchRef = await _enchantmentRefRepo.GetByIdAsync(explicitId.Value);
                if (enchRef == null)
                    throw new ArgumentException($"MinecraftEnchantmentRef with id {explicitId} not found.");
                return explicitId;
            }

            // If no namespace key, keep existing or null
            if (string.IsNullOrWhiteSpace(namespaceKey))
                return currentId;

            // KNG-250: no catalogue lookup. A bare key of a custom definition belongs in the knk namespace; an unknown
            // key becomes an Unverified ref that the next server scan confirms, merges or marks Missing.
            var key = MinecraftEnchantmentKey.Normalize(namespaceKey, isCustom);

            // Check if enchantment ref already exists
            var existing = await _enchantmentRefRepo.GetByNamespaceKeyAsync(key);
            if (existing != null)
                return existing.Id;

            var newEnchRef = new MinecraftEnchantmentRef
            {
                NamespaceKey = key,
                DisplayName = MinecraftNamespaceKey.DisplayNameFromKey(key),
                IsCustom = MinecraftEnchantmentKey.IsCustomKey(key),
            };

            await _enchantmentRefRepo.AddAsync(newEnchRef);
            return newEnchRef.Id;
        }

        /// <summary>Upper bounds of the combat values (PandiO/knk-workspace#151); nulls are always allowed.</summary>
        public const int MaxCooldownSeconds = 86_400;
        public const double MaxEffectDurationSeconds = 3_600;
        public const double MaxLevelMultiplier = 10;
        public const int MaxEffectLevel = 10;
        public const double MaxAreaDamage = 1_000;
        public const double MaxGateDamage = 10_000;
        public const double MaxRadiusBlocks = 32;
        public const double MaxGateDamageBonusPerLevel = 10;
        public const double MaxStrikeMultiplier = 100;

        /// <summary>
        /// PandiO/knk-workspace#151: the combat values an admin may set on an enchantment. The chance per level may
        /// be negative (a weaker higher level is the admin's call); the plugin caps the resulting chance to 0-100 %.
        /// </summary>
        public static void ValidateCombatValues(double? procChancePercent, double? procChancePerLevelPercent,
            int? cooldownSeconds, double? effectDurationSeconds, double? levelMultiplier)
        {
            if (procChancePercent is < 0 or > 100 || (procChancePercent.HasValue && double.IsNaN(procChancePercent.Value)))
                throw new ArgumentException("Proc chance must be between 0 and 100 percent.");
            if (procChancePerLevelPercent is < -100 or > 100 || (procChancePerLevelPercent.HasValue && double.IsNaN(procChancePerLevelPercent.Value)))
                throw new ArgumentException("Proc chance per level must be between -100 and 100 percentage points.");
            if (cooldownSeconds is < 0 or > MaxCooldownSeconds)
                throw new ArgumentException($"Cooldown must be between 0 and {MaxCooldownSeconds} seconds.");
            if (effectDurationSeconds is < 0 or > MaxEffectDurationSeconds || (effectDurationSeconds.HasValue && double.IsNaN(effectDurationSeconds.Value)))
                throw new ArgumentException($"Effect duration must be between 0 and {MaxEffectDurationSeconds} seconds.");
            if (levelMultiplier is < 0 or > MaxLevelMultiplier || (levelMultiplier.HasValue && double.IsNaN(levelMultiplier.Value)))
                throw new ArgumentException($"Level multiplier must be between 0 and {MaxLevelMultiplier}.");
        }

        /// <summary>
        /// PandiO/knk-workspace#201: the effect level (Flash Chaos's Slowness, 1 = level I) and the second effect's
        /// duration (Flash Chaos's Nausea); nulls are always allowed.
        /// </summary>
        public static void ValidateSecondaryCombatValues(int? effectLevel, double? secondaryEffectDurationSeconds)
        {
            if (effectLevel is < 1 or > MaxEffectLevel)
                throw new ArgumentException($"Effect level must be between 1 and {MaxEffectLevel}.");
            if (secondaryEffectDurationSeconds is < 0 or > MaxEffectDurationSeconds
                || (secondaryEffectDurationSeconds.HasValue && double.IsNaN(secondaryEffectDurationSeconds.Value)))
                throw new ArgumentException($"Second effect duration must be between 0 and {MaxEffectDurationSeconds} seconds.");
        }

        /// <summary>
        /// PandiO/knk-workspace#223: the area blast of Chaos / Flash Chaos: damage to entities, damage to gates and the
        /// two radii in blocks; nulls are always allowed.
        /// </summary>
        public static void ValidateAreaCombatValues(double? areaDamage, double? gateDamage, double? damageRadiusBlocks,
            double? effectRadiusBlocks)
        {
            if (areaDamage is < 0 or > MaxAreaDamage || (areaDamage.HasValue && double.IsNaN(areaDamage.Value)))
                throw new ArgumentException($"Area damage must be between 0 and {MaxAreaDamage}.");
            if (gateDamage is < 0 or > MaxGateDamage || (gateDamage.HasValue && double.IsNaN(gateDamage.Value)))
                throw new ArgumentException($"Gate damage must be between 0 and {MaxGateDamage}.");
            if (damageRadiusBlocks is < 0 or > MaxRadiusBlocks || (damageRadiusBlocks.HasValue && double.IsNaN(damageRadiusBlocks.Value)))
                throw new ArgumentException($"Damage radius must be between 0 and {MaxRadiusBlocks} blocks.");
            if (effectRadiusBlocks is < 0 or > MaxRadiusBlocks || (effectRadiusBlocks.HasValue && double.IsNaN(effectRadiusBlocks.Value)))
                throw new ArgumentException($"Effect radius must be between 0 and {MaxRadiusBlocks} blocks.");
        }

        /// <summary>
        /// PandiO/knk-workspace#259: Cleaving's gate bonus per level (0-10), strike multiplier at level I (0-100) and per
        /// level (-100 to 100), and the strike cooldown's change per level in seconds (-86400 to 86400); nulls allowed.
        /// </summary>
        public static void ValidateGateStrikeCombatValues(double? gateDamageBonusPerLevel, double? strikeMultiplier,
            double? strikeMultiplierPerLevel, int? cooldownPerLevelSeconds)
        {
            if (gateDamageBonusPerLevel is < 0 or > MaxGateDamageBonusPerLevel
                || (gateDamageBonusPerLevel.HasValue && double.IsNaN(gateDamageBonusPerLevel.Value)))
                throw new ArgumentException($"Gate damage bonus per level must be between 0 and {MaxGateDamageBonusPerLevel}.");
            if (strikeMultiplier is < 0 or > MaxStrikeMultiplier || (strikeMultiplier.HasValue && double.IsNaN(strikeMultiplier.Value)))
                throw new ArgumentException($"Strike multiplier must be between 0 and {MaxStrikeMultiplier}.");
            if (strikeMultiplierPerLevel is < -MaxStrikeMultiplier or > MaxStrikeMultiplier
                || (strikeMultiplierPerLevel.HasValue && double.IsNaN(strikeMultiplierPerLevel.Value)))
                throw new ArgumentException($"Strike multiplier per level must be between -{MaxStrikeMultiplier} and {MaxStrikeMultiplier}.");
            if (cooldownPerLevelSeconds is < -MaxCooldownSeconds or > MaxCooldownSeconds)
                throw new ArgumentException($"Cooldown change per level must be between -{MaxCooldownSeconds} and {MaxCooldownSeconds} seconds.");
        }

        private static void ValidateAbilityInvariant(bool isCustom, AbilityDefinitionUpsertDto? abilityDefinition)
        {
            if (!isCustom && abilityDefinition != null)
            {
                throw new ArgumentException("AbilityDefinition is only allowed when IsCustom is true.");
            }

            if (abilityDefinition != null && string.IsNullOrWhiteSpace(abilityDefinition.AbilityKey))
            {
                throw new ArgumentException("AbilityDefinition.AbilityKey is required when ability metadata is provided.");
            }
        }
    }
}
