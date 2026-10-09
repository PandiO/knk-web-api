using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services;

public class GameSettingsService : IGameSettingsService
{
    /// <summary>Respawn policy modes the plugin understands (DESIGN §3.3).</summary>
    public static readonly string[] RespawnModes = { "WorldSpawn", "ConfiguredReference", "NearestTown", "JoinSpawn" };

    /// <summary>A server-list MOTD has two lines.</summary>
    public const int MaxMotdLines = 2;
    public const int MaxMotdLength = 512;

    private readonly IGameSettingsRepository _repository;
    private readonly IMapper _mapper;
    private readonly IPermissionGroupRepository _groups;

    public GameSettingsService(IGameSettingsRepository repository, IMapper mapper, IPermissionGroupRepository groups)
    {
        _repository = repository;
        _mapper = mapper;
        _groups = groups;
    }

    public async Task<GameSettingsReadDto> GetAsync()
    {
        var settings = await EnsureExistsAsync();
        return await ToReadDtoAsync(settings);
    }

    public async Task<GameSettingsReadDto> UpdateAsync(GameSettingsUpdateDto dto)
    {
        if (dto == null)
        {
            throw new ArgumentNullException(nameof(dto));
        }

        ValidateUpdate(dto);
        var groupsById = (await _groups.GetAllAsync()).ToDictionary(g => g.Id);
        var cleanedOverrides = dto.GroupOverrides == null ? null : CleanGroupOverrides(dto.GroupOverrides, groupsById);

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
        if (dto.Motd != null)
        {
            existing.Motd = NormalizeMotd(dto.Motd);
        }
        if (cleanedOverrides != null)
        {
            existing.GroupOverridesJson = GameSettingsJson.Serialize(cleanedOverrides);
        }
        existing.UpdatedAt = DateTime.UtcNow;

        var saved = await _repository.UpsertAsync(existing);
        return await ToReadDtoAsync(saved, groupsById);
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
        return await ToReadDtoAsync(saved);
    }

    /// <summary>
    /// The read DTO with the group overrides enriched: overrides of deleted groups dropped, the
    /// group name filled in and the list sorted by <see cref="PermissionGroupPrecedence"/>.
    /// </summary>
    private async Task<GameSettingsReadDto> ToReadDtoAsync(GameSettings settings, Dictionary<int, PermissionGroup>? groupsById = null)
    {
        var dto = _mapper.Map<GameSettingsReadDto>(settings);
        if (dto.GroupOverrides.Count == 0)
        {
            return dto;
        }
        groupsById ??= (await _groups.GetAllAsync()).ToDictionary(g => g.Id);
        var order = PermissionGroupPrecedence.Order(groupsById.Values, groupsById)
            .Select((g, i) => (g.Id, Rank: i))
            .ToDictionary(x => x.Id, x => x.Rank);
        dto.GroupOverrides = dto.GroupOverrides
            .Where(o => groupsById.ContainsKey(o.PermissionGroupId))
            .OrderBy(o => order[o.PermissionGroupId])
            .Select((o, i) =>
            {
                o.GroupName = groupsById[o.PermissionGroupId].Name;
                o.Precedence = i + 1;
                return o;
            })
            .ToList();
        return dto;
    }

    /// <summary>Known groups only (else 400), one entry per group, entries without any override dropped.</summary>
    private static List<PermissionGroupGameSettingsDto> CleanGroupOverrides(
        List<PermissionGroupGameSettingsDto> overrides, IReadOnlyDictionary<int, PermissionGroup> groupsById)
    {
        var seen = new HashSet<int>();
        var cleaned = new List<PermissionGroupGameSettingsDto>();
        foreach (var o in overrides)
        {
            if (o == null)
            {
                continue;
            }
            if (!groupsById.ContainsKey(o.PermissionGroupId))
            {
                throw new ArgumentException($"groupOverrides: permission group {o.PermissionGroupId} does not exist");
            }
            if (!seen.Add(o.PermissionGroupId))
            {
                throw new ArgumentException($"groupOverrides: permission group {o.PermissionGroupId} is listed twice");
            }
            if (o.JoinAnnouncement == null && o.LeaveAnnouncement == null && o.JoinSpawnReference == null && o.RespawnPolicy == null)
            {
                continue;
            }
            cleaned.Add(new PermissionGroupGameSettingsDto
            {
                PermissionGroupId = o.PermissionGroupId,
                JoinAnnouncement = o.JoinAnnouncement?.Trim(),
                LeaveAnnouncement = o.LeaveAnnouncement?.Trim(),
                JoinSpawnReference = o.JoinSpawnReference,
                RespawnPolicy = o.RespawnPolicy,
            });
        }
        return cleaned;
    }

    /// <summary>Blank = null (server.properties); line endings normalized, trailing blanks trimmed.</summary>
    private static string? NormalizeMotd(string motd)
    {
        var normalized = motd.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
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
            ValidateRespawnPolicy(world.RespawnPolicy, $"worldSettings[{world.WorldName}].respawnPolicy");
        }

        ValidateRespawnPolicy(dto.DefaultRespawnPolicy, "defaultRespawnPolicy");
        foreach (var o in dto.GroupOverrides ?? new List<PermissionGroupGameSettingsDto>())
        {
            if (o != null)
            {
                ValidateRespawnPolicy(o.RespawnPolicy, $"groupOverrides[{o.PermissionGroupId}].respawnPolicy");
            }
        }

        if (dto.Motd != null)
        {
            var motd = NormalizeMotd(dto.Motd) ?? string.Empty;
            if (motd.Length > MaxMotdLength)
            {
                throw new ArgumentException($"motd must be at most {MaxMotdLength} characters");
            }
            if (motd.Split('\n').Length > MaxMotdLines)
            {
                throw new ArgumentException($"motd has at most {MaxMotdLines} lines");
            }
        }
    }

    private static void ValidateRespawnPolicy(RespawnPolicyDto? policy, string field)
    {
        if (policy == null || string.IsNullOrWhiteSpace(policy.Mode))
        {
            return;
        }
        if (!RespawnModes.Contains(policy.Mode.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"{field}.mode must be one of {string.Join(", ", RespawnModes)}");
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
