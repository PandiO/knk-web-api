using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Invitations;
using knkwebapi_v2.Tests.Services.Invitations;
using Xunit;

namespace knkwebapi_v2.Tests.Security;

/// <summary>
/// POST api/Auth/register with invitations (KNG-212, IMPLEMENTATION_PLAN.md § 5.1 "Registration"): personal
/// and shared invites under Security:Registration:Mode=Invite, refused invites, the admitted-only link-code
/// path, and Mode=LinkCode (today's alpha) refusing invites. RegistrationAndLoginTests stay unchanged and
/// cover Mode=LinkCode without invite fields.
/// </summary>
public class InviteRegistrationTests : IDisposable
{
    private readonly AuthTestHarness _h;
    private readonly InvitationTestDb _t;
    private readonly InvitationService _invitations;

    public InviteRegistrationTests()
    {
        _h = new AuthTestHarness(s => s.Registration.Mode = RegistrationMode.Invite);
        _t = new InvitationTestDb(_h.Db);
        _t.Security.Registration.Mode = RegistrationMode.Invite;
        _invitations = _t.Service();
    }

    public void Dispose() => _h.Dispose();

    private AuthService Auth() => _h.CreateAuthService(_invitations);

    private static AuthRegisterRequestDto Request(string email, string? token = null, string? code = null, string? linkCode = null) =>
        new() { Email = email, Password = AuthTestHarness.Password, PasswordConfirmation = AuthTestHarness.Password,
            InviteToken = token, InviteCode = code, LinkCode = linkCode };

    private async Task<string> PersonalTokenAsync(string email = "tester@example.com")
    {
        var result = (await _invitations.CreatePersonalAsync(_t.Admin.Id, new CreatePersonalInvitationsRequestDto { Emails = new() { email } })).Value!.Results.Single();
        return InvitationTestDb.TokenOf(result.Link!);
    }

    private async Task<SharedInvitationResultDto> SharedAsync(int maxUses = 5, int? expiryDays = null) =>
        (await _invitations.CreateSharedAsync(_t.Admin.Id, new CreateSharedInvitationRequestDto { Label = "Discord", MaxUses = maxUses, ExpiryDays = expiryDays })).Value!;

    [Fact]
    public async Task Personal_CreatesAVerifiedWebShell_AndLogsIn()
    {
        var token = await PersonalTokenAsync();

        var outcome = await Auth().RegisterAsync(Request("Tester@Example.com", token: token));

        Assert.True(outcome.Ok, outcome.Message);
        Assert.False(string.IsNullOrEmpty(outcome.Result!.AccessToken));
        Assert.NotNull(outcome.Result.Session);
        var user = await _h.Db.Users.SingleAsync(u => u.Email == "tester@example.com");
        Assert.StartsWith("web-", user.Username);
        Assert.Null(user.Uuid);
        Assert.NotNull(user.EmailVerifiedAt);
        Assert.True(user.EmailVerified);
        Assert.NotNull(user.AdmittedAt);
        Assert.True(await _h.Passwords.VerifyPasswordAsync(AuthTestHarness.Password, user.PasswordHash!));
        Assert.Contains(_h.Db.EmailOutbox, m => m.Kind == "Welcome" && m.ToAddress == "tester@example.com");

        // Single use: the same link can't make a second account.
        var again = await Auth().RegisterAsync(Request("tester@example.com", token: token));
        Assert.Equal(AuthRegisterOutcome.InvalidInvite, again.Error);
    }

    [Fact]
    public async Task Personal_WithAnotherEmail_Is400Mismatch()
    {
        var token = await PersonalTokenAsync();

        var outcome = await Auth().RegisterAsync(Request("someone-else@example.com", token: token));

        Assert.Equal(AuthRegisterOutcome.InviteEmailMismatch, outcome.Error);
        Assert.Equal(400, await ControllerStatusAsync(outcome));
        Assert.Equal(0, (await _h.Db.Invitations.SingleAsync()).UseCount);
    }

    [Fact]
    public async Task Shared_UsesTheTypedEmail_Unverified()
    {
        var shared = await SharedAsync();

        var outcome = await Auth().RegisterAsync(Request("player@example.com", code: shared.Code.ToLowerInvariant()));

        Assert.True(outcome.Ok, outcome.Message);
        var user = await _h.Db.Users.SingleAsync(u => u.Email == "player@example.com");
        Assert.Null(user.EmailVerifiedAt);
        Assert.False(user.EmailVerified);
        Assert.Equal(1, (await _h.Db.Invitations.SingleAsync()).UseCount);
        Assert.Single(_h.Db.InvitationRedemptions);
    }

    [Fact]
    public async Task Shared_Exhausted_Expired_Paused_AndRevoked_AreRefusedAlike()
    {
        var exhausted = await SharedAsync(maxUses: 1);
        Assert.True((await Auth().RegisterAsync(Request("first@example.com", code: exhausted.Code))).Ok);
        var paused = await SharedAsync();
        await _invitations.PauseAsync(_t.Admin.Id, paused.Invitation.Id);
        var revoked = await SharedAsync();
        await _invitations.RevokeAsync(_t.Admin.Id, revoked.Invitation.Id);
        var expired = await SharedAsync(expiryDays: 1);
        _t.Clock.Advance(TimeSpan.FromDays(1));

        foreach (var code in new[] { exhausted.Code, paused.Code, revoked.Code, expired.Code, "KNK-00000-00000" })
        {
            var outcome = await Auth().RegisterAsync(Request("late@example.com", code: code));
            Assert.Equal(AuthRegisterOutcome.InvalidInvite, outcome.Error);
            Assert.Equal(400, await ControllerStatusAsync(outcome));
        }
        Assert.False(await _h.Db.Users.AnyAsync(u => u.Email == "late@example.com"));
    }

