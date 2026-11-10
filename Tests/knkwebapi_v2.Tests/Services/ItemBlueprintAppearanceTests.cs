using AutoMapper;
using FluentAssertions;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// PandiO/knk-workspace#232: a shield blueprint's banner (base colour + layers, in order) and an armour blueprint's
/// trim survive create -> read -> update -> read through ItemBlueprintService, the DTOs and the repository.
/// </summary>
public class ItemBlueprintAppearanceTests
{
    private readonly string _dbName = $"ItemBlueprintAppearanceTestDb_{Guid.NewGuid()}";

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_dbName).Options);

    private static IMapper Mapper() => new MapperConfiguration(cfg =>
    {
        cfg.AddProfile<ItemBlueprintMappingProfile>();
        cfg.AddProfile<GradeMappingProfile>();
        cfg.AddProfile<TagMappingProfile>();
    }).CreateMapper();

    private static ItemBlueprintService Service(KnKDbContext db) => new(new ItemBlueprintRepository(db),
        Mock.Of<IMinecraftMaterialRefRepository>(), Mock.Of<IEnchantmentDefinitionRepository>(),
        Mock.Of<ICategoryRepository>(), Mock.Of<IGradeRepository>(), Mock.Of<ITagRepository>(),
        Mock.Of<IDomainRepository>(), Mapper());

    private static ItemBannerLayerDto Layer(string key, string color) => new() { PatternKey = key, Color = color };

    [Fact]
    public async Task Create_ThenRead_GivesTheSameShieldBannerLayersInTheSameOrder()
    {
        int id;
        await using (var db = NewContext())
        {
            var created = await Service(db).CreateAsync(new ItemBlueprintCreateDto
            {
                Name = "Kingsguard Shield", DefaultDisplayName = "&6Kingsguard Shield", MaxStackSize = 1,
                ShieldBannerBaseColor = "red",
                ShieldBannerLayers = new List<ItemBannerLayerDto>
                {
                    Layer("stripe_top", "YELLOW"), Layer("cross", "WHITE"), Layer("border", "BLACK"), Layer("stripe_top", "BLUE"),
                },
            });
            id = created.Id;
            created.ShieldBannerBaseColor.Should().Be("RED");
        }

        await using var read = NewContext();
        var dto = await Service(read).GetByIdAsync(id);

        dto!.ShieldBannerBaseColor.Should().Be("RED");
        dto.ShieldBannerLayers.Select(l => (l.PatternKey, l.Color)).Should().Equal(
            ("minecraft:stripe_top", "YELLOW"), ("minecraft:cross", "WHITE"), ("minecraft:border", "BLACK"), ("minecraft:stripe_top", "BLUE"));
        (dto.ArmorTrimPattern, dto.ArmorTrimMaterial).Should().Be(((string?)null, (string?)null));
    }

    [Fact]
    public async Task APlainShield_HasNoBannerData()
    {
        int id;
        await using (var db = NewContext())
        {
            id = (await Service(db).CreateAsync(new ItemBlueprintCreateDto { Name = "Plain Shield", DefaultDisplayName = "Shield", MaxStackSize = 1 })).Id;
        }

        await using var read = NewContext();
        var dto = await Service(read).GetByIdAsync(id);
        dto!.ShieldBannerBaseColor.Should().BeNull();
        dto.ShieldBannerLayers.Should().BeEmpty();
        (await read.Set<ItemBlueprintBannerLayer>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Update_ReplacesTheLayers_AndClearsThemWhenNoneAreSent()
    {
        int id;
        await using (var db = NewContext())
        {
            id = (await Service(db).CreateAsync(new ItemBlueprintCreateDto
            {
                Name = "Shield", DefaultDisplayName = "Shield", MaxStackSize = 1, ShieldBannerBaseColor = "WHITE",
                ShieldBannerLayers = new List<ItemBannerLayerDto> { Layer("globe", "PINK"), Layer("border", "BLACK") },
            })).Id;
        }

        await using (var db = NewContext())
        {
            await Service(db).UpdateAsync(id, new ItemBlueprintUpdateDto
            {
                Id = id, Name = "Shield", DefaultDisplayName = "Shield", MaxStackSize = 1, ShieldBannerBaseColor = "BLUE",
                ShieldBannerLayers = new List<ItemBannerLayerDto> { Layer("creeper", "LIME") },
            });
        }
        await using (var read = NewContext())
        {
            var dto = await Service(read).GetByIdAsync(id);
            dto!.ShieldBannerBaseColor.Should().Be("BLUE");
            dto.ShieldBannerLayers.Select(l => (l.PatternKey, l.Color)).Should().Equal(("minecraft:creeper", "LIME"));
            (await read.Set<ItemBlueprintBannerLayer>().CountAsync()).Should().Be(1, "the old layers are gone, not orphaned");
        }

        await using (var db = NewContext())
        {
            await Service(db).UpdateAsync(id, new ItemBlueprintUpdateDto { Id = id, Name = "Shield", DefaultDisplayName = "Shield", MaxStackSize = 1 });
        }
        await using var again = NewContext();
        var plain = await Service(again).GetByIdAsync(id);
        plain!.ShieldBannerBaseColor.Should().BeNull();
        plain.ShieldBannerLayers.Should().BeEmpty();
        (await again.Set<ItemBlueprintBannerLayer>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ArmorTrim_RoundTrips_AndAnUnknownKeyIsRefusedBeforeAnythingIsSaved()
    {
        int id;
        await using (var db = NewContext())
        {
            id = (await Service(db).CreateAsync(new ItemBlueprintCreateDto
            {
                Name = "Netherite Chestplate", DefaultDisplayName = "Chestplate", MaxStackSize = 1,
                ArmorTrimPattern = "silence", ArmorTrimMaterial = "Gold",
            })).Id;

            var bad = () => Service(db).CreateAsync(new ItemBlueprintCreateDto
            {
                Name = "Broken", DefaultDisplayName = "Broken", MaxStackSize = 1, ArmorTrimPattern = "paisley", ArmorTrimMaterial = "gold",
            });
            await bad.Should().ThrowAsync<ArgumentException>().WithMessage("*paisley*");
        }

        await using var read = NewContext();
        var dto = await Service(read).GetByIdAsync(id);
        (dto!.ArmorTrimPattern, dto.ArmorTrimMaterial).Should().Be(("minecraft:silence", "minecraft:gold"));
        (await read.ItemBlueprints.CountAsync()).Should().Be(1);
    }
}
