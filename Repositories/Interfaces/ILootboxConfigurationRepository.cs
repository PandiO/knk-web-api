using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    public interface ILootboxConfigurationRepository
    {
        Task<LootboxConfiguration?> GetSingletonAsync();
        Task<LootboxConfiguration> UpsertAsync(LootboxConfiguration configuration);
    }
}
