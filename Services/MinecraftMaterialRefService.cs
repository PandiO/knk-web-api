using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.MinecraftCatalog;

namespace knkwebapi_v2.Services
{
    public class MinecraftMaterialRefService : IMinecraftMaterialRefService
    {
        private readonly IMinecraftMaterialRefRepository _repo;
        private readonly IMapper _mapper;

        public MinecraftMaterialRefService(IMinecraftMaterialRefRepository repo, IMapper mapper)
        {
            _repo = repo;
            _mapper = mapper;
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

            var entity = MinecraftMaterialRefFactory.NewUnverified(key, category);
            entity.LegacyName = legacyName;

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

        public async Task<PagedResultDto<MinecraftMaterialRefListDto>> SearchAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));
            var query = _mapper.Map<PagedQuery>(queryDto);
            var result = await _repo.SearchAsync(query);
            return _mapper.Map<PagedResultDto<MinecraftMaterialRefListDto>>(result);
        }

        public async Task<PagedResultDto<MinecraftMaterialRefListDto>> SearchPickerAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));

            // KNG-200: the database is the whole catalogue (the server scan fills it) - kind filter, legacy
            // categoryFilter values mapped to a kind, Missing rows hidden, sorted by display name by default.
            var query = _mapper.Map<PagedQuery>(queryDto);
            query.Filters = MinecraftPickerFilters.ForDatabase(query.Filters);
            if (string.IsNullOrWhiteSpace(query.SortBy)) query.SortBy = "displayName";
            var result = await _repo.SearchAsync(query);
            return _mapper.Map<PagedResultDto<MinecraftMaterialRefListDto>>(result);
        }
    }
}
