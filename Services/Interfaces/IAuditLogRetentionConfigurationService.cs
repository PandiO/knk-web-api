using System.Threading.Tasks;
using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces;

public interface IAuditLogRetentionConfigurationService
{
    Task<AuditLogRetentionConfigurationDto> GetAsync();
    Task<AuditLogRetentionConfigurationDto> UpdateAsync(UpdateAuditLogRetentionConfigurationDto dto);
}
