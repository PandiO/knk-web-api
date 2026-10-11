using System.Text.Json;
using AutoMapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Tests.Api;
using Xunit;

namespace knkwebapi_v2.Tests.Security;

/// <summary>
/// Closed-alpha hardening WP5: registration with a code from the game server, login by email or
/// Minecraft name, the username-squatting fix and the non-consuming link-code check.
/// </summary>
public class RegistrationAndLoginTests : IDisposable
{
    private readonly AuthTestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private static AuthRegisterRequestDto Request(string code, string email = "steve@example.com", string password = "Correct-Horse-9", string? confirmation = null) =>
        new() { LinkCode = code, Email = email, Password = password, PasswordConfirmation = confirmation ?? password };

    [Fact]
    public async Task Register_WithAGameCode_SetsEmailAndPassword_AndLogsIn()
    {
        var player = await _h.AddUserAsync(email: null, password: null);
        var code = await _h.AddLinkCodeAsync(player.Id);

        var outcome = await _h.CreateAuthService().RegisterAsync(Request("abcd-2345", email: "Steve@Example.com"));

        Assert.True(outcome.Ok, outcome.Message);
        Assert.NotNull(outcome.Result!.AccessToken);
        Assert.NotNull(outcome.Result.User);
        Assert.False(outcome.Result.Session!.RememberMe);
        Assert.Null(await _h.BearerProblemAsync(outcome.Result.AccessToken));
        var saved = await _h.Db.Users.SingleAsync();
        Assert.Equal("steve@example.com", saved.Email);
        Assert.Equal("Steve", saved.Username); // never typed: the verified Minecraft name
        Assert.True(await _h.Passwords.VerifyPasswordAsync("Correct-Horse-9", saved.PasswordHash!));
        Assert.Equal(LinkCodeStatus.Used, (await _h.Db.LinkCodes.SingleAsync(c => c.Code == code)).Status);
    }

    [Fact]
    public async Task Register_WithAnUnknownCode_IsInvalidLinkCode()
    {
        await _h.AddUserAsync(email: null, password: null);
        var outcome = await _h.CreateAuthService().RegisterAsync(Request("ZZZZ9999"));
        Assert.False(outcome.Ok);
        Assert.Equal("InvalidLinkCode", outcome.Error);
    }

    [Fact]
    public async Task Register_WithAnExpiredCode_IsInvalidLinkCode()
    {
        var player = await _h.AddUserAsync(email: null, password: null);
        await _h.AddLinkCodeAsync(player.Id, expiresAt: DateTime.UtcNow.AddMinutes(-1));
        var outcome = await _h.CreateAuthService().RegisterAsync(Request("ABCD2345"));
        Assert.Equal("InvalidLinkCode", outcome.Error);
        Assert.Null((await _h.Db.Users.SingleAsync()).PasswordHash);
    }

    [Fact]
    public async Task Register_WithACodeOfAWebOnlyAccount_IsRefused()
    {
        var webOnly = await _h.AddUserAsync(username: "webby", email: null, password: null, uuid: null);
        await _h.AddLinkCodeAsync(webOnly.Id);
        var outcome = await _h.CreateAuthService().RegisterAsync(Request("ABCD2345"));
        Assert.Equal("InvalidLinkCode", outcome.Error);
    }

    [Fact]
    public async Task Register_OnAnAlreadyRegisteredAccount_Is409AlreadyRegistered()
    {
        var player = await _h.AddUserAsync();
        await _h.AddLinkCodeAsync(player.Id);
        var outcome = await _h.CreateAuthService().RegisterAsync(Request("ABCD2345", email: "other@example.com"));
        Assert.Equal("AlreadyRegistered", outcome.Error);
        Assert.Equal(409, await ControllerStatusAsync(outcome));
    }

    [Fact]
    public async Task Register_WithATakenEmail_Is409DuplicateEmail()
    {
        await _h.AddUserAsync(username: "Alex", email: "taken@example.com", uuid: "00000000-0000-0000-0000-000000000002");
        var player = await _h.AddUserAsync(email: null, password: null);
        await _h.AddLinkCodeAsync(player.Id);
        var outcome = await _h.CreateAuthService().RegisterAsync(Request("ABCD2345", email: "TAKEN@example.com"));
        Assert.Equal("DuplicateEmail", outcome.Error);
    }

