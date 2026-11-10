using System.Text.Json;
using AutoMapper;
using Moq;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// PandiO/knk-workspace#151: admin-configurable combat values on an enchantment definition (proc chance, cooldown,
/// duration, level multiplier), read by the plugin's effect handlers.
/// </summary>
public class EnchantmentCombatValuesTests
{
    private readonly Mock<IEnchantmentDefinitionRepository> _repo = new();
    private readonly Mock<IMinecraftEnchantmentRefRepository> _refRepo = new();
    private readonly IMapper _mapper;
    private readonly EnchantmentDefinitionService _service;

    public EnchantmentCombatValuesTests()
    {
        _mapper = new MapperConfiguration(cfg => cfg.AddProfile<EnchantmentDefinitionMappingProfile>()).CreateMapper();
        _service = new EnchantmentDefinitionService(_repo.Object, _refRepo.Object, _mapper);
    }

    private static EnchantmentDefinition StoredFreeze() => new()
    {
        Id = 3,
        Key = "freeze",
        DisplayName = "Freeze",
        Description = "Hit player targets for a chance to immobilize them briefly.",
        IsCustom = true,
        MaxLevel = 3,
        ProcChancePercent = 10,
        ProcChancePerLevelPercent = 5,
        CooldownEnabled = true,
        CooldownSeconds = 20,
        EffectDurationSeconds = 1.5,
        LevelMultiplier = 0.5
    };

    private static EnchantmentDefinitionUpdateDto Parse(string json) =>
        JsonSerializer.Deserialize<EnchantmentDefinitionUpdateDto>(json)!;

    [Fact]
    public async Task Update_WithoutCombatValuesInTheRequest_KeepsTheStoredOnes()
    {
        var stored = StoredFreeze();
        _repo.Setup(r => r.GetByIdAsync(3)).ReturnsAsync(stored);

        // What a form configuration without the "Combat values" step (seed v1) sends.
        var dto = Parse("""{"id":3,"key":"freeze","displayName":"Freeze","description":"x","isCustom":true,"maxLevel":3}""");
        await _service.UpdateAsync(3, dto);

        Assert.Equal(10, stored.ProcChancePercent);
        Assert.Equal(5, stored.ProcChancePerLevelPercent);
        Assert.True(stored.CooldownEnabled);
        Assert.Equal(20, stored.CooldownSeconds);
        Assert.Equal(1.5, stored.EffectDurationSeconds);
        Assert.Equal(0.5, stored.LevelMultiplier);
        _repo.Verify(r => r.UpdateAsync(stored), Times.Once);
    }

    [Fact]
    public async Task Update_WithCombatValues_WritesThem_AndExplicitNullResetsToTheBuiltInValue()
    {
        var stored = StoredFreeze();
        _repo.Setup(r => r.GetByIdAsync(3)).ReturnsAsync(stored);

        var dto = Parse("""
            {"id":3,"key":"freeze","displayName":"Freeze","description":"x","isCustom":true,"maxLevel":3,
             "procChancePercent":7.5,"procChancePerLevelPercent":null,"cooldownEnabled":false,"cooldownSeconds":45,
             "effectDurationSeconds":2,"levelMultiplier":0}
            """);
        await _service.UpdateAsync(3, dto);

        Assert.Equal(7.5, stored.ProcChancePercent);
        Assert.Null(stored.ProcChancePerLevelPercent);
        Assert.False(stored.CooldownEnabled);
        Assert.Equal(45, stored.CooldownSeconds);
        Assert.Equal(2, stored.EffectDurationSeconds);
        Assert.Equal(0, stored.LevelMultiplier);
    }

