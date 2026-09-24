using Xunit;
using knkwebapi_v2.Models;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using AutoMapper;
using Microsoft.Extensions.Configuration;

namespace knkwebapi_v2.Tests.Integration;

/// <summary>
/// Integration tests for account management flows.
/// Tests complete workflows across repository, service, and mapping layers.
/// Uses in-memory database for isolation.
/// </summary>
public class AccountManagementIntegrationTests
{
    private readonly KnKDbContext _dbContext;
    private readonly UserRepository _userRepository;
    private readonly LinkCodeRepository _linkCodeRepository;
    private readonly PasswordService _passwordService;
    private readonly LinkCodeService _linkCodeService;
    private readonly UserService _userService;
    private readonly IMapper _mapper;

    public AccountManagementIntegrationTests()
    {
        // Set up in-memory database
        var options = new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new KnKDbContext(options);
        _dbContext.Database.EnsureCreated();

        // Initialize repositories
        _userRepository = new UserRepository(_dbContext);
        _linkCodeRepository = new LinkCodeRepository(_dbContext);

        // Initialize services
        var mapperConfig = new MapperConfiguration(cfg =>
        {
            cfg.AddMaps(typeof(UserMappingProfile).Assembly);
        });
        _mapper = mapperConfig.CreateMapper();

        var inMemorySettings = new Dictionary<string, string> {};
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings as IEnumerable<KeyValuePair<string, string?>>)
            .Build();
        var passwordServiceMock = new PasswordService(config);
        var securityOptions = Microsoft.Extensions.Options.Options.Create(new SecuritySettings { LinkCodeExpirationMinutes = 20, BcryptRounds = 10 });
        _linkCodeService = new LinkCodeService(_linkCodeRepository, _userRepository, _mapper, securityOptions);
        _passwordService = passwordServiceMock;
        var titleBracketRepository = new TitleBracketRepository(_dbContext);
        var titleService = new TitleService(titleBracketRepository);
        _userService = new UserService(_userRepository, _mapper, _passwordService, _linkCodeService, titleService);
    }

    #region Web App First Flow Tests

    [Fact]
    public async Task WebAppFirstFlow_UserCreatesAccountWithEmailAndPassword()
    {
        // Arrange
        var createDto = new UserCreateDto
        {
            Username = "webplayer",
            Email = "web@example.com",
            Password = "S3cure!Omega-4827",
            PasswordConfirmation = "S3cure!Omega-4827"
        };

        // Act
        var validation = await _userService.ValidateUserCreationAsync(createDto);
        var createdUser = await _userService.CreateAsync(createDto);

        // Assert
        Assert.True(validation.IsValid);
        Assert.NotNull(createdUser);
        Assert.Equal("webplayer", createdUser.Username);
        Assert.Equal("web@example.com", createdUser.Email);
        Assert.Equal(AccountCreationMethod.WebApp, createdUser.AccountCreatedVia);

        // Verify user was created with password hashed in the database
        var storedUser = await _userRepository.GetByIdAsync(createdUser.Id);
        Assert.NotNull(storedUser?.PasswordHash);
        Assert.NotEqual("S3cure!Omega-4827", storedUser.PasswordHash);
    }

    [Fact]
    public async Task WebAppFirstFlow_GeneratedLinkCodeIsValidFor20Minutes()
    {
        // Arrange
        var user = new User
        {
            Username = "player1",
            Email = "player1@example.com",
            CreatedAt = DateTime.UtcNow,
            AccountCreatedVia = AccountCreationMethod.WebApp
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        // Act
        var linkCodeDto = await _userService.GenerateLinkCodeAsync(user.Id);
        var storedLinkCode = await _linkCodeRepository.GetLinkCodeByCodeAsync(linkCodeDto.Code);

        // Assert
        Assert.NotNull(linkCodeDto);
        Assert.Equal(8, linkCodeDto.Code.Length);
        Assert.NotNull(storedLinkCode);
        var timeDiff = (storedLinkCode.ExpiresAt - storedLinkCode.CreatedAt).TotalMinutes;
        Assert.True(Math.Abs(timeDiff - 20) < 1);
    }

    [Fact]
    public async Task WebAppFirstFlow_LinkCodeCanBeLaterUsedToLinkMinecraftAccount()
    {
        // Arrange - create web app account
        var webUser = new User
        {
            Username = "webplayer2",
            Email = "web2@example.com",
            PasswordHash = await _passwordService.HashPasswordAsync("WebPass123!"),
            CreatedAt = DateTime.UtcNow,
            AccountCreatedVia = AccountCreationMethod.WebApp
        };
        _dbContext.Users.Add(webUser);
        await _dbContext.SaveChangesAsync();

        // Generate link code
        var linkCodeDto = await _userService.GenerateLinkCodeAsync(webUser.Id);
        var linkCode = linkCodeDto.Code;

        // Act - simulate Minecraft player discovering duplicate
        var (isValid, retrievedUser) = await _userService.ConsumeLinkCodeAsync(linkCode);

        // Assert
        Assert.True(isValid);
        Assert.NotNull(retrievedUser);
        Assert.Equal(webUser.Id, retrievedUser.Id);

        // Verify link code is marked as used
        var storedLinkCode = await _linkCodeRepository.GetLinkCodeByCodeAsync(linkCode);
        Assert.Equal(LinkCodeStatus.Used, storedLinkCode!.Status);
    }

    #endregion

    #region Minecraft Server First Flow Tests

    [Fact]
    public async Task MinecraftFirstFlow_CreateMinimalAccountWithUuidAndUsername()
    {
        // Arrange
        var uuid = "550e8400-e29b-41d4-a716-446655440000";
        var createDto = new UserCreateDto
        {
            Username = "mcplayer",
            Uuid = uuid
        };

        // Act
        var validation = await _userService.ValidateUserCreationAsync(createDto);
        var createdUser = await _userService.CreateAsync(createDto);

        // Assert
        Assert.True(validation.IsValid);
        Assert.NotNull(createdUser);
        Assert.Equal("mcplayer", createdUser.Username);
        Assert.Equal(uuid, createdUser.Uuid);
        Assert.Null(createdUser.Email);
        Assert.Equal(AccountCreationMethod.MinecraftServer, createdUser.AccountCreatedVia);

        // Verify user was created without password
        var storedUser = await _userRepository.GetByIdAsync(createdUser.Id);
        Assert.Null(storedUser?.PasswordHash);
    }

    [Fact]
    public async Task MinecraftFirstFlow_PlayerCanGenerateLinkCodeLater()
    {
        // Arrange - create Minecraft-only account
        var mcUser = new User
        {
            Username = "mcplayer2",
            Uuid = "550e8400-e29b-41d4-a716-446655440001",
            CreatedAt = DateTime.UtcNow,
            AccountCreatedVia = AccountCreationMethod.MinecraftServer
        };
        _dbContext.Users.Add(mcUser);
        await _dbContext.SaveChangesAsync();

        // Act - player decides to add email later and generates code
        var linkCodeDto = await _userService.GenerateLinkCodeAsync(mcUser.Id);

        // Assert
        Assert.NotNull(linkCodeDto);
        Assert.Equal(8, linkCodeDto.Code.Length);

        // Verify it's stored and active
        var storedCode = await _linkCodeRepository.GetLinkCodeByCodeAsync(linkCodeDto.Code);
        Assert.NotNull(storedCode);
        Assert.Equal(LinkCodeStatus.Active, storedCode.Status);
        Assert.Equal(mcUser.Id, storedCode.UserId);
    }

    #endregion

    #region Duplicate Detection & Merge Tests

    [Fact]
    public async Task DuplicateDetection_FindsDuplicateWhenUuidAndUsernameMatch()
    {
        // Arrange - create two users with same UUID and username
        var uuid = "550e8400-e29b-41d4-a716-446655440002";
        var username = "duplicateplayer";

        var user1 = new User
        {
            Username = username,
            Email = "user1@example.com",
            Uuid = uuid,
            Coins = 100,
            Gems = 20,
            ExperiencePoints = 500,
            CreatedAt = DateTime.UtcNow
        };

        var user2 = new User
        {
            Username = username,
            Email = "user2@example.com",
            Uuid = uuid,
            Coins = 50,
            Gems = 10,
            ExperiencePoints = 200,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(user1);
        _dbContext.Users.Add(user2);
        await _dbContext.SaveChangesAsync();

        // Act
        var (hasConflict, conflictingUserId) = await _userService.CheckForDuplicateAsync(uuid, username);

        // Assert
        Assert.True(hasConflict);
        Assert.NotNull(conflictingUserId);
    }

    [Fact]
    public async Task AccountMerge_KeepsWinnerValuesAndSoftDeletesLoser()
    {
        // Arrange - create two accounts with different balances
        var primaryUser = new User
        {
            Username = "primary",
            Email = "primary@example.com",
            Uuid = "550e8400-e29b-41d4-a716-446655440003",
            Coins = 500,
            Gems = 100,
            ExperiencePoints = 5000,
            CreatedAt = DateTime.UtcNow.AddDays(-30),
            IsActive = true
        };

        var secondaryUser = new User
        {
            Username = "secondary",
            Email = "secondary@example.com",
            Uuid = "550e8400-e29b-41d4-a716-446655440003",
            Coins = 100,
            Gems = 50,
            ExperiencePoints = 1000,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        _dbContext.Users.Add(primaryUser);
        _dbContext.Users.Add(secondaryUser);
        await _dbContext.SaveChangesAsync();

        var primaryId = primaryUser.Id;
        var secondaryId = secondaryUser.Id;

        // Act
        var mergedUser = await _userService.MergeAccountsAsync(primaryId, secondaryId);

        // Assert
        // Primary user retains its values
        Assert.NotNull(mergedUser);
        Assert.Equal(primaryId, mergedUser.Id);
        Assert.Equal(500, mergedUser.Coins);
        Assert.Equal(100, mergedUser.Gems);
        Assert.Equal(5000, mergedUser.ExperiencePoints);

        // Secondary is soft-deleted
        var deletedUser = await _userRepository.GetByIdAsync(secondaryId);
        Assert.NotNull(deletedUser);
        Assert.False(deletedUser.IsActive);
        Assert.NotNull(deletedUser.DeletedAt);
        Assert.NotNull(deletedUser.DeletedReason);
        Assert.Contains("merged", deletedUser.DeletedReason, StringComparison.OrdinalIgnoreCase);

        // ArchiveUntil is set to 90 days from deletion
        var daysDiff = (deletedUser.ArchiveUntil!.Value - deletedUser.DeletedAt!.Value).TotalDays;
        Assert.True(Math.Abs(daysDiff - 90) < 1);
    }

    #endregion

    #region Unique Constraint Tests

    [Fact]
    public async Task UniqueUsername_PreventsDuplicateUsernameCreation()
    {
        // Arrange
        var user1 = new User
        {
            Username = "uniqueplayer",
            Email = "user1@example.com",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Users.Add(user1);
        await _dbContext.SaveChangesAsync();

        // Act
        var (isTaken, conflictingId) = await _userService.CheckUsernameTakenAsync("uniqueplayer");

        // Assert
        Assert.True(isTaken);
        Assert.Equal(user1.Id, conflictingId);
    }

    [Fact]
    public async Task UniqueEmail_PreventsDuplicateEmailCreation()
    {
        // Arrange
        var user1 = new User
        {
            Username = "player1",
            Email = "unique@example.com",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Users.Add(user1);
        await _dbContext.SaveChangesAsync();

        // Act
        var (isTaken, conflictingId) = await _userService.CheckEmailTakenAsync("unique@example.com");

        // Assert
        Assert.True(isTaken);
        Assert.Equal(user1.Id, conflictingId);
    }

    [Fact]
    public async Task UniqueUuid_PreventsDuplicateUuidCreation()
    {
        // Arrange
        var uuid = "550e8400-e29b-41d4-a716-446655440004";
        var user1 = new User
        {
            Username = "player1",
            Uuid = uuid,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Users.Add(user1);
        await _dbContext.SaveChangesAsync();

        // Act
        var (isTaken, conflictingId) = await _userService.CheckUuidTakenAsync(uuid);

        // Assert
        Assert.True(isTaken);
        Assert.Equal(user1.Id, conflictingId);
    }

    [Fact]
    public async Task UniqueConstraint_AllowsNoOpUpdateWithSameUsername()
    {
        // Arrange
        var user = new User
        {
            Username = "player",
            Email = "player@example.com",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        // Act - check if username is taken but exclude current user
        var (isTaken, _) = await _userService.CheckUsernameTakenAsync("player", user.Id);

        // Assert
        Assert.False(isTaken);
    }

    #endregion

    #region Password Management Tests

    [Fact]
    public async Task ChangePassword_SucceedsWithCorrectCurrentPassword()
    {
        // Arrange
        var user = new User
        {
            Username = "player",
            Email = "player@example.com",
            PasswordHash = await _passwordService.HashPasswordAsync("OldPassword123!"),
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        // Act
        await _userService.ChangePasswordAsync(
            user.Id,
            "OldPassword123!",
            "N3w!Omega-4682",
            "N3w!Omega-4682"
        );

        // Assert
        var updatedUser = await _userRepository.GetByIdAsync(user.Id);
        Assert.NotNull(updatedUser);
        var verifyOld = await _passwordService.VerifyPasswordAsync("OldPassword123!", updatedUser.PasswordHash);
        var verifyNew = await _passwordService.VerifyPasswordAsync("N3w!Omega-4682", updatedUser.PasswordHash);
        
        Assert.False(verifyOld);
        Assert.True(verifyNew);
    }

    [Fact]
    public async Task ChangePassword_FailsWithIncorrectCurrentPassword()
    {
        // Arrange
        var user = new User
        {
            Username = "player",
            Email = "player@example.com",
            PasswordHash = await _passwordService.HashPasswordAsync("CorrectPassword123!"),
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _userService.ChangePasswordAsync(
                user.Id,
                "WrongPassword123!",
                "N3w!Omega-4682",
                "N3w!Omega-4682"
            )
        );
    }

    #endregion

    #region Link Code Expiration Tests

    [Fact]
    public async Task ExpiredLinkCode_CannotBeConsumed()
    {
        // Arrange
        var user = new User
        {
            Username = "player",
            Email = "player@example.com",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var expiredLinkCode = new LinkCode
        {
            UserId = user.Id,
            Code = "EXPIRED01",
            CreatedAt = DateTime.UtcNow.AddMinutes(-30),
            ExpiresAt = DateTime.UtcNow.AddMinutes(-10),
            Status = LinkCodeStatus.Active,
            User = user
        };
        _dbContext.LinkCodes.Add(expiredLinkCode);
        await _dbContext.SaveChangesAsync();

        // Act
        var (isValid, retrievedUser) = await _userService.ConsumeLinkCodeAsync("EXPIRED01");

        // Assert
        Assert.False(isValid);
        Assert.Null(retrievedUser);
    }

    [Fact]
    public async Task CleanupExpiredLinks_MarkExpiredCodesAsExpired()
    {
        // Arrange
        var user = new User
        {
            Username = "player",
            Email = "player@example.com",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var oldLinkCode = new LinkCode
        {
            UserId = user.Id,
            Code = "OLDCODE01",
            CreatedAt = DateTime.UtcNow.AddMinutes(-40),
            ExpiresAt = DateTime.UtcNow.AddMinutes(-20),
            Status = LinkCodeStatus.Active,
            User = user
        };
        _dbContext.LinkCodes.Add(oldLinkCode);
        await _dbContext.SaveChangesAsync();

        // Act
        var cleanedCount = await _userService.CleanupExpiredLinksAsync();

        // Assert
        Assert.Equal(1, cleanedCount);
        var markedCode = await _linkCodeRepository.GetLinkCodeByCodeAsync("OLDCODE01");
        Assert.Equal(LinkCodeStatus.Expired, markedCode!.Status);
    }

    #endregion

    public void Dispose()
    {
        _dbContext?.Dispose();
    }
}
