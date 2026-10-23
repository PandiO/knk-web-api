using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.MinecraftCatalog;
using Xunit;

namespace knkwebapi_v2.Tests.Services.MinecraftCatalog;

/// <summary>KNG-200 plan §5.2a: the snapshot fingerprint knk-plugin computes the same way (shared test vector).</summary>
public class MinecraftCatalogSnapshotHashTests
{
    public static MinecraftCatalogEntryDto Stone() => new()
    {
        Key = "minecraft:stone", IsItem = true, IsBlock = true, MaxStackSize = 64,
        TranslationKey = "block.minecraft.stone", DisplayName = "Stone", DefaultBlockState = "minecraft:stone",
    };

    public static MinecraftCatalogEntryDto Apple() => new()
    {
        Key = "minecraft:apple", IsItem = true, IsBlock = false, MaxStackSize = 64,
        TranslationKey = "item.minecraft.apple", DisplayName = null, DefaultBlockState = null,
    };

    [Fact]
    public void SharedTestVector()
    {
        Assert.Equal("86979e0606b8c36d782bf7bc0100dc2d3559aa53d714900419f0850431d5fb12",
            MinecraftCatalogSnapshotHash.Compute(new[] { Stone(), Apple() }));
    }

    [Fact]
    public void OrderOfEntries_DoesNotMatter_ButEveryFieldDoes()
    {
        var hash = MinecraftCatalogSnapshotHash.Compute(new[] { Apple(), Stone() });
        Assert.Equal(MinecraftCatalogSnapshotHash.Compute(new[] { Stone(), Apple() }), hash);

        var renamed = Stone();
        renamed.DisplayName = "Stone Block";
        Assert.NotEqual(hash, MinecraftCatalogSnapshotHash.Compute(new[] { Apple(), renamed }));
    }

    public static MinecraftCatalogEnchantmentDto Sharpness() => new()
    {
        Key = "minecraft:sharpness", IsCustom = false, MaxLevel = 5,
        TranslationKey = "enchantment.minecraft.sharpness", DisplayName = "Sharpness", Category = null,
    };

    public static MinecraftCatalogEnchantmentDto FlashChaos() => new()
    {
        Key = "knk:flash_chaos", IsCustom = true, MaxLevel = 1, TranslationKey = null, DisplayName = "Flash Chaos", Category = "SUPPORT",
    };

    /// <summary>KNG-250 §12.2: schema 2 appends the enchantment lines; knk-plugin pins the same two vectors.</summary>
    [Fact]
    public void SharedTestVectors_SchemaTwo()
    {
        Assert.Equal("ab2c65626e6508eb4c169dabefda5ef7570f94c8d966f31adecc624dfe823154",
            MinecraftCatalogSnapshotHash.Compute(new[] { Stone(), Apple() }, new[] { Sharpness(), FlashChaos() }));
        Assert.Equal("9846ad62d84d42d5a05e0cbac900ab6f671bf3bb0dd7c1bb5961aa2316f63c3e",
            MinecraftCatalogSnapshotHash.Compute(new[] { Stone(), Apple() }, Array.Empty<MinecraftCatalogEnchantmentDto>()));
    }

    [Fact]
    public void SchemaTwo_EnchantmentOrderDoesNotMatter_ButEveryFieldDoes()
    {
        var hash = MinecraftCatalogSnapshotHash.Compute(new[] { Stone() }, new[] { FlashChaos(), Sharpness() });
        Assert.Equal(MinecraftCatalogSnapshotHash.Compute(new[] { Stone() }, new[] { Sharpness(), FlashChaos() }), hash);

        var stronger = FlashChaos();
        stronger.MaxLevel = 2;
        Assert.NotEqual(hash, MinecraftCatalogSnapshotHash.Compute(new[] { Stone() }, new[] { Sharpness(), stronger }));
    }
}
