using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Invitations;
using knkwebapi_v2.Tests.Api;
using knkwebapi_v2.Tests.Services.Invitations;

namespace knkwebapi_v2.Tests.Security;

/// <summary>
/// KNG-214 (IMPLEMENTATION_PLAN.md § 6.1) on the auth harness with real repositories, the real
/// InvitationService, UserService, LinkCodeService and strict mail rendering: claim linking (case A
/// sets the identity, grants SIGNUP_GRANT once, mails and notifies; a non-admitted web account is
/// refused), register with an invite and a claim code (game-first), staff Admit and Detach web login,
/// and the plugin's create refused in invite mode.
/// </summary>
public class ClaimLinkingTests : IDisposable
{
    private const string Uuid = "0f6b2c8e-1d3a-4b5c-9e7f-001122334455";
    private const string ClaimCode = "7Q2HM4XA";

    private readonly AuthTestHarness _h;
    private readonly InvitationTestDb _t;
    private readonly InvitationService _invitations;
    private readonly Mock<ICurrencyService> _currency = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly InMemoryPlayerNotificationQueue _notifications = new();
    private readonly LinkAttemptLimiter _limiter;

    public ClaimLinkingTests()
    {
        _h = new AuthTestHarness(s => s.Registration.Mode = RegistrationMode.Invite);
        _t = new InvitationTestDb(_h.Db);
        _t.Security.Registration.Mode = RegistrationMode.Invite;
        _invitations = _t.Service();
        _limiter = new LinkAttemptLimiter(new MemoryCache(new MemoryCacheOptions()), Options.Create(_h.Settings));
        _currency.Setup(c => c.PostAsync(It.IsAny<IReadOnlyList<CurrencyLeg>>(), It.IsAny<CurrencyContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PostingResult)null!);
    }

    public void Dispose() => _h.Dispose();

    private UserService Users() => new(_h.Users, _h.Mapper.Object, _h.Passwords,
        new LinkCodeService(new LinkCodeRepository(_h.Db), _h.Users, _h.Mapper.Object, Options.Create(_h.Settings)),
        Mock.Of<ITitleService>(t => t.ResolveAsync(It.IsAny<int>(), It.IsAny<Gender?>()) == Task.FromResult(new TitleResolutionDto())),
        new Mock<IUserPermissionGroupService>().Object, _audit.Object, new Mock<IPermissionGroupRepository>().Object,
        NullLogger<UserService>.Instance, _currency.Object, new Mock<ITitleProgressionService>().Object,
        _notifications, _h.Revocation, _limiter, Options.Create(_h.Settings), new InvitationRepository(_h.Db), _t.Composer());

    private AuthService Auth() => _h.CreateAuthService(_invitations, Users());

