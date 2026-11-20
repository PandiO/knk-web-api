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
    public async Task TheNavigationCompassIsOffByDefaultAndCanBeSwitchedOn()
    {
        Assert.False((await _service.GetAsync()).GiveNavigationItem);

        var updated = await _service.UpdateAsync(new UpdateSiegeConfigurationDto { GiveNavigationItem = true });
        Assert.True(updated.GiveNavigationItem);

        // a partial update that does not mention it leaves it
        var other = await _service.UpdateAsync(new UpdateSiegeConfigurationDto { MaxBooksAlive = 11 });
        Assert.True(other.GiveNavigationItem);
    }

    [Fact]
    public async Task TheSkipSecondsAreEditableAndMustBeAtLeastOne()
    {
        var updated = await _service.UpdateAsync(new UpdateSiegeConfigurationDto { PlayerSkipMatchmakingSeconds = 90, AdminSkipMatchmakingSeconds = 45 });
        Assert.Equal((90, 45), (updated.PlayerSkipMatchmakingSeconds, updated.AdminSkipMatchmakingSeconds));

        // a partial update that does not mention them leaves them
        var other = await _service.UpdateAsync(new UpdateSiegeConfigurationDto { MaxBooksAlive = 11 });
        Assert.Equal((90, 45), (other.PlayerSkipMatchmakingSeconds, other.AdminSkipMatchmakingSeconds));

        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(new UpdateSiegeConfigurationDto { PlayerSkipMatchmakingSeconds = 0 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(new UpdateSiegeConfigurationDto { AdminSkipMatchmakingSeconds = -5 }));
    }

    /// <summary>Alpha run 1 (PandiO/knk-workspace#198): the post-match wait at the hub, default 30 s, 0-600.</summary>
    [Fact]
    public async Task ThePostMatchSecondsDefaultTo30AndAreEditableFrom0To600()
    {
        Assert.Equal(30, (await _service.GetAsync()).PostMatchSeconds);

        Assert.Equal(0, (await _service.UpdateAsync(new UpdateSiegeConfigurationDto { PostMatchSeconds = 0 })).PostMatchSeconds);
        Assert.Equal(45, (await _service.UpdateAsync(new UpdateSiegeConfigurationDto { PostMatchSeconds = 45 })).PostMatchSeconds);
        Assert.Equal(45, (await _service.UpdateAsync(new UpdateSiegeConfigurationDto { MaxBooksAlive = 12 })).PostMatchSeconds);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(new UpdateSiegeConfigurationDto { PostMatchSeconds = -1 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(new UpdateSiegeConfigurationDto { PostMatchSeconds = 601 }));
    }

    /// <summary>Siege shop L1 (PandiO/knk-workspace#866): the siege points earn settings, DESIGN § 5.2 defaults, transfers off.</summary>
    [Fact]
    public async Task TheSiegePointsSettingsHaveTheDesignDefaultsAndTransfersAreOff()
    {
        var c = await _service.GetAsync();
        Assert.Equal((100, 0.1, 1.5), (c.PointsPerKill, c.PointsStreakStep, c.PointsStreakCap));
        Assert.Equal((40, 4.0, 10), (c.PointsPerAssist, c.AssistMinDamage, c.AssistWindowSeconds));
        Assert.Equal((15, 5.0, 3, 100), (c.PointsPerDeath, c.GateHpPerPoint, c.PointsPerCapturePercent, c.PointsPerCapture));
        Assert.Equal((10, 60), (c.TrickleAmount, c.TrickleIntervalSeconds));
        Assert.False(c.PointTransfersEnabled);
    }

    [Fact]
    public async Task TheSiegePointsSettingsAreEditablePartially()
    {
        var updated = await _service.UpdateAsync(new UpdateSiegeConfigurationDto
        {
            PointsPerKill = 120, PointsStreakCap = 2.0, PointTransfersEnabled = true, GateHpPerPoint = 0
        });
        Assert.Equal((120, 2.0, true, 0.0), (updated.PointsPerKill, updated.PointsStreakCap, updated.PointTransfersEnabled, updated.GateHpPerPoint));
        Assert.Equal(40, updated.PointsPerAssist);

        var other = await _service.UpdateAsync(new UpdateSiegeConfigurationDto { MaxBooksAlive = 11 });
        Assert.Equal((120, true), (other.PointsPerKill, other.PointTransfersEnabled));
    }

    [Theory]
    [MemberData(nameof(InvalidSiegePoints))]
    public async Task TheSiegePointsSettingsRejectNonsense(UpdateSiegeConfigurationDto dto)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(dto));
    }

    public static IEnumerable<object[]> InvalidSiegePoints() => new[]
    {
        new object[] { new UpdateSiegeConfigurationDto { PointsPerKill = -1 } },
        new object[] { new UpdateSiegeConfigurationDto { PointsPerDeath = -1 } },
        new object[] { new UpdateSiegeConfigurationDto { PointsStreakStep = -0.1 } },
        new object[] { new UpdateSiegeConfigurationDto { PointsStreakCap = 0.9 } },
        new object[] { new UpdateSiegeConfigurationDto { AssistMinDamage = double.NaN } },
        new object[] { new UpdateSiegeConfigurationDto { AssistWindowSeconds = -1 } },
        new object[] { new UpdateSiegeConfigurationDto { GateHpPerPoint = -5 } },
        new object[] { new UpdateSiegeConfigurationDto { TrickleIntervalSeconds = 0 } },
        new object[] { new UpdateSiegeConfigurationDto { TrickleAmount = -10 } },
    };

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
        Assert.Equal((60, 31), (dto.PlayerSkipMatchmakingSeconds, dto.AdminSkipMatchmakingSeconds));
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
