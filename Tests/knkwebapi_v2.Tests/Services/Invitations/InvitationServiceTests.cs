using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Invitations;

namespace knkwebapi_v2.Tests.Services.Invitations;

/// <summary>
/// InvitationService (KNG-212, knk-workspace docs/specs/invitations/IMPLEMENTATION_PLAN.md § 5.1): clamps
/// and caps, the label rule, the group allow-list and escalation guard, duplicate personal invites,
/// resend limit and rotation, pause/revoke, hash-only storage and a check that looks the same for every
/// failure.
/// </summary>
public class InvitationServiceTests : IDisposable
{
    private readonly InvitationTestDb _t = new();
    private readonly InvitationService _service;

    public InvitationServiceTests()
    {
        _service = _t.Service();
    }

    public void Dispose() => _t.Dispose();

    private async Task<PersonalInvitationResultDto> PersonalAsync(string email = "tester@example.com", int? expiryDays = null,
        bool sendEmail = true, int? groupId = null)
    {
        var outcome = await _service.CreatePersonalAsync(_t.Admin.Id, new CreatePersonalInvitationsRequestDto
        {
            Emails = new() { email }, ExpiryDays = expiryDays, SendEmail = sendEmail, GroupId = groupId
        });
        Assert.True(outcome.Ok, outcome.Message);
        return outcome.Value!.Results.Single();
    }

    private async Task<SharedInvitationResultDto> SharedAsync(int? maxUses = null, int? expiryDays = null, string label = "Discord 21 Oct")
    {
        var outcome = await _service.CreateSharedAsync(_t.Admin.Id, new CreateSharedInvitationRequestDto
        {
            Label = label, MaxUses = maxUses, ExpiryDays = expiryDays
        });
        Assert.True(outcome.Ok, outcome.Message);
        return outcome.Value!;
    }

    // ---------------------------------------------------------------- personal

    [Theory]
    [InlineData(null, 7)]
    [InlineData(0, 1)]
    [InlineData(14, 14)]
    [InlineData(99, 30)]
    public async Task Personal_ClampsTheExpiry(int? requested, int expectedDays)
    {
        var result = await PersonalAsync(expiryDays: requested);

        Assert.Equal("Created", result.Outcome);
        Assert.Equal(InvitationTestDb.Now.AddDays(expectedDays), result.Invitation!.ExpiresAt);
        Assert.Equal(1, result.Invitation.MaxUses);
    }

    [Fact]
    public async Task Personal_StoresOnlyTheHash_AndQueuesTheInvitationMail()
    {
        var result = await PersonalAsync();
        var token = InvitationTestDb.TokenOf(result.Link!);

        Assert.StartsWith("https://knk.example/invite/", result.Link);
        Assert.Equal(43, token.Length);
        var row = await _t.Db.Invitations.SingleAsync();
        Assert.Equal(InvitationCodes.Hash(token), row.CodeHash);
        Assert.Equal(token[^4..], row.CodeHint);
        Assert.DoesNotContain(token, row.CodeHash);
        Assert.Equal("tester@example.com", row.Email);

        var mail = await _t.Db.EmailOutbox.SingleAsync();
        Assert.Equal("Invitation", mail.Kind);
        Assert.Equal($"invite:{row.Id}:1", mail.DedupeKey);
        Assert.Equal(row.ExpiresAt, mail.ExpiresAt);
        Assert.Contains(result.Link!, mail.TextBody);
        Assert.Contains("Pandi", mail.TextBody);
        Assert.Equal("Pending", result.MailStatus);
        Assert.Equal(1, row.SendCount);
        _t.Audit.Verify(a => a.RecordAsync(_t.Admin.Id, _t.Admin.Id, AuditAction.InvitationCreated, It.Is<string>(d => !d.Contains(token))), Times.Once);
    }

    [Fact]
    public async Task Personal_WithoutSendEmail_GivesTheLinkOnly()
    {
        var result = await PersonalAsync(sendEmail: false);

        Assert.NotNull(result.Link);
        Assert.Null(result.MailStatus);
        Assert.Empty(_t.Db.EmailOutbox);
    }

