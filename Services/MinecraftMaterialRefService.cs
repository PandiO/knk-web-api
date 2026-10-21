using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
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

            // KNG-200 phase 1: one row per normalised key; a second one is a 409 that names the existing row.
            var key = MinecraftNamespaceKey.Normalize(dto.NamespaceKey);
            var existing = await _repo.GetByNamespaceKeyAsync(key);
            if (existing != null) throw new MinecraftMaterialRefConflictException(key, existing.Id);

            var entity = _mapper.Map<MinecraftMaterialRef>(dto);
            entity.NamespaceKey = key;
            entity.DisplayName ??= MinecraftNamespaceKey.DisplayNameFromKey(key);
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

            try
            {
                await _repo.AddAsync(entity);
            }
            catch (DbUpdateException)
            {
                // Two requests created the same key at once: the unique index kept the other one.
                _repo.Detach(entity);
                var winner = await _repo.GetByNamespaceKeyAsync(key);
                if (winner == null) throw;
                return _mapper.Map<MinecraftMaterialRefDto>(winner);
            }
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

            var key = MinecraftNamespaceKey.Normalize(dto.NamespaceKey);
            var other = await _repo.GetByNamespaceKeyAsync(key);
            if (other != null && other.Id != id) throw new MinecraftMaterialRefConflictException(key, other.Id);

            existing.NamespaceKey = key;
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

            // KNG-200 phase 1: saved rows match catalogue entries by normalised key ("STONE" is minecraft:stone),
            // and saved rows the catalogue doesn't have pass the same search and category filter.
            var dbItems = (await _repo.GetAllAsync()).ToList();
            var dbByKey = dbItems
                .GroupBy(m => MinecraftNamespaceKey.TryNormalize(m.NamespaceKey) ?? m.NamespaceKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderBy(m => m.Id).First(), StringComparer.OrdinalIgnoreCase);

            var merged = new List<MinecraftHybridMaterialOptionDto>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cat in _catalog.Search(search, category))
            {
                var key = MinecraftNamespaceKey.TryNormalize(cat.NamespaceKey) ?? cat.NamespaceKey;
                if (!seen.Add(key)) continue;
                dbByKey.TryGetValue(key, out var match);

                merged.Add(new MinecraftHybridMaterialOptionDto
                {
                    Id = match?.Id,
                    NamespaceKey = match?.NamespaceKey ?? cat.NamespaceKey,
                    Category = cat.Category ?? match?.Category ?? string.Empty,
                    LegacyName = match?.LegacyName ?? cat.LegacyName,
                    DisplayName = cat.DisplayName ?? match?.DisplayName ?? key,
                    IsPersisted = match != null,
                    IconUrl = match?.IconUrlOverride ?? match?.IconUrl ?? cat.IconUrl
                });
            }

            foreach (var (key, db) in dbByKey)
            {
                if (seen.Contains(key) || !MatchesSearch(db, search)) continue;
                if (!string.IsNullOrWhiteSpace(category) && !string.Equals(db.Category, category, StringComparison.OrdinalIgnoreCase)) continue;
                merged.Add(new MinecraftHybridMaterialOptionDto
                {
                    Id = db.Id,
                    NamespaceKey = db.NamespaceKey,
                    Category = db.Category,
                    LegacyName = db.LegacyName,
                    DisplayName = db.DisplayName ?? MinecraftNamespaceKey.DisplayNameFromKey(key),
                    IsPersisted = true,
                    IconUrl = db.IconUrlOverride ?? db.IconUrl
                });
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

        private static bool MatchesSearch(MinecraftMaterialRef material, string? search)
        {
            if (string.IsNullOrWhiteSpace(search)) return true;
            var term = search.Trim();
            return material.NamespaceKey.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (material.DisplayName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (material.LegacyName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
        }

        /// <summary>Sorts merged hybrid rows as the table asked; unsorted keeps saved rows first, then by name.</summary>
        private static IEnumerable<MinecraftHybridMaterialOptionDto> SortHybrid(
            IEnumerable<MinecraftHybridMaterialOptionDto> rows, string? sortBy, bool descending)
        {
            Func<MinecraftHybridMaterialOptionDto, object?>? key = sortBy?.Trim().ToLowerInvariant() switch
            {
                "namespacekey" => r => r.NamespaceKey,
                "displayname" or "name" => r => r.DisplayName,
                "category" => r => r.Category,
                "legacyname" => r => r.LegacyName,
                "id" => r => r.Id,
                _ => null,
            };
            if (key == null) return rows;
            var comparer = Comparer<object?>.Create((a, b) => a is string x && b is string y
                ? StringComparer.OrdinalIgnoreCase.Compare(x, y)
                : Comparer<object?>.Default.Compare(a, b));
            return descending ? rows.OrderByDescending(key, comparer) : rows.OrderBy(key, comparer);
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
            
            // Get ALL hybrid results to calculate total count, sorted as the table asked (KNG-200 phase 1)
            var allHybridResults = SortHybrid(await GetHybridAsync(search, category, null), queryDto.SortBy, queryDto.SortDescending).ToList();
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
                    // KNG-200 phase 1: an unsaved catalogue row has no id (null), not 0.
                    Id = h.Id,
                    NamespaceKey = h.NamespaceKey,
                    Category = h.Category,
                    LegacyName = h.LegacyName,
                    DisplayName = h.DisplayName,
                    IsPersisted = h.IsPersisted,
                    IconUrl = h.IconUrl
                }).ToList(),
                TotalCount = totalCount,
                PageNumber = queryDto.PageNumber,
                PageSize = queryDto.PageSize
            };
        }
    }
}
