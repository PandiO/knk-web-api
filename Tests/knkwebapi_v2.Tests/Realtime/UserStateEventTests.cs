using System.Security.Claims;
using AutoMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Realtime;
using knkwebapi_v2.Realtime.Contracts;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.Realtime;

/// <summary>
/// PandiO/knk-workspace#179 (S2, DESIGN §6.1): the services that change user state raise the user-state events, and
/// the event sink sends them only after the database work committed (DESIGN §4.3 option A, P4).
/// </summary>
public class UserStateEventTests
{
    private const string Uuid = "00000000-0000-0000-0000-0000000000a1";

    /// <summary>Records what services hand to the sink.</summary>
    private sealed class RecordingSink : IRealtimeEventSink
    {
        public List<RealtimeEvent> Events { get; } = new();
        public void Publish(RealtimeEvent realtimeEvent) => Events.Add(realtimeEvent);
        public IEnumerable<T> Payloads<T>(string type) => Events.Where(e => e.Type == type).Select(e => (T)e.Payload!);
    }

    /// <summary>Records what the sink hands to the hubs.</summary>
    private sealed class RecordingPublisher : IRealtimePublisher
    {
        public List<(string Type, object? Payload, string? ServerId, RealtimeActor? Actor)> Game { get; } = new();

        public void PublishToGameServers(string type, object? payload, string? serverId = null, RealtimeActor? actor = null, int v = 1) =>
            Game.Add((type, payload, serverId, actor));

        public void PublishToWebUser(int userId, string type, object? payload, RealtimeActor? actor = null, int v = 1) { }

        public void PublishToConnection(RealtimeHubKind hub, string connectionId, string type, object? payload,
            RealtimeActor? actor = null, int v = 1) { }
    }

    private readonly RecordingSink _sink = new();

    // ===== the sink: after commit =====

