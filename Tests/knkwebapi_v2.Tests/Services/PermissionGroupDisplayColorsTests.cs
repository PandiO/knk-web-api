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
/// KNG-7: PermissionGroup chat/tab-list styles (Minecraft "&amp;" codes) — validation on write, and resolution onto a
/// user's DTO (premium tier first, then the "Default" group, field by field).
/// </summary>
public class PermissionGroupDisplayColorsTests
{
    #region MinecraftTextStyle.Normalize

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_Blank_ReturnsNull(string? value)
    {
        Assert.Null(MinecraftTextStyle.Normalize(value, "NameColor"));
    }

    [Theory]
    [InlineData("&e", "&e")]
    [InlineData("&6&L", "&6&l")]
    [InlineData(" §c ", "&c")]
    [InlineData("&x&F&F&a&a&0&0", "&x&f&f&a&a&0&0")]
    [InlineData("&x&f&f&a&a&0&0&l&o", "&x&f&f&a&a&0&0&l&o")]
    [InlineData("&r&b", "&r&b")]
    public void Normalize_FormattingCodes_ReturnsCanonical(string value, string expected)
    {
        Assert.Equal(expected, MinecraftTextStyle.Normalize(value, "NameColor"));
    }

    [Theory]
    [InlineData("YELLOW")]      // a color name, not a code
    [InlineData("&eNoble")]     // text after the codes
    [InlineData("&g")]          // not a code
    [InlineData("&x&f&f&a&a")]  // hex cut short
    [InlineData("#FF0000")]
    [InlineData("&")]
    public void Normalize_NotOnlyCodes_Throws(string value)
    {
        var ex = Assert.Throws<ArgumentException>(() => MinecraftTextStyle.Normalize(value, "ChatPrimaryColor"));
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
            ChatPrimaryColor = "&B", ChatSecondaryColor = "§9", NameColor = " "
        });

        Assert.Equal("&b", existing.ChatPrimaryColor);
        Assert.Equal("&9", existing.ChatSecondaryColor);
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
            ChatPrimaryColor = "&D&L", ChatSecondaryColor = "&5", NameColor = "&x&F&F&5&5&F&F"
        });

        Assert.NotNull(saved);
        Assert.Equal("&d&l", saved!.ChatPrimaryColor);
        Assert.Equal("&5", saved.ChatSecondaryColor);
        Assert.Equal("&x&f&f&5&5&f&f", saved.NameColor);
        Assert.Equal("&x&f&f&5&5&f&f", result.NameColor);
    }

    #endregion

    #region UserPermissionGroupService.GetActivePremiumTierAsync

    [Fact]
    public async Task GetActivePremiumTier_CarriesGroupColors()
    {
        var noble = new PermissionGroup
        {
            Id = 101, Name = "Noble", Weight = 10, IsPremiumTier = true,
            ChatPrimaryColor = "&e", ChatSecondaryColor = "&6", NameColor = "&e"
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
        Assert.Equal("&e", tier!.ChatPrimaryColor);
        Assert.Equal("&6", tier.ChatSecondaryColor);
        Assert.Equal("&e", tier.NameColor);
    }

    #endregion

    #region UserService — resolution onto UserDto

    private static readonly PermissionGroup DefaultGroup = new()
    {
        Id = 1, Name = UserService.DefaultGroupName, Weight = 0,
        ChatPrimaryColor = "&a", ChatSecondaryColor = "&2", NameColor = "&7"
    };

    private static (UserService Service, Mock<IPermissionGroupRepository> GroupRepo) UserServiceWith(
        UserPermissionGroupDto? tier, PermissionGroup? defaultGroup, params PermissionGroup[] held)
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
        // The player's effective groups (memberships and their parents), as the repository returns them.
        groupRepo.Setup(r => r.GetActiveGroupsForUserAsync(It.IsAny<int>(), It.IsAny<DateTime>()))
            .ReturnsAsync(held.ToList());

        var service = new UserService(userRepo.Object, mapper.Object, new Mock<IPasswordService>().Object,
            new Mock<ILinkCodeService>().Object, titles.Object, memberships.Object,
            new Mock<IAuditLogService>().Object, groupRepo.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UserService>.Instance,
            new Mock<ICurrencyService>().Object, new Mock<ITitleProgressionService>().Object);
        return (service, groupRepo);
    }

    private static PermissionGroup Group(int id, string name, int weight, string? chat = null, string? bracket = null,
        string? name_ = null, PermissionGroup? parent = null, bool tier = false) => new()
    {
        Id = id, Name = name, Weight = weight, IsPremiumTier = tier, ChatPrimaryColor = chat, ChatSecondaryColor = bracket,
        NameColor = name_, ParentGroupId = parent?.Id, ParentGroup = parent
    };

    private static UserPermissionGroupDto TierDto(PermissionGroup g) => new()
    {
        PermissionGroupId = g.Id, PermissionGroupName = g.Name, IsPremiumTier = true,
        ChatPrimaryColor = g.ChatPrimaryColor, ChatSecondaryColor = g.ChatSecondaryColor, NameColor = g.NameColor
    };

    [Fact]
    public async Task UserDto_WithTier_UsesTierColors()
    {
        var dragon = Group(103, "Dragon Blood", 30, "&c", "&4", "&c", parent: DefaultGroup, tier: true);
        var (service, _) = UserServiceWith(TierDto(dragon), DefaultGroup, dragon, DefaultGroup);

        var dto = await service.GetByIdAsync(7);

        Assert.Equal("Dragon Blood", dto!.PremiumTierName);
        Assert.Equal("&c", dto.ChatPrimaryColor);
        Assert.Equal("&4", dto.ChatSecondaryColor);
        Assert.Equal("&c", dto.NameColor);
        Assert.Equal(103, dto.NameColorGroupId);
    }

    [Fact]
    public async Task UserDto_WithoutTier_UsesDefaultGroupColors()
    {
        var (service, _) = UserServiceWith(null, DefaultGroup, DefaultGroup);

        var dto = await service.GetByIdAsync(7);

        Assert.Null(dto!.PremiumTierName);
        Assert.Equal("&a", dto.ChatPrimaryColor);
        Assert.Equal("&2", dto.ChatSecondaryColor);
        Assert.Equal("&7", dto.NameColor);
        Assert.Null(dto.NameColorGroupId); // the Default group's colour: the plugin's default team
    }

    [Fact]
    public async Task UserDto_WithoutTierOrMembership_StillGetsDefaultGroupColors()
    {
        var (service, _) = UserServiceWith(null, DefaultGroup);

        var dto = await service.GetByIdAsync(7);

        Assert.Equal("&7", dto!.NameColor);
        Assert.Null(dto.NameColorGroupId);
    }

    [Fact]
    public async Task UserDto_TierWithUnsetColor_FallsBackToDefaultPerField()
    {
        var emperor = Group(110, "Emperor", 40, chat: "&d", tier: true);
        var (service, _) = UserServiceWith(TierDto(emperor), DefaultGroup, emperor, DefaultGroup);

        var dto = await service.GetByIdAsync(7);

        Assert.Equal("&d", dto!.ChatPrimaryColor);
        Assert.Equal("&2", dto.ChatSecondaryColor);
        Assert.Equal("&7", dto.NameColor);
        Assert.Null(dto.NameColorGroupId);
        Assert.Equal(110, dto.ChatPrimaryColorGroupId);
        Assert.Null(dto.ChatSecondaryColorGroupId); // Default's
    }

    [Fact]
    public async Task UserDto_NoTierAndNoDefaultGroup_LeavesColorsNull()
    {
        var (service, _) = UserServiceWith(null, null);

        var dto = await service.GetByIdAsync(7);

        Assert.Null(dto!.ChatPrimaryColor);
        Assert.Null(dto.ChatSecondaryColor);
        Assert.Null(dto.NameColor);
        Assert.Null(dto.NameColorGroupId);
    }

    // ---- PandiO/knk-workspace#260: any group, by precedence (hierarchy, then weight) ----

    [Fact]
    public async Task UserDto_StaffGroupColours_AreUsed_AboveATierOfLowerWeight()
    {
        // The developer's S2 test: the Staff group (not a tier) sets all three; before #260 they were never drawn.
        var royal = Group(102, "Royal", 20, "&b", "&3", "&b", parent: DefaultGroup, tier: true);
        var staff = Group(50, "Staff", 80, "&9", "&1", "&9");
        var (service, _) = UserServiceWith(TierDto(royal), DefaultGroup, staff, royal, DefaultGroup);

        var dto = await service.GetByIdAsync(7);

        Assert.Equal(("&9", "&1", "&9", (int?)50), (dto!.ChatPrimaryColor, dto.ChatSecondaryColor, dto.NameColor, dto.NameColorGroupId));
    }

    [Fact]
    public async Task UserDto_EachFieldFromTheFirstGroupThatSetsIt()
    {
        var royal = Group(102, "Royal", 20, "&b", "&3", "&b", tier: true);
        var staff = Group(50, "Staff", 80, name_: "&9"); // only the tab-list colour
        var (service, _) = UserServiceWith(TierDto(royal), DefaultGroup, staff, royal, DefaultGroup);

        var dto = await service.GetByIdAsync(7);

        Assert.Equal("&b", dto!.ChatPrimaryColor);
        Assert.Equal("&3", dto.ChatSecondaryColor);
        Assert.Equal("&9", dto.NameColor);
        Assert.Equal((50, 102, 102), (dto.NameColorGroupId, dto.ChatPrimaryColorGroupId, dto.ChatSecondaryColorGroupId));
    }

    [Fact]
    public async Task UserDto_HierarchyBeforeWeight_AHeldGroupBeatsTheParentItInherits()
    {
        // Moderator inherits from Helper; Helper has the higher weight, but the held child comes first in the chain.
        var helper = Group(60, "Helper", 90, "&e", "&6", "&e");
        var moderator = Group(61, "Moderator", 70, "&c", null, "&c", parent: helper);
        var (service, _) = UserServiceWith(null, DefaultGroup, moderator, helper, DefaultGroup);

        var dto = await service.GetByIdAsync(7);

        Assert.Equal("&c", dto!.ChatPrimaryColor);
        Assert.Equal("&6", dto.ChatSecondaryColor); // Moderator leaves it unset: its parent's
        Assert.Equal((61, 61, 60), (dto.NameColorGroupId, dto.ChatPrimaryColorGroupId, dto.ChatSecondaryColorGroupId));
        Assert.Equal(new[] { 61, 60, 1 }, dto.PermissionGroups.Select(g => g.Id)); // the same order as before #260
    }

    [Fact]
    public async Task UserDto_TwoUnrelatedGroups_TheHigherWeightWins()
    {
        var builder = Group(70, "Builder", 40, name_: "&2");
        var staff = Group(50, "Staff", 80, name_: "&9");
        var (service, _) = UserServiceWith(null, DefaultGroup, builder, staff, DefaultGroup);

        var dto = await service.GetByIdAsync(7);

        Assert.Equal(("&9", (int?)50), (dto!.NameColor, dto.NameColorGroupId));
    }

    [Fact]
    public void Resolve_BlankValuesDontCount_AndDefaultIsLastEvenWithAHigherWeight()
    {
        var heavyDefault = Group(1, UserService.DefaultGroupName, 999, "&a", "&2", "&7");
        var builder = Group(70, "Builder", 40, chat: "  ", name_: "&2");

        var styles = GroupDisplayStyles.Resolve(new[] { heavyDefault, builder }, heavyDefault);

        Assert.Equal(new GroupDisplayStyles.Result("&a", "&2", "&2", null, null, 70), styles);
    }

    [Fact]
    public async Task GetAll_LooksUpDefaultGroupOnce()
    {
        var (service, groupRepo) = UserServiceWith(null, DefaultGroup);

        var dtos = (await service.GetAllAsync()).ToList();

        Assert.Equal(2, dtos.Count);
        Assert.All(dtos, d => Assert.Equal("&7", d.NameColor));
        groupRepo.Verify(r => r.GetByNameAsync(UserService.DefaultGroupName), Times.Once);
    }

    #endregion
}
