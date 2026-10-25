using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Email;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Invitations;
using knkwebapi_v2.Tests.Services.Email;

namespace knkwebapi_v2.Tests.Services.Invitations;

/// <summary>
/// InvitationService on EF InMemory with the real repositories, the real mail composer and throttle
/// (strict templates) and a <see cref="ManualClock"/>. The escalation guard allows everything unless a
/// test says otherwise; audit entries are recorded on a mock.
/// </summary>
internal sealed class InvitationTestDb : IDisposable
{
    public static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    public KnKDbContext Db { get; }
    public ManualClock Clock { get; } = new(Now);
    public InvitationSettings Settings { get; } = new();
    public SecuritySettings Security { get; } = new() { PasswordResetFrontendBaseUrl = "https://knk.example" };
    public Mock<IPermissionEscalationGuard> Guard { get; } = new();
    public Mock<IAuditLogService> Audit { get; } = new();
    public User Admin { get; }
    public PermissionGroup DefaultGroup { get; }
    public PermissionGroup AlphaTester { get; }
    public PermissionGroup Moderator { get; }

    public InvitationTestDb(KnKDbContext? db = null)
    {
        Db = db ?? new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase("invites-" + Guid.NewGuid()).Options);
        Guard.Setup(g => g.CanAssignGroupAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(EscalationCheck.Ok);

        Admin = new User { Username = "Pandi", Uuid = "uuid-admin", Email = "admin@example.com", CreatedAt = Now, AdmittedAt = Now };
        DefaultGroup = new PermissionGroup { Name = "Default", Weight = 0 };
        AlphaTester = new PermissionGroup { Name = "Alpha Tester", Weight = 1 };
        Moderator = new PermissionGroup { Name = "Moderator", Weight = 50 };
        Db.Users.Add(Admin);
        Db.PermissionGroups.AddRange(DefaultGroup, AlphaTester, Moderator);
        Db.SaveChanges();
    }

    /// <summary>The real composer with strict template rendering (every placeholder must be in the model).</summary>
    public EmailComposer Composer()
    {
        var emailSettings = new EmailSettings { FromAddress = "no-reply@knightsandkings.net" };
        return new EmailComposer(Db, new EmailTemplateRenderer(emailSettings, "https://knk.example", strict: true),
            new EmailThrottle(Db, Options.Create(emailSettings), new MemoryCache(new MemoryCacheOptions())), null, Clock);
    }

    public InvitationService Service()
    {
        var composer = Composer();
        return new InvitationService(new InvitationRepository(Db), new UserRepository(Db), new PermissionGroupRepository(Db),
            Guard.Object, Audit.Object, composer, Options.Create(Settings), Options.Create(Security), null, Clock);
    }

    /// <summary>The raw token at the end of a /invite/ link.</summary>
    public static string TokenOf(string link) => link[(link.LastIndexOf('/') + 1)..];

    public void Dispose() => Db.Dispose();
}
