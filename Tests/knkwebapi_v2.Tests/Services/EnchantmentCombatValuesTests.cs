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
}
