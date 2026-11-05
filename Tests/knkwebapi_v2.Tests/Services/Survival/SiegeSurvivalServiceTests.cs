using Microsoft.EntityFrameworkCore;
using Moq;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Survival;

/// <summary>
/// SiegeSurvivalService on InMemory with the real repositories (PandiO/knk-workspace#41, link S4): settings and
/// lobby settings upserts, the child collections' save-time validation, the frozen configuration with every
/// default resolved, readiness through the service and the per-round preview.
/// </summary>
public class SiegeSurvivalServiceTests : IAsyncLifetime
{
    private KnKDbContext _context = null!;
    private SiegeSurvivalService _service = null!;
    private readonly Mock<ILocationService> _locations = new();

    public async Task InitializeAsync()
    {
        _context = SiegeTestData.NewContext("SiegeSurvival");
        await SiegeTestData.SeedValidScenarioAsync(_context);
        await SurvivalTestData.SeedReadySurvivalAsync(_context);
        _service = NewService();
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    private SiegeSurvivalService NewService() => new(
        new SiegeSurvivalRepository(_context), new SiegeScenarioRepository(_context), _locations.Object, SurvivalTestData.Mapper());

    private static SiegeSurvivalSettingsUpsertDto SettingsDto() => new();

    // ==== Settings ====

    [Fact]
    public async Task Summaries_ListScenariosWithSettings()
    {
        var summaries = await _service.GetScenarioSummariesAsync();
        var one = Assert.Single(summaries);
        Assert.Equal(100, one.SiegeScenarioId);
        Assert.Equal("Siege of Cinix", one.SiegeScenarioName);
        Assert.Equal(1, one.DifficultyProfileCount);
    }

    [Fact]
    public async Task GetSettings_ReadsTheRow_AndNullWithoutOne()
    {
        var read = await _service.GetSettingsAsync(100);
        Assert.NotNull(read);
        Assert.Equal("Siege of Cinix", read!.SiegeScenarioName);
        Assert.Equal(SurvivalEquipmentMode.OwnInventory, read.EquipmentMode);
        Assert.Null(await _service.GetSettingsAsync(12345));
    }

    [Fact]
    public async Task UpsertSettings_CreatesForAScenarioWithoutARow_AndReplacesAnExistingOne()
    {
        await _service.DeleteSettingsAsync(100);
        Assert.Null(await _service.GetSettingsAsync(100));

        var dto = SettingsDto();
        dto.EquipmentMode = SurvivalEquipmentMode.Kits;
        dto.SelectedKits = new() { new() { KitId = SurvivalTestData.KitBow }, new() { KitId = SurvivalTestData.KitSword, SortOrder = 5 } };
        dto.DefaultKitId = SurvivalTestData.KitBow;
        dto.RoundBackstopMillis = null;
        dto.CoinsPerKill = 12.5m;
        var created = await _service.UpsertSettingsAsync(100, dto);
        Assert.Equal(SurvivalEquipmentMode.Kits, created.EquipmentMode);
        Assert.Equal(new[] { SurvivalTestData.KitBow, SurvivalTestData.KitSword }, created.SelectedKits.Select(k => k.KitId));
        Assert.Equal("Archer", created.SelectedKits[0].KitName);
        Assert.Null(created.RoundBackstopMillis);
        Assert.Equal(12.5m, created.CoinsPerKill);

        dto.SelectedKits = new() { new() { KitId = SurvivalTestData.KitSword } };
        dto.DefaultKitId = SurvivalTestData.KitSword;
        var replaced = await _service.UpsertSettingsAsync(100, dto);
        Assert.Equal(created.Id, replaced.Id);
        Assert.Equal(new[] { SurvivalTestData.KitSword }, replaced.SelectedKits.Select(k => k.KitId));

        dto.SelectedKits = null; // unchanged
        Assert.Single((await _service.UpsertSettingsAsync(100, dto)).SelectedKits);
    }

    [Fact]
    public async Task UpsertSettings_RefusesBadValues()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.UpsertSettingsAsync(12345, SettingsDto()));

