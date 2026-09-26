using AutoMapper;
using Xunit;
using Moq;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// KNG-7: PermissionGroup chat/tab-list colors — validation on write, and resolution onto a
/// user's DTO (premium tier first, then the "Default" group, field by field).
/// </summary>
public class PermissionGroupDisplayColorsTests
{
    #region MinecraftChatColors.Normalize

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_Blank_ReturnsNull(string? value)
    {
        Assert.Null(MinecraftChatColors.Normalize(value, "NameColor"));
    }

    [Theory]
    [InlineData("YELLOW", "YELLOW")]
    [InlineData("dark_red", "DARK_RED")]
    [InlineData(" Dark Red ", "DARK_RED")]
    [InlineData("light_purple", "LIGHT_PURPLE")]
    public void Normalize_KnownName_ReturnsCanonical(string value, string expected)
    {
        Assert.Equal(expected, MinecraftChatColors.Normalize(value, "NameColor"));
    }

    [Theory]
    [InlineData("PINK")]
    [InlineData("#FF0000")]
    [InlineData("§e")]
    public void Normalize_UnknownName_Throws(string value)
    {
        var ex = Assert.Throws<ArgumentException>(() => MinecraftChatColors.Normalize(value, "ChatPrimaryColor"));
        Assert.Contains("ChatPrimaryColor", ex.Message);
    }

    #endregion

    #region PermissionGroupService

    private static PermissionGroupService GroupService(Mock<IPermissionGroupRepository> repo) =>
        new(repo.Object, new MapperConfiguration(cfg => cfg.AddProfile<PermissionMappingProfile>()).CreateMapper());

