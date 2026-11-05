using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.EnchantmentInfo;
using KnKWebAPI.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// PandiO/knk-workspace#221: the public info sheets show the live combat values per level, worked out like the plugin,
/// with the defaults for empty values.
/// </summary>
public class EnchantmentInfoServiceTests
{
    private static EnchantmentDefinition Custom(string key, string name, int maxLevel) => new()
    {
        Key = key, DisplayName = name, Description = "x", IsCustom = true, MaxLevel = maxLevel
    };

    [Fact]
    public void EveryBuiltInCustomEnchantmentHasItsFacts()
    {
        foreach (var entry in AbilityDefinition.CanonicalCatalog)
        {
            Assert.True(EnchantmentInfoCatalog.Find(entry.EnchantmentKey) != null, entry.EnchantmentKey);
        }
        Assert.NotNull(EnchantmentInfoCatalog.Find("knk:flash_chaos"));
        Assert.Null(EnchantmentInfoCatalog.Find("minecraft:sharpness"));
    }

    [Fact]
    public void Poison_EmptyColumns_ShowTheDefaultsPerLevel()
    {
        var sheet = EnchantmentInfoService.Build(Custom("poison", "Poison", 3));

        Assert.Equal(EnchantmentInfoCatalog.OnHit, sheet.Trigger);
        Assert.Equal(EnchantmentInfoCatalog.Target, sheet.Application);
        Assert.Equal(new double?[] { 10, 15, 20 }, sheet.Levels.Select(l => l.ChancePercent));
        Assert.All(sheet.Levels, l => Assert.Equal(8, l.CooldownSeconds));
        Assert.Equal(new double?[] { 3, 4.5, 6 }, sheet.Levels.Select(l => l.Effects.Single().DurationSeconds));
        Assert.All(sheet.Levels, l => Assert.Equal(("Poison", (int?)2, "Target"),
            (l.Effects[0].Name, l.Effects[0].PotionLevel, l.Effects[0].AppliesTo)));
        Assert.Null(sheet.ArmourReducesDamage);
        Assert.Null(sheet.Levels[0].AreaDamage);
    }

    [Fact]
    public void FlashChaos_ShowsItsBlastAndDebuffs_AlwaysGoesOff()
    {
        var sheet = EnchantmentInfoService.Build(Custom("knk:flash_chaos", "Flash Chaos", 1));

        Assert.Equal(EnchantmentInfoCatalog.RightClick, sheet.Trigger);
        Assert.Equal(EnchantmentInfoCatalog.Area, sheet.Application);
        Assert.Equal(3, sheet.DamageRadiusBlocks);
        Assert.Equal(5, sheet.KnockbackRadiusBlocks);
        Assert.True(sheet.ArmourReducesDamage);
        var level = Assert.Single(sheet.Levels);
        Assert.Null(level.ChancePercent);
        Assert.Equal(90, level.CooldownSeconds);
        Assert.Equal(60, level.AreaDamage);
        Assert.Equal(60, level.GateDamage);
        Assert.Equal(new[] { ("Slowness", (int?)1, (double?)5), ("Nausea", (int?)null, (double?)12.5) },
            level.Effects.Select(e => (e.Name, e.PotionLevel, e.DurationSeconds)));
        Assert.All(level.Effects, e => Assert.Equal(EnchantmentInfoCatalog.PlayersInRange, e.AppliesTo));
    }

    [Fact]
    public void AnAdminsValuesAreShown_TheIssuesLiveCheck()
    {
        var chaos = Custom("chaos", "Chaos", 1);
        chaos.AreaDamage = 6;
        chaos.DamageRadiusBlocks = 2;
        chaos.CooldownSeconds = 45;
        var flash = Custom("flash_chaos", "Flash Chaos", 1);
        flash.CooldownSeconds = 30;
        flash.EffectLevel = 3;

        var chaosSheet = EnchantmentInfoService.Build(chaos);
        Assert.Equal(6, chaosSheet.Levels[0].AreaDamage);
        Assert.Equal(40, chaosSheet.Levels[0].GateDamage);
        Assert.Equal(2, chaosSheet.DamageRadiusBlocks);
        Assert.Equal(5, chaosSheet.KnockbackRadiusBlocks);
        Assert.Equal(45, chaosSheet.Levels[0].CooldownSeconds);

        var flashSheet = EnchantmentInfoService.Build(flash);
        Assert.Equal(30, flashSheet.Levels[0].CooldownSeconds);
        Assert.Equal(3, flashSheet.Levels[0].Effects[0].PotionLevel);
    }

    [Fact]
    public void DamageAndDurationsGrowWithTheLevelMultiplier_AsInThePlugin()
    {
        var chaos = Custom("chaos", "Chaos", 3);
        chaos.LevelMultiplier = 0.5;
        chaos.AreaDamage = 10;
        chaos.GateDamage = 100;
        var sheet = EnchantmentInfoService.Build(chaos);
        Assert.Equal(new double?[] { 10, 15, 20 }, sheet.Levels.Select(l => l.AreaDamage));
        Assert.Equal(new double?[] { 100, 150, 200 }, sheet.Levels.Select(l => l.GateDamage));

        // Whole ticks, like EnchantmentTuning.durationTicksAt: 1.5 s = 30 ticks, x1.5 = 45 ticks = 2.25 s.
        var freeze = EnchantmentInfoService.Build(Custom("freeze", "Freeze", 3));
        Assert.Equal(new double?[] { 1.5, 2.25, 3 }, freeze.Levels.Select(l => l.Effects[0].DurationSeconds));
        Assert.Null(freeze.Levels[0].Effects[0].PotionLevel);
    }

