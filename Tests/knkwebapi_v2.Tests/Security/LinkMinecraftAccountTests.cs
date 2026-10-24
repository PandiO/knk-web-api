using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Tests.Api;

namespace knkwebapi_v2.Tests.Security;

/// <summary>
/// POST api/Users/link-minecraft-account end to end on the auth harness (real repositories on EF
/// InMemory, real LinkCodeService, AuthService and session revocation): the DESIGN § 5.4 rules
/// (KNG-207) as the web app sees them, including the new session after a fold.
/// </summary>
public class LinkMinecraftAccountTests : IDisposable
{
    private readonly AuthTestHarness _h = new(s => s.LinkAttempts.MaxFailures = 2);
    private readonly LinkAttemptLimiter _limiter;

    public LinkMinecraftAccountTests()
    {
        _limiter = new LinkAttemptLimiter(new MemoryCache(new MemoryCacheOptions()), Options.Create(_h.Settings));
    }

    public void Dispose() => _h.Dispose();

    private UsersController Controller(int callerId)
    {
        var users = new UserService(_h.Users, _h.Mapper.Object, _h.Passwords,
            new LinkCodeService(new LinkCodeRepository(_h.Db), _h.Users, _h.Mapper.Object, Options.Create(_h.Settings)),
            Mock.Of<ITitleService>(t => t.ResolveAsync(It.IsAny<int>(), It.IsAny<Gender?>()) == Task.FromResult(new TitleResolutionDto())),
            new Mock<IUserPermissionGroupService>().Object, new Mock<IAuditLogService>().Object,
            new Mock<IPermissionGroupRepository>().Object, NullLogger<UserService>.Instance, new Mock<ICurrencyService>().Object,
            new Mock<ITitleProgressionService>().Object, null, _h.Revocation, _limiter);
        var environment = Mock.Of<IWebHostEnvironment>(e => e.EnvironmentName == "Development");
        return new UsersController(users, _h.Mapper.Object, new Mock<IPermissionResolutionService>().Object,
            new Mock<ISalaryService>().Object, new Mock<IUserProfileSummaryService>().Object,
            new Mock<IUserPermissionGroupService>().Object, new Mock<IPermissionGrantService>().Object, new Mock<IPermissionEscalationGuard>().Object,
            _h.CreateAuthService(), environment, Options.Create(_h.Settings))
        {
            ControllerContext = new ControllerContext { HttpContext = ServiceAuthTestHelper.WebUser(callerId) }
        };
    }

    private static JsonElement Body(IActionResult result) =>
        JsonDocument.Parse(JsonSerializer.Serialize(((ObjectResult)result).Value)).RootElement;

    private static string? Error(IActionResult result) => Body(result).GetProperty("error").GetString();

    [Fact]
    public async Task CaseB_FoldsTheShellIntoTheMinecraftAccount_AndLogsTheCallerIntoIt()
    {
        var shell = await _h.AddUserAsync(username: "web-1", email: "steve@example.com", uuid: null);
        var shellHash = shell.PasswordHash;
        var minecraft = await _h.AddUserAsync(username: "Steve", email: null, password: null, uuid: "uuid-1");
        await _h.AddLinkCodeAsync(minecraft.Id);
        var oldSession = await _h.CreateAuthService().StartSessionAsync(shell.Id);
        Assert.Null(await _h.BearerProblemAsync(oldSession!.AccessToken));

        var controller = Controller(shell.Id);
        var result = await controller.LinkMinecraftAccount(new LinkMinecraftAccountDto { LinkCode = "abcd-2345" });

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Body(ok);
        Assert.Equal(minecraft.Id, body.GetProperty("user").GetProperty("id").GetInt32());
        Assert.True(body.GetProperty("merged").GetBoolean());
        var accessToken = body.GetProperty("accessToken").GetString()!;
        Assert.Null(await _h.BearerProblemAsync(accessToken));
        var principal = await _h.Tokens.ValidateAccessTokenAsync(accessToken);
        Assert.Equal(minecraft.Id.ToString(), principal!.FindFirst("uid")!.Value);
        Assert.Contains("refreshToken=", controller.Response.Headers.SetCookie.ToString());

        var savedMinecraft = await _h.Db.Users.SingleAsync(u => u.Id == minecraft.Id);
        Assert.Equal("steve@example.com", savedMinecraft.Email);
        Assert.Equal(shellHash, savedMinecraft.PasswordHash);
        Assert.Equal("uuid-1", savedMinecraft.Uuid);
        Assert.Equal("Steve", savedMinecraft.Username);
        Assert.True(savedMinecraft.IsActive);
        var savedShell = await _h.Db.Users.SingleAsync(u => u.Id == shell.Id);
        Assert.Null(savedShell.Email);
        Assert.False(savedShell.IsActive);
        Assert.NotNull(savedShell.DeletedAt);
        Assert.Equal(LinkCodeStatus.Used, (await _h.Db.LinkCodes.SingleAsync()).Status);

        // The shell's old session is over.
        Assert.NotNull(await _h.BearerProblemAsync(oldSession.AccessToken));
        Assert.All(await _h.Db.RefreshTokens.Where(t => t.UserId == shell.Id).ToListAsync(), t => Assert.NotNull(t.RevokedAt));
        Assert.Contains(await _h.Db.RefreshTokens.Where(t => t.UserId == minecraft.Id).ToListAsync(), t => t.RevokedAt == null);
    }

