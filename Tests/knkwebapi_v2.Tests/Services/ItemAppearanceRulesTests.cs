using FluentAssertions;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// PandiO/knk-workspace#232: the shield banner / armour trim fields of a blueprint are normalised on the way in
/// (any casing, bare or namespaced keys) and an unknown colour or key is refused, never dropped.
/// </summary>
public class ItemAppearanceRulesTests
{
    private static ItemBannerLayerDto Layer(string key, string color) => new() { PatternKey = key, Color = color };

    [Fact]
    public void Normalize_KeepsLayerOrderAndNormalisesKeysAndColours()
    {
        var look = ItemAppearanceRules.Normalize("red", new[]
        {
            Layer("Stripe_Top", "yellow"),
            Layer("minecraft:cross", "WHITE"),
            Layer("border", "light blue"),
        }, "Silence", "netherite");

        look.ShieldBannerBaseColor.Should().Be(BannerDyeColor.RED);
        look.ShieldBannerLayers.Should().Equal(
            ("minecraft:stripe_top", BannerDyeColor.YELLOW),
            ("minecraft:cross", BannerDyeColor.WHITE),
            ("minecraft:border", BannerDyeColor.LIGHT_BLUE));
        (look.ArmorTrimPattern, look.ArmorTrimMaterial).Should().Be(("minecraft:silence", "minecraft:netherite"));
    }

    [Fact]
    public void Normalize_NothingSet_IsNone()
    {
        var look = ItemAppearanceRules.Normalize(null, null, null, null);
        look.Should().BeEquivalentTo(ItemAppearance.None);

        var blank = ItemAppearanceRules.Normalize("  ", new List<ItemBannerLayerDto>(), "", " ");
        blank.Should().BeEquivalentTo(ItemAppearance.None);
    }

    [Fact]
    public void Normalize_LayersWithoutABaseColour_GetMinecraftsWhiteShield()
    {
        var look = ItemAppearanceRules.Normalize(null, new[] { Layer("stripe_bottom", "RED") }, null, null);
        look.ShieldBannerBaseColor.Should().Be(BannerDyeColor.WHITE);
    }

    [Fact]
    public void Normalize_RefusesUnknownColoursAndKeys()
    {
        FluentActions.Invoking(() => ItemAppearanceRules.Normalize("maroon", null, null, null))
            .Should().Throw<ArgumentException>().WithMessage("*maroon*");
        FluentActions.Invoking(() => ItemAppearanceRules.Normalize("RED", new[] { Layer("minecraft:from_the_future", "RED") }, null, null))
            .Should().Throw<ArgumentException>().WithMessage("*layer 1*from_the_future*");
        FluentActions.Invoking(() => ItemAppearanceRules.Normalize("RED", new[] { Layer("cross", "plaid") }, null, null))
            .Should().Throw<ArgumentException>().WithMessage("*layer 1*plaid*");
        FluentActions.Invoking(() => ItemAppearanceRules.Normalize(null, null, "silence", "wool"))
            .Should().Throw<ArgumentException>().WithMessage("*wool*");
        FluentActions.Invoking(() => ItemAppearanceRules.Normalize(null, null, "cuneiform", "gold"))
            .Should().Throw<ArgumentException>().WithMessage("*cuneiform*");
    }

    [Fact]
    public void Normalize_ATrimNeedsBothPatternAndMaterial()
    {
        FluentActions.Invoking(() => ItemAppearanceRules.Normalize(null, null, "silence", null))
            .Should().Throw<ArgumentException>().WithMessage("*both*");
        FluentActions.Invoking(() => ItemAppearanceRules.Normalize(null, null, null, "netherite"))
            .Should().Throw<ArgumentException>().WithMessage("*both*");
    }

    [Fact]
    public void Apply_ReplacesTheBlueprintsLayersAsAWhole_BottomFirst()
    {
        var blueprint = new ItemBlueprint { Name = "Shield" };
        blueprint.ShieldBannerLayers.Add(new ItemBlueprintBannerLayer { SortOrder = 0, PatternKey = "minecraft:globe", Color = BannerDyeColor.PINK });

        ItemAppearanceRules.Apply(blueprint, ItemAppearanceRules.Normalize("BLUE",
            new[] { Layer("stripe_top", "RED"), Layer("border", "BLACK") }, null, null));

        blueprint.ShieldBannerBaseColor.Should().Be(BannerDyeColor.BLUE);
        blueprint.ShieldBannerLayers.Select(l => (l.SortOrder, l.PatternKey, l.Color)).Should().Equal(
            (0, "minecraft:stripe_top", BannerDyeColor.RED),
            (1, "minecraft:border", BannerDyeColor.BLACK));
        (blueprint.ArmorTrimPattern, blueprint.ArmorTrimMaterial).Should().Be(((string?)null, (string?)null));
    }

    [Fact]
    public void Of_ReadsLayersBySortOrderThenId_WhateverTheCollectionOrder()
    {
        var blueprint = new ItemBlueprint { ShieldBannerBaseColor = BannerDyeColor.GREEN };
        blueprint.ShieldBannerLayers.Add(new ItemBlueprintBannerLayer { Id = 3, SortOrder = 1, PatternKey = "minecraft:border", Color = BannerDyeColor.BLACK });
        blueprint.ShieldBannerLayers.Add(new ItemBlueprintBannerLayer { Id = 2, SortOrder = 0, PatternKey = "minecraft:cross", Color = BannerDyeColor.WHITE });
        blueprint.ShieldBannerLayers.Add(new ItemBlueprintBannerLayer { Id = 1, SortOrder = 0, PatternKey = "minecraft:stripe_top", Color = BannerDyeColor.RED });

        var dtos = ItemAppearanceRules.ToDtos(ItemAppearanceRules.Of(blueprint));

        dtos.Select(d => (d.PatternKey, d.Color)).Should().Equal(
            ("minecraft:stripe_top", "RED"), ("minecraft:cross", "WHITE"), ("minecraft:border", "BLACK"));
    }

    [Fact]
    public void ArmorTrimKeys_NormaliseLikeBannerPatternKeys()
    {
        ArmorTrimKeys.NormalizePattern("Wayfinder").Should().Be("minecraft:wayfinder");
        ArmorTrimKeys.NormalizePattern("minecraft:bolt").Should().Be("minecraft:bolt");
        ArmorTrimKeys.NormalizePattern("knk:bolt").Should().BeNull();
        ArmorTrimKeys.NormalizeMaterial(" AMETHYST ").Should().Be("minecraft:amethyst");
        ArmorTrimKeys.NormalizeMaterial(null).Should().BeNull();
    }
}
