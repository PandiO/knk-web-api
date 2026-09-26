using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Lootbox;

namespace knkwebapi_v2.Services
{
    public class LootboxTypeService : ILootboxTypeService
    {
        private readonly ILootboxTypeRepository _repo;
        private readonly IMapper _mapper;

        public LootboxTypeService(ILootboxTypeRepository repo, IMapper mapper)
        {
            _repo = repo;
            _mapper = mapper;
        }

        // ===== CRUD (FormWizard) =====

        public async Task<IEnumerable<LootboxTypeDto>> GetAllAsync()
        {
            return _mapper.Map<IEnumerable<LootboxTypeDto>>(await _repo.GetAllAsync());
        }

        public async Task<LootboxTypeDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var type = await _repo.GetByIdAsync(id);
            return type == null ? null : _mapper.Map<LootboxTypeDto>(type);
        }

        public async Task<LootboxTypeDto> CreateAsync(LootboxTypeDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            await ValidateAsync(dto, null);

            var entity = _mapper.Map<LootboxType>(dto);
            entity.Name = dto.Name.Trim();
            ReplaceChildren(entity, dto);

            await _repo.AddAsync(entity);
            return _mapper.Map<LootboxTypeDto>(await _repo.GetByIdAsync(entity.Id) ?? entity);
        }

