using Xunit;
using Moq;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Unit tests for UserPermissionGroupService (docs/specs/user-features/IMPLEMENTATION_PLAN.md §5):
/// membership upsert/delete validation and premium-tier selection, including the "temporary
/// higher tier over a permanent lower one" restore-on-expiry behavior.
/// </summary>
public class UserPermissionGroupServiceTests
{
    private readonly Mock<IUserPermissionGroupRepository> _mockRepo = new();
    private readonly Mock<IUserRepository> _mockUserRepo = new();
    private readonly Mock<IPermissionGroupRepository> _mockGroupRepo = new();
    private readonly Mock<IAuditLogService> _mockAuditLogService = new();
    private readonly UserPermissionGroupService _service;

    private static readonly PermissionGroup Staff = new() { Id = 100, Name = "Staff", Weight = 50, IsPremiumTier = false };
    private static readonly PermissionGroup Noble = new() { Id = 101, Name = "Noble", Weight = 10, IsPremiumTier = true };
    private static readonly PermissionGroup Royal = new() { Id = 102, Name = "Royal", Weight = 20, IsPremiumTier = true };
    private static readonly PermissionGroup DragonBlood = new() { Id = 103, Name = "Dragon Blood", Weight = 30, IsPremiumTier = true };

    public UserPermissionGroupServiceTests()
    {
        _service = new UserPermissionGroupService(_mockRepo.Object, _mockUserRepo.Object, _mockGroupRepo.Object, _mockAuditLogService.Object);
        _mockUserRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new User { Id = 1, Username = "alice" });
        _mockGroupRepo.Setup(r => r.GetByIdAsync(Royal.Id)).ReturnsAsync(Royal);
    }

    private static UserPermissionGroup Membership(PermissionGroup group, DateTime? expiresAt) =>
        new() { UserId = 1, PermissionGroupId = group.Id, PermissionGroup = group, ExpiresAt = expiresAt };

    #region SelectPremiumTier

    [Fact]
    public void SelectPremiumTier_NoMemberships_ReturnsNull()
    {
        Assert.Null(UserPermissionGroupService.SelectPremiumTier(new List<UserPermissionGroup>(), DateTime.UtcNow));
    }

    [Fact]
    public void SelectPremiumTier_OnlyNonPremiumGroups_ReturnsNull()
    {
        var result = UserPermissionGroupService.SelectPremiumTier(new[] { Membership(Staff, null) }, DateTime.UtcNow);
        Assert.Null(result);
    }

    [Fact]
    public void SelectPremiumTier_IgnoresHigherWeightStaffGroup_PicksPremium()
    {
        var result = UserPermissionGroupService.SelectPremiumTier(
            new[] { Membership(Staff, null), Membership(Noble, null) }, DateTime.UtcNow);
        Assert.Equal(Noble.Id, result!.PermissionGroupId);
    }

    [Fact]
    public void SelectPremiumTier_MultiplePremium_PicksHighestWeight()
    {
        var result = UserPermissionGroupService.SelectPremiumTier(
            new[] { Membership(Noble, null), Membership(DragonBlood, null), Membership(Royal, null) }, DateTime.UtcNow);
        Assert.Equal(DragonBlood.Id, result!.PermissionGroupId);
    }

    [Fact]
    public void SelectPremiumTier_TemporaryHigherTierActive_WinsOverPermanentLowerTier()
    {
        var now = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
        var result = UserPermissionGroupService.SelectPremiumTier(
            new[] { Membership(Noble, null), Membership(Royal, now.AddDays(7)) }, now);
        Assert.Equal(Royal.Id, result!.PermissionGroupId);
    }

    [Fact]
    public void SelectPremiumTier_TemporaryHigherTierExpired_RestoresPermanentLowerTier()
    {
        var now = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
        var result = UserPermissionGroupService.SelectPremiumTier(
            new[] { Membership(Noble, null), Membership(Royal, now.AddSeconds(-1)) }, now);
        Assert.Equal(Noble.Id, result!.PermissionGroupId);
    }

    [Fact]
    public void SelectPremiumTier_ExpiresExactlyNow_TreatedAsExpired()
    {
        var now = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
        var result = UserPermissionGroupService.SelectPremiumTier(new[] { Membership(Royal, now) }, now);
        Assert.Null(result);
    }

    [Fact]
    public void SelectPremiumTier_EqualWeight_TieBrokenByLowestGroupId()
    {
        var a = new PermissionGroup { Id = 7, Name = "A", Weight = 10, IsPremiumTier = true };
        var b = new PermissionGroup { Id = 3, Name = "B", Weight = 10, IsPremiumTier = true };
        var result = UserPermissionGroupService.SelectPremiumTier(new[] { Membership(a, null), Membership(b, null) }, DateTime.UtcNow);
        Assert.Equal(3, result!.PermissionGroupId);
    }

    #endregion

    #region UpsertAsync

    [Fact]
    public async Task UpsertAsync_NewMembership_AddsAndReturnsActiveDto()
    {
        var expires = DateTime.UtcNow.AddDays(30);

        var result = await _service.UpsertAsync(new UpsertUserPermissionGroupDto { UserId = 1, PermissionGroupId = Royal.Id, ExpiresAt = expires });

        _mockRepo.Verify(r => r.AddAsync(It.Is<UserPermissionGroup>(m =>
            m.UserId == 1 && m.PermissionGroupId == Royal.Id && m.ExpiresAt == expires)), Times.Once);
        Assert.Equal("Royal", result.PermissionGroupName);
        Assert.True(result.IsPremiumTier);
        Assert.True(result.IsActive);
        _mockAuditLogService.Verify(a => a.RecordAsync(null, 1, knkwebapi_v2.Enums.AuditAction.GroupAssigned, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task UpsertAsync_ExistingMembership_UpdatesExpiryInsteadOfAdding()
    {
        var existing = Membership(Royal, DateTime.UtcNow.AddDays(1));
        _mockRepo.Setup(r => r.GetAsync(1, Royal.Id)).ReturnsAsync(existing);

        var result = await _service.UpsertAsync(new UpsertUserPermissionGroupDto { UserId = 1, PermissionGroupId = Royal.Id, ExpiresAt = null });

        _mockRepo.Verify(r => r.AddAsync(It.IsAny<UserPermissionGroup>()), Times.Never);
        _mockRepo.Verify(r => r.UpdateAsync(It.Is<UserPermissionGroup>(m => m.ExpiresAt == null)), Times.Once);
        Assert.Null(result.ExpiresAt);
    }

    [Fact]
    public async Task UpsertAsync_ExpiresAtInPast_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpsertAsync(
            new UpsertUserPermissionGroupDto { UserId = 1, PermissionGroupId = Royal.Id, ExpiresAt = DateTime.UtcNow.AddMinutes(-1) }));
        _mockRepo.Verify(r => r.AddAsync(It.IsAny<UserPermissionGroup>()), Times.Never);
    }

    [Fact]
    public async Task UpsertAsync_UnspecifiedKindExpiry_StoredAsUtc()
    {
        var unspecified = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(3), DateTimeKind.Unspecified);

        await _service.UpsertAsync(new UpsertUserPermissionGroupDto { UserId = 1, PermissionGroupId = Royal.Id, ExpiresAt = unspecified });

        _mockRepo.Verify(r => r.AddAsync(It.Is<UserPermissionGroup>(m =>
            m.ExpiresAt!.Value.Kind == DateTimeKind.Utc && m.ExpiresAt.Value.Ticks == unspecified.Ticks)), Times.Once);
    }

    [Fact]
    public async Task UpsertAsync_UnknownUser_ThrowsKeyNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.UpsertAsync(
            new UpsertUserPermissionGroupDto { UserId = 999, PermissionGroupId = Royal.Id }));
    }

    [Fact]
    public async Task UpsertAsync_UnknownGroup_ThrowsKeyNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.UpsertAsync(
            new UpsertUserPermissionGroupDto { UserId = 1, PermissionGroupId = 999 }));
    }

    #endregion

    #region DeleteAsync / GetActivePremiumTierAsync

    [Fact]
    public async Task DeleteAsync_NotAMember_ThrowsKeyNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.DeleteAsync(1, Royal.Id));
    }

    [Fact]
    public async Task DeleteAsync_Member_Deletes()
    {
        var existing = Membership(Royal, null);
        _mockRepo.Setup(r => r.GetAsync(1, Royal.Id)).ReturnsAsync(existing);

        await _service.DeleteAsync(1, Royal.Id, actorUserId: 9);

        _mockRepo.Verify(r => r.DeleteAsync(existing), Times.Once);
        _mockAuditLogService.Verify(a => a.RecordAsync(9, 1, knkwebapi_v2.Enums.AuditAction.GroupRemoved, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task GetActivePremiumTierAsync_ReturnsSelectedTierDto()
    {
        var expires = DateTime.UtcNow.AddDays(2);
        _mockRepo.Setup(r => r.GetByUserAsync(1)).ReturnsAsync(new List<UserPermissionGroup>
        {
            Membership(Staff, null), Membership(Noble, null), Membership(Royal, expires)
        });

        var result = await _service.GetActivePremiumTierAsync(1);

        Assert.Equal("Royal", result!.PermissionGroupName);
        Assert.Equal(expires, result.ExpiresAt);
    }

    #endregion
}
