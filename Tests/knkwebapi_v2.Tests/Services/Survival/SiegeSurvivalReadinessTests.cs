using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Survival;

/// <summary>
/// Survival readiness (DESIGN §10; PandiO/knk-workspace#41, link S4): every rule, one at a time, on
/// SurvivalTestData.ReadyGraph(). Codes mirror knk-core's survival ReadinessIssue codes.
/// </summary>
public class SiegeSurvivalReadinessTests
{
    private static SurvivalReadinessDto Evaluate(SurvivalScenarioGraph graph, SurvivalLobbySettings? lobby = null, int? profileId = null) =>
        SiegeSurvivalReadiness.Evaluate(graph, lobby, profileId);

    private static void AssertError(SurvivalReadinessDto result, string code) =>
        Assert.True(result.Errors.Any(e => e.Code == code), $"expected error {code}, got errors [{string.Join(", ", result.Errors.Select(e => e.Code))}]");

    private static void AssertWarning(SurvivalReadinessDto result, string code) =>
        Assert.True(result.Warnings.Any(w => w.Code == code), $"expected warning {code}, got warnings [{string.Join(", ", result.Warnings.Select(w => w.Code))}]");

    private static void AssertNo(SurvivalReadinessDto result, string code)
    {
        Assert.DoesNotContain(result.Errors, e => e.Code == code);
        Assert.DoesNotContain(result.Warnings, w => w.Code == code);
    }

    [Fact]
    public void TheReadyGraph_IsReady_WithoutErrors()
    {
        var result = Evaluate(SurvivalTestData.ReadyGraph());
        Assert.True(result.IsReady, string.Join("; ", result.Errors.Select(e => e.Code + ": " + e.Message)));
        Assert.Empty(result.Errors);
        // The Siege warning about instant victory is replaced by the survival end-condition rules.
        AssertNo(result, SiegeReadinessCodes.NoInstantVictoryObjective);
    }

    [Fact]
    public void SiegeStructuralErrors_StillApply()
    {
        var graph = SurvivalTestData.ReadyGraph();
        graph.Scenario.Teams.Clear();
        var result = Evaluate(graph);
        AssertError(result, SiegeReadinessCodes.TeamsMinTwo);
        AssertError(result, SurvivalReadinessCodes.TeamHumanMissing);
        AssertError(result, SurvivalReadinessCodes.TeamNpcMissing);
    }

    [Fact]
    public void NoSettings_IsAnError_AndStopsThere()
    {
        var graph = new SurvivalScenarioGraph { Scenario = SiegeTestData.ValidScenario() };
        var result = Evaluate(graph);
        AssertError(result, SurvivalReadinessCodes.SettingsMissing);
        Assert.False(result.IsReady);
        AssertNo(result, SurvivalReadinessCodes.ProfileNone);
    }

    [Fact]
    public void DisabledSettings_Warn()
    {
        var graph = SurvivalTestData.ReadyGraph();
        graph.Settings!.Enabled = false;
        AssertWarning(Evaluate(graph), SurvivalReadinessCodes.SettingsDisabled);
    }

    [Fact]
    public void MinPlayersAboveTheScenarioMax_IsAnError()
    {
        var graph = SurvivalTestData.ReadyGraph();
        graph.Settings!.MinPlayers = 99;
        AssertError(Evaluate(graph), SurvivalReadinessCodes.SettingsMinPlayersAboveMax);
    }

    // ---- Teams ----

    [Fact]
    public void Teams_DefaultToFirstDefenderAndFirstAttacker()
    {
        var graph = SurvivalTestData.ReadyGraph();
        var teams = graph.Scenario.Teams.ToList();
        Assert.Equal(201, SiegeSurvivalReadiness.ResolveHumanTeam(graph.Settings!, teams)!.Id);
        Assert.Equal(202, SiegeSurvivalReadiness.ResolveNpcTeam(graph.Settings!, teams)!.Id);
    }