        public async Task UpdateAsync(int id, LootboxTypeDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));

            var existing = await _repo.GetByIdAsync(id) ?? throw new KeyNotFoundException($"LootboxType with id {id} not found.");
            await ValidateAsync(dto, id);

            existing.Name = dto.Name.Trim();
            existing.CategoryId = dto.CategoryId;
            existing.IncludeSubcategories = dto.IncludeSubcategories;
            existing.Enabled = dto.Enabled;
            existing.SpawnWeight = dto.SpawnWeight;
            existing.MinBoxStars = dto.MinBoxStars;
            existing.MaxBoxStars = dto.MaxBoxStars;
            existing.ItemStarSpread = dto.ItemStarSpread;
            existing.DisplayMaterialRefId = dto.DisplayMaterialRefId;
            existing.MaxClaimsPerPlayerPerDay = dto.MaxClaimsPerPlayerPerDay;
            existing.AnnounceMinItemStars = dto.AnnounceMinItemStars;
            ReplaceChildren(existing, dto);

            await _repo.UpdateAsync(existing);
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            if (await _repo.GetByIdAsync(id) == null) throw new KeyNotFoundException($"LootboxType with id {id} not found.");

            var blocker = await _repo.FindDeleteBlockerAsync(id);
            if (blocker != null) throw new LootboxConflictException("InUse", $"LootboxType {id} can't be deleted: {blocker}.");

            await _repo.DeleteAsync(id);
        }

        public async Task<PagedResultDto<LootboxTypeDto>> SearchAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));
            var result = await _repo.SearchAsync(_mapper.Map<PagedQuery>(queryDto));
            return _mapper.Map<PagedResultDto<LootboxTypeDto>>(result);
        }

        // The child rows are replaced wholesale on every save, like Kit.Contents.
        private static void ReplaceChildren(LootboxType entity, LootboxTypeDto dto)
        {
            entity.GradeWeights.Clear();
            foreach (var weight in dto.GradeWeights)
            {
                entity.GradeWeights.Add(new LootboxTypeGradeWeight { GradeId = weight.GradeId, Weight = weight.Weight });
            }

            entity.PoolEntries.Clear();
            foreach (var entry in dto.PoolEntries)
            {
                entity.PoolEntries.Add(new LootboxPoolEntry
                {
                    ItemBlueprintId = entry.ItemBlueprintId,
                    Mode = ParseMode(entry.Mode),
                    WeightOverride = entry.WeightOverride,
                    GradeIdOverride = entry.GradeIdOverride,
                });
            }

            entity.EnchantRolls.Clear();
            foreach (var roll in dto.EnchantRolls)
            {
                entity.EnchantRolls.Add(new LootboxEnchantRoll
                {
                    EnchantmentDefinitionId = roll.EnchantmentDefinitionId,
                    ChancePercent = roll.ChancePercent,
                    MinLevel = roll.MinLevel,
                    MaxLevel = roll.MaxLevel,
                    MinBoxStars = roll.MinBoxStars,
                    SortOrder = roll.SortOrder,
                });
            }
        }

        private static LootboxPoolMode ParseMode(string? mode) =>
            Enum.TryParse<LootboxPoolMode>(mode, true, out var parsed) && Enum.IsDefined(parsed)
                ? parsed
                : throw new ArgumentException($"Pool entry mode '{mode}' must be Include or Exclude.");

        // Validation (IMPLEMENTATION_PLAN.md Phase 1 "Validation (service)").
        private async Task ValidateAsync(LootboxTypeDto dto, int? id)
        {
            // A client may send the lists as null.
            dto.GradeWeights ??= new List<LootboxTypeGradeWeightDto>();
            dto.PoolEntries ??= new List<LootboxPoolEntryDto>();
            dto.EnchantRolls ??= new List<LootboxEnchantRollDto>();

            if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Name is required.", nameof(dto));
            if (dto.Name.Trim().Length > 128) throw new ArgumentException("Name can be at most 128 characters.", nameof(dto));
            if (dto.MinBoxStars < 1 || dto.MaxBoxStars > LootboxRollEngine.MaxBoxStars || dto.MinBoxStars > dto.MaxBoxStars)
                throw new ArgumentException($"Box stars must satisfy 1 <= minBoxStars <= maxBoxStars <= {LootboxRollEngine.MaxBoxStars}.", nameof(dto));
            if (dto.ItemStarSpread < 0 || dto.ItemStarSpread > 9)
                throw new ArgumentException("itemStarSpread must be 0-9.", nameof(dto));
            if (dto.SpawnWeight < 0) throw new ArgumentException("spawnWeight can't be negative.", nameof(dto));
            if (dto.MaxClaimsPerPlayerPerDay is < 1)
                throw new ArgumentException("maxClaimsPerPlayerPerDay must be empty or at least 1.", nameof(dto));
            if (dto.AnnounceMinItemStars is < 1 or > 10)
                throw new ArgumentException("announceMinItemStars must be empty or 1-10.", nameof(dto));

            var categories = await _repo.GetCategoriesAsync();
            if (categories.All(c => c.Id != dto.CategoryId))
                throw new ArgumentException($"Category with id {dto.CategoryId} not found.", nameof(dto));
            if (await _repo.CategoryTakenAsync(dto.CategoryId, id))
                throw new LootboxConflictException("CategoryTaken", $"Category {dto.CategoryId} already has a lootbox type (one per category).");

            if (dto.DisplayMaterialRefId is int materialId && !await _repo.MaterialExistsAsync(materialId))
                throw new ArgumentException($"MinecraftMaterialRef with id {materialId} not found.", nameof(dto));

            var gradeIds = (await _repo.GetGradesAsync()).Select(g => g.Id).ToHashSet();

            if (dto.GradeWeights.GroupBy(w => w.GradeId).Any(g => g.Count() > 1))
                throw new ArgumentException("Each grade can have one weight.", nameof(dto));
            foreach (var weight in dto.GradeWeights)
            {
                if (!gradeIds.Contains(weight.GradeId)) throw new ArgumentException($"Grade with id {weight.GradeId} not found.", nameof(dto));
                if (weight.Weight < 0m) throw new ArgumentException("Grade weights can't be negative.", nameof(dto));
            }

            if (dto.PoolEntries.GroupBy(p => p.ItemBlueprintId).Any(g => g.Count() > 1))
                throw new ArgumentException("Each blueprint can have one pool entry.", nameof(dto));
            var blueprintIds = await _repo.GetExistingBlueprintIdsAsync(dto.PoolEntries.Select(p => p.ItemBlueprintId));
            foreach (var entry in dto.PoolEntries)
            {
                ParseMode(entry.Mode);
                if (!blueprintIds.Contains(entry.ItemBlueprintId))
                    throw new ArgumentException($"ItemBlueprint with id {entry.ItemBlueprintId} not found.", nameof(dto));
                if (entry.WeightOverride is < 0m) throw new ArgumentException("Pool weight overrides can't be negative.", nameof(dto));
                if (entry.GradeIdOverride is int overrideId && !gradeIds.Contains(overrideId))
                    throw new ArgumentException($"Grade with id {overrideId} not found.", nameof(dto));
            }

            var definitions = await _repo.GetEnchantmentDefinitionsAsync(dto.EnchantRolls.Select(r => r.EnchantmentDefinitionId));
            foreach (var roll in dto.EnchantRolls)
            {
                if (!definitions.TryGetValue(roll.EnchantmentDefinitionId, out var definition))
                    throw new ArgumentException($"EnchantmentDefinition with id {roll.EnchantmentDefinitionId} not found.", nameof(dto));
                if (roll.ChancePercent < 0m || roll.ChancePercent > 100m)
                    throw new ArgumentException("Enchant roll chancePercent must be 0-100.", nameof(dto));
                if (roll.MinLevel < 1 || roll.MinLevel > roll.MaxLevel || roll.MaxLevel > definition.MaxLevel)
                    throw new ArgumentException(
                        $"Enchant roll levels for {definition.Key} must satisfy 1 <= minLevel <= maxLevel <= {definition.MaxLevel}.", nameof(dto));
                if (roll.MinBoxStars < 1 || roll.MinBoxStars > LootboxRollEngine.MaxBoxStars)
                    throw new ArgumentException($"Enchant roll minBoxStars must be 1-{LootboxRollEngine.MaxBoxStars}.", nameof(dto));
            }
        }

        // ===== Roll input and odds preview =====

        public async Task<LootboxRollInput?> BuildRollInputAsync(int typeId)
        {
            var type = await _repo.GetByIdAsync(typeId);
            return type == null ? null : await BuildRollInputAsync(type);
        }

        private async Task<LootboxRollInput> BuildRollInputAsync(LootboxType type)
        {
            var categories = await _repo.GetCategoriesAsync();
            var scope = LootboxRollInputBuilder.CategoryScope(type.CategoryId, type.IncludeSubcategories, categories);
            var includes = type.PoolEntries.Where(p => p.Mode == LootboxPoolMode.Include).Select(p => p.ItemBlueprintId).ToList();
            var blueprints = await _repo.GetPoolBlueprintsAsync(scope, includes);
            var specials = await _repo.GetApplicableSpecialsAsync(type.Id);
            var grades = await _repo.GetGradesAsync();
            return LootboxRollInputBuilder.Build(type, categories, grades, blueprints, specials);
        }

        public async Task<LootboxOddsDto?> GetOddsAsync(int id, int? boxStars)
        {
            var type = await _repo.GetByIdAsync(id);
            if (type == null) return null;

            var stars = boxStars ?? type.MaxBoxStars;
            if (stars < 1 || stars > LootboxRollEngine.MaxBoxStars)
                throw new ArgumentException($"boxStars must be 1-{LootboxRollEngine.MaxBoxStars}.", nameof(boxStars));

            var input = await BuildRollInputAsync(type);
            var odds = LootboxRollEngine.ComputeOdds(input, stars);
            var weightOverrides = type.GradeWeights.ToDictionary(w => w.GradeId, w => w.Weight);
            var boxGrades = LootboxRollEngine.BoxGradeDistribution(input.Grades, type.MinBoxStars, type.MaxBoxStars, weightOverrides);

            return new LootboxOddsDto
            {
                LootboxTypeId = type.Id,
                LootboxTypeName = type.Name,
                BoxStars = stars,
                BoxGrades = boxGrades.Select(g => new LootboxGradeOddsDto
                {
                    GradeId = g.Grade.Id, Name = g.Grade.Name, Stars = g.Grade.Stars, Percent = Percent(g.Probability),
                }).ToList(),
                Specials = odds.Specials.Select(s => new LootboxSpecialOddsDto
                {
                    SpecialEntryId = s.Special.EntryId,
                    ItemBlueprintId = s.Special.Item.BlueprintId,
                    Name = s.Special.Item.Name,
                    ChancePerMillion = s.Special.ChancePerMillion,
                    ChancePercent = Percent(s.Special.ChancePerMillion / 1_000_000.0),
                    Percent = Percent(s.Probability),
                }).ToList(),
                NormalRollPercent = Percent(odds.NormalRollProbability),
                WindowWidened = odds.WindowWidened,
                ItemGrades = odds.Grades.Select(g => new LootboxGradeOddsDto
                {
                    GradeId = g.Grade.Id, Name = g.Grade.Name, Stars = g.Grade.Stars, Percent = Percent(g.Probability), ItemCount = g.ItemCount,
                }).ToList(),
                Items = odds.Items.Select(i => new LootboxItemOddsDto
                {
                    ItemBlueprintId = i.Item.BlueprintId,
                    Name = i.Item.Name,
                    GradeId = i.Grade.Id,
                    Stars = i.Grade.Stars,
                    Weight = i.Item.Weight,
                    Quantity = i.Item.Quantity,
                    RollsEnchantments = LootboxRollEngine.CanRollEnchantments(i.Item),
                    PercentWithinGrade = Percent(i.ProbabilityWithinGrade),
                    Percent = Percent(i.Probability),
                }).ToList(),
                Enchantments = odds.Enchantments.Select(e => new LootboxEnchantOddsDto
                {
                    EnchantRollId = e.Roll.RollId,
                    EnchantmentDefinitionId = e.Roll.DefinitionId,
                    Key = e.Roll.Key,
                    IsCustom = e.Roll.IsCustom,
                    HitPercent = Percent(e.HitProbability),
                    MinLevel = e.Roll.MinLevel,
                    MaxLevel = e.Roll.MaxLevel,
                    LevelsByGrade = e.Levels.Select(l => new LootboxEnchantLevelRangeDto
                    {
                        GradeId = l.Grade.Id, Stars = l.Grade.Stars, MinLevel = l.MinLevel, MaxLevel = l.MaxLevel,
                    }).ToList(),
                }).ToList(),
            };
        }

        // 0-1 -> 0-100, rounded so 0.0005 (a 500-per-million special) survives.
        private static double Percent(double probability) => Math.Round(probability * 100, 6);
    }
}
