using Xunit;
using Moq;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Unit tests for KitService (docs/specs/kits/IMPLEMENTATION_PLAN.md §2): gating combinations,
/// cooldown boundary, cost deduction, single-purchase premium, first-join grant, and GiveKitAsync's
/// full bypass. Mirrors PermissionResolutionServiceTests/UserPermissionGroupServiceTests' own
/// mocking/style precedent.
/// </summary>
public class KitServiceTests
{
    private readonly Mock<IKitRepository> _kitRepo = new();
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<IItemBlueprintRepository> _itemBlueprintRepo = new();
    private readonly Mock<ITitleBracketRepository> _titleBracketRepo = new();
    private readonly Mock<IPermissionGroupRepository> _permissionGroupRepo = new();
    private readonly Mock<ITitleService> _titleService = new();
    private readonly Mock<IUserPermissionGroupService> _userPermissionGroupService = new();
    private readonly Mock<IPermissionResolutionService> _permissionResolutionService = new();
    private readonly AutoMapper.IMapper _mapper;
    private readonly KitService _service;

    private static readonly User PlainUser = new() { Id = 1, Username = "alice", Coins = 250, Gems = 50, ExperiencePoints = 0 };

    public KitServiceTests()
    {
        var config = new AutoMapper.MapperConfiguration(cfg => cfg.AddProfile<knkwebapi_v2.Mapping.KitProfile>());
        _mapper = config.CreateMapper();

        _service = new KitService(
            _kitRepo.Object,
            _userRepo.Object,
            _itemBlueprintRepo.Object,
            _titleBracketRepo.Object,
            _permissionGroupRepo.Object,
            _titleService.Object,
            _userPermissionGroupService.Object,
            _permissionResolutionService.Object,
            _mapper);

        _titleBracketRepo.Setup(r => r.GetAllOrderedByMinExperienceAsync()).ReturnsAsync(new List<TitleBracket>());
        _userPermissionGroupService.Setup(s => s.GetByUserAsync(It.IsAny<int>())).ReturnsAsync(new List<UserPermissionGroupDto>());
        _permissionResolutionService.Setup(s => s.CheckAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(new PermissionCheckResponseDto { Result = PermissionResolutionResult.Undeclared });
    }

    private static Kit PlainKit(int id = 10) => new()
    {
        Id = id,
        Name = "Default",
        CooldownSeconds = 0,
        Contents = new List<KitContent>()
    };

    private void SetUser(User user) => _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
    private void SetKit(Kit kit) => _kitRepo.Setup(r => r.GetByIdAsync(kit.Id)).ReturnsAsync(kit);

    #region Gating combinations

    [Fact]
    public async Task ClaimKitAsync_NoGating_Succeeds()
    {
        SetUser(PlainUser);
        SetKit(PlainKit());

        var result = await _service.ClaimKitAsync(PlainUser.Id, 10);

        Assert.Equal(10, result.KitId);
        _kitRepo.Verify(r => r.AddClaimAsync(It.Is<KitClaim>(c => c.KitId == 10 && c.UserId == PlainUser.Id), null), Times.Once);
    }

    [Fact]
    public async Task ClaimKitAsync_TitleOnly_BelowRequiredBracket_Throws()
    {
        var brackets = new List<TitleBracket>
        {
            new() { Id = 1, MaleName = "Novice", FemaleName = "Novice", MinExperience = 0 },
            new() { Id = 2, MaleName = "Knight", FemaleName = "Knight", MinExperience = 100 }
        };
        _titleBracketRepo.Setup(r => r.GetAllOrderedByMinExperienceAsync()).ReturnsAsync(brackets);
        _titleService.Setup(s => s.ResolveAsync(0, It.IsAny<Gender?>())).ReturnsAsync(new TitleResolutionDto { TitleBracketId = 1, TitleName = "Novice" });

        var user = new User { Id = 2, Username = "bob", ExperiencePoints = 0 };
        var kit = PlainKit(); kit.MinTitleBracketId = 2;
        SetUser(user);
        SetKit(kit);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ClaimKitAsync(user.Id, kit.Id));
        Assert.Contains("Knight", ex.Message);
        _kitRepo.Verify(r => r.AddClaimAsync(It.IsAny<KitClaim>(), It.IsAny<User?>()), Times.Never);
    }