    [Fact]
    public void Teams_SameTeam_SameAlliance_ForeignTeam()
    {
        var graph = SurvivalTestData.ReadyGraph();
        graph.Settings!.HumanTeamId = 202;
        graph.Settings.NpcTeamId = 202;
        AssertError(Evaluate(graph), SurvivalReadinessCodes.TeamSame);

        graph.Scenario.Teams.First(t => t.Id == 202).AllianceGroup = 1;
        graph.Settings.HumanTeamId = 201;
        AssertError(Evaluate(graph), SurvivalReadinessCodes.TeamSameAlliance);

        graph.Settings.NpcTeamId = 999;
        var result = Evaluate(graph);
        AssertError(result, SurvivalReadinessCodes.TeamNotInScenario);
        AssertError(result, SurvivalReadinessCodes.TeamNpcMissing);
    }

    [Fact]
    public void Teams_NoAttacker_IsNpcMissing()
    {
        var graph = SurvivalTestData.ReadyGraph();
        graph.Scenario.Teams.First(t => t.Id == 202).Role = SiegeTeamRole.Defender;
        AssertError(Evaluate(graph), SurvivalReadinessCodes.TeamNpcMissing);
    }

    // ---- Equipment ----

    [Fact]
    public void KitsMode_NeedsSelectedKits_AndADefaultAmongThem()
    {
        var graph = SurvivalTestData.ReadyGraph();
        graph.Settings!.EquipmentMode = SurvivalEquipmentMode.Kits;
        var result = Evaluate(graph);
        AssertError(result, SurvivalReadinessCodes.KitsNoneSelected);
        AssertError(result, SurvivalReadinessCodes.KitsDefaultMissing);

        graph.Settings.SelectedKits.Add(new SurvivalSettingsKit { KitId = SurvivalTestData.KitSword });
        graph.Settings.DefaultKitId = SurvivalTestData.KitBow;
        result = Evaluate(graph);
        AssertNo(result, SurvivalReadinessCodes.KitsNoneSelected);
        AssertError(result, SurvivalReadinessCodes.KitsDefaultNotSelected);

        graph.Settings.DefaultKitId = SurvivalTestData.KitSword;
        result = Evaluate(graph);
        Assert.True(result.IsReady, string.Join("; ", result.Errors.Select(e => e.Code)));
    }

    [Fact]
    public void UnknownKits_AreErrors_InEveryMode()
    {
        var graph = SurvivalTestData.ReadyGraph();
        graph.Settings!.SelectedKits.Add(new SurvivalSettingsKit { KitId = 12345 });
        AssertError(Evaluate(graph), SurvivalReadinessCodes.KitsUnknown);

        graph = SurvivalTestData.ReadyGraph();
        graph.Settings!.EquipmentMode = SurvivalEquipmentMode.MatchProgression;
        graph.Settings.BaselineLoadoutKitId = 12345;
        AssertError(Evaluate(graph), SurvivalReadinessCodes.LoadoutUnknownKit);

        graph.Settings.BaselineLoadoutKitId = null; // empty inventory is fine
        Assert.True(Evaluate(graph).IsReady);
    }

    // ---- End conditions ----

    [Fact]
    public void WipeOnly_Warns_WhenNoFlagAndAllCapturedOff()
    {
        var graph = SurvivalTestData.ReadyGraph();
        AssertNo(Evaluate(graph), SurvivalReadinessCodes.EndWipeOnly); // Keep is insta-lose

        graph.Scenario.Objectives.First(o => o.Id == 501).InstantVictory = false;
        var result = Evaluate(graph);
        AssertWarning(result, SurvivalReadinessCodes.EndWipeOnly);
        Assert.True(result.IsReady);

        graph.Settings!.EndWhenAllObjectivesCaptured = true;
        AssertNo(Evaluate(graph), SurvivalReadinessCodes.EndWipeOnly);
    }

