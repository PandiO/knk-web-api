using Xunit;
using Moq;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Models;
using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Unit tests for SalaryService (docs/specs/user-features/DESIGN.md §5,
/// IMPLEMENTATION_PLAN.md §6). Covers the 1-hour eligibility gate, the gap-covering payout math
/// (global x personal x rank multipliers x hours elapsed), and the developer-confirmed "multiply
/// every active membership's SalaryMultiplier together" rank-multiplier combination rule.
/// </summary>
public class SalaryServiceTests
{
    private readonly Mock<IUserRepository> _mockUserRepo;
    private readonly Mock<IUserPermissionGroupRepository> _mockMembershipRepo;
    private readonly Mock<ISalaryConfigurationService> _mockConfigService;
    private readonly Mock<IAuditLogService> _mockAuditLogService;
    private readonly SalaryService _service;

    public SalaryServiceTests()
    {
        _mockUserRepo = new Mock<IUserRepository>();
        _mockMembershipRepo = new Mock<IUserPermissionGroupRepository>();
        _mockConfigService = new Mock<ISalaryConfigurationService>();
        _mockAuditLogService = new Mock<IAuditLogService>();
        _service = new SalaryService(_mockUserRepo.Object, _mockMembershipRepo.Object, _mockConfigService.Object, _mockAuditLogService.Object);

        _mockConfigService.Setup(c => c.GetAsync()).ReturnsAsync(new SalaryConfigurationDto { GlobalMultiplier = 1.0m });
        _mockMembershipRepo.Setup(r => r.GetByUserAsync(It.IsAny<int>())).ReturnsAsync(new List<UserPermissionGroup>());
    }

    private static User MakeUser(int id, DateTime lastPayout, decimal personalMultiplier = 1.0m, int coins = 0) => new()
    {
        Id = id,
        Username = $"user{id}",
        LastSalaryPayoutAt = lastPayout,
        PersonalSalaryMultiplier = personalMultiplier,
        Coins = coins
    };

    [Fact]
    public async Task PayOutAsync_UnknownUser_ThrowsKeyNotFound()
    {
        _mockUserRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((User?)null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.PayOutAsync(999));
    }

    [Fact]
    public async Task PayOutAsync_LessThanOneHourSinceLastPayout_ReturnsNotPaidAndDoesNotTouchCoins()
    {
        var user = MakeUser(1, DateTime.UtcNow.AddMinutes(-30), coins: 100);
        _mockUserRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);

        var result = await _service.PayOutAsync(1);

        Assert.False(result.Paid);
        Assert.Equal(0, result.AmountPaid);
        _mockUserRepo.Verify(r => r.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        _mockAuditLogService.Verify(a => a.RecordAsync(It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<Enums.AuditAction>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task PayOutAsync_Paid_RecordsSystemInitiatedAuditEntry()
    {
        var user = MakeUser(1, DateTime.UtcNow.AddHours(-2));
        _mockUserRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);

        await _service.PayOutAsync(1);

        // System-initiated (docs/specs/user-management/DESIGN.md §4: actor null for automatic
        // payouts) — closes user-features IMPLEMENTATION_PLAN.md §6 carried-forward item 4.
        _mockAuditLogService.Verify(a => a.RecordAsync(null, 1, Enums.AuditAction.SalaryPayout, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task PayOutAsync_ExactlyOneHourElapsed_IsEligible()
    {
        var user = MakeUser(1, DateTime.UtcNow.AddHours(-1).AddSeconds(-1));
        _mockUserRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);

        var result = await _service.PayOutAsync(1);

        Assert.True(result.Paid);
    }

    [Fact]
    public async Task PayOutAsync_NoActiveMemberships_RankMultiplierIsOneNotZero()
    {
        var user = MakeUser(1, DateTime.UtcNow.AddHours(-2));
        _mockUserRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);
        _mockConfigService.Setup(c => c.GetAsync()).ReturnsAsync(new SalaryConfigurationDto { GlobalMultiplier = 10.0m });

        var result = await _service.PayOutAsync(1);

        Assert.Equal(1.0m, result.RankMultiplier);
        // 10 (global) * 1.0 (personal) * 1.0 (rank) * 2 hours = 20, not 0.
        Assert.Equal(20, result.AmountPaid);
    }

    [Fact]
    public async Task PayOutAsync_MultipleActiveMemberships_MultipliesRankMultipliersTogether()
    {
        var user = MakeUser(1, DateTime.UtcNow.AddHours(-1));
        _mockUserRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);
        _mockConfigService.Setup(c => c.GetAsync()).ReturnsAsync(new SalaryConfigurationDto { GlobalMultiplier = 1.0m });

        var staffGroup = new PermissionGroup { Id = 10, Name = "Staff", SalaryMultiplier = 1.2m };
        var premiumGroup = new PermissionGroup { Id = 20, Name = "Royal", SalaryMultiplier = 1.5m, IsPremiumTier = true };
        _mockMembershipRepo.Setup(r => r.GetByUserAsync(1)).ReturnsAsync(new List<UserPermissionGroup>
        {
            new() { UserId = 1, PermissionGroupId = 10, PermissionGroup = staffGroup, ExpiresAt = null },
            new() { UserId = 1, PermissionGroupId = 20, PermissionGroup = premiumGroup, ExpiresAt = null }
        });

        var result = await _service.PayOutAsync(1);

        Assert.Equal(1.8m, result.RankMultiplier); // 1.2 * 1.5, not 1.2 + 1.5 or highest-wins 1.5
    }

    [Fact]
    public async Task PayOutAsync_ExpiredMembershipExcludedFromRankMultiplier()
    {
        var user = MakeUser(1, DateTime.UtcNow.AddHours(-1));
        _mockUserRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);

        var expiredGroup = new PermissionGroup { Id = 10, Name = "ExpiredTier", SalaryMultiplier = 5.0m };
        _mockMembershipRepo.Setup(r => r.GetByUserAsync(1)).ReturnsAsync(new List<UserPermissionGroup>
        {
            new() { UserId = 1, PermissionGroupId = 10, PermissionGroup = expiredGroup, ExpiresAt = DateTime.UtcNow.AddMinutes(-5) }
        });

        var result = await _service.PayOutAsync(1);

        Assert.Equal(1.0m, result.RankMultiplier);
    }

    [Fact]
    public async Task PayOutAsync_StillActiveFutureExpiryIncludedInRankMultiplier()
    {
        var user = MakeUser(1, DateTime.UtcNow.AddHours(-1));
        _mockUserRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);

        var group = new PermissionGroup { Id = 10, Name = "TempTier", SalaryMultiplier = 2.0m };
        _mockMembershipRepo.Setup(r => r.GetByUserAsync(1)).ReturnsAsync(new List<UserPermissionGroup>
        {
            new() { UserId = 1, PermissionGroupId = 10, PermissionGroup = group, ExpiresAt = DateTime.UtcNow.AddMinutes(5) }
        });

        var result = await _service.PayOutAsync(1);

        Assert.Equal(2.0m, result.RankMultiplier);
    }

    [Fact]
    public async Task PayOutAsync_ScalesByAllThreeMultipliersAndHoursCovered()
    {
        var user = MakeUser(1, DateTime.UtcNow.AddHours(-3), personalMultiplier: 2.0m, coins: 50);
        _mockUserRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);
        _mockConfigService.Setup(c => c.GetAsync()).ReturnsAsync(new SalaryConfigurationDto { GlobalMultiplier = 5.0m });

        var group = new PermissionGroup { Id = 10, Name = "Rank", SalaryMultiplier = 1.5m };
        _mockMembershipRepo.Setup(r => r.GetByUserAsync(1)).ReturnsAsync(new List<UserPermissionGroup>
        {
            new() { UserId = 1, PermissionGroupId = 10, PermissionGroup = group, ExpiresAt = null }
        });

        var result = await _service.PayOutAsync(1);

        // 5 (global) * 2.0 (personal) * 1.5 (rank) * 3 hours = 45.
        Assert.Equal(45, result.AmountPaid);
        Assert.Equal(95, result.NewCoinsBalance); // 50 existing + 45
        Assert.Equal(50 + 45, user.Coins);
        _mockUserRepo.Verify(r => r.UpdateUserAsync(It.Is<User>(u => u.Coins == 95)), Times.Once);
    }

    [Fact]
    public async Task GetCurrentRankMultiplierAsync_InvalidUserId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetCurrentRankMultiplierAsync(0));
    }

    [Fact]
    public async Task GetCurrentRankMultiplierAsync_NoActiveMemberships_ReturnsOne()
    {
        var result = await _service.GetCurrentRankMultiplierAsync(1);

        Assert.Equal(1.0m, result);
    }

    [Fact]
    public async Task GetCurrentRankMultiplierAsync_MatchesPayOutAsyncAndDoesNotTouchCoins()
    {
        var group = new PermissionGroup { Id = 10, Name = "Rank", SalaryMultiplier = 1.5m };
        _mockMembershipRepo.Setup(r => r.GetByUserAsync(1)).ReturnsAsync(new List<UserPermissionGroup>
        {
            new() { UserId = 1, PermissionGroupId = 10, PermissionGroup = group, ExpiresAt = null }
        });

        var result = await _service.GetCurrentRankMultiplierAsync(1);

        Assert.Equal(1.5m, result);
        _mockUserRepo.Verify(r => r.GetByIdAsync(It.IsAny<int>()), Times.Never);
        _mockUserRepo.Verify(r => r.UpdateUserAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task PayOutAsync_AdvancesLastSalaryPayoutAtToNow()
    {
        var lastPayout = DateTime.UtcNow.AddHours(-5);
        var user = MakeUser(1, lastPayout);
        _mockUserRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);

        var before = DateTime.UtcNow;
        var result = await _service.PayOutAsync(1);
        var after = DateTime.UtcNow;

        Assert.True(result.LastSalaryPayoutAt >= before && result.LastSalaryPayoutAt <= after);
        Assert.Equal(result.LastSalaryPayoutAt, user.LastSalaryPayoutAt);
        Assert.NotEqual(lastPayout, user.LastSalaryPayoutAt);
    }
}
