using System;
using System.Collections.Generic;
using Xunit;
using Moq;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Models;
using knkwebapi_v2.Dtos;
using AutoMapper;
using knkwebapi_v2.Repositories;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Unit tests for UserService business logic.
/// Tests validation, password management, duplicate checking, and account merging.
/// </summary>
public class UserServiceTests
{
    private readonly Mock<IUserRepository> _mockUserRepository;
    private readonly Mock<IPasswordService> _mockPasswordService;
    private readonly Mock<ILinkCodeService> _mockLinkCodeService;
    private readonly Mock<IMapper> _mockMapper;
    private readonly Mock<ITitleService> _mockTitleService;
    private readonly Mock<IUserPermissionGroupService> _mockMembershipService;
    private readonly Mock<IAuditLogService> _mockAuditLogService;
    private readonly UserService _userService;

    public UserServiceTests()
    {
        _mockUserRepository = new Mock<IUserRepository>();
        _mockPasswordService = new Mock<IPasswordService>();
        _mockLinkCodeService = new Mock<ILinkCodeService>();
        _mockMapper = new Mock<IMapper>();
        _mockTitleService = new Mock<ITitleService>();
        _mockTitleService
            .Setup(s => s.ResolveAsync(It.IsAny<int>()))
            .ReturnsAsync(new TitleResolutionDto());
        _mockMembershipService = new Mock<IUserPermissionGroupService>();
        _mockAuditLogService = new Mock<IAuditLogService>();

        _userService = new UserService(
            _mockUserRepository.Object,
            _mockMapper.Object,
            _mockPasswordService.Object,
            _mockLinkCodeService.Object,
            _mockTitleService.Object,
            _mockMembershipService.Object,
            _mockAuditLogService.Object
        );
    }

    #region ValidateUserCreationAsync Tests