    [Fact]
    public void LobbyOverride_Wins_OverTheScenario()
    {
        var graph = SurvivalTestData.ReadyGraph();
        graph.Scenario.Objectives.First(o => o.Id == 501).InstantVictory = false;
        var on = new SurvivalLobbySettings { SiegeLobbyId = 7, AllCapturedOverride = SurvivalAllCapturedOverride.On };
        AssertNo(Evaluate(graph, on), SurvivalReadinessCodes.EndWipeOnly);

        graph.Settings!.EndWhenAllObjectivesCaptured = true;
        var off = new SurvivalLobbySettings { SiegeLobbyId = 7, AllCapturedOverride = SurvivalAllCapturedOverride.Off };
        AssertWarning(Evaluate(graph, off), SurvivalReadinessCodes.EndWipeOnly);
        var inherit = new SurvivalLobbySettings { SiegeLobbyId = 7, AllCapturedOverride = SurvivalAllCapturedOverride.Inherit };
        var result = Evaluate(graph, inherit);
        AssertNo(result, SurvivalReadinessCodes.EndWipeOnly);
        Assert.Equal(7, result.SiegeLobbyId);
    }

    [Fact]
    public void AllCapturedWithoutObjectives_Warns()
    {
        var graph = SurvivalTestData.ReadyGraph();
        graph.Scenario.Objectives.Clear();
        graph.Settings!.EndWhenAllObjectivesCaptured = true;
        var result = Evaluate(graph);
        AssertWarning(result, SurvivalReadinessCodes.EndAllCapturedUnreachable);
        AssertError(result, SiegeReadinessCodes.ObjectivesMinOne);
    }

    // ---- Difficulty profiles ----

    [Fact]
    public void Profiles_NoneIsAnError_NoDefaultWarns_UnknownRequestedIsAnError()
    {
        var graph = SurvivalTestData.ReadyGraph();
        graph.DifficultyProfiles.Clear();
        AssertError(Evaluate(graph), SurvivalReadinessCodes.ProfileNone);

        graph = SurvivalTestData.ReadyGraph();
        graph.DifficultyProfiles[0].IsDefault = false;
        AssertWarning(Evaluate(graph), SurvivalReadinessCodes.ProfileDefaultMissing);

        graph.DifficultyProfiles[0].IsDefault = true;
        graph.DifficultyProfiles.Add(SurvivalTestData.Profile(11));
        AssertWarning(Evaluate(graph), SurvivalReadinessCodes.ProfileMultipleDefault);

        AssertError(Evaluate(graph, null, 999), SurvivalReadinessCodes.ProfileUnknown);
        Assert.Equal(999, Evaluate(graph, null, 999).DifficultyProfileId);
    }

    [Theory]
    [InlineData("count-base", SurvivalReadinessCodes.ScalingCountBase)]
    [InlineData("count-cap", SurvivalReadinessCodes.ScalingCountCap)]
    [InlineData("multiplier", SurvivalReadinessCodes.ScalingMultiplier)]
    [InlineData("switch-round", SurvivalReadinessCodes.ScalingSwitchRound)]
    [InlineData("stat-cap", SurvivalReadinessCodes.ScalingStatCap)]
    [InlineData("repair-rate", SurvivalReadinessCodes.ScalingRepairRate)]
    public void Scaling_ParameterErrors(string change, string code)
    {
        var graph = SurvivalTestData.ReadyGraph();
        var p = graph.DifficultyProfiles[0];
        switch (change)
        {
            case "count-base": p.CountBase = 0; break;
            case "count-cap": p.CountCap = 3; break;
            case "multiplier": p.StatMultiplier = 0; break;
            case "switch-round": p.StatSwitchRound = 0; break;
            case "stat-cap": p.StatCap = 0.5; break;
            case "repair-rate": p.RepairRatePerSecond = 0; break;
        }
        AssertError(Evaluate(graph), code);
    }

    [Fact]
    public void Scaling_FlatCurves_Warn()
    {
        var graph = SurvivalTestData.ReadyGraph();
        var p = graph.DifficultyProfiles[0];
        p.CountStep = 0;
        p.StatStep = 0;
        p.StatMultiplier = 1;
        var result = Evaluate(graph);
        AssertWarning(result, SurvivalReadinessCodes.ScalingCountFlat);
        AssertWarning(result, SurvivalReadinessCodes.ScalingStatFlat);
        Assert.True(result.IsReady);
    }

