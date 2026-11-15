using Xunit;
using Moq;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Unit tests for PermissionGrantService's audit retrofit (docs/specs/user-management/
/// IMPLEMENTATION_PLAN.md §0). The interesting case is that HolderId may belong to a User or a
/// PermissionGroup (PermissionHolder is a TPT base for both) — only the former has one specific
/// TargetUserId an audit entry can point at; a group's change is audited with the group as subject (#50).
/// </summary>
public class PermissionGrantServiceTests
{
    private readonly Mock<IPermissionGrantRepository> _mockRepo = new();
    private readonly Mock<IMapper> _mockMapper = new();
    private readonly Mock<IUserRepository> _mockUserRepo = new();
    private readonly Mock<IAuditLogService> _mockAuditLogService = new();
    private readonly PermissionGrantService _service;

    public PermissionGrantServiceTests()
    {
        _service = new PermissionGrantService(_mockRepo.Object, _mockMapper.Object, _mockUserRepo.Object, _mockAuditLogService.Object);
        _mockMapper.Setup(m => m.Map<PermissionGrant>(It.IsAny<PermissionGrantDto>()))
            .Returns((PermissionGrantDto dto) => new PermissionGrant { Id = 1, HolderId = dto.HolderId, Node = dto.Node, Value = dto.Value, ExpiresAt = dto.ExpiresAt });
        _mockMapper.Setup(m => m.Map<PermissionGrantDto>(It.IsAny<PermissionGrant>()))
            .Returns((PermissionGrant g) => new PermissionGrantDto { Id = g.Id, HolderId = g.HolderId, Node = g.Node, Value = g.Value, ExpiresAt = g.ExpiresAt });
        _mockRepo.Setup(r => r.HolderExistsAsync(It.IsAny<int>())).ReturnsAsync(true);
        _mockRepo.Setup(r => r.GetGrantsForHolderNodeAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(new List<PermissionGrant>());
    }

    [Fact]
    public async Task CreateAsync_HolderIsUser_RecordsGrantAddedAgainstThatUser()
    {
        _mockUserRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new User { Id = 1, Username = "alice" });

        await _service.CreateAsync(new PermissionGrantDto { HolderId = 1, Node = "knk.chat.color", Value = true }, actorUserId: 4);

        _mockAuditLogService.Verify(a => a.RecordAsync(4, 1, AuditAction.GrantAdded, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_HolderIsPermissionGroup_RecordsOneEntryWithTheGroupAsSubject()
    {
        // HolderId 100 is a PermissionGroup, not a User: no single player is "the target", so the entry is about the
        // group (PandiO/knk-workspace#50, generic audit subject) and names no target user.
        _mockUserRepo.Setup(r => r.GetByIdAsync(100)).ReturnsAsync((User?)null);

        await _service.CreateAsync(new PermissionGrantDto { HolderId = 100, Node = "knk.mode.staff", Value = true }, actorUserId: 4);

        _mockAuditLogService.Verify(a => a.RecordAsync(It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<AuditAction>(), It.IsAny<string?>()), Times.Never);
        _mockAuditLogService.Verify(a => a.RecordForSubjectAsync(4, AuditSubjectTypes.PermissionGroup, 100, AuditAction.GrantAdded,
            It.Is<string?>(d => d!.Contains("\"node\":\"knk.mode.staff\"") && d.Contains("\"value\":true") && d.Contains("\"expiresAt\":null"))),
            Times.Once);
    }

    [Fact]
    public async Task UserHolderChanges_NeverRecordASubjectEntry()
    {
        _mockUserRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new User { Id = 1, Username = "alice" });

        await _service.UpsertByNodeAsync(1, "knk.fly", true, null, actorUserId: 4);

        _mockAuditLogService.Verify(a => a.RecordForSubjectAsync(It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<AuditAction>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task UpsertByNodeAsync_GroupHolderExistingNode_RecordsGrantUpdatedWithBeforeAndAfter()
    {
        var expiry = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var existing = new PermissionGrant { Id = 7, HolderId = 100, Node = "knk.gate.open", Value = true, ExpiresAt = null };
        _mockRepo.Setup(r => r.GetGrantsForHolderNodeAsync(100, "knk.gate.open")).ReturnsAsync(new List<PermissionGrant> { existing });
        _mockUserRepo.Setup(r => r.GetByIdAsync(100)).ReturnsAsync((User?)null);
        string? details = null;
        _mockAuditLogService.Setup(a => a.RecordForSubjectAsync(4, AuditSubjectTypes.PermissionGroup, 100, AuditAction.GrantUpdated, It.IsAny<string?>()))
            .Callback((int? _, string _, int _, AuditAction _, string? d) => details = d)
            .Returns(Task.CompletedTask);

        await _service.UpsertByNodeAsync(100, "knk.gate.open", false, expiry, actorUserId: 4);

        _mockRepo.Verify(r => r.AddAsync(It.IsAny<PermissionGrant>()), Times.Never);
        Assert.NotNull(details);
        using var json = System.Text.Json.JsonDocument.Parse(details!);
        Assert.True(json.RootElement.GetProperty("from").GetProperty("value").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, json.RootElement.GetProperty("from").GetProperty("expiresAt").ValueKind);
        Assert.False(json.RootElement.GetProperty("to").GetProperty("value").GetBoolean());
        Assert.Equal(expiry, json.RootElement.GetProperty("to").GetProperty("expiresAt").GetDateTime());
        Assert.Equal("knk.gate.open", json.RootElement.GetProperty("to").GetProperty("node").GetString());
    }

    [Fact]
    public async Task RevokeByNodeAsync_GroupHolder_RecordsGrantRemovedWithTheExpiry()
    {
        var expiry = DateTime.UtcNow.AddDays(3);
        _mockRepo.Setup(r => r.GetActiveGrantsForHolderAsync(100, It.IsAny<DateTime>())).ReturnsAsync(new List<PermissionGrant>
        {
            new() { Id = 8, HolderId = 100, Node = "knk.fly", Value = false, ExpiresAt = expiry }
        });
        _mockUserRepo.Setup(r => r.GetByIdAsync(100)).ReturnsAsync((User?)null);

        await _service.RevokeByNodeAsync(100, "knk.fly", actorUserId: 4);

        _mockAuditLogService.Verify(a => a.RecordForSubjectAsync(4, AuditSubjectTypes.PermissionGroup, 100, AuditAction.GrantRemoved,
            It.Is<string?>(d => d!.Contains("\"node\":\"knk.fly\"") && d.Contains("\"value\":false") && d.Contains("\"expiresAt\":\""))),
            Times.Once);
        _mockAuditLogService.Verify(a => a.RecordAsync(It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<AuditAction>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAndUpdateById_GroupHolder_AreAuditedWithTheGroupAsSubject()
    {
        _mockRepo.Setup(r => r.GetByIdAsync(9)).ReturnsAsync(() => new PermissionGrant { Id = 9, HolderId = 100, Node = "knk.fly", Value = true });
        _mockUserRepo.Setup(r => r.GetByIdAsync(100)).ReturnsAsync((User?)null);

        await _service.UpdateAsync(9, new PermissionGrantDto { HolderId = 100, Node = "knk.fly", Value = false }, actorUserId: 4);
        await _service.DeleteAsync(9, actorUserId: 4);

        _mockAuditLogService.Verify(a => a.RecordForSubjectAsync(4, AuditSubjectTypes.PermissionGroup, 100, AuditAction.GrantUpdated, It.IsAny<string?>()), Times.Once);
        _mockAuditLogService.Verify(a => a.RecordForSubjectAsync(4, AuditSubjectTypes.PermissionGroup, 100, AuditAction.GrantRemoved, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_HolderIsUser_RecordsGrantRemoved()
    {
        _mockRepo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new PermissionGrant { Id = 5, HolderId = 1, Node = "knk.fly", Value = true });
        _mockUserRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new User { Id = 1, Username = "alice" });

        await _service.DeleteAsync(5, actorUserId: 4);

        _mockAuditLogService.Verify(a => a.RecordAsync(4, 1, AuditAction.GrantRemoved, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_HolderIsUser_RecordsGrantUpdated()
    {
        _mockRepo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new PermissionGrant { Id = 5, HolderId = 1, Node = "knk.fly", Value = true });
        _mockUserRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new User { Id = 1, Username = "alice" });

        await _service.UpdateAsync(5, new PermissionGrantDto { HolderId = 1, Node = "knk.fly", Value = false }, actorUserId: 4);

        _mockAuditLogService.Verify(a => a.RecordAsync(4, 1, AuditAction.GrantUpdated, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task UpsertByNodeAsync_NoExistingGrant_CreatesOneAndRecordsGrantAdded()
    {
        _mockRepo.Setup(r => r.GetGrantsForHolderNodeAsync(1, "knk.mode.staff")).ReturnsAsync(new List<PermissionGrant>());
        _mockUserRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new User { Id = 1, Username = "alice" });

        var result = await _service.UpsertByNodeAsync(1, "knk.mode.staff", true, null, actorUserId: 4);

        Assert.Equal("knk.mode.staff", result.Node);
        _mockRepo.Verify(r => r.AddAsync(It.IsAny<PermissionGrant>()), Times.Once);
        _mockAuditLogService.Verify(a => a.RecordAsync(4, 1, AuditAction.GrantAdded, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task UpsertByNodeAsync_ExistingGrantForNode_UpdatesItAndRecordsGrantUpdated()
    {
        var existing = new PermissionGrant { Id = 5, HolderId = 1, Node = "knk.mode.staff", Value = true, ExpiresAt = null };
        _mockRepo.Setup(r => r.GetGrantsForHolderNodeAsync(1, "knk.mode.staff")).ReturnsAsync(new List<PermissionGrant> { existing });
        _mockUserRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new User { Id = 1, Username = "alice" });
        var newExpiry = DateTime.UtcNow.AddHours(2);

        await _service.UpsertByNodeAsync(1, "knk.mode.staff", true, newExpiry, actorUserId: 4);

        _mockRepo.Verify(r => r.UpdateAsync(It.Is<PermissionGrant>(g => g.Id == 5 && g.ExpiresAt == newExpiry)), Times.Once);
        _mockRepo.Verify(r => r.AddAsync(It.IsAny<PermissionGrant>()), Times.Never);
        _mockAuditLogService.Verify(a => a.RecordAsync(4, 1, AuditAction.GrantUpdated, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task UpsertByNodeAsync_GrantThenDeny_FlipsTheSameRowInsteadOfAddingOne()
    {
        // KNG-59: denying a node the player was granted updates that row's value and expiry.
        var existing = new PermissionGrant { Id = 5, HolderId = 1, Node = "knk.gate.open", Value = true, ExpiresAt = null };
        _mockRepo.Setup(r => r.GetGrantsForHolderNodeAsync(1, "knk.gate.open")).ReturnsAsync(new List<PermissionGrant> { existing });
        _mockUserRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new User { Id = 1, Username = "alice" });
        var newExpiry = DateTime.UtcNow.AddDays(1);

        var result = await _service.UpsertByNodeAsync(1, "knk.gate.open", false, newExpiry, actorUserId: 4);

        Assert.Equal(5, result.Id);
        Assert.False(result.Value);
        _mockRepo.Verify(r => r.UpdateAsync(It.Is<PermissionGrant>(g => g.Id == 5 && !g.Value && g.ExpiresAt == newExpiry)), Times.Once);
        _mockRepo.Verify(r => r.AddAsync(It.IsAny<PermissionGrant>()), Times.Never);
        _mockRepo.Verify(r => r.DeleteAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task UpsertByNodeAsync_OnlyAnExpiredRow_RevivesItInsteadOfAddingOne()
    {
        var expired = new PermissionGrant { Id = 5, HolderId = 1, Node = "knk.gate.open", Value = false, ExpiresAt = DateTime.UtcNow.AddDays(-1) };
        _mockRepo.Setup(r => r.GetGrantsForHolderNodeAsync(1, "knk.gate.open")).ReturnsAsync(new List<PermissionGrant> { expired });
        _mockUserRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new User { Id = 1, Username = "alice" });

        await _service.UpsertByNodeAsync(1, "knk.gate.open", true, null, actorUserId: 4);

        _mockRepo.Verify(r => r.UpdateAsync(It.Is<PermissionGrant>(g => g.Id == 5 && g.Value && g.ExpiresAt == null)), Times.Once);
        _mockRepo.Verify(r => r.AddAsync(It.IsAny<PermissionGrant>()), Times.Never);
        _mockAuditLogService.Verify(a => a.RecordAsync(4, 1, AuditAction.GrantUpdated, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task UpsertByNodeAsync_ExistingDuplicates_KeepsTheActiveRowAndDeletesTheRest()
    {
        // Rows left by the old create-only quick action: an expired grant, an active grant and an
        // active deny for one node. The first active row is kept and updated; the others go, and
        // only the active one that goes is audited as a removal.
        var expired = new PermissionGrant { Id = 4, HolderId = 1, Node = "knk.gate.open", Value = true, ExpiresAt = DateTime.UtcNow.AddDays(-1) };
        var grant = new PermissionGrant { Id = 5, HolderId = 1, Node = "knk.gate.open", Value = true, ExpiresAt = null };
        var deny = new PermissionGrant { Id = 6, HolderId = 1, Node = "knk.gate.open", Value = false, ExpiresAt = null };
        _mockRepo.Setup(r => r.GetGrantsForHolderNodeAsync(1, "knk.gate.open")).ReturnsAsync(new List<PermissionGrant> { expired, grant, deny });
        _mockUserRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new User { Id = 1, Username = "alice" });

        await _service.UpsertByNodeAsync(1, "knk.gate.open", false, null, actorUserId: 4);

        _mockRepo.Verify(r => r.UpdateAsync(It.Is<PermissionGrant>(g => g.Id == 5 && !g.Value)), Times.Once);
        _mockRepo.Verify(r => r.DeleteAsync(4), Times.Once);
        _mockRepo.Verify(r => r.DeleteAsync(6), Times.Once);
        _mockRepo.Verify(r => r.DeleteAsync(5), Times.Never);
        _mockRepo.Verify(r => r.AddAsync(It.IsAny<PermissionGrant>()), Times.Never);
        _mockAuditLogService.Verify(a => a.RecordAsync(4, 1, AuditAction.GrantRemoved, It.IsAny<string?>()), Times.Once);
        _mockAuditLogService.Verify(a => a.RecordAsync(4, 1, AuditAction.GrantUpdated, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_NodeTheHolderAlreadyHas_UpdatesThatRowInsteadOfAddingOne()
    {
        // KNG-59: the generic create upserts by (holder, node) like every other grant write.
        var existing = new PermissionGrant { Id = 5, HolderId = 1, Node = "knk.gate.open", Value = true, ExpiresAt = null };
        _mockRepo.Setup(r => r.GetGrantsForHolderNodeAsync(1, "knk.gate.open")).ReturnsAsync(new List<PermissionGrant> { existing });
        _mockUserRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new User { Id = 1, Username = "alice" });

        var result = await _service.CreateAsync(new PermissionGrantDto { HolderId = 1, Node = "knk.gate.open", Value = false }, actorUserId: 4);

        Assert.Equal(5, result.Id);
        _mockRepo.Verify(r => r.UpdateAsync(It.Is<PermissionGrant>(g => g.Id == 5 && !g.Value)), Times.Once);
        _mockRepo.Verify(r => r.AddAsync(It.IsAny<PermissionGrant>()), Times.Never);
        _mockAuditLogService.Verify(a => a.RecordAsync(4, 1, AuditAction.GrantUpdated, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_MissingNodeOrHolder_ThrowsArgumentException()
    {
        _mockRepo.Setup(r => r.HolderExistsAsync(99)).ReturnsAsync(false);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(new PermissionGrantDto { HolderId = 1, Node = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(new PermissionGrantDto { HolderId = 99, Node = "knk.fly" }));
        _mockRepo.Verify(r => r.AddAsync(It.IsAny<PermissionGrant>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_OntoANodeTheHolderAlreadyHas_ThrowsConflictAndLeavesBothRows()
    {
        var moving = new PermissionGrant { Id = 5, HolderId = 1, Node = "knk.fly", Value = true };
        var taken = new PermissionGrant { Id = 6, HolderId = 1, Node = "knk.gate.open", Value = false };
        _mockRepo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(moving);
        _mockRepo.Setup(r => r.GetGrantsForHolderNodeAsync(1, "knk.gate.open")).ReturnsAsync(new List<PermissionGrant> { taken });

        var ex = await Assert.ThrowsAsync<PermissionGrantConflictException>(() =>
            _service.UpdateAsync(5, new PermissionGrantDto { HolderId = 1, Node = "knk.gate.open", Value = true }));

        Assert.Equal(6, ex.ExistingGrantId);
        Assert.Equal("knk.fly", moving.Node);
        _mockRepo.Verify(r => r.UpdateAsync(It.IsAny<PermissionGrant>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_OntoAnotherHolderThatHasTheNode_ThrowsConflict()
    {
        var moving = new PermissionGrant { Id = 5, HolderId = 1, Node = "knk.fly", Value = true };
        _mockRepo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(moving);
        _mockRepo.Setup(r => r.GetGrantsForHolderNodeAsync(2, "knk.fly"))
            .ReturnsAsync(new List<PermissionGrant> { new() { Id = 9, HolderId = 2, Node = "knk.fly", Value = true } });

        await Assert.ThrowsAsync<PermissionGrantConflictException>(() =>
            _service.UpdateAsync(5, new PermissionGrantDto { HolderId = 2, Node = "knk.fly", Value = true }));
    }

    [Fact]
    public async Task UpdateAsync_SameNodeNewValue_UpdatesWithoutAConflictCheck()
    {
        var row = new PermissionGrant { Id = 5, HolderId = 1, Node = "knk.fly", Value = true };
        _mockRepo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(row);

        await _service.UpdateAsync(5, new PermissionGrantDto { HolderId = 1, Node = "knk.fly", Value = false });

        _mockRepo.Verify(r => r.UpdateAsync(It.Is<PermissionGrant>(g => g.Id == 5 && !g.Value)), Times.Once);
        _mockRepo.Verify(r => r.GetGrantsForHolderNodeAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RevokeByNodeAsync_ExistingGrant_DeletesItAndRecordsGrantRemoved()
    {
        var existing = new PermissionGrant { Id = 5, HolderId = 1, Node = "knk.mode.staff", Value = true };
        _mockRepo.Setup(r => r.GetActiveGrantsForHolderAsync(1, It.IsAny<DateTime>())).ReturnsAsync(new List<PermissionGrant> { existing });
        _mockUserRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new User { Id = 1, Username = "alice" });

        await _service.RevokeByNodeAsync(1, "knk.mode.staff", actorUserId: 4);

        _mockRepo.Verify(r => r.DeleteAsync(5), Times.Once);
        _mockAuditLogService.Verify(a => a.RecordAsync(4, 1, AuditAction.GrantRemoved, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task RevokeByNodeAsync_GrantAndDenyOnSameNode_DeletesBothAndLeavesOtherNodes()
    {
        // KNG-59: POST {id}/grants creates rather than upserts, so a player can carry a grant and
        // a deny for the same node; removing the node must clear both or it stays in effect.
        var grant = new PermissionGrant { Id = 5, HolderId = 1, Node = "knk.gate.open", Value = true };
        var deny = new PermissionGrant { Id = 6, HolderId = 1, Node = "knk.gate.open", Value = false };
        var other = new PermissionGrant { Id = 7, HolderId = 1, Node = "knk.gate.close", Value = true };
        _mockRepo.Setup(r => r.GetActiveGrantsForHolderAsync(1, It.IsAny<DateTime>())).ReturnsAsync(new List<PermissionGrant> { grant, deny, other });
        _mockUserRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new User { Id = 1, Username = "alice" });

        await _service.RevokeByNodeAsync(1, "knk.gate.open", actorUserId: 4);

        _mockRepo.Verify(r => r.DeleteAsync(5), Times.Once);
        _mockRepo.Verify(r => r.DeleteAsync(6), Times.Once);
        _mockRepo.Verify(r => r.DeleteAsync(7), Times.Never);
        _mockAuditLogService.Verify(a => a.RecordAsync(4, 1, AuditAction.GrantRemoved, It.IsAny<string?>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RevokeByNodeAsync_NoExistingGrant_ThrowsKeyNotFoundException()
    {
        _mockRepo.Setup(r => r.GetActiveGrantsForHolderAsync(1, It.IsAny<DateTime>())).ReturnsAsync(new List<PermissionGrant>());

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.RevokeByNodeAsync(1, "knk.mode.staff"));
    }
}
