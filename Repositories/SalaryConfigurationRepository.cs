using System;
using System.Threading.Tasks;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories;

public class SalaryConfigurationRepository : ISalaryConfigurationRepository
{
    private readonly KnKDbContext _context;

    public SalaryConfigurationRepository(KnKDbContext context)
    {
        _context = context;
    }

    public async Task<SalaryConfiguration?> GetSingletonAsync()
    {
        return await _context.SalaryConfigurations.FirstOrDefaultAsync(sc => sc.Id == "global");
    }

    public async Task<SalaryConfiguration> UpsertAsync(SalaryConfiguration configuration)
    {
        var existing = await GetSingletonAsync();

        if (existing == null)
        {
            configuration.Id = "global";
            configuration.CreatedAt = DateTime.UtcNow;
            configuration.UpdatedAt = DateTime.UtcNow;
            _context.SalaryConfigurations.Add(configuration);
            await _context.SaveChangesAsync();
            return configuration;
        }

        existing.GlobalMultiplier = configuration.GlobalMultiplier;
        existing.UpdatedAt = DateTime.UtcNow;

        _context.SalaryConfigurations.Update(existing);
        await _context.SaveChangesAsync();

        return existing;
    }
}
