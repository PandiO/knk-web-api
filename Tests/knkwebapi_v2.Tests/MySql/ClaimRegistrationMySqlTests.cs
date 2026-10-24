using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Email;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Invitations;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// KNG-214 game-first sign-up on a real MySQL: the shell account (InvitationService.RedeemAsync) and the
/// claim link run in one transaction, so a link that fails after the shell was inserted leaves no account,
/// no redemption and the invite's use untaken. EF InMemory has no transactions, so only MySQL shows this.
/// </summary>
[Trait("Category", "requires-mysql")]
public class ClaimRegistrationMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;

    public ClaimRegistrationMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    [MySqlFact]
    public async Task AFailedLink_RollsBackTheShell_TheRedemption_AndTheUse()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var code = InvitationCodes.RandomCrockford(8);
        var shared = InvitationCodes.NewSharedCode();
        var normalized = InvitationCodes.NormalizeShared(shared)!;
        int invitationId;
        await using (var seed = _db.NewContext())
        {
            var invitation = new Invitation
            {
                Kind = InvitationKind.Shared, CodeHash = InvitationCodes.Hash(normalized), CodeHint = InvitationCodes.Hint(normalized), Label = "test",
                MaxUses = 5, CreatedAt = DateTime.UtcNow, CreatedByUserId = 1, ExpiresAt = DateTime.UtcNow.AddDays(7)
            };
            seed.Invitations.Add(invitation);
            seed.LinkCodes.Add(new LinkCode { Code = code, Purpose = LinkCodePurpose.Claim, MinecraftUuid = Guid.NewGuid().ToString("D"),
                MinecraftName = "Steve", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddMinutes(15) });
            await seed.SaveChangesAsync();
            invitationId = invitation.Id;
        }

        var email = $"claim-{tag}@example.com";
        await using (var ctx = _db.NewContext())
        {
            var security = new SecuritySettings { BcryptRounds = 4 };
            security.Registration.Mode = RegistrationMode.Invite;
            var users = new UserRepository(ctx);
            var mapper = new Mock<IMapper>().Object;
            var composer = new Mock<IEmailComposer>().Object;
            var invitations = new InvitationService(new InvitationRepository(ctx), users, new PermissionGroupRepository(ctx),
                new Mock<IPermissionEscalationGuard>().Object, new Mock<IAuditLogService>().Object, composer,
                Options.Create(new InvitationSettings()), Options.Create(security));
            // The link fails after the shell exists (as a concurrent link of the same UUID would).
            var failingLink = new Mock<IUserService>();
            failingLink.Setup(u => u.LinkMinecraftAccountAsync(It.IsAny<int>(), It.IsAny<string>()))
                .ThrowsAsync(new LinkConflictException(LinkConflictException.AlreadyRegistered, "taken meanwhile"));
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Security:BcryptRounds"] = "4" }).Build();
            var auth = new AuthService(users, new Mock<ITokenService>().Object, new PasswordService(config), mapper,
                new LinkCodeRepository(ctx), composer, new MemoryCache(new MemoryCacheOptions()), Options.Create(security),
                NullLogger<AuthService>.Instance, new Mock<IRefreshTokenRepository>().Object, new Mock<ISessionRevocationService>().Object,
                new LinkCodeService(new LinkCodeRepository(ctx), users, mapper, Options.Create(security)),
                new Mock<ILoginAttemptLimiter>().Object, null, invitations, failingLink.Object);

            var outcome = await auth.RegisterAsync(new AuthRegisterRequestDto
            {
                Email = email, Password = "Correct-Horse-9", PasswordConfirmation = "Correct-Horse-9",
                InviteCode = shared, ClaimCode = code
            });

            Assert.Equal("AlreadyRegistered", outcome.Error);
        }

        await using var check = _db.NewContext();
        Assert.False(await check.Users.AnyAsync(u => u.Email == email));
        Assert.Equal(0, (await check.Invitations.SingleAsync(i => i.Id == invitationId)).UseCount);
        Assert.False(await check.InvitationRedemptions.AnyAsync(r => r.InvitationId == invitationId));
        Assert.Equal(LinkCodeStatus.Active, (await check.LinkCodes.SingleAsync(c => c.Code == code)).Status);
    }
}
