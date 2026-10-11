using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using AutoMapper;
using Moq;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Tests.Api;
using Xunit;

namespace knkwebapi_v2.Tests.Security;

/// <summary>
/// Closed-alpha hardening WP6: uniform login failures, the lockout, rate limits, check-duplicate,
/// email change rules, one password policy and the forgot-password queue.
/// </summary>
public class AccountSecurityTests : IDisposable
{
    private readonly AuthTestHarness _h = new();

    public void Dispose() => _h.Dispose();

    // ===== Uniform failures (6.1) =====

    [Fact]
    public async Task InactiveAccount_WithTheRightPassword_GetsTheGenericMessage()
    {
        await _h.AddUserAsync(isActive: false);
        var (ok, _, error, locked) = await _h.CreateAuthService().LoginAsync("Steve", AuthTestHarness.Password, false);
        Assert.False(ok);
        Assert.Equal("Invalid credentials.", error);
        Assert.Null(locked);
    }

    [Fact]
    public async Task UnknownName_AndWrongPassword_LookAlike()
    {
        await _h.AddUserAsync();
        var auth = _h.CreateAuthService();
        var (_, _, unknown, _) = await auth.LoginAsync("nobody", "Some-Password-1", false);
        var (_, _, wrong, _) = await auth.LoginAsync("Steve", "Some-Password-1", false);
        Assert.Equal(unknown, wrong);
    }

    // ===== Lockout (6.2) =====

    [Fact]
    public async Task FiveFailures_LockTheAccount_EvenForTheRightPassword()
    {
        await _h.AddUserAsync();
        var auth = _h.CreateAuthService();
        for (var i = 0; i < 5; i++)
        {
            var (_, _, error, locked) = await auth.LoginAsync("Steve", "Wrong-Password-1", false);
            Assert.Equal("Invalid credentials.", error);
            Assert.Null(locked);
        }

        // The same account by email is locked too (keyed by account, not by identifier).
        var (ok, _, message, lockedFor) = await auth.LoginAsync("steve@example.com", AuthTestHarness.Password, false);
        Assert.False(ok);
        Assert.NotNull(lockedFor);
        Assert.Equal("Too many attempts. Try again in 15 minutes.", message);
    }

    [Fact]
    public async Task ASuccess_ResetsTheCount()
    {
        await _h.AddUserAsync();
        var auth = _h.CreateAuthService();
        for (var i = 0; i < 4; i++) await auth.LoginAsync("Steve", "Wrong-Password-1", false);
        Assert.True((await auth.LoginAsync("Steve", AuthTestHarness.Password, false)).Ok);
        for (var i = 0; i < 4; i++) await auth.LoginAsync("Steve", "Wrong-Password-1", false);
        Assert.True((await auth.LoginAsync("Steve", AuthTestHarness.Password, false)).Ok);
    }

    [Fact]
    public async Task UnknownNames_AreLockedToo()
    {
        var auth = _h.CreateAuthService();
        for (var i = 0; i < 5; i++) await auth.LoginAsync("ghost", "Wrong-Password-1", false);
        Assert.NotNull((await auth.LoginAsync("GHOST", "Wrong-Password-1", false)).LockedFor);
    }

    [Theory]
    [InlineData(1, "Too many attempts. Try again in 1 minute.")]
    [InlineData(14.2, "Too many attempts. Try again in 15 minutes.")]
    public void LockedMessage_IsFriendly(double minutes, string expected)
    {
        Assert.Equal(expected, LoginAttemptLimiter.LockedMessage(TimeSpan.FromMinutes(minutes)));
    }

