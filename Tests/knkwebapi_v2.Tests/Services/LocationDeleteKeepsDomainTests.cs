using System;
using System.Threading.Tasks;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>KNG-118 D3 (developer decision 2026-10-12): deleting a Location never deletes the domain that uses it.</summary>
public class LocationDeleteKeepsDomainTests : IDisposable
{
    private readonly KnKDbContext _context;
    private readonly LocationService _service;

    public LocationDeleteKeepsDomainTests()
    {
        _context = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _service = new LocationService(new LocationRepository(_context), null!);
        _context.Locations.Add(new Location { Id = 1, Name = "Square", World = "world" });
        _context.Locations.Add(new Location { Id = 2, Name = "Loose", World = "world" });
        _context.Towns.Add(new Town { Id = 4, Name = "Kardenna", Description = "", WgRegionId = "town_4", LocationId = 1 });
        _context.SaveChanges();
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task ALocationADomainUsesIsRefusedWithTheDomainsName()
    {
        var refused = await Assert.ThrowsAsync<LocationInUseException>(() => _service.DeleteAsync(1));
        Assert.Contains("Town 'Kardenna'", refused.Message);
        Assert.NotNull(await _context.Locations.FindAsync(1));
        Assert.NotNull(await _context.Towns.FindAsync(4));
    }

    [Fact]
    public async Task ALocationNoDomainUsesIsDeleted()
    {
        await _service.DeleteAsync(2);
        Assert.Null(await _context.Locations.FindAsync(2));
    }

    [Fact]
    public void TheDatabaseRefusesTooNotOnlyTheService()
    {
        var foreignKey = _context.Model.FindEntityType(typeof(Domain))!.FindNavigation(nameof(Domain.Location))!.ForeignKey;
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }
}
