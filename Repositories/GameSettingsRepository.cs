using System;
using System.Threading.Tasks;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories;

public class GameSettingsRepository : IGameSettingsRepository
{
    private readonly KnKDbContext _context;

    public GameSettingsRepository(KnKDbContext context)
    {
        _context = context;
    }

    public async Task<GameSettings?> GetSingletonAsync()
    {
        return await _context.GameSettings.FirstOrDefaultAsync(gs => gs.Id == "global");
    }

    public async Task<GameSettings> UpsertAsync(GameSettings settings)
    {
        var existing = await GetSingletonAsync();

        if (existing == null)
        {
            settings.Id = "global";
            settings.CreatedAt = DateTime.UtcNow;
            settings.UpdatedAt = DateTime.UtcNow;
            _context.GameSettings.Add(settings);
            await _context.SaveChangesAsync();
            return settings;
        }

        existing.SettingsVersion = settings.SettingsVersion;
        existing.JoinAnnouncement = settings.JoinAnnouncement;
        existing.LeaveAnnouncement = settings.LeaveAnnouncement;
        existing.JoinSpawnMode = settings.JoinSpawnMode;
        existing.JoinSpawnReferenceJson = settings.JoinSpawnReferenceJson;
        existing.DefaultRespawnPolicyJson = settings.DefaultRespawnPolicyJson;
        existing.WorldSettingsJson = settings.WorldSettingsJson;
        existing.RuntimeWorldsJson = settings.RuntimeWorldsJson;
        existing.RuntimeWorldsLastUpdatedAt = settings.RuntimeWorldsLastUpdatedAt;
        // The service decides: a runtime-world report alone must not look like an admin edit.
        existing.UpdatedAt = settings.UpdatedAt;

        _context.GameSettings.Update(existing);
        await _context.SaveChangesAsync();

        return existing;
    }
}
