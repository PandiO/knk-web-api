using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Shops;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static knkwebapi_v2.Tests.Services.Shops.ShopTestDb;

namespace knkwebapi_v2.Tests.Services.Shops;

/// <summary>Display stalls P12 (PandiO/knk-workspace#250, shops DESIGN § 7): display spots registered per shop, one per
/// block, with an optional pin to an entry of the same shop.</summary>
public class ShopDisplaySpotTests : IDisposable
{
    private readonly ShopTestDb _s = new();
    private readonly ShopDisplaySpotService _spots;

    public ShopDisplaySpotTests() => _spots = new ShopDisplaySpotService(_s.E.Db);

    public void Dispose() => _s.Dispose();

    private Task<ShopDisplaySpotDto> Add(int shop, int x, string? facing = null, int? pin = null, int? sortOrder = null, int? actor = 7) =>
        _spots.CreateAsync(new ShopDisplaySpotDto
        {
            ShopId = shop, WorldName = "world", X = x, Y = 64, Z = 10, Facing = facing, PinnedCatalogueEntryId = pin, SortOrder = sortOrder,
        }, actor);

    [Fact]
    public async Task ASpot_IsRegisteredForAShop_WithDefaults_AndNumberedAfterTheLastSpot()
    {
        await _s.SetUpAsync();
        var first = await Add(_s.Bakery, 1);
        var second = await Add(_s.Bakery, 2, facing: "west");
        var other = await Add(_s.Weaponsmith, 3);

        Assert.Equal(("South", 1, true, 7), (first.Facing, first.SortOrder!.Value, first.Enabled!.Value, first.CreatedByUserId!.Value));
        Assert.Equal(("West", 2), (second.Facing, second.SortOrder!.Value));
        Assert.Equal(1, other.SortOrder);
        Assert.Equal(_s.Bakery, first.Shop!.Id);
        var listed = await _spots.SearchAsync(new PagedQueryDto { PageSize = 50, Filters = new() { ["shopId"] = _s.Bakery.ToString() } });
        Assert.Equal(new[] { first.Id!.Value, second.Id!.Value }, listed.Items.Select(i => i.Id).ToArray());
        Assert.Equal(3, (await _spots.SearchAsync(new PagedQueryDto { PageSize = 50, Filters = new() { ["worldName"] = "world" } })).TotalCount);
    }

    [Fact]
    public async Task ABlock_IsOneSpot_AndTheFieldsAreChecked()
    {
        await _s.SetUpAsync();
        await Add(_s.Bakery, 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Add(_s.Weaponsmith, 1));
        await Assert.ThrowsAsync<ArgumentException>(() => Add(12345, 2));
        await Assert.ThrowsAsync<ArgumentException>(() => Add(_s.Bakery, 2, facing: "Up"));
        await Assert.ThrowsAsync<ArgumentException>(() => _spots.CreateAsync(new ShopDisplaySpotDto { ShopId = _s.Bakery, X = 2, Y = 64, Z = 1 }, null));
        await Assert.ThrowsAsync<ArgumentException>(() => _spots.CreateAsync(new ShopDisplaySpotDto { ShopId = _s.Bakery, WorldName = "world", X = 2, Y = 400, Z = 1 }, null));
        await Assert.ThrowsAsync<ArgumentException>(() => _spots.CreateAsync(new ShopDisplaySpotDto { ShopId = _s.Bakery, WorldName = "world", X = 2, Z = 1 }, null));
    }

    [Fact]
    public async Task APin_MustBeAnEntryOfTheSpotsOwnShop_AndZeroClearsIt()
    {
        await _s.SetUpAsync();
        var bread = await _s.Entry(_s.Bakery, Bread);
        var sword = await _s.Entry(_s.Weaponsmith, Sword);
        await Assert.ThrowsAsync<ArgumentException>(() => Add(_s.Bakery, 1, pin: sword.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => Add(_s.Bakery, 1, pin: 99999));

        var spot = await Add(_s.Bakery, 1, pin: bread.Id);
        Assert.Equal(bread.Id, spot.PinnedCatalogueEntryId);
        Assert.Equal(bread.Id, spot.PinnedCatalogueEntry!.Id);
        var cleared = await _spots.UpdateAsync(spot.Id!.Value, new ShopDisplaySpotDto { PinnedCatalogueEntryId = 0 });
        Assert.Null(cleared.PinnedCatalogueEntryId);
        Assert.NotNull(cleared.UpdatedAt);
    }

    [Fact]
    public async Task ASpot_KeepsItsShop_CanMove_AndIsRemoved()
    {
        await _s.SetUpAsync();
        var a = await Add(_s.Bakery, 1);
        var b = await Add(_s.Bakery, 2);
        await Assert.ThrowsAsync<ArgumentException>(() => _spots.UpdateAsync(a.Id!.Value, new ShopDisplaySpotDto { ShopId = _s.Weaponsmith }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _spots.UpdateAsync(a.Id!.Value, new ShopDisplaySpotDto { X = 2 }));
        var moved = await _spots.UpdateAsync(a.Id!.Value, new ShopDisplaySpotDto { X = 5, Label = "Front shelf", Enabled = false, SortOrder = 9 });
        Assert.Equal((5, "Front shelf", false, 9), (moved.X!.Value, moved.Label, moved.Enabled!.Value, moved.SortOrder!.Value));
        Assert.Null((await _spots.UpdateAsync(a.Id!.Value, new ShopDisplaySpotDto { Label = "" })).Label);
        Assert.Single((await _spots.SearchAsync(new PagedQueryDto { Filters = new() { ["enabled"] = "true" } })).Items);

        await _spots.DeleteAsync(b.Id!.Value);
        Assert.Null(await _spots.GetByIdAsync(b.Id!.Value));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _spots.DeleteAsync(b.Id!.Value));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _spots.UpdateAsync(b.Id!.Value, new ShopDisplaySpotDto { X = 1 }));
    }

    [Fact]
    public async Task AShop_HasAtMostItsMaximumOfSpots()
    {
        await _s.SetUpAsync();
        for (int i = 0; i < ShopDisplaySpot.MaxPerShop; i++)
        {
            _s.E.Db.Set<ShopDisplaySpot>().Add(new ShopDisplaySpot { ShopId = _s.Bakery, WorldName = "world", X = 1000 + i, Y = 64, Z = 0 });
        }
        _s.E.Db.SaveChanges();
        _s.E.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Add(_s.Bakery, 1));
        Assert.NotNull(await Add(_s.Weaponsmith, 1));
    }
}
