using knkwebapi_v2.Services.MinecraftCatalog;
using Xunit;

namespace knkwebapi_v2.Tests.Services.MinecraftCatalog;

/// <summary>KNG-200 §4: canonical block states and their identity.</summary>
public class MinecraftBlockStateTests
{
    [Theory]
    [InlineData("minecraft:oak_stairs[half=bottom,facing=north]")]
    [InlineData("[facing=north,half=bottom]")]
    [InlineData("facing=north, half=bottom")]
    [InlineData(" Minecraft:Oak_Stairs[ FACING=North ,half=bottom] ")]
    public void EveryNotationOfOneState_IsTheSameCanonicalState(string state)
    {
        var canonical = MinecraftBlockState.TryCanonicalize("minecraft:oak_stairs", state);

        Assert.NotNull(canonical);
        Assert.Equal("minecraft:oak_stairs[facing=north,half=bottom]", canonical!.State);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("minecraft:oak_stairs")]
    [InlineData("oak_stairs[]")]
    public void NoProperties_MeansAnyState(string? state)
    {
        var canonical = MinecraftBlockState.TryCanonicalize("oak_stairs", state);

        Assert.NotNull(canonical);
        Assert.Equal("minecraft:oak_stairs", canonical!.NamespaceKey);
        Assert.Null(canonical.State);
    }

    [Theory]
    [InlineData("minecraft:birch_stairs[facing=north]")] // another block
    [InlineData("minecraft:birch_stairs")]
    [InlineData("[facing=north")] // unclosed
    [InlineData("[facing]")] // no value
    [InlineData("[facing=north,facing=south]")] // twice
    [InlineData("[facing=no rth]")]
    public void Malformed_OrForAnotherBlock_IsRejected(string state)
    {
        Assert.Null(MinecraftBlockState.TryCanonicalize("minecraft:oak_stairs", state));
    }

    [Fact]
    public void InvalidKey_IsRejected()
    {
        Assert.Null(MinecraftBlockState.TryCanonicalize("oak stairs", "[facing=north]"));
    }

    [Fact]
    public void StateKey_IsStablePerIdentity_AndDiffersPerStateAndBlock()
    {
        var a = MinecraftBlockState.StateKey(MinecraftBlockState.TryCanonicalize("minecraft:oak_stairs", "[facing=north]")!);
        var b = MinecraftBlockState.StateKey(MinecraftBlockState.TryCanonicalize("OAK_STAIRS", "facing=NORTH")!);
        var partialVsFull = MinecraftBlockState.StateKey(MinecraftBlockState.TryCanonicalize("minecraft:oak_stairs", "[facing=north,half=bottom]")!);
        var anyState = MinecraftBlockState.StateKey(MinecraftBlockState.TryCanonicalize("minecraft:oak_stairs", null)!);
        var otherBlock = MinecraftBlockState.StateKey(MinecraftBlockState.TryCanonicalize("minecraft:birch_stairs", "[facing=north]")!);

        Assert.Equal(a, b);
        Assert.Equal(64, a.Length);
        Assert.Matches("^[0-9a-f]{64}$", a);
        Assert.Equal(4, new[] { a, partialVsFull, anyState, otherBlock }.Distinct().Count());
    }
}