    [Fact]
    public void WalkingSpeed_AboveTheHardCap_Warns_AboveRecommended_Warns()
    {
        var graph = SurvivalTestData.ReadyGraph(); // zombie 0.23 and skeleton 0.25, cap 1.5 → 0.375: fine
        AssertNo(Evaluate(graph), SurvivalReadinessCodes.ScalingWalkingSpeedFast);
        AssertNo(Evaluate(graph), SurvivalReadinessCodes.ScalingWalkingSpeedHardCap);

        graph.DifficultyProfiles[0].StatCap = 1.8; // 0.25 × 1.8 = 0.45
        var result = Evaluate(graph);
        AssertWarning(result, SurvivalReadinessCodes.ScalingWalkingSpeedFast);
        Assert.True(result.IsReady);

        graph.DifficultyProfiles[0].StatCap = 3; // 0.75
        result = Evaluate(graph);
        AssertWarning(result, SurvivalReadinessCodes.ScalingWalkingSpeedHardCap);
        Assert.True(result.IsReady);

        graph.DifficultyProfiles[0].WalkingSpeedWeight = 0; // speed stays vanilla whatever the cap
        AssertNo(Evaluate(graph), SurvivalReadinessCodes.ScalingWalkingSpeedHardCap);
    }

    // ---- Monster mix ----

    [Fact]
    public void Mix_EmptyIsAnError_NoneInRound1IsAnError_DuplicateIsAnError()
    {
        var graph = SurvivalTestData.ReadyGraph();
        graph.MonsterTypes.Clear();
        AssertError(Evaluate(graph), SurvivalReadinessCodes.MixEmpty);

        graph = SurvivalTestData.ReadyGraph();
        graph.MonsterTypes.ForEach(t => t.UnlockRound = 2);
        AssertError(Evaluate(graph), SurvivalReadinessCodes.MixNoneInRound1);

        graph = SurvivalTestData.ReadyGraph();
        graph.MonsterTypes.Add(new SurvivalMonsterType { Id = 13, SiegeScenarioId = 100, EntityType = "zombie" });
        AssertError(Evaluate(graph), SurvivalReadinessCodes.MixDuplicate);
    }

    [Fact]
    public void SpecialProfileMix_EmptyMeansTheNormalMix_OwnMixHasTheMixRules()
    {
        var graph = SurvivalTestData.ReadyGraph();
        graph.SpecialWaveProfiles[0].MonsterTypes.Clear();
        AssertNo(Evaluate(graph), SurvivalReadinessCodes.MixEmpty);

        graph.SpecialWaveProfiles[0].MonsterTypes.Add(new SurvivalSpecialWaveMonsterType { EntityType = "WOLF", UnlockRound = 5 });
        var result = Evaluate(graph);
        AssertError(result, SurvivalReadinessCodes.MixNoneInRound1);
        Assert.Equal(nameof(SurvivalSpecialWaveProfile), result.Errors.First(e => e.Code == SurvivalReadinessCodes.MixNoneInRound1).EntityType);
    }

    // ---- Special waves ----

    [Fact]
    public void Special_UnknownProfile_InvalidEntry()
    {
        var graph = SurvivalTestData.ReadyGraph();
        graph.SpecialWaveEntries[0].SurvivalSpecialWaveProfileId = 999;
        AssertError(Evaluate(graph), SurvivalReadinessCodes.SpecialUnknownProfile);

        graph = SurvivalTestData.ReadyGraph();
        graph.SpecialWaveEntries[0].Interval = null;
        AssertError(Evaluate(graph), SurvivalReadinessCodes.SpecialEntryInvalid);
        graph = SurvivalTestData.ReadyGraph();
        graph.SpecialWaveEntries[1].Wave = 0;
        AssertError(Evaluate(graph), SurvivalReadinessCodes.SpecialEntryInvalid);
    }

