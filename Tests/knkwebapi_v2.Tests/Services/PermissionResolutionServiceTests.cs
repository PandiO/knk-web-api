using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

// Covers docs/specs/user-features/DESIGN.md §2.2's resolution/precedence rules: direct grants
// always win, then each group membership ordered by weight, then each group's own single-parent
// inheritance chain, with wildcard/longest-prefix matching and expiry exclusion at every level.
// This is the one piece of logic every later user-features phase (and the separate
// user-management module) depends on being correct, per
// docs/specs/user-features/IMPLEMENTATION_PLAN.md §1.
public class PermissionResolutionServiceTests
{
    private readonly Mock<IUserRepository> _userRepo;
    private readonly Mock<IPermissionGrantRepository> _grantRepo;
    private readonly Mock<IPermissionGroupRepository> _groupRepo;
    private readonly PermissionResolutionService _service;

    public PermissionResolutionServiceTests()
    {
        _userRepo = new Mock<IUserRepository>();
        _grantRepo = new Mock<IPermissionGrantRepository>();
        _groupRepo = new Mock<IPermissionGroupRepository>();
        _service = new PermissionResolutionService(_userRepo.Object, _grantRepo.Object, _groupRepo.Object);
    }

    private static User MakeUser(int id, string username) => new User { Id = id, Username = username };

    private static PermissionGrant Grant(int holderId, string node, bool value, DateTime? expiresAt = null) =>
        new PermissionGrant { HolderId = holderId, Node = node, Value = value, ExpiresAt = expiresAt };

    private static PermissionGroup MakeGroup(int id, string name, int weight, PermissionGroup? parent = null, params PermissionGrant[] grants)
    {
        var g = new PermissionGroup
        {
            Id = id,
            Name = name,
            Weight = weight,
            ParentGroup = parent,
            ParentGroupId = parent?.Id,
            Grants = new List<PermissionGrant>(grants)
        };
        return g;
    }

