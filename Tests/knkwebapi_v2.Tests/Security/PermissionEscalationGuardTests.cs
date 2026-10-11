using Microsoft.AspNetCore.Mvc;
using Moq;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Tests.Api;
using AutoMapper;
using Xunit;

namespace knkwebapi_v2.Tests.Security;

/// <summary>
/// Closed-alpha hardening WP3: staff can only hand out what they hold, never to themselves; the
/// owner (*) can do both; the game server is not checked by the API.
/// </summary>
public class PermissionEscalationGuardTests
{
    private const int Owner = 1;
    private const int Moderator = 10;
    private const int Player = 20;
    private const int AdminGroup = 500;
    private const int SupportGroup = 501;
    private const int SupportBaseGroup = 502;
    private const int DefaultGroup = 503;
    private const int NobleGroup = 504;
    private const int RoyalGroup = 505;
    private const int SneakyPremiumGroup = 506;

    private readonly Dictionary<int, string[]> _held = new()
    {
        [Owner] = new[] { "*" },
        [Moderator] = new[] { "knk.admin.user.manage", "knk.admin.user.perm", "knk.admin.user.group", "knk.freeze" },
        [Player] = Array.Empty<string>(),
    };

    private readonly Mock<IPermissionResolutionService> _permissions = new();
    private readonly Mock<IPermissionGroupRepository> _groups = new();
    private readonly Mock<IUserPermissionGroupRepository> _memberships = new();
    private const int ModeratorRankGroup = 600;
    private const int AdminRankGroup = 601;
    private const int StaffRankGroup = 602;
    private readonly PermissionEscalationGuard _guard;

