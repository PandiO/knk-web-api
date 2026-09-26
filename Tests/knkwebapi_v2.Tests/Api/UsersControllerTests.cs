using System.Security.Claims;
using Xunit;
using Moq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Models;
using knkwebapi_v2.Dtos;
using AutoMapper;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// API endpoint tests for UsersController.
/// Tests HTTP response codes, error handling, and request/response contracts.
/// </summary>
[Trait("Category", "API")]
public class UsersControllerTests
{
    private readonly Mock<IUserService> _mockUserService;
    private readonly Mock<IMapper> _mockMapper;
    private readonly Mock<IPermissionResolutionService> _mockPermissionResolutionService;
    private readonly Mock<ISalaryService> _mockSalaryService;
    private readonly Mock<IUserProfileSummaryService> _mockProfileSummaryService;
    private readonly Mock<IUserPermissionGroupService> _mockMembershipService;
    private readonly Mock<IPermissionGrantService> _mockGrantService;
    private readonly UsersController _controller;

    public UsersControllerTests()
    {
        _mockUserService = new Mock<IUserService>();
        _mockMapper = new Mock<IMapper>();
        _mockPermissionResolutionService = new Mock<IPermissionResolutionService>();
        _mockSalaryService = new Mock<ISalaryService>();
        _mockProfileSummaryService = new Mock<IUserProfileSummaryService>();
        _mockMembershipService = new Mock<IUserPermissionGroupService>();
        _mockGrantService = new Mock<IPermissionGrantService>();
        _controller = new UsersController(_mockUserService.Object, _mockMapper.Object, _mockPermissionResolutionService.Object, _mockSalaryService.Object, _mockProfileSummaryService.Object, _mockMembershipService.Object, _mockGrantService.Object);
    }

    #region Create Tests

    [Fact]
    public async Task Create_WithDuplicateUsername_Returns409Conflict()
    {
        // Arrange
        var createDto = new UserCreateDto
        {
            Username = "existingplayer",
            Email = "new@example.com",
            Password = "SecurePass123!",
            PasswordConfirmation = "SecurePass123!"
        };

        // ValidateUserCreationAsync mock without expression tree (optional param issue)
        _mockUserService
            .Setup(s => s.ValidateUserCreationAsync(It.IsAny<UserCreateDto>(), It.IsAny<int?>()))
            .ReturnsAsync((true, null));

        _mockUserService
            .Setup(s => s.CheckUsernameTakenAsync("existingplayer", null))
            .ReturnsAsync((true, 5));

        // Act
        var result = await _controller.Create(createDto);

        // Assert
        var conflictResult = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(409, conflictResult.StatusCode);
    }

    [Fact]
    public async Task Create_WithDuplicateEmail_Returns409Conflict()
    {
        // Arrange
        var createDto = new UserCreateDto
        {
            Username = "newplayer",
            Email = "existing@example.com",
            Password = "SecurePass123!",
            PasswordConfirmation = "SecurePass123!"
        };

        // ValidateUserCreationAsync mock without expression tree (optional param issue)
        _mockUserService
            .Setup(s => s.ValidateUserCreationAsync(It.IsAny<UserCreateDto>(), It.IsAny<int?>()))
            .ReturnsAsync((true, null));

        _mockUserService
            .Setup(s => s.CheckUsernameTakenAsync("newplayer", null))
            .ReturnsAsync((false, null));

        _mockUserService
            .Setup(s => s.CheckEmailTakenAsync("existing@example.com", null))
            .ReturnsAsync((true, 7));

        // Act
        var result = await _controller.Create(createDto);

        // Assert
        var conflictResult = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(409, conflictResult.StatusCode);
    }