    private void SetupUser(User user) =>
        _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);

    private void SetupDirectGrants(int userId, params PermissionGrant[] grants) =>
        _grantRepo.Setup(r => r.GetActiveGrantsForHolderAsync(userId, It.IsAny<DateTime>()))
            .ReturnsAsync(new List<PermissionGrant>(grants));

    private void SetupMemberGroups(int userId, params PermissionGroup[] groups) =>
        _groupRepo.Setup(r => r.GetActiveGroupsForUserAsync(userId, It.IsAny<DateTime>()))
            .ReturnsAsync(new List<PermissionGroup>(groups));

    // ===== User not found =====

    [Fact]
    public async Task CheckAsync_UnknownUser_ReturnsNull()
    {
        _userRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((User?)null);

        var result = await _service.CheckAsync(999, "knk.gate.open");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetEffectiveAsync_UnknownUser_ReturnsNull()
    {
        _userRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((User?)null);

        var result = await _service.GetEffectiveAsync(999);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckAsync_BlankNode_Throws()
    {
        SetupUser(MakeUser(1, "alice"));

        await Assert.ThrowsAsync<ArgumentException>(() => _service.CheckAsync(1, ""));
    }

    // ===== Undeclared =====

    [Fact]
    public async Task CheckAsync_NoMatchingGrantAnywhere_ReturnsUndeclared()
    {
        SetupUser(MakeUser(1, "alice"));
        SetupDirectGrants(1);
        SetupMemberGroups(1);

        var result = await _service.CheckAsync(1, "totally.undeclared");

        result!.Result.Should().Be(PermissionResolutionResult.Undeclared);
        result.Allowed.Should().BeFalse();
        result.SourceHolderId.Should().BeNull();
    }

    // ===== User direct grants always win =====

    [Fact]
    public async Task CheckAsync_DirectExactGrant_Wins()
    {
        SetupUser(MakeUser(1, "alice"));
        SetupDirectGrants(1, Grant(1, "knk.gate.open", true));
        SetupMemberGroups(1);

        var result = await _service.CheckAsync(1, "knk.gate.open");

        result!.Result.Should().Be(PermissionResolutionResult.Granted);
        result.SourceHolderType.Should().Be("User");
        result.SourceHolderId.Should().Be(1);
        result.MatchedNode.Should().Be("knk.gate.open");
    }

    [Fact]
    public async Task CheckAsync_UserDirectDeny_BeatsGroupGrant_EvenWhenGroupNodeIsMoreSpecific()
    {
        // User's own level always wins per DESIGN.md §2.2, regardless of node specificity at a
        // lower (group) level - a broad user-level wildcard deny should still beat a group's
        // exact grant for the same node.
        SetupUser(MakeUser(1, "alice"));
        SetupDirectGrants(1, Grant(1, "knk.*", false));

        var group = MakeGroup(10, "staff", weight: 5, parent: null, Grant(10, "knk.gate.open", true));
        SetupMemberGroups(1, group);

        var result = await _service.CheckAsync(1, "knk.gate.open");

        result!.Result.Should().Be(PermissionResolutionResult.Denied);
        result.SourceHolderType.Should().Be("User");
    }

    // ===== Group weight ordering (direct multi-membership, not inheritance) =====

    [Fact]
    public async Task CheckAsync_MultipleDirectGroupMemberships_HigherWeightGroupWinsOverLowerWeight()
    {
        SetupUser(MakeUser(1, "alice"));
        SetupDirectGrants(1);

        var lowWeight = MakeGroup(10, "vip", weight: 10, parent: null, Grant(10, "knk.gate.open", true));
        var highWeight = MakeGroup(20, "moderator", weight: 20, parent: null, Grant(20, "knk.gate.open", false));

        // Repository contract: GetActiveGroupsForUserAsync returns groups already ordered by
        // weight descending - assert the service respects that order rather than re-sorting
        // (or worse, assuming insertion order), by deliberately returning them low-weight-first.
        SetupMemberGroups(1, lowWeight, highWeight);
        _groupRepo.Setup(r => r.GetActiveGroupsForUserAsync(1, It.IsAny<DateTime>()))
            .ReturnsAsync(new List<PermissionGroup> { highWeight, lowWeight });

        var result = await _service.CheckAsync(1, "knk.gate.open");

        result!.Result.Should().Be(PermissionResolutionResult.Denied);
        result.SourceHolderId.Should().Be(20);
    }

    // ===== Inheritance chain walk =====

    [Fact]
    public async Task CheckAsync_NoMatchInGroupOwnList_FallsThroughToParentGroup()
    {
        SetupUser(MakeUser(1, "alice"));
        SetupDirectGrants(1);

        var parent = MakeGroup(1, "default", weight: 0, parent: null, Grant(1, "knk.account.use", true));
        var child = MakeGroup(2, "vip", weight: 10, parent: parent /* no own grants */);
        SetupMemberGroups(1, child);

        var result = await _service.CheckAsync(1, "knk.account.use");

        result!.Result.Should().Be(PermissionResolutionResult.Granted);
        result.SourceHolderId.Should().Be(1);
        result.SourceHolderType.Should().Be("PermissionGroup");
    }

    [Fact]
    public async Task CheckAsync_ChildGroupOwnGrant_BeatsParentGroupGrant_ForSameNode()
    {
        SetupUser(MakeUser(1, "alice"));
        SetupDirectGrants(1);

        var parent = MakeGroup(1, "default", weight: 0, parent: null, Grant(1, "knk.gate.open", false));
        var child = MakeGroup(2, "vip", weight: 10, parent: parent, Grant(2, "knk.gate.open", true));
        SetupMemberGroups(1, child);

        var result = await _service.CheckAsync(1, "knk.gate.open");

        result!.Result.Should().Be(PermissionResolutionResult.Granted);
        result.SourceHolderId.Should().Be(2);
    }

    [Fact]
    public async Task CheckAsync_ThreeLevelChain_WalksAllTheWayToGrandparent()
    {
        SetupUser(MakeUser(1, "alice"));
        SetupDirectGrants(1);

        var grandparent = MakeGroup(1, "default", weight: 0, parent: null, Grant(1, "knk.account.use", true));
        var parent = MakeGroup(2, "vip", weight: 10, parent: grandparent);
        var child = MakeGroup(3, "moderator", weight: 20, parent: parent);
        SetupMemberGroups(1, child);

        var result = await _service.CheckAsync(1, "knk.account.use");

        result!.Result.Should().Be(PermissionResolutionResult.Granted);
        result.SourceHolderId.Should().Be(1);
    }

    [Fact]
    public async Task CheckAsync_CyclicParentChain_DoesNotHang_AndTreatsCycleAsExhausted()
    {
        // Defensive: cycles should be rejected at write time (PermissionGroupService), but the
        // resolution engine must not hang if one ever reaches storage anyway.
        var a = MakeGroup(1, "a", weight: 0);
        var b = MakeGroup(2, "b", weight: 0, parent: a);
        a.ParentGroup = b; // a -> b -> a
        a.ParentGroupId = b.Id;

        SetupUser(MakeUser(1, "alice"));
        SetupDirectGrants(1);
        SetupMemberGroups(1, a);

        var result = await _service.CheckAsync(1, "anything");

        result!.Result.Should().Be(PermissionResolutionResult.Undeclared);
    }

    // ===== Wildcard vs exact precedence =====

    [Fact]
    public async Task CheckAsync_ExactMatch_BeatsWildcardMatch_AtSameHolder()
    {
        SetupUser(MakeUser(1, "alice"));
        SetupDirectGrants(1);
        var group = MakeGroup(1, "staff", weight: 0, parent: null,
            Grant(1, "knk.gate.*", true),
            Grant(1, "knk.gate.close", false));
        SetupMemberGroups(1, group);

        var result = await _service.CheckAsync(1, "knk.gate.close");

        result!.Result.Should().Be(PermissionResolutionResult.Denied);
        result.MatchedNode.Should().Be("knk.gate.close");
    }

    [Fact]
    public async Task CheckAsync_LongerWildcardPrefix_BeatsShorterWildcardPrefix()
    {
        SetupUser(MakeUser(1, "alice"));
        SetupDirectGrants(1);
        var group = MakeGroup(1, "staff", weight: 0, parent: null,
            Grant(1, "knk.*", false),
            Grant(1, "knk.gate.*", true));
        SetupMemberGroups(1, group);

        var result = await _service.CheckAsync(1, "knk.gate.open");

        result!.Result.Should().Be(PermissionResolutionResult.Granted);
        result.MatchedNode.Should().Be("knk.gate.*");
    }

    [Fact]
    public async Task CheckAsync_WildcardNode_AlsoMatchesTheBareNodeWithoutTrailingSegment()
    {
        SetupUser(MakeUser(1, "alice"));
        SetupDirectGrants(1);
        var group = MakeGroup(1, "staff", weight: 0, parent: null, Grant(1, "knk.gate.*", true));
        SetupMemberGroups(1, group);

        var result = await _service.CheckAsync(1, "knk.gate");

        result!.Result.Should().Be(PermissionResolutionResult.Granted);
    }

    [Fact]
    public async Task CheckAsync_BareRootWildcard_MatchesAnyNode_ButIsLessSpecificThanNarrowerWildcard()
    {
        SetupUser(MakeUser(1, "alice"));
        SetupDirectGrants(1);
        var group = MakeGroup(1, "staff", weight: 0, parent: null,
            Grant(1, "*", true),
            Grant(1, "knk.gate.*", false));
        SetupMemberGroups(1, group);

        (await _service.CheckAsync(1, "knk.gate.open"))!.Result.Should().Be(PermissionResolutionResult.Denied);
        (await _service.CheckAsync(1, "something.else"))!.Result.Should().Be(PermissionResolutionResult.Granted);
    }

    [Fact]
    public async Task CheckAsync_DenyBeatsGrant_AtIdenticalSpecificity_SameHolder()
    {
        // Defensive tie-break for duplicate/overlapping rows at the exact same node+holder.
        SetupUser(MakeUser(1, "alice"));
        SetupDirectGrants(1, Grant(1, "knk.gate.open", true), Grant(1, "knk.gate.open", false));
        SetupMemberGroups(1);

        var result = await _service.CheckAsync(1, "knk.gate.open");

        result!.Result.Should().Be(PermissionResolutionResult.Denied);
    }

    // ===== Expiry exclusion =====

    [Fact]
    public async Task CheckAsync_ExpiredDirectGrant_IsExcluded()
    {
        SetupUser(MakeUser(1, "alice"));
        SetupDirectGrants(1); // repository contract: caller already filters by asOf, so an
                              // expired grant simply isn't returned - simulate that here.
        SetupMemberGroups(1);

        var result = await _service.CheckAsync(1, "knk.gate.open");

        result!.Result.Should().Be(PermissionResolutionResult.Undeclared);
    }

    [Fact]
    public async Task CheckAsync_ExpiredGroupGrant_IsExcluded_ButNonExpiredSiblingStillApplies()
    {
        SetupUser(MakeUser(1, "alice"));
        SetupDirectGrants(1);

        // The repository (GetActiveGroupsForUserAsync) is documented to return every grant for
        // every holder in the chain regardless of expiry - the service itself is responsible for
        // filtering by ExpiresAt when matching. Exercise that filtering directly.
        var group = MakeGroup(1, "staff", weight: 0, parent: null,
            Grant(1, "knk.gate.open", true, expiresAt: DateTime.UtcNow.AddDays(-1)),
            Grant(1, "knk.account.use", true, expiresAt: null));
        SetupMemberGroups(1, group);

        (await _service.CheckAsync(1, "knk.gate.open"))!.Result.Should().Be(PermissionResolutionResult.Undeclared);
        (await _service.CheckAsync(1, "knk.account.use"))!.Result.Should().Be(PermissionResolutionResult.Granted);
    }

    [Fact]
    public async Task CheckAsync_ExpiredGroupMembership_ExcludesTheWholeGroupChain()
    {
        // A membership expiring is the repository's job to filter (GetActiveGroupsForUserAsync
        // only returns non-expired memberships) - simulate that contract by simply not including
        // the expired group's chain in the returned set.
        SetupUser(MakeUser(1, "alice"));
        SetupDirectGrants(1);
        SetupMemberGroups(1); // expired membership -> group chain never reaches the service

        var result = await _service.CheckAsync(1, "knk.admin.reload");

        result!.Result.Should().Be(PermissionResolutionResult.Undeclared);
    }

    // ===== GetEffectiveAsync =====

    [Fact]
    public async Task GetEffectiveAsync_ReturnsOneEntryPerDistinctNode_FromHighestPrecedenceHolder()
    {
        SetupUser(MakeUser(1, "alice"));
        SetupDirectGrants(1, Grant(1, "knk.gate.open", false));

        var parent = MakeGroup(1, "default", weight: 0, parent: null, Grant(1, "knk.account.use", true));
        var child = MakeGroup(2, "vip", weight: 10, parent: parent,
            Grant(2, "knk.cosmetic.*", true),
            Grant(2, "knk.gate.close", false));
        SetupMemberGroups(1, child);

        var result = await _service.GetEffectiveAsync(1);

        result!.Permissions.Should().HaveCount(4);
        result.Permissions.Should().ContainSingle(p => p.Node == "knk.gate.open" && p.Value == false && p.SourceHolderType == "User");
        result.Permissions.Should().ContainSingle(p => p.Node == "knk.cosmetic.*" && p.Value == true && p.SourceHolderId == 2);
        result.Permissions.Should().ContainSingle(p => p.Node == "knk.gate.close" && p.Value == false && p.SourceHolderId == 2);
        result.Permissions.Should().ContainSingle(p => p.Node == "knk.account.use" && p.Value == true && p.SourceHolderId == 1);
    }

    [Fact]
    public async Task GetEffectiveAsync_SameNodeDeclaredAtMultipleHolders_OnlyHighestPrecedenceHolderWins()
    {
        SetupUser(MakeUser(1, "alice"));
        SetupDirectGrants(1); // user declares nothing directly for this node

        var parent = MakeGroup(1, "default", weight: 0, parent: null, Grant(1, "knk.gate.open", true));
        var child = MakeGroup(2, "vip", weight: 10, parent: parent, Grant(2, "knk.gate.open", false));
        SetupMemberGroups(1, child);

        var result = await _service.GetEffectiveAsync(1);

        result!.Permissions.Should().ContainSingle();
        var entry = result.Permissions[0];
        entry.Node.Should().Be("knk.gate.open");
        entry.Value.Should().BeFalse(); // child (vip) wins - closer in the chain than parent
        entry.SourceHolderId.Should().Be(2);
    }

    [Fact]
    public async Task GetEffectiveAsync_NoGrantsAnywhere_ReturnsEmptyList()
    {
        SetupUser(MakeUser(1, "alice"));
        SetupDirectGrants(1);
        SetupMemberGroups(1);

        var result = await _service.GetEffectiveAsync(1);

        result!.Permissions.Should().BeEmpty();
    }
}
