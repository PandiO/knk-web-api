using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories;

// The SiegeConfiguration singleton row ("global") - SalaryConfigurationRepository's shape.
public class SiegeConfigurationRepository : ISiegeConfigurationRepository
{
    private readonly KnKDbContext _context;

    public SiegeConfigurationRepository(KnKDbContext context)
    {
        _context = context;
    }

    public async Task<SiegeConfiguration?> GetSingletonAsync()
    {
        return await _context.SiegeConfigurations.FirstOrDefaultAsync(c => c.Id == SiegeConfiguration.SingletonId);
    }

    public async Task AddAsync(SiegeConfiguration configuration)
    {
        configuration.Id = SiegeConfiguration.SingletonId;
        configuration.CreatedAt = DateTime.UtcNow;
        configuration.UpdatedAt = DateTime.UtcNow;
        _context.SiegeConfigurations.Add(configuration);
        await _context.SaveChangesAsync();
    }

    public async Task SaveAsync(SiegeConfiguration configuration)
    {
        configuration.UpdatedAt = DateTime.UtcNow;
        if (_context.Entry(configuration).State == EntityState.Detached) _context.SiegeConfigurations.Update(configuration);
        await _context.SaveChangesAsync();
    }
}