    [Fact]
    public async Task Personal_SkipsAddressesInUse_Invalid_AndDuplicates()
    {
        var outcome = await _service.CreatePersonalAsync(_t.Admin.Id, new CreatePersonalInvitationsRequestDto
        {
            Emails = new() { "ADMIN@example.com\nnot-an-email\nnew@example.com", "new@example.com" }
        });

        var results = outcome.Value!.Results;
        Assert.Equal(new[] { "EmailInUse", "InvalidEmail", null, "Duplicate" }, results.Select(r => r.Reason));
        Assert.Equal(new[] { "Skipped", "Skipped", "Created", "Skipped" }, results.Select(r => r.Outcome));
        Assert.Single(_t.Db.Invitations);
    }

    [Fact]
    public async Task Personal_ForAnAddressWithAnActiveInvite_RevokesTheOldOne()
    {
        var first = await PersonalAsync();
        var second = await PersonalAsync();

        Assert.Equal(first.Invitation!.Id, second.ReplacedInvitationId);
        var old = await _t.Db.Invitations.SingleAsync(i => i.Id == first.Invitation.Id);
        Assert.NotNull(old.RevokedAt);
        Assert.Equal(_t.Admin.Id, old.RevokedByUserId);
        Assert.Null(await _service.FindUsableAsync(InvitationTestDb.TokenOf(first.Link!), null));
        Assert.NotNull(await _service.FindUsableAsync(InvitationTestDb.TokenOf(second.Link!), null));
    }

    [Fact]
    public async Task Personal_MailOverTheThrottle_IsReportedAsSuppressed()
    {
        // The invitation class allows 3 mails per recipient per hour (KNG-206 throttle).
        var statuses = new List<string?>();
        for (var i = 0; i < 4; i++)
        {
            statuses.Add((await PersonalAsync()).MailStatus);
        }

        Assert.Equal(new[] { "Pending", "Pending", "Pending", "Suppressed" }, statuses);
    }

    [Fact]
    public async Task Create_RefusesAGroupOutsideTheAllowList()
    {
        var outcome = await _service.CreatePersonalAsync(_t.Admin.Id, new CreatePersonalInvitationsRequestDto
        {
            Emails = new() { "tester@example.com" }, GroupId = _t.Moderator.Id
        });
        var shared = await _service.CreateSharedAsync(_t.Admin.Id, new CreateSharedInvitationRequestDto { Label = "x", GroupId = _t.Moderator.Id });

        Assert.Equal(InvitationErrors.GroupNotInvitable, outcome.Error);
        Assert.Equal(InvitationErrors.GroupNotInvitable, shared.Error);
        Assert.Empty(_t.Db.Invitations);
    }

    [Fact]
    public async Task Create_RefusesAGroupTheEscalationGuardRefuses()
    {
        _t.Guard.Setup(g => g.CanAssignGroupAsync(_t.Admin.Id, 0, _t.AlphaTester.Id))
            .ReturnsAsync(EscalationCheck.Refused(PermissionEscalationGuard.RankMessage));

        var outcome = await _service.CreateSharedAsync(_t.Admin.Id, new CreateSharedInvitationRequestDto { Label = "x", GroupId = _t.AlphaTester.Id });

        Assert.Equal(InvitationErrors.Escalation, outcome.Error);
        Assert.Equal(PermissionEscalationGuard.RankMessage, outcome.Message);
    }

    [Fact]
    public async Task Create_WithAnInvitableGroup_RecordsIt()
    {
        var result = await PersonalAsync(groupId: _t.AlphaTester.Id);

        Assert.Equal(_t.AlphaTester.Id, result.Invitation!.GroupId);
        Assert.Equal("Alpha Tester", result.Invitation.GroupName);
    }

    // ---------------------------------------------------------------- shared

    [Fact]
    public async Task Shared_NeedsALabel()
    {
        var outcome = await _service.CreateSharedAsync(_t.Admin.Id, new CreateSharedInvitationRequestDto { Label = "  " });

        Assert.Equal(InvitationErrors.Invalid, outcome.Error);
    }

    [Theory]
    [InlineData(null, 5)]
    [InlineData(0, 1)]
    [InlineData(20, 20)]
    [InlineData(999, 50)]
    public async Task Shared_ClampsMaxUsesToTheCap(int? requested, int expected)
    {
        var result = await SharedAsync(maxUses: requested);

        Assert.Equal(expected, result.Invitation.MaxUses);
    }