        async Task Refuses(Action<SiegeSurvivalSettingsUpsertDto> change)
        {
            var dto = SettingsDto();
            change(dto);
            await Assert.ThrowsAsync<ArgumentException>(() => _service.UpsertSettingsAsync(100, dto));
        }
        await Refuses(d => d.MinPlayers = 0);
        await Refuses(d => d.EquipmentMode = (SurvivalEquipmentMode)9);
        await Refuses(d => d.HumanTeamId = 999);
        await Refuses(d => { d.HumanTeamId = 201; d.NpcTeamId = 201; });
        await Refuses(d => d.FavorNearPlayersPercent = 101);
        await Refuses(d => d.NearZoneCount = 0);
        await Refuses(d => d.StuckAfterMillis = 0);
        await Refuses(d => d.RoundBackstopMillis = 0);
        await Refuses(d => d.MeaningfulDistance = 0);
        await Refuses(d => d.KillBonusMultiplier = 0);
        await Refuses(d => d.SelectedKits = new() { new() { KitId = 4242 } });
        await Refuses(d => d.SelectedKits = new() { new() { KitId = SurvivalTestData.KitBow }, new() { KitId = SurvivalTestData.KitBow } });
        await Refuses(d => d.DefaultKitId = 4242);
        await Refuses(d => d.BaselineLoadoutKitId = 4242);
    }

    [Fact]
    public async Task DeleteSettings_KeepsTheSurvivalChildren()
    {
        await _service.DeleteSettingsAsync(100);
        Assert.Null(await _service.GetSettingsAsync(100));
        Assert.Equal(2, (await _service.SpawnZones.ListAsync(100)).Count);
        Assert.Single(await _service.DifficultyProfiles.ListAsync(100));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.DeleteSettingsAsync(100));
    }

    // ==== Lobby settings ====

    [Fact]
    public async Task LobbySettings_UpsertReadDelete()
    {
        _context.SiegeLobbies.Add(new SiegeLobby { Id = 7, Name = "Siege — Cinix", Key = "cinix", IsEnabled = true });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        Assert.Null(await _service.GetLobbySettingsAsync(7));
        var created = await _service.UpsertLobbySettingsAsync(7, new SurvivalLobbySettingsUpsertDto { AllCapturedOverride = SurvivalAllCapturedOverride.On });
        Assert.Equal(SurvivalAllCapturedOverride.On, created.AllCapturedOverride);
        Assert.Equal("Siege — Cinix", created.SiegeLobbyName);

        var updated = await _service.UpsertLobbySettingsAsync(7, new SurvivalLobbySettingsUpsertDto { AllCapturedOverride = SurvivalAllCapturedOverride.Off });
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(SurvivalAllCapturedOverride.Off, (await _service.GetLobbySettingsAsync(7))!.AllCapturedOverride);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.UpsertLobbySettingsAsync(8, new SurvivalLobbySettingsUpsertDto()));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpsertLobbySettingsAsync(7, new SurvivalLobbySettingsUpsertDto { AllCapturedOverride = (SurvivalAllCapturedOverride)9 }));

        await _service.DeleteLobbySettingsAsync(7);
        Assert.Null(await _service.GetLobbySettingsAsync(7));
    }

    // ==== Frozen configuration ====

    [Fact]
    public async Task Configuration_ResolvesEveryDefault()
    {
        var config = await _service.GetConfigurationAsync(100, null, null);

        Assert.Equal(100, config.SiegeScenarioId);
        Assert.True(config.Readiness.IsReady, string.Join("; ", config.Readiness.Errors.Select(e => e.Code + ": " + e.Message)));
        Assert.Equal(201, config.HumanTeamId);  // first Defender
        Assert.Equal(202, config.NpcTeamId);    // first Attacker
        Assert.Equal(1, config.MinPlayers);
        Assert.Equal(10_000, config.IntermissionMillis);
        Assert.False(config.AllowRecapture);
        Assert.Equal(10, config.Difficulty!.Id); // the default profile
        Assert.Equal(1, config.Difficulty.StatCurve.Base);
        Assert.Equal(6, config.Difficulty.CountCurve.Base);
        Assert.Equal(0.3, config.Difficulty.StatWeights.WalkingSpeed);

        // Kill rewards default to the scenario's capture rewards ÷ 10 (S32: CoinRewardCapture 50 → 5, ExpRewardCapture 5 → 0.5).
        Assert.Equal(5m, config.KillRewards.CoinsPerKill);
        Assert.Equal(0.5m, config.KillRewards.XpPerKill);
        Assert.Equal(70, config.KillRewards.MatchCurrencyPerKill);
        Assert.Equal(1.1m, config.KillRewards.BonusMultiplier);

        Assert.False(config.EndConditions.AllCapturedEnabled);
        Assert.Equal(SurvivalAllCapturedOverride.Inherit, config.EndConditions.LobbyOverride);
        Assert.Equal(new[] { 501 }, config.EndConditions.InstaLoseObjectiveIds);
        Assert.Equal(2, config.Objectives.Count);
        Assert.True(config.Objectives[0].InstaLose);
        Assert.Equal(SurvivalTargetOverrideMode.Standard, config.Objectives[0].TargetOverride);
        Assert.NotNull(config.Objectives[1].CaptureLocation); // from the gate
        var gate = Assert.Single(config.Gates);
        Assert.Equal(SurvivalTargetOverrideMode.Allow, gate.TargetOverride);
        Assert.True(gate.IsObjectiveGate);

        Assert.Equal(2, config.Spawn.Zones.Count);
        Assert.Equal("Outside", config.Spawn.Zones[0].Name);
        Assert.NotNull(config.Spawn.Zones[0].AnchorLocation);
        Assert.Single(config.Spawn.Zones[0].Points);
        Assert.Equal(new[] { 502 }, config.Spawn.Zones[1].PrerequisiteObjectiveIds);

        Assert.Equal(new[] { "ZOMBIE", "SKELETON" }, config.MonsterTypes.Select(t => t.EntityType));
        var special = Assert.Single(config.SpecialWaves.Profiles);
        Assert.Equal("wolves", special.Key);
        Assert.Equal("WOLF", Assert.Single(special.MonsterTypes).EntityType);
        Assert.Equal(2, config.SpecialWaves.Entries.Count);
        Assert.Equal("wolves", config.SpecialWaves.Entries[0].ProfileKey);

        var shop = Assert.Single(config.PurchasePoints);
        Assert.Equal("Iron sword", Assert.Single(shop.Offers).ItemBlueprintName);
        Assert.Equal(2, config.MysteryBoxPoints.Count);
        var box = config.MysteryBoxPoints[0];
        Assert.Equal("minecraft:end_portal_frame", box.BlockType);
        Assert.Equal(1000, box.RollPrice);
        Assert.Equal(10_000, box.ClaimTimeoutMillis);
        Assert.Equal(SurvivalMysteryBoxVariant.Items, box.Pool.Variant);
        Assert.Equal(100m, box.Pool.TotalChancePercent);
        Assert.Equal(6050, box.Pool.Entries[0].ChanceBasisPoints);
        Assert.Equal("minecraft:sharpness", Assert.Single(box.Pool.Entries[0].Enchantments).EnchantmentKey);
        Assert.Equal(2500, box.Pool.Entries[0].Enchantments[0].ChanceBasisPoints);
        Assert.Equal(SurvivalMysteryBoxVariant.EnchantedBooks, config.MysteryBoxPoints[1].Pool.Variant);
        Assert.Equal(3, config.MysteryBoxPoints[1].Pool.Entries[0].EnchantmentLevel);
    }

    [Fact]
    public async Task Configuration_ResolvesTheLobbyOverride_AndTheRequestedProfile()
    {
        _context.SiegeLobbies.Add(new SiegeLobby { Id = 7, Name = "L", Key = "l", IsEnabled = true });
        _context.SurvivalLobbySettings.Add(new SurvivalLobbySettings { SiegeLobbyId = 7, AllCapturedOverride = SurvivalAllCapturedOverride.On });
        _context.SurvivalDifficultyProfiles.Add(new SurvivalDifficultyProfile { Id = 11, SiegeScenarioId = 100, Name = "Hard", CountBase = 10, StatCap = 1.2 });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var config = await _service.GetConfigurationAsync(100, 7, 11);
        Assert.Equal(7, config.SiegeLobbyId);
        Assert.True(config.EndConditions.AllCapturedEnabled);
        Assert.Equal(SurvivalAllCapturedOverride.On, config.EndConditions.LobbyOverride);
        Assert.False(config.EndConditions.ScenarioAllCaptured);
        Assert.Equal(11, config.Difficulty!.Id);
        Assert.Equal(2, config.DifficultyProfiles.Count);
        Assert.Equal(11, config.Readiness.DifficultyProfileId);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.GetConfigurationAsync(100, 8, null));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetConfigurationAsync(100, 7, 999));
    }

    [Fact]
    public async Task Configuration_Is404_ForAScenarioWithoutSurvivalSettings_ButReadinessSaysWhy()
    {
        await _service.DeleteSettingsAsync(100);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.GetConfigurationAsync(100, null, null));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.GetConfigurationAsync(12345, null, null));

        var readiness = await _service.GetReadinessAsync(100, null, null);
        Assert.False(readiness.IsReady);
        Assert.Contains(readiness.Errors, e => e.Code == SurvivalReadinessCodes.SettingsMissing);
    }

    [Fact]
    public async Task Readiness_AndPreview_ThroughTheService()
    {
        var readiness = await _service.GetReadinessAsync(100, null, null);
        Assert.True(readiness.IsReady, string.Join("; ", readiness.Errors.Select(e => e.Code + ": " + e.Message)));
        Assert.Equal(10, readiness.DifficultyProfileId);

        var preview = await _service.PreviewAsync(100, null, 1, 10, 3);
        Assert.Equal(10, preview.Rounds.Count);
        Assert.Equal(10, preview.DifficultyProfileId);
        Assert.Equal(9, preview.Rounds[0].EnemyCount); // 6 × 1.5

        await Assert.ThrowsAsync<ArgumentException>(() => _service.PreviewAsync(100, null, 0, 10, 3));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.PreviewAsync(12345, null, 1, 10, 3));
    }

    // ==== Child collections ====

    [Fact]
    public async Task DifficultyProfiles_CreateUpdateDelete_WithOneDefaultPerScenario()
    {
        var created = await _service.DifficultyProfiles.CreateAsync(new SurvivalDifficultyProfileUpsertDto { SiegeScenarioId = 100, Name = "Hard", IsDefault = true, CountBase = 10 });
        Assert.True(created.IsDefault);
        Assert.Equal(1, created.SortOrder);
        Assert.False((await _service.DifficultyProfiles.GetAsync(10))!.IsDefault);

        await _service.DifficultyProfiles.UpdateAsync(created.Id, new SurvivalDifficultyProfileUpsertDto { Name = "Harder", CountBase = 12, StatCap = 2 });
        var updated = await _service.DifficultyProfiles.GetAsync(created.Id);
        Assert.Equal("Harder", updated!.Name);
        Assert.Equal(12, updated.CountBase);
        Assert.Equal(100, updated.SiegeScenarioId);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.DifficultyProfiles.UpdateAsync(created.Id, new SurvivalDifficultyProfileUpsertDto { SiegeScenarioId = 101, Name = "x" }));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.DifficultyProfiles.CreateAsync(new SurvivalDifficultyProfileUpsertDto { SiegeScenarioId = 12345, Name = "x" }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DifficultyProfiles.CreateAsync(new SurvivalDifficultyProfileUpsertDto { SiegeScenarioId = 100, Name = "x", CountBase = 0 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DifficultyProfiles.CreateAsync(new SurvivalDifficultyProfileUpsertDto { SiegeScenarioId = 100, Name = "x", StatCap = 0.9 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DifficultyProfiles.CreateAsync(new SurvivalDifficultyProfileUpsertDto { SiegeScenarioId = 100, Name = "x", RepairRatePerSecond = 0 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DifficultyProfiles.CreateAsync(new SurvivalDifficultyProfileUpsertDto { SiegeScenarioId = 100, Name = "x", HealthWeight = -1 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DifficultyProfiles.CreateAsync(new SurvivalDifficultyProfileUpsertDto { SiegeScenarioId = 100, Name = "x", AggroRadius = 0 }));

        await _service.DifficultyProfiles.DeleteAsync(created.Id);
        Assert.Null(await _service.DifficultyProfiles.GetAsync(created.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.DifficultyProfiles.DeleteAsync(created.Id));
    }

    [Fact]
    public async Task MonsterTypes_NormalizeTheKey_AndRefuseDuplicates()
    {
        var created = await _service.MonsterTypes.CreateAsync(new SurvivalMonsterTypeUpsertDto { SiegeScenarioId = 100, EntityType = "minecraft:cave_spider", Weight = 0.5, UnlockRound = 4 });
        Assert.Equal("CAVE_SPIDER", created.EntityType);
        Assert.Equal(1, created.SortOrder); // the seeded types both have order 0
        await Assert.ThrowsAsync<ArgumentException>(() => _service.MonsterTypes.CreateAsync(new SurvivalMonsterTypeUpsertDto { SiegeScenarioId = 100, EntityType = "zombie" }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.MonsterTypes.CreateAsync(new SurvivalMonsterTypeUpsertDto { SiegeScenarioId = 100, EntityType = "not a key!" }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.MonsterTypes.CreateAsync(new SurvivalMonsterTypeUpsertDto { SiegeScenarioId = 100, EntityType = "WITCH", Weight = 0 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.MonsterTypes.CreateAsync(new SurvivalMonsterTypeUpsertDto { SiegeScenarioId = 100, EntityType = "WITCH", UnlockRound = 0 }));
        Assert.Equal(3, (await _service.MonsterTypes.ListAsync(100)).Count);
    }

    [Fact]
    public async Task SpecialWaves_ProfileKeyIsUnique_EntriesCheckTheirKind_AndTheProfilesScenario()
    {
        var profile = await _service.SpecialWaveProfiles.CreateAsync(new SurvivalSpecialWaveProfileUpsertDto { SiegeScenarioId = 100, Key = "Boss-Wave", Name = "Boss", CountOverride = 1, Additive = true });
        Assert.Equal("boss-wave", profile.Key);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SpecialWaveProfiles.CreateAsync(new SurvivalSpecialWaveProfileUpsertDto { SiegeScenarioId = 100, Key = "wolves", Name = "Dup" }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SpecialWaveProfiles.CreateAsync(new SurvivalSpecialWaveProfileUpsertDto { SiegeScenarioId = 100, Key = "x", Name = "x", CountMultiplier = 0 }));

        var type = await _service.SpecialWaveMonsterTypes.CreateAsync(new SurvivalSpecialWaveMonsterTypeUpsertDto { SurvivalSpecialWaveProfileId = profile.Id, EntityType = "ravager" });
        Assert.Equal("RAVAGER", type.EntityType);
        Assert.Equal("RAVAGER", Assert.Single((await _service.SpecialWaveProfiles.GetAsync(profile.Id))!.MonsterTypes).EntityType);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.SpecialWaveMonsterTypes.CreateAsync(new SurvivalSpecialWaveMonsterTypeUpsertDto { SurvivalSpecialWaveProfileId = 999, EntityType = "x" }));

        var explicitEntry = await _service.SpecialWaveEntries.CreateAsync(new SurvivalSpecialWaveEntryUpsertDto { SiegeScenarioId = 100, SurvivalSpecialWaveProfileId = profile.Id, Kind = SurvivalSpecialWaveKind.Explicit, Wave = 10, Interval = 99 });
        Assert.Null(explicitEntry.Interval);
        Assert.Equal(10, explicitEntry.Wave);
        Assert.Equal("boss-wave", explicitEntry.ProfileKey);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SpecialWaveEntries.CreateAsync(new SurvivalSpecialWaveEntryUpsertDto { SiegeScenarioId = 100, SurvivalSpecialWaveProfileId = profile.Id, Kind = SurvivalSpecialWaveKind.Explicit }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SpecialWaveEntries.CreateAsync(new SurvivalSpecialWaveEntryUpsertDto { SiegeScenarioId = 100, SurvivalSpecialWaveProfileId = profile.Id, Kind = SurvivalSpecialWaveKind.Recurring }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SpecialWaveEntries.CreateAsync(new SurvivalSpecialWaveEntryUpsertDto { SiegeScenarioId = 100, SurvivalSpecialWaveProfileId = 999, Kind = SurvivalSpecialWaveKind.Recurring, Interval = 2 }));

        // A profile of another scenario can't be scheduled here.
        _context.SiegeScenarios.Add(new SiegeScenario { Id = 101, Name = "Other", TownId = 1, HubLocationId = 1000 });
        _context.SurvivalSpecialWaveProfiles.Add(new SurvivalSpecialWaveProfile { Id = 29, SiegeScenarioId = 101, Key = "foreign", Name = "Foreign" });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<ArgumentException>(() => NewService().SpecialWaveEntries.CreateAsync(new SurvivalSpecialWaveEntryUpsertDto { SiegeScenarioId = 100, SurvivalSpecialWaveProfileId = 29, Kind = SurvivalSpecialWaveKind.Recurring, Interval = 2 }));

        // Deleting the profile takes its schedule entries with it (cascade), and its monster types.
        await _service.SpecialWaveProfiles.DeleteAsync(profile.Id);
        Assert.Null(await _service.SpecialWaveEntries.GetAsync(explicitEntry.Id));
        Assert.Null(await _service.SpecialWaveMonsterTypes.GetAsync(type.Id));
    }

    [Fact]
    public async Task SpawnZones_ResolveLocations_CheckPrerequisites_AndOwnTheirPoints()
    {
        _locations.Setup(l => l.CreateAsync(It.IsAny<LocationDto>())).ReturnsAsync(new LocationDto { Id = 1002 });
        var zone = await _service.SpawnZones.CreateAsync(new SurvivalSpawnZoneUpsertDto
        {
            SiegeScenarioId = 100, Name = "Keep yard", AnchorLocation = new LocationDto { Name = "a", X = 1, Y = 64, Z = 1, World = "world" },
            PrerequisitePolicy = SurvivalPrerequisitePolicy.Any, PrerequisiteObjectiveIds = new() { 501, 502 }, MinWave = 3, MaxWave = 20, AreaRadius = 4
        });
        Assert.Equal(1002, zone.AnchorLocationId);
        Assert.Equal(new[] { 501, 502 }, zone.PrerequisiteObjectiveIds);
        Assert.Equal(1, zone.SortOrder); // the seeded zones both have order 0

        await _service.SpawnZones.UpdateAsync(zone.Id, new SurvivalSpawnZoneUpsertDto { Name = "Keep yard", AnchorLocationId = 1002, PrerequisiteObjectiveIds = new() { 502 } });
        Assert.Equal(new[] { 502 }, (await _service.SpawnZones.GetAsync(zone.Id))!.PrerequisiteObjectiveIds);
        await _service.SpawnZones.UpdateAsync(zone.Id, new SurvivalSpawnZoneUpsertDto { Name = "Keep yard", AnchorLocationId = 1002, PrerequisiteObjectiveIds = null });
        Assert.Equal(new[] { 502 }, (await _service.SpawnZones.GetAsync(zone.Id))!.PrerequisiteObjectiveIds);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.SpawnZones.CreateAsync(new SurvivalSpawnZoneUpsertDto { SiegeScenarioId = 100, Name = "x", AnchorLocationId = 1002, PrerequisiteObjectiveIds = new() { 777 } }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SpawnZones.CreateAsync(new SurvivalSpawnZoneUpsertDto { SiegeScenarioId = 100, Name = "x" }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SpawnZones.CreateAsync(new SurvivalSpawnZoneUpsertDto { SiegeScenarioId = 100, Name = "x", AnchorLocationId = 1002, Weight = 0 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SpawnZones.CreateAsync(new SurvivalSpawnZoneUpsertDto { SiegeScenarioId = 100, Name = "x", AnchorLocationId = 1002, MinWave = 5, MaxWave = 4 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SpawnZones.CreateAsync(new SurvivalSpawnZoneUpsertDto { SiegeScenarioId = 100, Name = "x", AnchorLocationId = 99999 }));

        var lane1 = await _service.SpawnZonePoints.CreateAsync(new SurvivalSpawnZonePointUpsertDto { SurvivalSpawnZoneId = zone.Id, Kind = SurvivalSpawnZonePointKind.Lane, LocationId = 1003 });
        var lane2 = await _service.SpawnZonePoints.CreateAsync(new SurvivalSpawnZonePointUpsertDto { SurvivalSpawnZoneId = zone.Id, Kind = SurvivalSpawnZonePointKind.Lane, LocationId = 1004 });
        var spawn = await _service.SpawnZonePoints.CreateAsync(new SurvivalSpawnZonePointUpsertDto { SurvivalSpawnZoneId = zone.Id, Kind = SurvivalSpawnZonePointKind.Spawn, LocationId = 1001, Name = "Gate" });
        Assert.Equal(0, lane1.SortOrder);
        Assert.Equal(1, lane2.SortOrder);
        Assert.Equal(0, spawn.SortOrder); // its own sequence per kind
        Assert.NotNull(spawn.Location);
        var points = (await _service.SpawnZones.GetAsync(zone.Id))!.Points;
        Assert.Equal(3, points.Count);
        Assert.Equal(SurvivalSpawnZonePointKind.Spawn, points[0].Kind);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.SpawnZonePoints.CreateAsync(new SurvivalSpawnZonePointUpsertDto { SurvivalSpawnZoneId = 999, LocationId = 1001 }));

        await _service.SpawnZones.DeleteAsync(zone.Id);
        Assert.Null(await _service.SpawnZonePoints.GetAsync(spawn.Id));
        Assert.True(await _context.Locations.AnyAsync(l => l.Id == 1002)); // the Location row stays
    }

    [Fact]
    public async Task TargetOverrides_NameExactlyOneTargetOfTheScenario_OncePerTarget()
    {
        var created = await _service.TargetOverrides.CreateAsync(new SurvivalTargetOverrideUpsertDto { SiegeScenarioId = 100, SiegeObjectiveId = 501, Mode = SurvivalTargetOverrideMode.Deny });
        Assert.Equal("Keep", created.SiegeObjectiveName);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.TargetOverrides.CreateAsync(new SurvivalTargetOverrideUpsertDto { SiegeScenarioId = 100, SiegeObjectiveId = 501, Mode = SurvivalTargetOverrideMode.Allow }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.TargetOverrides.CreateAsync(new SurvivalTargetOverrideUpsertDto { SiegeScenarioId = 100, GateStructureId = 400 })); // already has one
        await Assert.ThrowsAsync<ArgumentException>(() => _service.TargetOverrides.CreateAsync(new SurvivalTargetOverrideUpsertDto { SiegeScenarioId = 100, GateStructureId = 401 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.TargetOverrides.CreateAsync(new SurvivalTargetOverrideUpsertDto { SiegeScenarioId = 100, SiegeObjectiveId = 502, GateStructureId = 400 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.TargetOverrides.CreateAsync(new SurvivalTargetOverrideUpsertDto { SiegeScenarioId = 100 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.TargetOverrides.CreateAsync(new SurvivalTargetOverrideUpsertDto { SiegeScenarioId = 100, SiegeObjectiveId = 999 }));

        await _service.TargetOverrides.UpdateAsync(created.Id, new SurvivalTargetOverrideUpsertDto { SiegeObjectiveId = 502, Mode = SurvivalTargetOverrideMode.Allow });
        var updated = await _service.TargetOverrides.GetAsync(created.Id);
        Assert.Equal(502, updated!.SiegeObjectiveId);
        Assert.Equal(SurvivalTargetOverrideMode.Allow, updated.Mode);
        Assert.Equal(2, (await _service.TargetOverrides.ListAsync(100)).Count);
    }

    [Fact]
    public async Task PurchasePoints_AndOffers()
    {
        var point = await _service.PurchasePoints.CreateAsync(new SurvivalPurchasePointUpsertDto { SiegeScenarioId = 100, Name = "Smithy", LocationId = 1001, InteractionRange = 5 });
        Assert.NotNull(point.Location);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.PurchasePoints.CreateAsync(new SurvivalPurchasePointUpsertDto { SiegeScenarioId = 100, Name = "x", LocationId = 1001, InteractionRange = 0 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.PurchasePoints.CreateAsync(new SurvivalPurchasePointUpsertDto { SiegeScenarioId = 100, Name = "x" }));

        var offer = await _service.PurchaseOffers.CreateAsync(new SurvivalPurchaseOfferUpsertDto { SurvivalPurchasePointId = point.Id, ItemBlueprintId = SurvivalTestData.BlueprintBow, Quantity = 2, Price = 300 });
        Assert.Equal("Bow", offer.ItemBlueprintName);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.PurchaseOffers.CreateAsync(new SurvivalPurchaseOfferUpsertDto { SurvivalPurchasePointId = point.Id, ItemBlueprintId = 4242 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.PurchaseOffers.CreateAsync(new SurvivalPurchaseOfferUpsertDto { SurvivalPurchasePointId = point.Id, ItemBlueprintId = SurvivalTestData.BlueprintBow, Price = -1 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.PurchaseOffers.CreateAsync(new SurvivalPurchaseOfferUpsertDto { SurvivalPurchasePointId = point.Id, ItemBlueprintId = SurvivalTestData.BlueprintBow, Quantity = 0 }));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.PurchaseOffers.CreateAsync(new SurvivalPurchaseOfferUpsertDto { SurvivalPurchasePointId = 999, ItemBlueprintId = SurvivalTestData.BlueprintBow }));
        Assert.Single((await _service.PurchasePoints.GetAsync(point.Id))!.Offers);

        await _service.PurchasePoints.DeleteAsync(point.Id);
        Assert.Null(await _service.PurchaseOffers.GetAsync(offer.Id));
    }

    [Fact]
    public async Task MysteryBox_PoolsEntriesAndPoints()
    {
        var pool = await _service.MysteryBoxPools.CreateAsync(new SurvivalMysteryBoxPoolUpsertDto { SiegeScenarioId = 100, Name = "Tools", Variant = SurvivalMysteryBoxVariant.Items });
        var entry = await _service.MysteryBoxEntries.CreateAsync(new SurvivalMysteryBoxEntryUpsertDto
        {
            SurvivalMysteryBoxPoolId = pool.Id, ItemBlueprintId = SurvivalTestData.BlueprintSword, ChancePercent = 33.33m,
            EnchantmentDefinitionId = SurvivalTestData.EnchantPower, EnchantmentLevel = 2, // ignored for an items pool
            Enchantments = new() { new() { EnchantmentDefinitionId = SurvivalTestData.EnchantSharpness, Level = 3, ChancePercent = 12.5m } }
        });
        Assert.Null(entry.EnchantmentDefinitionId);
        Assert.Equal(3333, entry.ChanceBasisPoints);
        Assert.Equal(1250, Assert.Single(entry.Enchantments).ChanceBasisPoints);
        Assert.Equal("minecraft:sharpness", entry.Enchantments[0].EnchantmentKey);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.MysteryBoxEntries.CreateAsync(new SurvivalMysteryBoxEntryUpsertDto { SurvivalMysteryBoxPoolId = pool.Id, ItemBlueprintId = SurvivalTestData.BlueprintSword, ChancePercent = 33.333m }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.MysteryBoxEntries.CreateAsync(new SurvivalMysteryBoxEntryUpsertDto { SurvivalMysteryBoxPoolId = pool.Id, ItemBlueprintId = SurvivalTestData.BlueprintSword, ChancePercent = 0 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.MysteryBoxEntries.CreateAsync(new SurvivalMysteryBoxEntryUpsertDto { SurvivalMysteryBoxPoolId = pool.Id, ChancePercent = 10 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.MysteryBoxEntries.CreateAsync(new SurvivalMysteryBoxEntryUpsertDto { SurvivalMysteryBoxPoolId = pool.Id, ItemBlueprintId = 4242, ChancePercent = 10 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.MysteryBoxEntries.CreateAsync(new SurvivalMysteryBoxEntryUpsertDto
        {
            SurvivalMysteryBoxPoolId = pool.Id, ItemBlueprintId = SurvivalTestData.BlueprintSword, ChancePercent = 10,
            Enchantments = new() { new() { EnchantmentDefinitionId = SurvivalTestData.EnchantSharpness }, new() { EnchantmentDefinitionId = SurvivalTestData.EnchantSharpness } }
        }));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.MysteryBoxEntries.CreateAsync(new SurvivalMysteryBoxEntryUpsertDto { SurvivalMysteryBoxPoolId = 999, ItemBlueprintId = SurvivalTestData.BlueprintSword }));

        // Replace-set of the enchantments; null leaves them.
        await _service.MysteryBoxEntries.UpdateAsync(entry.Id, new SurvivalMysteryBoxEntryUpsertDto { ItemBlueprintId = SurvivalTestData.BlueprintSword, ChancePercent = 66.67m, Enchantments = null });
        Assert.Single((await _service.MysteryBoxEntries.GetAsync(entry.Id))!.Enchantments);
        await _service.MysteryBoxEntries.UpdateAsync(entry.Id, new SurvivalMysteryBoxEntryUpsertDto { ItemBlueprintId = SurvivalTestData.BlueprintSword, ChancePercent = 100, Enchantments = new() });
        var reread = await _service.MysteryBoxPools.GetAsync(pool.Id);
        Assert.Empty(reread!.Entries[0].Enchantments);
        Assert.Equal(100m, reread.TotalChancePercent);

        // Books pool: an enchantment and level, no blueprint.
        var books = await _service.MysteryBoxPools.CreateAsync(new SurvivalMysteryBoxPoolUpsertDto { SiegeScenarioId = 100, Name = "More books", Variant = SurvivalMysteryBoxVariant.EnchantedBooks });
        var book = await _service.MysteryBoxEntries.CreateAsync(new SurvivalMysteryBoxEntryUpsertDto { SurvivalMysteryBoxPoolId = books.Id, EnchantmentDefinitionId = SurvivalTestData.EnchantPower, EnchantmentLevel = 2, ItemBlueprintId = SurvivalTestData.BlueprintBow, ChancePercent = 100 });
        Assert.Null(book.ItemBlueprintId);
        Assert.Equal("minecraft:power", book.EnchantmentKey);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.MysteryBoxEntries.CreateAsync(new SurvivalMysteryBoxEntryUpsertDto { SurvivalMysteryBoxPoolId = books.Id, EnchantmentDefinitionId = SurvivalTestData.EnchantPower, EnchantmentLevel = 0, ChancePercent = 1 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.MysteryBoxEntries.CreateAsync(new SurvivalMysteryBoxEntryUpsertDto { SurvivalMysteryBoxPoolId = books.Id, ItemBlueprintId = SurvivalTestData.BlueprintBow, ChancePercent = 1 }));

        // Points: block key normalisation, pool of the same scenario, 409 on deleting a used pool.
        var point = await _service.MysteryBoxPoints.CreateAsync(new SurvivalMysteryBoxPointUpsertDto { SiegeScenarioId = 100, Name = "Tool box", LocationId = 1004, SurvivalMysteryBoxPoolId = pool.Id, BlockType = "ENCHANTING_TABLE", RollPrice = 1500 });
        Assert.Equal("minecraft:enchanting_table", point.BlockType);
        Assert.Equal("Tools", point.PoolName);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.MysteryBoxPoints.CreateAsync(new SurvivalMysteryBoxPointUpsertDto { SiegeScenarioId = 100, Name = "x", LocationId = 1004, SurvivalMysteryBoxPoolId = pool.Id, BlockType = "not a block!" }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.MysteryBoxPoints.CreateAsync(new SurvivalMysteryBoxPointUpsertDto { SiegeScenarioId = 100, Name = "x", LocationId = 1004, SurvivalMysteryBoxPoolId = 999 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.MysteryBoxPoints.CreateAsync(new SurvivalMysteryBoxPointUpsertDto { SiegeScenarioId = 100, Name = "x", LocationId = 1004, SurvivalMysteryBoxPoolId = pool.Id, RollPrice = -1 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.MysteryBoxPools.DeleteAsync(pool.Id));

        await _service.MysteryBoxPoints.DeleteAsync(point.Id);
        await _service.MysteryBoxPools.DeleteAsync(pool.Id);
        Assert.Null(await _service.MysteryBoxEntries.GetAsync(entry.Id));
    }

    [Fact]
    public async Task ChildLists_AreEmptyForUnknownParents_AndGetsNullForUnknownIds()
    {
        Assert.Empty(await _service.MonsterTypes.ListAsync(12345));
        Assert.Empty(await _service.SpawnZonePoints.ListAsync(0));
        Assert.Null(await _service.SpawnZones.GetAsync(0));
        Assert.Null(await _service.MysteryBoxPoints.GetAsync(12345));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.SpawnZones.UpdateAsync(12345, new SurvivalSpawnZoneUpsertDto { Name = "x", AnchorLocationId = 1002 }));
    }
}
