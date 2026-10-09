using System.Reflection;
using AutoMapper;
using Moq;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
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
    private readonly Mock<IPermissionGroupRepository> _groupRepo = new();
    private readonly GameSettingsService _service;
    private GameSettings? _stored;

    // Default <- Noble (child, weight 10); Staff stands alone with weight 100; Admin is a child of Staff.
    private static readonly PermissionGroup Default = new() { Id = 1, Name = "Default", Weight = 0 };
    private static readonly PermissionGroup Noble = new() { Id = 2, Name = "Noble", Weight = 10, ParentGroupId = 1 };
    private static readonly PermissionGroup Staff = new() { Id = 3, Name = "Staff", Weight = 100 };
    private static readonly PermissionGroup Admin = new() { Id = 4, Name = "Admin", Weight = 5, ParentGroupId = 3 };
    private List<PermissionGroup> _groups = new() { Default, Noble, Staff, Admin };

    public GameSettingsServiceTests()
    {
        _repo.Setup(r => r.GetSingletonAsync()).ReturnsAsync(() => _stored);
        _repo.Setup(r => r.UpsertAsync(It.IsAny<GameSettings>()))
            .ReturnsAsync((GameSettings s) => _stored = s);
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<GameSettingsMappingProfile>()).CreateMapper();
        _groupRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(() => _groups);
        _service = new GameSettingsService(_repo.Object, mapper, _groupRepo.Object);
    }

    private static GameSettingsUpdateDto Update(List<PermissionGroupGameSettingsDto>? overrides = null, string? motd = null) => new()
    {
        JoinSpawnMode = "WorldSpawn",
        GroupOverrides = overrides,
        Motd = motd,
    };

    private static PermissionGroupGameSettingsDto Join(int groupId, string text) => new()
    {
        PermissionGroupId = groupId,
        JoinAnnouncement = text,
    };

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

    // ===== KNG-52 round 2: group overrides, MOTD, respawn modes =====

    [Fact]
    public async Task GroupOverrides_AreReturnedInPrecedenceOrder_WeightFirstEachFollowedByItsParents()
    {
        var dto = await _service.UpdateAsync(Update(new()
        {
            Join(1, "&7{player} joined"),
            Join(3, "&c[Staff] {player}"),
            Join(2, "&6Noble {player}"),
            Join(4, "&4Admin {player}"),
        }));

        // Staff (w100); Noble (w10) followed by its parent Default; Admin (w5) last - its parent Staff is already placed.
        Assert.Equal(new[] { "Staff", "Noble", "Default", "Admin" }, dto.GroupOverrides.Select(o => o.GroupName));
        Assert.Equal(new[] { 1, 2, 3, 4 }, dto.GroupOverrides.Select(o => o.Precedence));
        Assert.Equal("&c[Staff] {player}", dto.GroupOverrides[0].JoinAnnouncement);
    }

    [Fact]
    public async Task GroupOverrides_JoinAtLastLocation_ReplacesTheChosenSpot_AndFalseIsNotAnOverride()
    {
        var spot = new LocationReferenceDto { SourceType = "Location", SourceId = 12, DisplayLabel = "Lounge" };
        var dto = await _service.UpdateAsync(Update(new()
        {
            new PermissionGroupGameSettingsDto { PermissionGroupId = 2, JoinAtLastLocation = true, JoinSpawnReference = spot },
            new PermissionGroupGameSettingsDto { PermissionGroupId = 3, JoinAtLastLocation = false },
        }));

        var only = Assert.Single(dto.GroupOverrides);
        Assert.Equal(2, only.PermissionGroupId);
        Assert.True(only.JoinAtLastLocation);
        Assert.Null(only.JoinSpawnReference);
    }

    [Fact]
    public async Task ServerDefault_IsAValidRespawnMode()
    {
        var update = Update(new()
        {
            new PermissionGroupGameSettingsDto { PermissionGroupId = 2, RespawnPolicy = new RespawnPolicyDto { Mode = "ServerDefault" } },
        });
        var dto = await _service.UpdateAsync(update);
        Assert.Equal("ServerDefault", dto.GroupOverrides[0].RespawnPolicy!.Mode);
    }

    [Fact]
    public async Task GroupOverrides_KeepALeaveMessage()
    {
        var dto = await _service.UpdateAsync(Update(new()
        {
            new PermissionGroupGameSettingsDto { PermissionGroupId = 2, LeaveAnnouncement = " &6{group} {title} {player} left " },
            new PermissionGroupGameSettingsDto { PermissionGroupId = 3, LeaveAnnouncement = "" },
        }));

        Assert.Equal("&6{group} {title} {player} left", dto.GroupOverrides.Single(o => o.PermissionGroupId == 2).LeaveAnnouncement);
        Assert.Equal("", dto.GroupOverrides.Single(o => o.PermissionGroupId == 3).LeaveAnnouncement);
    }

    [Fact]
    public async Task GroupOverrides_UnknownOrDuplicateGroupsAreRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(Update(new() { Join(99, "x") })));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(Update(new() { Join(1, "a"), Join(1, "b") })));
    }

    [Fact]
    public async Task GroupOverrides_WithoutAnyOverrideAreDropped_ButABlankJoinMessageIsKept()
    {
        var dto = await _service.UpdateAsync(Update(new()
        {
            new PermissionGroupGameSettingsDto { PermissionGroupId = 1 },
            Join(2, ""),
        }));

        var only = Assert.Single(dto.GroupOverrides);
        Assert.Equal(2, only.PermissionGroupId);
        Assert.Equal("", only.JoinAnnouncement);
    }

    [Fact]
    public async Task GroupOverrides_OfADeletedGroupDisappearOnRead()
    {
        await _service.UpdateAsync(Update(new() { Join(1, "a"), Join(3, "b") }));
        _groups = new() { Default, Noble };

        var dto = await _service.GetAsync();

        Assert.Equal(new[] { 1 }, dto.GroupOverrides.Select(o => o.PermissionGroupId));
    }

    [Fact]
    public async Task AnUpdateWithoutGroupOverridesOrMotd_KeepsThem()
    {
        await _service.UpdateAsync(Update(new() { Join(1, "a") }, "&6Knights and Kings"));

        var dto = await _service.UpdateAsync(Update());

        Assert.Single(dto.GroupOverrides);
        Assert.Equal("&6Knights and Kings", dto.Motd);
    }

    [Fact]
    public async Task Motd_IsNormalized_BlankClearsIt_AndThreeLinesAreRejected()
    {
        var dto = await _service.UpdateAsync(Update(motd: "&6Knights\r\n&eand Kings   "));
        Assert.Equal("&6Knights\n&eand Kings", dto.Motd);

        dto = await _service.UpdateAsync(Update(motd: "   "));
        Assert.Null(dto.Motd);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(Update(motd: "a\nb\nc")));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(Update(motd: new string('x', 600))));
    }

    [Fact]
    public async Task RespawnModes_JoinSpawnIsAccepted_UnknownIsRejected()
    {
        var update = Update(new()
        {
            new PermissionGroupGameSettingsDto { PermissionGroupId = 2, RespawnPolicy = new RespawnPolicyDto { Mode = "JoinSpawn" } },
        });
        update.WorldSettings = new() { new WorldGameSettingsDto { WorldName = "world", RespawnPolicy = new RespawnPolicyDto { Mode = "joinspawn" } } };
        var dto = await _service.UpdateAsync(update);
        Assert.Equal("JoinSpawn", dto.GroupOverrides[0].RespawnPolicy!.Mode);

        update.WorldSettings[0].RespawnPolicy.Mode = "Bed";
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(update));
    }

    [Fact]
    public void Precedence_IsTheTeleportFeeOrder()
    {
        // The same groups with ParentGroup loaded, as TeleportGroupPolicy.Chain (KNG-41) expects them.
        var def = new PermissionGroup { Id = 1, Name = "Default", Weight = 0 };
        var noble = new PermissionGroup { Id = 2, Name = "Noble", Weight = 10, ParentGroupId = 1, ParentGroup = def };
        var staff = new PermissionGroup { Id = 3, Name = "Staff", Weight = 100 };
        var admin = new PermissionGroup { Id = 4, Name = "Admin", Weight = 5, ParentGroupId = 3, ParentGroup = staff };
        var royal = new PermissionGroup { Id = 5, Name = "Royal", Weight = 10, ParentGroupId = 2, ParentGroup = noble };
        var all = new List<PermissionGroup> { def, noble, staff, admin, royal };

        var precedence = PermissionGroupPrecedence.Order(all, all.ToDictionary(g => g.Id)).Select(g => g.Id);
        var teleport = TeleportGroupPolicy.Chain(all).Select(g => g.Id);

        Assert.Equal(teleport, precedence);
        Assert.Equal(new[] { 3, 2, 1, 5, 4 }, precedence);
    }

    [Fact]
    public void Precedence_SurvivesCyclesAndMissingParents()
    {
        var a = new PermissionGroup { Id = 10, Name = "A", Weight = 1, ParentGroupId = 11 };
        var b = new PermissionGroup { Id = 11, Name = "B", ParentGroupId = 10 };
        var cyclic = new Dictionary<int, PermissionGroup> { [10] = a, [11] = b };
        Assert.Equal(new[] { 10, 11 }, PermissionGroupPrecedence.Order(cyclic.Values, cyclic).Select(g => g.Id));

        // A parent outside the given set ends the chain.
        Assert.Equal(new[] { 2 }, PermissionGroupPrecedence.Order(new[] { Noble }, new Dictionary<int, PermissionGroup> { [2] = Noble }).Select(g => g.Id));
    }
}