    [Fact]
    public async Task Shared_CodeHasTheDisplayForm_AndIsReadLeniently()
    {
        var result = await SharedAsync();

        Assert.Matches("^KNK-[0-9A-HJKMNP-TV-Z]{5}-[0-9A-HJKMNP-TV-Z]{5}$", result.Code);
        Assert.Equal($"https://knk.example/join?code={result.Code}", result.Link);
        var typed = result.Code.ToLowerInvariant().Replace("-", " ").Replace('0', 'o').Replace('1', 'l');
        Assert.NotNull(await _service.FindUsableAsync(null, typed));
        Assert.NotNull(await _service.FindUsableAsync(null, result.Code[4..]));
        // A shared code is never accepted as a personal token, and the other way round.
        Assert.Null(await _service.FindUsableAsync(result.Code, null));
    }

    [Fact]
    public async Task Shared_IsExhaustedAfterMaxUses()
    {
        var result = await SharedAsync(maxUses: 2);
        var repo = new knkwebapi_v2.Repositories.InvitationRepository(_t.Db);

        Assert.True(await repo.TryTakeUseAsync(result.Invitation.Id, InvitationTestDb.Now));
        Assert.True(await repo.TryTakeUseAsync(result.Invitation.Id, InvitationTestDb.Now));
        Assert.False(await repo.TryTakeUseAsync(result.Invitation.Id, InvitationTestDb.Now));
        Assert.Null(await _service.FindUsableAsync(null, result.Code));
    }

    // ---------------------------------------------------------------- resend / rotate

    [Fact]
    public async Task Resend_RotatesTheToken_AndRestartsTheExpiry()
    {
        var first = await PersonalAsync(expiryDays: 2);
        _t.Clock.Advance(TimeSpan.FromDays(3)); // expired by now

        var resent = await _service.ResendAsync(_t.Admin.Id, first.Invitation!.Id, sendEmail: true);

        Assert.True(resent.Ok, resent.Message);
        Assert.NotEqual(first.Link, resent.Value!.Link);
        Assert.Null(await _service.FindUsableAsync(InvitationTestDb.TokenOf(first.Link!), null));
        Assert.NotNull(await _service.FindUsableAsync(InvitationTestDb.TokenOf(resent.Value.Link!), null));
        Assert.Equal(_t.Clock.UtcNow.AddDays(7), resent.Value.Invitation!.ExpiresAt);
        Assert.Equal(2, resent.Value.Invitation.SendCount);
        Assert.Contains(_t.Db.EmailOutbox, m => m.DedupeKey == $"invite:{first.Invitation.Id}:2");
    }

    [Fact]
    public async Task Resend_IsLimitedPerDay()
    {
        var invite = await PersonalAsync(sendEmail: false);
        for (var i = 0; i < 3; i++)
        {
            Assert.True((await _service.ResendAsync(_t.Admin.Id, invite.Invitation!.Id, sendEmail: false)).Ok);
        }

        var fourth = await _service.ResendAsync(_t.Admin.Id, invite.Invitation!.Id, sendEmail: false);
        _t.Clock.Advance(TimeSpan.FromHours(24));
        var nextDay = await _service.ResendAsync(_t.Admin.Id, invite.Invitation.Id, sendEmail: false);

        Assert.Equal(InvitationErrors.ResendLimit, fourth.Error);
        Assert.True(nextDay.Ok);
    }

    [Fact]
    public async Task Resend_AndRotate_OnlyForTheirKind()
    {
        var personal = await PersonalAsync();
        var shared = await SharedAsync();

        Assert.Equal(InvitationErrors.WrongKind, (await _service.ResendAsync(_t.Admin.Id, shared.Invitation.Id, true)).Error);
        Assert.Equal(InvitationErrors.WrongKind, (await _service.RotateAsync(_t.Admin.Id, personal.Invitation!.Id)).Error);
        Assert.Equal(InvitationErrors.WrongKind, (await _service.PauseAsync(_t.Admin.Id, personal.Invitation.Id)).Error);
        Assert.Equal(InvitationErrors.NotFound, (await _service.RevokeAsync(_t.Admin.Id, 999)).Error);
    }