    [Fact]
    public async Task Create_WithValidationFailure_Returns400BadRequest()
    {
        // Arrange
        var createDto = new UserCreateDto
        {
            Username = string.Empty,
            Email = "test@example.com",
            Password = "weak",
            PasswordConfirmation = "weak"
        };

        // ValidateUserCreationAsync mock without expression tree (optional param issue)
        _mockUserService
            .Setup(s => s.ValidateUserCreationAsync(It.IsAny<UserCreateDto>(), It.IsAny<int?>()))
            .ReturnsAsync((false, "Username is required"));

        // Act
        var result = await _controller.Create(createDto);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequestResult.StatusCode);
    }

    #endregion

    #region GenerateLinkCode Tests

    [Fact]
    public async Task GenerateLinkCode_WithAuthenticatedUser_Returns200WithCode()
    {
        // Arrange
        var userId = 1;
        var linkCodeDto = new LinkCodeResponseDto
        {
            Code = "ABC12XYZ",
            ExpiresAt = DateTime.UtcNow.AddMinutes(20)
        };

        // Setup authenticated user claims
        var claims = new List<Claim>
        {
            new Claim("uid", userId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "TestAuthType");
        var claimsPrincipal = new ClaimsPrincipal(identity);
        
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claimsPrincipal }
        };

        _mockUserService
            .Setup(s => s.GetByIdAsync(userId))
            .ReturnsAsync(new UserDto { Id = userId, Username = "player" });

        _mockUserService
            .Setup(s => s.GenerateLinkCodeAsync(userId))
            .ReturnsAsync(linkCodeDto);

        // Act
        var result = await _controller.GenerateLinkCode();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);
        Assert.NotNull(okResult.Value);
    }

    #endregion

    #region ValidateLinkCode Tests

    [Fact]
    public async Task ValidateLinkCode_WithValidCode_Returns200WithDetails()
    {
        // Arrange
        const string code = "ABC12XYZ";
        var userDto = new UserDto
        {
            Id = 1,
            Username = "player",
            Email = "player@example.com"
        };

        _mockUserService
            .Setup(s => s.ConsumeLinkCodeAsync(code))
            .ReturnsAsync((true, userDto));

        // Act
        var result = await _controller.ValidateLinkCode(code);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);
    }

    [Fact]
    public async Task ValidateLinkCode_WithExpiredCode_Returns200Invalid()
    {
        // Arrange
        const string code = "EXPIRED99";

        _mockUserService
            .Setup(s => s.ConsumeLinkCodeAsync(code))
            .ReturnsAsync((false, null));

        // Act
        var result = await _controller.ValidateLinkCode(code);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);
    }

    #endregion

    #region ChangePassword Tests

    [Fact]
    public async Task ChangePassword_WithCorrectCurrentPassword_Returns204NoContent()
    {
        // Arrange
        const int userId = 1;
        var changePasswordDto = new ChangePasswordDto
        {
            CurrentPassword = "OldPassword123!",
            NewPassword = "NewPassword456!",
            PasswordConfirmation = "NewPassword456!"
        };

        _mockUserService
            .Setup(s => s.GetByIdAsync(userId))
            .ReturnsAsync(new UserDto { Id = userId, Username = "player" });

        _mockUserService
            .Setup(s => s.ChangePasswordAsync(userId, "OldPassword123!", "NewPassword456!", "NewPassword456!"))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.ChangePassword(userId, changePasswordDto);

        // Assert
        var noContentResult = Assert.IsType<NoContentResult>(result);
        Assert.Equal(204, noContentResult.StatusCode);
    }

    #endregion

    #region UpdateEmail Tests

    [Fact]
    public async Task UpdateEmail_WithValidEmail_Returns204NoContent()
    {
        // Arrange
        const int userId = 1;
        var updateEmailDto = new UpdateEmailDto
        {
            NewEmail = "newemail@example.com",
            CurrentPassword = "MyPassword123!"
        };

        _mockUserService
            .Setup(s => s.GetByIdAsync(userId))
            .ReturnsAsync(new UserDto { Id = userId, Username = "player" });

        _mockUserService
            .Setup(s => s.CheckEmailTakenAsync("newemail@example.com", userId))
            .ReturnsAsync((false, null));

        _mockUserService
            .Setup(s => s.UpdateEmailAsync(userId, "newemail@example.com", "MyPassword123!"))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.UpdateEmail(userId, updateEmailDto);

        // Assert
        var noContentResult = Assert.IsType<NoContentResult>(result);
        Assert.Equal(204, noContentResult.StatusCode);
    }

    #endregion

    #region CheckDuplicate Tests

    [Fact]
    public async Task CheckDuplicate_WithNoDuplicate_Returns200False()
    {
        // Arrange
        var checkDto = new DuplicateCheckDto
        {
            Uuid = "550e8400-e29b-41d4-a716-446655440000",
            Username = "newplayer"
        };

        _mockUserService
            .Setup(s => s.CheckForDuplicateAsync("550e8400-e29b-41d4-a716-446655440000", "newplayer"))
            .ReturnsAsync((false, null));

        // Act
        var result = await _controller.CheckDuplicate(checkDto);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);
    }

    [Fact]
    public async Task CheckDuplicate_WithDuplicate_Returns200WithDetails()
    {
        // Arrange
        var checkDto = new DuplicateCheckDto
        {
            Uuid = "550e8400-e29b-41d4-a716-446655440000",
            Username = "existingplayer"
        };

        _mockUserService
            .Setup(s => s.CheckForDuplicateAsync("550e8400-e29b-41d4-a716-446655440000", "existingplayer"))
            .ReturnsAsync((true, 42));

        _mockUserService
            .Setup(s => s.GetByUuidAsync("550e8400-e29b-41d4-a716-446655440000"))
            .ReturnsAsync(new UserDto { Id = 1, Username = "existingplayer", Uuid = "550e8400-e29b-41d4-a716-446655440000" });

        _mockUserService
            .Setup(s => s.GetByIdAsync(42))
            .ReturnsAsync(new UserDto { Id = 42, Username = "conflicting" });

        // Act
        var result = await _controller.CheckDuplicate(checkDto);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);
    }

    #endregion

    #region MergeAccounts Tests

    [Fact]
    public async Task MergeAccounts_WithValidAccounts_Returns200WithMergedUser()
    {
        // Arrange
        var mergeDto = new AccountMergeDto
        {
            PrimaryUserId = 1,
            SecondaryUserId = 2
        };

        var mergedUser = new UserDto
        {
            Id = 1,
            Username = "primary",
            Coins = 500,
            Gems = 100,
            ExperiencePoints = 5000
        };

        _mockUserService
            .Setup(s => s.GetByIdAsync(1))
            .ReturnsAsync(new UserDto { Id = 1, Username = "primary" });

        _mockUserService
            .Setup(s => s.GetByIdAsync(2))
            .ReturnsAsync(new UserDto { Id = 2, Username = "secondary" });

        _mockUserService
            .Setup(s => s.MergeAccountsAsync(1, 2))
            .ReturnsAsync(mergedUser);

        // Act
        var result = await _controller.MergeAccounts(mergeDto);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);
    }

    #endregion

    #region LinkAccount Tests

    [Fact]
    public async Task LinkAccount_WithValidCode_Returns200LinkedUser()
    {
        // Arrange
        var linkAccountDto = new LinkAccountDto
        {
            LinkCode = "ABC12XYZ",
            Email = "player@example.com",
            Password = "SecurePass123!",
            PasswordConfirmation = "SecurePass123!"
        };

        var linkedUser = new UserDto
        {
            Id = 1,
            Username = "player",
            Email = "player@example.com"
        };

        _mockUserService
            .Setup(s => s.ValidateLinkCodeAsync("ABC12XYZ"))
            .ReturnsAsync((true, linkedUser));

        _mockUserService
            .Setup(s => s.ValidatePasswordAsync("SecurePass123!"))
            .ReturnsAsync((true, null));

        _mockUserService
            .Setup(s => s.CheckEmailTakenAsync("player@example.com", 1))
            .ReturnsAsync((false, null));

        _mockUserService
            .Setup(s => s.UpdateEmailAsync(1, "player@example.com", null))
            .Returns(Task.CompletedTask);

        _mockUserService
            .Setup(s => s.ChangePasswordAsync(1, "", "SecurePass123!", "SecurePass123!"))
            .Returns(Task.CompletedTask);

        _mockUserService
            .Setup(s => s.ConsumeLinkCodeAsync("ABC12XYZ"))
            .ReturnsAsync((true, linkedUser));

        _mockUserService
            .Setup(s => s.GetByIdAsync(1))
            .ReturnsAsync(linkedUser);

        // Act
        var result = await _controller.LinkAccount(linkAccountDto);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);
    }

    [Fact]
    public async Task LinkAccount_WithInvalidCode_Returns400BadRequest()
    {
        // Arrange
        var linkAccountDto = new LinkAccountDto
        {
            LinkCode = "INVALID99",
            Email = "player@example.com",
            Password = "SecurePass123!",
            PasswordConfirmation = "SecurePass123!"
        };

        _mockUserService
            .Setup(s => s.ValidateLinkCodeAsync("INVALID99"))
            .ReturnsAsync((false, null));

        // Act
        var result = await _controller.LinkAccount(linkAccountDto);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequestResult.StatusCode);
    }

    [Fact]
    public async Task LinkAccount_WithPasswordMismatch_Returns400BadRequest()
    {
        // Arrange
        var linkAccountDto = new LinkAccountDto
        {
            LinkCode = "ABC12XYZ",
            Email = "player@example.com",
            Password = "SecurePass123!",
            PasswordConfirmation = "DifferentPass123!"
        };

        var linkedUser = new UserDto
        {
            Id = 1,
            Username = "player",
            Email = "player@example.com"
        };

        _mockUserService
            .Setup(s => s.ValidateLinkCodeAsync("ABC12XYZ"))
            .ReturnsAsync((true, linkedUser));

        // Act
        var result = await _controller.LinkAccount(linkAccountDto);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequestResult.StatusCode);
    }

    #endregion

    #region GetProfileSummary Tests

    [Fact]
    public async Task GetProfileSummary_UnknownUser_Returns404()
    {
        _mockProfileSummaryService.Setup(s => s.GetAsync(999)).ReturnsAsync((UserProfileSummaryDto?)null);

        var result = await _controller.GetProfileSummary(999);

        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(404, notFoundResult.StatusCode);
    }

    [Fact]
    public async Task GetProfileSummary_KnownUser_Returns200WithSummary()
    {
        var summary = new UserProfileSummaryDto
        {
            Account = new UserDto { Id = 1, Username = "alice" },
            Permissions = new PermissionEffectiveResponseDto { UserId = 1 },
            Title = new TitleResolutionDto(),
            Salary = new SalaryStateDto()
        };
        _mockProfileSummaryService.Setup(s => s.GetAsync(1)).ReturnsAsync(summary);

        var result = await _controller.GetProfileSummary(1);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Same(summary, okResult.Value);
    }

    #endregion

    [Fact]
    public async Task GetUserSummaryByUuid_IncludesGender()
    {
        _mockUserService.Setup(s => s.GetByUuidAsync("uuid-1"))
            .ReturnsAsync(new UserDto { Id = 3, Username = "Lady", Uuid = "uuid-1", Gender = Gender.Female });

        var result = await _controller.GetUserSummaryByUuid("uuid-1");

        var dto = Assert.IsType<UserSummaryDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(Gender.Female, dto.Gender);
    }

    #region Acting user (plugin X-Acting-User-Id header) and balance nodes (KNG-22)

    private void SetRequest(ClaimsPrincipal? user = null, string? actingUserId = null, string? apiKey = null, string? configuredKey = null,
        bool development = false, bool allowUnauthenticated = false)
    {
        var configuration = new Mock<Microsoft.Extensions.Configuration.IConfiguration>();
        configuration.Setup(c => c["Security:PluginApiKey"]).Returns(configuredKey);
        configuration.Setup(c => c["Security:AllowUnauthenticatedPluginCalls"]).Returns(allowUnauthenticated ? "true" : null);
        var environment = new Mock<Microsoft.Extensions.Hosting.IHostEnvironment>();
        environment.Setup(e => e.EnvironmentName).Returns(development ? "Development" : "Production");
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(Microsoft.Extensions.Configuration.IConfiguration))).Returns(configuration.Object);
        services.Setup(s => s.GetService(typeof(Microsoft.Extensions.Hosting.IHostEnvironment))).Returns(environment.Object);

        var httpContext = new DefaultHttpContext { RequestServices = services.Object };
        if (user != null) httpContext.User = user;
        if (actingUserId != null) httpContext.Request.Headers[UsersController.ActingUserHeader] = actingUserId;
        if (apiKey != null) httpContext.Request.Headers[UsersController.PluginApiKeyHeader] = apiKey;
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        _mockUserService.Setup(s => s.AdjustBalancesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<bool>()))
            .ReturnsAsync(new BalanceAdjustmentResultDto());
    }

    private static ClaimsPrincipal LoggedIn(int userId) =>
        new(new ClaimsIdentity(new[] { new Claim("uid", userId.ToString()) }, "Bearer"));

    private void Holds(int userId, string node, bool granted) =>
        _mockPermissionResolutionService.Setup(p => p.CheckAsync(userId, node))
            .ReturnsAsync(new PermissionCheckResponseDto
            {
                UserId = userId,
                Node = node,
                Result = granted ? PermissionResolutionResult.Granted : PermissionResolutionResult.Denied
            });

    private Task<IActionResult> AdjustCoins() =>
        _controller.AdjustBalances(7, new AdjustBalancesDto { CoinsDelta = 500, Reason = "event prize" });

    private void VerifyActor(int? actor) =>
        _mockUserService.Verify(s => s.AdjustBalancesAsync(7, 500, 0, 0, "event prize", It.IsAny<string?>(), actor, It.IsAny<bool>()), Times.Once);

    private void VerifyNotAdjusted() =>
        _mockUserService.Verify(s => s.AdjustBalancesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Never);

    [Fact]
    public async Task AdjustBalances_NoKeyConfigured_AnonymousCallIsRefused()
    {
        // KNG-22: before, an anonymous call with this header was trusted whenever the key was
        // unset (the shipped default), so anyone could mint coins and blame a staff member.
        SetRequest(actingUserId: "42");

        var result = await AdjustCoins();

        Assert.IsType<UnauthorizedObjectResult>(result);
        VerifyNotAdjusted();
    }

    [Fact]
    public async Task AdjustBalances_NoKeyButDevelopmentOptOut_TrustsTheHeader()
    {
        SetRequest(actingUserId: "42", development: true, allowUnauthenticated: true);

        await AdjustCoins();

        VerifyActor(42);
    }

    [Fact]
    public async Task AdjustBalances_OptOutOutsideDevelopment_IsIgnored()
    {
        SetRequest(actingUserId: "42", development: false, allowUnauthenticated: true);

        Assert.IsType<UnauthorizedObjectResult>(await AdjustCoins());
        VerifyNotAdjusted();
    }

    [Fact]
    public async Task AdjustBalances_LoggedInCaller_IsTheActorEvenIfTheHeaderNamesSomeoneElse()
    {
        Holds(5, "knk.admin.user.coins", granted: true);
        SetRequest(user: LoggedIn(5), actingUserId: "42", apiKey: "secret", configuredKey: "secret");

        await AdjustCoins();

        VerifyActor(5);
    }

    [Fact]
    public async Task AdjustBalances_LoggedInWithoutTheCoinsNode_IsForbidden()
    {
        Holds(5, "knk.admin.user.coins", granted: false);
        SetRequest(user: LoggedIn(5));

        var result = await AdjustCoins();

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
        VerifyNotAdjusted();
    }

    [Fact]
    public async Task AdjustBalances_LoggedIn_NeedsTheNodeOfEveryChangedBalance()
    {
        Holds(5, "knk.admin.user.coins", granted: true);
        Holds(5, "knk.admin.user.gems", granted: false);
        SetRequest(user: LoggedIn(5));

        var result = await _controller.AdjustBalances(7, new AdjustBalancesDto { CoinsDelta = 1, GemsDelta = 1, Reason = "x" });

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
        VerifyNotAdjusted();
    }

    [Fact]
    public async Task AdjustBalances_PluginKeyConfiguredButMissing_IsRefused()
    {
        SetRequest(actingUserId: "42", configuredKey: "secret");

        Assert.IsType<UnauthorizedObjectResult>(await AdjustCoins());
        VerifyNotAdjusted();
    }

    [Fact]
    public async Task AdjustBalances_PluginKeyConfiguredAndSent_TrustsTheHeader()
    {
        SetRequest(actingUserId: "42", apiKey: "secret", configuredKey: "secret");

        await AdjustCoins();

        VerifyActor(42);
    }

    [Fact]
    public async Task AdjustBalances_WrongPluginKey_IsRefused()
    {
        SetRequest(actingUserId: "42", apiKey: "guess", configuredKey: "secret");

        Assert.IsType<UnauthorizedObjectResult>(await AdjustCoins());
        VerifyNotAdjusted();
    }

    [Fact]
    public async Task AdjustBalances_PluginKeyWithoutUsableHeader_IsSystem()
    {
        SetRequest(actingUserId: "not-a-number", apiKey: "secret", configuredKey: "secret");

        await AdjustCoins();

        VerifyActor(null);
    }

    [Fact]
    public async Task AdjustBalances_CapExceeded_Returns400WithItsOwnCode()
    {
        SetRequest(apiKey: "secret", configuredKey: "secret");
        _mockUserService.Setup(s => s.AdjustBalancesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<bool>()))
            .ThrowsAsync(new BalanceCapExceededException("coins", 999_999_000, 5_000, BalanceLimits.MaxCoins));

        var result = Assert.IsType<BadRequestObjectResult>(await AdjustCoins());

        Assert.Contains("BalanceCapExceeded", System.Text.Json.JsonSerializer.Serialize(result.Value));
    }

    [Fact]
    public async Task GenerateLinkCode_ForAnotherUserId_WithoutThePluginKey_IsRefused()
    {
        // A link code lets its holder set the account's email and password (link-account).
        SetRequest(configuredKey: "secret");

        var result = await _controller.GenerateLinkCode(new GenerateLinkCodeRequestDto { UserId = 1 });

        Assert.IsType<UnauthorizedObjectResult>(result);
        _mockUserService.Verify(s => s.GenerateLinkCodeAsync(It.IsAny<int?>()), Times.Never);
    }

    #endregion
}
