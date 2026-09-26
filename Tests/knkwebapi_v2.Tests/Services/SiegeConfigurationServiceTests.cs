using Moq;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Siege Phase 2: the SiegeConfiguration singleton (docs/specs/siege-minigame/DESIGN.md §3.8) -
/// seeded with the legacy defaults on first read, partial updates, list normalization, validation.
/// </summary>
public class SiegeConfigurationServiceTests
{
    private readonly Mock<ISiegeConfigurationRepository> _repo = new();
    private readonly SiegeConfigurationService _service;
    private SiegeConfiguration? _stored;

    public SiegeConfigurationServiceTests()
    {
        _repo.Setup(r => r.GetSingletonAsync()).ReturnsAsync(() => _stored);
        _repo.Setup(r => r.AddAsync(It.IsAny<SiegeConfiguration>()))
            .Callback<SiegeConfiguration>(c => _stored = c)
            .Returns(Task.CompletedTask);
        _service = new SiegeConfigurationService(_repo.Object);
    }

    [Fact]
    public async Task FirstRead_SeedsTheLegacyDefaults()
    {
        var dto = await _service.GetAsync();

        _repo.Verify(r => r.AddAsync(It.IsAny<SiegeConfiguration>()), Times.Once);
        Assert.Equal("global", _stored!.Id);
        // §7.2 capture constants, §7.4 side-capture, §6.2 timeline, §6.6/6.7 combat, §9.4 drops, §8.5 view
        // Phase 9: A1 and IV A2 are the playtest tuning (10), the rest the legacy values.
        Assert.Equal((10, 2, 10, 6, 3, 6), (dto.CaptureAttackBase, dto.CaptureAttackPerExtra, dto.CaptureAttackPerExtraInstantVictory,
            dto.CaptureDefendBase, dto.CaptureDefendPerExtra, dto.CaptureDefendPerExtraInstantVictory));
        Assert.Equal(0.4, dto.SideCaptureReduction);
        Assert.Equal((30, 25, 15, 10), (dto.VoteCloseSecondsBeforeStart, dto.DrawSecondsBeforeStart, dto.HubSecondsBeforeStart, dto.TeamSplitSecondsBeforeStart));
        Assert.Equal(new[] { 290, 60, 30, 15 }, dto.MatchmakingAnnouncementMarks);
        Assert.Equal(new[] { 5, 10, 15 }, dto.KillAnnouncementThresholds);
        Assert.Equal(3, dto.KillStreakAnnounceAbove);
        Assert.Equal(1.5, dto.HeadshotMultiplier);
        Assert.Equal(new[] { "/siege", "/msg", "/r", "/staffchat", "/menu" }, dto.AllowedCommands);
        Assert.Equal(20, dto.SpawnPickerDelayTicks);
        Assert.Equal(30, dto.EnchantDropChancePerMille);
        Assert.Contains("minecraft:sharpness", dto.AllowedEnchantmentKeys);
        Assert.DoesNotContain("minecraft:mending", dto.AllowedEnchantmentKeys);
        Assert.Equal((1, 2), (dto.EnchantLevelMin, dto.EnchantLevelMax));
        Assert.Equal(SiegeNonMemberGateView.PreLockdownView, dto.NonMemberGateView);

        await _service.GetAsync();
        _repo.Verify(r => r.AddAsync(It.IsAny<SiegeConfiguration>()), Times.Once);   // only once
    }

    [Fact]
    public async Task Update_IsPartial()
    {
        var dto = await _service.UpdateAsync(new UpdateSiegeConfigurationDto
        {
            HeadshotMultiplier = 1.0,
            NonMemberGateView = SiegeNonMemberGateView.PassThroughOnly
        });

        Assert.Equal(1.0, dto.HeadshotMultiplier);
        Assert.Equal(SiegeNonMemberGateView.PassThroughOnly, dto.NonMemberGateView);
        Assert.Equal(10, dto.CaptureAttackBase);         // untouched
        Assert.Equal(new[] { 290, 60, 30, 15 }, dto.MatchmakingAnnouncementMarks);
        _repo.Verify(r => r.SaveAsync(_stored!), Times.Once);
    }

    [Fact]
    public async Task Update_NormalizesLists()
    {
        var dto = await _service.UpdateAsync(new UpdateSiegeConfigurationDto
        {
            AllowedCommands = new() { "siege", "/MSG", " /r ", "siege" },
            AllowedEnchantmentKeys = new() { "Sharpness", "minecraft:power", "sharpness" },
            MatchmakingAnnouncementMarks = new() { 15, 290, 60 }
        });

        Assert.Equal(new[] { "/siege", "/msg", "/r" }, dto.AllowedCommands);
        Assert.Equal(new[] { "minecraft:sharpness", "minecraft:power" }, dto.AllowedEnchantmentKeys);
        Assert.Equal(new[] { 290, 60, 15 }, dto.MatchmakingAnnouncementMarks);
    }

    public static IEnumerable<object[]> InvalidUpdates() => new List<object[]>
    {
        new object[] { new UpdateSiegeConfigurationDto { HubSecondsBeforeStart = 26 } },          // hub after draw
        new object[] { new UpdateSiegeConfigurationDto { TeamSplitSecondsBeforeStart = 0 } },
        new object[] { new UpdateSiegeConfigurationDto { SideCaptureReduction = 1.5 } },
        new object[] { new UpdateSiegeConfigurationDto { HeadshotMultiplier = 0.5 } },
        new object[] { new UpdateSiegeConfigurationDto { EnchantLevelMin = 3 } },                  // min > max (2)
        new object[] { new UpdateSiegeConfigurationDto { EnchantDropChancePerMille = 1001 } },
        new object[] { new UpdateSiegeConfigurationDto { CaptureDefendBase = -1 } },
        new object[] { new UpdateSiegeConfigurationDto { AllowedEnchantmentKeys = new() { "not a key" } } },
        new object[] { new UpdateSiegeConfigurationDto { AllowedCommands = new() { "/siege join" } } },
        new object[] { new UpdateSiegeConfigurationDto { MatchmakingAnnouncementMarks = new() { 30, -5 } } },
    };

    [Theory]
    [MemberData(nameof(InvalidUpdates))]
    public async Task Update_RejectsInvalidValues_WithoutSaving(UpdateSiegeConfigurationDto dto)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(dto));
        _repo.Verify(r => r.SaveAsync(It.IsAny<SiegeConfiguration>()), Times.Never);
    }
}