    private static KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase("sink-" + Guid.NewGuid()).Options);

    [Fact]
    public void Sink_WithoutATransaction_SendsAtOnce_AsSystemOutsideARequest()
    {
        var publisher = new RecordingPublisher();
        using var db = NewContext();
        var sink = new AfterCommitRealtimeEventSink(publisher, new RealtimeTransactionEvents(), db);

        sink.Publish(new RealtimeEvent(RealtimeEventTypes.UserChanged, "p", ActorUserId: 7));

        var sent = Assert.Single(publisher.Game);
        Assert.Equal(RealtimeEventTypes.UserChanged, sent.Type);
        Assert.Null(sent.ServerId);
        Assert.Equal(7, sent.Actor!.UserId);
        Assert.Equal("system", sent.Actor.Source);
    }

    [Fact]
    public void Sink_InsideATransaction_WaitsForTheCommit_InOrder()
    {
        var publisher = new RecordingPublisher();
        var transactions = new RealtimeTransactionEvents();
        using var db = NewContext();
        var sink = new AfterCommitRealtimeEventSink(publisher, transactions, db, inTransaction: _ => true);

        sink.Publish(new RealtimeEvent("First", null));
        sink.Publish(new RealtimeEvent("Second", null));
        Assert.Empty(publisher.Game);
        Assert.Equal(2, transactions.HeldCount(db));

        transactions.Committed(db);

        Assert.Equal(new[] { "First", "Second" }, publisher.Game.Select(g => g.Type));
        Assert.Equal(0, transactions.HeldCount(db));
        transactions.Committed(db);
        Assert.Equal(2, publisher.Game.Count);
    }

    [Fact]
    public void Sink_RolledBack_DropsWhatTheTransactionHeld_AndOtherContextsKeepTheirs()
    {
        var publisher = new RecordingPublisher();
        var transactions = new RealtimeTransactionEvents();
        using var db = NewContext();
        using var other = NewContext();
        new AfterCommitRealtimeEventSink(publisher, transactions, db, inTransaction: _ => true).Publish(new RealtimeEvent("RolledBack", null));
        new AfterCommitRealtimeEventSink(publisher, transactions, other, inTransaction: _ => true).Publish(new RealtimeEvent("Other", null));

        transactions.Discarded(db);
        transactions.Committed(db);
        Assert.Empty(publisher.Game);

        transactions.Committed(other);
        Assert.Equal("Other", Assert.Single(publisher.Game).Type);
    }

    [Fact]
    public void Sink_NamesTheSource_WebForALoggedInUser()
    {
        var publisher = new RecordingPublisher();
        var http = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("uid", "3") }, "test")),
            }
        };
        using var db = NewContext();
        new AfterCommitRealtimeEventSink(publisher, new RealtimeTransactionEvents(), db, http).Publish(new RealtimeEvent("T", null, 3));

        Assert.Equal("web", Assert.Single(publisher.Game).Actor!.Source);
    }

    [Fact]
    public void Sink_PublisherThrows_DoesNotThrowIntoTheService()
    {
        var publisher = new Mock<IRealtimePublisher>();
        publisher.Setup(p => p.PublishToGameServers(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<string?>(),
            It.IsAny<RealtimeActor?>(), It.IsAny<int>())).Throws(new InvalidOperationException("closed"));
        using var db = NewContext();

        new AfterCommitRealtimeEventSink(publisher.Object, new RealtimeTransactionEvents(), db).Publish(new RealtimeEvent("T", null));
    }

    // ===== UserStateEvents =====

    [Fact]
    public void Events_ForAUserWithoutUuid_AreNotSent()
    {
        var webOnly = new User { Id = 5, Username = "web" };

        _sink.PermissionsChanged(webOnly, UserPermissionsChangeReasons.GroupAssigned);
        _sink.FrozenChanged(webOnly);
        _sink.ModeChanged(webOnly, ActiveMode.Staff);
        _sink.UserChanged(webOnly, new[] { UserChangedFields.Link });
        _sink.BalancesChanged(5, null, 1, 2, 3);
        ((IRealtimeEventSink?)null).PermissionsChanged(new User { Id = 1, Uuid = Uuid }, "x");

        Assert.Empty(_sink.Events);
    }

    // ===== memberships =====

    private readonly Mock<IUserPermissionGroupRepository> _memberships = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IPermissionGroupRepository> _groups = new();
    private readonly Mock<IPlayerNotificationQueue> _queue = new();

    private static readonly PermissionGroup Builders = new() { Id = 40, Name = "Builders", Weight = 5 };

    private UserPermissionGroupService MembershipService() =>
        new(_memberships.Object, _users.Object, _groups.Object, new Mock<IAuditLogService>().Object, _queue.Object,
            realtime: _sink);

    [Fact]
    public async Task GroupAssigned_SendsUserPermissionsChanged_AndStillQueuesRankChanged()
    {
        _users.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new User { Id = 1, Username = "alice", Uuid = Uuid });
        _groups.Setup(r => r.GetByIdAsync(Builders.Id)).ReturnsAsync(Builders);
        _memberships.Setup(r => r.GetByUserAsync(1)).ReturnsAsync(new List<UserPermissionGroup>());

        await MembershipService().UpsertAsync(new UpsertUserPermissionGroupDto { UserId = 1, PermissionGroupId = Builders.Id }, actorUserId: 9);

        var e = Assert.Single(_sink.Events);
        Assert.Equal(RealtimeEventTypes.UserPermissionsChanged, e.Type);
        Assert.Equal(9, e.ActorUserId);
        Assert.Equal(new UserPermissionsChangedPayload(1, Uuid, UserPermissionsChangeReasons.GroupAssigned), e.Payload);
        _queue.Verify(q => q.Enqueue(1, Uuid, "alice", PlayerNotificationTypes.RankChanged, null), Times.Once);
    }

    [Fact]
    public async Task GroupRemoved_SendsUserPermissionsChanged()
    {
        _memberships.Setup(r => r.GetAsync(1, Builders.Id)).ReturnsAsync(new UserPermissionGroup
            { UserId = 1, PermissionGroupId = Builders.Id, PermissionGroup = Builders });
        _users.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new User { Id = 1, Username = "alice", Uuid = Uuid });

        await MembershipService().DeleteAsync(1, Builders.Id, actorUserId: 9);

        Assert.Equal(UserPermissionsChangeReasons.GroupRemoved,
            Assert.Single(_sink.Payloads<UserPermissionsChangedPayload>(RealtimeEventTypes.UserPermissionsChanged)).Reason);
    }

    // ===== grants =====

    private readonly Mock<IPermissionGrantRepository> _grants = new();

    private PermissionGrantService GrantService()
    {
        var mapper = new Mock<IMapper>();
        mapper.Setup(m => m.Map<PermissionGrantDto>(It.IsAny<PermissionGrant>()))
            .Returns((PermissionGrant g) => new PermissionGrantDto { Id = g.Id, HolderId = g.HolderId, Node = g.Node, Value = g.Value });
        _grants.Setup(r => r.HolderExistsAsync(It.IsAny<int>())).ReturnsAsync(true);
        _grants.Setup(r => r.GetGrantsForHolderNodeAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(new List<PermissionGrant>());
        return new PermissionGrantService(_grants.Object, mapper.Object, _users.Object, new Mock<IAuditLogService>().Object, _sink);
    }

    [Fact]
    public async Task GrantToAUser_SendsUserPermissionsChanged()
    {
        _users.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new User { Id = 1, Username = "alice", Uuid = Uuid });

        await GrantService().UpsertByNodeAsync(1, "knk.chat.color", true, null, actorUserId: 9);

        var payload = Assert.Single(_sink.Payloads<UserPermissionsChangedPayload>(RealtimeEventTypes.UserPermissionsChanged));
        Assert.Equal(UserPermissionsChangeReasons.GrantChanged, payload.Reason);
    }

    [Fact]
    public async Task GrantToAGroup_SendsPermissionGroupChanged()
    {
        _users.Setup(r => r.GetByIdAsync(Builders.Id)).ReturnsAsync((User?)null);
        _grants.Setup(r => r.GetActiveGrantsForHolderAsync(Builders.Id, It.IsAny<DateTime>()))
            .ReturnsAsync(new List<PermissionGrant> { new() { Id = 3, HolderId = Builders.Id, Node = "knk.build", Value = true } });

        await GrantService().RevokeByNodeAsync(Builders.Id, "knk.build");

        Assert.Equal(new PermissionGroupChangedPayload(Builders.Id, PermissionGroupChangeOps.GrantsChanged),
            Assert.Single(_sink.Events).Payload);
    }

    [Fact]
    public async Task GrantMovedToAnotherHolder_TellsBothHolders()
    {
        _users.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new User { Id = 1, Username = "alice", Uuid = Uuid });
        _users.Setup(r => r.GetByIdAsync(Builders.Id)).ReturnsAsync((User?)null);
        _grants.Setup(r => r.GetByIdAsync(3)).ReturnsAsync(new PermissionGrant { Id = 3, HolderId = Builders.Id, Node = "knk.build", Value = true });

        await GrantService().UpdateAsync(3, new PermissionGrantDto { HolderId = 1, Node = "knk.build", Value = true });

        Assert.Equal(new[] { RealtimeEventTypes.UserPermissionsChanged, RealtimeEventTypes.PermissionGroupChanged },
            _sink.Events.Select(e => e.Type));
    }

    // ===== groups =====

    [Fact]
    public async Task GroupUpdatedAndDeleted_SendPermissionGroupChanged()
    {
        var repo = new Mock<IPermissionGroupRepository>();
        repo.Setup(r => r.GetByIdAsync(Builders.Id)).ReturnsAsync(new PermissionGroup { Id = Builders.Id, Name = "Builders" });
        var service = new PermissionGroupService(repo.Object, new Mock<IMapper>().Object, _sink);

        await service.UpdateAsync(Builders.Id, new PermissionGroupDto { Name = "Builders", SalaryMultiplier = 1 });
        await service.DeleteAsync(Builders.Id);

        Assert.Equal(new[] { PermissionGroupChangeOps.Updated, PermissionGroupChangeOps.Deleted },
            _sink.Payloads<PermissionGroupChangedPayload>(RealtimeEventTypes.PermissionGroupChanged).Select(p => p.Op));
    }

    // ===== freeze, mode, multipliers, gate method =====

    private UserService UserService(IUserRepository repo) =>
        new(repo, new Mock<IMapper>().Object, new Mock<IPasswordService>().Object, new Mock<ILinkCodeService>().Object,
            new Mock<ITitleService>().Object, new Mock<IUserPermissionGroupService>().Object, new Mock<IAuditLogService>().Object,
            new Mock<IPermissionGroupRepository>().Object, NullLogger<UserService>.Instance, new Mock<ICurrencyService>().Object,
            new Mock<ITitleProgressionService>().Object, realtime: _sink);

    [Fact]
    public async Task Freeze_SendsUserFrozenChanged_WithReasonAndStaff()
    {
        var user = new User { Id = 1, Username = "alice", Uuid = Uuid };
        _users.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);

        await UserService(_users.Object).SetFrozenAsync(1, true, "griefing", actorUserId: 9);
        await UserService(_users.Object).SetFrozenAsync(1, false, null, actorUserId: 9);

        var payloads = _sink.Payloads<UserFrozenChangedPayload>(RealtimeEventTypes.UserFrozenChanged).ToList();
        Assert.Equal(new UserFrozenChangedPayload(1, Uuid, true, "griefing", 9), payloads[0]);
        Assert.Equal(new UserFrozenChangedPayload(1, Uuid, false, null, null), payloads[1]);
    }

    [Fact]
    public async Task ActiveMode_SendsUserModeChanged_OnlyWhenItChanged()
    {
        _users.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new User { Id = 1, Username = "alice", Uuid = Uuid, ActiveMode = ActiveMode.None });

        await UserService(_users.Object).UpdateActiveModeAsync(1, ActiveMode.Staff, actorUserId: 9);
        await UserService(_users.Object).UpdateActiveModeAsync(1, ActiveMode.None);

        Assert.Equal(new UserModeChangedPayload(1, Uuid, "Staff"),
            Assert.Single(_sink.Payloads<UserModeChangedPayload>(RealtimeEventTypes.UserModeChanged)));
    }

    [Fact]
    public async Task MultipliersAndGateMethod_SendUserChanged()
    {
        _users.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new User { Id = 1, Username = "alice", Uuid = Uuid });

        await UserService(_users.Object).SetPersonalMultipliersAsync(1, new UpdatePersonalMultipliersDto { PersonalSalaryMultiplier = 2m });
        await UserService(_users.Object).UpdateGatePassThroughMethodAsync(1, GatePassThroughMethod.Teleport);

        Assert.Equal(new[] { UserChangedFields.Multipliers, UserChangedFields.GatePassThroughMethod },
            _sink.Payloads<UserChangedPayload>(RealtimeEventTypes.UserChanged).Select(p => Assert.Single(p.Fields)));
    }

    // ===== balances =====

    [Fact]
    public async Task Posting_SendsUserBalancesChanged_WithTheBalancesAfterIt_AndAReplaySendsNothing()
    {
        var dbName = "balances-" + Guid.NewGuid();
        await using (var seed = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(dbName).Options))
        {
            seed.Users.Add(new User { Id = 1, Username = "alice", Uuid = Uuid, Coins = 100, Gems = 4, ExperiencePoints = 50 });
            await seed.SaveChangesAsync();
        }
        await using var ctx = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(dbName).Options);
        var currency = new CurrencyService(new CurrencyRepository(ctx), new UserRepository(ctx), NullLogger<CurrencyService>.Instance,
            realtime: _sink);
        var context = CurrencyContext.ForSystem("test", CurrencyReasons.EventReward, "event:1");

        await currency.GrantAsync(1, Currency.Coins, 25, context);
        await currency.GrantAsync(1, Currency.Coins, 25, context);

        Assert.Equal(new UserBalancesChangedPayload(1, Uuid, 125, 4, 50),
            Assert.Single(_sink.Payloads<UserBalancesChangedPayload>(RealtimeEventTypes.UserBalancesChanged)));
    }
}