    [Fact]
    public async Task ClaimKitAsync_TitleOnly_AtOrAboveRequiredBracket_Succeeds()
    {
        var brackets = new List<TitleBracket>
        {
            new() { Id = 1, MaleName = "Novice", FemaleName = "Novice", MinExperience = 0 },
            new() { Id = 2, MaleName = "Knight", FemaleName = "Knight", MinExperience = 100 }
        };
        _titleBracketRepo.Setup(r => r.GetAllOrderedByMinExperienceAsync()).ReturnsAsync(brackets);
        _titleService.Setup(s => s.ResolveAsync(150, It.IsAny<Gender?>())).ReturnsAsync(new TitleResolutionDto { TitleBracketId = 2, TitleName = "Knight" });

        var user = new User { Id = 3, Username = "carl", ExperiencePoints = 150 };
        var kit = PlainKit(); kit.MinTitleBracketId = 2;
        SetUser(user);
        SetKit(kit);

        var result = await _service.ClaimKitAsync(user.Id, kit.Id);
        Assert.Equal(kit.Id, result.KitId);
    }

    [Fact]
    public async Task ClaimKitAsync_GroupOnly_NoActiveMembership_Throws()
    {
        var user = new User { Id = 4, Username = "dana" };
        var kit = PlainKit(); kit.RequiredPermissionGroupId = 55;
        SetUser(user);
        SetKit(kit);
        _userPermissionGroupService.Setup(s => s.GetByUserAsync(user.Id)).ReturnsAsync(new List<UserPermissionGroupDto>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ClaimKitAsync(user.Id, kit.Id));
    }

    [Fact]
    public async Task ClaimKitAsync_GroupOnly_ActiveMembership_Succeeds()
    {
        var user = new User { Id = 5, Username = "eve" };
        var kit = PlainKit(); kit.RequiredPermissionGroupId = 55;
        SetUser(user);
        SetKit(kit);
        _userPermissionGroupService.Setup(s => s.GetByUserAsync(user.Id)).ReturnsAsync(new List<UserPermissionGroupDto>
        {
            new() { UserId = user.Id, PermissionGroupId = 55, IsActive = true }
        });

        var result = await _service.ClaimKitAsync(user.Id, kit.Id);
        Assert.Equal(kit.Id, result.KitId);
    }

    [Fact]
    public async Task ClaimKitAsync_GroupOnly_ExpiredMembership_Throws()
    {
        var user = new User { Id = 6, Username = "finn" };
        var kit = PlainKit(); kit.RequiredPermissionGroupId = 55;
        SetUser(user);
        SetKit(kit);
        _userPermissionGroupService.Setup(s => s.GetByUserAsync(user.Id)).ReturnsAsync(new List<UserPermissionGroupDto>
        {
            new() { UserId = user.Id, PermissionGroupId = 55, IsActive = false }
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ClaimKitAsync(user.Id, kit.Id));
    }

    [Fact]
    public async Task ClaimKitAsync_NodeOnly_NotGranted_Throws()
    {
        var user = new User { Id = 7, Username = "gus" };
        var kit = PlainKit(); kit.RequiredPermissionNode = "knk.kit.veteran";
        SetUser(user);
        SetKit(kit);
        _permissionResolutionService.Setup(s => s.CheckAsync(user.Id, "knk.kit.veteran"))
            .ReturnsAsync(new PermissionCheckResponseDto { Result = PermissionResolutionResult.Denied });

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ClaimKitAsync(user.Id, kit.Id));
    }

    [Fact]
    public async Task ClaimKitAsync_NodeOnly_Granted_Succeeds()
    {
        var user = new User { Id = 8, Username = "hana" };
        var kit = PlainKit(); kit.RequiredPermissionNode = "knk.kit.veteran";
        SetUser(user);
        SetKit(kit);
        _permissionResolutionService.Setup(s => s.CheckAsync(user.Id, "knk.kit.veteran"))
            .ReturnsAsync(new PermissionCheckResponseDto { Result = PermissionResolutionResult.Granted });

        var result = await _service.ClaimKitAsync(user.Id, kit.Id);
        Assert.Equal(kit.Id, result.KitId);
    }