    [Fact]
    public void ChanceIsKeptBetween0And100_ASwitchedOffCooldownIs0_AZeroDurationLeavesTheEffectOut()
    {
        var blindness = Custom("blindness", "Blindness", 3);
        blindness.ProcChancePercent = 90;
        blindness.ProcChancePerLevelPercent = 20;
        blindness.CooldownEnabled = false;
        blindness.EffectDurationSeconds = 0;

        var sheet = EnchantmentInfoService.Build(blindness);

        Assert.Equal(new double?[] { 90, 100, 100 }, sheet.Levels.Select(l => l.ChancePercent));
        Assert.All(sheet.Levels, l => Assert.Equal(0, l.CooldownSeconds));
        Assert.All(sheet.Levels, l => Assert.Empty(l.Effects));
    }

    [Fact]
    public void SelfBuffsHealAndCleaving()
    {
        var strength = EnchantmentInfoService.Build(Custom("strength", "Strength", 2));
        Assert.Equal(new int?[] { 1, 2 }, strength.Levels.Select(l => l.Effects[0].PotionLevel));
        Assert.Equal(EnchantmentInfoCatalog.Self, strength.Application);
        Assert.Equal(new double?[] { 15, 30 }, strength.Levels.Select(l => l.ChancePercent));

        var heal = EnchantmentInfoService.Build(Custom("health_boost", "Health Boost", 1));
        Assert.Equal(6, heal.Levels[0].HealAmount);
        Assert.Null(heal.Levels[0].ChancePercent);

        var cleaving = EnchantmentInfoService.Build(Custom("cleaving", "Cleaving", 4));
        Assert.Equal(EnchantmentInfoCatalog.GateHit, cleaving.Trigger);
        Assert.Contains("+50 %", cleaving.Levels[1].Detail);
        Assert.Contains("6x", cleaving.Levels[1].Detail);
        Assert.Contains("30 s", cleaving.Levels[3].Detail);
        Assert.Equal(new[] { 60, 50, 40, 30 }, cleaving.Levels.Select(l => l.CooldownSeconds));
    }

    /// <summary>PandiO/knk-workspace#259: Cleaving's rows follow its live combat values.</summary>
    [Fact]
    public void Cleaving_AnAdminsValuesAreShown()
    {
        var cleaving = Custom("cleaving", "Cleaving", 4);
        cleaving.GateDamageBonusPerLevel = 0.5;
        cleaving.StrikeMultiplier = 3;
        cleaving.StrikeMultiplierPerLevel = 2;
        cleaving.CooldownSeconds = 90;
        cleaving.CooldownPerLevelSeconds = -40;

        var sheet = EnchantmentInfoService.Build(cleaving);

        Assert.Equal("Melee hits on gate doors deal +50 % gate damage; the charged strike deals 3x that hit, then needs 90 s to recharge.",
            sheet.Levels[0].Detail);
        Assert.Equal("Melee hits on gate doors deal +200 % gate damage; the charged strike deals 9x that hit, with no cooldown.",
            sheet.Levels[3].Detail);
        Assert.DoesNotContain(sheet.Notes, n => n.Contains("default settings"));
    }

    [Fact]
    public void ACustomDefinitionWithoutPluginEffect_IsListedAsUnknown()
    {
        var sheet = EnchantmentInfoService.Build(Custom("knk:thunder", "Thunder", 2));
        Assert.Equal(EnchantmentInfoCatalog.Unknown, sheet.Trigger);
        Assert.Equal("x", sheet.Summary);
        Assert.Equal(2, sheet.Levels.Count);
        Assert.All(sheet.Levels, l => Assert.Null(l.ChancePercent));
    }

    [Fact]
    public async Task OnlyCustomDefinitions_ByName_ThroughTheAnonymousEndpoint()
    {
        var repo = new Mock<IEnchantmentDefinitionRepository>();
        repo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[]
        {
            Custom("wither", "Wither", 3),
            new EnchantmentDefinition { Key = "minecraft:sharpness", DisplayName = "Sharpness", IsCustom = false, MaxLevel = 5 },
            Custom("chaos", "Chaos", 1)
        });
        var controller = new EnchantmentDefinitionsController(
            new Mock<knkwebapi_v2.Services.Interfaces.IEnchantmentDefinitionService>().Object,
            new EnchantmentInfoService(repo.Object));

        var ok = Assert.IsType<OkObjectResult>(await controller.PublicInfo());
        var sheets = Assert.IsType<List<EnchantmentInfoDto>>(ok.Value);
        Assert.Equal(new[] { "Chaos", "Wither" }, sheets.Select(s => s.DisplayName));
    }
}
