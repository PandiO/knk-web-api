using System;
using System.Threading.Tasks;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories;

public class AuditLogRetentionConfigurationRepository : IAuditLogRetentionConfigurationRepository
{
    private readonly KnKDbContext _context;

    public AuditLogRetentionConfigurationRepository(KnKDbContext context)
    {
        _context = context;
    }

    public async Task<AuditLogRetentionConfiguration?> GetSingletonAsync()
    {
        return await _context.AuditLogRetentionConfigurations.FirstOrDefaultAsync(c => c.Id == "global");
    }

    public async Task<AuditLogRetentionConfiguration> UpsertAsync(AuditLogRetentionConfiguration configuration)
    {
        var existing = await GetSingletonAsync();

        if (existing == null)
        {
            configuration.Id = "global";
            configuration.CreatedAt = DateTime.UtcNow;
            configuration.UpdatedAt = DateTime.UtcNow;
            _context.AuditLogRetentionConfigurations.Add(configuration);
            await _context.SaveChangesAsync();
            return configuration;
        }

        existing.RetentionDays = configuration.RetentionDays;
        existing.UpdatedAt = DateTime.UtcNow;

        _context.AuditLogRetentionConfigurations.Update(existing);
        await _context.SaveChangesAsync();

        return existing;
    }
}
