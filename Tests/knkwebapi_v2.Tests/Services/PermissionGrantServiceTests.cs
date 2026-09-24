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
}