    public PermissionEscalationGuardTests()
    {
        // Same matching rules as PermissionResolutionService.TryMatch: exact, or a trailing-* prefix.
        _permissions.Setup(p => p.CheckAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync((int userId, string node) => new PermissionCheckResponseDto
            {
                UserId = userId,
                Node = node,
                Result = _held.GetValueOrDefault(userId, Array.Empty<string>()).Any(g => Matches(g, node))
                    ? PermissionResolutionResult.Granted
                    : PermissionResolutionResult.Undeclared
            });

        Group(AdminGroup, null, "knk.admin.*");
        Group(SupportBaseGroup, null, "knk.freeze");
        Group(SupportGroup, SupportBaseGroup, "knk.admin.user.manage");
        Group(DefaultGroup, null, "knk.freeze");
        Group(NobleGroup, DefaultGroup, premium: true, "knk.teleport.free", "knk.chat.color");
        Group(RoyalGroup, NobleGroup, premium: true, "knk.teleport.unlimited");
        Group(SneakyPremiumGroup, AdminGroup, premium: true, "knk.chat.color");
        // The moderator's own rank: a Weight-50 group (the test groups above default to Weight 0).
        Group(ModeratorRankGroup, null, "knk.freeze");
        _groups.Setup(g => g.GetByIdAsync(ModeratorRankGroup)).ReturnsAsync(new PermissionGroup { Id = ModeratorRankGroup, Name = "Moderator", Weight = 50, Grants = new List<PermissionGrant>() });
        MemberOf(Moderator, 50);
        _guard = new PermissionEscalationGuard(_permissions.Object, _groups.Object, _memberships.Object);
    }

    private static bool Matches(string grant, string node)
    {
        if (grant == node) return true;
        if (!grant.EndsWith('*')) return false;
        var prefix = grant[..^1];
        return prefix.Length == 0 || node.StartsWith(prefix, StringComparison.Ordinal) || node == prefix.TrimEnd('.');
    }

    private void MemberOf(int userId, params int[] weights) =>
        _memberships.Setup(m => m.GetByUserAsync(userId)).ReturnsAsync(weights
            .Select((w, i) => new UserPermissionGroup { UserId = userId, PermissionGroupId = 900 + i, PermissionGroup = new PermissionGroup { Id = 900 + i, Weight = w } })
            .ToList());

    private void RankedGroup(int id, int weight, int? parent, params PermissionGrant[] grants) =>
        _groups.Setup(g => g.GetByIdAsync(id)).ReturnsAsync(new PermissionGroup
        {
            Id = id, Name = $"group{id}", Weight = weight, ParentGroupId = parent, Grants = grants.ToList()
        });

    private static PermissionGrant Allow(string node) => new() { Node = node, Value = true };
    private static PermissionGrant Deny(string node) => new() { Node = node, Value = false };

    private void Group(int id, int? parent, params string[] nodes) => Group(id, parent, false, nodes);

    private void Group(int id, int? parent, bool premium, params string[] nodes) =>
        _groups.Setup(g => g.GetByIdAsync(id)).ReturnsAsync(new PermissionGroup
        {
            Id = id,
            Name = $"group{id}",
            ParentGroupId = parent,
            IsPremiumTier = premium,
            Grants = nodes.Select(n => new PermissionGrant { HolderId = id, Node = n, Value = true }).ToList()
        });

    [Theory]
    [InlineData("*")]
    [InlineData("knk.admin.config")]
    [InlineData("knk.admin.*")]
    public async Task Moderator_CantGrantWhatTheyDontHold(string node)
    {
        var result = await _guard.CanGrantNodeAsync(Moderator, Player, node);
        Assert.False(result.Allowed);
        Assert.Equal(PermissionEscalationGuard.NotHeldMessage, result.Message);
    }

    [Fact]
    public async Task Moderator_CanGrantANodeTheyHold_ToSomeoneElse()
    {
        Assert.True((await _guard.CanGrantNodeAsync(Moderator, Player, "knk.freeze")).Allowed);
    }

    [Fact]
    public async Task Moderator_CantGrantToThemselves_EvenANodeTheyHold()
    {
        var result = await _guard.CanGrantNodeAsync(Moderator, Moderator, "knk.freeze");
        Assert.False(result.Allowed);
        Assert.Equal(PermissionEscalationGuard.SelfMessage, result.Message);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("knk.admin.config")]
    public async Task Owner_CanGrantAnything_ToOthersAndThemselves(string node)
    {
        Assert.True((await _guard.CanGrantNodeAsync(Owner, Player, node)).Allowed);
        Assert.True((await _guard.CanGrantNodeAsync(Owner, Owner, node)).Allowed);
    }

    [Fact]
    public async Task Moderator_CantAssignAGroupWithNodesTheyLack()
    {
        Assert.False((await _guard.CanAssignGroupAsync(Moderator, Player, AdminGroup)).Allowed);
    }

    [Fact]
    public async Task GroupCheck_IncludesTheParentChain()
    {
        Assert.True((await _guard.CanAssignGroupAsync(Moderator, Player, SupportGroup)).Allowed);
        _held[Moderator] = new[] { "knk.admin.user.manage", "knk.admin.user.group" }; // lost knk.freeze
        Assert.False((await _guard.CanAssignGroupAsync(Moderator, Player, SupportGroup)).Allowed);
    }

    [Fact]
    public async Task PremiumRanks_NeedTheirPerks_WithoutThePremiumNode()
    {
        Assert.False((await _guard.CanAssignGroupAsync(Moderator, Player, NobleGroup)).Allowed);
    }

    [Fact]
    public async Task PremiumNode_LetsStaffHandOutPremiumRanks_WithoutTheirPerks()
    {
        _held[Moderator] = _held[Moderator].Append("knk.admin.user.group.premium").ToArray();
        Assert.True((await _guard.CanAssignGroupAsync(Moderator, Player, NobleGroup)).Allowed);
        Assert.True((await _guard.CanAssignGroupAsync(Moderator, Player, RoyalGroup)).Allowed);
    }

    [Fact]
    public async Task PremiumNode_StillNeedsTheNonPremiumParentChain()
    {
        _held[Moderator] = new[] { "knk.admin.user.manage", "knk.admin.user.group", "knk.admin.user.group.premium" };
        // Noble inherits Default (knk.freeze), which this moderator no longer holds.
        Assert.False((await _guard.CanAssignGroupAsync(Moderator, Player, NobleGroup)).Allowed);
        // A "premium" group under the Admin group can't be used to hand out knk.admin.*.
        Assert.False((await _guard.CanAssignGroupAsync(Moderator, Player, SneakyPremiumGroup)).Allowed);
        // Non-premium groups are unaffected.
        Assert.False((await _guard.CanAssignGroupAsync(Moderator, Player, AdminGroup)).Allowed);
    }

    [Fact]
    public async Task PremiumNode_DoesNotAllowGivingYourselfARank()
    {
        _held[Moderator] = _held[Moderator].Append("knk.admin.user.group.premium").ToArray();
        Assert.Equal(PermissionEscalationGuard.SelfMessage, (await _guard.CanAssignGroupAsync(Moderator, Moderator, NobleGroup)).Message);
    }

    [Fact]
    public async Task AnAdmin_CanHandOutLowerRanks_EvenWhenTheyCarryADeny()
    {
        const int admin = 30;
        RankedGroup(StaffRankGroup, 25, null, Allow("knk.staffchat"), Deny("knk.teleport.bypass.requirements"));
        RankedGroup(ModeratorRankGroup, 50, StaffRankGroup, Allow("knk.freeze"));
        RankedGroup(AdminRankGroup, 100, ModeratorRankGroup, Allow("knk.admin.user.group"));
        // What an Admin effectively holds through the chain: the deny is inherited as a deny.
        _held[admin] = new[] { "knk.staffchat", "knk.freeze", "knk.admin.user.group" };
        MemberOf(admin, 100);

        Assert.True((await _guard.CanAssignGroupAsync(admin, Player, StaffRankGroup)).Allowed);
        Assert.True((await _guard.CanAssignGroupAsync(admin, Player, ModeratorRankGroup)).Allowed);
    }

    [Fact]
    public async Task NobodyHandsOutOrRemovesTheirOwnRankOrHigher()
    {
        const int admin = 30;
        RankedGroup(AdminRankGroup, 100, null, Allow("knk.admin.user.group"));
        RankedGroup(SupportGroup, 150, null);
        _held[admin] = new[] { "knk.admin.user.group" };
        MemberOf(admin, 100);

        Assert.Equal(PermissionEscalationGuard.RankMessage, (await _guard.CanAssignGroupAsync(admin, Player, AdminRankGroup)).Message);
        Assert.Equal(PermissionEscalationGuard.RankMessage, (await _guard.CanAssignGroupAsync(admin, Player, SupportGroup)).Message);
    }

    [Fact]
    public async Task WithoutAnyGroup_NoGroupCanBeHandedOut()
    {
        MemberOf(Moderator);
        Assert.Equal(PermissionEscalationGuard.RankMessage, (await _guard.CanAssignGroupAsync(Moderator, Player, SupportBaseGroup)).Message);
    }

    [Fact]
    public async Task Moderator_CantAssignAGroupToThemselves()
    {
        var result = await _guard.CanAssignGroupAsync(Moderator, Moderator, SupportGroup);
        Assert.False(result.Allowed);
        Assert.Equal(PermissionEscalationGuard.SelfMessage, result.Message);
    }

    [Fact]
    public async Task Owner_CanAssignAnyGroup_IncludingToThemselves()
    {
        Assert.True((await _guard.CanAssignGroupAsync(Owner, Player, AdminGroup)).Allowed);
        Assert.True((await _guard.CanAssignGroupAsync(Owner, Owner, AdminGroup)).Allowed);
    }

    [Fact]
    public async Task ParentGroup_NeedsEveryInheritedNode()
    {
        Assert.False((await _guard.CanInheritFromGroupAsync(Moderator, AdminGroup)).Allowed);
        Assert.True((await _guard.CanInheritFromGroupAsync(Owner, AdminGroup)).Allowed);
    }

    // ===== Through the controller =====

    private UsersController Controller(Microsoft.AspNetCore.Http.HttpContext http, Mock<IPermissionGrantService> grants)
    {
        return new UsersController(new Mock<IUserService>().Object, new Mock<IMapper>().Object, _permissions.Object,
            new Mock<ISalaryService>().Object, new Mock<IUserProfileSummaryService>().Object,
            new Mock<IUserPermissionGroupService>().Object, grants.Object, _guard)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }

    [Fact]
    public async Task GrantNode_ByModerator_OfStar_Is403EscalationRefused_AndNothingIsWritten()
    {
        var grants = new Mock<IPermissionGrantService>();
        var result = await Controller(ServiceAuthTestHelper.WebUser(Moderator), grants)
            .GrantNode(Player, new GrantNodeRequestDto { Node = "*", Value = true });

        var refused = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, refused.StatusCode);
        Assert.Contains("EscalationRefused", refused.Value!.ToString());
        grants.Verify(g => g.UpsertByNodeAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<DateTime?>(), It.IsAny<int?>()), Times.Never);
    }

    [Fact]
    public async Task GrantNode_ByOwner_OfStar_IsWritten()
    {
        var grants = new Mock<IPermissionGrantService>();
        grants.Setup(g => g.UpsertByNodeAsync(Player, "*", true, null, Owner)).ReturnsAsync(new PermissionGrantDto { HolderId = Player, Node = "*", Value = true });
        var result = await Controller(ServiceAuthTestHelper.WebUser(Owner), grants)
            .GrantNode(Player, new GrantNodeRequestDto { Node = "*", Value = true });

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GrantNode_ByThePlugin_IsNotGuarded()
    {
        var grants = new Mock<IPermissionGrantService>();
        grants.Setup(g => g.UpsertByNodeAsync(Player, "knk.admin.config", true, null, Moderator))
            .ReturnsAsync(new PermissionGrantDto { HolderId = Player, Node = "knk.admin.config", Value = true });
        var result = await Controller(ServiceAuthTestHelper.Plugin(actingUserId: Moderator), grants)
            .GrantNode(Player, new GrantNodeRequestDto { Node = "knk.admin.config", Value = true });

        Assert.IsType<OkObjectResult>(result);
    }
}
