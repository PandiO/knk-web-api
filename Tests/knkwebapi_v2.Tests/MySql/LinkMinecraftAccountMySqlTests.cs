using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// KNG-207 case B on a real MySQL: users.Email and users.Uuid are unique indexes, which EF InMemory
/// doesn't enforce. The fold must clear the shell's email before the Minecraft account gets it, in
/// one transaction, and must never copy the UUID onto a second row.
/// </summary>
[Trait("Category", "requires-mysql")]
public class LinkMinecraftAccountMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;

    public LinkMinecraftAccountMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private static UserService Service(KnKDbContext ctx)
    {
        var mapper = new Mock<IMapper>();
        mapper.Setup(m => m.Map<UserDto>(It.IsAny<User>())).Returns((User u) => new UserDto { Id = u.Id, Username = u.Username, Email = u.Email, Uuid = u.Uuid });
        var users = new UserRepository(ctx);
        var currency = new CurrencyService(new CurrencyRepository(ctx), users, NullLogger<CurrencyService>.Instance);
        var titles = Mock.Of<ITitleService>(t => t.ResolveAsync(It.IsAny<int>(), It.IsAny<Gender?>()) == Task.FromResult(new TitleResolutionDto()));
        var memberships = new Mock<IUserPermissionGroupService>().Object;
        var audit = new Mock<IAuditLogService>().Object;
        var progression = new TitleProgressionService(currency, users, titles, memberships, audit);
        return new UserService(users, mapper.Object, new Mock<IPasswordService>().Object,
            new LinkCodeService(new LinkCodeRepository(ctx), users, mapper.Object, Options.Create(new SecuritySettings())),
            titles, memberships, audit, new Mock<IPermissionGroupRepository>().Object, NullLogger<UserService>.Instance,
            currency, progression);
    }

    private async Task<(int ShellId, int MinecraftId, string Code)> SeedAsync(string? minecraftPassword = null)
    {
        await using var ctx = _db.NewContext();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var shell = new User { Username = "web-" + tag, Email = $"shell-{tag}@example.com", PasswordHash = "hash-" + tag, EmailVerified = true, CreatedAt = DateTime.UtcNow };
        var minecraft = new User { Username = "Steve" + tag, Uuid = "uuid-" + tag, PasswordHash = minecraftPassword, CreatedAt = DateTime.UtcNow };
        ctx.Users.AddRange(shell, minecraft);
        await ctx.SaveChangesAsync();
        var code = ("C" + tag).ToUpperInvariant()[..8];
        ctx.LinkCodes.Add(new LinkCode { Code = code, UserId = minecraft.Id, Purpose = LinkCodePurpose.Link, CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddMinutes(20) });
        await ctx.SaveChangesAsync();
        return (shell.Id, minecraft.Id, code);
    }

    [MySqlFact]
    public async Task CaseB_MovesTheEmailAcrossTheUniqueIndex_InOneGo()
    {
        var (shellId, minecraftId, code) = await SeedAsync();

        LinkMinecraftAccountResult result;
        await using (var ctx = _db.NewContext())
        {
            result = await Service(ctx).LinkMinecraftAccountAsync(shellId, code);
        }

        Assert.True(result.Merged);
        Assert.Equal(minecraftId, result.User.Id);
        await using var check = _db.NewContext();
        var shell = await check.Users.SingleAsync(u => u.Id == shellId);
        var minecraft = await check.Users.SingleAsync(u => u.Id == minecraftId);
        Assert.Null(shell.Email);
        Assert.Null(shell.Uuid);
        Assert.False(shell.IsActive);
        Assert.NotNull(shell.DeletedAt);
        Assert.StartsWith("shell-", minecraft.Email);
        Assert.StartsWith("hash-", minecraft.PasswordHash);
        Assert.True(minecraft.EmailVerified);
        Assert.StartsWith("uuid-", minecraft.Uuid);
        Assert.Equal(1, await check.Users.CountAsync(u => u.Uuid == minecraft.Uuid));
        Assert.Equal(LinkCodeStatus.Used, (await check.LinkCodes.SingleAsync(c => c.Code == code)).Status);
    }

    [MySqlFact]
    public async Task CaseB_Refused_LeavesBothRowsUntouched()
    {
        var (shellId, minecraftId, code) = await SeedAsync(minecraftPassword: "already-registered");

        await using (var ctx = _db.NewContext())
        {
            var ex = await Assert.ThrowsAsync<LinkConflictException>(() => Service(ctx).LinkMinecraftAccountAsync(shellId, code));
            Assert.Equal(LinkConflictException.AlreadyRegistered, ex.Code);
        }

        await using var check = _db.NewContext();
        Assert.StartsWith("shell-", (await check.Users.SingleAsync(u => u.Id == shellId)).Email);
        Assert.Null((await check.Users.SingleAsync(u => u.Id == minecraftId)).Email);
        Assert.Equal(LinkCodeStatus.Active, (await check.LinkCodes.SingleAsync(c => c.Code == code)).Status);
    }
}
