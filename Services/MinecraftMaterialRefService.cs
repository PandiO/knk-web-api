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
using knkwebapi_v2.Services.MinecraftCatalog;

namespace knkwebapi_v2.Services
{
    public class MinecraftMaterialRefService : IMinecraftMaterialRefService
    {
        private readonly IMinecraftMaterialRefRepository _repo;
        private readonly IMapper _mapper;
        private readonly IMinecraftMaterialCatalogService _catalog;
        private readonly IMinecraftCatalogPickerSource _pickerSource;

        public MinecraftMaterialRefService(
            IMinecraftMaterialRefRepository repo,
            IMapper mapper,
            IMinecraftMaterialCatalogService catalog,
            IMinecraftCatalogPickerSource pickerSource)
        {
            _repo = repo;
            _mapper = mapper;
            _catalog = catalog;
            _pickerSource = pickerSource;
        }

        public async Task<IEnumerable<MinecraftMaterialRefDto>> GetAllAsync()
        {
            var items = await _repo.GetAllAsync();
            return _mapper.Map<IEnumerable<MinecraftMaterialRefDto>>(items);
        }

        public async Task<MinecraftMaterialRefDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var entity = await _repo.GetByIdAsync(id);
            return _mapper.Map<MinecraftMaterialRefDto>(entity);
        }

        public async Task<MinecraftMaterialRefDto> CreateAsync(MinecraftMaterialRefCreateDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (string.IsNullOrWhiteSpace(dto.NamespaceKey)) throw new ArgumentException("NamespaceKey is required.", nameof(dto));
            if (string.IsNullOrWhiteSpace(dto.Category)) throw new ArgumentException("Category is required.", nameof(dto));

            var entity = _mapper.Map<MinecraftMaterialRef>(dto);
            await _repo.AddAsync(entity);
            return _mapper.Map<MinecraftMaterialRefDto>(entity);
        }

        public async Task<MinecraftMaterialRefDto> GetOrCreateAsync(string namespaceKey, string category, string? legacyName = null)
        {
            if (string.IsNullOrWhiteSpace(namespaceKey)) throw new ArgumentException("NamespaceKey is required.", nameof(namespaceKey));
            if (string.IsNullOrWhiteSpace(category)) throw new ArgumentException("Category is required.", nameof(category));

            // KNG-200 §3: one normalisation, so "STONE" finds minecraft:stone instead of creating a duplicate.
            var key = MinecraftNamespaceKey.Normalize(namespaceKey);
            var existing = await _repo.GetByNamespaceKeyAsync(key);
            if (existing != null)
            {
                return _mapper.Map<MinecraftMaterialRefDto>(existing);
            }

            var catalogEntry = _catalog.Search(null, null)
                .FirstOrDefault(x => x.NamespaceKey.Equals(key, StringComparison.OrdinalIgnoreCase));

            var entity = MinecraftMaterialRefFactory.NewUnverified(key, catalogEntry, category);
            entity.Category = category;
            entity.LegacyName = legacyName ?? entity.LegacyName;

            await _repo.AddAsync(entity);
            return _mapper.Map<MinecraftMaterialRefDto>(entity);
        }

        public async Task UpdateAsync(int id, MinecraftMaterialRefUpdateDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            if (string.IsNullOrWhiteSpace(dto.NamespaceKey)) throw new ArgumentException("NamespaceKey is required.", nameof(dto));
            if (string.IsNullOrWhiteSpace(dto.Category)) throw new ArgumentException("Category is required.", nameof(dto));

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"MinecraftMaterialRef with id {id} not found.");

            existing.NamespaceKey = dto.NamespaceKey;
            existing.LegacyName = dto.LegacyName;
            existing.Category = dto.Category;
            existing.IconUrl = dto.IconUrl;

