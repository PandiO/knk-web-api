using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces;

public interface ISiegeConfigurationService
{
    Task<SiegeConfigurationDto> GetAsync();
    Task<SiegeConfigurationDto> UpdateAsync(UpdateSiegeConfigurationDto dto);
}
