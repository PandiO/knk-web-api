using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// PandiO/knk-workspace#409: a town's streets are the distinct streets of its districts, read-only, and follow every
/// change on the districts at once. Saving a town writes no district or street rows.
/// </summary>
public class TownStreetsDerivedTests : IDisposable
{
    private readonly SaveRecorder _saves = new();
    private readonly KnKDbContext _db;
    private readonly TownService _service;

    public TownStreetsDerivedTests()
    {
        _db = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase("town-streets-" + Guid.NewGuid())
            .AddInterceptors(_saves)
            .Options);
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<TownMappingProfile>();
            cfg.AddProfile<LocationMappingProfile>();
            cfg.AddProfile<DomainMappingProfile>();
        }).CreateMapper();
        _service = new TownService(
            new TownRepository(_db),
            new Mock<ILocationRepository>().Object,
            new Mock<ILocationService>().Object,
            mapper,
            new Mock<ITeleportDestinationService>().Object,
            new Mock<IDomainRegionNameFinalizer>().Object,
            FixedWorldResolver.Instance);
    }

    public void Dispose() => _db.Dispose();

    private static Town Town(int id, string name) =>
        new() { Id = id, Name = name, Description = "", WgRegionId = $"domain_{id}", WorldName = FixedWorldResolver.World };

    private static District District(int id, int townId, string name, params Street[] streets)
    {
        var district = new District { Id = id, Name = name, Description = "", WgRegionId = $"domain_{id}", TownId = townId, WorldName = FixedWorldResolver.World };
        foreach (var street in streets) district.Streets.Add(street);
        return district;
    }

    /// <summary>Cinix (1): Old Quarter (10) with Main and harbour road, Harbour (11) with harbour road and Mill Lane.
    /// Oakhaven (2): Market (20) with Oak Avenue. Lonely Lane (9) is in no district.</summary>
    private async Task SeedAsync()
    {
        var main = new Street { Id = 5, Name = "Main Street" };
        var harbour = new Street { Id = 3, Name = "harbour road" };
        var mill = new Street { Id = 7, Name = "Mill Lane" };
        var oak = new Street { Id = 8, Name = "Oak Avenue" };
        _db.Streets.AddRange(main, harbour, mill, oak, new Street { Id = 9, Name = "Lonely Lane" });
        _db.Towns.AddRange(Town(1, "Cinix"), Town(2, "Oakhaven"), Town(3, "Empty"));
        _db.Districts.AddRange(
            District(10, 1, "Old Quarter", main, harbour),
            District(11, 1, "Harbour", harbour, mill),
            District(20, 2, "Market", oak));
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private async Task<TownDto> ReadAsync(int id) => (await _service.GetByIdAsync(id))!;

    // ===== read =====

    [Fact]
    public async Task Detail_ListsTheStreetsOfItsDistricts_OnceEach_ByNameThenId()
    {
        await SeedAsync();

        var town = await ReadAsync(1);

        // harbour road is in both districts and shows once; the order ignores case.
        Assert.Equal(new[] { "harbour road", "Main Street", "Mill Lane" }, town.Streets!.Select(s => s.Name));
        Assert.Equal(new[] { 3, 5, 7 }, town.StreetIds);
        Assert.DoesNotContain(town.Streets!, s => s.Id == 8); // Oakhaven's street
        Assert.DoesNotContain(town.Streets!, s => s.Id == 9); // in no district
    }

    [Fact]
    public async Task SameName_IsOrderedById()
    {
        _db.Towns.Add(Town(1, "Cinix"));
        _db.Districts.Add(District(10, 1, "Old Quarter", new Street { Id = 12, Name = "Gate" }, new Street { Id = 4, Name = "gate" }));
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        Assert.Equal(new[] { 4, 12 }, (await ReadAsync(1)).StreetIds);
    }

    [Fact]
    public async Task ATownWithoutDistricts_HasNoStreets()
    {
        await SeedAsync();

        var town = await ReadAsync(3);

        Assert.Empty(town.Streets!);
        Assert.Empty(town.StreetIds!);
    }

    [Fact]
    public void TheList_IsComputedOnTheModel_FromTheLoadedDistricts()
    {
        var shared = new Street { Id = 1, Name = "Shared" };
        var town = new Town { Districts = { District(10, 1, "A", shared), District(11, 1, "B", new Street { Id = 1, Name = "Shared" }) } };

        Assert.Equal(new[] { 1 }, town.Streets.Select(s => s.Id));
        Assert.Empty(new Town().Streets);
    }

    // ===== following the districts =====

    [Fact]
    public async Task LinkingOrUnlinkingAStreetOnADistrict_ShowsOnTheTownAtOnce()
    {
        await SeedAsync();
        var relationships = new RelationshipService(_db);

        await relationships.LinkAsync("District", 10, "streets", 9, false);
        Assert.Equal(new[] { 3, 9, 5, 7 }, (await ReadAsync(1)).StreetIds);

        await relationships.UnlinkAsync("District", 11, "streets", 7);
        Assert.Equal(new[] { 3, 9, 5 }, (await ReadAsync(1)).StreetIds);

        // harbour road stays while one district still has it.
        await relationships.UnlinkAsync("District", 10, "streets", 3);
        Assert.Contains(3, (await ReadAsync(1)).StreetIds!);
    }

    [Fact]
    public async Task ADistrictMovingToAnotherTown_TakesItsStreetsWithIt()
    {
        await SeedAsync();

        var harbour = await _db.Districts.SingleAsync(d => d.Id == 11);
        harbour.TownId = 2;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        Assert.Equal(new[] { 3, 5 }, (await ReadAsync(1)).StreetIds);
        Assert.Equal(new[] { 3, 7, 8 }, (await ReadAsync(2)).StreetIds);
    }

    // ===== saving a town =====

    [Fact]
    public async Task UpdatingATown_WritesOnlyTheTown()
    {
        await SeedAsync();
        _saves.Changed.Clear();

        await _service.UpdateAsync(1, new TownDto { Name = "Cinix Prime", Description = "The capital", WgRegionId = "domain_1" });

        var written = Assert.Single(_saves.Changed);
        Assert.Equal((nameof(Models.Town), EntityState.Modified), written);
        Assert.Equal("Cinix Prime", (await ReadAsync(1)).Name);
        Assert.Equal(new[] { 3, 5, 7 }, (await ReadAsync(1)).StreetIds);
    }

    /// <summary>Records which entities a SaveChanges writes (type and state), before it writes them.</summary>
    private sealed class SaveRecorder : SaveChangesInterceptor
    {
        public List<(string Type, EntityState State)> Changed { get; } = new();

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Record(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Record(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        private void Record(DbContext? context)
        {
            if (context == null) return;
            context.ChangeTracker.DetectChanges();
            Changed.AddRange(context.ChangeTracker.Entries()
                .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Select(e => (e.Metadata.ClrType.Name, e.State)));
        }
    }
}
