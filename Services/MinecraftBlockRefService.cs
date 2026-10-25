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
        private readonly IMinecraftMaterialRefRepository _materialRepo;

        public MinecraftBlockRefService(IMinecraftBlockRefRepository repo, IMapper mapper, IMinecraftMaterialRefRepository materialRepo)
        {
            _repo = repo;
            _mapper = mapper;
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
                material = MinecraftMaterialRefFactory.NewUnverified(canonical.NamespaceKey, "BLOCK");
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

        public async Task<PagedResultDto<MinecraftBlockRefListDto>> SearchAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));

            var query = _mapper.Map<PagedQuery>(queryDto);
            var result = await _repo.SearchAsync(query);
            return _mapper.Map<PagedResultDto<MinecraftBlockRefListDto>>(result);
        }

        public async Task<PagedResultDto<MinecraftBlockRefListDto>> SearchPickerAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));

            // KNG-200: block refs are always saved rows; the picker flags are not filters.
            var query = _mapper.Map<PagedQuery>(queryDto);
            query.Filters?.Remove(MinecraftPickerFilters.Picker);
            var result = await _repo.SearchAsync(query);
            return _mapper.Map<PagedResultDto<MinecraftBlockRefListDto>>(result);
        }
    }
}
