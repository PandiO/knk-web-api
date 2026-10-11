using Xunit;
using Moq;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
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
    private readonly Mock<IAuditLogService> _auditLogService = new();
    private readonly AutoMapper.IMapper _mapper;
    private readonly FakeCurrencyService _currency;
    private readonly KitService _service;

    private static readonly User PlainUser = new() { Id = 1, Username = "alice", Coins = 250, Gems = 50, ExperiencePoints = 0 };

    public KitServiceTests()
    {
        // KitDto's read-only nav objects (Helmet, RequiredPermissionGroup, ...) reuse the
        // ItemBlueprint/PermissionGroup profiles' nav maps, as the app's assembly-scan registration does.
        var config = new AutoMapper.MapperConfiguration(cfg =>
        {
            cfg.AddProfile<knkwebapi_v2.Mapping.KitProfile>();
            cfg.AddProfile<knkwebapi_v2.Mapping.ItemBlueprintMappingProfile>();
            cfg.AddProfile<knkwebapi_v2.Mapping.PermissionMappingProfile>();
        });
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
            _auditLogService.Object,
            _mapper,
            _currency = new FakeCurrencyService(id => _userRepo.Object.GetByIdAsync(id).Result));

        _titleBracketRepo.Setup(r => r.GetAllOrderedByMinExperienceAsync()).ReturnsAsync(new List<TitleBracket>());
        // The row lock is a DB concern; here it just runs the work (see UserRepository).
        _userRepo.Setup(r => r.RunWithUsersLockedAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<Func<Task>>()))
            .Returns((IEnumerable<int> _, Func<Task> work) => work());
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

    /// <summary>The game server's claim, as KitsController builds it from the Idempotency-Key header.</summary>
    private static CurrencyContext ClaimCtx(string key = "claim-key-1") => new()
    {
        IdempotencyKey = key,
        IdempotencyScope = CurrencyIdempotencyScopes.Plugin,
        ReasonCode = CurrencyReasons.KitClaimCost,
        Initiator = CurrencyInitiator.PluginService,
        InitiatorComponent = "KitsController"
    };

    private void SetUser(User user) => _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
    private void SetKit(Kit kit) => _kitRepo.Setup(r => r.GetByIdAsync(kit.Id)).ReturnsAsync(kit);

    #region Gating combinations

    [Fact]
    public async Task ClaimKitAsync_NoGating_Succeeds()
    {
        SetUser(PlainUser);
        SetKit(PlainKit());

        var result = await _service.ClaimKitAsync(PlainUser.Id, 10, ClaimCtx());

        Assert.Equal(10, result.KitId);
        _kitRepo.Verify(r => r.AddClaimAsync(It.Is<KitClaim>(c => c.KitId == 10 && c.UserId == PlainUser.Id)), Times.Once);
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

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ClaimKitAsync(user.Id, kit.Id, ClaimCtx()));
        Assert.Contains("Knight", ex.Message);
        _kitRepo.Verify(r => r.AddClaimAsync(It.IsAny<KitClaim>()), Times.Never);
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

        var result = await _service.ClaimKitAsync(user.Id, kit.Id, ClaimCtx());
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

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ClaimKitAsync(user.Id, kit.Id, ClaimCtx()));
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

        var result = await _service.ClaimKitAsync(user.Id, kit.Id, ClaimCtx());
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

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ClaimKitAsync(user.Id, kit.Id, ClaimCtx()));
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

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ClaimKitAsync(user.Id, kit.Id, ClaimCtx()));
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

        var result = await _service.ClaimKitAsync(user.Id, kit.Id, ClaimCtx());
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

        var result = await _service.ClaimKitAsync(user.Id, kit.Id, ClaimCtx());
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

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ClaimKitAsync(user.Id, kit.Id, ClaimCtx()));
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

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ClaimKitAsync(user.Id, kit.Id, ClaimCtx()));
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

        var result = await _service.ClaimKitAsync(user.Id, kit.Id, ClaimCtx());
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

        var result = await _service.ClaimKitAsync(user.Id, kit.Id, ClaimCtx());
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

        await _service.ClaimKitAsync(user.Id, kit.Id, ClaimCtx());

        Assert.Equal(60, user.Coins);
        Assert.Equal(100, user.Gems);
        _kitRepo.Verify(r => r.AddClaimAsync(It.IsAny<KitClaim>()), Times.Once);
    }

    [Fact]
    public async Task ClaimKitAsync_GemsCost_SufficientBalance_DeductsCorrectCurrencyOnly()
    {
        var user = new User { Id = 15, Username = "omar", Coins = 100, Gems = 100 };
        var kit = PlainKit(); kit.CostAmount = 40; kit.CostCurrency = KitCostCurrency.Gems;
        SetUser(user);
        SetKit(kit);

        await _service.ClaimKitAsync(user.Id, kit.Id, ClaimCtx());

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

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ClaimKitAsync(user.Id, kit.Id, ClaimCtx()));

        Assert.Equal(10, user.Coins); // untouched
        _kitRepo.Verify(r => r.AddClaimAsync(It.IsAny<KitClaim>()), Times.Never);
    }

    [Fact]
    public async Task ClaimKitAsync_GemsCost_InsufficientBalance_DoesNotTouchCoins()
    {
        var user = new User { Id = 17, Username = "quinn", Coins = 100, Gems = 10 };
        var kit = PlainKit(); kit.CostAmount = 40; kit.CostCurrency = KitCostCurrency.Gems;
        SetUser(user);
        SetKit(kit);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ClaimKitAsync(user.Id, kit.Id, ClaimCtx()));

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

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ClaimKitAsync(user.Id, kit.Id, ClaimCtx()));
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

        var result = await _service.ClaimKitAsync(user.Id, kit.Id, ClaimCtx());

        Assert.Equal(kit.Id, result.KitId);
        _kitRepo.Verify(r => r.GetLastClaimAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        Assert.Equal(0, user.Gems); // no per-claim cost charged
        _kitRepo.Verify(r => r.AddClaimAsync(It.IsAny<KitClaim>()), Times.Once);
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
        _kitRepo.Verify(r => r.AddPurchaseAsync(It.IsAny<KitPurchase>()), Times.Once);
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
        _kitRepo.Verify(r => r.AddClaimAsync(It.IsAny<KitClaim>()), Times.Once);
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
        _kitRepo.Verify(r => r.AddClaimAsync(It.IsAny<KitClaim>()), Times.Never);
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

    // KNG-81: the plugin calls grant-first-join whenever it reads isNewUser=true, so a relog
    // inside its cache TTL calls it again. The second call must grant nothing.
    [Fact]
    public async Task GrantFirstJoinKitsAsync_RepeatedCall_GrantsKitsAndTokensOnce()
    {
        var lootbox = new Mock<ILootboxTokenGrantService>();
        var service = ServiceWithLootbox(lootbox.Object);
        var user = new User { Id = 27, Username = "ari" };
        var kit = PlainKit(300); kit.GrantOnFirstJoin = true;
        SetUser(user);
        _kitRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Kit> { kit });
        var nextClaimId = 1;
        _kitRepo.Setup(r => r.AddClaimAsync(It.IsAny<KitClaim>()))
            .ReturnsAsync((KitClaim c) => { c.Id = nextClaimId++; return c; });

        var first = await service.GrantFirstJoinKitsAsync(user.Id);
        var second = await service.GrantFirstJoinKitsAsync(user.Id);

        Assert.Single(first);
        Assert.Empty(second);
        Assert.NotNull(user.FirstJoinKitsGrantedAt);
        _kitRepo.Verify(r => r.AddClaimAsync(It.IsAny<KitClaim>()), Times.Once);
        lootbox.Verify(l => l.IssueForKitAsync(user.Id, kit.Id, 1, null), Times.Once);
        lootbox.Verify(l => l.IssueForKitAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>()), Times.Once);
        _userRepo.Verify(r => r.RunWithUsersLockedAsync(It.Is<IEnumerable<int>>(ids => ids.Single() == user.Id), It.IsAny<Func<Task>>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GrantFirstJoinKitsAsync_AlreadyGranted_GrantsNothing()
    {
        var user = new User { Id = 28, Username = "bo", FirstJoinKitsGrantedAt = DateTime.UtcNow.AddDays(-3) };
        var kit = PlainKit(301); kit.GrantOnFirstJoin = true;
        SetUser(user);
        _kitRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Kit> { kit });

        var results = await _service.GrantFirstJoinKitsAsync(user.Id);

        Assert.Empty(results);
        _kitRepo.Verify(r => r.AddClaimAsync(It.IsAny<KitClaim>()), Times.Never);
        _userRepo.Verify(r => r.UpdateUserAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task GrantFirstJoinKitsAsync_NoFlaggedKits_StillMarksFirstJoinHandled()
    {
        var user = new User { Id = 29, Username = "cy" };
        SetUser(user);
        _kitRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Kit>());

        var results = await _service.GrantFirstJoinKitsAsync(user.Id);

        Assert.Empty(results);
        Assert.NotNull(user.FirstJoinKitsGrantedAt);
        _userRepo.Verify(r => r.UpdateUserAsync(user), Times.Once);
    }

    private KitService ServiceWithLootbox(ILootboxTokenGrantService lootbox) => new(
        _kitRepo.Object,
        _userRepo.Object,
        _itemBlueprintRepo.Object,
        _titleBracketRepo.Object,
        _permissionGroupRepo.Object,
        _titleService.Object,
        _userPermissionGroupService.Object,
        _permissionResolutionService.Object,
        _auditLogService.Object,
        _mapper,
        _currency,
        lootbox);

    #endregion

    #region Price validation and minting guards (KNG-22, currency DESIGN.md §1.4 A4)

    [Theory]
    [InlineData(-1, null)]
    [InlineData(null, -5)]
    public async Task CreateAsync_NegativePrice_IsRejected(int? costAmount, int? premiumPriceGems)
    {
        var dto = new KitDto { Name = "Mint", CostAmount = costAmount, CostCurrency = "Gems", PremiumPriceGems = premiumPriceGems };

        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(dto));
        _kitRepo.Verify(r => r.AddAsync(It.IsAny<Kit>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_NegativePremiumPrice_IsRejected()
    {
        SetKit(PlainKit());
        var dto = new KitDto { Name = "Default", IsSinglePurchasePremium = true, PremiumPriceGems = -100 };

        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(10, dto));
        _kitRepo.Verify(r => r.UpdateAsync(It.IsAny<Kit>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_PriceAboveTheGemCap_IsRejected()
    {
        var dto = new KitDto { Name = "Pricey", IsSinglePurchasePremium = true, PremiumPriceGems = BalanceLimits.MaxGems + 1 };

        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(dto));
    }

    [Theory]
    [InlineData(-100)]
    [InlineData(0)]
    [InlineData(null)]
    public async Task PurchaseKitAsync_NonPositivePrice_IsRejectedAndNeverCreditsGems(int? price)
    {
        // A kit row saved before the create/update check: "Gems -= -100" used to mint 100 gems.
        var user = new User { Id = 24, Username = "xena", Gems = 50 };
        var kit = PlainKit(); kit.IsSinglePurchasePremium = true; kit.PremiumPriceGems = price;
        SetUser(user);
        SetKit(kit);
        _kitRepo.Setup(r => r.GetPurchaseAsync(kit.Id, user.Id)).ReturnsAsync((KitPurchase?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.PurchaseKitAsync(user.Id, kit.Id));

        Assert.Equal(50, user.Gems);
        _kitRepo.Verify(r => r.AddPurchaseAsync(It.IsAny<KitPurchase>()), Times.Never);
    }

    [Fact]
    public async Task PurchaseKitAsync_RunsUnderTheUsersRowLock()
    {
        var user = new User { Id = 25, Username = "yuri", Gems = 500 };
        var kit = PlainKit(); kit.IsSinglePurchasePremium = true; kit.PremiumPriceGems = 200;
        SetUser(user);
        SetKit(kit);
        _kitRepo.Setup(r => r.GetPurchaseAsync(kit.Id, user.Id)).ReturnsAsync((KitPurchase?)null);

        await _service.PurchaseKitAsync(user.Id, kit.Id);

        _userRepo.Verify(r => r.RunWithUsersLockedAsync(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 25 })), It.IsAny<Func<Task>>()), Times.Once);
    }

    [Fact]
    public async Task ClaimKitAsync_RunsUnderTheUsersRowLock()
    {
        SetUser(PlainUser);
        SetKit(PlainKit());

        await _service.ClaimKitAsync(PlainUser.Id, 10, ClaimCtx());

        _userRepo.Verify(r => r.RunWithUsersLockedAsync(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { PlainUser.Id })), It.IsAny<Func<Task>>()), Times.Once);
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
        _kitRepo.Verify(r => r.AddClaimAsync(It.Is<KitClaim>(c => c.UserId == target.Id && c.KitId == kit.Id)), Times.Once);
    }

    [Fact]
    public async Task GiveKitAsync_RecordsExactlyOneKitGrantedAuditEntry()
    {
        var actor = new User { Id = 35, Username = "staff4" };
        var target = new User { Id = 36, Username = "target4" };
        var kit = PlainKit();
        SetUser(actor);
        SetUser(target);
        SetKit(kit);

        await _service.GiveKitAsync(actor.Id, target.Id, kit.Id);

        _auditLogService.Verify(s => s.RecordAsync(
            actor.Id,
            target.Id,
            AuditAction.KitGranted,
            It.Is<string?>(d => d != null && d.Contains($"\"kitId\":{kit.Id}") && d.Contains(kit.Name))), Times.Once);
        _auditLogService.Verify(s => s.RecordAsync(It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<AuditAction>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task ClaimKitAsync_DoesNotRecordAuditEntry()
    {
        var user = new User { Id = 37, Username = "selfclaimer" };
        var kit = PlainKit();
        SetUser(user);
        SetKit(kit);

        await _service.ClaimKitAsync(user.Id, kit.Id, ClaimCtx());

        _auditLogService.Verify(s => s.RecordAsync(It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<AuditAction>(), It.IsAny<string?>()), Times.Never);
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
        _auditLogService.Verify(s => s.RecordAsync(It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<AuditAction>(), It.IsAny<string?>()), Times.Never);
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

    [Fact]
    public async Task GiveKitAsync_NoActor_IsRecordedAsSystem()
    {
        // KNG-15/22: the plugin gives with its key; without X-Acting-User-Id there is no actor.
        var target = new User { Id = 35, Username = "target4" };
        var kit = PlainKit();
        SetUser(target);
        SetKit(kit);

        await _service.GiveKitAsync(null, target.Id, kit.Id);

        _auditLogService.Verify(s => s.RecordAsync(null, target.Id, AuditAction.KitGranted, It.IsAny<string?>()), Times.Once);
    }

    #endregion

    #region Edit-mode read shape

    // The Kit FormConfiguration authors its pickers on the navigation property ("Helmet"), so
    // FormWizard edit mode pre-fills them from these objects. Without them an untouched edit
    // submit wrote null to every equipment FK.
    [Fact]
    public async Task GetByIdAsync_EmitsNavigationObjectsAlongsideForeignKeys()
    {
        var helmet = new ItemBlueprint { Id = 20, Name = "iron_helmet", DefaultDisplayName = "Iron Helmet" };
        var hand = new ItemBlueprint { Id = 26, Name = "iron_sword", DefaultDisplayName = "Iron Sword" };
        var bracket = new TitleBracket { Id = 3, MaleName = "Knight", FemaleName = "Dame", MinExperience = 500 };
        var group = new PermissionGroup { Id = 7, Name = "VIP", Weight = 10 };
        var kit = PlainKit();
        kit.HelmetId = helmet.Id;
        kit.Helmet = helmet;
        kit.HandId = hand.Id;
        kit.Hand = hand;
        kit.MinTitleBracketId = bracket.Id;
        kit.MinTitleBracket = bracket;
        kit.RequiredPermissionGroupId = group.Id;
        kit.RequiredPermissionGroup = group;
        SetKit(kit);

        var dto = await _service.GetByIdAsync(kit.Id);

        Assert.NotNull(dto);
        Assert.Equal(20, dto!.HelmetId);
        Assert.Equal(20, dto.Helmet!.Id);
        Assert.Equal("iron_helmet", dto.Helmet.Name);
        Assert.Equal("Iron Helmet", dto.Helmet.DefaultDisplayName);
        Assert.Equal(26, dto.Hand!.Id);
        Assert.Null(dto.ChestplateId);
        Assert.Null(dto.Chestplate);
        Assert.Null(dto.Leggings);
        Assert.Null(dto.Boots);
        Assert.Null(dto.Shield);
        Assert.Equal(3, dto.MinTitleBracket!.Id);
        Assert.Equal("Knight", dto.MinTitleBracket.Name);
        Assert.Equal(7, dto.RequiredPermissionGroup!.Id);
        Assert.Equal("VIP", dto.RequiredPermissionGroup.Name);
    }

    [Fact]
    public async Task UpdateAsync_IgnoresNavigationObjects_OnlyForeignKeysAreWritten()
    {
        var kit = PlainKit();
        kit.HelmetId = 20;
        SetKit(kit);
        _itemBlueprintRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => new ItemBlueprint { Id = id, Name = "bp" + id });

        await _service.UpdateAsync(kit.Id, new KitDto
        {
            Name = "Default",
            HelmetId = 21,
            Helmet = new ItemBlueprintNavDto { Id = 99, Name = "stale" }
        });

        Assert.Equal(21, kit.HelmetId);
        _kitRepo.Verify(r => r.UpdateAsync(kit), Times.Once);
    }

    #endregion

    #region Contents slot range

    private static KitDto KitWithSlot(int slotIndex) => new()
    {
        Name = "Slot range",
        Contents = new List<KitContentDto> { new() { SlotIndex = slotIndex, ItemBlueprintId = 5, Quantity = 1 } }
    };

    [Theory]
    [InlineData(-1)]
    [InlineData(36)]
    public async Task CreateAsync_SlotIndexOutOfRange_Throws(int slotIndex)
    {
        _itemBlueprintRepo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new ItemBlueprint { Id = 5, Name = "Arrow" });

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(KitWithSlot(slotIndex)));

        Assert.Contains($"SlotIndex {slotIndex}", ex.Message);
        _kitRepo.Verify(r => r.AddAsync(It.IsAny<Kit>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(35)]
    public async Task CreateAsync_SlotIndexAtBoundary_IsAccepted(int slotIndex)
    {
        _itemBlueprintRepo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new ItemBlueprint { Id = 5, Name = "Arrow" });

        await _service.CreateAsync(KitWithSlot(slotIndex));

        _kitRepo.Verify(r => r.AddAsync(It.Is<Kit>(k => k.Contents.Single().SlotIndex == slotIndex)), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_SlotIndexOutOfRange_Throws()
    {
        SetKit(PlainKit());
        _itemBlueprintRepo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new ItemBlueprint { Id = 5, Name = "Arrow" });

        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(10, KitWithSlot(36)));

        _kitRepo.Verify(r => r.UpdateAsync(It.IsAny<Kit>()), Times.Never);
    }

    #endregion

    #region Lootbox token hook (knk-workspace docs/specs/lootboxes/IMPLEMENTATION_PLAN.md Phase 5)

    private KitService ServiceWithTokenGrants(Mock<ILootboxTokenGrantService> grants) => new(
        _kitRepo.Object, _userRepo.Object, _itemBlueprintRepo.Object, _titleBracketRepo.Object, _permissionGroupRepo.Object,
        _titleService.Object, _userPermissionGroupService.Object, _permissionResolutionService.Object, _auditLogService.Object,
        _mapper, new FakeCurrencyService(id => _userRepo.Object.GetByIdAsync(id).Result), grants.Object);

    private void NumberClaims()
    {
        var next = 100;
        _kitRepo.Setup(r => r.AddClaimAsync(It.IsAny<KitClaim>()))
            .ReturnsAsync((KitClaim claim) => { claim.Id = next++; return claim; });
    }

    [Fact]
    public async Task ClaimAndGive_IssueTheKitsLootboxTokens_KeyedByTheKitClaim()
    {
        SetUser(PlainUser);
        var staff = new User { Id = 7, Username = "staff" };
        SetUser(staff);
        SetKit(PlainKit());
        NumberClaims();
        var grants = new Mock<ILootboxTokenGrantService>();
        var service = ServiceWithTokenGrants(grants);

        await service.ClaimKitAsync(PlainUser.Id, 10);
        await service.GiveKitAsync(staff.Id, PlainUser.Id, 10);

        grants.Verify(g => g.IssueForKitAsync(PlainUser.Id, 10, 100, null), Times.Once);
        grants.Verify(g => g.IssueForKitAsync(PlainUser.Id, 10, 101, staff.Id), Times.Once);
    }

    [Fact]
    public async Task RefusedClaim_IssuesNoLootboxTokens()
    {
        SetUser(PlainUser);
        var kit = PlainKit();
        kit.IsSinglePurchasePremium = true; // not purchased: refused
        SetKit(kit);
        var grants = new Mock<ILootboxTokenGrantService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => ServiceWithTokenGrants(grants).ClaimKitAsync(PlainUser.Id, 10));

        grants.Verify(g => g.IssueForKitAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>()), Times.Never);
    }

    #endregion

    #region Currency ledger (currency-payments Phase 2)

    [Fact]
    public async Task ClaimKitAsync_Cost_IsAKitClaimCostPostingKeyedByTheClientKey()
    {
        var user = new User { Id = 40, Username = "ledger", Coins = 100 };
        var kit = PlainKit(); kit.CostAmount = 40; kit.CostCurrency = KitCostCurrency.Coins;
        SetUser(user);
        SetKit(kit);

        await _service.ClaimKitAsync(user.Id, kit.Id, ClaimCtx("abc-123"));

        var posting = Assert.Single(_currency.Postings);
        Assert.Equal(CurrencyReasons.KitClaimCost, posting.Ctx.ReasonCode);
        Assert.Equal("kit-claim:abc-123", posting.Ctx.IdempotencyKey);
        Assert.Equal(("Kit", "10"), (posting.Ctx.SourceType, posting.Ctx.SourceRef));
        Assert.Equal((Currency.Coins, -40L), (posting.Legs[0].Currency, posting.Legs[0].Amount));
    }

    [Fact]
    public async Task ClaimKitAsync_RetryWithTheSameKey_NeitherPaysNorClaimsTwice()
    {
        var user = new User { Id = 41, Username = "retry", Coins = 100 };
        var kit = PlainKit(); kit.CostAmount = 40; kit.CostCurrency = KitCostCurrency.Coins;
        SetUser(user);
        SetKit(kit);

        await _service.ClaimKitAsync(user.Id, kit.Id, ClaimCtx("same"));
        await _service.ClaimKitAsync(user.Id, kit.Id, ClaimCtx("same"));

        Assert.Equal(60, user.Coins);
        Assert.Single(_currency.Postings);
        _kitRepo.Verify(r => r.AddClaimAsync(It.IsAny<KitClaim>()), Times.Once);
    }

    [Fact]
    public async Task ClaimKitAsync_CostWithoutAKey_IsRejected()
    {
        var user = new User { Id = 42, Username = "nokey", Coins = 100 };
        var kit = PlainKit(); kit.CostAmount = 40; kit.CostCurrency = KitCostCurrency.Coins;
        SetUser(user);
        SetKit(kit);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.ClaimKitAsync(user.Id, kit.Id));

        Assert.Equal(100, user.Coins);
        _kitRepo.Verify(r => r.AddClaimAsync(It.IsAny<KitClaim>()), Times.Never);
    }

    [Fact]
    public async Task ClaimKitAsync_FreeKit_NeedsNoKeyAndPostsNothing()
    {
        SetUser(PlainUser);
        SetKit(PlainKit());

        await _service.ClaimKitAsync(PlainUser.Id, 10);

        Assert.Empty(_currency.Postings);
        _kitRepo.Verify(r => r.AddClaimAsync(It.IsAny<KitClaim>()), Times.Once);
    }

    [Fact]
    public async Task PurchaseKitAsync_IsAKitPurchasePostingUnderTheOncePerUserKey()
    {
        var user = new User { Id = 43, Username = "buyer", Gems = 500 };
        var kit = PlainKit(); kit.IsSinglePurchasePremium = true; kit.PremiumPriceGems = 200;
        SetUser(user);
        SetKit(kit);
        _kitRepo.Setup(r => r.GetPurchaseAsync(kit.Id, user.Id)).ReturnsAsync((KitPurchase?)null);

        await _service.PurchaseKitAsync(user.Id, kit.Id, ClaimCtx("ignored") with { ReasonCode = CurrencyReasons.KitPurchase });

        var posting = Assert.Single(_currency.Postings);
        Assert.Equal(CurrencyReasons.KitPurchase, posting.Ctx.ReasonCode);
        Assert.Equal("kit-purchase:10:43", posting.Ctx.IdempotencyKey);
        Assert.Equal(CurrencyIdempotencyScopes.System, posting.Ctx.IdempotencyScope);
        Assert.Equal(CurrencyInitiator.PluginService, posting.Ctx.Initiator);
        Assert.Equal((Currency.Gems, -200L), (posting.Legs[0].Currency, posting.Legs[0].Amount));
    }

    #endregion
}
