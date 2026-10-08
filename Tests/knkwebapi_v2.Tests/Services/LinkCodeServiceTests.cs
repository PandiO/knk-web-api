using Xunit;
using Moq;
using knkwebapi_v2.Services;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Models;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Configuration;
using AutoMapper;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Unit tests for LinkCodeService.
/// Tests link code generation, validation, consumption, and cleanup.
/// </summary>
public class LinkCodeServiceTests
{
    private readonly Mock<ILinkCodeRepository> _mockLinkCodeRepository;
    private readonly Mock<IUserRepository> _mockUserRepository;
    private readonly Mock<IMapper> _mockMapper;
    private readonly Mock<IOptions<SecuritySettings>> _mockSecuritySettings;
    private readonly LinkCodeService _linkCodeService;

    public LinkCodeServiceTests()
    {
        _mockLinkCodeRepository = new Mock<ILinkCodeRepository>();
        _mockUserRepository = new Mock<IUserRepository>();
        _mockMapper = new Mock<IMapper>();
        _mockSecuritySettings = new Mock<IOptions<SecuritySettings>>();

        var securitySettings = new SecuritySettings
        {
            LinkCodeExpirationMinutes = 20,
            BcryptRounds = 10
        };

        _mockSecuritySettings
            .Setup(s => s.Value)
            .Returns(securitySettings);

        _linkCodeService = new LinkCodeService(
            _mockLinkCodeRepository.Object,
            _mockUserRepository.Object,
            _mockMapper.Object,
            _mockSecuritySettings.Object
        );
    }

    #region GenerateCodeAsync Tests

    [Fact]
    public async Task GenerateCodeAsync_GeneratesValidEightCharCode()
    {
        // Act
        var code = await _linkCodeService.GenerateCodeAsync();

        // Assert
        Assert.NotNull(code);
        Assert.Equal(8, code.Length);
        Assert.True(code.All(c => char.IsLetterOrDigit(c)), "Code should contain only alphanumeric characters");
    }

    [Fact]
    public async Task GenerateCodeAsync_GeneratesUniqueCode()
    {
        // Act
        var code1 = await _linkCodeService.GenerateCodeAsync();
        var code2 = await _linkCodeService.GenerateCodeAsync();
        var code3 = await _linkCodeService.GenerateCodeAsync();

        // Assert
        Assert.NotEqual(code1, code2);
        Assert.NotEqual(code2, code3);
        Assert.NotEqual(code1, code3);
    }

    [Fact]
    public async Task GenerateCodeAsync_GeneratesHighEntropyCode()
    {
        // Arrange - generate many codes and check for randomness
        var codes = new HashSet<string>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            var code = await _linkCodeService.GenerateCodeAsync();
            codes.Add(code);
        }

