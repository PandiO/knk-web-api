using AutoMapper;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// UserService.LinkMinecraftAccountAsync against the rules of the invitations DESIGN § 5.4
/// (decision I7, KNG-207) and the per-account attempt limiter (KNG-208), with mocked repositories.
/// Case A (no account has the UUID) is only reachable through claim codes, which arrive with
/// KNG-214; it is tested here at service level.
/// </summary>
public class UserServiceLinkMinecraftAccountTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ILinkCodeService> _codes = new();
    private readonly Mock<ISessionRevocationService> _sessions = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<IUserPermissionGroupService> _memberships = new();
    private readonly FakeCurrencyService _currency;
    private readonly SecuritySettings _settings = new() { LinkAttempts = new LockoutSettings { MaxFailures = 3, WindowMinutes = 15, LockMinutes = 15 } };
    private readonly LinkAttemptLimiter _limiter;
    private readonly UserService _service;

    public UserServiceLinkMinecraftAccountTests()
    {
        var mapper = new Mock<IMapper>();
        mapper.Setup(m => m.Map<UserDto>(It.IsAny<User>()))
            .Returns((object u) => new UserDto { Id = ((User)u).Id, Username = ((User)u).Username, Uuid = ((User)u).Uuid, Email = ((User)u).Email });
        var titles = new Mock<ITitleService>();
        titles.Setup(t => t.ResolveAsync(It.IsAny<int>(), It.IsAny<Gender?>())).ReturnsAsync(new TitleResolutionDto());
        var memberships = _memberships;
        _users.Setup(r => r.RunWithUsersLockedAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<Func<Task>>()))
            .Returns((IEnumerable<int> _, Func<Task> work) => work());
        _currency = new FakeCurrencyService(id => _users.Object.GetByIdAsync(id).Result);
        var progression = new TitleProgressionService(_currency, _users.Object, titles.Object, memberships.Object, _audit.Object);
        _limiter = new LinkAttemptLimiter(new MemoryCache(new MemoryCacheOptions()), Options.Create(_settings));

        _service = new UserService(_users.Object, mapper.Object, new Mock<IPasswordService>().Object, _codes.Object, titles.Object,
            memberships.Object, _audit.Object, new Mock<IPermissionGroupRepository>().Object, NullLogger<UserService>.Instance,
            _currency, progression, null, _sessions.Object, _limiter);
    }

    private User Seed(User user)
    {
        _users.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
        if (user.Uuid != null)
        {
            _users.Setup(r => r.GetByUuidAsync(user.Uuid)).ReturnsAsync(user);
        }
        return user;
    }

    private LinkCode LinkCodeFor(User minecraft, string code = "ABCD2345")
    {
        var linkCode = new LinkCode { Code = code, UserId = minecraft.Id, Purpose = LinkCodePurpose.Link, Status = LinkCodeStatus.Active };
        _codes.Setup(c => c.ValidateLinkCodeAsync(code)).ReturnsAsync((true, linkCode, (string?)null));
        _codes.Setup(c => c.ConsumeLinkCodeAsync(code)).ReturnsAsync((true, linkCode, (string?)null));
        return linkCode;
    }

    private LinkCode ClaimCodeFor(string uuid, string name, string code = "7Q2HM4XA")
    {
        var linkCode = new LinkCode { Code = code, Purpose = LinkCodePurpose.Claim, MinecraftUuid = uuid, MinecraftName = name, Status = LinkCodeStatus.Active };
        _codes.Setup(c => c.ValidateLinkCodeAsync(code)).ReturnsAsync((true, linkCode, (string?)null));
        _codes.Setup(c => c.ConsumeLinkCodeAsync(code)).ReturnsAsync((true, linkCode, (string?)null));
        return linkCode;
    }

    private void InvalidCode(string code)
    {
        _codes.Setup(c => c.ValidateLinkCodeAsync(code)).ReturnsAsync((false, (LinkCode?)null, "Link code not found."));
    }

    [Fact]
    public async Task CaseB_FoldsTheWebAccountIntoTheMinecraftAccount()
    {
        var changedAt = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        var web = Seed(new User { Id = 1, Username = "web-1", Email = "steve@example.com", PasswordHash = "bcrypt-hash", EmailVerified = true,
            LastPasswordChangeAt = changedAt, Coins = 5, ExperiencePoints = 40 });
        var minecraft = Seed(new User { Id = 2, Username = "Steve", Uuid = "uuid-2", Coins = 100, ExperiencePoints = 10 });
        LinkCodeFor(minecraft);

        var result = await _service.LinkMinecraftAccountAsync(web.Id, "ABCD2345");

        Assert.True(result.Merged);
        Assert.Equal(2, result.User.Id); // the Minecraft account keeps its id (and with it groups, stats, kits)
        Assert.Equal("steve@example.com", minecraft.Email);
        Assert.Equal("bcrypt-hash", minecraft.PasswordHash);
        Assert.True(minecraft.EmailVerified);
        Assert.Equal(changedAt, minecraft.LastPasswordChangeAt);
        Assert.Equal("Steve", minecraft.Username);
        Assert.Equal("uuid-2", minecraft.Uuid);
        Assert.Null(web.Email); // cleared before the Minecraft account gets it: users.Email is unique
        Assert.Null(web.PasswordHash);
        Assert.Equal((100, 40), (minecraft.Coins, minecraft.ExperiencePoints)); // the higher of each balance
        Assert.Equal((0, 0), (web.Coins, web.ExperiencePoints));
        _users.Verify(r => r.MergeUsersAsync(2, 1), Times.Once);
        _sessions.Verify(s => s.RevokeAllSessionsAsync(1, It.IsAny<string>()), Times.Once);
        _sessions.Verify(s => s.RevokeAllSessionsAsync(2, It.IsAny<string>()), Times.Never);
        _codes.Verify(c => c.ConsumeLinkCodeAsync("ABCD2345"), Times.Once);
    }

    [Fact]
    public async Task CaseB_CarriesTheInviteFields_TheVerification_AndTheInvitesGroup()
    {
        // KNG-212 (L1-2): a shell created from an invite brings its invite, admission, verified address
        // and the invite's group to the Minecraft account it folds into.
        var verifiedAt = new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);
        var web = Seed(new User { Id = 1, Username = "web-abc", Email = "steve@example.com", PasswordHash = "h", EmailVerified = true,
            EmailVerifiedAt = verifiedAt, AdmittedAt = verifiedAt, InvitedViaInvitationId = 5, InvitedByUserId = 9 });
        var minecraft = Seed(new User { Id = 2, Username = "Steve", Uuid = "uuid-2" });
        LinkCodeFor(minecraft);
        var invitations = new Mock<IInvitationRepository>();
        invitations.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new Invitation { Id = 5, GrantGroupId = 3 });
        var service = new UserService(_users.Object, Mock.Of<IMapper>(m => m.Map<UserDto>(It.IsAny<User>()) == new UserDto { Id = 2 }),
            new Mock<IPasswordService>().Object, _codes.Object,
            Mock.Of<ITitleService>(t => t.ResolveAsync(It.IsAny<int>(), It.IsAny<Gender?>()) == Task.FromResult(new TitleResolutionDto())),
            _memberships.Object, _audit.Object, new Mock<IPermissionGroupRepository>().Object, NullLogger<UserService>.Instance,
            _currency, new TitleProgressionService(_currency, _users.Object, new Mock<ITitleService>().Object, _memberships.Object, _audit.Object),
            null, _sessions.Object, _limiter, null, invitations.Object);

        var result = await service.LinkMinecraftAccountAsync(web.Id, "ABCD2345");

        Assert.True(result.Merged);
        Assert.Equal(verifiedAt, minecraft.EmailVerifiedAt);
        Assert.Equal(verifiedAt, minecraft.AdmittedAt);
        Assert.Equal((5, 9), (minecraft.InvitedViaInvitationId, minecraft.InvitedByUserId));
        Assert.Null(web.EmailVerifiedAt);
        _memberships.Verify(m => m.UpsertAsync(It.Is<UpsertUserPermissionGroupDto>(d => d.UserId == 2 && d.PermissionGroupId == 3 && d.ExpiresAt == null), It.IsAny<int?>()), Times.Once);
    }

    [Fact]
    public async Task CaseB_KeepsTheMinecraftAccountsOwnAdmission()
    {
        var admitted = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var web = Seed(new User { Id = 1, Username = "web-1", Email = "steve@example.com", PasswordHash = "h", AdmittedAt = DateTime.UtcNow });
        var minecraft = Seed(new User { Id = 2, Username = "Steve", Uuid = "uuid-2", AdmittedAt = admitted });
        LinkCodeFor(minecraft);

        await _service.LinkMinecraftAccountAsync(web.Id, "ABCD2345");

        Assert.Equal(admitted, minecraft.AdmittedAt);
        _memberships.Verify(m => m.UpsertAsync(It.IsAny<UpsertUserPermissionGroupDto>(), It.IsAny<int?>()), Times.Never);
    }

    [Fact]
    public async Task CaseB_SavesTheShellWithoutItsEmailBeforeTheMinecraftAccountGetsIt()
    {
        var web = Seed(new User { Id = 1, Username = "web-1", Email = "steve@example.com", PasswordHash = "h" });
        var minecraft = Seed(new User { Id = 2, Username = "Steve", Uuid = "uuid-2" });
        LinkCodeFor(minecraft);
        var saves = new List<(int Id, string? Email)>();
        _users.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).Callback((User u) => saves.Add((u.Id, u.Email))).Returns(Task.CompletedTask);

        await _service.LinkMinecraftAccountAsync(web.Id, "ABCD2345");

        Assert.Equal(new[] { (1, (string?)null), (2, "steve@example.com") }, saves);
    }

    [Fact]
    public async Task CaseB_IsRefusedWhenTheMinecraftAccountAlreadyHasAWebLogin()
    {
        var web = Seed(new User { Id = 1, Username = "web-1", Email = "second@example.com", PasswordHash = "h2" });
        var minecraft = Seed(new User { Id = 2, Username = "Steve", Uuid = "uuid-2", Email = "steve@example.com", PasswordHash = "h1" });
        LinkCodeFor(minecraft);

        var ex = await Assert.ThrowsAsync<LinkConflictException>(() => _service.LinkMinecraftAccountAsync(web.Id, "ABCD2345"));

        Assert.Equal(LinkConflictException.AlreadyRegistered, ex.Code);
        Assert.Equal("steve@example.com", minecraft.Email);
        Assert.Equal("second@example.com", web.Email);
        _users.Verify(r => r.MergeUsersAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        _codes.Verify(c => c.ConsumeLinkCodeAsync(It.IsAny<string>()), Times.Never); // the code is still good
        Assert.False(_limiter.IsLocked(1, out _)); // a refusal of a valid code isn't a guess
    }

    [Fact]
    public async Task IsRefusedWhenTheWebAccountIsLinkedToAnotherMinecraftAccount()
    {
        var web = Seed(new User { Id = 1, Username = "Alex", Uuid = "uuid-1", Email = "alex@example.com", PasswordHash = "h" });
        var minecraft = Seed(new User { Id = 2, Username = "Steve", Uuid = "uuid-2" });
        LinkCodeFor(minecraft);

        var ex = await Assert.ThrowsAsync<LinkConflictException>(() => _service.LinkMinecraftAccountAsync(web.Id, "ABCD2345"));

        Assert.Equal(LinkConflictException.AlreadyLinked, ex.Code);
        Assert.Equal("uuid-1", web.Uuid);
        _users.Verify(r => r.MergeUsersAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        _codes.Verify(c => c.ConsumeLinkCodeAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ACodeForTheCallersOwnAccount_IsSpentAndChangesNothing()
    {
        var me = Seed(new User { Id = 2, Username = "Steve", Uuid = "uuid-2", Email = "steve@example.com", PasswordHash = "h" });
        LinkCodeFor(me);

        var result = await _service.LinkMinecraftAccountAsync(me.Id, "ABCD2345");

        Assert.False(result.Merged);
        Assert.Equal(2, result.User.Id);
        _codes.Verify(c => c.ConsumeLinkCodeAsync("ABCD2345"), Times.Once);
        _users.Verify(r => r.MergeUsersAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CaseA_AClaimCodeForAnUnknownUuid_GivesTheWebAccountTheUuidAndVerifiedName()
    {
        var web = Seed(new User { Id = 1, Username = "web-1", Email = "steve@example.com", PasswordHash = "h" });
        _users.Setup(r => r.GetByUuidAsync("uuid-7")).ReturnsAsync((User?)null);
        _users.Setup(r => r.GetByUsernameAsync("Steve")).ReturnsAsync((User?)null);
        ClaimCodeFor("uuid-7", "Steve");

        var result = await _service.LinkMinecraftAccountAsync(web.Id, "7Q2HM4XA");

        Assert.False(result.Merged);
        Assert.Equal(1, result.User.Id);
        Assert.Equal("uuid-7", web.Uuid);
        Assert.Equal("Steve", web.Username);
        Assert.Equal("steve@example.com", web.Email);
        _users.Verify(r => r.UpdateUserAsync(web), Times.Once);
        _users.Verify(r => r.MergeUsersAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        _codes.Verify(c => c.ConsumeLinkCodeAsync("7Q2HM4XA"), Times.Once);
    }

    [Fact]
    public async Task CaseA_ReleasesTheNameFromASquattingWebOnlyAccountFirst()
    {
        var web = Seed(new User { Id = 1, Username = "web-1", Email = "steve@example.com", PasswordHash = "h" });
        var squatter = Seed(new User { Id = 3, Username = "Steve", Email = "other@example.com", PasswordHash = "h3" });
        _users.Setup(r => r.GetByUuidAsync("uuid-7")).ReturnsAsync((User?)null);
        _users.Setup(r => r.GetByUsernameAsync("Steve")).ReturnsAsync(squatter);
        ClaimCodeFor("uuid-7", "Steve");

        await _service.LinkMinecraftAccountAsync(web.Id, "7Q2HM4XA");

        Assert.Equal("unclaimed-3", squatter.Username);
        Assert.Equal("Steve", web.Username);
    }

    [Fact]
    public async Task AClaimCodeForAnExistingMinecraftAccount_IsCaseB()
    {
        var web = Seed(new User { Id = 1, Username = "web-1", Email = "steve@example.com", PasswordHash = "h" });
        var minecraft = Seed(new User { Id = 2, Username = "Steve", Uuid = "uuid-2" });
        ClaimCodeFor("uuid-2", "Steve");

        var result = await _service.LinkMinecraftAccountAsync(web.Id, "7Q2HM4XA");

        Assert.True(result.Merged);
        Assert.Equal(2, result.User.Id);
        Assert.Equal("steve@example.com", minecraft.Email);
    }

    [Theory]
    [InlineData(LinkCodePurpose.PasswordReset)]
    [InlineData(LinkCodePurpose.Link)]
    public async Task ACodeWithoutAMinecraftIdentity_IsInvalid(LinkCodePurpose purpose)
    {
        // A reset-token hash, or a link code generated for a web-only account (no UUID).
        var web = Seed(new User { Id = 1, Username = "web-1", Email = "steve@example.com", PasswordHash = "h" });
        var webOnly = Seed(new User { Id = 4, Username = "other-web" });
        var code = new LinkCode { Code = "ABCD2345", UserId = purpose == LinkCodePurpose.Link ? webOnly.Id : web.Id, Purpose = purpose };
        _codes.Setup(c => c.ValidateLinkCodeAsync("ABCD2345")).ReturnsAsync((true, code, (string?)null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.LinkMinecraftAccountAsync(web.Id, "ABCD2345"));

        _codes.Verify(c => c.ConsumeLinkCodeAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task WrongCodes_CountAgainstTheAccount_AndTheBlockRefusesEvenAValidCode()
    {
        var web = Seed(new User { Id = 1, Username = "web-1", Email = "steve@example.com", PasswordHash = "h" });
        var minecraft = Seed(new User { Id = 2, Username = "Steve", Uuid = "uuid-2" });
        InvalidCode("WRONG001");
        LinkCodeFor(minecraft);

        for (var i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => _service.LinkMinecraftAccountAsync(web.Id, "WRONG001"));
        }

        var blocked = await Assert.ThrowsAsync<LinkAttemptsExceededException>(() => _service.LinkMinecraftAccountAsync(web.Id, "ABCD2345"));
        Assert.InRange(blocked.Remaining, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));
        _codes.Verify(c => c.ValidateLinkCodeAsync("ABCD2345"), Times.Never); // not even looked up
        Assert.Null(minecraft.Email);
    }

    [Fact]
    public async Task ASuccessResetsTheFailureCount()
    {
        var web = Seed(new User { Id = 1, Username = "web-1", Email = "steve@example.com", PasswordHash = "h" });
        var minecraft = Seed(new User { Id = 2, Username = "Steve", Uuid = "uuid-2" });
        InvalidCode("WRONG001");
        LinkCodeFor(minecraft);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.LinkMinecraftAccountAsync(web.Id, "WRONG001"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.LinkMinecraftAccountAsync(web.Id, "WRONG001"));

        await _service.LinkMinecraftAccountAsync(web.Id, "ABCD2345");

        // Two more wrong codes would have locked a count that hadn't been reset.
        var again = Seed(new User { Id = 1, Username = "web-1" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.LinkMinecraftAccountAsync(again.Id, "WRONG001"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.LinkMinecraftAccountAsync(again.Id, "WRONG001"));
        Assert.False(_limiter.IsLocked(1, out _));
    }

    [Fact]
    public async Task TheBlockIsPerAccount()
    {
        Seed(new User { Id = 1, Username = "web-1" });
        Seed(new User { Id = 5, Username = "web-5" });
        InvalidCode("WRONG001");
        for (var i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => _service.LinkMinecraftAccountAsync(1, "WRONG001"));
        }

        await Assert.ThrowsAsync<LinkAttemptsExceededException>(() => _service.LinkMinecraftAccountAsync(1, "WRONG001"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.LinkMinecraftAccountAsync(5, "WRONG001"));
    }
}
