using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.LocationRetention;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// KNG-80 on a real MySQL: the metadata-built anti-join predicate translates to SQL over every FK
/// to Location (including the guard-spawn join table), the keyset batches work, and a delete
/// locks the Location row, re-checks and deletes in one transaction.
/// </summary>
[Trait("Category", "requires-mysql")]
public class LocationRetentionMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;

    public LocationRetentionMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private static LocationRetentionService NewService(KnKDbContext ctx) => new(
        ctx,
        new ILocationReferenceSource[] { new GameSettingsLocationReferenceSource(ctx), new FormDraftLocationReferenceSource(ctx) },
        new Mock<IPlayerNotificationQueue>().Object,
        new LocationRetentionRunGate(),
        NullLogger<LocationRetentionService>.Instance,
        Options.Create(new LocationRetentionOptions { BatchSize = 10 }));

    [MySqlFact]
    public async Task Run_FlagsOnlyUnreferencedDefaultNamedLocations_AndDeleteReChecksUnderALock()
    {
        var old = DateTime.UtcNow.AddDays(-30);
        var tag = Guid.NewGuid().ToString("N")[..8];
        int orphanId, townLocationId, namedId;
        var batchOfOrphans = new List<int>();
        await using (var ctx = _db.NewContext())
        {
            var orphan = new Location { Name = "Location", World = "world", CreatedAt = old };
            var townLocation = new Location { Name = "Location", World = "world", CreatedAt = old };
            var named = new Location { Name = "Gatehouse " + tag, World = "world", CreatedAt = old };
            ctx.Locations.AddRange(orphan, townLocation, named);
            // More than one batch of 10.
            var extra = Enumerable.Range(0, 12).Select(_ => new Location { Name = null, World = "world", CreatedAt = null }).ToList();
            ctx.Locations.AddRange(extra);
            await ctx.SaveChangesAsync();
            ctx.Towns.Add(new Town { Name = "Town " + tag, Description = "", WgRegionId = "town_" + tag, LocationId = townLocation.Id });
            await ctx.SaveChangesAsync();
            (orphanId, townLocationId, namedId) = (orphan.Id, townLocation.Id, named.Id);
            batchOfOrphans.AddRange(extra.Select(e => e.Id));
        }

        await using (var ctx = _db.NewContext())
        {
            var run = await NewService(ctx).RunCheckAsync("manual", null);
            Assert.True(run!.Succeeded, run.Error);
        }

        await using (var ctx = _db.NewContext())
        {
            var flagged = await ctx.LocationOrphans.AsNoTracking().Select(o => o.LocationId).ToListAsync();
            Assert.Contains(orphanId, flagged);
            Assert.All(batchOfOrphans, id => Assert.Contains(id, flagged));
            Assert.DoesNotContain(townLocationId, flagged);
            Assert.DoesNotContain(namedId, flagged);
        }

        await using (var ctx = _db.NewContext())
        {
            var item = await ctx.LocationOrphans.AsNoTracking().SingleAsync(o => o.LocationId == orphanId);
            var result = await NewService(ctx).DeleteAsync(item.Id, 1, "MySQL test");
            Assert.Equal("Deleted", result.Outcome);
        }

        await using (var ctx = _db.NewContext())
        {
            Assert.False(await ctx.Locations.AnyAsync(l => l.Id == orphanId));
            Assert.Equal(LocationOrphanStatus.Deleted, (await ctx.LocationOrphans.SingleAsync(o => o.LocationId == orphanId)).Status);
            Assert.True(await ctx.Locations.AnyAsync(l => l.Id == townLocationId));
        }
    }

    [MySqlFact]
    public async Task Delete_IsRefused_WhenATownStartedUsingTheLocation()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        int locationId;
        await using (var ctx = _db.NewContext())
        {
            var location = new Location { Name = "Location", World = "world", CreatedAt = DateTime.UtcNow.AddDays(-30) };
            ctx.Locations.Add(location);
            await ctx.SaveChangesAsync();
            locationId = location.Id;
        }
        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).RunCheckAsync("manual", null);
        }
        await using (var ctx = _db.NewContext())
        {
            ctx.Towns.Add(new Town { Name = "Late " + tag, Description = "", WgRegionId = "late_" + tag, LocationId = locationId });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
        {
            var item = await ctx.LocationOrphans.AsNoTracking().SingleAsync(o => o.LocationId == locationId);
            Assert.Equal("NoLongerOrphan", (await NewService(ctx).DeleteAsync(item.Id, 1, null)).Outcome);
        }

        await using (var ctx = _db.NewContext())
        {
            // Domain.LocationId cascades on delete, so a wrong delete here would also have taken the town.
            Assert.True(await ctx.Locations.AnyAsync(l => l.Id == locationId));
            Assert.True(await ctx.Towns.AnyAsync(t => t.LocationId == locationId));
        }
    }
}
