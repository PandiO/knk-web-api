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
/// PandiO/knk-workspace#180 (DESIGN §8.2, §8.7): the world-task state machine on the real repositories (EF InMemory):
/// creator from the caller, expiry, task codes, claim modes and races, complete/fail guards, cancel/release/heartbeat,
/// tasks anchored to the draft.
/// </summary>
public class WorldTaskStateMachineTests
{
    private readonly string _dbName = $"world-tasks-{Guid.NewGuid()}";
    private DateTime _now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    private readonly IMapper _mapper = new MapperConfiguration(c => c.AddProfile<WorkflowMappingProfile>()).CreateMapper();
    private const string Uuid = "0f6c2a1e-7d0b-4c52-9a51-3c1e6f0a9b11";
    private const string OtherUuid = "aa6c2a1e-7d0b-4c52-9a51-3c1e6f0a9b22";

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_dbName).Options);

    private WorldTaskService Service(KnKDbContext db) => new(
        new WorldTaskRepository(db), new WorkflowRepository(db), Mock.Of<IGateDoorService>(), _mapper,
        Options.Create(new WorldTaskOptions()), () => _now);

    private async Task<int> SeedSessionAsync(int ownerId = 5, string? entityType = "Town")
    {
        await using var db = NewContext();
        if (!await db.Users.AnyAsync(u => u.Id == ownerId))
        {
            db.Users.Add(new User { Id = ownerId, Username = $"owner{ownerId}" });
        }
        if (!await db.Users.AnyAsync(u => u.Id == 9))
        {
            db.Users.Add(new User { Id = 9, Username = "Steve", Uuid = Uuid });
        }
        var session = new WorkflowSession { UserId = ownerId, EntityTypeName = entityType, CreatedAt = _now };
        db.WorkflowSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    private async Task<int> SeedDraftAsync(int? sessionId = null)
    {
        await using var db = NewContext();
        if (!await db.FormConfigurations.AnyAsync(c => c.Id == 3))
        {
            db.FormConfigurations.Add(new FormConfiguration { Id = 3, Name = "Town", EntityTypeName = "Town" });
        }
        var draft = new FormSubmissionProgress { UserId = 5, FormConfigurationId = 3, WorkflowSessionId = sessionId };
        db.FormSubmissionProgresses.Add(draft);
        await db.SaveChangesAsync();
        return draft.Id;
    }

    private static WorldTaskCreateDto Create(int sessionId, string field = "Location", string type = "LocationSelection",
        int? draftId = null) => new()
    {
        WorkflowSessionId = sessionId, StepNumber = 0, StepKey = "step-1", FieldName = field, TaskType = type,
        InputJson = "{}", FormSubmissionProgressId = draftId
    };

    private async Task<WorldTaskReadDto> CreateTaskAsync(int sessionId, string field = "Location", string type = "LocationSelection",
        int? draftId = null, int? caller = 5)
    {
        await using var db = NewContext();
        return await Service(db).CreateAsync(Create(sessionId, field, type, draftId), caller);
    }

    private async Task<WorldTask> ReadAsync(int id)
    {
        await using var db = NewContext();
        return await db.WorldTasks.AsNoTracking().SingleAsync(t => t.Id == id);
    }

    private async Task<WorldTaskReadDto> ClaimAsync(int id, ClaimTaskDto dto)
    {
        await using var db = NewContext();
        return await Service(db).ClaimAsync(id, dto);
    }

    private static ClaimTaskDto ByCode(string? code, string uuid = Uuid, string server = "survival-1") => new()
    {
        TaskCode = code, ClaimedByUuid = uuid, ClaimedByServerId = server, ClaimedByMinecraftUsername = "Steve"
    };

    private async Task<WorldTaskReadDto> ClaimedTaskAsync(string server = "survival-1")
    {
        var sessionId = await SeedSessionAsync();
        var task = await CreateTaskAsync(sessionId);
        return await ClaimAsync(task.Id, ByCode(task.LinkCode, server: server));
    }

    // ---- create ----

    [Fact]
    public async Task Create_TakesTheCreatorFromTheCaller_SetsExpiryAndACode()
    {
        var sessionId = await SeedSessionAsync(ownerId: 5);

        var task = await CreateTaskAsync(sessionId, caller: 7);

        var stored = await ReadAsync(task.Id);
        Assert.Equal(7, stored.CreatedByUserId);
        Assert.Equal(WorldTaskStatuses.Pending, stored.Status);
        Assert.Equal(_now.AddMinutes(30), stored.ExpiresAt);
        Assert.Matches("^[A-Z0-9]{6}$", stored.LinkCode!);
        Assert.Equal("Town", task.EntityType);
        Assert.Equal(WorldTaskOfferStates.None, stored.OfferState);
    }

    [Fact]
    public async Task Create_ByTheGameServer_MakesTheSessionOwnerTheCreator()
    {
        var sessionId = await SeedSessionAsync(ownerId: 5);

        var task = await CreateTaskAsync(sessionId, caller: null);

        Assert.Equal(5, (await ReadAsync(task.Id)).CreatedByUserId);
    }

    [Fact]
    public async Task Create_WithAnUnknownSession_IsRefused()
    {
        await using var db = NewContext();
        await Assert.ThrowsAsync<ArgumentException>(() => Service(db).CreateAsync(Create(404), 5));
    }

    [Fact]
    public async Task Create_ForADraft_LinksTheDraftToTheSession_AndReplacesTheFieldsOpenTask()
    {
        var sessionId = await SeedSessionAsync();
        var draftId = await SeedDraftAsync();

        var first = await CreateTaskAsync(sessionId, draftId: draftId);
        var other = await CreateTaskAsync(sessionId, field: "WgRegionId", type: "WgRegionId", draftId: draftId);
        var second = await CreateTaskAsync(sessionId, draftId: draftId);

        Assert.Equal(WorldTaskStatuses.Cancelled, (await ReadAsync(first.Id)).Status);
        Assert.Contains($"#{second.Id}", (await ReadAsync(first.Id)).ErrorMessage);
        Assert.Equal(WorldTaskStatuses.Pending, (await ReadAsync(other.Id)).Status);
        Assert.Equal(draftId, (await ReadAsync(second.Id)).FormSubmissionProgressId);
        await using var db = NewContext();
        Assert.Equal(sessionId, (await db.FormSubmissionProgresses.SingleAsync(p => p.Id == draftId)).WorkflowSessionId);
    }

    [Fact]
    public async Task Create_WithoutADraft_ReplacesTheOpenTaskOfTheSameSessionStepAndField()
    {
        var sessionId = await SeedSessionAsync();
        var first = await CreateTaskAsync(sessionId);
        await CreateTaskAsync(sessionId);

        Assert.Equal(WorldTaskStatuses.Cancelled, (await ReadAsync(first.Id)).Status);
    }

    [Fact]
    public async Task Create_RetriesATaskCodeThatAnOpenTaskHolds()
    {
        var repo = new Mock<knkwebapi_v2.Repositories.Interfaces.IWorldTaskRepository>();
        var workflow = new Mock<knkwebapi_v2.Repositories.Interfaces.IWorkflowRepository>();
        workflow.Setup(w => w.GetSessionByIdAsync(1)).ReturnsAsync(new WorkflowSession { Id = 1, UserId = 5 });
        repo.SetupSequence(r => r.IsLinkCodeInUseAsync(It.IsAny<string>())).ReturnsAsync(true).ReturnsAsync(true).ReturnsAsync(false);
        repo.Setup(r => r.ListOpenForFieldAsync(1, null, "step-1", "Location")).ReturnsAsync(new List<WorldTask>());
        var service = new WorldTaskService(repo.Object, workflow.Object, Mock.Of<IGateDoorService>(), _mapper);

        await service.CreateAsync(Create(1), 5);

        repo.Verify(r => r.IsLinkCodeInUseAsync(It.IsAny<string>()), Times.Exactly(3));
    }

    [Fact]
    public void NewTaskCode_IsSixCharactersFromTheAlphabet()
    {
        for (var i = 0; i < 200; i++)
        {
            Assert.Matches("^[A-Z0-9]{6}$", WorldTaskService.NewTaskCode());
        }
        Assert.Equal("AB12CD", WorldTaskService.NormalizeTaskCode(" ab-12 cd "));
        Assert.Null(WorldTaskService.NormalizeTaskCode(" - "));
    }

    // ---- lookup by code ----

    [Fact]
    public async Task GetByLinkCode_FindsOpenTasksOnly_WhateverTheCaseOrDashes()
    {
        var sessionId = await SeedSessionAsync();
        var task = await CreateTaskAsync(sessionId);
        var code = task.LinkCode!;

        await using (var db = NewContext())
        {
            Assert.Equal(task.Id, (await Service(db).GetByLinkCodeAsync(code.ToLowerInvariant().Insert(3, "-")))!.Id);
            await Service(db).CancelAsync(task.Id, null);
        }
        await using (var db = NewContext())
        {
            Assert.Null(await Service(db).GetByLinkCodeAsync(code));
        }
    }

    // ---- claim ----

    [Fact]
    public async Task Claim_ByCode_MovesToInProgressWithALease_AndRecordsTheClaimer()
    {
        var sessionId = await SeedSessionAsync();
        var task = await CreateTaskAsync(sessionId);

        var claimed = await ClaimAsync(task.Id, ByCode(task.LinkCode!.ToLowerInvariant()));

        var stored = await ReadAsync(task.Id);
        Assert.Equal(WorldTaskStatuses.InProgress, claimed.Status);
        Assert.Equal("survival-1", stored.ClaimedByServerId);
        Assert.Equal("Steve", stored.ClaimedByMinecraftUsername);
        Assert.Equal(Uuid, stored.ClaimedByUuid);
        Assert.Equal(9, stored.ClaimedByUserId);
        Assert.Equal(_now.AddMinutes(3), stored.LeaseUntil);
    }

    [Fact]
    public async Task Claim_WithTheWrongCodeOrNoCode_IsRefused()
    {
        var sessionId = await SeedSessionAsync();
        var task = await CreateTaskAsync(sessionId);

        await Assert.ThrowsAsync<ArgumentException>(() => ClaimAsync(task.Id, ByCode("ZZZZZZ")));
        await Assert.ThrowsAsync<ArgumentException>(() => ClaimAsync(task.Id, new ClaimTaskDto
        {
            ClaimMode = WorldTaskClaimModes.Code, ClaimedByUuid = Uuid, ClaimedByServerId = "s"
        }));
        Assert.Equal(WorldTaskStatuses.Pending, (await ReadAsync(task.Id)).Status);
    }

    [Fact]
    public async Task Claim_ByAnotherPlayer_OfAClaimedTask_Is409_AndTheSamePlayerRepeatingIsIdempotent()
    {
        var sessionId = await SeedSessionAsync();
        var task = await CreateTaskAsync(sessionId);
        await ClaimAsync(task.Id, ByCode(task.LinkCode));

        var ex = await Assert.ThrowsAsync<WorldTaskConflictException>(() => ClaimAsync(task.Id, ByCode(task.LinkCode, OtherUuid)));
        Assert.Equal(WorldTaskStatuses.InProgress, ex.CurrentStatus);

        _now = _now.AddMinutes(1);
        var again = await ClaimAsync(task.Id, ByCode(task.LinkCode));
        Assert.Equal(WorldTaskStatuses.InProgress, again.Status);
        Assert.Equal(_now.AddMinutes(3), (await ReadAsync(task.Id)).LeaseUntil);
        Assert.Equal(Uuid, (await ReadAsync(task.Id)).ClaimedByUuid);
    }

    [Fact]
    public async Task Claim_RaceBetweenTwoClaimers_OnlyOneWins()
    {
        var sessionId = await SeedSessionAsync();
        var task = await CreateTaskAsync(sessionId);
        await using var first = NewContext();
        await using var second = NewContext();
        var firstService = Service(first);
        var secondService = Service(second);
        // Both read the Pending row before either writes.
        await first.WorldTasks.SingleAsync(t => t.Id == task.Id);
        await second.WorldTasks.SingleAsync(t => t.Id == task.Id);

        await firstService.ClaimAsync(task.Id, ByCode(task.LinkCode));
        await Assert.ThrowsAsync<WorldTaskConflictException>(() => secondService.ClaimAsync(task.Id, ByCode(task.LinkCode, OtherUuid)));

        Assert.Equal(Uuid, (await ReadAsync(task.Id)).ClaimedByUuid);
    }

    [Fact]
    public async Task Claim_ByIdAndHeadless_FollowTheirRules()
    {
        var sessionId = await SeedSessionAsync();
        var player = await CreateTaskAsync(sessionId);
        var scan = await CreateTaskAsync(sessionId, field: "BlockSnapshots", type: WorldTaskTypes.GateBlockScan);

        await Assert.ThrowsAsync<ArgumentException>(() => ClaimAsync(player.Id, new ClaimTaskDto { ClaimedByServerId = "s" }));
        await Assert.ThrowsAsync<ArgumentException>(() => ClaimAsync(player.Id, new ClaimTaskDto { ClaimMode = "id", ClaimedByServerId = "s" }));
        var byId = await ClaimAsync(player.Id, new ClaimTaskDto { ClaimMode = "id", ClaimedByUuid = Uuid, ClaimedByServerId = "s" });
        var headless = await ClaimAsync(scan.Id, new ClaimTaskDto { ClaimMode = "headless", ClaimedByServerId = "s" });

        Assert.Equal(WorldTaskStatuses.InProgress, byId.Status);
        Assert.Equal(WorldTaskStatuses.InProgress, headless.Status);
        // A second server can't take the scan over.
        await Assert.ThrowsAsync<WorldTaskConflictException>(() => ClaimAsync(scan.Id, new ClaimTaskDto { ClaimMode = "headless", ClaimedByServerId = "other" }));
    }

    [Fact]
    public async Task Claim_WithThePluginsOldFieldNames_StillRecordsServerAndPlayer()
    {
        var sessionId = await SeedSessionAsync();
        var task = await CreateTaskAsync(sessionId);

        await ClaimAsync(task.Id, new ClaimTaskDto { LinkCode = task.LinkCode, ServerId = "localhost", MinecraftUsername = "Alex", ClaimedByUuid = Uuid });

        var stored = await ReadAsync(task.Id);
        Assert.Equal("localhost", stored.ClaimedByServerId);
        Assert.Equal("Alex", stored.ClaimedByMinecraftUsername);
    }

    [Fact]
    public async Task Claim_OfAnExpiredOrClosedTask_Is409()
    {
        var sessionId = await SeedSessionAsync();
        var task = await CreateTaskAsync(sessionId);
        _now = _now.AddMinutes(31);

        var ex = await Assert.ThrowsAsync<WorldTaskConflictException>(() => ClaimAsync(task.Id, ByCode(task.LinkCode)));

        Assert.Equal(WorldTaskStatuses.Expired, ex.CurrentStatus);
        Assert.Equal(WorldTaskStatuses.Expired, (await ReadAsync(task.Id)).Status);
        await Assert.ThrowsAsync<WorldTaskConflictException>(() => ClaimAsync(task.Id, ByCode(task.LinkCode)));
    }

    [Fact]
    public async Task Claim_ByOffer_NeedsTheTargetPlayer()
    {
        var sessionId = await SeedSessionAsync();
        var task = await CreateTaskAsync(sessionId);

        await Assert.ThrowsAsync<WorldTaskConflictException>(() =>
            ClaimAsync(task.Id, new ClaimTaskDto { ClaimMode = "offer", ClaimedByUuid = Uuid, ClaimedByServerId = "s" }));

        await using (var db = NewContext())
        {
            var row = await db.WorldTasks.SingleAsync(t => t.Id == task.Id);
            row.TargetUserId = 9;
            row.OfferState = WorldTaskOfferStates.Offered;
            await db.SaveChangesAsync();
        }
        await ClaimAsync(task.Id, new ClaimTaskDto { ClaimMode = "offer", ClaimedByUuid = Uuid, ClaimedByServerId = "s" });
        Assert.Equal(WorldTaskOfferStates.Accepted, (await ReadAsync(task.Id)).OfferState);
    }

    // ---- complete / fail ----

    [Fact]
    public async Task Complete_NeedsAClaimByThisServer()
    {
        var sessionId = await SeedSessionAsync();
        var pending = await CreateTaskAsync(sessionId);
        await using (var db = NewContext())
        {
            await Assert.ThrowsAsync<WorldTaskConflictException>(() =>
                Service(db).CompleteAsync(pending.Id, new CompleteTaskDto { OutputJson = "{}" }));
        }

        var claimed = await ClaimAsync(pending.Id, ByCode(pending.LinkCode));
        await using (var db = NewContext())
        {
            await Assert.ThrowsAsync<WorldTaskConflictException>(() =>
                Service(db).CompleteAsync(claimed.Id, new CompleteTaskDto { OutputJson = "{}", ServerId = "other" }));
        }
        await using (var db = NewContext())
        {
            var done = await Service(db).CompleteAsync(claimed.Id, new CompleteTaskDto { OutputJson = "{\"x\":1}", ServerId = "survival-1" });
            Assert.Equal(WorldTaskStatuses.Completed, done.Status);
        }
        var stored = await ReadAsync(claimed.Id);
        Assert.Null(stored.LeaseUntil);
        Assert.Equal(_now, stored.CompletedAt);
        await using (var db = NewContext())
        {
            // Completing twice returns the task; failing a completed task is refused.
            Assert.Equal(WorldTaskStatuses.Completed, (await Service(db).CompleteAsync(claimed.Id, new CompleteTaskDto { OutputJson = "{}" })).Status);
            await Assert.ThrowsAsync<WorldTaskConflictException>(() =>
                Service(db).FailAsync(claimed.Id, new FailTaskDto { ErrorMessage = "late" }));
        }
    }

    [Fact]
    public async Task Fail_OfAClaimedTask_RecordsTheReason()
    {
        var claimed = await ClaimedTaskAsync();
        await using var db = NewContext();

        var failed = await Service(db).FailAsync(claimed.Id, new FailTaskDto { ErrorMessage = "No handler for VerifyLocation", ServerId = "survival-1" });

        Assert.Equal(WorldTaskStatuses.Failed, failed.Status);
        Assert.Equal("No handler for VerifyLocation", failed.ErrorMessage);
    }

    // ---- cancel / release / heartbeat ----

    [Fact]
    public async Task Cancel_EndsAnOpenTask_AndIsIdempotent_ButNotForAClosedOne()
    {
        var claimed = await ClaimedTaskAsync();
        await using (var db = NewContext())
        {
            var cancelled = await Service(db).CancelAsync(claimed.Id, new CancelTaskDto { Reason = "Capture again" });
            Assert.Equal(WorldTaskStatuses.Cancelled, cancelled.Status);
            Assert.Equal("Capture again", cancelled.ErrorMessage);
        }
        await using (var db = NewContext())
        {
            Assert.Equal(WorldTaskStatuses.Cancelled, (await Service(db).CancelAsync(claimed.Id, null)).Status);
            // The game server learns from its next heartbeat.
            await Assert.ThrowsAsync<WorldTaskConflictException>(() => Service(db).HeartbeatAsync(claimed.Id, new HeartbeatTaskDto { ServerId = "survival-1" }));
        }
        var sessionId = await SeedSessionAsync();
        var other = await CreateTaskAsync(sessionId, field: "Other");
        await using (var db = NewContext())
        {
            var row = await db.WorldTasks.SingleAsync(t => t.Id == other.Id);
            row.Status = WorldTaskStatuses.Failed;
            await db.SaveChangesAsync();
        }
        await using (var db = NewContext())
        {
            await Assert.ThrowsAsync<WorldTaskConflictException>(() => Service(db).CancelAsync(other.Id, null));
        }
    }

    [Fact]
    public async Task Release_PutsTheTaskBackToPending_WithItsCodeAndAFreshExpiry()
    {
        var claimed = await ClaimedTaskAsync();
        _now = _now.AddMinutes(20);
        await using (var db = NewContext())
        {
            await Assert.ThrowsAsync<WorldTaskConflictException>(() => Service(db).ReleaseAsync(claimed.Id, new ReleaseTaskDto { ServerId = "other" }));
        }
        await using (var db = NewContext())
        {
            var released = await Service(db).ReleaseAsync(claimed.Id, new ReleaseTaskDto { ServerId = "survival-1", Reason = "quit" });
            Assert.Equal(WorldTaskStatuses.Pending, released.Status);
        }

        var stored = await ReadAsync(claimed.Id);
        Assert.Equal(claimed.LinkCode, stored.LinkCode);
        Assert.Null(stored.ClaimedByUuid);
        Assert.Null(stored.ClaimedByServerId);
        Assert.Null(stored.LeaseUntil);
        Assert.Equal(1, stored.ReleaseCount);
        Assert.Equal(_now.AddMinutes(30), stored.ExpiresAt);
        // Someone else can now claim it by code.
        Assert.Equal(WorldTaskStatuses.InProgress, (await ClaimAsync(claimed.Id, ByCode(claimed.LinkCode, OtherUuid))).Status);
    }

    [Fact]
    public async Task Heartbeat_RenewsTheLease_OnTheClaimingServerOnly()
    {
        var claimed = await ClaimedTaskAsync();
        _now = _now.AddMinutes(2);
        await using (var db = NewContext())
        {
            await Service(db).HeartbeatAsync(claimed.Id, new HeartbeatTaskDto { ServerId = "survival-1" });
            await Assert.ThrowsAsync<WorldTaskConflictException>(() => Service(db).HeartbeatAsync(claimed.Id, new HeartbeatTaskDto { ServerId = "other" }));
        }
        Assert.Equal(_now.AddMinutes(3), (await ReadAsync(claimed.Id)).LeaseUntil);
    }

    [Fact]
    public async Task ListOpenForServer_ReturnsTheTasksThisServerHolds()
    {
        var claimed = await ClaimedTaskAsync("survival-1");
        var sessionId = await SeedSessionAsync();
        await CreateTaskAsync(sessionId, field: "Pending");
        await using var db = NewContext();

        var open = await Service(db).ListOpenForServerAsync("survival-1");

        Assert.Equal(new[] { claimed.Id }, open.Select(t => t.Id));
        Assert.Empty(await Service(db).ListOpenForServerAsync("other"));
    }

    // ---- status endpoint ----

    [Fact]
    public async Task UpdateStatus_FollowsTheStateMachine()
    {
        var sessionId = await SeedSessionAsync();
        var task = await CreateTaskAsync(sessionId);
        await using var db = NewContext();
        var service = Service(db);

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateStatusAsync(task.Id, "Claimed"));
        await Assert.ThrowsAsync<WorldTaskConflictException>(() => service.UpdateStatusAsync(task.Id, "completed"));
        Assert.Equal(WorldTaskStatuses.Cancelled, (await service.UpdateStatusAsync(task.Id, "cancelled")).Status);
        await Assert.ThrowsAsync<WorldTaskConflictException>(() => service.UpdateStatusAsync(task.Id, "Pending"));
    }

    [Theory]
    [InlineData("Pending", "InProgress", true)]
    [InlineData("Pending", "Expired", true)]
    [InlineData("Pending", "Completed", false)]
    [InlineData("InProgress", "Pending", true)]
    [InlineData("InProgress", "Cancelled", true)]
    [InlineData("Completed", "Failed", false)]
    [InlineData("Failed", "Pending", false)]
    [InlineData("Expired", "InProgress", false)]
    [InlineData("Cancelled", "Pending", false)]
    public void CanTransition_MatchesTheDesign(string from, string to, bool allowed)
    {
        Assert.Equal(allowed, WorldTaskStatuses.CanTransition(from, to));
    }

    // ---- anchored to the draft ----

    [Fact]
    public async Task ListLatestForProgress_GivesTheNewestTaskPerField_AndMarkAppliedNeedsACompletedTask()
    {
        var sessionId = await SeedSessionAsync();
        var draftId = await SeedDraftAsync();
        var oldLocation = await CreateTaskAsync(sessionId, draftId: draftId);
        _now = _now.AddSeconds(5);
        var newLocation = await CreateTaskAsync(sessionId, draftId: draftId);
        var region = await CreateTaskAsync(sessionId, field: "WgRegionId", type: "WgRegionId", draftId: draftId);
        await CreateTaskAsync(sessionId, field: "Unrelated");

        await using (var db = NewContext())
        {
            var latest = await Service(db).ListLatestForProgressAsync(draftId);
            Assert.Equal(new[] { newLocation.Id, region.Id }, latest.Select(t => t.Id).OrderBy(i => i));
            Assert.DoesNotContain(latest, t => t.Id == oldLocation.Id);
            await Assert.ThrowsAsync<WorldTaskConflictException>(() => Service(db).MarkAppliedAsync(newLocation.Id));
        }

        await ClaimAsync(newLocation.Id, ByCode(newLocation.LinkCode));
        await using (var db = NewContext())
        {
            await Service(db).CompleteAsync(newLocation.Id, new CompleteTaskDto { OutputJson = "{}" });
        }
        await using (var db = NewContext())
        {
            var applied = await Service(db).MarkAppliedAsync(newLocation.Id);
            Assert.NotNull(applied.AppliedAt);
        }
        Assert.Equal(_now, (await ReadAsync(newLocation.Id)).AppliedAt);
    }
}
