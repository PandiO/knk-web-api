using System.Reflection;
using AutoMapper;
using Moq;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// KNG-52: the GameSettings singleton (docs/specs/game-settings/DESIGN.md) - the plugin's 30-second
/// runtime-world report must not look like an admin edit, a newly seen world gets a default entry,
/// and both writes need the plugin key or the server-config node.
/// </summary>
public class GameSettingsServiceTests
{
    private readonly Mock<IGameSettingsRepository> _repo = new();
    private readonly GameSettingsService _service;
    private GameSettings? _stored;

    public GameSettingsServiceTests()
    {
        _repo.Setup(r => r.GetSingletonAsync()).ReturnsAsync(() => _stored);
        _repo.Setup(r => r.UpsertAsync(It.IsAny<GameSettings>()))
            .ReturnsAsync((GameSettings s) => _stored = s);
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<GameSettingsMappingProfile>()).CreateMapper();
        _service = new GameSettingsService(_repo.Object, mapper);
    }

    private static GameSettingsRuntimeWorldsUpdateDto Report(params string[] worlds) => new()
    {
        RuntimeWorlds = worlds.Select((w, i) => new MinecraftWorldRuntimeDto
        {
            WorldName = w, FolderName = w, Environment = "NORMAL", PlayerCount = 1, IsPrimary = i == 0
        }).ToList()
    };

    [Fact]
    public async Task RuntimeReport_AddsADefaultEntryForANewWorld_AndMovesUpdatedAt()
    {
        await _service.GetAsync();
        _stored!.UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var dto = await _service.UpdateRuntimeWorldsAsync(Report("world", "world_nether"));

        Assert.Equal(new[] { "world", "world_nether" }, dto.WorldSettings.Select(w => w.WorldName));
        Assert.All(dto.WorldSettings, w =>
        {
            Assert.Equal("SURVIVAL", w.DefaultGameMode);
            Assert.False(w.LockTime);
            Assert.Equal("Normal", w.Weather.Mode);
            Assert.Equal("WorldSpawn", w.RespawnPolicy.Mode);
        });
        Assert.True(dto.UpdatedAt > new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        Assert.NotNull(dto.RuntimeWorldsLastUpdatedAt);
    }

    [Fact]
    public async Task RepeatedRuntimeReport_KeepsUpdatedAt()
    {
        await _service.UpdateRuntimeWorldsAsync(Report("world"));
        var adminEdit = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _stored!.UpdatedAt = adminEdit;

        var dto = await _service.UpdateRuntimeWorldsAsync(Report("world"));

        Assert.Equal(adminEdit, dto.UpdatedAt);
        Assert.Single(dto.RuntimeWorlds);
    }

    [Fact]
    public async Task RuntimeReport_KeepsAdminWorldSettings()
    {
        await _service.UpdateRuntimeWorldsAsync(Report("world"));
        await _service.UpdateAsync(new GameSettingsUpdateDto
        {
            JoinSpawnMode = "WorldSpawn",
            WorldSettings = new List<WorldGameSettingsDto>
            {
                new() { WorldName = "world", DefaultGameMode = "adventure", LockTime = true, LockedTime = 6000 }
            }
        });

        var dto = await _service.UpdateRuntimeWorldsAsync(Report("world"));

        var world = Assert.Single(dto.WorldSettings);
        Assert.Equal("ADVENTURE", world.DefaultGameMode);
        Assert.True(world.LockTime);
        Assert.Equal(6000, world.LockedTime);
    }

    [Theory]
    [InlineData(nameof(GameSettingsController.Update))]
    [InlineData(nameof(GameSettingsController.UpdateRuntimeWorlds))]
    public void Writes_NeedThePluginKeyOrServerConfig(string action)
    {
        var nodes = typeof(GameSettingsController).GetMethod(action)!
            .GetCustomAttributes<RequireServiceOrPermissionAttribute>().Select(a => a.Node);

        Assert.Equal(new[] { StaffPermissions.ServerConfig }, nodes);
    }

    [Fact]
    public void Read_StaysOpen()
    {
        Assert.Empty(typeof(GameSettingsController).GetMethod(nameof(GameSettingsController.Get))!
            .GetCustomAttributes<RequireServiceOrPermissionAttribute>());
    }
}
