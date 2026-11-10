using System.Text.Json;
using AutoMapper;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Realtime.Contracts;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services.WorldTasks;

/// <summary>
/// PandiO/knk-workspace#181 (WT2, DESIGN §8.3–§8.6): how a task reaches the game: auto (offered to the online creator),
/// busy, queue, replace, hand-off with Accept/Decline, the offer answer, the task code always working, re-offers on
/// rejoin, the busy check, and the sweep's offer timeout. Real repositories on EF InMemory; the hub side is recorded.
/// </summary>
public class WorldTaskDeliveryTests
{
    private readonly string _dbName = $"world-task-delivery-{Guid.NewGuid()}";
    private DateTime _now = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
    private readonly IMapper _mapper = new MapperConfiguration(c => c.AddProfile<WorkflowMappingProfile>()).CreateMapper();
    private readonly RecordingRealtime _realtime = new();

    private const int Admin = 5;
    private const string AdminUuid = "0f6c2a1e-7d0b-4c52-9a51-3c1e6f0a9b11";
    private const int Builder = 9;
    private const string BuilderUuid = "aa6c2a1e-7d0b-4c52-9a51-3c1e6f0a9b22";

    /// <summary>Records what the service tells the hubs; "online" is a uuid → server map.</summary>
    private sealed class RecordingRealtime : IWorldTaskRealtime
    {
        public Dictionary<string, string> Online { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(int TaskId, string Status, string OfferState)> Changes { get; } = new();
        public List<(int TaskId, string ServerId, string Uuid, string Mode, string? From)> Offers { get; } = new();
        public List<(int TaskId, string Status, string? Uuid, string? Reason)> Cancels { get; } = new();
        public List<(int TaskId, string Uuid)> Withdrawn { get; } = new();
        public List<int> Headless { get; } = new();

        public string? ServerOf(string? uuid) => uuid != null && Online.TryGetValue(uuid, out var server) ? server : null;
        public void Changed(WorldTask task) => Changes.Add((task.Id, task.Status, task.OfferState));
        public void Offered(WorldTask task, string serverId, string uuid, string mode, string? fromUsername) =>
            Offers.Add((task.Id, serverId, uuid, mode, fromUsername));
        public void Cancelled(WorldTask task, string? uuid, string? reason) => Cancels.Add((task.Id, task.Status, uuid, reason));
        public void OfferWithdrawn(WorldTask task, string uuid, string? reason) => Withdrawn.Add((task.Id, uuid));
        public void HeadlessPending(WorldTask task) => Headless.Add(task.Id);
    }

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_dbName).Options);

    private WorldTaskService Service(KnKDbContext db) => new(
        new WorldTaskRepository(db), new WorkflowRepository(db), Mock.Of<IGateDoorService>(), _mapper,
        Options.Create(new WorldTaskOptions()), () => _now, _realtime);

    private async Task<int> SeedAsync()
    {
        await using var db = NewContext();
        if (!await db.Users.AnyAsync())
        {
            db.Users.Add(new User { Id = Admin, Username = "Alice", Uuid = AdminUuid });
            db.Users.Add(new User { Id = Builder, Username = "Bob", Uuid = BuilderUuid });
            db.Users.Add(new User { Id = 11, Username = "NoAccount" });
            db.FormConfigurations.Add(new FormConfiguration { Id = 3, Name = "New town", EntityTypeName = "Town" });
        }
        var session = new WorkflowSession { UserId = Admin, EntityTypeName = "Town", FormConfigurationId = 3, CreatedAt = _now };
        db.WorkflowSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    private static JsonElement Deliver(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private async Task<WorldTaskReadDto> CreateAsync(int sessionId, string? deliver = null, string field = "Location",
        string type = "LocationSelection", int caller = Admin)
    {
        await using var db = NewContext();
        return await Service(db).CreateAsync(new WorldTaskCreateDto
        {
            WorkflowSessionId = sessionId, StepNumber = 0, StepKey = "step-1", FieldName = field, TaskType = type,
            InputJson = "{}", Deliver = deliver == null ? null : Deliver(deliver)
        }, caller);
    }

    private async Task<WorldTask> ReadAsync(int id)
    {
        await using var db = NewContext();
        return await db.WorldTasks.AsNoTracking().SingleAsync(t => t.Id == id);
    }

    private async Task<WorldTaskReadDto> ClaimAsync(int id, string mode, string uuid, string? code = null)
    {
        await using var db = NewContext();
        return await Service(db).ClaimAsync(id, new ClaimTaskDto
        {
            ClaimMode = mode, ClaimedByUuid = uuid, TaskCode = code, ClaimedByServerId = "survival-1",
            ClaimedByMinecraftUsername = uuid == AdminUuid ? "Alice" : "Bob"
        });
    }

    /// <summary>The admin holds a claimed task (InProgress) in game.</summary>
    private async Task<WorldTaskReadDto> HeldByAdminAsync(int sessionId)
    {
        var held = await CreateAsync(sessionId, "\"code\"", field: "Held");
        return await ClaimAsync(held.Id, "code", AdminUuid, held.LinkCode);
    }

    // ---- delivery parsing ----

    [Theory]
    [InlineData(null, "auto", null)]
    [InlineData("\"auto\"", "auto", null)]
    [InlineData("\"CODE\"", "code", null)]
    [InlineData("\"queue\"", "queue", null)]
    [InlineData("\"replace\"", "replace", null)]
    [InlineData("{\"userId\": 9}", "user", 9)]
    public void Parse_ReadsEveryForm(string? json, string kind, int? userId)
    {
        var delivery = WorldTaskDelivery.Parse(json == null ? null : Deliver(json));
        Assert.Equal(kind, delivery.Kind);
        Assert.Equal(userId, delivery.UserId);
    }

    [Theory]
    [InlineData("\"broadcast\"")]
    [InlineData("{\"userId\": 0}")]
    [InlineData("42")]
    public void Parse_RefusesTheRest(string json) =>
        Assert.Throws<ArgumentException>(() => WorldTaskDelivery.Parse(Deliver(json)));

    // ---- auto ----

    [Fact]
    public async Task Auto_CreatorOnline_IsOfferedToTheirServer_AsAutoClaim()
    {
        var sessionId = await SeedAsync();
        _realtime.Online[AdminUuid] = "survival-1";

        var task = await CreateAsync(sessionId);

        var stored = await ReadAsync(task.Id);
        Assert.Equal(WorldTaskOfferStates.Offered, stored.OfferState);
        Assert.Equal(Admin, stored.TargetUserId);
        Assert.Equal(_now, stored.OfferedAt);
        Assert.NotNull(stored.LinkCode); // the code always exists (R5)
        var offer = Assert.Single(_realtime.Offers);
        Assert.Equal((task.Id, "survival-1", AdminUuid, WorldTaskOfferModes.AutoClaim, (string?)null), offer);
        Assert.Equal("Alice", task.TargetUsername);
        Assert.Contains(_realtime.Changes, c => c.TaskId == task.Id && c.OfferState == WorldTaskOfferStates.Offered);
    }

    [Fact]
    public async Task Auto_CreatorOffline_LeavesTheTaskCode_AsBefore()
    {
        var sessionId = await SeedAsync();

        var task = await CreateAsync(sessionId);

        var stored = await ReadAsync(task.Id);
        Assert.Equal(WorldTaskOfferStates.None, stored.OfferState);
        Assert.Null(stored.TargetUserId);
        Assert.Empty(_realtime.Offers);
    }

    [Fact]
    public async Task Auto_CreatorAlreadyHoldsATask_IsBusy_NotSent()
    {
        var sessionId = await SeedAsync();
        await HeldByAdminAsync(sessionId);
        _realtime.Online[AdminUuid] = "survival-1";

        var task = await CreateAsync(sessionId);

        Assert.Equal(WorldTaskOfferStates.Busy, (await ReadAsync(task.Id)).OfferState);
        Assert.Empty(_realtime.Offers);
    }

    [Fact]
    public async Task Code_IsNeverOffered()
    {
        var sessionId = await SeedAsync();
        _realtime.Online[AdminUuid] = "survival-1";

        var task = await CreateAsync(sessionId, "\"code\"");

        Assert.Equal(WorldTaskOfferStates.None, (await ReadAsync(task.Id)).OfferState);
        Assert.Empty(_realtime.Offers);
    }

    [Fact]
    public async Task Headless_GoesToTheGameServersAsCreated_NotOffered()
    {
        var sessionId = await SeedAsync();
        _realtime.Online[AdminUuid] = "survival-1";

        var task = await CreateAsync(sessionId, type: WorldTaskTypes.GateBlockScan);

        Assert.Equal(new[] { task.Id }, _realtime.Headless);
        Assert.Empty(_realtime.Offers);
    }

    // ---- queue and replace ----

    [Fact]
    public async Task Queue_WaitsForTheHeldTask_ThenIsOfferedWhenItCompletes()
    {
        var sessionId = await SeedAsync();
        var held = await HeldByAdminAsync(sessionId);
        _realtime.Online[AdminUuid] = "survival-1";

        var queued = await CreateAsync(sessionId, "\"queue\"", field: "Second");
        Assert.Equal(WorldTaskOfferStates.Queued, (await ReadAsync(queued.Id)).OfferState);
        Assert.Empty(_realtime.Offers);

        await using (var db = NewContext())
        {
            await Service(db).CompleteAsync(held.Id, new CompleteTaskDto { OutputJson = "{\"x\":1}", ServerId = "survival-1" });
        }

        Assert.Equal(WorldTaskOfferStates.Offered, (await ReadAsync(queued.Id)).OfferState);
        Assert.Equal(queued.Id, Assert.Single(_realtime.Offers).TaskId);
    }

    [Fact]
    public async Task Queue_WhileOffline_StaysQueued_AndIsOfferedOnJoin()
    {
        var sessionId = await SeedAsync();
        var queued = await CreateAsync(sessionId, "\"queue\"");
        Assert.Equal(WorldTaskOfferStates.Queued, (await ReadAsync(queued.Id)).OfferState);

        _realtime.Online[AdminUuid] = "survival-1";
        await using (var db = NewContext())
        {
            Assert.Equal(1, await Service(db).OfferWaitingForPlayersAsync(new[] { AdminUuid.ToUpperInvariant() }));
        }

        Assert.Equal(WorldTaskOfferStates.Offered, (await ReadAsync(queued.Id)).OfferState);
        Assert.Equal(WorldTaskOfferModes.AutoClaim, Assert.Single(_realtime.Offers).Mode);
    }

    [Fact]
    public async Task Replace_ReleasesTheHeldTaskWithItsCode_AndOffersTheNewOne()
    {
        var sessionId = await SeedAsync();
        var held = await HeldByAdminAsync(sessionId);
        _realtime.Online[AdminUuid] = "survival-1";

        var task = await CreateAsync(sessionId, "\"replace\"", field: "Second");

        var released = await ReadAsync(held.Id);
        Assert.Equal(WorldTaskStatuses.Pending, released.Status); // released, not cancelled (Q17)
        Assert.Equal(held.LinkCode, released.LinkCode);
        Assert.Null(released.ClaimedByUuid);
        var cancel = Assert.Single(_realtime.Cancels);
        Assert.Equal((held.Id, WorldTaskStatuses.Pending, (string?)AdminUuid), (cancel.TaskId, cancel.Status, cancel.Uuid));
        Assert.Equal(WorldTaskOfferStates.Offered, (await ReadAsync(task.Id)).OfferState);
        Assert.Equal(task.Id, Assert.Single(_realtime.Offers).TaskId);
    }

    // ---- hand-off ----

    [Fact]
    public async Task HandOff_OnlinePlayer_GetsAnAcceptDeclineOffer_NamingTheSender()
    {
        var sessionId = await SeedAsync();
        _realtime.Online[BuilderUuid] = "creative-2";

        var task = await CreateAsync(sessionId, "{\"userId\": 9}");

        var stored = await ReadAsync(task.Id);
        Assert.Equal((Builder, WorldTaskOfferStates.Offered), (stored.TargetUserId!.Value, stored.OfferState));
        Assert.Equal((task.Id, "creative-2", BuilderUuid, WorldTaskOfferModes.Accept, (string?)"Alice"), Assert.Single(_realtime.Offers));
    }

    [Fact]
    public async Task HandOff_PlayerNotOnline_IsDeclined_SoTheWebAppShowsTheCode()
    {
        var sessionId = await SeedAsync();

        var task = await CreateAsync(sessionId, "{\"userId\": 9}");

        Assert.Equal(WorldTaskOfferStates.Declined, (await ReadAsync(task.Id)).OfferState);
        Assert.Empty(_realtime.Offers);
    }

    [Fact]
    public async Task HandOff_UnknownUser_IsRefusedBeforeAnythingIsWritten()
    {
        var sessionId = await SeedAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => CreateAsync(sessionId, "{\"userId\": 404}"));
        await using var db = NewContext();
        Assert.Empty(db.WorldTasks);
    }

    // ---- the offer claim and its answers ----

    [Fact]
    public async Task OfferClaim_ByTheTarget_IsAccepted()
    {
        var sessionId = await SeedAsync();
        _realtime.Online[BuilderUuid] = "survival-1";
        var task = await CreateAsync(sessionId, "{\"userId\": 9}");

        var claimed = await ClaimAsync(task.Id, "offer", BuilderUuid);

        Assert.Equal(WorldTaskStatuses.InProgress, claimed.Status);
        Assert.Equal(WorldTaskOfferStates.Accepted, claimed.OfferState);
    }

    [Theory]
    [InlineData(WorldTaskOfferResults.Declined, WorldTaskOfferStates.Declined)]
    [InlineData(WorldTaskOfferResults.Offline, WorldTaskOfferStates.Declined)]
    [InlineData(WorldTaskOfferResults.Unavailable, WorldTaskOfferStates.Declined)]
    [InlineData(WorldTaskOfferResults.NoPermission, WorldTaskOfferStates.NoPermission)] // #239: kept apart from Declined
    [InlineData(WorldTaskOfferResults.TimedOut, WorldTaskOfferStates.TimedOut)]
    [InlineData(WorldTaskOfferResults.Busy, WorldTaskOfferStates.Busy)]
    public async Task OfferResult_ClosesTheOffer_AndTheOfferClaimIsRefusedAfterwards(string result, string state)
    {
        var sessionId = await SeedAsync();
        _realtime.Online[BuilderUuid] = "survival-1";
        var task = await CreateAsync(sessionId, "{\"userId\": 9}");

        await using (var db = NewContext())
        {
            Assert.True(await Service(db).RecordOfferResultAsync(task.Id, result, BuilderUuid));
        }

        Assert.Equal(state, (await ReadAsync(task.Id)).OfferState);
        var refused = await Assert.ThrowsAsync<WorldTaskConflictException>(() => ClaimAsync(task.Id, "offer", BuilderUuid));
        Assert.Contains("no longer open", refused.Message);
        // The task code still works (R5).
        var byCode = await ClaimAsync(task.Id, "code", BuilderUuid, task.LinkCode);
        Assert.Equal(WorldTaskStatuses.InProgress, byCode.Status);
    }

    [Fact]
    public async Task OfferResult_IsIgnored_WithoutAnOpenOffer_OrFromSomeoneElse()
    {
        var sessionId = await SeedAsync();
        _realtime.Online[BuilderUuid] = "survival-1";
        var plain = await CreateAsync(sessionId, "\"code\"", field: "A");
        var offered = await CreateAsync(sessionId, "{\"userId\": 9}", field: "B");

        await using var db = NewContext();
        var service = Service(db);
        Assert.False(await service.RecordOfferResultAsync(plain.Id, WorldTaskOfferResults.Declined, null));
        Assert.False(await service.RecordOfferResultAsync(offered.Id, WorldTaskOfferResults.Declined, AdminUuid));
        Assert.False(await service.RecordOfferResultAsync(offered.Id, "maybe", BuilderUuid));
        Assert.Equal(WorldTaskOfferStates.Offered, (await ReadAsync(offered.Id)).OfferState);
    }

    [Fact]
    public async Task CodeClaimBySomeoneElse_WithdrawsTheOffer()
    {
        var sessionId = await SeedAsync();
        _realtime.Online[BuilderUuid] = "survival-1";
        var task = await CreateAsync(sessionId, "{\"userId\": 9}");

        var claimed = await ClaimAsync(task.Id, "code", AdminUuid, task.LinkCode);

        Assert.Equal(WorldTaskOfferStates.None, claimed.OfferState);
        Assert.Equal((task.Id, BuilderUuid), Assert.Single(_realtime.Withdrawn));
    }

    // ---- cancel, release, rejoin ----

    [Fact]
    public async Task Cancel_TellsTheGameServers_WhoHeldIt()
    {
        var sessionId = await SeedAsync();
        var held = await HeldByAdminAsync(sessionId);

        await using (var db = NewContext())
        {
            await Service(db).CancelAsync(held.Id, null);
        }

        var cancel = Assert.Single(_realtime.Cancels);
        Assert.Equal((held.Id, WorldTaskStatuses.Cancelled, (string?)AdminUuid), (cancel.TaskId, cancel.Status, cancel.Uuid));
    }

    [Fact]
    public async Task Release_OnQuit_KeepsTheOffer_AndTheTaskIsOfferedAgainOnRejoin()
    {
        var sessionId = await SeedAsync();
        _realtime.Online[AdminUuid] = "survival-1";
        var task = await CreateAsync(sessionId);
        await ClaimAsync(task.Id, "offer", AdminUuid);

        await using (var db = NewContext())
        {
            await Service(db).ReleaseAsync(task.Id, new ReleaseTaskDto { ServerId = "survival-1", Reason = "Alice left" });
        }
        Assert.Equal(WorldTaskOfferStates.Accepted, (await ReadAsync(task.Id)).OfferState);

        await using (var db = NewContext())
        {
            Assert.Equal(1, await Service(db).OfferWaitingForPlayersAsync(new[] { AdminUuid }));
        }
        Assert.Equal(2, _realtime.Offers.Count);
        Assert.Equal(WorldTaskOfferStates.Offered, (await ReadAsync(task.Id)).OfferState);
    }

    [Fact]
    public async Task Release_HandedBackOnPurpose_IsNotOfferedAgain()
    {
        var sessionId = await SeedAsync();
        _realtime.Online[AdminUuid] = "survival-1";
        var task = await CreateAsync(sessionId);
        await ClaimAsync(task.Id, "offer", AdminUuid);

        await using (var db = NewContext())
        {
            await Service(db).ReleaseAsync(task.Id, new ReleaseTaskDto { ServerId = "survival-1", HandBack = true });
        }
        Assert.Equal(WorldTaskOfferStates.Declined, (await ReadAsync(task.Id)).OfferState);

        await using (var db = NewContext())
        {
            Assert.Equal(0, await Service(db).OfferWaitingForPlayersAsync(new[] { AdminUuid }));
        }
    }

    [Fact]
    public async Task Rejoin_HandOffOffer_IsOfferedAgainWithAcceptDecline()
    {
        var sessionId = await SeedAsync();
        _realtime.Online[BuilderUuid] = "survival-1";
        var task = await CreateAsync(sessionId, "{\"userId\": 9}");

        await using (var db = NewContext())
        {
            await Service(db).OfferWaitingForPlayersAsync(new[] { BuilderUuid });
        }

        Assert.Equal(2, _realtime.Offers.Count);
        Assert.All(_realtime.Offers, o => Assert.Equal(WorldTaskOfferModes.Accept, o.Mode));
        Assert.Equal(task.Id, _realtime.Offers[1].TaskId);
    }

    // ---- busy check ----

    [Fact]
    public async Task ActiveForMe_NamesTheHeldTaskAndItsForm_AndCountsTheQueue()
    {
        var sessionId = await SeedAsync();
        var held = await HeldByAdminAsync(sessionId);
        _realtime.Online[AdminUuid] = "survival-1";
        await CreateAsync(sessionId, "\"queue\"", field: "Second");

        await using var db = NewContext();
        var active = await Service(db).GetActiveForUserAsync(Admin);

        Assert.Equal(held.Id, active.Task!.Id);
        Assert.Equal("New town", active.FormName);
        Assert.Equal(1, active.Queued);
        Assert.Null((await Service(db).GetActiveForUserAsync(Builder)).Task);
    }

    // ---- sweep ----

    [Fact]
    public async Task Sweep_TimesOutAnUnansweredOffer_AndWithdrawsIt()
    {
        var sessionId = await SeedAsync();
        _realtime.Online[BuilderUuid] = "survival-1";
        var task = await CreateAsync(sessionId, "{\"userId\": 9}");

        _now = _now.AddSeconds(121);
        await using (var db = NewContext())
        {
            var result = await new WorldTaskSweeper(db, Options.Create(new WorldTaskOptions()), () => _now, _realtime).RunOnceAsync();
            Assert.Equal(1, result.OffersTimedOut);
        }

        Assert.Equal(WorldTaskOfferStates.TimedOut, (await ReadAsync(task.Id)).OfferState);
        Assert.Equal((task.Id, BuilderUuid), Assert.Single(_realtime.Withdrawn));
    }

    [Fact]
    public async Task Sweep_ExpiringAnOfferedTask_TellsTheGameServer()
    {
        var sessionId = await SeedAsync();
        _realtime.Online[AdminUuid] = "survival-1";
        var task = await CreateAsync(sessionId);
        // Keep the offer inside its timeout so only the expiry applies.
        await using (var db = NewContext())
        {
            var row = await db.WorldTasks.SingleAsync(t => t.Id == task.Id);
            row.ExpiresAt = _now.AddSeconds(5);
            row.OfferedAt = _now.AddMinutes(1);
            await db.SaveChangesAsync();
        }

        _now = _now.AddSeconds(10);
        await using (var db = NewContext())
        {
            await new WorldTaskSweeper(db, Options.Create(new WorldTaskOptions()), () => _now, _realtime).RunOnceAsync();
        }

        Assert.Equal(WorldTaskStatuses.Expired, (await ReadAsync(task.Id)).Status);
        var cancel = Assert.Single(_realtime.Cancels);
        Assert.Equal((WorldTaskStatuses.Expired, (string?)AdminUuid), (cancel.Status, cancel.Uuid));
    }

    // ---- #240: Send again ----

    private async Task<WorldTaskReadDto> OfferAgainAsync(int id, string? deliver = null)
    {
        await using var db = NewContext();
        return await Service(db).OfferAgainAsync(id, new OfferTaskDto { Deliver = deliver == null ? null : Deliver(deliver) });
    }

    [Fact]
    public async Task OfferAgain_HandedBackTask_IsOfferedToTheOnlineCreatorAgain_WithAFreshExpiry()
    {
        var sessionId = await SeedAsync();
        _realtime.Online[AdminUuid] = "survival-1";
        var task = await CreateAsync(sessionId);
        await ClaimAsync(task.Id, "offer", AdminUuid);
        await using (var db = NewContext())
        {
            await Service(db).ReleaseAsync(task.Id, new ReleaseTaskDto { ServerId = "survival-1", HandBack = true });
        }
        _now = _now.AddMinutes(20);

        var again = await OfferAgainAsync(task.Id);

        var stored = await ReadAsync(task.Id);
        Assert.Equal(WorldTaskStatuses.Pending, stored.Status);
        Assert.Equal(WorldTaskOfferStates.Offered, stored.OfferState);
        Assert.Equal(task.LinkCode, stored.LinkCode); // same task, same code
        Assert.Equal(_now.AddMinutes(30), stored.ExpiresAt);
        Assert.Equal(2, _realtime.Offers.Count);
        Assert.Equal(WorldTaskOfferModes.AutoClaim, _realtime.Offers[^1].Mode);
        Assert.Equal("Alice", again.TargetUsername);
    }

    [Fact]
    public async Task OfferAgain_DeclinedHandOff_CanBeHandedToSomeoneElse()
    {
        var sessionId = await SeedAsync();
        _realtime.Online[BuilderUuid] = "survival-1";
        var task = await CreateAsync(sessionId, "{\"userId\": 9}");
        await using (var db = NewContext())
        {
            await Service(db).RecordOfferResultAsync(task.Id, WorldTaskOfferResults.NoPermission, BuilderUuid);
        }
        Assert.Equal(WorldTaskOfferStates.NoPermission, (await ReadAsync(task.Id)).OfferState);
        _realtime.Online[AdminUuid] = "survival-2";

        await OfferAgainAsync(task.Id, "\"auto\"");

        var stored = await ReadAsync(task.Id);
        Assert.Equal(WorldTaskOfferStates.Offered, stored.OfferState);
        Assert.Equal(Admin, stored.TargetUserId);
        Assert.Equal(("survival-2", AdminUuid), (_realtime.Offers[^1].ServerId, _realtime.Offers[^1].Uuid));
    }

    [Fact]
    public async Task OfferAgain_CreatorNotOnline_Is409_AndChangesNothing()
    {
        var sessionId = await SeedAsync();
        var task = await CreateAsync(sessionId);

        var error = await Assert.ThrowsAsync<WorldTaskConflictException>(() => OfferAgainAsync(task.Id));

        Assert.Contains("not online", error.Message);
        Assert.Equal(WorldTaskOfferStates.None, (await ReadAsync(task.Id)).OfferState);
        Assert.Empty(_realtime.Offers);
    }

    [Fact]
    public async Task OfferAgain_Queue_WhileOffline_IsQueued()
    {
        var sessionId = await SeedAsync();
        var task = await CreateAsync(sessionId);

        await OfferAgainAsync(task.Id, "\"queue\"");

        Assert.Equal(WorldTaskOfferStates.Queued, (await ReadAsync(task.Id)).OfferState);
    }

    [Fact]
    public async Task OfferAgain_CreatorHoldsATask_IsBusy_UnlessReplace()
    {
        var sessionId = await SeedAsync();
        var held = await HeldByAdminAsync(sessionId);
        _realtime.Online[AdminUuid] = "survival-1";
        var task = await CreateAsync(sessionId, "\"code\"");

        await OfferAgainAsync(task.Id);
        Assert.Equal(WorldTaskOfferStates.Busy, (await ReadAsync(task.Id)).OfferState);

        await OfferAgainAsync(task.Id, "\"replace\"");
        Assert.Equal(WorldTaskOfferStates.Offered, (await ReadAsync(task.Id)).OfferState);
        Assert.Equal(WorldTaskStatuses.Pending, (await ReadAsync(held.Id)).Status); // released with its code
    }

    [Fact]
    public async Task OfferAgain_IsRefused_WhileOffered_ForAScan_OnceClosed_AndForCode()
    {
        var sessionId = await SeedAsync();
        _realtime.Online[AdminUuid] = "survival-1";
        var offered = await CreateAsync(sessionId);
        await Assert.ThrowsAsync<WorldTaskConflictException>(() => OfferAgainAsync(offered.Id));

        var scan = await CreateAsync(sessionId, field: "Scan", type: "GateBlockScan");
        await Assert.ThrowsAsync<WorldTaskConflictException>(() => OfferAgainAsync(scan.Id));

        var closed = await CreateAsync(sessionId, "\"code\"", field: "Closed");
        await using (var db = NewContext())
        {
            await Service(db).CancelAsync(closed.Id, null);
        }
        await Assert.ThrowsAsync<WorldTaskConflictException>(() => OfferAgainAsync(closed.Id));

        await Assert.ThrowsAsync<ArgumentException>(() => OfferAgainAsync(closed.Id, "\"code\""));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => OfferAgainAsync(9999));
    }
}
