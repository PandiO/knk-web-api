using Microsoft.EntityFrameworkCore;
using knkwebapi_v2.Models;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Survival;

/// <summary>
/// Siege Survival phase 1 delete rules (PandiO/knk-workspace#41, link S4), per the Siege Phase 2 convention: every
/// survival-authored row cascades from the scenario (or its parent row); shared world/catalog rows are Restrict;
/// team references are SetNull; a target override goes with its target; a pool in use is Restrict. Asserted on the
/// EF model (they become the deferred migration's ON DELETE clauses), plus the behaviour on InMemory.
/// </summary>
public class SiegeSurvivalDeleteBehaviourTests
{
    public static IEnumerable<object[]> ExpectedDeleteRules() => new List<object[]>
    {
        new object[] { typeof(SiegeSurvivalSettings), nameof(SiegeSurvivalSettings.SiegeScenarioId), DeleteBehavior.Cascade },
        new object[] { typeof(SiegeSurvivalSettings), nameof(SiegeSurvivalSettings.HumanTeamId), DeleteBehavior.SetNull },
        new object[] { typeof(SiegeSurvivalSettings), nameof(SiegeSurvivalSettings.NpcTeamId), DeleteBehavior.SetNull },
        new object[] { typeof(SiegeSurvivalSettings), nameof(SiegeSurvivalSettings.DefaultKitId), DeleteBehavior.Restrict },
        new object[] { typeof(SiegeSurvivalSettings), nameof(SiegeSurvivalSettings.BaselineLoadoutKitId), DeleteBehavior.Restrict },
        new object[] { typeof(SurvivalSettingsKit), nameof(SurvivalSettingsKit.SiegeSurvivalSettingsId), DeleteBehavior.Cascade },
        new object[] { typeof(SurvivalSettingsKit), nameof(SurvivalSettingsKit.KitId), DeleteBehavior.Restrict },
        new object[] { typeof(SurvivalLobbySettings), nameof(SurvivalLobbySettings.SiegeLobbyId), DeleteBehavior.Cascade },
        new object[] { typeof(SurvivalDifficultyProfile), nameof(SurvivalDifficultyProfile.SiegeScenarioId), DeleteBehavior.Cascade },
        new object[] { typeof(SurvivalMonsterType), nameof(SurvivalMonsterType.SiegeScenarioId), DeleteBehavior.Cascade },
        new object[] { typeof(SurvivalSpecialWaveProfile), nameof(SurvivalSpecialWaveProfile.SiegeScenarioId), DeleteBehavior.Cascade },
        new object[] { typeof(SurvivalSpecialWaveMonsterType), nameof(SurvivalSpecialWaveMonsterType.SurvivalSpecialWaveProfileId), DeleteBehavior.Cascade },
        new object[] { typeof(SurvivalSpecialWaveEntry), nameof(SurvivalSpecialWaveEntry.SiegeScenarioId), DeleteBehavior.Cascade },
        new object[] { typeof(SurvivalSpecialWaveEntry), nameof(SurvivalSpecialWaveEntry.SurvivalSpecialWaveProfileId), DeleteBehavior.Cascade },
        new object[] { typeof(SurvivalSpawnZone), nameof(SurvivalSpawnZone.SiegeScenarioId), DeleteBehavior.Cascade },
        new object[] { typeof(SurvivalSpawnZone), nameof(SurvivalSpawnZone.AnchorLocationId), DeleteBehavior.Restrict },
        new object[] { typeof(SurvivalSpawnZonePoint), nameof(SurvivalSpawnZonePoint.SurvivalSpawnZoneId), DeleteBehavior.Cascade },
        new object[] { typeof(SurvivalSpawnZonePoint), nameof(SurvivalSpawnZonePoint.LocationId), DeleteBehavior.Restrict },
        new object[] { typeof(SurvivalSpawnZoneObjective), nameof(SurvivalSpawnZoneObjective.SurvivalSpawnZoneId), DeleteBehavior.Cascade },
        new object[] { typeof(SurvivalSpawnZoneObjective), nameof(SurvivalSpawnZoneObjective.SiegeObjectiveId), DeleteBehavior.Cascade },
        new object[] { typeof(SurvivalTargetOverride), nameof(SurvivalTargetOverride.SiegeScenarioId), DeleteBehavior.Cascade },
        new object[] { typeof(SurvivalTargetOverride), nameof(SurvivalTargetOverride.SiegeObjectiveId), DeleteBehavior.Cascade },
        new object[] { typeof(SurvivalTargetOverride), nameof(SurvivalTargetOverride.GateStructureId), DeleteBehavior.Cascade },
        new object[] { typeof(SurvivalPurchasePoint), nameof(SurvivalPurchasePoint.SiegeScenarioId), DeleteBehavior.Cascade },
        new object[] { typeof(SurvivalPurchasePoint), nameof(SurvivalPurchasePoint.LocationId), DeleteBehavior.Restrict },
        new object[] { typeof(SurvivalPurchaseOffer), nameof(SurvivalPurchaseOffer.SurvivalPurchasePointId), DeleteBehavior.Cascade },
        new object[] { typeof(SurvivalPurchaseOffer), nameof(SurvivalPurchaseOffer.ItemBlueprintId), DeleteBehavior.Restrict },
        new object[] { typeof(SurvivalMysteryBoxPool), nameof(SurvivalMysteryBoxPool.SiegeScenarioId), DeleteBehavior.Cascade },
        new object[] { typeof(SurvivalMysteryBoxEntry), nameof(SurvivalMysteryBoxEntry.SurvivalMysteryBoxPoolId), DeleteBehavior.Cascade },
        new object[] { typeof(SurvivalMysteryBoxEntry), nameof(SurvivalMysteryBoxEntry.ItemBlueprintId), DeleteBehavior.Restrict },
        new object[] { typeof(SurvivalMysteryBoxEntry), nameof(SurvivalMysteryBoxEntry.EnchantmentDefinitionId), DeleteBehavior.Restrict },
        new object[] { typeof(SurvivalMysteryBoxEntryEnchantment), nameof(SurvivalMysteryBoxEntryEnchantment.SurvivalMysteryBoxEntryId), DeleteBehavior.Cascade },
        new object[] { typeof(SurvivalMysteryBoxEntryEnchantment), nameof(SurvivalMysteryBoxEntryEnchantment.EnchantmentDefinitionId), DeleteBehavior.Restrict },
        new object[] { typeof(SurvivalMysteryBoxPoint), nameof(SurvivalMysteryBoxPoint.SiegeScenarioId), DeleteBehavior.Cascade },
        new object[] { typeof(SurvivalMysteryBoxPoint), nameof(SurvivalMysteryBoxPoint.LocationId), DeleteBehavior.Restrict },
        new object[] { typeof(SurvivalMysteryBoxPoint), nameof(SurvivalMysteryBoxPoint.SurvivalMysteryBoxPoolId), DeleteBehavior.Restrict },
    };

