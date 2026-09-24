using System.Threading.Tasks;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces;

public interface ISalaryConfigurationRepository
{
    Task<SalaryConfiguration?> GetSingletonAsync();
    Task<SalaryConfiguration> UpsertAsync(SalaryConfiguration configuration);
}
