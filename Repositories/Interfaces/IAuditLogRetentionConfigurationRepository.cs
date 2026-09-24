using System.Threading.Tasks;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces;

public interface IAuditLogRetentionConfigurationRepository
{
    Task<AuditLogRetentionConfiguration?> GetSingletonAsync();
    Task<AuditLogRetentionConfiguration> UpsertAsync(AuditLogRetentionConfiguration configuration);
}
