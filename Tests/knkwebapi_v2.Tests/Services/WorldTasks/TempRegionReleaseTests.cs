using AutoMapper;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services.WorldTasks;

/// <summary>
/// PandiO/knk-workspace#269: the temp regions of abandoned drafts are released for the game server to remove earlier than
/// its 14-day cleanup: when the draft is deleted, when a region task closes without a result (cancelled, expired, failed)
/// and when the sweep removes the task's session. The API lists a release only once nothing it knows of uses the region,
/// and keeps it when unsure.
/// </summary>
public class TempRegionReleaseTests
{
    private readonly string _dbName = $"temp-region-releases-{Guid.NewGuid()}";
    private DateTime _now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
    private readonly IMapper _mapper = new MapperConfiguration(c =>
    {
        c.AddProfile<WorkflowMappingProfile>();
        c.AddProfile<FormMappingProfile>();
    }).CreateMapper();

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_dbName).Options);

    private TempRegionReleaseService Releases(KnKDbContext db) =>
        new(db, Options.Create(new WorldTaskOptions()), utcNow: () => _now);

    private WorldTaskService Tasks(KnKDbContext db) => new(
        new WorldTaskRepository(db), new WorkflowRepository(db), Mock.Of<IGateDoorService>(), _mapper,
        Options.Create(new WorldTaskOptions()), () => _now, tempRegionReleases: Releases(db));

    private static string Output(int taskId, string world = "knk_hub", string? region = null) =>
        $"{{\"fieldName\":\"WgRegionId\",\"regionId\":\"{region ?? "tempregion_worldtask_" + taskId}\",\"worldName\":\"{world}\"}}";

    private static WorldTask RegionTask(string status, bool claimed = true) => new()
    {
        TaskType = "ReagionCreate", FieldName = "WgRegionId", StepKey = "step-1", Status = status,
        ClaimedAt = claimed ? new DateTime(2026, 11, 9, 10, 0, 0, DateTimeKind.Utc) : null,
        ClaimedByServerId = claimed ? "survival-1" : null,
        CreatedAt = new DateTime(2026, 11, 9, 10, 0, 0, DateTimeKind.Utc)
    };

    /// <summary>Seeds a session (and a draft pointing at it unless <paramref name="draft"/> is false) with the tasks.</summary>
    private async Task<(int SessionId, int? DraftId)> SeedAsync(bool draft, params WorldTask[] tasks)
    {
        await using var db = NewContext();
        if (!await db.Users.AnyAsync(u => u.Id == 5)) db.Users.Add(new User { Id = 5, Username = "owner" });
        if (!await db.FormConfigurations.AnyAsync(c => c.Id == 3))
            db.FormConfigurations.Add(new FormConfiguration { Id = 3, Name = "Town", EntityTypeName = "Town" });
        var session = new WorkflowSession { UserId = 5, EntityTypeName = "Town", CreatedAt = _now.AddHours(-3) };
        db.WorkflowSessions.Add(session);
        await db.SaveChangesAsync();
        FormSubmissionProgress? progress = null;
        if (draft)
        {
            progress = new FormSubmissionProgress { UserId = 5, FormConfigurationId = 3, WorkflowSessionId = session.Id };
            db.FormSubmissionProgresses.Add(progress);
            await db.SaveChangesAsync();
        }
        foreach (var task in tasks)
        {
            task.WorkflowSessionId = session.Id;
            task.FormSubmissionProgressId = progress?.Id;
            db.WorldTasks.Add(task);
        }
        await db.SaveChangesAsync();
        foreach (var task in tasks.Where(t => t.Status == WorldTaskStatuses.Completed && t.OutputJson == "auto"))
        {
            task.OutputJson = Output(task.Id);
        }
        await db.SaveChangesAsync();
        return (session.Id, progress?.Id);
    }

    private async Task<List<TempRegionRelease>> RowsAsync()
    {
        await using var db = NewContext();
        return await db.TempRegionReleases.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
    }

    private async Task<List<TempRegionReleaseDto>> DueAsync(string? serverId = "survival-1", int minutesLater = 10)
    {
        _now = _now.AddMinutes(minutesLater);
        await using var db = NewContext();
        return await Releases(db).ListDueAsync(serverId);
    }

    private async Task DeleteDraftAsync(int draftId)
    {
        await using var db = NewContext();
        var service = new FormSubmissionProgressService(new FormSubmissionProgressRepository(db), _mapper,
            Mock.Of<IFormConfigurationRepository>(), Mock.Of<IDisplayConditionEvaluator>(), Releases(db));
        await service.DeleteAsync(draftId);
    }

    // ===== which tasks release a region =====

    [Theory]
    [InlineData("Completed", true, TempRegionReleaseReasons.DraftDeleted, true)]
    [InlineData("InProgress", true, TempRegionReleaseReasons.DraftDeleted, true)]
    [InlineData("Pending", false, TempRegionReleaseReasons.DraftDeleted, true)]
    [InlineData("Expired", false, TempRegionReleaseReasons.DraftDeleted, false)]
    [InlineData("Cancelled", true, TempRegionReleaseReasons.DraftDeleted, true)]
    [InlineData("Cancelled", true, TempRegionReleaseReasons.TaskCancelled, true)]
    [InlineData("Cancelled", false, TempRegionReleaseReasons.TaskCancelled, false)]
    [InlineData("Expired", false, TempRegionReleaseReasons.TaskExpired, false)]
    [InlineData("Failed", true, TempRegionReleaseReasons.TaskFailed, true)]
    [InlineData("Completed", true, TempRegionReleaseReasons.SessionRemoved, false)]
    [InlineData("Expired", true, TempRegionReleaseReasons.SessionRemoved, true)]
    public void ShouldRelease_RegionTasks_ByStatusAndWhetherAServerHeldThem(string status, bool claimed, string reason, bool expected)
    {
        var task = RegionTask(status, claimed);
        task.Id = 40;
        if (status == "Completed") task.OutputJson = Output(40);

        Assert.Equal(expected, TempRegionReleases.ShouldRelease(task, reason));
    }

    [Fact]
    public void ShouldRelease_NotForOtherTasks_NorForARegionPickedWithSelect()
    {
        var location = new WorldTask { Id = 41, TaskType = "LocationSelection", FieldName = "Location", Status = "Cancelled", ClaimedAt = _now };
        var picked = RegionTask("Completed");
        picked.Id = 42;
        picked.OutputJson = Output(42, region: "town_rivia");
        var released = RegionTask("Expired", claimed: false);
        released.Id = 43;
        released.ReleaseCount = 1; // drawn, then the server crashed: the lease lapsed and it went back to Pending

        Assert.False(TempRegionReleases.ShouldRelease(location, TempRegionReleaseReasons.TaskCancelled));
        Assert.False(TempRegionReleases.ShouldRelease(picked, TempRegionReleaseReasons.DraftDeleted));
        Assert.True(TempRegionReleases.ShouldRelease(released, TempRegionReleaseReasons.TaskExpired));
        Assert.Equal("knk_hub", TempRegionReleases.WorldOf(Output(1)));
        Assert.Null(TempRegionReleases.WorldOf("not json"));
    }

    // ===== hooks =====

    [Fact]
    public async Task DeletingADraft_ReleasesItsDrawnRegions_WithWorldAndServer()
    {
        var drawn = RegionTask("Completed");
        drawn.OutputJson = "auto";
        var location = new WorldTask { TaskType = "LocationSelection", FieldName = "Location", StepKey = "s", Status = "Completed", ClaimedAt = _now };
        var (_, draftId) = await SeedAsync(true, drawn, location);

        await DeleteDraftAsync(draftId!.Value);

        var row = Assert.Single(await RowsAsync());
        Assert.Equal(drawn.Id, row.WorldTaskId);
        Assert.Equal($"tempregion_worldtask_{drawn.Id}", row.RegionId);
        Assert.Equal("knk_hub", row.World);
        Assert.Equal("survival-1", row.ServerId);
        Assert.Equal(TempRegionReleaseReasons.DraftDeleted, row.Reason);
        await using var db = NewContext();
        Assert.False(await db.FormSubmissionProgresses.AnyAsync(p => p.Id == draftId));
    }

    [Fact]
    public async Task CancellingAHeldRegionTask_ReleasesItsRegion_AnUnclaimedOneDoesNot()
    {
        var held = RegionTask("InProgress");
        var waiting = RegionTask("Pending", claimed: false);
        await SeedAsync(true, held, waiting);

        await using (var db = NewContext()) await Tasks(db).CancelAsync(held.Id, null);
        await using (var db = NewContext()) await Tasks(db).CancelAsync(waiting.Id, null);

        var row = Assert.Single(await RowsAsync());
        Assert.Equal((held.Id, TempRegionReleaseReasons.TaskCancelled), (row.WorldTaskId, row.Reason));
    }

    [Fact]
    public async Task FailingARegionTask_ReleasesItsRegion_Once()
    {
        var held = RegionTask("InProgress");
        await SeedAsync(true, held);

        await using (var db = NewContext()) await Tasks(db).FailAsync(held.Id, new FailTaskDto { ErrorMessage = "x", ServerId = "survival-1" });
        await using (var db = NewContext()) await Releases(db).QueueAsync(await db.WorldTasks.SingleAsync(t => t.Id == held.Id), TempRegionReleaseReasons.TaskFailed);

        var row = Assert.Single(await RowsAsync());
        Assert.Equal(TempRegionReleaseReasons.TaskFailed, row.Reason);
    }

    [Fact]
    public async Task UpdateStatusToCancelled_ReleasesAHeldRegionTask()
    {
        var held = RegionTask("InProgress");
        await SeedAsync(true, held);

        await using (var db = NewContext()) await Tasks(db).UpdateStatusAsync(held.Id, "Cancelled");

        Assert.Equal(TempRegionReleaseReasons.TaskCancelled, Assert.Single(await RowsAsync()).Reason);
    }

    [Fact]
    public async Task Sweep_ExpiringARegionTaskAServerOnceHeld_ReleasesIt()
    {
        var lost = RegionTask("Pending", claimed: false);
        lost.ReleaseCount = 1;
        lost.ExpiresAt = _now.AddMinutes(-1);
        var neverClaimed = RegionTask("Pending", claimed: false);
        neverClaimed.ExpiresAt = _now.AddMinutes(-1);
        await SeedAsync(true, lost, neverClaimed);

        await using (var db = NewContext())
        {
            var result = await new WorldTaskSweeper(db, Options.Create(new WorldTaskOptions()), () => _now).RunOnceAsync();
            Assert.Equal(2, result.Expired);
        }

        var row = Assert.Single(await RowsAsync());
        Assert.Equal((lost.Id, TempRegionReleaseReasons.TaskExpired), (row.WorldTaskId, row.Reason));
    }

    [Fact]
    public async Task Sweep_RemovingASession_ReleasesItsHeldRegionTasks_AndTheRowsOutliveTheTasks()
    {
        var cancelled = RegionTask("Cancelled");
        cancelled.CreatedAt = _now.AddDays(-10);
        cancelled.UpdatedAt = _now.AddDays(-10);
        var (sessionId, _) = await SeedAsync(false, cancelled);
        await using (var db = NewContext())
        {
            var session = await db.WorkflowSessions.SingleAsync(s => s.Id == sessionId);
            session.CreatedAt = _now.AddDays(-10);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var result = await new WorldTaskSweeper(db, Options.Create(new WorldTaskOptions()), () => _now).RunOnceAsync();
            Assert.Equal(1, result.SessionsRemoved);
        }

        var row = Assert.Single(await RowsAsync());
        Assert.Equal((cancelled.Id, TempRegionReleaseReasons.SessionRemoved), (row.WorldTaskId, row.Reason));
        await using (var check = NewContext())
        {
            Assert.False(await check.WorldTasks.AnyAsync(t => t.Id == cancelled.Id));
        }
        Assert.Equal(cancelled.Id, Assert.Single(await DueAsync()).WorldTaskId);
    }

    // ===== what the game server is given =====

    [Fact]
    public async Task ListDue_WaitsForTheGracePeriod_ThenListsTheRelease()
    {
        var drawn = RegionTask("Completed");
        drawn.OutputJson = "auto";
        var (_, draftId) = await SeedAsync(true, drawn);
        await DeleteDraftAsync(draftId!.Value);

        Assert.Empty(await DueAsync(minutesLater: 2));
        var due = Assert.Single(await DueAsync(minutesLater: 4));
        Assert.Equal($"tempregion_worldtask_{drawn.Id}", due.RegionId);
        Assert.Equal("knk_hub", due.World);
        Assert.Equal(TempRegionReleaseReasons.DraftDeleted, due.Reason);
    }

    [Fact]
    public async Task ListDue_AnOpenTaskOfADeletedDraft_WaitsUntilItCloses()
    {
        var held = RegionTask("InProgress");
        var (_, draftId) = await SeedAsync(true, held);
        await DeleteDraftAsync(draftId!.Value);

        Assert.Empty(await DueAsync());

        await using (var db = NewContext())
        {
            var task = await db.WorldTasks.SingleAsync(t => t.Id == held.Id);
            task.Status = WorldTaskStatuses.Completed;
            task.OutputJson = Output(held.Id);
            task.UpdatedAt = _now;
            await db.SaveChangesAsync();
        }
        Assert.Empty(await DueAsync(minutesLater: 1));
        Assert.Single(await DueAsync(minutesLater: 10));
    }

    [Fact]
    public async Task ListDue_ACompletedTaskWhoseDraftStillExists_IsNotListed()
    {
        var drawn = RegionTask("Completed");
        drawn.OutputJson = "auto";
        await SeedAsync(true, drawn);
        await using (var db = NewContext())
        {
            // A release whose draft delete then failed.
            await TempRegionReleases.AddIfNewAsync(db, await db.WorldTasks.SingleAsync(t => t.Id == drawn.Id),
                TempRegionReleaseReasons.DraftDeleted, _now);
            await db.SaveChangesAsync();
        }

        Assert.Empty(await DueAsync());
        Assert.Single(await RowsAsync());
    }

    public static IEnumerable<object[]> Users() => new[]
    {
        new object[] { "domain" }, new object[] { "subregion" }, new object[] { "area" }, new object[] { "exclusion" },
        new object[] { "road" }, new object[] { "draft" }
    };

    [Theory]
    [MemberData(nameof(Users))]
    public async Task ListDue_ARegionSomethingNames_IsKept(string user)
    {
        var drawn = RegionTask("Completed");
        drawn.OutputJson = "auto";
        var (_, draftId) = await SeedAsync(true, drawn);
        await DeleteDraftAsync(draftId!.Value);
        string region = $"TempRegion_WorldTask_{drawn.Id}"; // WorldGuard ids are case-insensitive

        await using (var db = NewContext())
        {
            var town = new Town { Name = "Rivia", Description = "", WgRegionId = user == "domain" ? region : "town_rivia" };
            db.Towns.Add(town);
            switch (user)
            {
                case "subregion":
                    db.DomainSubRegions.Add(new DomainSubRegion { Domain = town, WgRegionId = region });
                    break;
                case "area":
                    db.LootboxSpawnAreas.Add(new LootboxSpawnArea { Name = "a", World = "knk_hub", WgRegionId = region });
                    break;
                case "exclusion":
                    db.LootboxSpawnAreas.Add(new LootboxSpawnArea { Name = "a", World = "knk_hub", WgRegionId = "lootbox_a", ExcludedRegionIds = $"spawn,{region}" });
                    break;
                case "road":
                    var tile = new RoadTile { World = "knk_hub" };
                    db.RoadEdges.Add(new RoadEdge
                    {
                        World = "knk_hub", Tile = tile, FromNode = new RoadNode { World = "knk_hub", Tile = tile },
                        ToNode = new RoadNode { World = "knk_hub", X = 5, Tile = tile }, RegionIdsJson = $"[\"{region}\"]"
                    });
                    break;
                case "draft":
                    db.FormSubmissionProgresses.Add(new FormSubmissionProgress
                    {
                        UserId = 5, FormConfigurationId = 3, AllStepsDataJson = $"{{\"Step0\":{{\"WgRegionId\":\"{region}\"}}}}"
                    });
                    break;
            }
            await db.SaveChangesAsync();
        }

        Assert.Empty(await DueAsync());
        Assert.Single(await RowsAsync());
    }

    [Fact]
    public async Task ListDue_ReleasesOfAnotherServerAreLeftOut_UnknownServerGoesToAll()
    {
        var mine = RegionTask("Cancelled");
        var theirs = RegionTask("Cancelled");
        theirs.ClaimedByServerId = "survival-2";
        var unknown = RegionTask("Expired", claimed: false);
        unknown.ReleaseCount = 1;
        await SeedAsync(true, mine, theirs, unknown);
        await using (var db = NewContext())
        {
            foreach (var task in await db.WorldTasks.OrderBy(t => t.Id).ToListAsync())
            {
                await TempRegionReleases.AddIfNewAsync(db, task, TempRegionReleaseReasons.ForClosedStatus(task.Status)!, _now);
            }
            await db.SaveChangesAsync();
        }

        var due = await DueAsync("survival-1");

        Assert.Equal(new[] { mine.Id, unknown.Id }, due.Select(d => d.WorldTaskId).ToArray());
        Assert.Null(due[1].World);
    }

    [Fact]
    public async Task ListDue_DropsReleasesPastTheKeepPeriod()
    {
        var drawn = RegionTask("Cancelled");
        await SeedAsync(true, drawn);
        await using (var db = NewContext())
        {
            db.TempRegionReleases.Add(new TempRegionRelease
            {
                WorldTaskId = drawn.Id, RegionId = TempRegionReleases.RegionIdFor(drawn.Id), Reason = "TaskCancelled",
                RequestedAt = _now.AddDays(-31)
            });
            await db.SaveChangesAsync();
        }

        Assert.Empty(await DueAsync());
        Assert.Empty(await RowsAsync());
    }

    // ===== the game server's answer =====

    [Fact]
    public async Task Settle_DeletesTheRelease_UnknownOutcomeIsRefused_ASecondSettleFindsNothing()
    {
        var drawn = RegionTask("Cancelled");
        await SeedAsync(true, drawn);
        int id;
        await using (var db = NewContext())
        {
            var row = await TempRegionReleases.AddIfNewAsync(db, await db.WorldTasks.SingleAsync(), TempRegionReleaseReasons.TaskCancelled, _now);
            await db.SaveChangesAsync();
            id = row!.Id;
        }

        await using (var db = NewContext())
        {
            await Assert.ThrowsAsync<ArgumentException>(() => Releases(db).SettleAsync(id, new TempRegionReleaseSettleDto { Outcome = "Deleted" }));
            Assert.True(await Releases(db).SettleAsync(id, new TempRegionReleaseSettleDto { Outcome = "removed", ServerId = "survival-1" }));
        }
        await using (var db = NewContext())
        {
            Assert.False(await Releases(db).SettleAsync(id, new TempRegionReleaseSettleDto { Outcome = "Missing" }));
        }
        Assert.Empty(await RowsAsync());
    }
}
