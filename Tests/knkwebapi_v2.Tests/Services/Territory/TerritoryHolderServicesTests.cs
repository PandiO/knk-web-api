using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using static knkwebapi_v2.Tests.Services.Territory.TerritoryTestDb;

namespace knkwebapi_v2.Tests.Services.Territory;

/// <summary>
/// Holders (territory DESIGN § 2, PandiO/knk-workspace#326): kingdoms, provinces in one kingdom, the town's kingdom,
/// province and power with the same-kingdom check, and crown land as a kingdom-owned domain with a range (G10).
/// </summary>
public class TerritoryHolderServicesTests : IDisposable
{
    private readonly KnKDbContext _db = Create();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Kingdom_NameIsUniqueIgnoringCase_AndTrimmed()
    {
        var service = new KingdomService(_db);

        var created = await service.CreateAsync(new KingdomDto { Name = "  Borealis " });

        Assert.Equal("Borealis", created.Name);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new KingdomDto { Name = "aurelia" }));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new KingdomDto { Name = " " }));
    }

    [Fact]
    public async Task Kingdom_WithTowns_CannotBeDeleted()
    {
        var service = new KingdomService(_db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(KingdomId));

        Assert.Contains("2 town(s)", ex.Message);
        var empty = await service.CreateAsync(new KingdomDto { Name = "Empty" });
        await service.DeleteAsync(empty.Id!.Value);
        Assert.Null(await service.GetByIdAsync(empty.Id.Value));
    }

    [Fact]
    public async Task Kingdom_Read_ListsItsTowns_AndTheSearchCountsThem()
    {
        var service = new KingdomService(_db);

        var dto = await service.GetByIdAsync(KingdomId);
        var page = await service.SearchAsync(new PagedQueryDto { SearchTerm = "aur" });

        Assert.Equal(new[] { "Phoenix", "Rivertown" }, dto!.Towns!.Select(t => t.Name));
        Assert.Equal(2, Assert.Single(page.Items).TownCount);
    }

    [Fact]
    public async Task Province_NeedsAKingdom_AndItsNameIsUniqueThere()
    {
        var service = new ProvinceService(_db);

        var created = await service.CreateAsync(new ProvinceDto { Name = "North", KingdomId = KingdomId });

        Assert.Equal("Aurelia", created.Kingdom!.Name);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new ProvinceDto { Name = "North", KingdomId = KingdomId }));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new ProvinceDto { Name = "South", KingdomId = 999 }));
    }

    [Fact]
    public async Task Province_WithTowns_CannotMoveKingdom_OrBeDeleted()
    {
        var provinces = new ProvinceService(_db);
        var other = await new KingdomService(_db).CreateAsync(new KingdomDto { Name = "Borealis" });
        var north = await provinces.CreateAsync(new ProvinceDto { Name = "North", KingdomId = KingdomId });
        var phoenix = await _db.Towns.SingleAsync(t => t.Id == Phoenix);
        phoenix.ProvinceId = north.Id;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            provinces.UpdateAsync(north.Id!.Value, new ProvinceDto { Name = "North", KingdomId = other.Id!.Value }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provinces.DeleteAsync(north.Id!.Value));
    }

    [Fact]
    public async Task Town_ProvinceMustBeInTheTownsKingdom()
    {
        var rules = new TerritoryHolderRules(_db);
        var other = await new KingdomService(_db).CreateAsync(new KingdomDto { Name = "Borealis" });
        var north = await new ProvinceService(_db).CreateAsync(new ProvinceDto { Name = "North", KingdomId = KingdomId });

        await rules.ValidateTownAsync(new TownDto { Power = 5, KingdomId = KingdomId, ProvinceId = north.Id });
        await Assert.ThrowsAsync<ArgumentException>(() =>
            rules.ValidateTownAsync(new TownDto { Power = 5, KingdomId = other.Id, ProvinceId = north.Id }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            rules.ValidateTownAsync(new TownDto { Power = 5, KingdomId = null, ProvinceId = north.Id }));
        await Assert.ThrowsAsync<ArgumentException>(() => rules.ValidateTownAsync(new TownDto { Power = -1 }));
        await Assert.ThrowsAsync<ArgumentException>(() => rules.ValidateTownAsync(new TownDto { Power = 1, KingdomId = 999 }));
        // Small kingdoms need no provinces; a form without the territory fields changes nothing.
        await rules.ValidateTownAsync(new TownDto { Power = 0, KingdomId = KingdomId });
        await rules.ValidateTownAsync(new TownDto { Power = null, ProvinceId = 999 });
    }

    [Fact]
    public void Town_TerritoryFields_AreAppliedOnlyWhenPowerIsOnTheForm()
    {
        var town = new Town { Power = 50, KingdomId = KingdomId };

        TerritoryHolderRules.ApplyTown(town, new TownDto { Power = null, KingdomId = null });
        Assert.Equal((50, (int?)KingdomId), (town.Power, town.KingdomId));

        TerritoryHolderRules.ApplyTown(town, new TownDto { Power = 20, KingdomId = null, ProvinceId = null });
        Assert.Equal((20, (int?)null), (town.Power, town.KingdomId));
    }

    private CrownLandService CrownLands()
    {
        var worlds = new Mock<IDomainWorldResolver>();
        worlds.Setup(w => w.ResolveAsync(It.IsAny<DomainWorldRequest>())).ReturnsAsync("world");
        var names = new Mock<IDomainRegionNameFinalizer>();
        names.Setup(n => n.FinalizeAsync(It.IsAny<Domain>())).ReturnsAsync(true);
        return new CrownLandService(_db, worlds.Object, names.Object);
    }

    [Fact]
    public async Task CrownLand_IsADomainOwnedByAKingdom_WithARange()
    {
        var service = CrownLands();

        var created = await service.CreateAsync(new CrownLandDto
        {
            Name = " Royal forest ", Description = "Hunting grounds", WgRegionId = "tempregion_abc", KingdomId = KingdomId, Range = 80,
        });

        Assert.Equal(("Royal forest", "world", 80, "Aurelia"), (created.Name, created.WorldName, created.Range, created.Kingdom!.Name));
        Assert.True(await _db.Domains.AnyAsync(d => d.Id == created.Id)); // a Domain like towns and districts (G10)
        var page = await service.SearchAsync(new PagedQueryDto { Filters = new() { ["kingdomId"] = KingdomId.ToString() } });
        Assert.Equal("Aurelia", Assert.Single(page.Items).KingdomName);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(10001, 1)]
    [InlineData(10, 999)]
    public async Task CrownLand_RangeOutOfBoundsOrUnknownKingdom_IsRefused(int range, int kingdomId)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => CrownLands().CreateAsync(new CrownLandDto
        {
            Name = "Fort", Description = "", WgRegionId = "r", KingdomId = kingdomId, Range = range,
        }));
    }

    [Fact]
    public async Task CrownLand_Update_ChangesRangeAndKingdom()
    {
        var service = CrownLands();
        var other = await new KingdomService(_db).CreateAsync(new KingdomDto { Name = "Borealis" });
        var created = await service.CreateAsync(new CrownLandDto { Name = "Fort", Description = "", WgRegionId = "r", KingdomId = KingdomId, Range = 10 });

        await service.UpdateAsync(created.Id!.Value, new CrownLandDto { Name = "Fort", Description = "", WgRegionId = "r", KingdomId = other.Id!.Value, Range = 40 });

        var read = await service.GetByIdAsync(created.Id.Value);
        Assert.Equal((other.Id.Value, 40), (read!.KingdomId, read.Range));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UpdateAsync(999, new CrownLandDto { Name = "x", WgRegionId = "r", KingdomId = KingdomId }));
    }
}