    [Fact]
    public async Task CaseB_Is409AlreadyRegistered_WhenTheMinecraftAccountHasAWebLogin()
    {
        var shell = await _h.AddUserAsync(username: "web-1", email: "second@example.com", uuid: null);
        var minecraft = await _h.AddUserAsync(username: "Steve", email: "steve@example.com", uuid: "uuid-1");
        await _h.AddLinkCodeAsync(minecraft.Id);

        var result = await Controller(shell.Id).LinkMinecraftAccount(new LinkMinecraftAccountDto { LinkCode = "ABCD2345" });

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal("AlreadyRegistered", Error(result));
        Assert.Equal(LinkCodeStatus.Active, (await _h.Db.LinkCodes.SingleAsync()).Status);
        Assert.Equal("second@example.com", (await _h.Db.Users.SingleAsync(u => u.Id == shell.Id)).Email);
        Assert.True((await _h.Db.Users.SingleAsync(u => u.Id == shell.Id)).IsActive);
    }

    [Fact]
    public async Task Is409AlreadyLinked_WhenTheCallerHasAnotherMinecraftAccount()
    {
        var caller = await _h.AddUserAsync(username: "Alex", email: "alex@example.com", uuid: "uuid-9");
        var minecraft = await _h.AddUserAsync(username: "Steve", email: null, password: null, uuid: "uuid-1");
        await _h.AddLinkCodeAsync(minecraft.Id);

        var result = await Controller(caller.Id).LinkMinecraftAccount(new LinkMinecraftAccountDto { LinkCode = "ABCD2345" });

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal("AlreadyLinked", Error(result));
        Assert.Equal("uuid-9", (await _h.Db.Users.SingleAsync(u => u.Id == caller.Id)).Uuid);
        Assert.Equal(LinkCodeStatus.Active, (await _h.Db.LinkCodes.SingleAsync()).Status);
    }

    [Fact]
    public async Task AWrongCode_Is400_AndTooManyOfThem_Is429TooManyAttempts()
    {
        var shell = await _h.AddUserAsync(username: "web-1", email: "steve@example.com", uuid: null);
        var minecraft = await _h.AddUserAsync(username: "Steve", email: null, password: null, uuid: "uuid-1");
        await _h.AddLinkCodeAsync(minecraft.Id);

        var first = await Controller(shell.Id).LinkMinecraftAccount(new LinkMinecraftAccountDto { LinkCode = "WRONG001" });
        Assert.IsType<BadRequestObjectResult>(first);
        var second = await Controller(shell.Id).LinkMinecraftAccount(new LinkMinecraftAccountDto { LinkCode = "WRONG002" });
        Assert.IsType<BadRequestObjectResult>(second);

        var controller = Controller(shell.Id);
        var blocked = await controller.LinkMinecraftAccount(new LinkMinecraftAccountDto { LinkCode = "ABCD2345" }); // even the right one
        var status = Assert.IsType<ObjectResult>(blocked);
        Assert.Equal(StatusCodes.Status429TooManyRequests, status.StatusCode);
        Assert.Equal("TooManyAttempts", Error(blocked));
        Assert.False(string.IsNullOrEmpty(controller.Response.Headers.RetryAfter));
        Assert.Equal(LinkCodeStatus.Active, (await _h.Db.LinkCodes.SingleAsync()).Status);
    }

    [Fact]
    public async Task CaseA_AClaimCode_LinksTheWebAccountItself_WithoutANewSession()
    {
        var shell = await _h.AddUserAsync(username: "web-1", email: "steve@example.com", uuid: null);
        shell.AdmittedAt = DateTime.UtcNow; // invited (KNG-214: claim codes only link admitted accounts)
        _h.Db.LinkCodes.Add(new LinkCode
        {
            Code = "7Q2HM4XA", Purpose = LinkCodePurpose.Claim, MinecraftUuid = "uuid-7", MinecraftName = "Steve",
            CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddMinutes(15), Status = LinkCodeStatus.Active
        });
        await _h.Db.SaveChangesAsync();

        var result = await Controller(shell.Id).LinkMinecraftAccount(new LinkMinecraftAccountDto { LinkCode = "7Q2H-M4XA" });

        var body = Body(Assert.IsType<OkObjectResult>(result));
        Assert.Equal(shell.Id, body.GetProperty("user").GetProperty("id").GetInt32());
        Assert.False(body.TryGetProperty("accessToken", out _));
        var saved = await _h.Db.Users.SingleAsync(u => u.Id == shell.Id);
        Assert.Equal("uuid-7", saved.Uuid);
        Assert.Equal("Steve", saved.Username);
        Assert.Equal("steve@example.com", saved.Email);
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task ACodeForTheCallersOwnAccount_Is200WithoutANewSession()
    {
        var me = await _h.AddUserAsync(username: "Steve", email: "steve@example.com", uuid: "uuid-1");
        await _h.AddLinkCodeAsync(me.Id);

        var result = await Controller(me.Id).LinkMinecraftAccount(new LinkMinecraftAccountDto { LinkCode = "ABCD2345" });

        var body = Body(Assert.IsType<OkObjectResult>(result));
        Assert.Equal(me.Id, body.GetProperty("user").GetProperty("id").GetInt32());
        Assert.False(body.TryGetProperty("accessToken", out _));
        Assert.Equal(LinkCodeStatus.Used, (await _h.Db.LinkCodes.SingleAsync()).Status);
    }

    [Fact]
    public async Task AResetTokenHash_IsNotALinkCode()
    {
        var me = await _h.AddUserAsync(username: "web-1", email: "steve@example.com", uuid: null);
        var hash = new string('a', 64);
        _h.Db.LinkCodes.Add(new LinkCode { Code = hash, UserId = me.Id, Purpose = LinkCodePurpose.PasswordReset, CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddMinutes(30) });
        await _h.Db.SaveChangesAsync();

        var result = await Controller(me.Id).LinkMinecraftAccount(new LinkMinecraftAccountDto { LinkCode = hash });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(LinkCodeStatus.Active, (await _h.Db.LinkCodes.SingleAsync()).Status);
    }
}
