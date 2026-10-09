using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.LocationRetention;

namespace knkwebapi_v2.Tests.Services;

/// <summary>KNG-80: the orphan check, review states, digest, delete re-check and schedule (EF InMemory).</summary>
public class LocationRetentionServiceTests
{
    private readonly string _db = $"location-retention-{Guid.NewGuid()}";
    private readonly Mock<IPlayerNotificationQueue> _notifications = new();
    private readonly LocationRetentionRunGate _gate = new();

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_db, b => b.EnableNullChecks(false)).Options);

    private LocationRetentionService NewService(KnKDbContext context) => new(
        context,
        new ILocationReferenceSource[] { new GameSettingsLocationReferenceSource(context), new FormDraftLocationReferenceSource(context) },
        _notifications.Object,
        _gate,
        NullLogger<LocationRetentionService>.Instance,
        Options.Create(new LocationRetentionOptions { BatchSize = 10, TimeZoneId = "UTC" }));

    private static readonly DateTime LongAgo = DateTime.UtcNow.AddDays(-60);

    private async Task<int> AddLocationAsync(string? name = "Location", DateTime? createdAt = null, bool legacy = false)
    {
        await using var context = NewContext();
        var location = new Location { Name = name, World = "world", X = 10, Y = 64, Z = -20, CreatedAt = legacy ? null : createdAt ?? LongAgo };
        context.Locations.Add(location);
        await context.SaveChangesAsync();
        return location.Id;
    }

    private async Task<LocationRetentionRunDto> RunAsync()
    {
        await using var context = NewContext();
        return (await NewService(context).RunCheckAsync("manual", 1))!;
    }

    private async Task<List<LocationOrphan>> ItemsAsync()
    {
        await using var context = NewContext();
        return await context.LocationOrphans.AsNoTracking().OrderBy(o => o.Id).ToListAsync();
    }

    // ===== Detection =====

    [Fact]
    public async Task Run_FlagsUnnamedUnreferencedOldLocations_Only()
    {
        var orphan = await AddLocationAsync();
        var legacy = await AddLocationAsync(name: null, legacy: true);
        var named = await AddLocationAsync(name: "Kardenna gate");
        var fresh = await AddLocationAsync(createdAt: DateTime.UtcNow.AddDays(-1));
        var usedByTown = await AddLocationAsync();
        await using (var context = NewContext())
        {
            context.Towns.Add(new Town { Name = "Kardenna", LocationId = usedByTown });
            await context.SaveChangesAsync();
        }

        var run = await RunAsync();

        Assert.True(run.Succeeded, run.Error);
        Assert.Equal(new[] { orphan, legacy }.OrderBy(i => i), (await ItemsAsync()).Select(i => i.LocationId).OrderBy(i => i));
        Assert.Equal(2, run.NewOrphans);
        Assert.Equal(3, run.CandidatesScanned); // orphan, legacy, usedByTown (default name, past grace)
        var item = (await ItemsAsync()).Single(i => i.LocationId == orphan);
        Assert.Equal(("world", 10d, 64d, -20d), (item.World, item.X, item.Y, item.Z));
        Assert.Equal(LocationOrphanStatus.Open, item.Status);
    }

    [Fact]
    public async Task Run_SkipsLocationsNamedInGameSettingsJson()
    {
        var joinSpawn = await AddLocationAsync();
        var worldRespawn = await AddLocationAsync();
        var free = await AddLocationAsync();
        await using (var context = NewContext())
        {
            context.GameSettings.Add(new GameSettings
            {
                JoinSpawnReferenceJson = $"{{\"sourceType\":\"Location\",\"sourceId\":{joinSpawn},\"displayLabel\":\"Spawn\"}}",
                WorldSettingsJson = $"[{{\"worldName\":\"world\",\"respawnPolicy\":{{\"mode\":\"Custom\",\"locationReference\":{{\"sourceType\":\"Town\",\"sourceId\":3,\"location\":{{\"locationId\":{worldRespawn}}}}}}}}}]"
            });
            await context.SaveChangesAsync();
        }

        await RunAsync();

        Assert.Equal(new[] { free }, (await ItemsAsync()).Select(i => i.LocationId));
    }

    [Fact]
    public async Task Run_SkipsLocationsNamedInGroupOverrides()
    {
        var groupSpawn = await AddLocationAsync();
        var groupRespawn = await AddLocationAsync();
        var free = await AddLocationAsync();
        await using (var context = NewContext())
        {
            context.GameSettings.Add(new GameSettings
            {
                GroupOverridesJson = $"[{{\"permissionGroupId\":2,\"joinSpawnReference\":{{\"sourceType\":\"Location\",\"sourceId\":{groupSpawn}}}}},"
                    + $"{{\"permissionGroupId\":3,\"respawnPolicy\":{{\"mode\":\"ConfiguredReference\",\"locationReference\":{{\"sourceType\":\"Location\",\"sourceId\":{groupRespawn}}}}}}}]"
            });
            await context.SaveChangesAsync();
        }

        await RunAsync();

        Assert.Equal(new[] { free }, (await ItemsAsync()).Select(i => i.LocationId));
    }

    [Fact]
    public async Task Run_SkipsLocationsAnUnfinishedFormDraftPointsAt()
    {
        var inDraft = await AddLocationAsync();
        var inObject = await AddLocationAsync();
        var inFinishedForm = await AddLocationAsync();
        await using (var context = NewContext())
        {
            context.FormSubmissionProgresses.Add(new FormSubmissionProgress
            {
                Status = "Paused",
                AllStepsDataJson = $"{{\"0\":{{\"Name\":\"Ironhold\",\"LocationId\":{inDraft},\"HubLocation\":{{\"id\":{inObject}}}}}}}"
            });
            context.FormSubmissionProgresses.Add(new FormSubmissionProgress
            {
                Status = "Completed",
                AllStepsDataJson = $"{{\"0\":{{\"LocationId\":{inFinishedForm}}}}}"
            });
            await context.SaveChangesAsync();
        }

        await RunAsync();

        Assert.Equal(new[] { inFinishedForm }, (await ItemsAsync()).Select(i => i.LocationId));
    }

    // ===== Digest and repeated runs =====

    [Fact]
    public async Task Digest_IsQueuedOncePerRun_AndOnlyWhenSomethingIsNew()
    {
        await AddLocationAsync();
        await AddLocationAsync();

        var first = await RunAsync();
        var second = await RunAsync();

        Assert.Equal(2, first.NewOrphans);
        Assert.NotNull(first.DigestQueuedAt);
        Assert.Equal(0, second.NewOrphans);
        Assert.Equal(2, second.AlreadyKnown);
        Assert.Null(second.DigestQueuedAt);
        _notifications.Verify(n => n.EnqueueLocationOrphanDigest(It.Is<LocationOrphanDigestNotificationDto>(d =>
            d.RunId == first.Id && d.NewCount == 2 && d.OpenCount == 2)), Times.Once);
        _notifications.Verify(n => n.EnqueueLocationOrphanDigest(It.IsAny<LocationOrphanDigestNotificationDto>()), Times.Once);
        Assert.Equal(2, (await ItemsAsync()).Count);
    }

    [Fact]
    public async Task OpenItem_ResolvesItself_WhenTheLocationGetsACustomName()
    {
        var id = await AddLocationAsync();
        await RunAsync();
        await using (var context = NewContext())
        {
            (await context.Locations.FindAsync(id))!.Name = "Market square";
            await context.SaveChangesAsync();
        }

        var run = await RunAsync();

        Assert.Equal(1, run.Resolved);
        var item = (await ItemsAsync()).Single();
        Assert.Equal(LocationOrphanStatus.Resolved, item.Status);
        Assert.Contains("custom name", item.ResolvedReason);
    }

    // ===== Keep =====

    [Fact]
    public async Task Kept_IsNotFlaggedAgain_BeforeTheRecheckPeriod()
    {
        await AddLocationAsync();
        await RunAsync();
        var itemId = (await ItemsAsync()).Single().Id;
        await using (var context = NewContext())
        {
            var kept = await NewService(context).KeepAsync(itemId, 7, "  Spawn marker for an event  ");
            Assert.Equal("Kept", kept.Status);
            Assert.Equal("Spawn marker for an event", kept.DecisionNote);
        }

        var run = await RunAsync();

        Assert.Equal(0, run.NewOrphans);
        Assert.Equal(1, run.AlreadyKnown);
        _notifications.Verify(n => n.EnqueueLocationOrphanDigest(It.IsAny<LocationOrphanDigestNotificationDto>()), Times.Once);
    }

    [Fact]
    public async Task Kept_IsFlaggedAgain_AfterTheRecheckPeriod_WithTheEarlierDecisionShown()
    {
        await AddLocationAsync();
        await RunAsync();
        var itemId = (await ItemsAsync()).Single().Id;
        await using (var context = NewContext())
        {
            await NewService(context).KeepAsync(itemId, 7, "Event marker");
            // Kept seven months ago.
            (await context.LocationOrphans.FindAsync(itemId))!.DecidedAt = DateTime.UtcNow.AddMonths(-7);
            await context.SaveChangesAsync();
        }

        var run = await RunAsync();

        Assert.Equal(1, run.NewOrphans);
        Assert.Equal(1, run.Reflagged);
        var items = await ItemsAsync();
        Assert.Equal(2, items.Count);
        Assert.Equal(items[1].Id, items[0].SupersededByItemId);
        Assert.Equal(itemId, items[1].PreviousItemId);
        await using (var context = NewContext())
        {
            var open = (await NewService(context).ListAsync("open", 1, 25)).Items.Single();
            Assert.NotNull(open.PreviousDecision);
            Assert.Equal("Event marker", open.PreviousDecision!.DecisionNote);
            Assert.Equal(7, open.PreviousDecision.DecidedByUserId);
        }
    }

    [Fact]
    public async Task Kept_IsFlaggedAgain_EarlierWhenTheLocationChanged()
    {
        var id = await AddLocationAsync();
        await RunAsync();
        var itemId = (await ItemsAsync()).Single().Id;
        await using (var context = NewContext())
        {
            await NewService(context).KeepAsync(itemId, 7, null);
            (await context.Locations.FindAsync(id))!.X = 500;
            await context.SaveChangesAsync();
        }

        var run = await RunAsync();

        Assert.Equal(1, run.Reflagged);
        Assert.Equal(500, (await ItemsAsync()).Last().X);
    }

    [Fact]
    public async Task Keep_RefusesAnItemThatIsNotOpen()
    {
        await AddLocationAsync();
        await RunAsync();
        var itemId = (await ItemsAsync()).Single().Id;
        await using var context = NewContext();
        var service = NewService(context);
        await service.KeepAsync(itemId, 7, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.KeepAsync(itemId, 7, null));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.KeepAsync(9999, 7, null));
    }

    // ===== Delete =====

    [Fact]
    public async Task Delete_RemovesTheLocation_AndRecordsWhoWhenAndTheSnapshot()
    {
        var id = await AddLocationAsync();
        await RunAsync();
        var itemId = (await ItemsAsync()).Single().Id;

        await using (var context = NewContext())
        {
            var result = await NewService(context).DeleteAsync(itemId, 7, "Leftover from a test");
            Assert.Equal("Deleted", result.Outcome);
            Assert.False(result.Item.LocationExists);
        }

        await using var check = NewContext();
        Assert.Null(await check.Locations.FindAsync(id));
        var item = await check.LocationOrphans.SingleAsync();
        Assert.Equal(LocationOrphanStatus.Deleted, item.Status);
        Assert.Equal(7, item.DecidedByUserId);
        Assert.NotNull(item.DecidedAt);
        Assert.Equal("Leftover from a test", item.DecisionNote);
        Assert.Equal(("world", 10d, 64d, -20d), (item.World, item.X, item.Y, item.Z));
    }

    [Fact]
    public async Task Delete_ReChecksAndRefuses_WhenARelationAppearedSinceFlagging()
    {
        var id = await AddLocationAsync();
        await RunAsync();
        var itemId = (await ItemsAsync()).Single().Id;
        await using (var context = NewContext())
        {
            context.Towns.Add(new Town { Name = "Late town", LocationId = id });
            await context.SaveChangesAsync();
        }

        await using (var context = NewContext())
        {
            var result = await NewService(context).DeleteAsync(itemId, 7, null);
            Assert.Equal("NoLongerOrphan", result.Outcome);
            Assert.Equal("Resolved", result.Item.Status);
        }

        await using var check = NewContext();
        Assert.NotNull(await check.Locations.FindAsync(id));
    }

    [Fact]
    public async Task Delete_ReChecksAndRefuses_WhenTheLocationGotACustomNameOrAJsonReference()
    {
        var renamed = await AddLocationAsync();
        var referenced = await AddLocationAsync();
        await RunAsync();
        var items = await ItemsAsync();
        await using (var context = NewContext())
        {
            (await context.Locations.FindAsync(renamed))!.Name = "Arena";
            context.GameSettings.Add(new GameSettings { JoinSpawnReferenceJson = $"{{\"sourceType\":\"Location\",\"sourceId\":{referenced}}}" });
            await context.SaveChangesAsync();
        }

        await using (var context = NewContext())
        {
            var service = NewService(context);
            Assert.Equal("NoLongerOrphan", (await service.DeleteAsync(items.Single(i => i.LocationId == renamed).Id, 7, null)).Outcome);
            var json = await service.DeleteAsync(items.Single(i => i.LocationId == referenced).Id, 7, null);
            Assert.Equal("NoLongerOrphan", json.Outcome);
            Assert.Contains("Game settings", json.Message);
        }

        await using var check = NewContext();
        Assert.Equal(2, await check.Locations.CountAsync());
    }

    // ===== Run gate, schedule, settings =====

    [Fact]
    public async Task Run_ReturnsNull_WhileAnotherRunIsGoingOn()
    {
        Assert.True(_gate.TryBegin());
        try
        {
            await using var context = NewContext();
            Assert.Null(await NewService(context).RunCheckAsync("manual", 1));
        }
        finally
        {
            _gate.End();
        }
    }

    [Theory]
    // Thursday 2026-10-08 12:00 UTC → last Sunday 2026-10-04 04:00.
    [InlineData("2026-10-08T12:00:00Z", "2026-10-04T04:00:00Z")]
    // Sunday 03:59 → the Sunday before; Sunday 04:00 → today.
    [InlineData("2026-10-11T03:59:00Z", "2026-10-04T04:00:00Z")]
    [InlineData("2026-10-11T04:00:00Z", "2026-10-11T04:00:00Z")]
    public void LatestSlot_IsTheLastSundayAt0400(string now, string expected)
    {
        var slot = LocationRetentionService.LatestSlotUtc(new LocationRetentionSettings(), DateTime.Parse(now).ToUniversalTime(), TimeZoneInfo.Utc);
        Assert.Equal(DateTime.Parse(expected).ToUniversalTime(), slot);
    }

    [Fact]
    public async Task ScheduledSlot_IsDueOnce()
    {
        var now = DateTime.Parse("2026-10-08T12:00:00Z").ToUniversalTime();
        await using var context = NewContext();
        var service = NewService(context);

        var slot = await service.DueScheduledSlotAsync(now);
        Assert.Equal(DateTime.Parse("2026-10-04T04:00:00Z").ToUniversalTime(), slot);
        await service.RunCheckAsync("scheduled", null, slot);

        Assert.Null(await service.DueScheduledSlotAsync(now));
        Assert.Equal(DateTime.Parse("2026-10-11T04:00:00Z").ToUniversalTime(), await service.DueScheduledSlotAsync(now.AddDays(3)));
    }

    [Fact]
    public async Task ScheduledSlot_IsNeverDue_WhenTheScheduleIsOff()
    {
        await using var context = NewContext();
        var service = NewService(context);
        await service.UpdateSettingsAsync(new LocationRetentionSettingsDto { ScheduleEnabled = false }, 7);

        Assert.Null(await service.DueScheduledSlotAsync(DateTime.UtcNow));
    }

    [Fact]
    public async Task Settings_DefaultToTheDevelopersDecisions_AndValidate()
    {
        await using var context = NewContext();
        var service = NewService(context);

        var status = await service.GetStatusAsync();
        Assert.Equal(("Weekly", "Sunday", "04:00", 7, 6), (status.Settings.Frequency, status.Settings.RunDayOfWeek, status.Settings.RunAtTime,
            status.Settings.GracePeriodDays, status.Settings.KeptRecheckMonths));
        Assert.Contains("Domain.LocationId", status.Relations);

        var updated = await service.UpdateSettingsAsync(new LocationRetentionSettingsDto
        {
            Frequency = "daily", RunDayOfWeek = "Monday", RunAtTime = "3:30", GracePeriodDays = 14, KeptRecheckMonths = 12
        }, 7);
        Assert.Equal(("Daily", "03:30", 14, 12), (updated.Frequency, updated.RunAtTime, updated.GracePeriodDays, updated.KeptRecheckMonths));

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateSettingsAsync(new LocationRetentionSettingsDto { RunAtTime = "25:00" }, 7));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateSettingsAsync(new LocationRetentionSettingsDto { GracePeriodDays = -1 }, 7));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateSettingsAsync(new LocationRetentionSettingsDto { Frequency = "Hourly" }, 7));
    }

    [Fact]
    public async Task GracePeriodSetting_IsUsedByTheRun()
    {
        await AddLocationAsync(createdAt: DateTime.UtcNow.AddDays(-10));
        await using (var context = NewContext())
        {
            await NewService(context).UpdateSettingsAsync(new LocationRetentionSettingsDto { GracePeriodDays = 14 }, 7);
        }

        Assert.Equal(0, (await RunAsync()).OrphansFound);
    }

    [Fact]
    public async Task TeleportTarget_ReturnsAnyLocation()
    {
        var id = await AddLocationAsync(name: "Arena");
        await using var context = NewContext();
        var service = NewService(context);

        var target = await service.GetTeleportTargetAsync(id);
        Assert.Equal(("world", 10d, 64d, -20d), (target!.World, target.X, target.Y, target.Z));
        Assert.Null(await service.GetTeleportTargetAsync(9999));
    }

    [Fact]
    public void NewLocations_GetACreatedAt()
    {
        Assert.NotNull(new Location().CreatedAt);
    }
}
