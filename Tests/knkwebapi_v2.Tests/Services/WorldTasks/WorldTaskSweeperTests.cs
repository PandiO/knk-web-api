using knkwebapi_v2.Configuration;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace knkwebapi_v2.Tests.Services.WorldTasks;

/// <summary>
/// PandiO/knk-workspace#180 (decisions Q10, Q18): Pending expires after 30 min, a claim without a heartbeat for 3 min is
/// released, and a session with no draft and no activity for 7 days is removed with its tasks, unless something can
/// still read it.
/// </summary>
public class WorldTaskSweeperTests
{
    private readonly string _dbName = $"world-task-sweep-{Guid.NewGuid()}";
    private readonly DateTime _now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_dbName).Options);

    private async Task<WorldTaskSweepResult> SweepAsync()
    {
        await using var db = NewContext();
        return await new WorldTaskSweeper(db, Options.Create(new WorldTaskOptions()), () => _now).RunOnceAsync();
    }

    private async Task<WorkflowSession> SeedSessionAsync(DateTime createdAt, string? entityType = "Town", params WorldTask[] tasks)
    {
        await using var db = NewContext();
        if (!await db.Users.AnyAsync(u => u.Id == 5)) db.Users.Add(new User { Id = 5, Username = "owner" });
        var session = new WorkflowSession { UserId = 5, CreatedAt = createdAt, EntityTypeName = entityType };
        foreach (var task in tasks)
        {
            task.TaskType ??= "LocationSelection";
            session.WorldTasks.Add(task);
        }
        db.WorkflowSessions.Add(session);
        await db.SaveChangesAsync();
        return session;
    }

    private async Task<WorldTask> ReadTaskAsync(int id)
    {
        await using var db = NewContext();
        return await db.WorldTasks.AsNoTracking().SingleAsync(t => t.Id == id);
    }

    [Fact]
    public async Task ExpiresPendingTasksPastTheirExpiry_AndOldRowsByTheirAge()
    {
        var due = new WorldTask { Status = "Pending", CreatedAt = _now.AddMinutes(-40), ExpiresAt = _now.AddSeconds(-1) };
        var notYet = new WorldTask { Status = "Pending", CreatedAt = _now.AddMinutes(-40), ExpiresAt = _now.AddMinutes(1) };
        var legacyOld = new WorldTask { Status = "Pending", CreatedAt = _now.AddMinutes(-31) };
        var legacyNew = new WorldTask { Status = "Pending", CreatedAt = _now.AddMinutes(-10) };
        await SeedSessionAsync(_now, "Town", due, notYet, legacyOld, legacyNew);

        var result = await SweepAsync();

        Assert.Equal(2, result.Expired);
        Assert.Equal("Expired", (await ReadTaskAsync(due.Id)).Status);
        Assert.Equal("Expired", (await ReadTaskAsync(legacyOld.Id)).Status);
        Assert.Equal("Pending", (await ReadTaskAsync(notYet.Id)).Status);
        Assert.Equal("Pending", (await ReadTaskAsync(legacyNew.Id)).Status);
        Assert.StartsWith("Expired", (await ReadTaskAsync(due.Id)).ErrorMessage);
    }

    [Fact]
    public async Task ReleasesClaimsWhoseLeaseLapsed_KeepsTheCodeAndGivesAFreshExpiry()
    {
        var lapsed = new WorldTask
        {
            Status = "InProgress", LinkCode = "ABC123", CreatedAt = _now.AddMinutes(-10), ClaimedAt = _now.AddMinutes(-5),
            LeaseUntil = _now.AddSeconds(-1), ClaimedByServerId = "s1", ClaimedByUuid = "u", ClaimedByMinecraftUsername = "Steve"
        };
        var alive = new WorldTask { Status = "InProgress", CreatedAt = _now.AddMinutes(-10), LeaseUntil = _now.AddMinutes(2) };
        var legacy = new WorldTask { Status = "InProgress", CreatedAt = _now.AddDays(-3), ClaimedAt = _now.AddDays(-3) };
        await SeedSessionAsync(_now, "Town", lapsed, alive, legacy);

        var result = await SweepAsync();

        Assert.Equal(2, result.Released);
        var released = await ReadTaskAsync(lapsed.Id);
        Assert.Equal("Pending", released.Status);
        Assert.Equal("ABC123", released.LinkCode);
        Assert.Null(released.ClaimedByServerId);
        Assert.Null(released.ClaimedByUuid);
        Assert.Equal(1, released.ReleaseCount);
        Assert.Equal(_now.AddMinutes(30), released.ExpiresAt);
        Assert.Equal("InProgress", (await ReadTaskAsync(alive.Id)).Status);
        Assert.Equal("Pending", (await ReadTaskAsync(legacy.Id)).Status);
    }

    [Fact]
    public async Task RemovesSessionsWithoutDraftOrActivity_AndKeepsTheOnesSomethingStillUses()
    {
        var old = _now.AddDays(-8);
        var abandoned = await SeedSessionAsync(old, "Town",
            new WorldTask { Status = "Expired", CreatedAt = old, UpdatedAt = old },
            new WorldTask { Status = "Cancelled", CreatedAt = old });
        var empty = await SeedSessionAsync(old);
        var recent = await SeedSessionAsync(_now.AddDays(-2));
        var withCompletedTask = await SeedSessionAsync(old, "Town", new WorldTask { Status = "Completed", CreatedAt = old, UpdatedAt = old });
        var catalogScan = await SeedSessionAsync(old, WorldTaskSweeper.CatalogScanSessionType);
        var recentTask = await SeedSessionAsync(old, "Town", new WorldTask { Status = "Cancelled", CreatedAt = old, UpdatedAt = _now.AddDays(-1) });
        var withDraft = await SeedSessionAsync(old);
        await using (var db = NewContext())
        {
            db.FormConfigurations.Add(new FormConfiguration { Id = 3, Name = "Town", EntityTypeName = "Town" });
            db.FormSubmissionProgresses.Add(new FormSubmissionProgress { UserId = 5, FormConfigurationId = 3, WorkflowSessionId = withDraft.Id });
            await db.SaveChangesAsync();
        }

        var result = await SweepAsync();

        Assert.Equal(2, result.SessionsRemoved);
        await using var check = NewContext();
        var left = await check.WorkflowSessions.Select(s => s.Id).ToListAsync();
        Assert.DoesNotContain(abandoned.Id, left);
        Assert.DoesNotContain(empty.Id, left);
        Assert.Contains(recent.Id, left);
        Assert.Contains(withCompletedTask.Id, left);
        Assert.Contains(catalogScan.Id, left);
        Assert.Contains(recentTask.Id, left);
        Assert.Contains(withDraft.Id, left);
        Assert.False(await check.WorldTasks.AnyAsync(t => t.WorkflowSessionId == abandoned.Id));
    }

    [Fact]
    public async Task NothingToDo_ReportsNothing()
    {
        await SeedSessionAsync(_now, "Town", new WorldTask { Status = "Pending", CreatedAt = _now, ExpiresAt = _now.AddMinutes(30) });

        var result = await SweepAsync();

        Assert.False(result.Any);
    }
}
