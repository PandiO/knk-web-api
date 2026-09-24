using System;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services;

public class SalaryConfigurationService : ISalaryConfigurationService
{
    private readonly ISalaryConfigurationRepository _repository;

    public SalaryConfigurationService(ISalaryConfigurationRepository repository)
    {
        _repository = repository;
    }

    public async Task<SalaryConfigurationDto> GetAsync()
    {
        var config = await EnsureExistsAsync();
        return ToDto(config);
    }

    public async Task<SalaryConfigurationDto> UpdateAsync(UpdateSalaryConfigurationDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        if (dto.GlobalMultiplier < 0) throw new ArgumentException("globalMultiplier cannot be negative.", nameof(dto));

        var existing = await EnsureExistsAsync();
        existing.GlobalMultiplier = dto.GlobalMultiplier;

        var saved = await _repository.UpsertAsync(existing);
        return ToDto(saved);
    }

    private async Task<SalaryConfiguration> EnsureExistsAsync()
    {
        var existing = await _repository.GetSingletonAsync();
        if (existing != null) return existing;

        return await _repository.UpsertAsync(new SalaryConfiguration
        {
            Id = "global",
            GlobalMultiplier = 1.0m
        });
    }

    private static SalaryConfigurationDto ToDto(SalaryConfiguration config) => new()
    {
        GlobalMultiplier = config.GlobalMultiplier,
        UpdatedAt = DateTime.SpecifyKind(config.UpdatedAt, DateTimeKind.Utc)
    };
}
