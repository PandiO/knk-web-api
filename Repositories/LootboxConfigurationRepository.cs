using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    // Singleton upsert, same shape as SalaryConfigurationRepository.
    public class LootboxConfigurationRepository : ILootboxConfigurationRepository
    {
        public const string SingletonId = "global";

        private readonly KnKDbContext _context;

        public LootboxConfigurationRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<LootboxConfiguration?> GetSingletonAsync()
        {
            return await _context.LootboxConfigurations.FirstOrDefaultAsync(c => c.Id == SingletonId);
        }

        public async Task<LootboxConfiguration> UpsertAsync(LootboxConfiguration configuration)
        {
            var existing = await GetSingletonAsync();

            if (existing == null)
            {
                configuration.Id = SingletonId;
                configuration.CreatedAt = DateTime.UtcNow;
                configuration.UpdatedAt = DateTime.UtcNow;
                _context.LootboxConfigurations.Add(configuration);
                await _context.SaveChangesAsync();
                return configuration;
            }

            existing.Enabled = configuration.Enabled;
            existing.GlobalMaxActive = configuration.GlobalMaxActive;
            existing.MaxClaimsPerPlayerPerDay = configuration.MaxClaimsPerPlayerPerDay;
            existing.AnnounceMinItemStars = configuration.AnnounceMinItemStars;
            existing.AnnounceSpawnMinBoxStars = configuration.AnnounceSpawnMinBoxStars;
            existing.DropAnnouncementTemplate = configuration.DropAnnouncementTemplate;
            existing.SpawnAnnouncementTemplate = configuration.SpawnAnnouncementTemplate;
            existing.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return existing;
        }
    }
}