    [Fact]
    public async Task Rotate_GivesANewCode_WithTheSameCounters()
    {
        var shared = await SharedAsync(maxUses: 3);
        await new knkwebapi_v2.Repositories.InvitationRepository(_t.Db).TryTakeUseAsync(shared.Invitation.Id, InvitationTestDb.Now);

        var rotated = await _service.RotateAsync(_t.Admin.Id, shared.Invitation.Id);

        Assert.NotEqual(shared.Code, rotated.Value!.Code);
        Assert.Equal((1, 3), (rotated.Value.Invitation.UseCount, rotated.Value.Invitation.MaxUses));
        Assert.Null(await _service.FindUsableAsync(null, shared.Code));
        Assert.NotNull(await _service.FindUsableAsync(null, rotated.Value.Code));
    }

    // ---------------------------------------------------------------- pause / revoke / check

    [Fact]
    public async Task PauseResumeAndRevoke_ChangeWhetherTheCodeWorks()
    {
        var shared = await SharedAsync();
        var id = shared.Invitation.Id;

        Assert.Equal("Paused", (await _service.PauseAsync(_t.Admin.Id, id)).Value!.Status);
        Assert.Null(await _service.FindUsableAsync(null, shared.Code));
        Assert.Equal("Active", (await _service.ResumeAsync(_t.Admin.Id, id)).Value!.Status);
        Assert.NotNull(await _service.FindUsableAsync(null, shared.Code));
        Assert.Equal("Revoked", (await _service.RevokeAsync(_t.Admin.Id, id)).Value!.Status);
        Assert.Null(await _service.FindUsableAsync(null, shared.Code));
        Assert.Equal(InvitationErrors.NotActive, (await _service.ResumeAsync(_t.Admin.Id, id)).Error);
        _t.Audit.Verify(a => a.RecordAsync(_t.Admin.Id, _t.Admin.Id, AuditAction.InvitationRevoked, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Check_AnswersTheSameForEveryFailure()
    {
        var revoked = await SharedAsync();
        await _service.RevokeAsync(_t.Admin.Id, revoked.Invitation.Id);
        var paused = await SharedAsync();
        await _service.PauseAsync(_t.Admin.Id, paused.Invitation.Id);
        var exhausted = await SharedAsync(maxUses: 1);
        await new knkwebapi_v2.Repositories.InvitationRepository(_t.Db).TryTakeUseAsync(exhausted.Invitation.Id, InvitationTestDb.Now);
        var expired = await SharedAsync(expiryDays: 1);

        _t.Clock.Advance(TimeSpan.FromDays(1));
        var answers = new[]
        {
            await _service.CheckAsync(null, "KNK-00000-00000"),
            await _service.CheckAsync(null, "garbage"),
            await _service.CheckAsync("not-a-token", null),
            await _service.CheckAsync(null, revoked.Code),
            await _service.CheckAsync(null, paused.Code),
            await _service.CheckAsync(null, exhausted.Code),
            await _service.CheckAsync(null, expired.Code),
            await _service.CheckAsync(null, null),
        };

        Assert.All(answers, a =>
        {
            Assert.False(a.Valid);
            Assert.Equal("InvalidInvite", a.Error);
            Assert.Null(a.Kind);
            Assert.Null(a.Email);
            Assert.Null(a.ExpiresAt);
        });
    }

    [Fact]
    public async Task Check_ForAPersonalInvite_GivesTheBoundAddress()
    {
        var personal = await PersonalAsync();

        var answer = await _service.CheckAsync(InvitationTestDb.TokenOf(personal.Link!), null);

        Assert.True(answer.Valid);
        Assert.Equal("Personal", answer.Kind);
        Assert.Equal("tester@example.com", answer.Email);
        Assert.NotEqual("tester@example.com", answer.EmailHint);
        Assert.Equal(personal.Invitation!.ExpiresAt, answer.ExpiresAt);
    }

    [Fact]
    public async Task List_FiltersByStatusAndKind()
    {
        await PersonalAsync();
        var shared = await SharedAsync();
        await _service.RevokeAsync(_t.Admin.Id, shared.Invitation.Id);

        var active = await _service.ListAsync(1, 25, "Active", null);
        var revoked = await _service.ListAsync(1, 25, "revoked", "Shared");
        var all = await _service.ListAsync(1, 25, null, null);

        Assert.Equal("Personal", Assert.Single(active.Items).Kind);
        Assert.Equal(shared.Invitation.Id, Assert.Single(revoked.Items).Id);
        Assert.Equal(2, all.TotalCount);
        Assert.Equal("Pandi", all.Items[0].CreatedByName);
    }

    [Fact]
    public async Task Options_ListOnlyInvitableGroupsThatExist()
    {
        _t.Settings.InvitableGroups.Add("Missing Group");

        var options = await _t.Service().GetOptionsAsync();

        Assert.Equal(new[] { "Alpha Tester" }, options.Groups.Select(g => g.Name));
        Assert.Equal((7, 30, 5, 50), (options.DefaultExpiryDays, options.MaxExpiryDays, options.SharedDefaultMaxUses, options.SharedMaxUsesCap));
        Assert.Equal("LinkCode", options.RegistrationMode);
    }

    // ---------------------------------------------------------------- redemption

    [Fact]
    public async Task Redeem_CreatesAnAdmittedWebShell_WithTheInvitesGroup()
    {
        var shared = await SharedAsync();
        var invitation = await _t.Db.Invitations.SingleAsync();
        invitation.GrantGroupId = _t.AlphaTester.Id;
        await _t.Db.SaveChangesAsync();

        var user = await _service.RedeemAsync(new InviteSignup(invitation, "new@example.com", "bcrypt-hash", EmailVerified: false));

        Assert.NotNull(user);
        Assert.Matches("^web-[0-9a-hjkmnp-tv-z]{10}$", user!.Username);
        Assert.Null(user.Uuid);
        Assert.Equal(AccountCreationMethod.WebApp, user.AccountCreatedVia);
        Assert.Equal(_t.Clock.UtcNow, user.AdmittedAt);
        Assert.Null(user.EmailVerifiedAt);
        Assert.False(user.EmailVerified);
        Assert.Equal(invitation.Id, user.InvitedViaInvitationId);
        Assert.Equal(_t.Admin.Id, user.InvitedByUserId);
        var groups = _t.Db.UserPermissionGroups.Where(m => m.UserId == user.Id).Select(m => m.PermissionGroupId).OrderBy(g => g).ToList();
        Assert.Equal(new[] { _t.DefaultGroup.Id, _t.AlphaTester.Id }.OrderBy(g => g), groups);
        var redemption = await _t.Db.InvitationRedemptions.SingleAsync();
        Assert.Equal((invitation.Id, user.Id), (redemption.InvitationId, redemption.UserId));
        Assert.Equal(1, (await _t.Db.Invitations.SingleAsync()).UseCount);
        var welcome = await _t.Db.EmailOutbox.SingleAsync(m => m.Kind == "Welcome");
        Assert.Equal($"welcome:{user.Id}", welcome.DedupeKey);
        Assert.Contains("https://knk.example/account/link", welcome.TextBody);
        Assert.DoesNotContain(user.Username, welcome.TextBody);
        _t.Audit.Verify(a => a.RecordAsync(user.Id, user.Id, AuditAction.InvitationRedeemed, It.IsAny<string>()), Times.Once);
        Assert.NotNull(shared.Code);
    }

    [Fact]
    public async Task Redeem_OfAnExhaustedInvite_CreatesNothing()
    {
        await SharedAsync(maxUses: 1);
        var invitation = await _t.Db.Invitations.SingleAsync();
        Assert.NotNull(await _service.RedeemAsync(new InviteSignup(invitation, "one@example.com", "h", false)));

        var second = await _service.RedeemAsync(new InviteSignup(invitation, "two@example.com", "h", false));

        Assert.Null(second);
        Assert.Equal(2, _t.Db.Users.Count()); // the admin and the first redeemer
        Assert.Single(_t.Db.InvitationRedemptions);
    }

    [Fact]
    public async Task Redemptions_ShowTheMaskedEmailAndLinkState()
    {
        await SharedAsync();
        var invitation = await _t.Db.Invitations.SingleAsync();
        var user = await _service.RedeemAsync(new InviteSignup(invitation, "new@example.com", "h", false));

        var list = await _service.GetRedemptionsAsync(invitation.Id);

        var row = Assert.Single(list.Value!);
        Assert.Equal(user!.Id, row.UserId);
        Assert.False(row.MinecraftLinked);
        Assert.NotEqual("new@example.com", row.EmailMasked);
        Assert.EndsWith("@example.com", row.EmailMasked);
    }
}