    [Fact]
    public async Task ValidateUserCreationAsync_WithValidWebAppSignup_ReturnsValid()
    {
        // Arrange
        var dto = new UserCreateDto
        {
            Username = "player123",
            Email = "player@example.com",
            Password = "SecurePass123!",
            PasswordConfirmation = "SecurePass123!"
        };

        _mockPasswordService
            .Setup(p => p.ValidatePasswordAsync(It.IsAny<string>()))
            .ReturnsAsync((true, null));

        // Act
        var result = await _userService.ValidateUserCreationAsync(dto);

        // Assert
        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateUserCreationAsync_WithMissingUsername_ReturnsFalse()
    {
        // Arrange
        var dto = new UserCreateDto
        {
            Username = string.Empty,
            Email = "player@example.com",
            Password = "SecurePass123!",
            PasswordConfirmation = "SecurePass123!"
        };

        _mockPasswordService
            .Setup(p => p.ValidatePasswordAsync(It.IsAny<string>()))
            .ReturnsAsync((true, null));

        // Act
        var result = await _userService.ValidateUserCreationAsync(dto);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("username", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateUserCreationAsync_WithPasswordMismatch_ReturnsFalse()
    {
        // Arrange
        var dto = new UserCreateDto
        {
            Username = "player123",
            Email = "player@example.com",
            Password = "SecurePass123!",
            PasswordConfirmation = "DifferentPass123!"
        };

        _mockPasswordService
            .Setup(p => p.ValidatePasswordAsync(It.IsAny<string>()))
            .ReturnsAsync((true, null));

        // Act
        var result = await _userService.ValidateUserCreationAsync(dto);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("password", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateUserCreationAsync_WithInvalidPassword_ReturnsFalse()
    {
        // Arrange
        var dto = new UserCreateDto
        {
            Username = "player123",
            Email = "player@example.com",
            Password = "weak",
            PasswordConfirmation = "weak"
        };

        _mockPasswordService
            .Setup(p => p.ValidatePasswordAsync("weak"))
            .ReturnsAsync((false, "Password must be at least 8 characters long."));

        // Act
        var result = await _userService.ValidateUserCreationAsync(dto);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateUserCreationAsync_WithMinecraftOnlySignup_ReturnsValid()
    {
        // Arrange
        var dto = new UserCreateDto
        {
            Username = "player123",
            Uuid = "550e8400-e29b-41d4-a716-446655440000"
        };

        // Act
        var result = await _userService.ValidateUserCreationAsync(dto);

        // Assert
        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    #endregion

    #region ValidatePasswordAsync Tests

    [Fact]
    public async Task ValidatePasswordAsync_WithValidPassword_ReturnsTrue()
    {
        // Arrange
        const string validPassword = "SecurePassword123!";

        _mockPasswordService
            .Setup(p => p.ValidatePasswordAsync(validPassword))
            .ReturnsAsync((true, null));

        // Act
        var result = await _userService.ValidatePasswordAsync(validPassword);

        // Assert
        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task ValidatePasswordAsync_WithWeakPassword_ReturnsFalse()
    {
        // Arrange
        const string weakPassword = "password";

        _mockPasswordService
            .Setup(p => p.ValidatePasswordAsync(weakPassword))
            .ReturnsAsync((false, "This password is too common."));

        // Act
        var result = await _userService.ValidatePasswordAsync(weakPassword);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
    }

    #endregion

    #region CheckUsernameTakenAsync Tests

    [Fact]
    public async Task CheckUsernameTakenAsync_WithExistingUsername_ReturnsTrueWithUserId()
    {
        // Arrange
        const string username = "existingplayer";
        const int existingUserId = 42;

        _mockUserRepository
            .Setup(r => r.IsUsernameTakenAsync(username, null))
            .ReturnsAsync(true);

        _mockUserRepository
            .Setup(r => r.GetByUsernameAsync(username))
            .ReturnsAsync(new User { Id = existingUserId, Username = username });

        // Act
        var result = await _userService.CheckUsernameTakenAsync(username);

        // Assert
        Assert.True(result.IsTaken);
        Assert.Equal(existingUserId, result.ConflictingUserId);
    }

    [Fact]
    public async Task CheckUsernameTakenAsync_WithNewUsername_ReturnsFalse()
    {
        // Arrange
        const string username = "newplayer";

        _mockUserRepository
            .Setup(r => r.IsUsernameTakenAsync(username, null))
            .ReturnsAsync(false);

        // Act
        var result = await _userService.CheckUsernameTakenAsync(username);

        // Assert
        Assert.False(result.IsTaken);
        Assert.Null(result.ConflictingUserId);
    }

    [Fact]
    public async Task CheckUsernameTakenAsync_ExcludesCurrentUser()
    {
        // Arrange
        const string username = "player";
        const int userId = 5;
        const int otherUserId = 10;

        _mockUserRepository
            .Setup(r => r.IsUsernameTakenAsync(username, userId))
            .ReturnsAsync(false);

        // Act
        var result = await _userService.CheckUsernameTakenAsync(username, userId);

        // Assert
        Assert.False(result.IsTaken);
        _mockUserRepository.Verify(r => r.IsUsernameTakenAsync(username, userId), Times.Once);
    }

    #endregion

    #region CheckEmailTakenAsync Tests

    [Fact]
    public async Task CheckEmailTakenAsync_WithExistingEmail_ReturnsTrueWithUserId()
    {
        // Arrange
        const string email = "player@example.com";
        const int existingUserId = 7;

        _mockUserRepository
            .Setup(r => r.IsEmailTakenAsync(email, null))
            .ReturnsAsync(true);

        _mockUserRepository
            .Setup(r => r.GetByEmailAsync(email))
            .ReturnsAsync(new User { Id = existingUserId, Email = email });

        // Act
        var result = await _userService.CheckEmailTakenAsync(email);

        // Assert
        Assert.True(result.IsTaken);
        Assert.Equal(existingUserId, result.ConflictingUserId);
    }

    [Fact]
    public async Task CheckEmailTakenAsync_WithNewEmail_ReturnsFalse()
    {
        // Arrange
        const string email = "newemail@example.com";

        _mockUserRepository
            .Setup(r => r.IsEmailTakenAsync(email, null))
            .ReturnsAsync(false);

        // Act
        var result = await _userService.CheckEmailTakenAsync(email);

        // Assert
        Assert.False(result.IsTaken);
        Assert.Null(result.ConflictingUserId);
    }

    #endregion

    #region CheckUuidTakenAsync Tests

    [Fact]
    public async Task CheckUuidTakenAsync_WithExistingUuid_ReturnsTrueWithUserId()
    {
        // Arrange
        const string uuid = "550e8400-e29b-41d4-a716-446655440000";
        const int existingUserId = 12;

        _mockUserRepository
            .Setup(r => r.IsUuidTakenAsync(uuid, null))
            .ReturnsAsync(true);

        _mockUserRepository
            .Setup(r => r.GetByUuidAsync(uuid))
            .ReturnsAsync(new User { Id = existingUserId, Uuid = uuid });

        // Act
        var result = await _userService.CheckUuidTakenAsync(uuid);

        // Assert
        Assert.True(result.IsTaken);
        Assert.Equal(existingUserId, result.ConflictingUserId);
    }

    [Fact]
    public async Task CheckUuidTakenAsync_WithNewUuid_ReturnsFalse()
    {
        // Arrange
        const string uuid = "550e8400-e29b-41d4-a716-446655440001";

        _mockUserRepository
            .Setup(r => r.IsUuidTakenAsync(uuid, null))
            .ReturnsAsync(false);

        // Act
        var result = await _userService.CheckUuidTakenAsync(uuid);

        // Assert
        Assert.False(result.IsTaken);
        Assert.Null(result.ConflictingUserId);
    }

    #endregion

    #region ChangePasswordAsync Tests

    [Fact]
    public async Task ChangePasswordAsync_WithCorrectCurrentPassword_Succeeds()
    {
        // Arrange
        const int userId = 1;
        const string currentPassword = "OldPassword123!";
        const string newPassword = "NewPassword456!";
        const string passwordConfirmation = "NewPassword456!";
        const string passwordHash = "$2a$10$hashedpassword";
        const string newPasswordHash = "$2a$10$newhash";

        var user = new User
        {
            Id = userId,
            PasswordHash = passwordHash
        };

        _mockUserRepository
            .Setup(r => r.GetByIdAsync(userId))
            .ReturnsAsync(user);

        _mockPasswordService
            .Setup(p => p.VerifyPasswordAsync(currentPassword, passwordHash))
            .ReturnsAsync(true);

        _mockPasswordService
            .Setup(p => p.ValidatePasswordAsync(newPassword))
            .ReturnsAsync((true, null));

        _mockPasswordService
            .Setup(p => p.HashPasswordAsync(newPassword))
            .ReturnsAsync(newPasswordHash);

        _mockUserRepository
            .Setup(r => r.UpdatePasswordHashAsync(userId, newPasswordHash))
            .Returns(Task.CompletedTask);

        // Act & Assert
        await _userService.ChangePasswordAsync(userId, currentPassword, newPassword, passwordConfirmation);
        _mockUserRepository.Verify(r => r.UpdatePasswordHashAsync(userId, newPasswordHash), Times.Once);
    }

    [Fact]
    public async Task ChangePasswordAsync_WithIncorrectCurrentPassword_Throws()
    {
        // Arrange
        const int userId = 1;
        const string currentPassword = "WrongPassword";
        const string newPassword = "NewPassword456!";
        const string passwordConfirmation = "NewPassword456!";
        const string passwordHash = "$2a$10$hashedpassword";

        var user = new User
        {
            Id = userId,
            PasswordHash = passwordHash
        };

        _mockUserRepository
            .Setup(r => r.GetByIdAsync(userId))
            .ReturnsAsync(user);

        _mockPasswordService
            .Setup(p => p.VerifyPasswordAsync(currentPassword, passwordHash))
            .ReturnsAsync(false);

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _userService.ChangePasswordAsync(userId, currentPassword, newPassword, passwordConfirmation)
        );
    }

    [Fact]
    public async Task ChangePasswordAsync_WithPasswordMismatch_Throws()
    {
        // Arrange
        const int userId = 1;
        const string currentPassword = "OldPassword123!";
        const string newPassword = "NewPassword456!";
        const string passwordConfirmation = "DifferentPassword!";
        const string passwordHash = "$2a$10$hashedpassword";

        var user = new User
        {
            Id = userId,
            PasswordHash = passwordHash
        };

        _mockUserRepository
            .Setup(r => r.GetByIdAsync(userId))
            .ReturnsAsync(user);

        _mockPasswordService
            .Setup(p => p.VerifyPasswordAsync(currentPassword, passwordHash))
            .ReturnsAsync(true);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _userService.ChangePasswordAsync(userId, currentPassword, newPassword, passwordConfirmation)
        );
    }

    #endregion

    #region CheckForDuplicateAsync Tests

    [Fact]
    public async Task CheckForDuplicateAsync_WithNoDuplicate_ReturnsFalse()
    {
        // Arrange
        const string uuid = "550e8400-e29b-41d4-a716-446655440000";
        const string username = "newplayer";

        _mockUserRepository
            .Setup(r => r.FindDuplicateAsync(uuid, username))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _userService.CheckForDuplicateAsync(uuid, username);

        // Assert
        Assert.False(result.HasConflict);
        Assert.Null(result.SecondaryUserId);
    }

    [Fact]
    public async Task CheckForDuplicateAsync_WithDuplicate_ReturnsTrueWithUserId()
    {
        // Arrange
        const string uuid = "550e8400-e29b-41d4-a716-446655440000";
        const string username = "existingplayer";
        const int duplicateUserId = 99;

        var duplicateUser = new User
        {
            Id = duplicateUserId,
            Uuid = uuid,
            Username = username
        };

        _mockUserRepository
            .Setup(r => r.FindDuplicateAsync(uuid, username))
            .ReturnsAsync(duplicateUser);

        // Act
        var result = await _userService.CheckForDuplicateAsync(uuid, username);

        // Assert
        Assert.True(result.HasConflict);
        Assert.Equal(duplicateUserId, result.SecondaryUserId);
    }

    #endregion

    #region MergeAccountsAsync Tests

    [Fact]
    public async Task MergeAccountsAsync_WithValidAccounts_MergesSuccessfully()
    {
        // Arrange
        const int primaryUserId = 1;
        const int secondaryUserId = 2;

        var primaryUser = new User
        {
            Id = primaryUserId,
            Username = "primary",
            Email = "primary@example.com",
            Coins = 500,
            Gems = 100,
            ExperiencePoints = 5000,
            IsActive = true
        };

        var secondaryUser = new User
        {
            Id = secondaryUserId,
            Username = "secondary",
            Email = "secondary@example.com",
            Coins = 100,
            Gems = 50,
            ExperiencePoints = 1000,
            IsActive = true
        };

        var mergedUserDto = new UserDto
        {
            Id = primaryUserId,
            Username = "primary",
            Email = "primary@example.com",
            Coins = 500,
            Gems = 100,
            ExperiencePoints = 5000,
            EmailVerified = false
        };

        _mockUserRepository
            .Setup(r => r.GetByIdAsync(primaryUserId))
            .ReturnsAsync(primaryUser);

        _mockUserRepository
            .Setup(r => r.GetByIdAsync(secondaryUserId))
            .ReturnsAsync(secondaryUser);

        _mockUserRepository
            .Setup(r => r.MergeUsersAsync(primaryUserId, secondaryUserId))
            .Returns(Task.CompletedTask);

        _mockMapper
            .Setup(m => m.Map<UserDto>(primaryUser))
            .Returns(mergedUserDto);

        // Act
        var result = await _userService.MergeAccountsAsync(primaryUserId, secondaryUserId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(primaryUserId, result.Id);
        _mockUserRepository.Verify(r => r.MergeUsersAsync(primaryUserId, secondaryUserId), Times.Once);
    }

    [Fact]
    public async Task MergeAccountsAsync_WithNonExistentPrimary_Throws()
    {
        // Arrange
        const int primaryUserId = 1;
        const int secondaryUserId = 2;

        _mockUserRepository
            .Setup(r => r.GetByIdAsync(primaryUserId))
            .ReturnsAsync((User?)null);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _userService.MergeAccountsAsync(primaryUserId, secondaryUserId)
        );
    }

    [Fact]
    public async Task MergeAccountsAsync_WithNonExistentSecondary_Throws()
    {
        // Arrange
        const int primaryUserId = 1;
        const int secondaryUserId = 2;

        var primaryUser = new User { Id = primaryUserId };

        _mockUserRepository
            .Setup(r => r.GetByIdAsync(primaryUserId))
            .ReturnsAsync(primaryUser);

        _mockUserRepository
            .Setup(r => r.GetByIdAsync(secondaryUserId))
            .ReturnsAsync((User?)null);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _userService.MergeAccountsAsync(primaryUserId, secondaryUserId)
        );
    }

    #endregion

    #region Link Code Delegation Tests

    [Fact]
    public async Task GenerateLinkCodeAsync_DelegatestoLinkCodeService()
    {
        // Arrange
        const int userId = 1;
        var linkCodeResponseDto = new LinkCodeResponseDto
        {
            Code = "ABC12XYZ",
            ExpiresAt = DateTime.UtcNow.AddMinutes(20)
        };

        _mockLinkCodeService
            .Setup(s => s.GenerateLinkCodeAsync(userId))
            .ReturnsAsync(linkCodeResponseDto);

        _mockUserRepository
            .Setup(r => r.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId });

        // Act
        var result = await _userService.GenerateLinkCodeAsync(userId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("ABC12XYZ", result.Code);
        _mockLinkCodeService.Verify(s => s.GenerateLinkCodeAsync(userId), Times.Once);
    }

    [Fact]
    public async Task ConsumeLinkCodeAsync_DelegatestoLinkCodeService()
    {
        // Arrange
        const string code = "ABC12XYZ";
        var userDto = new UserDto { Id = 1, Username = "player" };

        var linkCode = new LinkCode { UserId = 1 };
        _mockLinkCodeService
            .Setup(s => s.ConsumeLinkCodeAsync(code))
            .ReturnsAsync((true, linkCode, null));

        _mockUserRepository
            .Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(new User { Id = 1, Username = "player" });

        _mockMapper
            .Setup(m => m.Map<UserDto>(It.IsAny<User>()))
            .Returns(userDto);

        // Act
        var result = await _userService.ConsumeLinkCodeAsync(code);

        // Assert
        Assert.True(result.IsValid);
        Assert.NotNull(result.User);
        _mockLinkCodeService.Verify(s => s.ConsumeLinkCodeAsync(code), Times.Once);
    }

    #endregion
    #region UpdateActiveModeAsync Tests (User features Phase 3, owner/staff mode)

    [Theory]
    [InlineData(ActiveMode.None)]
    [InlineData(ActiveMode.Staff)]
    [InlineData(ActiveMode.Owner)]
    public async Task UpdateActiveModeAsync_WithExistingUser_PersistsMode(ActiveMode mode)
    {
        // Arrange
        _mockUserRepository
            .Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(new User { Id = 1, Username = "player" });

        // Act
        await _userService.UpdateActiveModeAsync(1, mode);

        // Assert
        _mockUserRepository.Verify(r => r.UpdateActiveModeAsync(1, mode), Times.Once);
    }

    [Fact]
    public async Task UpdateActiveModeAsync_WithMissingUser_ThrowsKeyNotFound()
    {
        // Arrange
        _mockUserRepository
            .Setup(r => r.GetByIdAsync(99))
            .ReturnsAsync((User?)null);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _userService.UpdateActiveModeAsync(99, ActiveMode.Owner));
        _mockUserRepository.Verify(r => r.UpdateActiveModeAsync(It.IsAny<int>(), It.IsAny<ActiveMode>()), Times.Never);
    }

    [Fact]
    public async Task UpdateActiveModeAsync_WithUndefinedMode_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _userService.UpdateActiveModeAsync(1, (ActiveMode)7));
        _mockUserRepository.Verify(r => r.UpdateActiveModeAsync(It.IsAny<int>(), It.IsAny<ActiveMode>()), Times.Never);
    }

    [Fact]
    public async Task UpdateActiveModeAsync_WithInvalidId_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _userService.UpdateActiveModeAsync(0, ActiveMode.Staff));
    }