    private UsersController Controller(HttpContextKind caller, int callerId = 0, SecuritySettings? security = null) =>
        new(Users(), _h.Mapper.Object, new Mock<IPermissionResolutionService>().Object,
            new Mock<ISalaryService>().Object, new Mock<IUserProfileSummaryService>().Object,
            new Mock<IUserPermissionGroupService>().Object, new Mock<IPermissionGrantService>().Object, new Mock<IPermissionEscalationGuard>().Object,
            _h.CreateAuthService(), Mock.Of<IWebHostEnvironment>(e => e.EnvironmentName == "Development"), Options.Create(security ?? _h.Settings))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = caller switch
                {
                    HttpContextKind.Plugin => ServiceAuthTestHelper.Plugin(),
                    _ => ServiceAuthTestHelper.WebUser(callerId)
                }
            }
        };

    public enum HttpContextKind { WebUser, Plugin }

    private async Task AddClaimCodeAsync(string uuid = Uuid, string name = "Steve", string code = ClaimCode, DateTime? expiresAt = null)
    {
        _h.Db.LinkCodes.Add(new LinkCode
        {
            Code = code, Purpose = LinkCodePurpose.Claim, MinecraftUuid = uuid, MinecraftName = name,
            CreatedAt = DateTime.UtcNow, ExpiresAt = expiresAt ?? DateTime.UtcNow.AddMinutes(15), Status = LinkCodeStatus.Active
        });
        await _h.Db.SaveChangesAsync();
    }

    private async Task<SharedInvitationResultDto> SharedAsync() =>
        (await _invitations.CreateSharedAsync(_t.Admin.Id, new CreateSharedInvitationRequestDto { Label = "Discord", MaxUses = 5 })).Value!;

    private static AuthRegisterRequestDto Request(string email, string? inviteCode, string? claimCode, string? linkCode = null) =>
        new() { Email = email, Password = AuthTestHarness.Password, PasswordConfirmation = AuthTestHarness.Password,
            InviteCode = inviteCode, ClaimCode = claimCode, LinkCode = linkCode };

    private static JsonElement Body(IActionResult result) =>
        JsonDocument.Parse(JsonSerializer.Serialize(((ObjectResult)result).Value)).RootElement;

    private void VerifySignupGrant(int userId, Times times) =>
        _currency.Verify(c => c.PostAsync(It.IsAny<IReadOnlyList<CurrencyLeg>>(),
            It.Is<CurrencyContext>(ctx => ctx.IdempotencyKey == $"signup:{userId}"), It.IsAny<CancellationToken>()), times);

    // ===== Claim linking (POST api/Users/link-minecraft-account) =====

    [Fact]
    public async Task CaseA_SetsTheIdentity_GrantsTheStartingBalanceOnce_MailsAndNotifies()
    {
        var shell = await _h.AddUserAsync(username: "web-1a2b3c", email: "steve@example.com", uuid: null);
        shell.AdmittedAt = DateTime.UtcNow;
        await _h.Db.SaveChangesAsync();
        await AddClaimCodeAsync();

        // Typed off the disconnect screen: lower case, hyphen.
        var result = await Controller(HttpContextKind.WebUser, shell.Id).LinkMinecraftAccount(new LinkMinecraftAccountDto { LinkCode = "7q2h-m4xa" });

        Assert.IsType<OkObjectResult>(result);
        var saved = await _h.Db.Users.SingleAsync(u => u.Id == shell.Id);
        Assert.Equal(Uuid, saved.Uuid);
        Assert.Equal("Steve", saved.Username);
        Assert.NotNull(saved.AdmittedAt);
        Assert.Equal(LinkCodeStatus.Used, (await _h.Db.LinkCodes.SingleAsync()).Status);
        VerifySignupGrant(shell.Id, Times.Once());
        _audit.Verify(a => a.RecordAsync(shell.Id, shell.Id, AuditAction.MinecraftLinked, It.IsAny<string?>()), Times.Once);
        var mail = await _h.Db.EmailOutbox.SingleAsync(m => m.Kind == "MinecraftLinked");
        Assert.Equal("steve@example.com", mail.ToAddress);
        var notice = Assert.Single(_notifications.GetPending(), n => n.Type == PlayerNotificationTypes.AccountLinked);
        Assert.Equal(Uuid, notice.Uuid);
        Assert.Equal("Steve", notice.Username);
        Assert.Equal("st***@example.com", notice.AccountLinked!.MaskedEmail);

        // The same code again: spent, and nothing is granted twice.
        var again = await Controller(HttpContextKind.WebUser, shell.Id).LinkMinecraftAccount(new LinkMinecraftAccountDto { LinkCode = ClaimCode });
        Assert.IsType<BadRequestObjectResult>(again);
        VerifySignupGrant(shell.Id, Times.Once());
    }

    [Fact]
    public async Task AClaimCode_OnAWebAccountThatWasNeverAdmitted_Is403InviteRequired()
    {
        var web = await _h.AddUserAsync(username: "web-zz", email: "steve@example.com", uuid: null);
        await AddClaimCodeAsync();

        var result = await Controller(HttpContextKind.WebUser, web.Id).LinkMinecraftAccount(new LinkMinecraftAccountDto { LinkCode = ClaimCode });

        Assert.Equal(403, ((ObjectResult)result).StatusCode);
        Assert.Equal("InviteRequired", Body(result).GetProperty("error").GetString());
        Assert.Equal(LinkCodeStatus.Active, (await _h.Db.LinkCodes.SingleAsync()).Status);
        Assert.Null((await _h.Db.Users.SingleAsync(u => u.Id == web.Id)).Uuid);
        VerifySignupGrant(web.Id, Times.Never());
    }

    [Fact]
    public async Task AClaimCode_ForAUuidLinkedMeanwhile_WithAWebLogin_Is409AlreadyRegistered()
    {
        var shell = await _h.AddUserAsync(username: "web-1", email: "second@example.com", uuid: null);
        shell.AdmittedAt = DateTime.UtcNow;
        await _h.Db.SaveChangesAsync();
        await AddClaimCodeAsync();
        await _h.AddUserAsync(username: "Steve", email: "steve@example.com", uuid: Uuid);

        var result = await Controller(HttpContextKind.WebUser, shell.Id).LinkMinecraftAccount(new LinkMinecraftAccountDto { LinkCode = ClaimCode });

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal("AlreadyRegistered", Body(result).GetProperty("error").GetString());
    }

    [Fact]
    public async Task AClaimCode_ForAMinecraftOnlyAccount_FoldsAndNotifiesTheSurvivor()
    {
        var shell = await _h.AddUserAsync(username: "web-1", email: "steve@example.com", uuid: null);
        shell.AdmittedAt = DateTime.UtcNow;
        await _h.Db.SaveChangesAsync();
        var minecraft = await _h.AddUserAsync(username: "Steve", email: null, password: null, uuid: Uuid);
        await AddClaimCodeAsync();

        var result = await Controller(HttpContextKind.WebUser, shell.Id).LinkMinecraftAccount(new LinkMinecraftAccountDto { LinkCode = ClaimCode });

        var body = Body(Assert.IsType<OkObjectResult>(result));
        Assert.True(body.GetProperty("merged").GetBoolean());
        var survivor = await _h.Db.Users.SingleAsync(u => u.Id == minecraft.Id);
        Assert.Equal("steve@example.com", survivor.Email);
        Assert.NotNull(survivor.AdmittedAt);
        _audit.Verify(a => a.RecordAsync(shell.Id, minecraft.Id, AuditAction.MinecraftLinked, It.IsAny<string?>()), Times.Once);
        Assert.Single(_notifications.GetPending(), n => n.Type == PlayerNotificationTypes.AccountLinked && n.UserId == minecraft.Id);
    }

    // ===== Register with an invite and a claim code (game-first) =====

    [Fact]
    public async Task Register_WithInviteAndClaimCode_CreatesTheAccountLinked_InOneStep()
    {
        var shared = await SharedAsync();
        await AddClaimCodeAsync();

        var outcome = await Auth().RegisterAsync(Request("steve@example.com", shared.Code, "7Q2H-M4XA"));

        Assert.True(outcome.Ok, outcome.Message);
        Assert.True(outcome.Result!.Linked);
        Assert.Equal("Steve", outcome.Result.MinecraftName);
        Assert.NotNull(outcome.Result.Session);
        var user = await _h.Db.Users.SingleAsync(u => u.Email == "steve@example.com");
        Assert.Equal(Uuid, user.Uuid);
        Assert.Equal("Steve", user.Username);
        Assert.NotNull(user.AdmittedAt);
        Assert.NotNull(user.InvitedViaInvitationId);
        Assert.Equal(1, (await _h.Db.Invitations.SingleAsync()).UseCount);
        Assert.Equal(LinkCodeStatus.Used, (await _h.Db.LinkCodes.SingleAsync()).Status);
        VerifySignupGrant(user.Id, Times.Once());
        Assert.Contains(_h.Db.EmailOutbox, m => m.Kind == "Welcome");
        Assert.Contains(_h.Db.EmailOutbox, m => m.Kind == "MinecraftLinked");

        // The JSON carries linked and minecraftName.
        var json = JsonSerializer.Serialize(outcome.Result);
        Assert.Contains("\"linked\":true", json);
        Assert.Contains("\"minecraftName\":\"Steve\"", json);
    }

    [Fact]
    public async Task Register_WithAWrongClaimCode_Is400_AndTakesNoUse()
    {
        var shared = await SharedAsync();

        var outcome = await Auth().RegisterAsync(Request("steve@example.com", shared.Code, "ZZZZ-ZZZZ"));

        Assert.Equal(AuthRegisterOutcome.InvalidClaimCode, outcome.Error);
        Assert.Equal(0, (await _h.Db.Invitations.SingleAsync()).UseCount);
        Assert.False(await _h.Db.Users.AnyAsync(u => u.Email == "steve@example.com"));
    }

    [Fact]
    public async Task Register_WithAnExpiredClaimCode_Is400()
    {
        var shared = await SharedAsync();
        await AddClaimCodeAsync(expiresAt: DateTime.UtcNow.AddMinutes(-1));

        var outcome = await Auth().RegisterAsync(Request("steve@example.com", shared.Code, ClaimCode));

        Assert.Equal(AuthRegisterOutcome.InvalidClaimCode, outcome.Error);
        Assert.Equal(0, (await _h.Db.Invitations.SingleAsync()).UseCount);
    }

    [Fact]
    public async Task Register_WithAClaimCodeForAnAccountWithAWebLogin_Is409_AndTakesNoUse()
    {
        var shared = await SharedAsync();
        await _h.AddUserAsync(username: "Steve", email: "steve@example.com", uuid: Uuid);
        await AddClaimCodeAsync();

        var outcome = await Auth().RegisterAsync(Request("other@example.com", shared.Code, ClaimCode));

        Assert.Equal("AlreadyRegistered", outcome.Error);
        Assert.Equal(0, (await _h.Db.Invitations.SingleAsync()).UseCount);
    }

    [Fact]
    public async Task Register_WithAClaimCodeForAMinecraftOnlyAccount_FoldsAndLogsIntoIt()
    {
        var shared = await SharedAsync();
        var minecraft = await _h.AddUserAsync(username: "Steve", email: null, password: null, uuid: Uuid);
        await AddClaimCodeAsync();

        var outcome = await Auth().RegisterAsync(Request("steve@example.com", shared.Code, ClaimCode));

        Assert.True(outcome.Ok, outcome.Message);
        Assert.Equal(minecraft.Id, outcome.Result!.User.Id);
        var survivor = await _h.Db.Users.SingleAsync(u => u.Id == minecraft.Id);
        Assert.Equal("steve@example.com", survivor.Email);
        Assert.NotNull(survivor.AdmittedAt);
        Assert.NotNull(survivor.InvitedViaInvitationId);
    }

    [Fact]
    public async Task Register_WithAClaimCodeButNoInvite_Is403InviteRequired()
    {
        await AddClaimCodeAsync();

        var outcome = await Auth().RegisterAsync(Request("steve@example.com", null, ClaimCode));

        Assert.Equal(AuthRegisterOutcome.InviteRequired, outcome.Error);
    }

    [Fact]
    public async Task Register_WithAClaimCodeAndALinkCode_IsInvalidRequest()
    {
        var shared = await SharedAsync();

        var outcome = await Auth().RegisterAsync(Request("steve@example.com", shared.Code, ClaimCode, linkCode: "ABCD2345"));

        Assert.Equal("InvalidRequest", outcome.Error);
    }

    [Fact]
    public async Task Register_WithAClaimCode_UnderModeLinkCode_IsInvitesDisabled()
    {
        _h.Settings.Registration.Mode = RegistrationMode.LinkCode;

        var outcome = await Auth().RegisterAsync(Request("steve@example.com", "KNK-AAAAA-BBBBB", ClaimCode));

        Assert.Equal(AuthRegisterOutcome.InvitesDisabled, outcome.Error);
    }

    // ===== Staff: Admit and Detach web login =====

    [Fact]
    public async Task Admit_SetsAdmittedAt_AndAuditsOnce()
    {
        var player = await _h.AddUserAsync(username: "Alex", email: null, password: null, uuid: Uuid);

        var first = await Controller(HttpContextKind.WebUser, _t.Admin.Id).Admit(player.Id);
        var second = await Controller(HttpContextKind.WebUser, _t.Admin.Id).Admit(player.Id);

        Assert.True(Body(Assert.IsType<OkObjectResult>(first)).GetProperty("changed").GetBoolean());
        Assert.False(Body(Assert.IsType<OkObjectResult>(second)).GetProperty("changed").GetBoolean());
        Assert.NotNull((await _h.Db.Users.SingleAsync(u => u.Id == player.Id)).AdmittedAt);
        _audit.Verify(a => a.RecordAsync(_t.Admin.Id, player.Id, AuditAction.PlayerAdmitted, It.IsAny<string?>()), Times.Once);
        Assert.IsType<NotFoundObjectResult>(await Controller(HttpContextKind.WebUser, _t.Admin.Id).Admit(9999));
    }

    [Fact]
    public async Task DetachWebLogin_ClearsTheLogin_RevokesSessions_AndKeepsTheMinecraftAccount()
    {
        var player = await _h.AddUserAsync(username: "Steve", email: "steve@example.com", uuid: Uuid);
        player.EmailVerified = true;
        player.EmailVerifiedAt = DateTime.UtcNow;
        player.LastPasswordChangeAt = DateTime.UtcNow;
        await _h.Db.SaveChangesAsync();
        var session = await _h.CreateAuthService().StartSessionAsync(player.Id);

        var result = await Controller(HttpContextKind.WebUser, _t.Admin.Id).DetachWebLogin(player.Id);

        Assert.IsType<OkObjectResult>(result);
        var saved = await _h.Db.Users.SingleAsync(u => u.Id == player.Id);
        Assert.Null(saved.Email);
        Assert.Null(saved.PasswordHash);
        Assert.Null(saved.EmailVerifiedAt);
        Assert.False(saved.EmailVerified);
        Assert.Null(saved.LastPasswordChangeAt);
        Assert.Equal(Uuid, saved.Uuid);
        Assert.True(saved.IsActive);
        Assert.NotNull(await _h.BearerProblemAsync(session!.AccessToken));
        _audit.Verify(a => a.RecordAsync(_t.Admin.Id, player.Id, AuditAction.MinecraftUnlinked,
            It.Is<string?>(d => d != null && d.Contains("st***@example.com") && !d.Contains("steve@example.com"))), Times.Once);

        // Nothing left to detach.
        var again = await Controller(HttpContextKind.WebUser, _t.Admin.Id).DetachWebLogin(player.Id);
        Assert.Equal("NoWebLogin", Body(Assert.IsType<ConflictObjectResult>(again)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task DetachWebLogin_OfAWebOnlyAccount_Is409NotLinked()
    {
        var shell = await _h.AddUserAsync(username: "web-1", email: "steve@example.com", uuid: null);

        var result = await Controller(HttpContextKind.WebUser, _t.Admin.Id).DetachWebLogin(shell.Id);

        Assert.Equal("NotLinked", Body(Assert.IsType<ConflictObjectResult>(result)).GetProperty("error").GetString());
        Assert.Equal("steve@example.com", (await _h.Db.Users.SingleAsync(u => u.Id == shell.Id)).Email);
    }

    // ===== POST api/Users from the plugin in invite mode =====

    [Fact]
    public async Task PluginCreate_InInviteMode_RefusesAUuidWithoutAnAdmittedAccount()
    {
        var result = await Controller(HttpContextKind.Plugin).Create(new UserCreateDto { Username = "Stranger", Uuid = Uuid });

        Assert.Equal(403, ((ObjectResult)result).StatusCode);
        Assert.Equal("NotAdmitted", Body(result).GetProperty("error").GetString());
        Assert.False(await _h.Db.Users.AnyAsync(u => u.Uuid == Uuid));
    }

    [Fact]
    public async Task PluginCreate_InLinkCodeMode_DoesNotAskForAdmission()
    {
        var security = new SecuritySettings();
        Assert.Equal(RegistrationMode.LinkCode, security.Registration.Mode);
        var users = new Mock<IUserService>();
        users.Setup(u => u.RunInTransactionAsync(It.IsAny<Func<Task<IActionResult>>>(), It.IsAny<Func<IActionResult, bool>>()))
            .Returns((Func<Task<IActionResult>> work, Func<IActionResult, bool> _) => work());
        users.Setup(u => u.ValidateUserCreationAsync(It.IsAny<UserCreateDto>(), It.IsAny<int?>())).ReturnsAsync((true, (string?)null));
        users.Setup(u => u.CreateAsync(It.IsAny<UserCreateDto>())).ReturnsAsync(new UserDto { Id = 42, Username = "Stranger", Uuid = Uuid });
        var controller = new UsersController(users.Object, _h.Mapper.Object, new Mock<IPermissionResolutionService>().Object,
            new Mock<ISalaryService>().Object, new Mock<IUserProfileSummaryService>().Object,
            new Mock<IUserPermissionGroupService>().Object, new Mock<IPermissionGrantService>().Object, new Mock<IPermissionEscalationGuard>().Object,
            null, null, Options.Create(security))
        {
            ControllerContext = new ControllerContext { HttpContext = ServiceAuthTestHelper.Plugin() }
        };

        var result = await controller.Create(new UserCreateDto { Username = "Stranger", Uuid = Uuid });

        Assert.IsType<CreatedAtRouteResult>(result);
        users.Verify(u => u.IsUuidAdmittedAsync(It.IsAny<string>()), Times.Never);
    }
}
