using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Tests.Security;

/// <summary>
/// AuthService wired to real repositories on an EF InMemory database, the real TokenService and
/// PasswordService (bcrypt cost 4), and the real session cache and revocation service, so the
/// closed-alpha session tests exercise what is actually stored.
/// </summary>
internal sealed class AuthTestHarness : IDisposable
{
    public const string JwtSecret = "test-only-jwt-secret-0123456789abcdef-0123456789";
    public const string Password = "Correct-Horse-9";

    private readonly ServiceProvider _provider;

    public SecuritySettings Settings { get; } = new();
    public IConfiguration Configuration { get; }
    public KnKDbContext Db { get; }
    public TokenService Tokens { get; }
    public PasswordService Passwords { get; }
    public UserRepository Users { get; }
    public RefreshTokenRepository RefreshTokens { get; }
    public UserSessionStateCache SessionState { get; }
    public SessionRevocationService Revocation { get; }
    public Mock<IPasswordResetDeliveryService> Delivery { get; } = new();
    public List<AccountMail> Mail { get; } = new();
    public Mock<IAccountMailQueue> MailQueue { get; } = new();
    public LoginAttemptLimiter Limiter { get; }
    public Mock<IMapper> Mapper { get; } = new();

    public AuthTestHarness(Action<SecuritySettings>? configure = null)
    {
        Settings.BcryptRounds = 4;
        configure?.Invoke(Settings);
        MailQueue.Setup(q => q.Enqueue(It.IsAny<AccountMail>())).Callback((AccountMail m) => Mail.Add(m)).Returns(true);
        Limiter = new LoginAttemptLimiter(new MemoryCache(new MemoryCacheOptions()), Options.Create(Settings));
        Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:Jwt:Secret"] = JwtSecret,
            ["Security:Jwt:Issuer"] = "knk-api",
            ["Security:Jwt:Audience"] = "knk-app",
            ["Security:BcryptRounds"] = "4",
        }).Build();

        var dbName = "auth-" + Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddDbContext<KnKDbContext>(o => o.UseInMemoryDatabase(dbName));
        _provider = services.BuildServiceProvider();
        Db = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(dbName).Options);

        Tokens = new TokenService(Configuration);
        Passwords = new PasswordService(Configuration);
        Users = new UserRepository(Db);
        RefreshTokens = new RefreshTokenRepository(Db);
        SessionState = new UserSessionStateCache(new MemoryCache(new MemoryCacheOptions()), _provider.GetRequiredService<IServiceScopeFactory>());
        Revocation = new SessionRevocationService(Users, RefreshTokens, SessionState, NullLogger<SessionRevocationService>.Instance);
        Mapper.Setup(m => m.Map<UserDto>(It.IsAny<User>()))
            .Returns((User u) => new UserDto { Id = u.Id, Username = u.Username, Email = u.Email, IsActive = u.IsActive });
    }

    public AuthService CreateAuthService() => new(
        Users,
        Tokens,
        Passwords,
        Mapper.Object,
        new LinkCodeRepository(Db),
        MailQueue.Object,
        new MemoryCache(new MemoryCacheOptions()),
        Options.Create(Settings),
        NullLogger<AuthService>.Instance,
        RefreshTokens,
        Revocation,
        new LinkCodeService(new LinkCodeRepository(Db), Users, Mapper.Object, Options.Create(Settings)),
        Limiter);

    /// <summary>An active /account link code for <paramref name="userId"/> (8 chars, 20 min).</summary>
    public async Task<string> AddLinkCodeAsync(int? userId, string code = "ABCD2345", DateTime? expiresAt = null)
    {
        Db.LinkCodes.Add(new LinkCode
        {
            UserId = userId,
            Code = code,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddMinutes(20),
            Status = LinkCodeStatus.Active
        });
        await Db.SaveChangesAsync();
        return code;
    }

    public async Task<User> AddUserAsync(string username = "Steve", string? email = "steve@example.com", string? password = Password,
        string? uuid = "00000000-0000-0000-0000-000000000001", bool isActive = true)
    {
        var user = new User
        {
            Username = username,
            Email = email,
            Uuid = uuid,
            PasswordHash = password == null ? null : await Passwords.HashPasswordAsync(password),
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow
        };
        Db.Users.Add(user);
        await Db.SaveChangesAsync();
        return user;
    }

    /// <summary>The per-request check JwtBearer runs (Program.cs OnTokenValidated): null = accepted.</summary>
    public async Task<string?> BearerProblemAsync(string accessToken)
    {
        var principal = await Tokens.ValidateAccessTokenAsync(accessToken);
        if (principal == null) return "invalid JWT";
        return await AccessTokenSessionCheck.FindProblemAsync(principal, SessionState);
    }

    public void Dispose()
    {
        Db.Dispose();
        _provider.Dispose();
    }
}
