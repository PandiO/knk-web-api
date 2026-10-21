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

        public MinecraftBlockRefService(IMinecraftBlockRefRepository repo, IMapper mapper, IMinecraftMaterialCatalogService catalog,
            IMinecraftCatalogPickerSource pickerSource)
        {
            _repo = repo;
            _mapper = mapper;
            _catalog = catalog;
            _pickerSource = pickerSource;
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
            if (string.IsNullOrWhiteSpace(dto.NamespaceKey)) throw new ArgumentException("NamespaceKey is required.", nameof(dto));

            var entity = _mapper.Map<MinecraftBlockRef>(dto);
            await _repo.AddAsync(entity);
            return _mapper.Map<MinecraftBlockRefDto>(entity);
        }

        public async Task UpdateAsync(int id, MinecraftBlockRefUpdateDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            if (string.IsNullOrWhiteSpace(dto.NamespaceKey)) throw new ArgumentException("NamespaceKey is required.", nameof(dto));

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"MinecraftBlockRef with id {id} not found.");

            existing.NamespaceKey = dto.NamespaceKey;
            existing.BlockStateString = dto.BlockStateString;
            existing.LogicalType = dto.LogicalType;
            existing.IconUrl = dto.IconUrl;

            await _repo.UpdateAsync(existing);
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"MinecraftBlockRef with id {id} not found.");

            await _repo.DeleteAsync(id);
        }

        public async Task<IEnumerable<MinecraftHybridBlockOptionDto>> GetHybridAsync(string? search = null, string? category = null, int? take = null)
        {
            var dbItems = await _repo.GetAllAsync();
            var dbByKey = dbItems.ToDictionary(x => x.NamespaceKey, StringComparer.OrdinalIgnoreCase);

            var catalogItems = _catalog.Search(search, category);

            var merged = new List<MinecraftHybridBlockOptionDto>();

            foreach (var cat in catalogItems)
            {
                dbByKey.TryGetValue(cat.NamespaceKey, out var match);

                merged.Add(new MinecraftHybridBlockOptionDto
                {
                    Id = match?.Id,
                    NamespaceKey = cat.NamespaceKey,
                    BlockStateString = match?.BlockStateString,
                    LogicalType = match?.LogicalType,
                    DisplayName = cat.DisplayName ?? match?.NamespaceKey ?? cat.NamespaceKey,
                    IsPersisted = match != null,
                    IconUrl = cat.IconUrl
                });
            }

            // Include any DB entries not present in catalog (edge cases)
            foreach (var db in dbItems)
            {
                if (!merged.Any(x => x.NamespaceKey.Equals(db.NamespaceKey, StringComparison.OrdinalIgnoreCase)))
                {
                    merged.Add(new MinecraftHybridBlockOptionDto
                    {
                        Id = db.Id,
                        NamespaceKey = db.NamespaceKey,
                        BlockStateString = db.BlockStateString,
                        LogicalType = db.LogicalType,
                        DisplayName = db.NamespaceKey,
                        IsPersisted = true,
                        IconUrl = null
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
            var allHybridResults = (await GetHybridAsync(search, category, null)).ToList();
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
                    Id = h.Id ?? 0,
                    NamespaceKey = h.NamespaceKey,
                    BlockStateString = h.BlockStateString,
                    LogicalType = h.LogicalType,
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