    [Theory]
    [MemberData(nameof(ExpectedDeleteRules))]
    public void ModelDeleteRules(Type entity, string foreignKey, DeleteBehavior expected)
    {
        using var context = SiegeTestData.NewContext("SurvivalDeleteRules");
        var entityType = context.Model.FindEntityType(entity)!;
        var fk = entityType.GetForeignKeys().Single(f => f.Properties.Count == 1 && f.Properties[0].Name == foreignKey);
        Assert.Equal(expected, fk.DeleteBehavior);
    }

    [Fact]
    public void EveryTable_HasTheSiegeSurvivalPrefix_AndEnumsAreStrings()
    {
        using var context = SiegeTestData.NewContext("SurvivalTables");
        var survivalTypes = context.Model.GetEntityTypes()
            .Where(t => t.ClrType.Name.StartsWith("Survival") || t.ClrType == typeof(SiegeSurvivalSettings)).ToList();
        Assert.Equal(18, survivalTypes.Count);
        Assert.All(survivalTypes, t => Assert.StartsWith("siege_survival_", t.GetTableName()!));
        foreach (var property in survivalTypes.SelectMany(t => t.GetProperties()).Where(p => p.ClrType.IsEnum))
            Assert.Equal(typeof(string), property.GetProviderClrType());
    }

