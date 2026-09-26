using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces;

public interface ISiegeConfigurationRepository
{
    Task<SiegeConfiguration?> GetSingletonAsync();
    Task AddAsync(SiegeConfiguration configuration);
    Task SaveAsync(SiegeConfiguration configuration);
}
