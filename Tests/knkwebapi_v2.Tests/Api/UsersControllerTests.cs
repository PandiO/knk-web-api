using System.Security.Claims;
using Xunit;
using Moq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Services;
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
    private readonly UsersController _controller;

    public UsersControllerTests()
    {
        _mockUserService = new Mock<IUserService>();
        _mockMapper = new Mock<IMapper>();
        _mockPermissionResolutionService = new Mock<IPermissionResolutionService>();
        _controller = new UsersController(_mockUserService.Object, _mockMapper.Object, _mockPermissionResolutionService.Object);
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
}
