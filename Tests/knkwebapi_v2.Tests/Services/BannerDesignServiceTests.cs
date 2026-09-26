using AutoMapper;
using Moq;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Siege Phase 1 (docs/specs/siege-minigame/IMPLEMENTATION_PLAN.md Phase 1): BannerDesign +
/// owned BannerLayer validation - layer cap, pattern-key whitelist, ordering, delete guard.
/// </summary>
public class BannerDesignServiceTests
{
    private readonly Mock<IBannerDesignRepository> _repo = new();
    private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<ClanMappingProfile>()).CreateMapper();
    private readonly BannerDesignService _service;
    private BannerLayer? _addedLayer;

    public BannerDesignServiceTests()
    {
        _repo.Setup(r => r.AddLayerAsync(It.IsAny<BannerLayer>()))
            .Callback<BannerLayer>(l => _addedLayer = l)
            .Returns(Task.CompletedTask);
        _service = new BannerDesignService(_repo.Object, _mapper);
    }

    private BannerDesign GivenDesign(int id, int layerCount = 0)
    {
        var design = new BannerDesign { Id = id, Name = "Cinix crown", BaseColor = BannerDyeColor.RED };
        for (var i = 0; i < layerCount; i++)
            design.Layers.Add(new BannerLayer { Id = 100 + i, BannerDesignId = id, SortOrder = i, PatternKey = "minecraft:border", Color = BannerDyeColor.BLACK });
        _repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(design);
        return design;
    }

    [Fact]
    public async Task CreateLayer_AppendsOnTop_WhenSortOrderOmitted()
    {
        GivenDesign(1, layerCount: 3);

        var created = await _service.CreateLayerAsync(1, new BannerLayerUpsertDto { PatternKey = "minecraft:stripe_top", Color = BannerDyeColor.YELLOW });

        Assert.Equal(3, created.SortOrder);
        Assert.Equal(1, _addedLayer!.BannerDesignId);
    }

    [Fact]
    public async Task CreateLayer_FirstLayerGetsSortOrderZero()
    {
        GivenDesign(1);

        var created = await _service.CreateLayerAsync(1, new BannerLayerUpsertDto { PatternKey = "minecraft:cross" });

        Assert.Equal(0, created.SortOrder);
    }

    [Fact]
    public async Task CreateLayer_RejectsSeventeenthLayer()
    {
        GivenDesign(1, layerCount: BannerDesignService.MaxLayers);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateLayerAsync(1, new BannerLayerUpsertDto { PatternKey = "minecraft:cross" }));
        Assert.Contains("16", ex.Message);
        _repo.Verify(r => r.AddLayerAsync(It.IsAny<BannerLayer>()), Times.Never);
    }

    [Fact]
    public async Task CreateLayer_AllowsSixteenthLayer()
    {
        GivenDesign(1, layerCount: BannerDesignService.MaxLayers - 1);

        await _service.CreateLayerAsync(1, new BannerLayerUpsertDto { PatternKey = "minecraft:cross" });

        _repo.Verify(r => r.AddLayerAsync(It.IsAny<BannerLayer>()), Times.Once);
    }

    [Theory]
    [InlineData("minecraft:not_a_pattern")]
    [InlineData("stripe_sideways")]
    [InlineData("")]
    [InlineData("othermod:stripe_top")]
    public async Task CreateLayer_RejectsUnknownPattern(string key)
    {
        GivenDesign(1);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateLayerAsync(1, new BannerLayerUpsertDto { PatternKey = key }));
        Assert.Contains("pattern", ex.Message);
    }

    [Theory]
    [InlineData("stripe_top")]
    [InlineData("STRIPE_TOP")]
    [InlineData("  minecraft:Stripe_Top ")]
    public async Task CreateLayer_NormalizesPatternKey(string key)
    {
        GivenDesign(1);

        var created = await _service.CreateLayerAsync(1, new BannerLayerUpsertDto { PatternKey = key });

        Assert.Equal("minecraft:stripe_top", created.PatternKey);
    }

    [Fact]
    public async Task CreateLayer_RejectsMismatchedBodyBannerId()
    {
        GivenDesign(1);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateLayerAsync(1, new BannerLayerUpsertDto { BannerDesignId = 2, PatternKey = "minecraft:cross" }));
    }

    [Fact]
    public async Task CreateLayer_UnknownBanner_IsNotFound()
    {
        _repo.Setup(r => r.GetByIdAsync(9)).ReturnsAsync((BannerDesign?)null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.CreateLayerAsync(9, new BannerLayerUpsertDto { PatternKey = "minecraft:cross" }));
    }

    [Fact]
    public async Task CreateLayer_RejectsUndefinedColour()
    {
        GivenDesign(1);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateLayerAsync(1, new BannerLayerUpsertDto { PatternKey = "minecraft:cross", Color = (BannerDyeColor)99 }));
    }

    [Fact]
    public async Task UpdateLayer_CannotMoveToAnotherBanner()
    {
        _repo.Setup(r => r.GetLayerByIdAsync(5)).ReturnsAsync(new BannerLayer { Id = 5, BannerDesignId = 1, PatternKey = "minecraft:cross" });

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateLayerAsync(5, new BannerLayerUpsertDto { BannerDesignId = 2, PatternKey = "minecraft:cross" }));
    }

    [Fact]
    public async Task UpdateLayer_KeepsSortOrder_WhenOmitted()
    {
        var layer = new BannerLayer { Id = 5, BannerDesignId = 1, SortOrder = 4, PatternKey = "minecraft:cross", Color = BannerDyeColor.BLACK };
        _repo.Setup(r => r.GetLayerByIdAsync(5)).ReturnsAsync(layer);

        await _service.UpdateLayerAsync(5, new BannerLayerUpsertDto { PatternKey = "circle", Color = BannerDyeColor.WHITE });

        Assert.Equal(4, layer.SortOrder);
        Assert.Equal("minecraft:circle", layer.PatternKey);
        Assert.Equal(BannerDyeColor.WHITE, layer.Color);
    }

    [Fact]
    public async Task Create_RequiresName()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(new BannerDesignUpsertDto { Name = "  " }));
    }

    [Fact]
    public async Task Delete_IsRefused_WhileAClanUsesTheBanner()
    {
        GivenDesign(1);
        _repo.Setup(r => r.IsReferencedByClanAsync(1)).ReturnsAsync(true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DeleteAsync(1));
        _repo.Verify(r => r.DeleteAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Read_OrdersLayersBottomToTop_AndFlagsSurvivalLimit()
    {
        var design = new BannerDesign { Id = 1, Name = "b", BaseColor = BannerDyeColor.WHITE };
        // Deliberately inserted out of order; the tie on SortOrder 2 is broken by Id.
        foreach (var (id, order) in new[] { (10, 5), (11, 0), (13, 2), (12, 2), (14, 1), (15, 3), (16, 4) })
            design.Layers.Add(new BannerLayer { Id = id, BannerDesignId = 1, SortOrder = order, PatternKey = "minecraft:cross" });
        _repo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(design);

        var dto = await _service.GetByIdAsync(1);

        Assert.Equal(new[] { 11, 14, 12, 13, 15, 16, 10 }, dto!.Layers.Select(l => l.Id).ToArray());
        Assert.True(dto.ExceedsSurvivalLoomLimit); // 7 layers > 6
    }

    [Fact]
    public void PatternKeys_AreCanonicalAndUnique()
    {
        Assert.All(BannerPatternKeys.All, k => Assert.Equal(k, BannerPatternKeys.Normalize(k)));
        Assert.Equal(BannerPatternKeys.All.Count, BannerPatternKeys.All.Distinct().Count());
    }
}