    [Fact]
    public void Special_TwoExplicitEntriesOnOneWave_FailOnlyWithDifferentProfiles()
    {
        var graph = SurvivalTestData.ReadyGraph();
        graph.SpecialWaveEntries.Add(new SurvivalSpecialWaveEntry { Id = 27, SiegeScenarioId = 100, SurvivalSpecialWaveProfileId = 20, Kind = SurvivalSpecialWaveKind.Explicit, Wave = 7 });
        AssertNo(Evaluate(graph), SurvivalReadinessCodes.SpecialExplicitConflict);

        graph.SpecialWaveProfiles.Add(new SurvivalSpecialWaveProfile { Id = 22, SiegeScenarioId = 100, Key = "boss", Name = "Boss" });
        graph.SpecialWaveEntries[2].SurvivalSpecialWaveProfileId = 22;
        AssertError(Evaluate(graph), SurvivalReadinessCodes.SpecialExplicitConflict);
    }

    [Fact]
    public void Special_ConflictingRecurringEntries_NeedUniquePriorities()
    {
        var graph = SurvivalTestData.ReadyGraph();
        // every 5 from 5 (5, 10, 15 …) and every 3 from 1 (1, 4, 7, 10 …): both hit 10
        graph.SpecialWaveEntries.Add(new SurvivalSpecialWaveEntry { Id = 28, SiegeScenarioId = 100, SurvivalSpecialWaveProfileId = 20, Kind = SurvivalSpecialWaveKind.Recurring, Interval = 3, StartWave = 1, Priority = 1 });
        AssertError(Evaluate(graph), SurvivalReadinessCodes.SpecialRecurringPriority);

        graph.SpecialWaveEntries[2].Priority = 2;
        AssertNo(Evaluate(graph), SurvivalReadinessCodes.SpecialRecurringPriority);

        // every 4 from 2 (2, 6, 10 …) and every 4 from 1 (1, 5, 9 …) never meet: the same priority is fine
        graph.SpecialWaveEntries[2] = new SurvivalSpecialWaveEntry { Id = 28, SiegeScenarioId = 100, SurvivalSpecialWaveProfileId = 20, Kind = SurvivalSpecialWaveKind.Recurring, Interval = 4, StartWave = 2, Priority = 1 };
        graph.SpecialWaveEntries[0] = new SurvivalSpecialWaveEntry { Id = 25, SiegeScenarioId = 100, SurvivalSpecialWaveProfileId = 20, Kind = SurvivalSpecialWaveKind.Recurring, Interval = 4, StartWave = 1, Priority = 1 };
        AssertNo(Evaluate(graph), SurvivalReadinessCodes.SpecialRecurringPriority);
    }

    // ---- Spawnzones ----

    [Fact]
    public void Spawn_NeedsAnInitialUsableZone()
    {
        var graph = SurvivalTestData.ReadyGraph();
        graph.SpawnZones[0].PrerequisiteObjectives.Add(new SurvivalSpawnZoneObjective { SiegeObjectiveId = 501 });
        AssertError(Evaluate(graph), SurvivalReadinessCodes.SpawnNoInitialZone);

        graph = SurvivalTestData.ReadyGraph();
        graph.SpawnZones[0].MinWave = 2;
        AssertError(Evaluate(graph), SurvivalReadinessCodes.SpawnNoInitialZone);

        graph = SurvivalTestData.ReadyGraph();
        graph.SpawnZones[0].Points.Clear();
        var result = Evaluate(graph);
        AssertError(result, SurvivalReadinessCodes.SpawnZoneUnusable);
        AssertError(result, SurvivalReadinessCodes.SpawnNoInitialZone);

        graph.SpawnZones[0].AreaRadius = 5; // an area makes it usable again
        Assert.True(Evaluate(graph).IsReady);
    }

