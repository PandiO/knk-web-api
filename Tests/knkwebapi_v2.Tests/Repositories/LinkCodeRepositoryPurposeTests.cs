using Microsoft.EntityFrameworkCore;
using Xunit;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;

namespace knkwebapi_v2.Tests.Repositories;

/// <summary>
/// KNG-208: reset tokens and link codes are told apart by Purpose, not by the code's length, and the
/// repository knows the claim code of a UUID (KNG-214).
/// </summary>
public class LinkCodeRepositoryPurposeTests : IDisposable
{
    private readonly KnKDbContext _db = new(new DbContextOptionsBuilder<KnKDbContext>()
        .UseInMemoryDatabase("linkcode-purpose-" + Guid.NewGuid()).Options);
    private readonly LinkCodeRepository _repo;

    public LinkCodeRepositoryPurposeTests()
    {
        _repo = new LinkCodeRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task<LinkCode> AddAsync(LinkCode code)
    {
        code.ExpiresAt = code.ExpiresAt == default ? DateTime.UtcNow.AddMinutes(20) : code.ExpiresAt;
        _db.LinkCodes.Add(code);
        await _db.SaveChangesAsync();
        return code;
    }

    private static string Hash(char c) => new(c, 64);

    [Fact]
    public async Task ResetTokenLookup_MatchesOnPurpose_NotOnLength()
    {
        var user = new User { Username = "Steve" };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        await AddAsync(new LinkCode { Code = Hash('a'), UserId = user.Id, Purpose = LinkCodePurpose.PasswordReset });
        await AddAsync(new LinkCode { Code = Hash('b'), UserId = user.Id, Purpose = LinkCodePurpose.Link }); // 64 chars but not a reset token

        Assert.NotNull(await _repo.GetActivePasswordResetTokenAsync(Hash('a')));
        Assert.Null(await _repo.GetActivePasswordResetTokenAsync(Hash('b')));
    }

    [Fact]
    public async Task InvalidatingResetTokens_LeavesTheUsersLinkCodesAlone()
    {
        var user = new User { Username = "Steve" };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        var reset = await AddAsync(new LinkCode { Code = Hash('a'), UserId = user.Id, Purpose = LinkCodePurpose.PasswordReset });
        var link = await AddAsync(new LinkCode { Code = "ABCD2345", UserId = user.Id, Purpose = LinkCodePurpose.Link });

        await _repo.InvalidateActivePasswordResetTokensAsync(user.Id);

        Assert.Equal(LinkCodeStatus.Expired, reset.Status);
        Assert.Equal(LinkCodeStatus.Active, link.Status);
    }

    [Fact]
    public async Task ActiveClaimCodeByUuid_IgnoresUsedExpiredAndOtherPurposes()
    {
        await AddAsync(new LinkCode { Code = "USED0001", Purpose = LinkCodePurpose.Claim, MinecraftUuid = "uuid-1", Status = LinkCodeStatus.Used });
        await AddAsync(new LinkCode { Code = "LAPSED01", Purpose = LinkCodePurpose.Claim, MinecraftUuid = "uuid-1", ExpiresAt = DateTime.UtcNow.AddMinutes(-1) });
        await AddAsync(new LinkCode { Code = "OTHERUU1", Purpose = LinkCodePurpose.Claim, MinecraftUuid = "uuid-2" });
        await AddAsync(new LinkCode { Code = "LINKCODE", Purpose = LinkCodePurpose.Link, MinecraftUuid = "uuid-1" });

        Assert.Null(await _repo.GetActiveClaimCodeByUuidAsync("uuid-1"));

        var live = await AddAsync(new LinkCode { Code = "7Q2HM4XA", Purpose = LinkCodePurpose.Claim, MinecraftUuid = "uuid-1" });
        Assert.Equal(live.Id, (await _repo.GetActiveClaimCodeByUuidAsync("uuid-1"))!.Id);
    }

    [Fact]
    public async Task DeleteFinishedBefore_NeverDeletesAnActiveRow()
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);
        await AddAsync(new LinkCode { Code = "ACTIVEOLD", Status = LinkCodeStatus.Active, ExpiresAt = cutoff.AddDays(-5) });
        await AddAsync(new LinkCode { Code = "USEDOLD1", Status = LinkCodeStatus.Used, ExpiresAt = cutoff.AddDays(-5), UsedAt = cutoff.AddDays(-5) });
        await AddAsync(new LinkCode { Code = "USEDLATE", Status = LinkCodeStatus.Used, ExpiresAt = cutoff.AddDays(-5), UsedAt = cutoff.AddDays(1) }); // used after the cutoff: kept

        Assert.Equal(1, await _repo.DeleteFinishedBeforeAsync(cutoff));
        Assert.Equal(new[] { "ACTIVEOLD", "USEDLATE" }, await _db.LinkCodes.OrderBy(c => c.Code).Select(c => c.Code).ToListAsync());
    }
}
