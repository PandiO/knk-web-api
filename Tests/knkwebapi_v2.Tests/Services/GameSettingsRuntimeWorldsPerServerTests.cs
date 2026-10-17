using AutoMapper;
using Moq;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Json;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// KNG-118 (A9): the runtime-worlds report is per game server - a report replaces only the reporting
/// server's worlds, a server that stopped reporting drops out after <see cref="GameSettingsService.RuntimeWorldsStaleAfter"/>,
/// and reports without a server id (older plugins) are the "default" server's.
/// </summary>
public class GameSettingsRuntimeWorldsPerServerTests
{
    private readonly Mock<IGameSettingsRepository> _repo = new();
    private readonly GameSettingsService _service;
    private GameSettings? _stored;

    public GameSettingsRuntimeWorldsPerServerTests()
    {
        _repo.Setup(r => r.GetSingletonAsync()).ReturnsAsync(() => _stored);
        _repo.Setup(r => r.UpsertAsync(It.IsAny<GameSettings>())).ReturnsAsync((GameSettings s) => _stored = s);
        var groups = new Mock<IPermissionGroupRepository>();
        groups.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<PermissionGroup>());
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<GameSettingsMappingProfile>()).CreateMapper();
        _service = new GameSettingsService(_repo.Object, mapper, groups.Object);
    }

    private static GameSettingsRuntimeWorldsUpdateDto Report(string? serverId, params string[] worlds) => new()
    {
        ServerId = serverId,
        RuntimeWorlds = worlds.Select(w => new MinecraftWorldRuntimeDto { WorldName = w, FolderName = w, Environment = "NORMAL" }).ToList()
    };

    private List<MinecraftWorldRuntimeDto> Stored() =>
        JsonColumn.DeserializeList<MinecraftWorldRuntimeDto>(_stored!.RuntimeWorldsJson);

    private void AgeStoredReports(TimeSpan by)
    {
        var worlds = Stored();
        worlds.ForEach(w => w.ReportedAt = w.ReportedAt - by);
        _stored!.RuntimeWorldsJson = JsonColumn.Serialize(worlds);
    }

    [Fact]
    public async Task ASecondServerDoesNotWipeTheFirstServersWorlds()
    {
        await _service.UpdateRuntimeWorldsAsync(Report("survival", "world", "world_nether"));

        var dto = await _service.UpdateRuntimeWorldsAsync(Report("hub-server", "hub"));

        Assert.Equal(new[] { ("hub-server", "hub"), ("survival", "world"), ("survival", "world_nether") },
            dto.RuntimeWorlds.Select(w => (w.ServerId!, w.WorldName)));
        Assert.All(dto.RuntimeWorlds, w => Assert.NotNull(w.ReportedAt));
        Assert.Equal(new[] { "hub", "world", "world_nether" }, dto.WorldSettings.Select(w => w.WorldName).OrderBy(n => n));
    }

    [Fact]
    public async Task AReportReplacesOnlyTheReportingServersOwnWorlds()
    {
        await _service.UpdateRuntimeWorldsAsync(Report("survival", "world", "world_nether"));
        await _service.UpdateRuntimeWorldsAsync(Report("hub-server", "hub"));

        var dto = await _service.UpdateRuntimeWorldsAsync(Report("SURVIVAL", "world"));

        Assert.Equal(new[] { ("hub-server", "hub"), ("SURVIVAL", "world") }, dto.RuntimeWorlds.Select(w => (w.ServerId!, w.WorldName)));
    }

    [Fact]
    public async Task TheSameWorldNameOnTwoServersIsListedForEach()
    {
        await _service.UpdateRuntimeWorldsAsync(Report("a", "world"));

        var dto = await _service.UpdateRuntimeWorldsAsync(Report("b", "world"));

        Assert.Equal(new[] { "a", "b" }, dto.RuntimeWorlds.Select(w => w.ServerId));
        Assert.Single(dto.WorldSettings);
    }

    [Fact]
    public async Task AServerThatStoppedReportingDropsOutAfterTheStaleTime()
    {
        await _service.UpdateRuntimeWorldsAsync(Report("old-server", "old_world"));
        AgeStoredReports(GameSettingsService.RuntimeWorldsStaleAfter - TimeSpan.FromMinutes(1));
        var dto = await _service.UpdateRuntimeWorldsAsync(Report("survival", "world"));
        Assert.Contains(dto.RuntimeWorlds, w => w.WorldName == "old_world");

        AgeStoredReports(TimeSpan.FromMinutes(2));
        dto = await _service.UpdateRuntimeWorldsAsync(Report("survival", "world"));

        Assert.Equal(new[] { "world" }, dto.RuntimeWorlds.Select(w => w.WorldName));
        Assert.Contains(dto.WorldSettings, w => w.WorldName == "old_world"); // its settings are kept
    }

    [Fact]
    public async Task AReportWithoutAServerIdIsTheDefaultServers_AndReplacesEntriesStoredBeforeServerIds()
    {
        await _service.GetAsync();
        _stored!.RuntimeWorldsJson = JsonColumn.Serialize(new List<MinecraftWorldRuntimeDto>
        {
            new() { WorldName = "legacy", FolderName = "legacy", Environment = "NORMAL" }
        });

        var dto = await _service.UpdateRuntimeWorldsAsync(Report(null, "world"));

        var world = Assert.Single(dto.RuntimeWorlds);
        Assert.Equal(("world", MinecraftWorldRuntimeDto.DefaultServerId), (world.WorldName, world.ServerId));

        dto = await _service.UpdateRuntimeWorldsAsync(Report("  ", "world", "world_the_end"));
        Assert.Equal(new[] { "world", "world_the_end" }, dto.RuntimeWorlds.Select(w => w.WorldName));
    }
}
