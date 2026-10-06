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
/// TargetUserId an audit entry can point at.
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
    }

    [Fact]
    public async Task CreateAsync_HolderIsUser_RecordsGrantAddedAgainstThatUser()
    {
        _mockUserRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new User { Id = 1, Username = "alice" });

        await _service.CreateAsync(new PermissionGrantDto { HolderId = 1, Node = "knk.chat.color", Value = true }, actorUserId: 4);

        _mockAuditLogService.Verify(a => a.RecordAsync(4, 1, AuditAction.GrantAdded, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_HolderIsPermissionGroup_DoesNotRecordAuditEntry()
    {
        // HolderId 100 is a PermissionGroup, not a User — no single player is "the target",
        // so this grant is intentionally not audited per-player (see the service's own doc
        // comment on IsUserHolderAsync).
        _mockUserRepo.Setup(r => r.GetByIdAsync(100)).ReturnsAsync((User?)null);

        await _service.CreateAsync(new PermissionGrantDto { HolderId = 100, Node = "knk.mode.staff", Value = true });

        _mockAuditLogService.Verify(a => a.RecordAsync(It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<AuditAction>(), It.IsAny<string?>()), Times.Never);
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
