using AutoMapper;
using Moq;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

public class EnchantmentDefinitionServiceTests
{
    private readonly Mock<IEnchantmentDefinitionRepository> _enchantmentRepo;
    private readonly Mock<IMinecraftEnchantmentRefRepository> _enchantmentRefRepo;
    private readonly Mock<IMapper> _mapper;
    private readonly EnchantmentDefinitionService _service;

    public EnchantmentDefinitionServiceTests()
    {
        _enchantmentRepo = new Mock<IEnchantmentDefinitionRepository>();
        _enchantmentRefRepo = new Mock<IMinecraftEnchantmentRefRepository>();
        _mapper = new Mock<IMapper>();

        _service = new EnchantmentDefinitionService(
            _enchantmentRepo.Object,
            _enchantmentRefRepo.Object,
            _mapper.Object);
    }

    [Fact]
    public async Task CreateAsync_WithAbilityDefinitionAndNonCustom_ThrowsArgumentException()
    {
        var dto = new EnchantmentDefinitionCreateDto
        {
            Key = "minecraft:sharpness",
            DisplayName = "Sharpness",
            IsCustom = false,
            AbilityDefinition = new AbilityDefinitionUpsertDto
            {
                AbilityKey = "combat.sharpness"
            }
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(dto));

        Assert.Contains("IsCustom", ex.Message);
        _enchantmentRepo.Verify(r => r.AddAsync(It.IsAny<EnchantmentDefinition>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_WithAbilityDefinitionAndNonCustom_ThrowsArgumentException()
    {
        var dto = new EnchantmentDefinitionUpdateDto
        {
            Id = 7,
            Key = "minecraft:sharpness",
            DisplayName = "Sharpness",
            IsCustom = false,
            AbilityDefinition = new AbilityDefinitionUpsertDto
            {
                AbilityKey = "combat.sharpness"
            }
        };

        _enchantmentRepo
            .Setup(r => r.GetByIdAsync(7))
            .ReturnsAsync(new EnchantmentDefinition { Id = 7, Key = "minecraft:sharpness", DisplayName = "Sharpness" });

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(7, dto));

        Assert.Contains("IsCustom", ex.Message);
        _enchantmentRepo.Verify(r => r.UpdateAsync(It.IsAny<EnchantmentDefinition>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithCustomAndAbilityDefinition_PersistsAbilityDefinition()
    {
        var dto = new EnchantmentDefinitionCreateDto
        {
            Key = "knk:lifesteal",
            DisplayName = "Lifesteal",
            IsCustom = true,
            AbilityDefinition = new AbilityDefinitionUpsertDto
            {
                AbilityKey = "combat.lifesteal",
                RuntimeConfigJson = "{\"chance\":0.15}",
                FutureUserAssignmentContract = "UserSkillAssignment:v1"
            }
        };

        var entity = new EnchantmentDefinition
        {
            Id = 0,
            Key = dto.Key,
            DisplayName = dto.DisplayName,
            IsCustom = dto.IsCustom
        };

        var mappedAbility = new AbilityDefinition
        {
            AbilityKey = dto.AbilityDefinition!.AbilityKey,
            RuntimeConfigJson = dto.AbilityDefinition.RuntimeConfigJson,
            FutureUserAssignmentContract = dto.AbilityDefinition.FutureUserAssignmentContract
        };

        _mapper.Setup(m => m.Map<EnchantmentDefinition>(dto)).Returns(entity);
        _mapper.Setup(m => m.Map<AbilityDefinition>(dto.AbilityDefinition)).Returns(mappedAbility);
        _mapper.Setup(m => m.Map<EnchantmentDefinitionReadDto>(It.IsAny<EnchantmentDefinition>()))
            .Returns((EnchantmentDefinition src) => new EnchantmentDefinitionReadDto
            {
                Id = src.Id,
                Key = src.Key,
                DisplayName = src.DisplayName,
                IsCustom = src.IsCustom,
                AbilityDefinition = src.AbilityDefinition == null
                    ? null
                    : new AbilityDefinitionReadDto
                    {
                        AbilityKey = src.AbilityDefinition.AbilityKey,
                        RuntimeConfigJson = src.AbilityDefinition.RuntimeConfigJson,
                        FutureUserAssignmentContract = src.AbilityDefinition.FutureUserAssignmentContract
                    }
            });

        _enchantmentRepo
            .Setup(r => r.AddAsync(It.IsAny<EnchantmentDefinition>()))
            .Callback<EnchantmentDefinition>(added => added.Id = 21)
            .Returns(Task.CompletedTask);

        var result = await _service.CreateAsync(dto);

        Assert.Equal(21, result.Id);
        Assert.NotNull(result.AbilityDefinition);
        Assert.Equal("combat.lifesteal", result.AbilityDefinition!.AbilityKey);
        _enchantmentRepo.Verify(r => r.AddAsync(It.Is<EnchantmentDefinition>(e => e.AbilityDefinition != null)), Times.Once);
    }

    // KNG-195: the web form never sends abilityDefinition; saving a custom enchantment must keep it.
    private EnchantmentDefinition CustomWithAbility() => new()
    {
        Id = 3,
        Key = "knk:frost",
        DisplayName = "Frost",
        IsCustom = true,
        AbilityDefinition = new AbilityDefinition { AbilityKey = "combat.frost", RuntimeConfigJson = "{}" }
    };

    [Fact]
    public async Task UpdateAsync_CustomWithoutAbilityDefinitionInRequest_KeepsTheAbility()
    {
        var existing = CustomWithAbility();
        _enchantmentRepo.Setup(r => r.GetByIdAsync(3)).ReturnsAsync(existing);
        var dto = new EnchantmentDefinitionUpdateDto { Id = 3, Key = "knk:frost", DisplayName = "Frost bite", IsCustom = true };

        await _service.UpdateAsync(3, dto);

        Assert.NotNull(existing.AbilityDefinition);
        Assert.Equal("combat.frost", existing.AbilityDefinition!.AbilityKey);
        Assert.Equal("Frost bite", existing.DisplayName);
    }

    [Fact]
    public async Task UpdateAsync_CustomWithExplicitNullAbilityDefinition_RemovesTheAbility()
    {
        var existing = CustomWithAbility();
        _enchantmentRepo.Setup(r => r.GetByIdAsync(3)).ReturnsAsync(existing);
        var dto = new EnchantmentDefinitionUpdateDto { Id = 3, Key = "knk:frost", DisplayName = "Frost", IsCustom = true, AbilityDefinition = null };

        await _service.UpdateAsync(3, dto);

        Assert.Null(existing.AbilityDefinition);
    }

    [Fact]
    public async Task UpdateAsync_MadeVanilla_StillRemovesTheAbility()
    {
        var existing = CustomWithAbility();
        _enchantmentRepo.Setup(r => r.GetByIdAsync(3)).ReturnsAsync(existing);
        var dto = new EnchantmentDefinitionUpdateDto { Id = 3, Key = "knk:frost", DisplayName = "Frost", IsCustom = false };

        await _service.UpdateAsync(3, dto);

        Assert.Null(existing.AbilityDefinition);
    }

    // KNG-195: a catalog-only enchantment picked on the form arrives with id 0 and its key under the
    // field-named alias; it is now created and linked instead of dropped.
    [Fact]
    public async Task UpdateAsync_UnsavedCatalogEnchantmentByFieldNamedKey_IsCreatedAndLinked()
    {
        var existing = new EnchantmentDefinition { Id = 4, Key = "minecraft:sharpness", DisplayName = "Sharpness" };
        _enchantmentRepo.Setup(r => r.GetByIdAsync(4)).ReturnsAsync(existing);
        _enchantmentRefRepo
            .Setup(r => r.AddAsync(It.IsAny<MinecraftEnchantmentRef>()))
            .Callback<MinecraftEnchantmentRef>(added => added.Id = 55)
            .Returns(Task.CompletedTask);
        var dto = new EnchantmentDefinitionUpdateDto
        {
            Id = 4,
            Key = "minecraft:sharpness",
            DisplayName = "Sharpness",
            MinecraftEnchantmentRefId = 0,
            MinecraftEnchantmentRefNamespaceKey = "minecraft:sharpness"
        };

        await _service.UpdateAsync(4, dto);

        Assert.Equal(55, existing.MinecraftEnchantmentRefId);
        _enchantmentRefRepo.Verify(r => r.AddAsync(It.Is<MinecraftEnchantmentRef>(e => e.NamespaceKey == "minecraft:sharpness")), Times.Once);
    }
}