            await _repo.UpdateAsync(existing);
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"MinecraftMaterialRef with id {id} not found.");

            await _repo.DeleteAsync(id);
        }

        public async Task<IEnumerable<MinecraftHybridMaterialOptionDto>> GetHybridAsync(string? search = null, string? category = null, int? take = null)
        {
            // KNG-200 phase 4: once a server scan has completed, the database is the whole catalogue.
            if (await _pickerSource.UseDatabaseAsync())
            {
                var filters = category == null ? null : new Dictionary<string, string> { ["category"] = category };
                var found = await _repo.SearchAsync(new PagedQuery
                {
                    PageNumber = 1,
                    PageSize = take is > 0 ? take.Value : 1000,
                    SearchTerm = search,
                    SortBy = "displayName",
                    Filters = MinecraftPickerFilters.ForDatabase(filters),
                });
                return found.Items.Select(m => new MinecraftHybridMaterialOptionDto
                {
                    Id = m.Id,
                    NamespaceKey = m.NamespaceKey,
                    Category = m.Category,
                    LegacyName = m.LegacyName,
                    DisplayName = m.DisplayName ?? MinecraftNamespaceKey.DisplayNameFromKey(m.NamespaceKey),
                    IsPersisted = true,
                    IconUrl = m.IconUrlOverride ?? m.IconUrl,
                }).ToList();
            }

            var dbItems = await _repo.GetAllAsync();
            var dbByKey = dbItems.ToDictionary(x => x.NamespaceKey, StringComparer.OrdinalIgnoreCase);

            var catalogItems = _catalog.Search(search, category);

            var merged = new List<MinecraftHybridMaterialOptionDto>();

            foreach (var cat in catalogItems)
            {
                dbByKey.TryGetValue(cat.NamespaceKey, out var match);

                merged.Add(new MinecraftHybridMaterialOptionDto
                {
                    Id = match?.Id,
                    NamespaceKey = cat.NamespaceKey,
                    Category = cat.Category ?? match?.Category ?? string.Empty,
                    LegacyName = match?.LegacyName ?? cat.LegacyName,
                    DisplayName = cat.DisplayName ?? match?.NamespaceKey ?? cat.NamespaceKey,
                    IsPersisted = match != null,
                    // Use persisted IconUrl if available, otherwise use catalog IconUrl
                    IconUrl = match?.IconUrlOverride ?? match?.IconUrl ?? cat.IconUrl
                });
            }

            // Include any DB entries not present in catalog (edge cases)
            foreach (var db in dbItems)
            {
                if (!merged.Any(x => x.NamespaceKey.Equals(db.NamespaceKey, StringComparison.OrdinalIgnoreCase)))
                {
                    merged.Add(new MinecraftHybridMaterialOptionDto
                    {
                        Id = db.Id,
                        NamespaceKey = db.NamespaceKey,
                        Category = db.Category,
                        LegacyName = db.LegacyName,
                        DisplayName = db.NamespaceKey,
                        IsPersisted = true,
                        IconUrl = db.IconUrlOverride ?? db.IconUrl
                    });
                }
            }

            var ordered = merged
                .OrderByDescending(x => x.IsPersisted)
                .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .AsEnumerable();

            if (take.HasValue && take.Value > 0)
            {
                ordered = ordered.Take(take.Value);
            }

            return ordered.ToList();
        }

        public async Task<PagedResultDto<MinecraftMaterialRefListDto>> SearchAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));

            var query = _mapper.Map<PagedQuery>(queryDto);
            var result = await _repo.SearchAsync(query);
            return _mapper.Map<PagedResultDto<MinecraftMaterialRefListDto>>(result);
        }

        public async Task<PagedResultDto<MinecraftMaterialRefListDto>> SearchHybridAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));

            // KNG-200 phase 4: once a server scan has completed, pickers search the database only - real
            // paging and sorting, kind filter, Missing rows hidden.
            if (await _pickerSource.UseDatabaseAsync())
            {
                var query = _mapper.Map<PagedQuery>(queryDto);
                query.Filters = MinecraftPickerFilters.ForDatabase(query.Filters);
                if (string.IsNullOrWhiteSpace(query.SortBy)) query.SortBy = "displayName";
                var result = await _repo.SearchAsync(query);
                return _mapper.Map<PagedResultDto<MinecraftMaterialRefListDto>>(result);
            }

            var search = queryDto.SearchTerm;
            var category = queryDto.Filters?.TryGetValue("category", out var cat) == true ? cat?.ToString() : null;
            
            // Get ALL hybrid results (unfiltered) to calculate total count
            var allHybridResults = (await GetHybridAsync(search, category, null)).ToList();
            var totalCount = allHybridResults.Count;
            
            // Apply pagination
            var skip = (queryDto.PageNumber - 1) * queryDto.PageSize;
            var pagedResults = allHybridResults
                .Skip(skip)
                .Take(queryDto.PageSize)
                .ToList();

            return new PagedResultDto<MinecraftMaterialRefListDto>
            {
                Items = pagedResults.Select(h => new MinecraftMaterialRefListDto
                {
                    Id = h.Id ?? 0,
                    NamespaceKey = h.NamespaceKey,
                    Category = h.Category,
                    LegacyName = h.LegacyName,
                    DisplayName = h.DisplayName,
                    IsPersisted = h.IsPersisted,
                    IconUrl = h.IconUrl
                }).ToList(),
                TotalCount = totalCount,
                PageNumber = queryDto.PageNumber,
                PageSize = pagedResults.Count
            };
        }
    }
}