    [Fact]
    public void Spawn_UnknownObjective_WaveRange_SingleLanePoint()
    {
        var graph = SurvivalTestData.ReadyGraph();
        graph.SpawnZones[1].PrerequisiteObjectives.Add(new SurvivalSpawnZoneObjective { SiegeObjectiveId = 777 });
        AssertError(Evaluate(graph), SurvivalReadinessCodes.SpawnUnknownObjective);

        graph = SurvivalTestData.ReadyGraph();
        graph.SpawnZones[1].MinWave = 5;
        graph.SpawnZones[1].MaxWave = 4;
        AssertError(Evaluate(graph), SurvivalReadinessCodes.SpawnWaveRange);

        graph = SurvivalTestData.ReadyGraph();
        graph.SpawnZones[0].Points.Add(new SurvivalSpawnZonePoint { Kind = SurvivalSpawnZonePointKind.Lane, LocationId = 1003 });
        var result = Evaluate(graph);
        AssertWarning(result, SurvivalReadinessCodes.SpawnLaneSinglePoint);
        Assert.True(result.IsReady);
    }

    // ---- Target overrides ----

    [Fact]
    public void Target_InvalidRow_UnknownObjective_UnselectedGate_AllDenied()
    {
        var graph = SurvivalTestData.ReadyGraph();
        graph.TargetOverrides.Add(new SurvivalTargetOverride { Id = 60, SiegeScenarioId = 100 });
        AssertError(Evaluate(graph), SurvivalReadinessCodes.TargetInvalid);

        graph = SurvivalTestData.ReadyGraph();
        graph.TargetOverrides.Add(new SurvivalTargetOverride { Id = 61, SiegeScenarioId = 100, SiegeObjectiveId = 999, Mode = SurvivalTargetOverrideMode.Deny });
        AssertError(Evaluate(graph), SurvivalReadinessCodes.TargetUnknownObjective);

        graph = SurvivalTestData.ReadyGraph();
        graph.TargetOverrides[0].GateStructureId = 401;
        AssertError(Evaluate(graph), SurvivalReadinessCodes.TargetGateNotSelected);

        graph = SurvivalTestData.ReadyGraph();
        graph.TargetOverrides.Add(new SurvivalTargetOverride { Id = 62, SiegeScenarioId = 100, SiegeObjectiveId = 501, Mode = SurvivalTargetOverrideMode.Deny });
        graph.TargetOverrides.Add(new SurvivalTargetOverride { Id = 63, SiegeScenarioId = 100, SiegeObjectiveId = 502, Mode = SurvivalTargetOverrideMode.Deny });
        var result = Evaluate(graph);
        AssertWarning(result, SurvivalReadinessCodes.TargetAllObjectivesDenied);
        Assert.True(result.IsReady);
    }

    // ---- Purchase points ----

    [Fact]
    public void Shop_NoOffersWarns_UnknownBlueprintFails()
    {
        var graph = SurvivalTestData.ReadyGraph();
        graph.PurchasePoints[0].Offers.Clear();
        var result = Evaluate(graph);
        AssertWarning(result, SurvivalReadinessCodes.ShopNoOffers);
        Assert.True(result.IsReady);

        graph.PurchasePoints[0].Offers.Add(new SurvivalPurchaseOffer { Id = 38, ItemBlueprintId = 4242, Price = 1 });
        AssertError(Evaluate(graph), SurvivalReadinessCodes.ShopUnknownBlueprint);
    }

    // ---- Mystery Box ----

    [Fact]
    public void Box_PoolMustAddUpToExactly100Percent()
    {
        var graph = SurvivalTestData.ReadyGraph();
        var pool = graph.MysteryBoxPools[0];
        pool.Entries.First().ChancePercent = 60.49m;
        var result = Evaluate(graph);
        AssertError(result, SurvivalReadinessCodes.BoxTotal);
        Assert.Contains("99.99", result.Errors.First(e => e.Code == SurvivalReadinessCodes.BoxTotal).Message);

        pool.Entries.First().ChancePercent = 60.5m;
        Assert.True(Evaluate(graph).IsReady);
    }

