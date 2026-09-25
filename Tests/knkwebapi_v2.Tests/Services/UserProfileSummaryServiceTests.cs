using Xunit;
using Moq;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Unit tests for UserProfileSummaryService (docs/specs/user-management/IMPLEMENTATION_PLAN.md
/// Phase 1). Covers the "not found" short-circuit and the salary-state composition (global x
/// personal x rank), since the rest of the aggregate is a thin pass-through of its per-concern
/// services' own already-tested output.
/// </summary>
public class UserProfileSummaryServiceTests
{
    private readonly Mock<IUserService> _mockUserService;
    private readonly Mock<IPermissionResolutionService> _mockPermissionResolutionService;
    private readonly Mock<IUserPermissionGroupService> _mockUserPermissionGroupService;
    private readonly Mock<ITitleService> _mockTitleService;
    private readonly Mock<ISalaryService> _mockSalaryService;
    private readonly Mock<ISalaryConfigurationService> _mockSalaryConfigurationService;
    private readonly UserProfileSummaryService _service;

    public UserProfileSummaryServiceTests()
    {
        _mockUserService = new Mock<IUserService>();
        _mockPermissionResolutionService = new Mock<IPermissionResolutionService>();
        _mockUserPermissionGroupService = new Mock<IUserPermissionGroupService>();
        _mockTitleService = new Mock<ITitleService>();
        _mockSalaryService = new Mock<ISalaryService>();
        _mockSalaryConfigurationService = new Mock<ISalaryConfigurationService>();
        _service = new UserProfileSummaryService(
            _mockUserService.Object,
            _mockPermissionResolutionService.Object,
            _mockUserPermissionGroupService.Object,
            _mockTitleService.Object,
            _mockSalaryService.Object,
            _mockSalaryConfigurationService.Object);
    }

    [Fact]
    public async Task GetAsync_UnknownUser_ReturnsNullWithoutCallingOtherServices()
    {
        _mockUserService.Setup(s => s.GetByIdAsync(999)).ReturnsAsync((UserDto?)null);

        var result = await _service.GetAsync(999);

        Assert.Null(result);
        _mockPermissionResolutionService.Verify(s => s.GetEffectiveAsync(It.IsAny<int>()), Times.Never);
        _mockUserPermissionGroupService.Verify(s => s.GetByUserAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetAsync_KnownUser_ComposesSalaryStateFromGlobalPersonalAndRankMultipliers()
    {
        var lastPayout = DateTime.UtcNow.AddHours(-2);
        var account = new UserDto
        {
            Id = 1,
            Username = "alice",
            ExperiencePoints = 20,
            PersonalSalaryMultiplier = 2.0m,
            LastSalaryPayoutAt = lastPayout
        };
        _mockUserService.Setup(s => s.GetByIdAsync(1)).ReturnsAsync(account);
        _mockPermissionResolutionService.Setup(s => s.GetEffectiveAsync(1))
            .ReturnsAsync(new PermissionEffectiveResponseDto { UserId = 1 });
        _mockUserPermissionGroupService.Setup(s => s.GetByUserAsync(1))
            .ReturnsAsync(new List<UserPermissionGroupDto>());
        _mockTitleService.Setup(s => s.ResolveAsync(20, It.IsAny<Gender?>())).ReturnsAsync(new TitleResolutionDto { TitleBracketId = 3 });
        _mockSalaryService.Setup(s => s.GetCurrentRankMultiplierAsync(1)).ReturnsAsync(1.5m);
        _mockSalaryConfigurationService.Setup(s => s.GetAsync())
            .ReturnsAsync(new SalaryConfigurationDto { GlobalMultiplier = 10.0m });

        var result = await _service.GetAsync(1);

        Assert.NotNull(result);
        Assert.Same(account, result!.Account);
        Assert.Equal(3, result.Title.TitleBracketId);
        Assert.Equal(10.0m, result.Salary.GlobalMultiplier);
        Assert.Equal(2.0m, result.Salary.PersonalMultiplier);
        Assert.Equal(1.5m, result.Salary.RankMultiplier);
        Assert.Equal(30.0m, result.Salary.EffectiveHourlyRate); // 10 * 2.0 * 1.5
        Assert.Equal(lastPayout, result.Salary.LastSalaryPayoutAt);
        Assert.Equal(lastPayout.AddHours(1), result.Salary.NextEligibleAt);
    }

    [Fact]
    public async Task GetAsync_EffectivePermissionsUnexpectedlyNull_FallsBackToEmptyRatherThanThrowing()
    {
        var account = new UserDto { Id = 1, Username = "alice" };
        _mockUserService.Setup(s => s.GetByIdAsync(1)).ReturnsAsync(account);
        _mockPermissionResolutionService.Setup(s => s.GetEffectiveAsync(1))
            .ReturnsAsync((PermissionEffectiveResponseDto?)null);
        _mockUserPermissionGroupService.Setup(s => s.GetByUserAsync(1))
            .ReturnsAsync(new List<UserPermissionGroupDto>());
        _mockTitleService.Setup(s => s.ResolveAsync(It.IsAny<int>(), It.IsAny<Gender?>())).ReturnsAsync(new TitleResolutionDto());
        _mockSalaryService.Setup(s => s.GetCurrentRankMultiplierAsync(1)).ReturnsAsync(1.0m);
        _mockSalaryConfigurationService.Setup(s => s.GetAsync())
            .ReturnsAsync(new SalaryConfigurationDto { GlobalMultiplier = 1.0m });

        var result = await _service.GetAsync(1);

        Assert.NotNull(result);
        Assert.NotNull(result!.Permissions);
        Assert.Equal(1, result.Permissions.UserId);
        Assert.Empty(result.Permissions.Permissions);
    }
}
