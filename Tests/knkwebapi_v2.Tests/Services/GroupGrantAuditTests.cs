using AutoMapper;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// PandiO/knk-workspace#50 (developer decision 2026-11-15, generic audit subject): a change to a permission group's
/// own grants leaves one audit entry with the group as subject and no target user, found by
/// <c>GET api/audit-log?subjectType=PermissionGroup&amp;subjectId=</c> (the group's History list). Real grant service,
/// audit service and repositories on an in-memory database.
/// </summary>
public class GroupGrantAuditTests : IDisposable
{
    private const int Staff = 1;
    private const int Alice = 2;
    private const int Knight = 50;
    private const int Squire = 51;

    private readonly KnKDbContext _db;
    private readonly PermissionGrantService _grants;
    private readonly AuditLogService _audit;

    public GroupGrantAuditTests()
    {
        _db = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase($"GroupGrantAudit_{Guid.NewGuid()}").Options);
        _db.Users.AddRange(new User { Id = Staff, Username = "staff" }, new User { Id = Alice, Username = "alice" });
        _db.PermissionGroups.AddRange(new PermissionGroup { Id = Knight, Name = "Knight", Weight = 10 },
            new PermissionGroup { Id = Squire, Name = "Squire", Weight = 5 });
        _db.SaveChanges();

        var mapper = new MapperConfiguration(c => c.AddProfile<PermissionMappingProfile>()).CreateMapper();
        var users = new UserRepository(_db);
        _audit = new AuditLogService(new AuditLogRepository(_db), users);
        _grants = new PermissionGrantService(new PermissionGrantRepository(_db), mapper, users, _audit);
    }

    public void Dispose() => _db.Dispose();

    private Task<knkwebapi_v2.Dtos.PagedResultDto<knkwebapi_v2.Dtos.AuditLogEntryDto>> HistoryOf(int groupId) =>
        _audit.SearchAsync(null, null, null, null, AuditSubjectTypes.PermissionGroup, groupId, 1, 20);

    [Fact]
    public async Task GrantChangeExpiryAndRemove_LeaveOneEntryEach_WithTheGroupAsSubject()
    {
        var expiry = new DateTime(2027, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        await _grants.UpsertByNodeAsync(Knight, "knk.gate.open", true, null, Staff);
        await _grants.UpsertByNodeAsync(Knight, "knk.gate.open", true, expiry, Staff);
        await _grants.UpsertByNodeAsync(Knight, "knk.gate.open", false, expiry, Staff);
        await _grants.RevokeByNodeAsync(Knight, "knk.gate.open", Staff);

        var history = await HistoryOf(Knight);

        Assert.Equal(4, history.TotalCount);
        Assert.Equal(new[] { "GrantRemoved", "GrantUpdated", "GrantUpdated", "GrantAdded" }, history.Items.Select(e => e.Action));
        Assert.All(history.Items, e =>
        {
            Assert.Null(e.TargetUserId);
            Assert.Null(e.TargetUsername);
            Assert.Equal(AuditSubjectTypes.PermissionGroup, e.SubjectType);
            Assert.Equal(Knight, e.SubjectId);
            Assert.Equal(Staff, e.ActorUserId);
            Assert.Equal("staff", e.ActorUsername);
            Assert.Contains("knk.gate.open", e.Details);
        });
    }

    [Fact]
    public async Task SavingAnExistingNode_UpdatesTheRowAndNeverAddsASecond()
    {
        await _grants.UpsertByNodeAsync(Knight, "knk.fly", true, null, Staff);
        await _grants.UpsertByNodeAsync(Knight, "knk.fly", false, null, Staff);

        Assert.Single(await _db.PermissionGrants.Where(g => g.HolderId == Knight && g.Node == "knk.fly").ToListAsync());
        Assert.Equal(new[] { "GrantUpdated", "GrantAdded" }, (await HistoryOf(Knight)).Items.Select(e => e.Action));
    }

    [Fact]
    public async Task TheHistoryOfOneGroup_LeavesOutOtherGroupsAndPlayers()
    {
        await _grants.UpsertByNodeAsync(Knight, "knk.a", true, null, Staff);
        await _grants.UpsertByNodeAsync(Squire, "knk.b", true, null, Staff);
        await _grants.UpsertByNodeAsync(Alice, "knk.c", true, null, Staff);

        var knight = await HistoryOf(Knight);
        Assert.Single(knight.Items);
        Assert.Contains("knk.a", knight.Items[0].Details);
        Assert.Single((await HistoryOf(Squire)).Items);
    }

    [Fact]
    public async Task APlayersGrantChange_StaysAboutThePlayer_WithoutASubject()
    {
        await _grants.UpsertByNodeAsync(Alice, "knk.fly", true, null, Staff);

        var player = await _audit.SearchAsync(Alice, null, null, null, 1, 20);
        var entry = Assert.Single(player.Items);
        Assert.Equal((int?)Alice, entry.TargetUserId);
        Assert.Equal("alice", entry.TargetUsername);
        Assert.Null(entry.SubjectType);
        Assert.Null(entry.SubjectId);
    }

    [Fact]
    public async Task APlayersAuditLog_DoesNotShowGroupEntries()
    {
        _db.UserPermissionGroups.Add(new UserPermissionGroup { UserId = Alice, PermissionGroupId = Knight });
        await _db.SaveChangesAsync();
        await _grants.UpsertByNodeAsync(Knight, "knk.a", true, null, Staff);

        Assert.Empty((await _audit.SearchAsync(Alice, null, null, null, 1, 20)).Items);
        // The staff member's own actor search still lists it (who changed what).
        Assert.Single((await _audit.SearchAsync(null, Staff, null, null, 1, 20)).Items);
    }

    [Fact]
    public async Task ErasingTheStaffMembersData_KeepsTheGroupHistory()
    {
        await _grants.UpsertByNodeAsync(Knight, "knk.a", true, null, Staff);
        _db.AuditLogEntries.Add(new AuditLogEntry { ActorUserId = Alice, TargetUserId = Staff, Action = AuditAction.TitleChanged });
        await _db.SaveChangesAsync();
        var privacy = new PrivacyRepository(_db);

        var counts = await privacy.CountUserDataAsync(new[] { Staff });
        Assert.Equal(1, counts["audit_log_entries"]);

        await privacy.EraseUserDataAsync(new[] { Staff });
        _db.ChangeTracker.Clear();

        // Rows about the erased player go; the group's entry the player wrote as staff stays (names only the id).
        Assert.Single((await HistoryOf(Knight)).Items);
        Assert.Empty(await _db.AuditLogEntries.Where(e => e.TargetUserId == Staff).ToListAsync());
    }
}