    [Fact]
    public async Task LoginController_LockedIs429TooManyAttempts_WithRetryAfter()
    {
        var auth = new Mock<IAuthService>();
        auth.Setup(a => a.LoginAsync("Steve", "x", false, It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync((false, (AuthLoginResponseDto?)null, "Too many attempts. Try again in 3 minutes.", TimeSpan.FromMinutes(3)));
        var controller = AuthControllerFor(auth.Object, new DefaultHttpContext());

        var result = Assert.IsType<ObjectResult>(await controller.Login(new AuthLoginRequestDto { Login = "Steve", Password = "x" }));

        Assert.Equal(429, result.StatusCode);
        Assert.Contains("TooManyAttempts", JsonSerializer.Serialize(result.Value));
        Assert.Equal("180", controller.Response.Headers.RetryAfter.ToString());
    }

    private static AuthController AuthControllerFor(IAuthService auth, HttpContext http, string environment = "Production")
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(environment);
        return new AuthController(auth, new Mock<ITokenService>().Object, env.Object, NullLogger<AuthController>.Instance,
            new Mock<IUserSessionStateCache>().Object, Options.Create(new SecuritySettings()))
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }

    // ===== Rate limits (6.3) =====

    [Fact]
    public async Task AuthPolicy_RefusesThe11thRequestInAMinute_With429AndRetryAfter()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddKnkRateLimiting(builder.Configuration);
        await using var app = builder.Build();
        app.UseRateLimiter();
        app.MapPost("/login", () => "ok").RequireRateLimiting(RateLimitingSetup.AuthPolicy);
        await app.StartAsync();
        var client = app.GetTestClient();

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/login", null)).StatusCode);
        }
        var refused = await client.PostAsync("/login", null);
        Assert.Equal((HttpStatusCode)429, refused.StatusCode);
        Assert.True(refused.Headers.RetryAfter != null);
        Assert.Contains("TooManyRequests", await refused.Content.ReadAsStringAsync());
    }

    // ===== check-duplicate (6.4) =====

    private UsersController UsersControllerFor(HttpContext http, Mock<IUserService> users, Mock<IPermissionResolutionService>? permissions = null) =>
        new(users.Object, new Mock<IMapper>().Object, (permissions ?? new Mock<IPermissionResolutionService>()).Object,
            new Mock<ISalaryService>().Object, new Mock<IUserProfileSummaryService>().Object,
            new Mock<IUserPermissionGroupService>().Object, new Mock<IPermissionGrantService>().Object, new Mock<IPermissionEscalationGuard>().Object)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };

    [Fact]
    public async Task CheckDuplicate_Anonymous_GetsOnlyAvailability()
    {
        var users = new Mock<IUserService>();
        users.Setup(u => u.CheckEmailTakenAsync("a@b.c", null)).ReturnsAsync((true, 5));

        var get = Assert.IsType<OkObjectResult>(await UsersControllerFor(ServiceAuthTestHelper.Anonymous(), users).CheckDuplicateAvailability("a@b.c", null, null));
        Assert.Equal("{\"available\":false}", JsonSerializer.Serialize(get.Value));

        var post = Assert.IsType<OkObjectResult>(await UsersControllerFor(ServiceAuthTestHelper.Anonymous(), users).CheckDuplicate(new DuplicateCheckDto { Email = "a@b.c" }));
        Assert.Equal("{\"available\":false}", JsonSerializer.Serialize(post.Value));
    }

    [Fact]
    public async Task CheckDuplicate_PluginAndStaff_KeepTheId()
    {
        var users = new Mock<IUserService>();
        users.Setup(u => u.CheckUsernameTakenAsync("Steve", null)).ReturnsAsync((true, 5));
        var plugin = Assert.IsType<OkObjectResult>(await UsersControllerFor(ServiceAuthTestHelper.Plugin(), users).CheckDuplicateAvailability(null, "Steve", null));
        Assert.Contains("\"conflictingUserId\":5", JsonSerializer.Serialize(plugin.Value));

        var permissions = new Mock<IPermissionResolutionService>();
        permissions.Setup(p => p.CheckAsync(9, StaffPermissions.ManageUsers))
            .ReturnsAsync(new PermissionCheckResponseDto { UserId = 9, Node = StaffPermissions.ManageUsers, Result = PermissionResolutionResult.Granted });
        var staff = Assert.IsType<OkObjectResult>(await UsersControllerFor(ServiceAuthTestHelper.WebUser(9), users, permissions).CheckDuplicateAvailability(null, "Steve", null));
        Assert.Contains("\"conflictingUserId\":5", JsonSerializer.Serialize(staff.Value));
    }

    [Fact]
    public async Task CheckDuplicate_ByUuid_IsPluginOrStaffOnly()
    {
        var users = new Mock<IUserService>();
        users.Setup(u => u.CheckForDuplicateAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((false, null));
        var request = new DuplicateCheckDto { Uuid = "uuid-1", Username = "Steve" };

        Assert.IsType<UnauthorizedObjectResult>(await UsersControllerFor(ServiceAuthTestHelper.Anonymous(), users).CheckDuplicate(request));
        Assert.Equal(403, (await UsersControllerFor(ServiceAuthTestHelper.WebUser(7), users).CheckDuplicate(request) as ObjectResult)?.StatusCode);
        Assert.IsType<OkObjectResult>(await UsersControllerFor(ServiceAuthTestHelper.Plugin(), users).CheckDuplicate(new DuplicateCheckDto()));
    }

    // ===== Email change (6.5) and the password policy (6.6) =====

    [Fact]
    public async Task EmailChange_NeedsTheCurrentPassword()
    {
        var user = await _h.AddUserAsync();
        var (ok, _, error) = await _h.CreateAuthService().UpdateUserAsync(user.Id, new AuthUpdateRequestDto { Email = "new@example.com" });
        Assert.False(ok);
        Assert.Contains("current password", error, StringComparison.OrdinalIgnoreCase);

        var (wrongOk, _, _) = await _h.CreateAuthService().UpdateUserAsync(user.Id, new AuthUpdateRequestDto { Email = "new@example.com", CurrentPassword = "Wrong-Password-1" });
        Assert.False(wrongOk);
    }

    [Fact]
    public async Task EmailChange_ChecksFormatAndDuplicates()
    {
        await _h.AddUserAsync(username: "Alex", email: "alex@example.com", uuid: "uuid-2");
        var user = await _h.AddUserAsync();
        var auth = _h.CreateAuthService();

        Assert.False((await auth.UpdateUserAsync(user.Id, new AuthUpdateRequestDto { Email = "not-an-email", CurrentPassword = AuthTestHarness.Password })).Ok);
        Assert.False((await auth.UpdateUserAsync(user.Id, new AuthUpdateRequestDto { Email = "ALEX@example.com", CurrentPassword = AuthTestHarness.Password })).Ok);
    }

    [Fact]
    public async Task EmailChange_RevokesSessions_ReturnsAFreshToken_AndNoticesTheOldAddress()
    {
        var user = await _h.AddUserAsync();
        var auth = _h.CreateAuthService();
        var (_, login, _, _) = await auth.LoginAsync("Steve", AuthTestHarness.Password, false);

        var (ok, result, error) = await auth.UpdateUserAsync(user.Id, new AuthUpdateRequestDto { Email = "steve.new@example.com", CurrentPassword = AuthTestHarness.Password });

        Assert.True(ok, error);
        Assert.NotNull(await _h.BearerProblemAsync(login!.AccessToken));
        Assert.Null(await _h.BearerProblemAsync(result!.AccessToken!));
        var notice = Assert.Single(_h.Mail);
        Assert.Equal(AccountMailKind.EmailChangedNotice, notice.Kind);
        Assert.Equal("steve@example.com", notice.To);
        Assert.Equal("st***@example.com", notice.Payload);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("password1")]
    [InlineData("abc12345")]
    public async Task PasswordChange_UsesTheSharedPolicy(string newPassword)
    {
        var user = await _h.AddUserAsync();
        var (ok, _, _) = await _h.CreateAuthService().UpdateUserAsync(user.Id, new AuthUpdateRequestDto { NewPassword = newPassword, CurrentPassword = AuthTestHarness.Password });
        Assert.False(ok);
    }

    // ===== Forgot-password (6.7) =====

    [Fact]
    public async Task ForgotPassword_QueuesTheMail_AndAnswersTheSameForUnknownEmails()
    {
        await _h.AddUserAsync();
        var auth = _h.CreateAuthService();

        var known = await auth.RequestPasswordResetAsync("steve@example.com", "1.2.3.4", "ua", false);
        var unknown = await auth.RequestPasswordResetAsync("nobody@example.com", "1.2.3.4", "ua", false);

        Assert.Equal(known.Message, unknown.Message);
        Assert.Null(known.DebugResetToken);
        var mail = Assert.Single(_h.Mail);
        Assert.Equal(AccountMailKind.PasswordReset, mail.Kind);
        _h.Delivery.Verify(d => d.SendPasswordResetAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task MailSender_LogsSmtpFailures_InsteadOfThrowing()
    {
        var delivery = new Mock<IPasswordResetDeliveryService>();
        delivery.Setup(d => d.SendPasswordResetAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("smtp down"));
        var services = new ServiceCollection().AddSingleton(delivery.Object).BuildServiceProvider();
        var sender = new AccountMailSender(new AccountMailQueue(NullLogger<AccountMailQueue>.Instance),
            services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AccountMailSender>.Instance);

        await sender.SendOneAsync(new AccountMail(AccountMailKind.PasswordReset, "a@b.c", "Steve", "http://x"));
        delivery.Verify(d => d.SendPasswordResetAsync("a@b.c", "Steve", "http://x"), Times.Once);
    }

    [Theory]
    [InlineData("Development", "127.0.0.1", true)]
    [InlineData("Development", "::1", true)]
    [InlineData("Development", "192.168.1.20", false)]
    [InlineData("Production", "127.0.0.1", false)]
    public async Task DebugResetToken_OnlyForALoopbackCallerInDevelopment(string environment, string remoteIp, bool allowed)
    {
        var auth = new Mock<IAuthService>();
        auth.Setup(a => a.RequestPasswordResetAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>()))
            .ReturnsAsync(new AuthForgotPasswordResponseDto { Message = "m" });
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);

        await AuthControllerFor(auth.Object, http, environment).ForgotPassword(new AuthForgotPasswordRequestDto { Email = "a@b.c" });

        auth.Verify(a => a.RequestPasswordResetAsync("a@b.c", remoteIp, It.IsAny<string?>(), allowed), Times.Once);
    }
}
