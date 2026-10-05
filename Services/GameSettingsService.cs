using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services;

public class GameSettingsService : IGameSettingsService
{
    private readonly IGameSettingsRepository _repository;
    private readonly IMapper _mapper;

    public GameSettingsService(IGameSettingsRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<GameSettingsReadDto> GetAsync()
    {
        var settings = await EnsureExistsAsync();
        return _mapper.Map<GameSettingsReadDto>(settings);
    }

    public async Task<GameSettingsReadDto> UpdateAsync(GameSettingsUpdateDto dto)
    {
        if (dto == null)
        {
            throw new ArgumentNullException(nameof(dto));
        }

        ValidateUpdate(dto);

        var existing = await EnsureExistsAsync();

        existing.SettingsVersion = string.IsNullOrWhiteSpace(dto.SettingsVersion) ? "1" : dto.SettingsVersion.Trim();
        existing.JoinAnnouncement = dto.JoinAnnouncement?.Trim() ?? string.Empty;
        existing.LeaveAnnouncement = dto.LeaveAnnouncement?.Trim() ?? string.Empty;
        existing.JoinSpawnMode = dto.JoinSpawnMode?.Trim() ?? "WorldSpawn";
        existing.JoinSpawnReferenceJson = GameSettingsJson.Serialize(dto.JoinSpawnReference);
        existing.DefaultRespawnPolicyJson = GameSettingsJson.Serialize(dto.DefaultRespawnPolicy);

        var cleanedWorldSettings = (dto.WorldSettings ?? new List<WorldGameSettingsDto>())
            .Where(ws => !string.IsNullOrWhiteSpace(ws.WorldName))
            .GroupBy(ws => ws.WorldName, StringComparer.OrdinalIgnoreCase)
            .Select(group => NormalizeWorldSettings(group.First()))
            .ToList();

        existing.WorldSettingsJson = GameSettingsJson.Serialize(cleanedWorldSettings);
        existing.UpdatedAt = DateTime.UtcNow;

        var saved = await _repository.UpsertAsync(existing);
        return _mapper.Map<GameSettingsReadDto>(saved);
    }

    public async Task<GameSettingsReadDto> UpdateRuntimeWorldsAsync(GameSettingsRuntimeWorldsUpdateDto dto)
    {
        if (dto == null)
        {
            throw new ArgumentNullException(nameof(dto));
        }

        var existing = await EnsureExistsAsync();

        var cleanedRuntimeWorlds = (dto.RuntimeWorlds ?? new List<MinecraftWorldRuntimeDto>())
            .Where(world => !string.IsNullOrWhiteSpace(world.WorldName))
            .GroupBy(world => world.WorldName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                first.WorldName = first.WorldName.Trim();
                first.FolderName = (first.FolderName ?? first.WorldName).Trim();
                first.Environment = (first.Environment ?? string.Empty).Trim();
                return first;
            })
            .ToList();

        var worldSettings = GameSettingsJson.DeserializeList<WorldGameSettingsDto>(existing.WorldSettingsJson);
        var worldSettingsBefore = existing.WorldSettingsJson;
        foreach (var runtimeWorld in cleanedRuntimeWorlds)
        {
            if (worldSettings.All(ws => !ws.WorldName.Equals(runtimeWorld.WorldName, StringComparison.OrdinalIgnoreCase)))
            {
                worldSettings.Add(new WorldGameSettingsDto
                {
                    WorldName = runtimeWorld.WorldName,
                    WorldFolderName = runtimeWorld.FolderName,
                    DefaultGameMode = "SURVIVAL",
                    LockTime = false,
                    LockedTime = 18000,
                    Weather = new WorldWeatherSettingsDto(),
                    RespawnPolicy = new RespawnPolicyDto()
                });
            }
            else
            {
                var existingWorldSetting = worldSettings.First(ws => ws.WorldName.Equals(runtimeWorld.WorldName, StringComparison.OrdinalIgnoreCase));
                existingWorldSetting.WorldFolderName = string.IsNullOrWhiteSpace(existingWorldSetting.WorldFolderName)
                    ? runtimeWorld.FolderName
                    : existingWorldSetting.WorldFolderName;
            }
        }

        existing.RuntimeWorldsJson = GameSettingsJson.Serialize(cleanedRuntimeWorlds);
        existing.RuntimeWorldsLastUpdatedAt = DateTime.UtcNow;
        existing.WorldSettingsJson = GameSettingsJson.Serialize(worldSettings.Select(NormalizeWorldSettings).ToList());
        // The plugin reports every 30 s; only a newly seen world (a new per-world entry) changes the
        // settings themselves, so only that moves UpdatedAt (KNG-52).
        if (!string.Equals(worldSettingsBefore, existing.WorldSettingsJson, StringComparison.Ordinal))
        {
            existing.UpdatedAt = DateTime.UtcNow;
        }

        var saved = await _repository.UpsertAsync(existing);
        return _mapper.Map<GameSettingsReadDto>(saved);
    }

    private async Task<GameSettings> EnsureExistsAsync()
    {
        var existing = await _repository.GetSingletonAsync();
        if (existing != null)
        {
            return existing;
        }

        var defaults = new GameSettings
        {
            Id = "global",
            SettingsVersion = "1",
            JoinAnnouncement = "&a{player} joined the server.",
            LeaveAnnouncement = "&e{player} left the server.",
            JoinSpawnMode = "WorldSpawn",
            JoinSpawnReferenceJson = null,
            DefaultRespawnPolicyJson = GameSettingsJson.Serialize(new RespawnPolicyDto()),
            WorldSettingsJson = GameSettingsJson.Serialize(new List<WorldGameSettingsDto>()),
            RuntimeWorldsJson = GameSettingsJson.Serialize(new List<MinecraftWorldRuntimeDto>()),
            RuntimeWorldsLastUpdatedAt = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        return await _repository.UpsertAsync(defaults);
    }

    private static void ValidateUpdate(GameSettingsUpdateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.JoinSpawnMode))
        {
            throw new ArgumentException("joinSpawnMode is required");
        }

        if (!dto.JoinSpawnMode.Equals("WorldSpawn", StringComparison.OrdinalIgnoreCase) &&
            !dto.JoinSpawnMode.Equals("CustomReference", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("joinSpawnMode must be either 'WorldSpawn' or 'CustomReference'");
        }

        foreach (var world in dto.WorldSettings ?? new List<WorldGameSettingsDto>())
        {
            if (string.IsNullOrWhiteSpace(world.WorldName))
            {
                throw new ArgumentException("Each world setting must include worldName");
            }
        }
    }

    private static WorldGameSettingsDto NormalizeWorldSettings(WorldGameSettingsDto world)
    {
        world.WorldName = world.WorldName.Trim();
        world.DefaultGameMode = string.IsNullOrWhiteSpace(world.DefaultGameMode) ? "SURVIVAL" : world.DefaultGameMode.Trim().ToUpperInvariant();
        world.Weather ??= new WorldWeatherSettingsDto();
        world.RespawnPolicy ??= new RespawnPolicyDto();

        if (world.Weather.ClearWeight < 0) world.Weather.ClearWeight = 0;
        if (world.Weather.RainWeight < 0) world.Weather.RainWeight = 0;
        if (world.Weather.ThunderWeight < 0) world.Weather.ThunderWeight = 0;

        return world;
    }
}
