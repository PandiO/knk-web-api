using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    public class ItemBlueprintService : IItemBlueprintService
    {
        private readonly IItemBlueprintRepository _repo;
        private readonly IMinecraftMaterialRefRepository _materialRepo;
        private readonly IMinecraftMaterialCatalogService _materialCatalog;
        private readonly IEnchantmentDefinitionRepository _enchantmentRepo;
        private readonly ICategoryRepository _categoryRepo;
        private readonly IGradeRepository _gradeRepo;
        private readonly ITagRepository _tagRepo;
        private readonly IDomainRepository _domainRepo;
        private readonly IMapper _mapper;

        public ItemBlueprintService(
            IItemBlueprintRepository repo,
            IMinecraftMaterialRefRepository materialRepo,
            IMinecraftMaterialCatalogService materialCatalog,
            IEnchantmentDefinitionRepository enchantmentRepo,
            ICategoryRepository categoryRepo,
            IGradeRepository gradeRepo,
            ITagRepository tagRepo,
            IDomainRepository domainRepo,
            IMapper mapper)
        {
            _repo = repo;
            _materialRepo = materialRepo;
            _materialCatalog = materialCatalog;
            _enchantmentRepo = enchantmentRepo;
            _categoryRepo = categoryRepo;
            _gradeRepo = gradeRepo;
            _tagRepo = tagRepo;
            _domainRepo = domainRepo;
            _mapper = mapper;
        }

        public async Task<IEnumerable<ItemBlueprintReadDto>> GetAllAsync()
        {
            var items = await _repo.GetAllAsync();
            return _mapper.Map<IEnumerable<ItemBlueprintReadDto>>(items);
        }

        public async Task<ItemBlueprintReadDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var entity = await _repo.GetByIdAsync(id);
            return _mapper.Map<ItemBlueprintReadDto>(entity);
        }

        public async Task<ItemBlueprintReadDto> CreateAsync(ItemBlueprintCreateDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Name is required.", nameof(dto));

            dto.Description ??= string.Empty;
            dto.DefaultDisplayName ??= string.Empty;
            dto.DefaultDisplayDescription ??= string.Empty;

            // Ensure icon material ref exists
            var iconMaterialRefId = await EnsureIconMaterialRefAsync(dto.IconMaterialRefId, dto.IconNamespaceKey);

            await ValidateCategoryAndGradeAsync(dto.CategoryId, dto.GradeId);

            var entity = _mapper.Map<ItemBlueprint>(dto);
            entity.IconMaterialRefId = iconMaterialRefId;

            // Handle default enchantments (Many-to-Many)
            if (dto.DefaultEnchantments != null && dto.DefaultEnchantments.Any())
            {
                entity.DefaultEnchantments = new List<ItemBlueprintDefaultEnchantment>();
                foreach (var enchDto in dto.DefaultEnchantments)
                {
                    // Validate enchantment definition exists
                    var enchDef = await _enchantmentRepo.GetByIdAsync(enchDto.EnchantmentDefinitionId);
                    if (enchDef == null)
                        throw new ArgumentException($"EnchantmentDefinition with id {enchDto.EnchantmentDefinitionId} not found.");

                    entity.DefaultEnchantments.Add(new ItemBlueprintDefaultEnchantment
                    {
                        EnchantmentDefinitionId = enchDto.EnchantmentDefinitionId,
                        Level = enchDto.Level
                    });
                }
            }

            entity.Tags = new List<ItemBlueprintTag>();
            await AddTagsAsync(entity, dto.Tags);

            entity.Origins = new List<ItemBlueprintOrigin>();
            await AddOriginsAsync(entity, dto.Origins);

            await _repo.AddAsync(entity);
            return _mapper.Map<ItemBlueprintReadDto>(entity);
        }

        public async Task UpdateAsync(int id, ItemBlueprintUpdateDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Name is required.", nameof(dto));

            dto.Description ??= string.Empty;
            dto.DefaultDisplayName ??= string.Empty;
            dto.DefaultDisplayDescription ??= string.Empty;

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"ItemBlueprint with id {id} not found.");

            // Ensure icon material ref exists
            var iconMaterialRefId = await EnsureIconMaterialRefAsync(dto.IconMaterialRefId, dto.IconNamespaceKey, existing.IconMaterialRefId);

            await ValidateCategoryAndGradeAsync(dto.CategoryId, dto.GradeId);

            // Update scalar properties
            existing.Name = dto.Name;
            existing.Description = dto.Description;
            existing.IconMaterialRefId = iconMaterialRefId;
            existing.DefaultDisplayName = dto.DefaultDisplayName;
            existing.DefaultDisplayDescription = dto.DefaultDisplayDescription;
            existing.DefaultQuantity = dto.DefaultQuantity;
            existing.MaxStackSize = dto.MaxStackSize;
            existing.CategoryId = dto.CategoryId;
            existing.GradeId = dto.GradeId;
            existing.BasePriceMin = dto.BasePriceMin;
            existing.BasePriceMax = dto.BasePriceMax;

            // Update default enchantments (cascade M2M)
            existing.DefaultEnchantments.Clear();
            if (dto.DefaultEnchantments != null && dto.DefaultEnchantments.Any())
            {
                foreach (var enchDto in dto.DefaultEnchantments)
                {
                    var enchDef = await _enchantmentRepo.GetByIdAsync(enchDto.EnchantmentDefinitionId);
                    if (enchDef == null)
                        throw new ArgumentException($"EnchantmentDefinition with id {enchDto.EnchantmentDefinitionId} not found.");

                    existing.DefaultEnchantments.Add(new ItemBlueprintDefaultEnchantment
                    {
                        ItemBlueprintId = existing.Id,
                        EnchantmentDefinitionId = enchDto.EnchantmentDefinitionId,
                        Level = enchDto.Level
                    });
                }
            }

            existing.Tags.Clear();
            await AddTagsAsync(existing, dto.Tags);

            existing.Origins.Clear();
            await AddOriginsAsync(existing, dto.Origins);

            await _repo.UpdateAsync(existing);
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"ItemBlueprint with id {id} not found.");

            await _repo.DeleteAsync(id);
        }

        public async Task<PagedResultDto<ItemBlueprintListDto>> SearchAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));

            var query = _mapper.Map<PagedQuery>(queryDto);
            var result = await _repo.SearchAsync(query);
            return _mapper.Map<PagedResultDto<ItemBlueprintListDto>>(result);
        }

        private async Task ValidateCategoryAndGradeAsync(int? categoryId, int? gradeId)
        {
            if (categoryId.HasValue && categoryId > 0)
            {
                var category = await _categoryRepo.GetByIdAsync(categoryId.Value);
                if (category == null)
                    throw new ArgumentException($"Category with id {categoryId} not found.");
            }

            if (gradeId.HasValue && gradeId > 0)
            {
                var grade = await _gradeRepo.GetByIdAsync(gradeId.Value);
                if (grade == null)
                    throw new ArgumentException($"Grade with id {gradeId} not found.");
            }
        }

        private async Task AddTagsAsync(ItemBlueprint entity, List<ItemBlueprintTagDto>? tags)
        {
            if (tags == null || !tags.Any()) return;

            foreach (var tagId in tags.Select(t => t.TagId).Distinct())
            {
                var tag = await _tagRepo.GetByIdAsync(tagId);
                if (tag == null)
                    throw new ArgumentException($"Tag with id {tagId} not found.");

                entity.Tags.Add(new ItemBlueprintTag
                {
                    ItemBlueprintId = entity.Id,
                    TagId = tagId
                });
            }
        }

        private async Task AddOriginsAsync(ItemBlueprint entity, List<ItemBlueprintOriginCreateDto>? origins)
        {
            if (origins == null || !origins.Any()) return;

            var sequenceNumbers = new HashSet<int>();
            foreach (var originDto in origins)
            {
                var domain = await _domainRepo.GetByIdAsync(originDto.DomainId);
                if (domain == null)
                    throw new ArgumentException($"Domain with id {originDto.DomainId} not found.");

                if (!sequenceNumbers.Add(originDto.SequenceNumber))
                    throw new ArgumentException($"Duplicate SequenceNumber {originDto.SequenceNumber} in Origins - sequence numbers must be unique per ItemBlueprint.");

                entity.Origins.Add(new ItemBlueprintOrigin
                {
                    ItemBlueprintId = entity.Id,
                    DomainId = originDto.DomainId,
                    SequenceNumber = originDto.SequenceNumber
                });
            }
        }

        private async Task<int?> EnsureIconMaterialRefAsync(int? explicitId, string? namespaceKey, int? currentId = null)
        {
            // If explicit ID provided, validate and return
            if (explicitId.HasValue && explicitId > 0)
            {
                var material = await _materialRepo.GetByIdAsync(explicitId.Value);
                if (material == null)
                    throw new ArgumentException($"MinecraftMaterialRef with id {explicitId} not found.");
                return explicitId;
            }

            // If no namespace key, keep existing or null
            if (string.IsNullOrWhiteSpace(namespaceKey))
                return currentId;

            var key = namespaceKey.Trim();

            // Check if material already exists
            var existing = await _materialRepo.GetByNamespaceKeyAsync(key);
            if (existing != null)
                return existing.Id;

            // Look up catalog info
            var catalogEntry = _materialCatalog.Search(key, null)
                .FirstOrDefault(c => c.NamespaceKey.Equals(key, StringComparison.OrdinalIgnoreCase));

            var newMaterial = new MinecraftMaterialRef
            {
                NamespaceKey = key,
                Category = catalogEntry?.Category ?? "Uncategorized",
                LegacyName = catalogEntry?.LegacyName,
                IconUrl = catalogEntry?.IconUrl
            };

            await _materialRepo.AddAsync(newMaterial);
            return newMaterial.Id;
        }
    }
}