        // Assert - all should be unique (entropy check)
        Assert.Equal(100, codes.Count);
    }

    #endregion

    #region GenerateLinkCodeAsync Tests

    [Fact]
    public async Task GenerateLinkCodeAsync_WithValidUserId_ReturnsLinkCodeResponseDto()
    {
        // Arrange
        const int userId = 1;
        var user = new User { Id = userId, Username = "player" };

        var linkCode = new LinkCode
        {
            Id = 1,
            UserId = userId,
            Code = "ABC12XYZ",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(20),
            Status = LinkCodeStatus.Active,
            User = user
        };

        var linkCodeResponseDto = new LinkCodeResponseDto
        {
            Code = "ABC12XYZ",
            ExpiresAt = linkCode.ExpiresAt
        };

        _mockUserRepository
            .Setup(r => r.GetByIdAsync(userId))
            .ReturnsAsync(user);

        _mockLinkCodeRepository
            .Setup(r => r.CreateAsync(It.IsAny<LinkCode>()))
            .ReturnsAsync(linkCode);

        _mockMapper
            .Setup(m => m.Map<LinkCodeResponseDto>(It.IsAny<LinkCode>()))
            .Returns(linkCodeResponseDto);

        // Act
        var result = await _linkCodeService.GenerateLinkCodeAsync(userId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("ABC12XYZ", result.Code);
        Assert.NotEqual(default, result.ExpiresAt);
        _mockLinkCodeRepository.Verify(r => r.CreateAsync(It.IsAny<LinkCode>()), Times.Once);
    }

    [Fact]
    public async Task GenerateLinkCodeAsync_WithNullUserId_ReturnsLinkCodeResponseDto()
    {
        // Arrange - for web app first flow without user ID yet
        var linkCode = new LinkCode
        {
            Id = 1,
            UserId = null,
            Code = "ABC12XYZ",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(20),
            Status = LinkCodeStatus.Active,
            User = null
        };

        var linkCodeResponseDto = new LinkCodeResponseDto
        {
            Code = "ABC12XYZ",
            ExpiresAt = linkCode.ExpiresAt
        };

        _mockLinkCodeRepository
            .Setup(r => r.CreateAsync(It.IsAny<LinkCode>()))
            .ReturnsAsync(linkCode);

        _mockMapper
            .Setup(m => m.Map<LinkCodeResponseDto>(It.IsAny<LinkCode>()))
            .Returns(linkCodeResponseDto);

        // Act
        var result = await _linkCodeService.GenerateLinkCodeAsync(null);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("ABC12XYZ", result.Code);
    }

    [Fact]
    public async Task GenerateLinkCodeAsync_SetExpirationTo20Minutes()
    {
        // Arrange
        const int userId = 1;
        var user = new User { Id = userId };

        LinkCode? capturedLinkCode = null;

        _mockUserRepository
            .Setup(r => r.GetByIdAsync(userId))
            .ReturnsAsync(user);

        _mockLinkCodeRepository
            .Setup(r => r.CreateAsync(It.IsAny<LinkCode>()))
            .Callback<LinkCode>(lc => capturedLinkCode = lc)
            .ReturnsAsync((LinkCode lc) => lc);

        _mockMapper
            .Setup(m => m.Map<LinkCodeResponseDto>(It.IsAny<LinkCode>()))
            .Returns(new LinkCodeResponseDto { Code = "ABC12XYZ", ExpiresAt = DateTime.UtcNow.AddMinutes(20) });

        // Act
        await _linkCodeService.GenerateLinkCodeAsync(userId);

        // Assert
        Assert.NotNull(capturedLinkCode);
        var timeDiff = (capturedLinkCode.ExpiresAt - capturedLinkCode.CreatedAt).TotalMinutes;
        Assert.True(Math.Abs(timeDiff - 20) < 1, "Expiration should be 20 minutes from creation");
    }

    #endregion

    #region ValidateLinkCodeAsync Tests

    [Fact]
    public async Task ValidateLinkCodeAsync_WithValidActiveCode_ReturnsTrue()
    {
        // Arrange
        const string code = "ABC12XYZ";
        var linkCode = new LinkCode
        {
            Id = 1,
            UserId = 1,
            Code = code,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(20),
            Status = LinkCodeStatus.Active
        };

        _mockLinkCodeRepository
            .Setup(r => r.GetLinkCodeByCodeAsync(code))
            .ReturnsAsync(linkCode);

        // Act
        var result = await _linkCodeService.ValidateLinkCodeAsync(code);

        // Assert
        Assert.True(result.IsValid);
        Assert.NotNull(result.LinkCode);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ValidateLinkCodeAsync_WithExpiredCode_ReturnsFalse()
    {
        // Arrange
        const string code = "EXPIR001";
        var linkCode = new LinkCode
        {
            Id = 1,
            Code = code,
            CreatedAt = DateTime.UtcNow.AddMinutes(-30),
            ExpiresAt = DateTime.UtcNow.AddMinutes(-10),
            Status = LinkCodeStatus.Expired
        };

        _mockLinkCodeRepository
            .Setup(r => r.GetLinkCodeByCodeAsync(code))
            .ReturnsAsync(linkCode);

        // Act
        var result = await _linkCodeService.ValidateLinkCodeAsync(code);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
        Assert.Contains("expired", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateLinkCodeAsync_WithAlreadyUsedCode_ReturnsFalse()
    {
        // Arrange
        const string code = "USED12XY";
        var linkCode = new LinkCode
        {
            Id = 1,
            Code = code,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(20),
            Status = LinkCodeStatus.Used,
            UsedAt = DateTime.UtcNow.AddMinutes(-5)
        };

        _mockLinkCodeRepository
            .Setup(r => r.GetLinkCodeByCodeAsync(code))
            .ReturnsAsync(linkCode);

        // Act
        var result = await _linkCodeService.ValidateLinkCodeAsync(code);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
        Assert.Contains("already been used", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateLinkCodeAsync_WithNonExistentCode_ReturnsFalse()
    {
        // Arrange
        const string code = "NOEXIST1";

        _mockLinkCodeRepository
            .Setup(r => r.GetLinkCodeByCodeAsync(code))
            .ReturnsAsync((LinkCode?)null);

        // Act
        var result = await _linkCodeService.ValidateLinkCodeAsync(code);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region ConsumeLinkCodeAsync Tests

    [Fact]
    public async Task ConsumeLinkCodeAsync_WithValidCode_MarkAsUsedAndReturnUser()
    {
        // Arrange
        const string code = "ABC12XYZ";
        const int userId = 1;
        var user = new User { Id = userId, Username = "player" };

        var linkCode = new LinkCode
        {
            Id = 1,
            UserId = userId,
            Code = code,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(20),
            Status = LinkCodeStatus.Active,
            User = user
        };

        var userDto = new UserDto { Id = userId, Username = "player" };

        _mockLinkCodeRepository
            .Setup(r => r.GetLinkCodeByCodeAsync(code))
            .ReturnsAsync(linkCode);

        _mockLinkCodeRepository
            .Setup(r => r.TryMarkUsedAsync(1))
            .ReturnsAsync(true);

        _mockMapper
            .Setup(m => m.Map<UserDto>(user))
            .Returns(userDto);

        // Act
        var result = await _linkCodeService.ConsumeLinkCodeAsync(code);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.LinkCode);
        Assert.Null(result.Error);
        _mockLinkCodeRepository.Verify(r => r.TryMarkUsedAsync(1), Times.Once);
    }

    [Fact]
    public async Task ConsumeLinkCodeAsync_WhenAnotherRequestUsedItFirst_Fails()
    {
        // Two registrations race with one code: the conditional write lets only one through.
        const string code = "RACE0001";
        var linkCode = new LinkCode
        {
            Id = 7,
            UserId = 3,
            Code = code,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(20),
            Status = LinkCodeStatus.Active,
            User = new User { Id = 3, Username = "player" }
        };
        _mockLinkCodeRepository.Setup(r => r.GetLinkCodeByCodeAsync(code)).ReturnsAsync(linkCode);
        _mockLinkCodeRepository.Setup(r => r.TryMarkUsedAsync(7)).ReturnsAsync(false);

        var result = await _linkCodeService.ConsumeLinkCodeAsync(code);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ConsumeLinkCodeAsync_WithExpiredCode_FailsAndSetsStatusToExpired()
    {
        // Arrange
        const string code = "EXPIR001";
        var linkCode = new LinkCode
        {
            Id = 1,
            Code = code,
            CreatedAt = DateTime.UtcNow.AddMinutes(-30),
            ExpiresAt = DateTime.UtcNow.AddMinutes(-10),
            Status = LinkCodeStatus.Active
        };

        _mockLinkCodeRepository
            .Setup(r => r.GetLinkCodeByCodeAsync(code))
            .ReturnsAsync(linkCode);

        _mockLinkCodeRepository
            .Setup(r => r.UpdateLinkCodeStatusAsync(1, LinkCodeStatus.Expired))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _linkCodeService.ConsumeLinkCodeAsync(code);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        _mockLinkCodeRepository.Verify(r => r.UpdateLinkCodeStatusAsync(1, LinkCodeStatus.Expired), Times.Once);
    }

    [Fact]
    public async Task ConsumeLinkCodeAsync_WithAlreadyUsedCode_FailsWithoutMarkingAgain()
    {
        // Arrange
        const string code = "USED12XY";
        var linkCode = new LinkCode
        {
            Id = 1,
            Code = code,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(20),
            Status = LinkCodeStatus.Used,
            UsedAt = DateTime.UtcNow.AddMinutes(-5)
        };

        _mockLinkCodeRepository
            .Setup(r => r.GetLinkCodeByCodeAsync(code))
            .ReturnsAsync(linkCode);

        // Act
        var result = await _linkCodeService.ConsumeLinkCodeAsync(code);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        _mockLinkCodeRepository.Verify(r => r.UpdateLinkCodeStatusAsync(It.IsAny<int>(), It.IsAny<LinkCodeStatus>()), Times.Never);
    }

    #endregion

    #region GetExpiredCodesAsync Tests

    [Fact]
    public async Task GetExpiredCodesAsync_ReturnsOnlyExpiredCodes()
    {
        // Arrange
        var expiredCodes = new List<LinkCode>
        {
            new LinkCode
            {
                Id = 1,
                Code = "EXPIRED01",
                ExpiresAt = DateTime.UtcNow.AddMinutes(-10),
                Status = LinkCodeStatus.Active
            },
            new LinkCode
            {
                Id = 2,
                Code = "EXPIRED02",
                ExpiresAt = DateTime.UtcNow.AddMinutes(-30),
                Status = LinkCodeStatus.Active
            }
        };

        _mockLinkCodeRepository
            .Setup(r => r.GetExpiredLinkCodesAsync())
            .ReturnsAsync(expiredCodes);

        // Act
        var result = await _linkCodeService.GetExpiredCodesAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count());
        _mockLinkCodeRepository.Verify(r => r.GetExpiredLinkCodesAsync(), Times.Once);
    }

    [Fact]
    public async Task GetExpiredCodesAsync_ReturnsEmptyIfNoneExpired()
    {
        // Arrange
        _mockLinkCodeRepository
            .Setup(r => r.GetExpiredLinkCodesAsync())
            .ReturnsAsync(new List<LinkCode>());

        // Act
        var result = await _linkCodeService.GetExpiredCodesAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion

    #region CleanupExpiredCodesAsync Tests

    [Fact]
    public async Task CleanupExpiredCodesAsync_DeletesExpiredCodesAndReturnsCount()
    {
        // Arrange
        var expiredCodes = new List<LinkCode>
        {
            new LinkCode { Id = 1, Code = "EXP01", ExpiresAt = DateTime.UtcNow.AddMinutes(-10), Status = LinkCodeStatus.Active },
            new LinkCode { Id = 2, Code = "EXP02", ExpiresAt = DateTime.UtcNow.AddMinutes(-20), Status = LinkCodeStatus.Active }
        };

        _mockLinkCodeRepository
            .Setup(r => r.GetExpiredLinkCodesAsync())
            .ReturnsAsync(expiredCodes);

        _mockLinkCodeRepository
            .Setup(r => r.UpdateLinkCodeStatusAsync(It.IsAny<int>(), LinkCodeStatus.Expired))
            .Returns(Task.CompletedTask);

        // Act
        var deletedCount = await _linkCodeService.CleanupExpiredCodesAsync();

        // Assert
        Assert.Equal(2, deletedCount);
        _mockLinkCodeRepository.Verify(
            r => r.UpdateLinkCodeStatusAsync(It.IsAny<int>(), LinkCodeStatus.Expired),
            Times.Exactly(2)
        );
    }

    [Fact]
    public async Task CleanupExpiredCodesAsync_WithNoExpiredCodes_Returns0()
    {
        // Arrange
        _mockLinkCodeRepository
            .Setup(r => r.GetExpiredLinkCodesAsync())
            .ReturnsAsync(new List<LinkCode>());

        // Act
        var deletedCount = await _linkCodeService.CleanupExpiredCodesAsync();

        // Assert
        Assert.Equal(0, deletedCount);
        _mockLinkCodeRepository.Verify(
            r => r.UpdateLinkCodeStatusAsync(It.IsAny<int>(), It.IsAny<LinkCodeStatus>()),
            Times.Never
        );
    }

    #endregion
}
