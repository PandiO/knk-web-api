using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    // Singleton upsert, same shape as SalaryConfigurationService: the row is created on first read if the seed
    // hasn't made it yet.
    public class LootboxConfigurationService : ILootboxConfigurationService
    {
        private const int MaxTemplateLength = 512;

        private readonly ILootboxConfigurationRepository _repository;
        private readonly IMapper _mapper;

        public LootboxConfigurationService(ILootboxConfigurationRepository repository, IMapper mapper)
        {
            _repository = repository;
            _mapper = mapper;
        }

        public async Task<LootboxConfigurationDto> GetAsync()
        {
            return _mapper.Map<LootboxConfigurationDto>(await EnsureExistsAsync());
        }

        public async Task<LootboxConfigurationDto> UpdateAsync(UpdateLootboxConfigurationDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (dto.GlobalMaxActive < 0) throw new ArgumentException("globalMaxActive can't be negative.", nameof(dto));
            if (dto.MaxClaimsPerPlayerPerDay is < 1)
                throw new ArgumentException("maxClaimsPerPlayerPerDay must be empty (no cap) or at least 1.", nameof(dto));
            if (dto.AnnounceMinItemStars < 1 || dto.AnnounceMinItemStars > 11)
                throw new ArgumentException("announceMinItemStars must be 1-11 (11 = never).", nameof(dto));
            if (dto.AnnounceSpawnMinBoxStars < 1 || dto.AnnounceSpawnMinBoxStars > 11)
                throw new ArgumentException("announceSpawnMinBoxStars must be 1-11 (6 or more = off while boxes are ★1-5).", nameof(dto));

            var existing = await EnsureExistsAsync();
            existing.Enabled = dto.Enabled;
            existing.GlobalMaxActive = dto.GlobalMaxActive;
            existing.MaxClaimsPerPlayerPerDay = dto.MaxClaimsPerPlayerPerDay;
            existing.AnnounceMinItemStars = dto.AnnounceMinItemStars;
            existing.AnnounceSpawnMinBoxStars = dto.AnnounceSpawnMinBoxStars;
            existing.DropAnnouncementTemplate = Template(dto.DropAnnouncementTemplate, LootboxConfiguration.DefaultDropAnnouncementTemplate, "dropAnnouncementTemplate");
            existing.SpawnAnnouncementTemplate = Template(dto.SpawnAnnouncementTemplate, LootboxConfiguration.DefaultSpawnAnnouncementTemplate, "spawnAnnouncementTemplate");

            return _mapper.Map<LootboxConfigurationDto>(await _repository.UpsertAsync(existing));
        }

        // Blank = back to the default template.
        private static string Template(string? value, string fallback, string field)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            if (value.Length > MaxTemplateLength) throw new ArgumentException($"{field} can be at most {MaxTemplateLength} characters.");
            return value;
        }

        private async Task<LootboxConfiguration> EnsureExistsAsync()
        {
            return await _repository.GetSingletonAsync()
                ?? await _repository.UpsertAsync(new LootboxConfiguration());
        }
    }
}