    [Fact]
    public async Task ClaimKitAsync_AllThreeGates_AllPass_Succeeds()
    {
        var brackets = new List<TitleBracket> { new() { Id = 1, MaleName = "Knight", FemaleName = "Knight", MinExperience = 0 } };
        _titleBracketRepo.Setup(r => r.GetAllOrderedByMinExperienceAsync()).ReturnsAsync(brackets);
        _titleService.Setup(s => s.ResolveAsync(0, It.IsAny<Gender?>())).ReturnsAsync(new TitleResolutionDto { TitleBracketId = 1, TitleName = "Knight" });

        var user = new User { Id = 9, Username = "iris" };
        var kit = PlainKit();
        kit.MinTitleBracketId = 1;
        kit.RequiredPermissionGroupId = 55;
        kit.RequiredPermissionNode = "knk.kit.veteran";
        SetUser(user);
        SetKit(kit);
        _userPermissionGroupService.Setup(s => s.GetByUserAsync(user.Id)).ReturnsAsync(new List<UserPermissionGroupDto>
        {
            new() { UserId = user.Id, PermissionGroupId = 55, IsActive = true }
        });
        _permissionResolutionService.Setup(s => s.CheckAsync(user.Id, "knk.kit.veteran"))
            .ReturnsAsync(new PermissionCheckResponseDto { Result = PermissionResolutionResult.Granted });

        var result = await _service.ClaimKitAsync(user.Id, kit.Id);
        Assert.Equal(kit.Id, result.KitId);
    }