    [Fact]
    public void Box_EmptyPool_ChanceRange_Variant_UnknownReferences_DuplicateEnchantment()
    {
        var graph = SurvivalTestData.ReadyGraph();
        graph.MysteryBoxPools[0].Entries.Clear();
        AssertError(Evaluate(graph), SurvivalReadinessCodes.BoxEmpty);

        graph = SurvivalTestData.ReadyGraph();
        graph.MysteryBoxPools[0].Entries.First().ChancePercent = 0;
        AssertError(Evaluate(graph), SurvivalReadinessCodes.BoxChanceRange);

        graph = SurvivalTestData.ReadyGraph();
        graph.MysteryBoxPools[0].Variant = SurvivalMysteryBoxVariant.EnchantedBooks;
        AssertError(Evaluate(graph), SurvivalReadinessCodes.BoxVariant);
        graph = SurvivalTestData.ReadyGraph();
        graph.MysteryBoxPools[1].Variant = SurvivalMysteryBoxVariant.Items;
        AssertError(Evaluate(graph), SurvivalReadinessCodes.BoxVariant);

        graph = SurvivalTestData.ReadyGraph();
        graph.MysteryBoxPools[0].Entries.First().ItemBlueprintId = 4242;
        AssertError(Evaluate(graph), SurvivalReadinessCodes.BoxUnknownBlueprint);
        graph = SurvivalTestData.ReadyGraph();
        graph.MysteryBoxPools[1].Entries.First().EnchantmentDefinitionId = 4242;
        AssertError(Evaluate(graph), SurvivalReadinessCodes.BoxUnknownEnchantment);
        graph = SurvivalTestData.ReadyGraph();
        graph.MysteryBoxPools[0].Entries.First().Enchantments.First().EnchantmentDefinitionId = 4242;
        AssertError(Evaluate(graph), SurvivalReadinessCodes.BoxUnknownEnchantment);

        graph = SurvivalTestData.ReadyGraph();
        graph.MysteryBoxPools[0].Entries.First().Enchantments.Add(new SurvivalMysteryBoxEntryEnchantment { EnchantmentDefinitionId = SurvivalTestData.EnchantSharpness, Level = 1, ChancePercent = 10 });
        AssertError(Evaluate(graph), SurvivalReadinessCodes.BoxDuplicateEnchantment);
    }

    [Fact]
    public void Box_BookLevel_UnusedPool_UnknownPointPool()
    {
        var graph = SurvivalTestData.ReadyGraph();
        graph.MysteryBoxPools[1].Entries.First().EnchantmentLevel = 0;
        AssertError(Evaluate(graph), SurvivalReadinessCodes.BoxVariant);

        graph = SurvivalTestData.ReadyGraph();
        graph.MysteryBoxPoints.RemoveAt(1);
        var result = Evaluate(graph);
        AssertWarning(result, SurvivalReadinessCodes.BoxPoolUnused);
        Assert.True(result.IsReady);

        graph = SurvivalTestData.ReadyGraph();
        graph.MysteryBoxPoints[0].SurvivalMysteryBoxPoolId = 999;
        AssertError(Evaluate(graph), SurvivalReadinessCodes.BoxPoolUnknown);
    }

    [Fact]
    public void RecurringConflict_FollowsTheGcdRule()
    {
        static SurvivalSpecialWaveEntry R(int interval, int start) => new() { Interval = interval, StartWave = start };
        Assert.True(SiegeSurvivalReadiness.RecurringConflict(R(5, 5), R(3, 1)));
        Assert.False(SiegeSurvivalReadiness.RecurringConflict(R(4, 2), R(4, 1)));
        Assert.True(SiegeSurvivalReadiness.RecurringConflict(R(4, 2), R(6, 4)));  // gcd 2, offsets 2 and 4 agree
        Assert.False(SiegeSurvivalReadiness.RecurringConflict(R(4, 2), R(6, 3))); // gcd 2, offsets differ
    }
}
