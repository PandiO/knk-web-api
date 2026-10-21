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
    public class MinecraftBlockRefService : IMinecraftBlockRefService
    {
        private readonly IMinecraftBlockRefRepository _repo;
        private readonly IMapper _mapper;
        private readonly IMinecraftMaterialCatalogService _catalog;
        private readonly IMinecraftCatalogPickerSource _pickerSource;
        private readonly IMinecraftMaterialRefRepository _materialRepo;

        public MinecraftBlockRefService(IMinecraftBlockRefRepository repo, IMapper mapper, IMinecraftMaterialCatalogService catalog,
            IMinecraftCatalogPickerSource pickerSource, IMinecraftMaterialRefRepository materialRepo)
        {
            _repo = repo;
            _mapper = mapper;
            _catalog = catalog;
            _pickerSource = pickerSource;
            _materialRepo = materialRepo;
        }

        public async Task<IEnumerable<MinecraftBlockRefDto>> GetAllAsync()
        {
            var items = await _repo.GetAllAsync();
            return _mapper.Map<IEnumerable<MinecraftBlockRefDto>>(items);
        }

        public async Task<MinecraftBlockRefDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var entity = await _repo.GetByIdAsync(id);
            return _mapper.Map<MinecraftBlockRefDto>(entity);
        }

        public async Task<MinecraftBlockRefDto> CreateAsync(MinecraftBlockRefCreateDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var (canonical, stateKey, material) = await ResolveAsync(dto.NamespaceKey, dto.BlockStateString);

            // KNG-200 phase 1: one row per block state; a second one is a 409 that names the existing row.
            var existing = await _repo.GetByStateKeyAsync(stateKey);
            if (existing != null) throw new MinecraftBlockRefConflictException(canonical.State ?? canonical.NamespaceKey, existing.Id);

            var entity = NewBlockRef(canonical, stateKey, material, dto.LogicalType, dto.IconUrl);
            await _repo.AddAsync(entity);
            return _mapper.Map<MinecraftBlockRefDto>(entity);
        }

        public async Task<MinecraftBlockRefDto> GetOrCreateAsync(MinecraftBlockRefCreateDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var (canonical, stateKey, material) = await ResolveAsync(dto.NamespaceKey, dto.BlockStateString);

            var existing = await _repo.GetByStateKeyAsync(stateKey);
            if (existing != null) return _mapper.Map<MinecraftBlockRefDto>(existing);

            var entity = NewBlockRef(canonical, stateKey, material, dto.LogicalType, dto.IconUrl);
            await _repo.AddAsync(entity);
            return _mapper.Map<MinecraftBlockRefDto>(entity);
        }

        public async Task UpdateAsync(int id, MinecraftBlockRefUpdateDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"MinecraftBlockRef with id {id} not found.");

            var (canonical, stateKey, material) = await ResolveAsync(dto.NamespaceKey, dto.BlockStateString);
            var other = await _repo.GetByStateKeyAsync(stateKey);
            if (other != null && other.Id != id) throw new MinecraftBlockRefConflictException(canonical.State ?? canonical.NamespaceKey, other.Id);

            existing.NamespaceKey = canonical.NamespaceKey;
            existing.BlockStateString = canonical.State;
            existing.StateKey = stateKey;
            existing.MaterialRefId = material.Id;
            existing.LogicalType = dto.LogicalType;
            existing.IconUrl = dto.IconUrl;

            await _repo.UpdateAsync(existing);
        }

        /// <summary>The canonical state, its identity and its material (created Unverified when missing) - KNG-200 §4.</summary>
        private async Task<(MinecraftBlockState.Canonical Canonical, string StateKey, MinecraftMaterialRef Material)> ResolveAsync(
            string? namespaceKey, string? blockStateString)
        {
            if (string.IsNullOrWhiteSpace(namespaceKey)) throw new ArgumentException("NamespaceKey is required.", nameof(namespaceKey));
            var canonical = MinecraftBlockState.TryCanonicalize(namespaceKey, blockStateString)
                ?? throw new ArgumentException($"\"{namespaceKey}\" with state \"{blockStateString}\" is not a valid block key and state.");

            var material = await _materialRepo.GetByNamespaceKeyAsync(canonical.NamespaceKey);
            if (material == null)
            {
                var entry = _catalog.GetAll().FirstOrDefault(e => string.Equals(
                    MinecraftNamespaceKey.TryNormalize(e.NamespaceKey), canonical.NamespaceKey, StringComparison.Ordinal));
                material = MinecraftMaterialRefFactory.NewUnverified(canonical.NamespaceKey, entry, "BLOCK");
                await _materialRepo.AddAsync(material);
            }
            return (canonical, MinecraftBlockState.StateKey(canonical), material);
        }

        private static MinecraftBlockRef NewBlockRef(MinecraftBlockState.Canonical canonical, string stateKey, MinecraftMaterialRef material,
            string? logicalType, string? iconUrl) => new()
        {
            NamespaceKey = canonical.NamespaceKey,
            BlockStateString = canonical.State,
            StateKey = stateKey,
            MaterialRefId = material.Id,
            LogicalType = logicalType,
            IconUrl = iconUrl,
        };

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"MinecraftBlockRef with id {id} not found.");

            await _repo.DeleteAsync(id);
        }

        public async Task<IEnumerable<MinecraftHybridBlockOptionDto>> GetHybridAsync(string? search = null, string? category = null, int? take = null)
        {
            // KNG-200 phase 1: a catalogue entry matches the saved "any state" row of its key (normalised); every
            // saved state is its own row (keying states by block key threw on two states of one block), and saved
            // rows pass the same search and category filter as the catalogue.
            var dbItems = (await _repo.GetAllAsync()).ToList();
            static string KeyOf(MinecraftBlockRef b) => MinecraftNamespaceKey.TryNormalize(b.NamespaceKey) ?? b.NamespaceKey;
            var anyStateByKey = dbItems
                .Where(b => string.IsNullOrWhiteSpace(b.BlockStateString))
                .GroupBy(KeyOf, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderBy(b => b.Id).First(), StringComparer.OrdinalIgnoreCase);
            var catalogByKey = _catalog.GetAll()
                .GroupBy(e => MinecraftNamespaceKey.TryNormalize(e.NamespaceKey) ?? e.NamespaceKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var merged = new List<MinecraftHybridBlockOptionDto>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var shownIds = new HashSet<int>();
            foreach (var cat in _catalog.Search(search, category))
            {
                var key = MinecraftNamespaceKey.TryNormalize(cat.NamespaceKey) ?? cat.NamespaceKey;
                if (!seenKeys.Add(key)) continue;
                anyStateByKey.TryGetValue(key, out var match);
                if (match != null) shownIds.Add(match.Id);

                merged.Add(new MinecraftHybridBlockOptionDto
                {
                    Id = match?.Id,
                    NamespaceKey = match?.NamespaceKey ?? cat.NamespaceKey,
                    BlockStateString = null,
                    LogicalType = match?.LogicalType,
                    DisplayName = cat.DisplayName ?? key,
                    IsPersisted = match != null,
                    IconUrl = match?.IconUrl ?? cat.IconUrl
                });
            }

            foreach (var db in dbItems)
            {
                if (shownIds.Contains(db.Id) || !MatchesSearch(db, search)) continue;
                var key = KeyOf(db);
                catalogByKey.TryGetValue(key, out var entry);
                if (!string.IsNullOrWhiteSpace(category) && entry != null
                    && !string.Equals(entry.Category, category, StringComparison.OrdinalIgnoreCase)) continue;
                merged.Add(new MinecraftHybridBlockOptionDto
                {
                    Id = db.Id,
                    NamespaceKey = db.NamespaceKey,
                    BlockStateString = db.BlockStateString,
                    LogicalType = db.LogicalType,
                    DisplayName = entry?.DisplayName ?? MinecraftNamespaceKey.DisplayNameFromKey(key),
                    IsPersisted = true,
                    IconUrl = db.IconUrl ?? entry?.IconUrl
                });
            }

            var ordered = merged
                .OrderByDescending(x => x.IsPersisted)
                .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.BlockStateString, StringComparer.Ordinal)
                .AsEnumerable();

            if (take.HasValue && take.Value > 0)
            {
                ordered = ordered.Take(take.Value);
            }

            return ordered.ToList();
        }

        private static bool MatchesSearch(MinecraftBlockRef block, string? search)
        {
            if (string.IsNullOrWhiteSpace(search)) return true;
            var term = search.Trim();
            return block.NamespaceKey.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (block.BlockStateString?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (block.LogicalType?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
        }

        /// <summary>Sorts merged hybrid rows as the table asked; unsorted keeps saved rows first, then by name.</summary>
        private static IEnumerable<MinecraftHybridBlockOptionDto> SortHybrid(
            IEnumerable<MinecraftHybridBlockOptionDto> rows, string? sortBy, bool descending)
        {
            Func<MinecraftHybridBlockOptionDto, object?>? key = sortBy?.Trim().ToLowerInvariant() switch
            {
                "namespacekey" => r => r.NamespaceKey,
                "displayname" or "name" => r => r.DisplayName,
                "blockstatestring" => r => r.BlockStateString,
                "logicaltype" => r => r.LogicalType,
                "id" => r => r.Id,
                _ => null,
            };
            if (key == null) return rows;
            var comparer = Comparer<object?>.Create((a, b) => a is string x && b is string y
                ? StringComparer.OrdinalIgnoreCase.Compare(x, y)
                : Comparer<object?>.Default.Compare(a, b));
            return descending ? rows.OrderByDescending(key, comparer) : rows.OrderBy(key, comparer);
        }

        public async Task<PagedResultDto<MinecraftBlockRefListDto>> SearchAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));

            var query = _mapper.Map<PagedQuery>(queryDto);
            var result = await _repo.SearchAsync(query);
            return _mapper.Map<PagedResultDto<MinecraftBlockRefListDto>>(result);
        }

        public async Task<PagedResultDto<MinecraftBlockRefListDto>> SearchHybridAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));

            // KNG-200 phase 4: once a server scan has completed, block refs are searched in the database only;
            // the merge below keyed states by block key and threw on two states of one block.
            if (await _pickerSource.UseDatabaseAsync())
            {
                var query = _mapper.Map<PagedQuery>(queryDto);
                query.Filters?.Remove("SearchHybrid");
                var result = await _repo.SearchAsync(query);
                return _mapper.Map<PagedResultDto<MinecraftBlockRefListDto>>(result);
            }

            // Extract filter parameters
            var search = queryDto.SearchTerm;
            var category = queryDto.Filters?.TryGetValue("category", out var cat) == true ? cat?.ToString() : null;
            
            // Get ALL hybrid results (unfiltered) to calculate total count
            var allHybridResults = SortHybrid(await GetHybridAsync(search, category, null), queryDto.SortBy, queryDto.SortDescending).ToList();
            var totalCount = allHybridResults.Count;
            
            // Apply pagination
            var skip = (queryDto.PageNumber - 1) * queryDto.PageSize;
            var pagedResults = allHybridResults
                .Skip(skip)
                .Take(queryDto.PageSize)
                .ToList();

            // Convert to PagedResultDto format
            return new PagedResultDto<MinecraftBlockRefListDto>
            {
                Items = pagedResults.Select(h => new MinecraftBlockRefListDto
                {
                    // KNG-200 phase 1: an unsaved catalogue row has no id (null), not 0.
                    Id = h.Id,
                    NamespaceKey = h.NamespaceKey,
                    BlockStateString = h.BlockStateString,
                    LogicalType = h.LogicalType,
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