    [Fact]
    public async Task UpdateAsync_CopiesAndNormalizesColors()
    {
        var existing = new PermissionGroup { Id = 5, Name = "Royal", Weight = 20, IsPremiumTier = true };
        var repo = new Mock<IPermissionGroupRepository>();
        repo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(existing);

        await GroupService(repo).UpdateAsync(5, new PermissionGroupDto
        {
            Name = "Royal", Weight = 20, IsPremiumTier = true,
            ChatPrimaryColor = "aqua", ChatSecondaryColor = "Blue", NameColor = " "
        });

        Assert.Equal("AQUA", existing.ChatPrimaryColor);
        Assert.Equal("BLUE", existing.ChatSecondaryColor);
        Assert.Null(existing.NameColor);
        repo.Verify(r => r.UpdateAsync(existing), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_InvalidColor_ThrowsAndDoesNotSave()
    {
        var existing = new PermissionGroup { Id = 5, Name = "Royal", Weight = 20 };
        var repo = new Mock<IPermissionGroupRepository>();
        repo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(existing);

        await Assert.ThrowsAsync<ArgumentException>(() => GroupService(repo).UpdateAsync(5,
            new PermissionGroupDto { Name = "Royal", Weight = 20, NameColor = "PINK" }));

        repo.Verify(r => r.UpdateAsync(It.IsAny<PermissionGroup>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_NormalizesColorsOntoEntity()
    {
        PermissionGroup? saved = null;
        var repo = new Mock<IPermissionGroupRepository>();
        repo.Setup(r => r.AddAsync(It.IsAny<PermissionGroup>())).Callback<PermissionGroup>(g => saved = g);

        var result = await GroupService(repo).CreateAsync(new PermissionGroupDto
        {
            Name = "Emperor", Weight = 40, IsPremiumTier = true,
            ChatPrimaryColor = "light_purple", ChatSecondaryColor = "dark_purple", NameColor = "light_purple"
        });

        Assert.NotNull(saved);
        Assert.Equal("LIGHT_PURPLE", saved!.ChatPrimaryColor);
        Assert.Equal("DARK_PURPLE", saved.ChatSecondaryColor);
        Assert.Equal("LIGHT_PURPLE", saved.NameColor);
        Assert.Equal("LIGHT_PURPLE", result.NameColor);
    }

    #endregion

    #region UserPermissionGroupService.GetActivePremiumTierAsync

    [Fact]
    public async Task GetActivePremiumTier_CarriesGroupColors()
    {
        var noble = new PermissionGroup
        {
            Id = 101, Name = "Noble", Weight = 10, IsPremiumTier = true,
            ChatPrimaryColor = "YELLOW", ChatSecondaryColor = "GOLD", NameColor = "YELLOW"
        };
        var repo = new Mock<IUserPermissionGroupRepository>();
        repo.Setup(r => r.GetByUserAsync(1)).ReturnsAsync(new List<UserPermissionGroup>
        {
            new() { UserId = 1, PermissionGroupId = noble.Id, PermissionGroup = noble }
        });
        var service = new UserPermissionGroupService(repo.Object, new Mock<IUserRepository>().Object,
            new Mock<IPermissionGroupRepository>().Object, new Mock<IAuditLogService>().Object);

        var tier = await service.GetActivePremiumTierAsync(1);

        Assert.NotNull(tier);
        Assert.Equal("YELLOW", tier!.ChatPrimaryColor);
        Assert.Equal("GOLD", tier.ChatSecondaryColor);
        Assert.Equal("YELLOW", tier.NameColor);
    }

    #endregion

    #region UserService — resolution onto UserDto

    private static readonly PermissionGroup DefaultGroup = new()
    {
        Id = 1, Name = UserService.DefaultGroupName, Weight = 0,
        ChatPrimaryColor = "GREEN", ChatSecondaryColor = "DARK_GREEN", NameColor = "GRAY"
    };

    private static (UserService Service, Mock<IPermissionGroupRepository> GroupRepo) UserServiceWith(
        UserPermissionGroupDto? tier, PermissionGroup? defaultGroup)
    {
        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => new User { Id = id, Username = "user" + id });
        userRepo.Setup(r => r.GetAllAsync())
            .ReturnsAsync(new List<User> { new() { Id = 1, Username = "a" }, new() { Id = 2, Username = "b" } });
        var mapper = new Mock<IMapper>();
        mapper.Setup(m => m.Map<UserDto>(It.IsAny<User>()))
            .Returns((User u) => new UserDto { Id = u.Id, Username = u.Username });
        var titles = new Mock<ITitleService>();
        titles.Setup(s => s.ResolveAsync(It.IsAny<int>(), It.IsAny<Gender?>())).ReturnsAsync(new TitleResolutionDto());
        var memberships = new Mock<IUserPermissionGroupService>();
        memberships.Setup(s => s.GetActivePremiumTierAsync(It.IsAny<int>())).ReturnsAsync(tier);
        var groupRepo = new Mock<IPermissionGroupRepository>();
        groupRepo.Setup(r => r.GetByNameAsync(UserService.DefaultGroupName)).ReturnsAsync(defaultGroup);

        var service = new UserService(userRepo.Object, mapper.Object, new Mock<IPasswordService>().Object,
            new Mock<ILinkCodeService>().Object, titles.Object, memberships.Object,
            new Mock<IAuditLogService>().Object, groupRepo.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UserService>.Instance);
        return (service, groupRepo);
    }

    [Fact]
    public async Task UserDto_WithTier_UsesTierColors()
    {
        var tier = new UserPermissionGroupDto
        {
            PermissionGroupId = 103, PermissionGroupName = "Dragon Blood", IsPremiumTier = true,
            ChatPrimaryColor = "RED", ChatSecondaryColor = "DARK_RED", NameColor = "RED"
        };
        var (service, _) = UserServiceWith(tier, DefaultGroup);

        var dto = await service.GetByIdAsync(7);

        Assert.Equal("Dragon Blood", dto!.PremiumTierName);
        Assert.Equal("RED", dto.ChatPrimaryColor);
        Assert.Equal("DARK_RED", dto.ChatSecondaryColor);
        Assert.Equal("RED", dto.NameColor);
    }

    [Fact]
    public async Task UserDto_WithoutTier_UsesDefaultGroupColors()
    {
        var (service, _) = UserServiceWith(null, DefaultGroup);

        var dto = await service.GetByIdAsync(7);

        Assert.Null(dto!.PremiumTierName);
        Assert.Equal("GREEN", dto.ChatPrimaryColor);
        Assert.Equal("DARK_GREEN", dto.ChatSecondaryColor);
        Assert.Equal("GRAY", dto.NameColor);
    }

    [Fact]
    public async Task UserDto_TierWithUnsetColor_FallsBackToDefaultPerField()
    {
        var tier = new UserPermissionGroupDto
        {
            PermissionGroupId = 110, PermissionGroupName = "Emperor", IsPremiumTier = true,
            ChatPrimaryColor = "LIGHT_PURPLE"
        };
        var (service, _) = UserServiceWith(tier, DefaultGroup);

        var dto = await service.GetByIdAsync(7);

        Assert.Equal("LIGHT_PURPLE", dto!.ChatPrimaryColor);
        Assert.Equal("DARK_GREEN", dto.ChatSecondaryColor);
        Assert.Equal("GRAY", dto.NameColor);
    }

    [Fact]
    public async Task UserDto_NoTierAndNoDefaultGroup_LeavesColorsNull()
    {
        var (service, _) = UserServiceWith(null, null);

        var dto = await service.GetByIdAsync(7);

        Assert.Null(dto!.ChatPrimaryColor);
        Assert.Null(dto.ChatSecondaryColor);
        Assert.Null(dto.NameColor);
    }

    [Fact]
    public async Task GetAll_LooksUpDefaultGroupOnce()
    {
        var (service, groupRepo) = UserServiceWith(null, DefaultGroup);

        var dtos = (await service.GetAllAsync()).ToList();

        Assert.Equal(2, dtos.Count);
        Assert.All(dtos, d => Assert.Equal("GRAY", d.NameColor));
        groupRepo.Verify(r => r.GetByNameAsync(UserService.DefaultGroupName), Times.Once);
    }

    #endregion
}