    [Fact]
    public async Task UpdateActiveModeAsync_ModeActuallyChanges_RecordsVanishToggledAuditEntry()
    {
        _mockUserRepository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new User { Id = 1, Username = "player", ActiveMode = ActiveMode.None });

        await _userService.UpdateActiveModeAsync(1, ActiveMode.Staff, actorUserId: 7);

        _mockAuditLogService.Verify(a => a.RecordAsync(7, 1, Enums.AuditAction.VanishToggled, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task UpdateActiveModeAsync_ModeUnchanged_DoesNotRecordAuditEntry()
    {
        _mockUserRepository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new User { Id = 1, Username = "player", ActiveMode = ActiveMode.Staff });

        await _userService.UpdateActiveModeAsync(1, ActiveMode.Staff);

        _mockAuditLogService.Verify(a => a.RecordAsync(It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<Enums.AuditAction>(), It.IsAny<string?>()), Times.Never);
    }

    #endregion

    #region AdjustBalancesAsync Audit Tests (user-management Phase 2 retrofit)

    [Fact]
    public async Task AdjustBalancesAsync_RecordsBalanceAdjustedAuditEntry()
    {
        _mockUserRepository.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(new User { Id = 1, Username = "player", Coins = 100, Gems = 0, ExperiencePoints = 0 });

        await _userService.AdjustBalancesAsync(1, coinsDelta: 50, gemsDelta: 0, experienceDelta: 0, reason: "test", actorUserId: 3);

        _mockAuditLogService.Verify(a => a.RecordAsync(3, 1, Enums.AuditAction.BalanceAdjusted, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task AdjustBalancesAsync_TitleBracketChanges_AlsoRecordsTitleChangedAuditEntry()
    {
        _mockUserRepository.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(new User { Id = 1, Username = "player", Coins = 0, Gems = 0, ExperiencePoints = 0 });
        _mockTitleService.SetupSequence(s => s.ResolveAsync(It.IsAny<int>()))
            .ReturnsAsync(new TitleResolutionDto { TitleBracketId = 1, TitleName = "Novice" })
            .ReturnsAsync(new TitleResolutionDto { TitleBracketId = 2, TitleName = "Apprentice" });

        await _userService.AdjustBalancesAsync(1, coinsDelta: 0, gemsDelta: 0, experienceDelta: 500, reason: "xp gain");

        _mockAuditLogService.Verify(a => a.RecordAsync(null, 1, Enums.AuditAction.BalanceAdjusted, It.IsAny<string?>()), Times.Once);
        _mockAuditLogService.Verify(a => a.RecordAsync(null, 1, Enums.AuditAction.TitleChanged, It.IsAny<string?>()), Times.Once);
    }

    #endregion
}
