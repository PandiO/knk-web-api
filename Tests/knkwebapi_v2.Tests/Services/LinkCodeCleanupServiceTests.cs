using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// KNG-208: the hourly LinkCodeCleanupService marks active codes past their ExpiresAt as Expired
/// and deletes codes used or expired more than 7 days ago. Active codes are never deleted.
/// </summary>
public class LinkCodeCleanupServiceTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly KnKDbContext _db;

    public LinkCodeCleanupServiceTests()
    {
        var dbName = "linkcode-cleanup-" + Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddDbContext<KnKDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<ILinkCodeRepository, LinkCodeRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ILinkCodeService, LinkCodeService>();
        services.AddSingleton(new Mock<IMapper>().Object);
        services.AddSingleton(Options.Create(new SecuritySettings()));
        _provider = services.BuildServiceProvider();
        _db = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(dbName).Options);
    }

    public void Dispose()
    {
        _db.Dispose();
        _provider.Dispose();
    }

    private async Task<LinkCode> AddAsync(string code, LinkCodeStatus status, DateTime expiresAt, DateTime? usedAt = null, LinkCodePurpose purpose = LinkCodePurpose.Link)
    {
        var row = new LinkCode { Code = code, Status = status, ExpiresAt = expiresAt, UsedAt = usedAt, Purpose = purpose, CreatedAt = expiresAt.AddMinutes(-20) };
        _db.LinkCodes.Add(row);
        await _db.SaveChangesAsync();
        return row;
    }

    private LinkCodeCleanupService Service() => new(_provider, NullLogger<LinkCodeCleanupService>.Instance);

    [Fact]
    public async Task OnePass_MarksExpiredCodes_AndDeletesOnlyTheOldFinishedOnes()
    {
        var now = DateTime.UtcNow;
        await AddAsync("ACTIVE01", LinkCodeStatus.Active, now.AddMinutes(10));                                   // live link code: untouched
        await AddAsync("JUSTEXP1", LinkCodeStatus.Active, now.AddHours(-1));                                     // lapsed yesterday-ish: marked Expired, kept
        await AddAsync("OLDEXPD1", LinkCodeStatus.Active, now.AddDays(-8));                                      // lapsed long ago, still Active: marked, then deleted
        await AddAsync("USEDOLD1", LinkCodeStatus.Used, now.AddDays(-9), usedAt: now.AddDays(-8));               // used 8 days ago: deleted
        await AddAsync("USEDNEW1", LinkCodeStatus.Used, now.AddDays(-2), usedAt: now.AddDays(-1));               // used yesterday: kept
        await AddAsync("EXPIRED1", LinkCodeStatus.Expired, now.AddDays(-3));                                     // expired 3 days ago: kept
        var resetHash = new string('a', 64);
        await AddAsync(resetHash, LinkCodeStatus.Used, now.AddDays(-30), usedAt: now.AddDays(-30), purpose: LinkCodePurpose.PasswordReset); // reset tokens too

        var (expired, deleted) = await Service().RunOnceAsync();

        Assert.Equal(2, expired);
        Assert.Equal(3, deleted);
        _db.ChangeTracker.Clear();
        var left = await _db.LinkCodes.OrderBy(c => c.Code).Select(c => new { c.Code, c.Status }).ToListAsync();
        Assert.Equal(new[] { "ACTIVE01", "EXPIRED1", "JUSTEXP1", "USEDNEW1" }, left.Select(l => l.Code));
        Assert.Equal(LinkCodeStatus.Active, left.Single(l => l.Code == "ACTIVE01").Status);
        Assert.Equal(LinkCodeStatus.Expired, left.Single(l => l.Code == "JUSTEXP1").Status);
    }

    [Fact]
    public async Task AnEmptyTableIsFine()
    {
        Assert.Equal((0, 0), await Service().RunOnceAsync());
    }

    [Fact]
    public void RunsHourly_AndKeepsFinishedCodesForSevenDays()
    {
        Assert.Equal(TimeSpan.FromHours(1), LinkCodeCleanupService.DefaultInterval);
        Assert.Equal(TimeSpan.FromDays(7), LinkCodeCleanupService.DefaultRetention);
    }
}
