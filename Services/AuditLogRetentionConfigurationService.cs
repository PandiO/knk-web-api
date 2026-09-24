using System;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services;

public class AuditLogRetentionConfigurationService : IAuditLogRetentionConfigurationService
{
    private readonly IAuditLogRetentionConfigurationRepository _repository;

    public AuditLogRetentionConfigurationService(IAuditLogRetentionConfigurationRepository repository)
    {
        _repository = repository;
    }

    public async Task<AuditLogRetentionConfigurationDto> GetAsync()
    {
        var config = await EnsureExistsAsync();
        return ToDto(config);
    }

    public async Task<AuditLogRetentionConfigurationDto> UpdateAsync(UpdateAuditLogRetentionConfigurationDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        if (dto.RetentionDays < 1) throw new ArgumentException("retentionDays must be at least 1.", nameof(dto));

        var existing = await EnsureExistsAsync();
        existing.RetentionDays = dto.RetentionDays;

        var saved = await _repository.UpsertAsync(existing);
        return ToDto(saved);
    }

    private async Task<AuditLogRetentionConfiguration> EnsureExistsAsync()
    {
        var existing = await _repository.GetSingletonAsync();
        if (existing != null) return existing;

        return await _repository.UpsertAsync(new AuditLogRetentionConfiguration
        {
            Id = "global",
            RetentionDays = 180
        });
    }

    private static AuditLogRetentionConfigurationDto ToDto(AuditLogRetentionConfiguration config) => new()
    {
        RetentionDays = config.RetentionDays,
        UpdatedAt = DateTime.SpecifyKind(config.UpdatedAt, DateTimeKind.Utc)
    };
}