    [Fact]
    public async Task ClaimKitAsync_AllThreeGates_OneFails_Throws()
    {
        var brackets = new List<TitleBracket> { new() { Id = 1, MaleName = "Knight", FemaleName = "Knight", MinExperience = 0 } };
        _titleBracketRepo.Setup(r => r.GetAllOrderedByMinExperienceAsync()).ReturnsAsync(brackets);
        _titleService.Setup(s => s.ResolveAsync(0, It.IsAny<Gender?>())).ReturnsAsync(new TitleResolutionDto { TitleBracketId = 1, TitleName = "Knight" });

        var user = new User { Id = 10, Username = "jack" };
        var kit = PlainKit();
        kit.MinTitleBracketId = 1;
        kit.RequiredPermissionGroupId = 55;
        kit.RequiredPermissionNode = "knk.kit.veteran";
        SetUser(user);
        SetKit(kit);
        // No active group membership -> the group gate fails even though title/node would pass.
        _userPermissionGroupService.Setup(s => s.GetByUserAsync(user.Id)).ReturnsAsync(new List<UserPermissionGroupDto>());
        _permissionResolutionService.Setup(s => s.CheckAsync(user.Id, "knk.kit.veteran"))
            .ReturnsAsync(new PermissionCheckResponseDto { Result = PermissionResolutionResult.Granted });

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ClaimKitAsync(user.Id, kit.Id));
    }

    #endregion

    #region Cooldown boundary

    [Fact]
    public async Task ClaimKitAsync_CooldownJustBeforeExpiry_Throws()
    {
        var user = new User { Id = 11, Username = "kim" };
        var kit = PlainKit(); kit.CooldownSeconds = 60;
        SetUser(user);
        SetKit(kit);
        _kitRepo.Setup(r => r.GetLastClaimAsync(kit.Id, user.Id))
            .ReturnsAsync(new KitClaim { KitId = kit.Id, UserId = user.Id, ClaimedAt = DateTime.UtcNow.AddSeconds(-59) });

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ClaimKitAsync(user.Id, kit.Id));
    }

    [Fact]
    public async Task ClaimKitAsync_CooldownJustAfterExpiry_Succeeds()
    {
        var user = new User { Id = 12, Username = "liam" };
        var kit = PlainKit(); kit.CooldownSeconds = 60;
        SetUser(user);
        SetKit(kit);
        _kitRepo.Setup(r => r.GetLastClaimAsync(kit.Id, user.Id))
            .ReturnsAsync(new KitClaim { KitId = kit.Id, UserId = user.Id, ClaimedAt = DateTime.UtcNow.AddSeconds(-61) });

        var result = await _service.ClaimKitAsync(user.Id, kit.Id);
        Assert.Equal(kit.Id, result.KitId);
    }

    [Fact]
    public async Task ClaimKitAsync_CooldownExactlyAtExpiry_Succeeds()
    {
        var user = new User { Id = 13, Username = "mira" };
        var kit = PlainKit(); kit.CooldownSeconds = 60;
        SetUser(user);
        SetKit(kit);
        // ClaimedAt exactly 60s ago -> readyAt == now, which is NOT > now, so allowed.
        var claimedAt = DateTime.UtcNow.AddSeconds(-60);
        _kitRepo.Setup(r => r.GetLastClaimAsync(kit.Id, user.Id))
            .ReturnsAsync(new KitClaim { KitId = kit.Id, UserId = user.Id, ClaimedAt = claimedAt });

        var result = await _service.ClaimKitAsync(user.Id, kit.Id);
        Assert.Equal(kit.Id, result.KitId);
    }

    #endregion

    #region Cost deduction

    [Fact]
    public async Task ClaimKitAsync_CoinsCost_SufficientBalance_DeductsAndSucceeds()
    {
        var user = new User { Id = 14, Username = "nora", Coins = 100, Gems = 100 };
        var kit = PlainKit(); kit.CostAmount = 40; kit.CostCurrency = KitCostCurrency.Coins;
        SetUser(user);
        SetKit(kit);

        await _service.ClaimKitAsync(user.Id, kit.Id);

        Assert.Equal(60, user.Coins);
        Assert.Equal(100, user.Gems);
        _kitRepo.Verify(r => r.AddClaimAsync(It.IsAny<KitClaim>(), user), Times.Once);
    }

    [Fact]
    public async Task ClaimKitAsync_GemsCost_SufficientBalance_DeductsCorrectCurrencyOnly()
    {
        var user = new User { Id = 15, Username = "omar", Coins = 100, Gems = 100 };
        var kit = PlainKit(); kit.CostAmount = 40; kit.CostCurrency = KitCostCurrency.Gems;
        SetUser(user);
        SetKit(kit);

        await _service.ClaimKitAsync(user.Id, kit.Id);

        Assert.Equal(100, user.Coins);
        Assert.Equal(60, user.Gems);
    }

    [Fact]
    public async Task ClaimKitAsync_CoinsCost_InsufficientBalance_ThrowsAndNeverClaims()
    {
        var user = new User { Id = 16, Username = "priya", Coins = 10, Gems = 100 };
        var kit = PlainKit(); kit.CostAmount = 40; kit.CostCurrency = KitCostCurrency.Coins;
        SetUser(user);
        SetKit(kit);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ClaimKitAsync(user.Id, kit.Id));

        Assert.Equal(10, user.Coins); // untouched
        _kitRepo.Verify(r => r.AddClaimAsync(It.IsAny<KitClaim>(), It.IsAny<User?>()), Times.Never);
    }

    [Fact]
    public async Task ClaimKitAsync_GemsCost_InsufficientBalance_DoesNotTouchCoins()
    {
        var user = new User { Id = 17, Username = "quinn", Coins = 100, Gems = 10 };
        var kit = PlainKit(); kit.CostAmount = 40; kit.CostCurrency = KitCostCurrency.Gems;
        SetUser(user);
        SetKit(kit);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ClaimKitAsync(user.Id, kit.Id));

        Assert.Equal(100, user.Coins);
        Assert.Equal(10, user.Gems);
    }

    #endregion

    #region Single-purchase premium

    [Fact]
    public async Task ClaimKitAsync_SinglePurchasePremium_Unpurchased_Throws()
    {
        var user = new User { Id = 18, Username = "ravi" };
        var kit = PlainKit(); kit.IsSinglePurchasePremium = true; kit.PremiumPriceGems = 200;
        SetUser(user);
        SetKit(kit);
        _kitRepo.Setup(r => r.GetPurchaseAsync(kit.Id, user.Id)).ReturnsAsync((KitPurchase?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ClaimKitAsync(user.Id, kit.Id));
    }

    [Fact]
    public async Task ClaimKitAsync_SinglePurchasePremium_Purchased_ClaimsFreeWithNoCooldownCheck()
    {
        var user = new User { Id = 19, Username = "sara", Coins = 0, Gems = 0 };
        var kit = PlainKit();
        kit.IsSinglePurchasePremium = true;
        kit.PremiumPriceGems = 200;
        kit.CooldownSeconds = 999999; // would block if the cooldown path were evaluated
        SetUser(user);
        SetKit(kit);
        _kitRepo.Setup(r => r.GetPurchaseAsync(kit.Id, user.Id))
            .ReturnsAsync(new KitPurchase { KitId = kit.Id, UserId = user.Id, GemsPaid = 200 });

        var result = await _service.ClaimKitAsync(user.Id, kit.Id);

        Assert.Equal(kit.Id, result.KitId);
        _kitRepo.Verify(r => r.GetLastClaimAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        Assert.Equal(0, user.Gems); // no per-claim cost charged
        _kitRepo.Verify(r => r.AddClaimAsync(It.IsAny<KitClaim>(), null), Times.Once);
    }

    [Fact]
    public async Task PurchaseKitAsync_NotSinglePurchasePremium_ThrowsArgumentException()
    {
        var user = new User { Id = 20, Username = "theo", Gems = 500 };
        var kit = PlainKit();
        SetUser(user);
        SetKit(kit);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.PurchaseKitAsync(user.Id, kit.Id));
    }

    [Fact]
    public async Task PurchaseKitAsync_AlreadyPurchased_Throws()
    {
        var user = new User { Id = 21, Username = "uma", Gems = 500 };
        var kit = PlainKit(); kit.IsSinglePurchasePremium = true; kit.PremiumPriceGems = 200;
        SetUser(user);
        SetKit(kit);
        _kitRepo.Setup(r => r.GetPurchaseAsync(kit.Id, user.Id))
            .ReturnsAsync(new KitPurchase { KitId = kit.Id, UserId = user.Id, GemsPaid = 200 });

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.PurchaseKitAsync(user.Id, kit.Id));
    }

    [Fact]
    public async Task PurchaseKitAsync_SufficientGems_DeductsAndRecordsPurchase()
    {
        var user = new User { Id = 22, Username = "vera", Gems = 500 };
        var kit = PlainKit(); kit.IsSinglePurchasePremium = true; kit.PremiumPriceGems = 200;
        SetUser(user);
        SetKit(kit);
        _kitRepo.Setup(r => r.GetPurchaseAsync(kit.Id, user.Id)).ReturnsAsync((KitPurchase?)null);

        var result = await _service.PurchaseKitAsync(user.Id, kit.Id);

        Assert.Equal(200, result.GemsPaid);
        Assert.Equal(300, user.Gems);
        _kitRepo.Verify(r => r.AddPurchaseAsync(It.IsAny<KitPurchase>(), user), Times.Once);
    }

    [Fact]
    public async Task PurchaseKitAsync_InsufficientGems_Throws()
    {
        var user = new User { Id = 23, Username = "walt", Gems = 50 };
        var kit = PlainKit(); kit.IsSinglePurchasePremium = true; kit.PremiumPriceGems = 200;
        SetUser(user);
        SetKit(kit);
        _kitRepo.Setup(r => r.GetPurchaseAsync(kit.Id, user.Id)).ReturnsAsync((KitPurchase?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.PurchaseKitAsync(user.Id, kit.Id));
        Assert.Equal(50, user.Gems);
    }

    #endregion

    #region First-join grant

    [Fact]
    public async Task GrantFirstJoinKitsAsync_IgnoresCostAndCooldown()
    {
        var user = new User { Id = 24, Username = "xena", Coins = 0, Gems = 0 };
        var kit = PlainKit();
        kit.GrantOnFirstJoin = true;
        kit.CostAmount = 999;
        kit.CostCurrency = KitCostCurrency.Coins;
        kit.CooldownSeconds = 999999;
        SetUser(user);
        _kitRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Kit> { kit });

        var results = await _service.GrantFirstJoinKitsAsync(user.Id);

        Assert.Single(results);
        Assert.Equal(0, user.Coins); // never charged
        _kitRepo.Verify(r => r.GetLastClaimAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        _kitRepo.Verify(r => r.AddClaimAsync(It.IsAny<KitClaim>(), null), Times.Once);
    }

    [Fact]
    public async Task GrantFirstJoinKitsAsync_GatingStillEnforced_SkipsUngatedKit()
    {
        var brackets = new List<TitleBracket>
        {
            new() { Id = 1, MaleName = "Novice", FemaleName = "Novice", MinExperience = 0 },
            new() { Id = 2, MaleName = "Knight", FemaleName = "Knight", MinExperience = 100 }
        };
        _titleBracketRepo.Setup(r => r.GetAllOrderedByMinExperienceAsync()).ReturnsAsync(brackets);
        _titleService.Setup(s => s.ResolveAsync(0, It.IsAny<Gender?>())).ReturnsAsync(new TitleResolutionDto { TitleBracketId = 1, TitleName = "Novice" });

        var user = new User { Id = 25, Username = "yuki", ExperiencePoints = 0 };
        var gatedKit = PlainKit(100);
        gatedKit.GrantOnFirstJoin = true;
        gatedKit.MinTitleBracketId = 2; // brand-new player can't be Knight yet

        SetUser(user);
        _kitRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Kit> { gatedKit });

        var results = await _service.GrantFirstJoinKitsAsync(user.Id);

        Assert.Empty(results);
        _kitRepo.Verify(r => r.AddClaimAsync(It.IsAny<KitClaim>(), It.IsAny<User?>()), Times.Never);
    }

    [Fact]
    public async Task GrantFirstJoinKitsAsync_OnlyGrantsFlaggedKits()
    {
        var user = new User { Id = 26, Username = "zane" };
        var flagged = PlainKit(200); flagged.GrantOnFirstJoin = true;
        var notFlagged = PlainKit(201); notFlagged.GrantOnFirstJoin = false;
        SetUser(user);
        _kitRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Kit> { flagged, notFlagged });

        var results = await _service.GrantFirstJoinKitsAsync(user.Id);

        Assert.Single(results);
        Assert.Equal(200, results[0].KitId);
    }

    #endregion

    #region GiveKitAsync

    [Fact]
    public async Task GiveKitAsync_BypassesGatingCooldownAndCost()
    {
        var actor = new User { Id = 30, Username = "staff" };
        var target = new User { Id = 31, Username = "newbie", Coins = 0, Gems = 0 };
        var kit = PlainKit();
        kit.RequiredPermissionNode = "knk.kit.veteran"; // would fail if evaluated
        kit.CooldownSeconds = 999999;
        kit.CostAmount = 999;
        kit.CostCurrency = KitCostCurrency.Coins;
        SetUser(actor);
        SetUser(target);
        SetKit(kit);

        var result = await _service.GiveKitAsync(actor.Id, target.Id, kit.Id);

        Assert.Equal(kit.Id, result.KitId);
        Assert.Equal(0, target.Coins); // never charged
        _permissionResolutionService.Verify(s => s.CheckAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        _kitRepo.Verify(r => r.GetLastClaimAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        _kitRepo.Verify(r => r.AddClaimAsync(It.Is<KitClaim>(c => c.UserId == target.Id && c.KitId == kit.Id), null), Times.Once);
    }

    [Fact]
    public async Task GiveKitAsync_UnknownTargetUser_ThrowsKeyNotFound()
    {
        var actor = new User { Id = 32, Username = "staff2" };
        var kit = PlainKit();
        SetUser(actor);
        SetKit(kit);
        _userRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((User?)null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.GiveKitAsync(actor.Id, 999, kit.Id));
    }

    [Fact]
    public async Task GiveKitAsync_UnknownKit_ThrowsKeyNotFound()
    {
        var actor = new User { Id = 33, Username = "staff3" };
        var target = new User { Id = 34, Username = "target3" };
        SetUser(actor);
        SetUser(target);
        _kitRepo.Setup(r => r.GetByIdAsync(404)).ReturnsAsync((Kit?)null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.GiveKitAsync(actor.Id, target.Id, 404));
    }

    #endregion
}