    [Fact]
    public async Task Create_StoresCombatValues_AndTheReadDtoCarriesThem()
    {
        EnchantmentDefinition? added = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<EnchantmentDefinition>()))
            .Callback<EnchantmentDefinition>(e => added = e)
            .Returns(Task.CompletedTask);

        var read = await _service.CreateAsync(new EnchantmentDefinitionCreateDto
        {
            Key = "poison",
            DisplayName = "Poison",
            IsCustom = true,
            MaxLevel = 3,
            ProcChancePercent = 12,
            ProcChancePerLevelPercent = 4,
            CooldownEnabled = true,
            CooldownSeconds = 8,
            EffectDurationSeconds = 3,
            LevelMultiplier = 0.5
        });

        Assert.NotNull(added);
        Assert.Equal(12, added!.ProcChancePercent);
        Assert.Equal(8, added.CooldownSeconds);
        Assert.Equal(12, read.ProcChancePercent);
        Assert.Equal(4, read.ProcChancePerLevelPercent);
        Assert.True(read.CooldownEnabled);
        Assert.Equal(8, read.CooldownSeconds);
        Assert.Equal(3, read.EffectDurationSeconds);
        Assert.Equal(0.5, read.LevelMultiplier);

        var json = JsonSerializer.Serialize(read);
        Assert.Contains("\"procChancePercent\":12", json);
        Assert.Contains("\"cooldownEnabled\":true", json);
        Assert.Contains("\"effectDurationSeconds\":3", json);
    }

    [Fact]
    public async Task Create_WithoutCombatValues_LeavesThemNull()
    {
        EnchantmentDefinition? added = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<EnchantmentDefinition>()))
            .Callback<EnchantmentDefinition>(e => added = e)
            .Returns(Task.CompletedTask);

        await _service.CreateAsync(new EnchantmentDefinitionCreateDto { Key = "wither", DisplayName = "Wither", IsCustom = true });

        Assert.Null(added!.ProcChancePercent);
        Assert.Null(added.CooldownEnabled);
        Assert.Null(added.CooldownSeconds);
        Assert.Null(added.EffectDurationSeconds);
        Assert.Null(added.LevelMultiplier);
    }

    [Theory]
    [InlineData("procChancePercent", "-1")]
    [InlineData("procChancePercent", "100.5")]
    [InlineData("procChancePerLevelPercent", "-101")]
    [InlineData("procChancePerLevelPercent", "101")]
    [InlineData("cooldownSeconds", "-1")]
    [InlineData("cooldownSeconds", "86401")]
    [InlineData("effectDurationSeconds", "-0.5")]
    [InlineData("effectDurationSeconds", "3601")]
    [InlineData("levelMultiplier", "-0.1")]
    [InlineData("levelMultiplier", "11")]
    [InlineData("effectLevel", "0")]
    [InlineData("effectLevel", "11")]
    [InlineData("secondaryEffectDurationSeconds", "-1")]
    [InlineData("secondaryEffectDurationSeconds", "3601")]
    [InlineData("areaDamage", "-1")]
    [InlineData("areaDamage", "1001")]
    [InlineData("gateDamage", "-1")]
    [InlineData("gateDamage", "10001")]
    [InlineData("damageRadiusBlocks", "-0.5")]
    [InlineData("damageRadiusBlocks", "33")]
    [InlineData("effectRadiusBlocks", "-0.5")]
    [InlineData("effectRadiusBlocks", "33")]
    [InlineData("gateDamageBonusPerLevel", "-0.1")]
    [InlineData("gateDamageBonusPerLevel", "11")]
    [InlineData("strikeMultiplier", "-1")]
    [InlineData("strikeMultiplier", "101")]
    [InlineData("strikeMultiplierPerLevel", "-101")]
    [InlineData("strikeMultiplierPerLevel", "101")]
    [InlineData("cooldownPerLevelSeconds", "-86401")]
    [InlineData("cooldownPerLevelSeconds", "86401")]
    public async Task Update_RefusesOutOfRangeValues(string property, string value)
    {
        var stored = StoredFreeze();
        _repo.Setup(r => r.GetByIdAsync(3)).ReturnsAsync(stored);

        var dto = Parse($$"""{"id":3,"key":"freeze","displayName":"Freeze","isCustom":true,"maxLevel":3,"{{property}}":{{value}}}""");

        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(3, dto));
        _repo.Verify(r => r.UpdateAsync(It.IsAny<EnchantmentDefinition>()), Times.Never);
        Assert.Equal(10, stored.ProcChancePercent);
    }

    [Theory]
    [InlineData(0, -100, 0, 0, 0)]
    [InlineData(100, 100, 86400, 3600, 10)]
    public void Validate_AcceptsTheBounds(double chance, double perLevel, int cooldown, double duration, double multiplier)
    {
        EnchantmentDefinitionService.ValidateCombatValues(chance, perLevel, cooldown, duration, multiplier);
        EnchantmentDefinitionService.ValidateCombatValues(null, null, null, null, null);
    }

    [Fact]
    public async Task Create_RefusesAnOutOfRangeChance()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(new EnchantmentDefinitionCreateDto
        {
            Key = "freeze", DisplayName = "Freeze", IsCustom = true, ProcChancePercent = 150
        }));
        _repo.Verify(r => r.AddAsync(It.IsAny<EnchantmentDefinition>()), Times.Never);
    }

    // PandiO/knk-workspace#201: Flash Chaos's Slowness level and Nausea duration.

    private static EnchantmentDefinition StoredFlashChaos() => new()
    {
        Id = 9,
        Key = "flash_chaos",
        DisplayName = "Flash Chaos",
        Description = "x",
        IsCustom = true,
        MaxLevel = 1,
        CooldownEnabled = true,
        CooldownSeconds = 90,
        EffectDurationSeconds = 5,
        LevelMultiplier = 0,
        EffectLevel = 1,
        SecondaryEffectDurationSeconds = 12.5
    };

    [Fact]
    public async Task Update_WritesTheFlashChaosValues_OnlyWhenSent()
    {
        var stored = StoredFlashChaos();
        _repo.Setup(r => r.GetByIdAsync(9)).ReturnsAsync(stored);

        await _service.UpdateAsync(9, Parse("""
            {"id":9,"key":"flash_chaos","displayName":"Flash Chaos","isCustom":true,"maxLevel":1,"effectDurationSeconds":8}
            """));
        Assert.Equal(8, stored.EffectDurationSeconds);
        Assert.Equal(1, stored.EffectLevel);
        Assert.Equal(12.5, stored.SecondaryEffectDurationSeconds);

        await _service.UpdateAsync(9, Parse("""
            {"id":9,"key":"flash_chaos","displayName":"Flash Chaos","isCustom":true,"maxLevel":1,
             "effectLevel":3,"secondaryEffectDurationSeconds":null}
            """));
        Assert.Equal(3, stored.EffectLevel);
        Assert.Null(stored.SecondaryEffectDurationSeconds);
    }

    [Fact]
    public async Task Create_StoresTheFlashChaosValues_AndTheReadDtoCarriesThem()
    {
        EnchantmentDefinition? added = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<EnchantmentDefinition>()))
            .Callback<EnchantmentDefinition>(e => added = e)
            .Returns(Task.CompletedTask);

        var read = await _service.CreateAsync(new EnchantmentDefinitionCreateDto
        {
            Key = "flash_chaos", DisplayName = "Flash Chaos", IsCustom = true, MaxLevel = 1,
            EffectLevel = 2, SecondaryEffectDurationSeconds = 20
        });

        Assert.Equal(2, added!.EffectLevel);
        Assert.Equal(20, added.SecondaryEffectDurationSeconds);
        Assert.Equal(2, read.EffectLevel);
        Assert.Equal(20, read.SecondaryEffectDurationSeconds);
    }

    [Fact]
    public void ValidateSecondary_AcceptsTheBoundsAndNulls()
    {
        EnchantmentDefinitionService.ValidateSecondaryCombatValues(1, 0);
        EnchantmentDefinitionService.ValidateSecondaryCombatValues(10, 3600);
        EnchantmentDefinitionService.ValidateSecondaryCombatValues(null, null);
        Assert.Throws<ArgumentException>(() => EnchantmentDefinitionService.ValidateSecondaryCombatValues(null, double.NaN));
    }

    // PandiO/knk-workspace#223: Chaos / Flash Chaos area damage, gate damage and radii.

    private static EnchantmentDefinition StoredChaos() => new()
    {
        Id = 8,
        Key = "chaos",
        DisplayName = "Chaos",
        Description = "x",
        IsCustom = true,
        MaxLevel = 1,
        CooldownEnabled = true,
        CooldownSeconds = 90,
        AreaDamage = 40,
        GateDamage = 40,
        DamageRadiusBlocks = 3,
        EffectRadiusBlocks = 5
    };

    [Fact]
    public async Task Update_WritesTheAreaValues_OnlyWhenSent()
    {
        var stored = StoredChaos();
        _repo.Setup(r => r.GetByIdAsync(8)).ReturnsAsync(stored);

        await _service.UpdateAsync(8, Parse("""
            {"id":8,"key":"chaos","displayName":"Chaos","isCustom":true,"maxLevel":1,"areaDamage":6,"damageRadiusBlocks":2}
            """));
        Assert.Equal(6, stored.AreaDamage);
        Assert.Equal(2, stored.DamageRadiusBlocks);
        Assert.Equal(40, stored.GateDamage);
        Assert.Equal(5, stored.EffectRadiusBlocks);

        await _service.UpdateAsync(8, Parse("""
            {"id":8,"key":"chaos","displayName":"Chaos","isCustom":true,"maxLevel":1,
             "gateDamage":100,"effectRadiusBlocks":null,"areaDamage":null}
            """));
        Assert.Equal(100, stored.GateDamage);
        Assert.Null(stored.EffectRadiusBlocks);
        Assert.Null(stored.AreaDamage);
        Assert.Equal(2, stored.DamageRadiusBlocks);
    }

    [Fact]
    public async Task Create_StoresTheAreaValues_AndTheReadDtoCarriesThem()
    {
        EnchantmentDefinition? added = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<EnchantmentDefinition>()))
            .Callback<EnchantmentDefinition>(e => added = e)
            .Returns(Task.CompletedTask);

        var read = await _service.CreateAsync(new EnchantmentDefinitionCreateDto
        {
            Key = "chaos", DisplayName = "Chaos", IsCustom = true, MaxLevel = 1,
            AreaDamage = 6, GateDamage = 30, DamageRadiusBlocks = 2, EffectRadiusBlocks = 4.5
        });

        Assert.Equal(6, added!.AreaDamage);
        Assert.Equal(30, added.GateDamage);
        Assert.Equal(2, added.DamageRadiusBlocks);
        Assert.Equal(4.5, added.EffectRadiusBlocks);
        Assert.Equal(6, read.AreaDamage);
        Assert.Equal(30, read.GateDamage);
        Assert.Equal(2, read.DamageRadiusBlocks);
        Assert.Equal(4.5, read.EffectRadiusBlocks);
    }

    // PandiO/knk-workspace#259: Cleaving's gate bonus and charged strike.

    [Fact]
    public async Task Update_WritesTheCleavingValues_OnlyWhenSent()
    {
        var stored = new EnchantmentDefinition
        {
            Id = 10, Key = "cleaving", DisplayName = "Cleaving", Description = "x", IsCustom = true, MaxLevel = 4,
            CooldownEnabled = true, CooldownSeconds = 60, GateDamageBonusPerLevel = 0.25, StrikeMultiplier = 5,
            StrikeMultiplierPerLevel = 1, CooldownPerLevelSeconds = -10
        };
        _repo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(stored);

        await _service.UpdateAsync(10, Parse("""
            {"id":10,"key":"cleaving","displayName":"Cleaving","isCustom":true,"maxLevel":4,"strikeMultiplier":3,"cooldownPerLevelSeconds":-5}
            """));
        Assert.Equal(3, stored.StrikeMultiplier);
        Assert.Equal(-5, stored.CooldownPerLevelSeconds);
        Assert.Equal(0.25, stored.GateDamageBonusPerLevel);
        Assert.Equal(1, stored.StrikeMultiplierPerLevel);

        await _service.UpdateAsync(10, Parse("""
            {"id":10,"key":"cleaving","displayName":"Cleaving","isCustom":true,"maxLevel":4,"gateDamageBonusPerLevel":null}
            """));
        Assert.Null(stored.GateDamageBonusPerLevel);
        Assert.Equal(3, stored.StrikeMultiplier);
    }

    [Fact]
    public void ValidateGateStrike_AcceptsTheBoundsAndNulls()
    {
        EnchantmentDefinitionService.ValidateGateStrikeCombatValues(0, 0, -100, -86400);
        EnchantmentDefinitionService.ValidateGateStrikeCombatValues(10, 100, 100, 86400);
        EnchantmentDefinitionService.ValidateGateStrikeCombatValues(null, null, null, null);
        Assert.Throws<ArgumentException>(() => EnchantmentDefinitionService.ValidateGateStrikeCombatValues(double.NaN, null, null, null));
    }

    [Fact]
    public void ValidateArea_AcceptsTheBoundsAndNulls()
    {
        EnchantmentDefinitionService.ValidateAreaCombatValues(0, 0, 0, 0);
        EnchantmentDefinitionService.ValidateAreaCombatValues(1000, 10000, 32, 32);
        EnchantmentDefinitionService.ValidateAreaCombatValues(null, null, null, null);
        Assert.Throws<ArgumentException>(() => EnchantmentDefinitionService.ValidateAreaCombatValues(double.NaN, null, null, null));
        Assert.Throws<ArgumentException>(() => EnchantmentDefinitionService.ValidateAreaCombatValues(null, null, null, double.NaN));
    }
}