    [Fact]
    public async Task DuplicateEmail_Is409_AndTakesNoUse()
    {
        await _h.AddUserAsync("Steve", "steve@example.com");
        var shared = await SharedAsync();

        var outcome = await Auth().RegisterAsync(Request("STEVE@example.com", code: shared.Code));

        Assert.Equal("DuplicateEmail", outcome.Error);
        Assert.Equal(409, await ControllerStatusAsync(outcome));
        Assert.Equal(0, (await _h.Db.Invitations.SingleAsync()).UseCount);
    }

    [Fact]
    public async Task WeakPassword_IsRefusedBeforeTheUseIsTaken()
    {
        var shared = await SharedAsync();
        var request = Request("player@example.com", code: shared.Code);
        request.Password = request.PasswordConfirmation = "short";

        var outcome = await Auth().RegisterAsync(request);

        Assert.Equal("InvalidPassword", outcome.Error);
        Assert.Equal(0, (await _h.Db.Invitations.SingleAsync()).UseCount);
    }

    [Fact]
    public async Task LinkCodeOnly_WorksForAnAdmittedAccount()
    {
        var player = await _h.AddUserAsync(email: null, password: null);
        player.AdmittedAt = DateTime.UtcNow.AddDays(-30);
        await _h.Db.SaveChangesAsync();
        var code = await _h.AddLinkCodeAsync(player.Id);

        var outcome = await Auth().RegisterAsync(Request("steve@example.com", linkCode: code));

        Assert.True(outcome.Ok, outcome.Message);
    }

    [Fact]
    public async Task LinkCodeOnly_ForAnAccountThatIsntAdmitted_Is403InviteRequired()
    {
        var player = await _h.AddUserAsync(email: null, password: null);
        var code = await _h.AddLinkCodeAsync(player.Id);

        var outcome = await Auth().RegisterAsync(Request("steve@example.com", linkCode: code));

        Assert.Equal(AuthRegisterOutcome.InviteRequired, outcome.Error);
        Assert.Equal(403, await ControllerStatusAsync(outcome));
        Assert.Null((await _h.Db.Users.SingleAsync(u => u.Id == player.Id)).PasswordHash);
    }

    [Fact]
    public async Task NoInviteAndNoCode_Is403InviteRequired()
    {
        var outcome = await Auth().RegisterAsync(Request("steve@example.com"));

        Assert.Equal(AuthRegisterOutcome.InviteRequired, outcome.Error);
    }

    [Fact]
    public async Task InviteAndLinkCodeTogether_AreRefused()
    {
        var shared = await SharedAsync();

        var outcome = await Auth().RegisterAsync(Request("steve@example.com", code: shared.Code, linkCode: "ABCD2345"));

        Assert.Equal("InvalidRequest", outcome.Error);
    }

    [Fact]
    public async Task ClaimCode_IsNotSupportedYet()
    {
        var shared = await SharedAsync();
        var request = Request("steve@example.com", code: shared.Code);
        request.ClaimCode = "ABCD-2345";

        var outcome = await Auth().RegisterAsync(request);

        Assert.Equal(AuthRegisterOutcome.NotSupportedYet, outcome.Error);
        Assert.Equal(400, await ControllerStatusAsync(outcome));
        Assert.Equal(0, (await _h.Db.Invitations.SingleAsync()).UseCount);
    }

    [Fact]
    public async Task ModeLinkCode_RefusesInvites()
    {
        var shared = await SharedAsync();
        using var linkCodeMode = new AuthTestHarness();

        var outcome = await linkCodeMode.CreateAuthService(_invitations).RegisterAsync(Request("steve@example.com", code: shared.Code));
        var defaultMode = new SecuritySettings().Registration.Mode;

        Assert.Equal(RegistrationMode.LinkCode, defaultMode);
        Assert.Equal(AuthRegisterOutcome.InvitesDisabled, outcome.Error);
        Assert.Equal(403, await ControllerStatusAsync(outcome));
        Assert.Equal(0, (await _h.Db.Invitations.SingleAsync()).UseCount);
    }

    private static async Task<int?> ControllerStatusAsync(AuthRegisterOutcome outcome)
    {
        var auth = new Mock<IAuthService>();
        auth.Setup(a => a.RegisterAsync(It.IsAny<AuthRegisterRequestDto>(), It.IsAny<string?>(), It.IsAny<string?>())).ReturnsAsync(outcome);
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(Environments.Production);
        var controller = new AuthController(auth.Object, new Mock<ITokenService>().Object, env.Object,
            NullLogger<AuthController>.Instance, new Mock<IUserSessionStateCache>().Object, Options.Create(new SecuritySettings()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var result = await controller.Register(new AuthRegisterRequestDto());
        return (result as ObjectResult)?.StatusCode;
    }
}
