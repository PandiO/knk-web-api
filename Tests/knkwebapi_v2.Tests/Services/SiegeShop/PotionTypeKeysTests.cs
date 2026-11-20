using AutoMapper;
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

namespace knkwebapi_v2.Tests.Services.SiegeShop;

/// <summary>
/// Siege shop DESIGN § 9.4 (PandiO/knk-workspace#867): an item blueprint can describe a potion's type, so the shop's
/// healing II splash and regeneration potions are real potions. Keys are normalised and an unknown vanilla key is a 400.
/// </summary>
public class PotionTypeKeysTests
{
    [Theory]
    [InlineData("strong_healing", "minecraft:strong_healing")]
    [InlineData(" Minecraft:Regeneration ", "minecraft:regeneration")]
    [InlineData("long regeneration", "minecraft:long_regeneration")]
    [InlineData("mypack:berserk", "mypack:berserk")]
    public void Normalize_AcceptsVanillaAndDatapackKeys(string input, string expected) =>
        Assert.Equal(expected, PotionTypeKeys.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Normalize_BlankIsNone(string? input) => Assert.Null(PotionTypeKeys.Normalize(input));

    [Theory]
    [InlineData("healing_iii")]
    [InlineData("minecraft:strong_regen")]
    [InlineData("bad key!")]
    public void Normalize_RefusesUnknownOrMalformedKeys(string input) =>
        Assert.Throws<ArgumentException>(() => PotionTypeKeys.Normalize(input));

    private static ItemBlueprintService Service(KnKDbContext db) => new(new ItemBlueprintRepository(db),
        Mock.Of<IMinecraftMaterialRefRepository>(), Mock.Of<IEnchantmentDefinitionRepository>(),
        Mock.Of<ICategoryRepository>(), Mock.Of<IGradeRepository>(), Mock.Of<ITagRepository>(),
        Mock.Of<IDomainRepository>(), new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<ItemBlueprintMappingProfile>();
            cfg.AddProfile<GradeMappingProfile>();
            cfg.AddProfile<TagMappingProfile>();
        }).CreateMapper());

    [Fact]
    public async Task Blueprint_SavesReadsAndClearsItsPotionType()
    {
        var options = new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase($"PotionType_{Guid.NewGuid()}").Options;
        int id;
        await using (var db = new KnKDbContext(options))
        {
            var created = await Service(db).CreateAsync(new ItemBlueprintCreateDto
            {
                Name = "Splash Potion of Healing II", DefaultDisplayName = "Healing", MaxStackSize = 1, PotionType = "strong_healing",
            });
            id = created.Id;
            Assert.Equal("minecraft:strong_healing", created.PotionType);
        }

        await using (var db = new KnKDbContext(options))
        {
            Assert.Equal("minecraft:strong_healing", (await Service(db).GetByIdAsync(id))!.PotionType);
            await Assert.ThrowsAsync<ArgumentException>(() => Service(db).UpdateAsync(id, new ItemBlueprintUpdateDto
            {
                Name = "Splash Potion of Healing II", DefaultDisplayName = "Healing", MaxStackSize = 1, PotionType = "healing_iii",
            }));
        }

        await using (var db = new KnKDbContext(options))
        {
            await Service(db).UpdateAsync(id, new ItemBlueprintUpdateDto { Name = "Plain", DefaultDisplayName = "Plain", MaxStackSize = 1 });
        }

        await using (var db = new KnKDbContext(options))
        {
            Assert.Null((await Service(db).GetByIdAsync(id))!.PotionType);
        }
    }
}
