using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Tests.Services.Invitations;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// AdmissionService (KNG-214, plan § 6.1) on EF InMemory with the real repositories and
/// LinkCodeService: every branch of the gate, claim code reuse per UUID and its lifetime, the
/// anonymous claim check, and the FinishLinking reminder sweep.
/// </summary>
public class AdmissionServiceTests : IDisposable
{
    private const string Uuid = "0F6B2C8E-1D3A-4B5C-9E7F-001122334455";
    private const string Lower = "0f6b2c8e-1d3a-4b5c-9e7f-001122334455";

    private readonly InvitationTestDb _t = new();
    private readonly SecuritySettings _security = new() { PasswordResetFrontendBaseUrl = "https://knk.example" };
    private readonly AdmissionSettings _settings = new();

    public AdmissionServiceTests()
    {
        _security.Registration.Mode = RegistrationMode.Invite;
    }

    public void Dispose() => _t.Dispose();

    private AdmissionService Service() => new(new UserRepository(_t.Db),
        new LinkCodeService(new LinkCodeRepository(_t.Db), new UserRepository(_t.Db), new Mock<IMapper>().Object, Options.Create(_security)),
        Options.Create(_settings), Options.Create(_security), _t.Composer(), null, _t.Clock);

    private User AddUser(string name, string? uuid, bool admitted = true, bool active = true, bool deleted = false)
    {
        var user = new User
        {
            Username = name, Uuid = uuid, CreatedAt = InvitationTestDb.Now, IsActive = active,
            AdmittedAt = admitted ? InvitationTestDb.Now : null, DeletedAt = deleted ? InvitationTestDb.Now : null
        };
        _t.Db.Users.Add(user);
        _t.Db.SaveChanges();
        return user;
    }

    [Theory]
    [InlineData(RegistrationMode.LinkCode)]
    [InlineData(RegistrationMode.Open)]
    public async Task OutsideInviteMode_EveryoneIsAdmitted_AndNoCodeIsMade(RegistrationMode mode)
    {
        _security.Registration.Mode = mode;

        var result = await Service().CheckAsync(Uuid, "Stranger");

        Assert.True(result.Admitted);
        Assert.Null(result.Code);
        Assert.Empty(_t.Db.LinkCodes);
    }

    [Fact]
    public async Task AnAdmittedAccount_IsAdmitted_AndFollowsTheMojangName()
    {
        var user = AddUser("OldName", Lower);

        var result = await Service().CheckAsync(Uuid, "NewName");

        Assert.True(result.Admitted);
        Assert.Equal("NewName", (await _t.Db.Users.SingleAsync(u => u.Id == user.Id)).Username);
        Assert.Empty(_t.Db.LinkCodes);
    }