    [Theory]
    [InlineData("short1", null)]          // policy: at least 8
    [InlineData("password", null)]        // policy: common
    [InlineData("Correct-Horse-9", "x")]  // confirmation mismatch
    public async Task Register_EnforcesThePasswordPolicy(string password, string? confirmation)
    {
        var player = await _h.AddUserAsync(email: null, password: null);
        await _h.AddLinkCodeAsync(player.Id);
        var outcome = await _h.CreateAuthService().RegisterAsync(Request("ABCD2345", password: password, confirmation: confirmation));
        Assert.False(outcome.Ok);
        Assert.Contains(outcome.Error, new[] { "InvalidPassword", "PasswordMismatch" });
        // The code is still usable after a refused attempt.
        Assert.Equal(LinkCodeStatus.Active, (await _h.Db.LinkCodes.SingleAsync()).Status);
    }

    [Fact]
    public async Task Register_WithoutACode_Is403RegistrationNeedsCode()
    {
        var outcome = await _h.CreateAuthService().RegisterAsync(new AuthRegisterRequestDto { Email = "a@b.c", Password = "Correct-Horse-9", PasswordConfirmation = "Correct-Horse-9" });
        Assert.Equal(AuthRegisterOutcome.RegistrationNeedsCode, outcome.Error);
        Assert.Equal(403, await ControllerStatusAsync(outcome));
    }

