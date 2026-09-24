using System.Threading.Tasks;
using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces;

public interface ISalaryConfigurationService
{
    Task<SalaryConfigurationDto> GetAsync();
    Task<SalaryConfigurationDto> UpdateAsync(UpdateSalaryConfigurationDto dto);
}