    [Fact]
    public async Task ANameAnotherAccountHolds_IsLeftAlone()
    {
        var user = AddUser("OldName", Lower);
        AddUser("NewName", "11111111-1111-1111-1111-111111111111");

        var result = await Service().CheckAsync(Uuid, "NewName");

        Assert.True(result.Admitted);
        Assert.Equal("OldName", (await _t.Db.Users.SingleAsync(u => u.Id == user.Id)).Username);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task ADeactivatedOrDeletedAccount_IsDeactivated_WithoutACode(bool active, bool deleted)
    {
        AddUser("Steve", Lower, admitted: true, active: active, deleted: deleted);

        var result = await Service().CheckAsync(Uuid, "Steve");

        Assert.False(result.Admitted);
        Assert.Equal(AdmissionReasons.Deactivated, result.Reason);
        Assert.Null(result.Code);
        Assert.Empty(_t.Db.LinkCodes);
    }

    [Fact]
    public async Task AStranger_GetsAClaimCode_BoundToTheUuidAndName_ForFifteenMinutes()
    {
        var result = await Service().CheckAsync(Uuid, "Steve");

        Assert.False(result.Admitted);
        Assert.Equal(AdmissionReasons.NotAdmitted, result.Reason);
        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$", result.Code);
        var code = await _t.Db.LinkCodes.SingleAsync();
        Assert.Equal(LinkCodePurpose.Claim, code.Purpose);
        Assert.Null(code.UserId);
        Assert.Equal(Lower, code.MinecraftUuid);
        Assert.Equal("Steve", code.MinecraftName);
        Assert.Equal(result.Code!.Replace("-", ""), code.Code);
        Assert.InRange((code.ExpiresAt - code.CreatedAt).TotalMinutes, 14.9, 15.1);
        Assert.Equal(code.ExpiresAt, result.ExpiresAt);
        // No user row for strangers.
        Assert.Equal(1, await _t.Db.Users.CountAsync());
    }

    [Fact]
    public async Task ANotAdmittedAccount_GetsAClaimCodeToo()
    {
        AddUser("Steve", Lower, admitted: false);

        var result = await Service().CheckAsync(Uuid, "Steve");

        Assert.Equal(AdmissionReasons.NotAdmitted, result.Reason);
        Assert.NotNull(result.Code);
    }

    [Fact]
    public async Task ARejoin_ShowsTheSameCode_AndASpentOrExpiredOneIsReplaced()
    {
        _settings.ClaimCodeMinutes = 10;
        var first = await Service().CheckAsync(Uuid, "Steve");
        var second = await Service().CheckAsync(Uuid, "Steve");
        Assert.Equal(first.Code, second.Code);
        Assert.Single(_t.Db.LinkCodes);

        var stored = await _t.Db.LinkCodes.SingleAsync();
        Assert.InRange((stored.ExpiresAt - stored.CreatedAt).TotalMinutes, 9.9, 10.1);
        stored.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        await _t.Db.SaveChangesAsync();

        var third = await Service().CheckAsync(Uuid, "Steve");
        Assert.NotEqual(first.Code, third.Code);
        Assert.Equal(2, await _t.Db.LinkCodes.CountAsync());
    }

    [Theory]
    [InlineData("not-a-uuid", "Steve")]
    [InlineData(Uuid, "")]
    [InlineData(Uuid, "web-1234")]
    [InlineData(Uuid, "ThisNameIsTooLong17")]
    public async Task BadInput_IsRefused(string uuid, string name)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service().CheckAsync(uuid, name));
        Assert.Empty(_t.Db.LinkCodes);
    }

    [Fact]
    public async Task ClaimCheck_NamesThePlayer_ForAnActiveClaimCodeTypedLoosely()
    {
        var made = await Service().CheckAsync(Uuid, "Steve");

        var check = await Service().CheckClaimAsync(" " + made.Code!.ToLowerInvariant() + " ");

        Assert.True(check.Valid);
        Assert.Equal("Steve", check.MinecraftName);
    }

    [Fact]
    public async Task AReusedClaimCode_AndTheClaimCheck_SendTheExpiryAsUtc_EvenWhenTheRowHasNoKind()
    {
        // A row read back from MySQL has Kind == Unspecified; serialized as-is it loses the trailing Z and
        // the plugin's Instant parser throws, which it treats as "API unreachable" (smoke test 2026-10-23).
        var unspecified = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(10), DateTimeKind.Unspecified);
        _t.Db.LinkCodes.Add(new LinkCode { Code = "K0QHM1XA", Purpose = LinkCodePurpose.Claim, MinecraftUuid = Lower, MinecraftName = "Steve",
            CreatedAt = DateTime.UtcNow, ExpiresAt = unspecified, Status = LinkCodeStatus.Active });
        await _t.Db.SaveChangesAsync();

        var gate = await Service().CheckAsync(Uuid, "Steve");
        var claim = await Service().CheckClaimAsync("K0QH-M1XA");

        Assert.Equal("K0QH-M1XA", gate.Code);
        Assert.Equal(DateTimeKind.Utc, gate.ExpiresAt!.Value.Kind);
        Assert.Equal(unspecified, gate.ExpiresAt.Value);
        Assert.Equal(DateTimeKind.Utc, claim.ExpiresAt!.Value.Kind);
        Assert.EndsWith("Z", System.Text.Json.JsonSerializer.Serialize(gate.ExpiresAt).Trim('"'));
    }

    [Fact]
    public async Task ClaimCheck_FoldsCrockfordLookAlikes()
    {
        _t.Db.LinkCodes.Add(new LinkCode { Code = "K0QHM1XA", Purpose = LinkCodePurpose.Claim, MinecraftUuid = Lower, MinecraftName = "Steve",
            CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddMinutes(15), Status = LinkCodeStatus.Active });
        await _t.Db.SaveChangesAsync();

        Assert.True((await Service().CheckClaimAsync("KOQH-MLXA")).Valid);
        Assert.True((await Service().CheckClaimAsync("koqh mixa")).Valid);
    }

    [Fact]
    public async Task ClaimCheck_IsInvalid_ForALinkCode_AUsedOrExpiredCode_OrGarbage()
    {
        _t.Db.LinkCodes.AddRange(
            new LinkCode { Code = "ABCD2345", Purpose = LinkCodePurpose.Link, UserId = _t.Admin.Id, CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(15), Status = LinkCodeStatus.Active },
            new LinkCode { Code = "7Q2HM4XA", Purpose = LinkCodePurpose.Claim, MinecraftUuid = Lower, MinecraftName = "Steve",
                CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddMinutes(15), Status = LinkCodeStatus.Used },
            new LinkCode { Code = "8Q2HM4XA", Purpose = LinkCodePurpose.Claim, MinecraftUuid = Lower, MinecraftName = "Steve",
                CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddMinutes(-1), Status = LinkCodeStatus.Active });
        await _t.Db.SaveChangesAsync();

        foreach (var code in new[] { "ABCD2345", "7Q2H-M4XA", "8Q2H-M4XA", "", null, "too-short" })
        {
            var check = await Service().CheckClaimAsync(code);
            Assert.False(check.Valid);
            Assert.Null(check.MinecraftName);
        }
    }

    [Fact]
    public async Task FinishLinking_RemindsInvitedAccountsWithoutMinecraft_Once()
    {
        var invitation = new Invitation { Kind = InvitationKind.Shared, CodeHash = new string('a', 64), CodeHint = "AAAA", MaxUses = 5,
            CreatedAt = InvitationTestDb.Now, CreatedByUserId = _t.Admin.Id, ExpiresAt = InvitationTestDb.Now.AddDays(7) };
        _t.Db.Invitations.Add(invitation);
        await _t.Db.SaveChangesAsync();
        User Invited(string name, string? uuid, int daysAgo) => new()
        {
            Username = name, Uuid = uuid, Email = name + "@example.com", CreatedAt = InvitationTestDb.Now.AddDays(-daysAgo),
            AdmittedAt = InvitationTestDb.Now, InvitedViaInvitationId = invitation.Id
        };
        var due = Invited("web-due", null, 3);
        _t.Db.Users.AddRange(due, Invited("web-new", null, 1), Invited("linked", Lower, 3), Invited("web-old", null, 30));
        await _t.Db.SaveChangesAsync();

        await Service().QueueFinishLinkingRemindersAsync();
        await Service().QueueFinishLinkingRemindersAsync();

        var mail = Assert.Single(_t.Db.EmailOutbox);
        Assert.Equal("FinishLinking", mail.Kind);
        Assert.Equal("web-due@example.com", mail.ToAddress);
        Assert.Equal($"finish-linking:{due.Id}", mail.DedupeKey);
    }

    [Fact]
    public async Task FinishLinking_IsOff_WithZeroDays()
    {
        _settings.FinishLinkingReminderDays = 0;

        Assert.Equal(0, await Service().QueueFinishLinkingRemindersAsync());
    }
}
