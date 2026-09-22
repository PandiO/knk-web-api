using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    public class CategoryService : ICategoryService
    {
        private readonly ICategoryRepository _repo;
        private readonly IMinecraftMaterialRefRepository _materialRepo;
        private readonly IMinecraftMaterialCatalogService _catalog;
        private readonly ITagRepository _tagRepo;
        private readonly IMapper _mapper;

        public CategoryService(
            ICategoryRepository repo,
            IMinecraftMaterialRefRepository materialRepo,
            IMinecraftMaterialCatalogService catalog,
            ITagRepository tagRepo,
            IMapper mapper)
        {
            _repo = repo;
            _materialRepo = materialRepo;
            _catalog = catalog;
            _tagRepo = tagRepo;
            _mapper = mapper;
        }

        public async Task<IEnumerable<CategoryDto>> GetAllAsync()
        {
            var categories = await _repo.GetAllAsync();
            return _mapper.Map<IEnumerable<CategoryDto>>(categories);
        }

        public async Task<CategoryDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var category = await _repo.GetByIdAsync(id);
            return _mapper.Map<CategoryDto>(category);
        }

        public async Task<IEnumerable<CategoryDto>> GetChildrenAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var children = await _repo.GetChildrenAsync(id);
            return _mapper.Map<IEnumerable<CategoryDto>>(children);
        }

        public async Task<CategoryDto> CreateAsync(CategoryDto categoryDto)
        {
            if (categoryDto == null) throw new ArgumentNullException(nameof(categoryDto));
            if (string.IsNullOrWhiteSpace(categoryDto.Name)) throw new ArgumentException("Category name is required.", nameof(categoryDto));

            // Validate parent category if provided
            if (categoryDto.ParentCategoryId.HasValue && categoryDto.ParentCategoryId > 0)
            {
                var parentExists = await _repo.GetByIdAsync(categoryDto.ParentCategoryId.Value);
                if (parentExists == null)
                    throw new ArgumentException($"Parent Category with id {categoryDto.ParentCategoryId} not found.", nameof(categoryDto));
            }

            var iconMaterialRefId = await EnsureIconMaterialRefAsync(categoryDto);

            var category = _mapper.Map<Category>(categoryDto);
            category.IconMaterialRefId = iconMaterialRefId;

            category.Tags = new List<CategoryTag>();
            await AddTagsAsync(category, categoryDto.Tags);

            await _repo.AddCategoryAsync(category);
            return _mapper.Map<CategoryDto>(category);
        }

        public async Task UpdateAsync(int id, CategoryDto categoryDto)
        {
            if (categoryDto == null) throw new ArgumentNullException(nameof(categoryDto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            if (string.IsNullOrWhiteSpace(categoryDto.Name)) throw new ArgumentException("Category name is required.", nameof(categoryDto));
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"Category with id {id} not found.");

            // Validate parent category if changed
            if (categoryDto.ParentCategoryId.HasValue && categoryDto.ParentCategoryId > 0 &&
                (!existing.ParentCategoryId.HasValue || existing.ParentCategoryId != categoryDto.ParentCategoryId))
            {
                // Prevent circular reference (parent cannot be the category itself)
                if (categoryDto.ParentCategoryId == id)
                    throw new ArgumentException("A category cannot be its own parent.", nameof(categoryDto));

                var parentExists = await _repo.GetByIdAsync(categoryDto.ParentCategoryId.Value);
                if (parentExists == null)
                    throw new ArgumentException($"Parent Category with id {categoryDto.ParentCategoryId} not found.", nameof(categoryDto));
            }

            var iconMaterialRefId = await EnsureIconMaterialRefAsync(categoryDto, existing.IconMaterialRefId);

            // Apply allowed updates
            existing.Name = categoryDto.Name;
            existing.IconMaterialRefId = iconMaterialRefId;
            existing.ParentCategoryId = categoryDto.ParentCategoryId;

            existing.Tags.Clear();
            await AddTagsAsync(existing, categoryDto.Tags);

            await _repo.UpdateCategoryAsync(existing);
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"Category with id {id} not found.");

            // Prevent deleting a category that has children; surface a clear error
            if (await _repo.HasChildrenAsync(id))
            {
                throw new InvalidOperationException("Cannot delete category with child categories. Reassign or delete children first.");
            }

            await _repo.DeleteCategoryAsync(id);
        }

        public async Task<PagedResultDto<CategoryListDto>> SearchAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));

            var query = _mapper.Map<PagedQuery>(queryDto);

            // Execute search
            var result = await _repo.SearchAsync(query);

            // Map result to DTO
            var resultDto = _mapper.Map<PagedResultDto<CategoryListDto>>(result);

            return resultDto;
        }

        private async Task AddTagsAsync(Category entity, List<CategoryTagDto>? tags)
        {
            if (tags == null || !tags.Any()) return;

            foreach (var tagId in tags.Select(t => t.TagId).Distinct())
            {
                var tag = await _tagRepo.GetByIdAsync(tagId);
                if (tag == null)
                    throw new ArgumentException($"Tag with id {tagId} not found.");

                entity.Tags.Add(new CategoryTag
                {
                    CategoryId = entity.Id,
                    TagId = tagId
                });
            }
        }

        private async Task<int?> EnsureIconMaterialRefAsync(CategoryDto categoryDto, int? currentIconId = null)
        {
            // If an explicit ID is provided, validate it and return
            if (categoryDto.IconMaterialRefId.HasValue && categoryDto.IconMaterialRefId > 0)
            {
                var icon = await _materialRepo.GetByIdAsync(categoryDto.IconMaterialRefId.Value);
                if (icon == null)
                    throw new ArgumentException($"MinecraftMaterialRef with id {categoryDto.IconMaterialRefId} not found.", nameof(categoryDto));

                return categoryDto.IconMaterialRefId;
            }

            // If no namespace key provided, keep existing or null
            if (string.IsNullOrWhiteSpace(categoryDto.IconNamespaceKey))
            {
                return currentIconId;
            }

            var namespaceKey = categoryDto.IconNamespaceKey.Trim();

            // Check if a material already exists for this namespace key
            var existing = await _materialRepo.GetByNamespaceKeyAsync(namespaceKey);
            if (existing != null)
            {
                return existing.Id;
            }

            // Look up catalog info to seed category/legacy
            var catalogEntry = _catalog.Search(namespaceKey, null)
                .FirstOrDefault(c => c.NamespaceKey.Equals(namespaceKey, StringComparison.OrdinalIgnoreCase));

            var materialCategory = catalogEntry?.Category ?? "Uncategorized";
            var legacyName = catalogEntry?.LegacyName;

            var newMaterial = new MinecraftMaterialRef
            {
                NamespaceKey = namespaceKey,
                Category = materialCategory,
                LegacyName = legacyName
            };

            await _materialRepo.AddAsync(newMaterial);

            return newMaterial.Id;
        }
    }
}