    [Fact]
    public async Task DeletingTheScenario_RemovesEverySurvivalRow()
    {
        await using var context = SiegeTestData.NewContext("SurvivalCascade");
        await SiegeTestData.SeedValidScenarioAsync(context);
        await SurvivalTestData.SeedReadySurvivalAsync(context);

        // InMemory cascades only through tracked rows (the database does it itself), so load everything first.
        await context.SiegeSurvivalSettings.Include(s => s.SelectedKits).LoadAsync();
        await context.SurvivalDifficultyProfiles.LoadAsync();
        await context.SurvivalMonsterTypes.LoadAsync();
        await context.SurvivalSpecialWaveProfiles.Include(p => p.MonsterTypes).LoadAsync();
        await context.SurvivalSpecialWaveEntries.LoadAsync();
        await context.SurvivalSpawnZones.Include(z => z.Points).Include(z => z.PrerequisiteObjectives).LoadAsync();
        await context.SurvivalTargetOverrides.LoadAsync();
        await context.SurvivalPurchasePoints.Include(p => p.Offers).LoadAsync();
        await context.SurvivalMysteryBoxPools.Include(p => p.Entries).ThenInclude(e => e.Enchantments).LoadAsync();
        await context.SurvivalMysteryBoxPoints.LoadAsync();
        var scenario = await context.SiegeScenarios
            .Include(s => s.Teams).ThenInclude(t => t.Spawnpoints)
            .Include(s => s.Objectives).Include(s => s.Gates).Include(s => s.Districts)
            .FirstAsync(s => s.Id == 100);
        context.SiegeScenarios.Remove(scenario);
        await context.SaveChangesAsync();

        Assert.Empty(context.SiegeSurvivalSettings);
        Assert.Empty(context.SurvivalDifficultyProfiles);
        Assert.Empty(context.SurvivalMonsterTypes);
        Assert.Empty(context.SurvivalSpecialWaveProfiles);
        Assert.Empty(context.SurvivalSpecialWaveMonsterTypes);
        Assert.Empty(context.SurvivalSpecialWaveEntries);
        Assert.Empty(context.SurvivalSpawnZones);
        Assert.Empty(context.SurvivalSpawnZonePoints);
        Assert.Empty(context.SurvivalSpawnZoneObjectives);
        Assert.Empty(context.SurvivalTargetOverrides);
        Assert.Empty(context.SurvivalPurchasePoints);
        Assert.Empty(context.SurvivalPurchaseOffers);
        Assert.Empty(context.SurvivalMysteryBoxPools);
        Assert.Empty(context.SurvivalMysteryBoxEntries);
        Assert.Empty(context.SurvivalMysteryBoxEntryEnchantments);
        Assert.Empty(context.SurvivalMysteryBoxPoints);
        // Shared rows stay.
        Assert.Equal(3, await context.Kits.CountAsync());
        Assert.Equal(5, await context.Locations.CountAsync());
    }

    [Fact]
    public async Task DeletingATeam_ClearsTheSurvivalTeamReference()
    {
        await using var context = SiegeTestData.NewContext("SurvivalTeamSetNull");
        await SiegeTestData.SeedValidScenarioAsync(context);
        await SurvivalTestData.SeedCatalogAsync(context);
        context.SiegeSurvivalSettings.Add(new SiegeSurvivalSettings { SiegeScenarioId = 100, HumanTeamId = 201, NpcTeamId = 202 });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await context.SiegeSurvivalSettings.LoadAsync();
        var team = await context.SiegeTeams.Include(t => t.Spawnpoints).FirstAsync(t => t.Id == 202);
        context.SiegeTeams.Remove(team);
        await context.SaveChangesAsync();

        var settings = await context.SiegeSurvivalSettings.AsNoTracking().FirstAsync();
        Assert.Equal(201, settings.HumanTeamId);
        Assert.Null(settings.NpcTeamId);
    }
}