    private static async Task<int?> ControllerStatusAsync(AuthRegisterOutcome outcome)
    {
        var auth = new Mock<IAuthService>();
        auth.Setup(a => a.RegisterAsync(It.IsAny<AuthRegisterRequestDto>(), It.IsAny<string?>(), It.IsAny<string?>())).ReturnsAsync(outcome);
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(Environments.Production);
        var controller = new AuthController(auth.Object, new Mock<ITokenService>().Object, env.Object,
            NullLogger<AuthController>.Instance, new Mock<IUserSessionStateCache>().Object, Options.Create(new knkwebapi_v2.Configuration.SecuritySettings()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var result = await controller.Register(new AuthRegisterRequestDto());
        return (result as ObjectResult)?.StatusCode;
    }

    [Theory]
    [InlineData("steve@example.com")]
    [InlineData("STEVE@example.com")]
    [InlineData("Steve")]
    [InlineData("steve")]
    public async Task Login_ByEmailOrMinecraftName(string login)
    {
        await _h.AddUserAsync();
        var (ok, result, error, _) = await _h.CreateAuthService().LoginAsync(login, AuthTestHarness.Password, false);
        Assert.True(ok, error);
        Assert.Equal("Steve", result!.User.Username);
    }

    [Fact]
    public async Task Login_ForAMinecraftOnlyAccount_FailsWithTheGenericMessage()
    {
        await _h.AddUserAsync(email: null, password: null);
        var (ok, _, error, _) = await _h.CreateAuthService().LoginAsync("Steve", "anything-long", false);
        Assert.False(ok);
        Assert.Equal("Invalid credentials.", error);
    }

    // ===== Username squatting (WP5.3) =====

    [Fact]
    public async Task PluginCreate_WithASquattedUsername_ReleasesItFirst()
    {
        var users = new Mock<IUserService>();
        var order = new List<string>();
        users.Setup(u => u.ReleaseUsernameFromWebOnlyAccountAsync("Steve", null)).Callback(() => order.Add("release")).ReturnsAsync(12);
        users.Setup(s => s.RunInTransactionAsync(It.IsAny<Func<Task<IActionResult>>>(), It.IsAny<Func<IActionResult, bool>>()))
            .Returns((Func<Task<IActionResult>> work, Func<IActionResult, bool> _) => work());
        users.Setup(u => u.ValidateUserCreationAsync(It.IsAny<UserCreateDto>(), It.IsAny<int?>())).ReturnsAsync((true, null));
        users.Setup(u => u.CheckUsernameTakenAsync("Steve", null)).ReturnsAsync((false, null));
        users.Setup(u => u.CheckUuidTakenAsync(It.IsAny<string>(), null)).ReturnsAsync((false, null));
        users.Setup(u => u.CreateAsync(It.IsAny<UserCreateDto>())).Callback(() => order.Add("create"))
            .ReturnsAsync(new UserDto { Id = 40, Username = "Steve", Uuid = "uuid-1" });
        var controller = new UsersController(users.Object, new Mock<IMapper>().Object, new Mock<IPermissionResolutionService>().Object,
            new Mock<ISalaryService>().Object, new Mock<IUserProfileSummaryService>().Object,
            new Mock<IUserPermissionGroupService>().Object, new Mock<IPermissionGrantService>().Object, new Mock<IPermissionEscalationGuard>().Object)
        {
            ControllerContext = new ControllerContext { HttpContext = ServiceAuthTestHelper.Plugin() }
        };

        var result = await controller.Create(new UserCreateDto { Username = "Steve", Uuid = "uuid-1" });

        Assert.IsType<CreatedAtRouteResult>(result);
        Assert.Equal(new[] { "release", "create" }, order);
        users.Verify(u => u.UpdateAsync(It.IsAny<int>(), It.IsAny<UserDto>(), It.IsAny<int?>()), Times.Never); // no linking by UUID
    }

    [Fact]
    public async Task PluginCreate_WithASquattedUsername_DoesNotCommitTheReleaseWhenCreateFails()
    {
        // Release + create run in one transaction committed only for a 201: a later 409 (here a
        // duplicate UUID) must leave the web-only account's name alone.
        var users = new Mock<IUserService>();
        bool? committed = null;
        users.Setup(u => u.RunInTransactionAsync(It.IsAny<Func<Task<IActionResult>>>(), It.IsAny<Func<IActionResult, bool>>()))
            .Returns(async (Func<Task<IActionResult>> work, Func<IActionResult, bool> shouldCommit) =>
            {
                var r = await work();
                committed = shouldCommit(r);
                return r;
            });
        users.Setup(u => u.ReleaseUsernameFromWebOnlyAccountAsync("Steve", null)).ReturnsAsync(12);
        users.Setup(u => u.ValidateUserCreationAsync(It.IsAny<UserCreateDto>(), It.IsAny<int?>())).ReturnsAsync((true, null));
        users.Setup(u => u.CheckUsernameTakenAsync("Steve", null)).ReturnsAsync((false, null));
        users.Setup(u => u.CheckUuidTakenAsync(It.IsAny<string>(), null)).ReturnsAsync((true, 99));
        var controller = new UsersController(users.Object, new Mock<IMapper>().Object, new Mock<IPermissionResolutionService>().Object,
            new Mock<ISalaryService>().Object, new Mock<IUserProfileSummaryService>().Object,
            new Mock<IUserPermissionGroupService>().Object, new Mock<IPermissionGrantService>().Object, new Mock<IPermissionEscalationGuard>().Object)
        {
            ControllerContext = new ControllerContext { HttpContext = ServiceAuthTestHelper.Plugin() }
        };

        var result = await controller.Create(new UserCreateDto { Username = "Steve", Uuid = "uuid-1" });

        Assert.IsType<ConflictObjectResult>(result);
        Assert.False(committed);
        users.Verify(u => u.CreateAsync(It.IsAny<UserCreateDto>()), Times.Never);
    }

    [Fact]
    public async Task ReleaseUsername_RenamesTheWebOnlyAccount_AndAudits()
    {
        var repo = new Mock<IUserRepository>();
        var webOnly = new User { Id = 12, Username = "Steve", Email = "squatter@example.com", Uuid = null };
        repo.Setup(r => r.GetByUsernameAsync("Steve")).ReturnsAsync(webOnly);
        var audit = new Mock<IAuditLogService>();
        var service = NewUserService(repo, audit);

        var released = await service.ReleaseUsernameFromWebOnlyAccountAsync("Steve");

        Assert.Equal(12, released);
        Assert.Equal("unclaimed-12", webOnly.Username);
        repo.Verify(r => r.UpdateUserAsync(webOnly), Times.Once);
        audit.Verify(a => a.RecordAsync(null, 12, AuditAction.UsernameReleased, It.Is<string>(d => d.Contains("Steve"))), Times.Once);
    }

    [Fact]
    public async Task ReleaseUsername_LeavesMinecraftAccountsAlone()
    {
        var repo = new Mock<IUserRepository>();
        repo.Setup(r => r.GetByUsernameAsync("Steve")).ReturnsAsync(new User { Id = 3, Username = "Steve", Uuid = "uuid-0" });
        var service = NewUserService(repo, new Mock<IAuditLogService>());

        Assert.Null(await service.ReleaseUsernameFromWebOnlyAccountAsync("Steve"));
        repo.Verify(r => r.UpdateUserAsync(It.IsAny<User>()), Times.Never);
    }

    private static UserService NewUserService(Mock<IUserRepository> repo, Mock<IAuditLogService> audit) =>
        new(repo.Object, new Mock<IMapper>().Object, new Mock<IPasswordService>().Object, new Mock<ILinkCodeService>().Object,
            new Mock<ITitleService>().Object, new Mock<IUserPermissionGroupService>().Object, audit.Object,
            new Mock<IPermissionGroupRepository>().Object, NullLogger<UserService>.Instance, new Mock<ICurrencyService>().Object,
            new Mock<ITitleProgressionService>().Object);

    // ===== validate-link-code (WP5.4) =====

    private UsersController LinkCodeController(HttpContext http)
    {
        var users = new UserService(_h.Users, _h.Mapper.Object, _h.Passwords,
            new LinkCodeService(new LinkCodeRepository(_h.Db), _h.Users, _h.Mapper.Object, Options.Create(_h.Settings)),
            Mock.Of<ITitleService>(t => t.ResolveAsync(It.IsAny<int>(), It.IsAny<Gender?>()) == Task.FromResult(new TitleResolutionDto())),
            new Mock<IUserPermissionGroupService>().Object, new Mock<IAuditLogService>().Object,
            new Mock<IPermissionGroupRepository>().Object, NullLogger<UserService>.Instance, new Mock<ICurrencyService>().Object,
            new Mock<ITitleProgressionService>().Object);
        return new UsersController(users, _h.Mapper.Object, new Mock<IPermissionResolutionService>().Object,
            new Mock<ISalaryService>().Object, new Mock<IUserProfileSummaryService>().Object,
            new Mock<IUserPermissionGroupService>().Object, new Mock<IPermissionGrantService>().Object, new Mock<IPermissionEscalationGuard>().Object)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }

    [Fact]
    public async Task ValidateLinkCode_ReturnsOnlyValidityAndName_AndDoesNotConsume()
    {
        var player = await _h.AddUserAsync();
        await _h.AddLinkCodeAsync(player.Id);

        var result = Assert.IsType<OkObjectResult>(await LinkCodeController(ServiceAuthTestHelper.Anonymous()).ValidateLinkCode("ABCD-2345"));
        var json = JsonSerializer.Serialize(result.Value);

        Assert.Equal("{\"isValid\":true,\"username\":\"Steve\"}", json);
        Assert.Equal(LinkCodeStatus.Active, (await _h.Db.LinkCodes.SingleAsync()).Status);
    }

    [Fact]
    public async Task ValidateLinkCode_GivesThePluginTheUserId()
    {
        var player = await _h.AddUserAsync();
        await _h.AddLinkCodeAsync(player.Id);

        var result = Assert.IsType<OkObjectResult>(await LinkCodeController(ServiceAuthTestHelper.Plugin()).ValidateLinkCode("ABCD2345"));
        var dto = Assert.IsType<ValidateLinkCodeResponseDto>(result.Value);
        Assert.Equal(player.Id, dto.UserId);
    }

    [Fact]
    public async Task ValidateLinkCode_Invalid_HasAnErrorAndNoUser()
    {
        var result = Assert.IsType<OkObjectResult>(await LinkCodeController(ServiceAuthTestHelper.Anonymous()).ValidateLinkCode("NOPE9999"));
        var json = JsonSerializer.Serialize(result.Value);
        Assert.Equal("{\"isValid\":false,\"error\":\"Invalid or expired link code\"}", json);
    }
}
