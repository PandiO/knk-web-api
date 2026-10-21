using knkwebapi_v2.Services.MinecraftCatalog;
using Xunit;

namespace knkwebapi_v2.Tests.Services.MinecraftCatalog;

/// <summary>KNG-200 §3: the one key normalisation.</summary>
public class MinecraftNamespaceKeyTests
{
    [Theory]
    [InlineData("minecraft:stone", "minecraft:stone")]
    [InlineData("Minecraft:Stone", "minecraft:stone")]
    [InlineData("  minecraft:oak_planks ", "minecraft:oak_planks")]
    [InlineData("STONE", "minecraft:stone")]
    [InlineData("oak_planks", "minecraft:oak_planks")]
    [InlineData("knk:custom/item.v2", "knk:custom/item.v2")]
    public void TryNormalize_ReturnsTheNormalForm(string raw, string expected)
    {
        Assert.Equal(expected, MinecraftNamespaceKey.TryNormalize(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("minecraft:oak planks")]
    [InlineData("minecraft:")]
    [InlineData(":stone")]
    [InlineData("minecraft:stone!")]
    [InlineData("a:b:c")]
    public void TryNormalize_RejectsWhatIsNotAKey(string? raw)
    {
        Assert.Null(MinecraftNamespaceKey.TryNormalize(raw));
    }

    [Fact]
    public void Normalize_ThrowsForAnInvalidKey()
    {
        Assert.Throws<ArgumentException>(() => MinecraftNamespaceKey.Normalize("oak planks"));
    }

    [Theory]
    [InlineData("minecraft:oak_stairs", "Oak Stairs")]
    [InlineData("minecraft:tnt", "Tnt")]
    [InlineData("knk:custom/fire_sword", "Fire Sword")]
    public void DisplayNameFromKey_TitleCasesTheLastPathPart(string key, string expected)
    {
        Assert.Equal(expected, MinecraftNamespaceKey.DisplayNameFromKey(key));
    }
}
