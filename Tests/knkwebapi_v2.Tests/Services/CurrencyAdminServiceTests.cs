using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Staff currency tooling (currency-payments IMPLEMENTATION_PLAN.md Phase 4) on the real ledger
/// over EF InMemory: transaction detail, reversals (full, partial, already reversed, would go
/// negative, retried) with their audit entries, transfer locks, policy edits, and the per-staff
/// daily grant cap with its knk.admin.currency.unlimited bypass. Locking and the reversal race
/// are covered against MySQL in Tests/MySql/CurrencyAdminMySqlTests.cs.
/// </summary>
public class CurrencyAdminServiceTests
{
    private const int Staff = 900;

    private readonly string _db = $"currency-admin-{Guid.NewGuid()}";
    private readonly Mock<IPermissionResolutionService> _permissions = new();

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_db).Options);

    private (CurrencyService Currency, CurrencyAdminService Admin) Services(KnKDbContext ctx)
    {
        var users = new UserRepository(ctx);
        var titles = new TitleService(new TitleBracketRepository(ctx));
        var audit = new AuditLogService(new AuditLogRepository(ctx), users);
        var memberships = new UserPermissionGroupService(new UserPermissionGroupRepository(ctx), users, new PermissionGroupRepository(ctx), audit);
        var currency = new CurrencyService(new CurrencyRepository(ctx), users, NullLogger<CurrencyService>.Instance, _permissions.Object);
        var admin = new CurrencyAdminService(currency, new CurrencyRepository(ctx), users, audit,
            new TitleProgressionService(currency, users, titles, memberships, audit));
        return (currency, admin);
    }

    private async Task SeedAsync()
    {
        await using var ctx = NewContext();
        ctx.Users.Add(new User { Id = 1, Username = "alice", Coins = 100 });
        ctx.Users.Add(new User { Id = 2, Username = "bob", Coins = 0 });
        ctx.Users.Add(new User { Id = Staff, Username = "moderator" });
        ctx.CurrencyPolicies.Add(new CurrencyPolicy
        {
            Currency = Currency.Coins, Transferable = true, MinTransfer = 10, MaxTransfer = 1_000_000, DailySendCap = 2_000_000,
            DailyReceiveCap = 4_000_000, ConfirmThreshold = 100_000, MaxBalance = 999_999_999, AdminDailyGrantCapPerActor = 1_000, SignupGrant = 250
        });
        ctx.CurrencyPolicies.Add(new CurrencyPolicy
        {
            Currency = Currency.Gems, MinTransfer = 1, MaxTransfer = 100, DailySendCap = 500, DailyReceiveCap = 500,
            ConfirmThreshold = 10, MaxBalance = 999_999, AdminDailyGrantCapPerActor = 0, SignupGrant = 50
        });
        ctx.TitleBrackets.Add(new TitleBracket { Id = 1, MaleName = "Peasant", FemaleName = "Peasant", MinExperience = 0 });
        await ctx.SaveChangesAsync();
    }

    private static KnkCaller WebStaff => new(isPluginService: false, isWebUser: true, webUserId: Staff, actingUserId: null);

    private static CurrencyContext StaffCtx(CurrencyOperation mode, string key, int actor = Staff,
        CurrencyInitiator initiator = CurrencyInitiator.Admin) => new()
    {
        IdempotencyKey = key,
        IdempotencyScope = CurrencyIdempotencyScopes.Web,
        ReasonCode = CurrencyReasons.ForAdminMode(mode),
        Reason = "Compensation: lost items to lag",
        Initiator = initiator,
        InitiatorUserId = initiator == CurrencyInitiator.PluginService ? null : actor,
        InitiatorComponent = "Test"
    };

    private async Task<User> UserAsync(int id)
    {
        await using var ctx = NewContext();
        return await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == id);
    }

    private async Task<List<AuditLogEntry>> AuditAsync(AuditAction action)
    {
        await using var ctx = NewContext();
        return await ctx.AuditLogEntries.AsNoTracking().Where(a => a.Action == action).ToListAsync();
    }

    private async Task<PostingResult> GrantAsync(int userId, long amount, string key)
    {
        await using var ctx = NewContext();
        return await Services(ctx).Currency.GrantAsync(userId, Currency.Coins, amount,
            CurrencyContext.ForSystem("SalaryService", CurrencyReasons.Salary, key));
    }

    // ===== Detail =====

    [Fact]
    public async Task Detail_ShowsEveryLegWithNames_AndAcceptsTheInGameTxPrefix()
    {
        await SeedAsync();
        var grant = await GrantAsync(1, 50, "salary:1:t0");
        await using var ctx = NewContext();

        var detail = await Services(ctx).Admin.GetTransactionAsync($"tx {grant.PublicId.ToLowerInvariant()}");

        Assert.Equal((grant.PublicId, "SALARY", "System", "SalaryService"), (detail.PublicId, detail.ReasonCode, detail.Initiator, detail.InitiatorComponent));
        Assert.True(detail.Reversible);
        var user = Assert.Single(detail.Entries, e => e.AccountKind == "User");
        Assert.Equal(("alice", 50L, (long?)100, (long?)150), (user.Username, user.Amount, user.BalanceBefore, user.BalanceAfter));
        var system = Assert.Single(detail.Entries, e => e.AccountKind == "System");
        Assert.Equal((CurrencyReasons.SysSalary, -50L), (system.SystemAccount, system.Amount));

        var missing = await Assert.ThrowsAsync<CurrencyException>(() => Services(ctx).Admin.GetTransactionAsync("nope"));
        Assert.Equal(CurrencyErrorCode.TransactionNotFound, missing.Code);
    }

    // ===== Reversals =====

    [Fact]
    public async Task Reverse_UndoesTheGrant_AuditsIt_AndLinksBothWays()
    {
        await SeedAsync();
        var grant = await GrantAsync(1, 50, "salary:1:t0");
        await using var ctx = NewContext();
        var (_, admin) = Services(ctx);

        var result = await admin.ReverseAsync(grant.PublicId, new ReverseTransactionDto { Note = "Paid twice by a bug" }, WebStaff, "WebAppLedger");

        Assert.False(result.Partial);
        Assert.False(result.Posting.Replayed);
        Assert.Equal(100, (await UserAsync(1)).Coins);
        var audit = Assert.Single(await AuditAsync(AuditAction.CurrencyTransactionReversed));
        Assert.Equal((Staff, 1), (audit.ActorUserId, audit.TargetUserId));
        Assert.Contains(grant.PublicId, audit.Details);

        await using var read = NewContext();
        var original = await Services(read).Admin.GetTransactionAsync(grant.PublicId);
        Assert.Equal((result.Posting.PublicId, false), (original.ReversedByPublicId, original.Reversible));
        var reversal = await Services(read).Admin.GetTransactionAsync(result.Posting.PublicId);
        Assert.Equal((grant.PublicId, "Admin", Staff, "moderator", false), (reversal.ReversesPublicId, reversal.Initiator, reversal.InitiatorUserId, reversal.InitiatorUsername, reversal.Reversible));
        Assert.Equal("Paid twice by a bug", reversal.Reason);
    }

    [Fact]
    public async Task Reverse_Again_IsAlreadyReversed_WithWhenAndByWhom_EvenAsASameKeyRetry()
    {
        // KNG-21 smoke test: a retry used to replay the stored reversal, so staff saw "reversed"
        // for a transaction that had been reversed long before.
        await SeedAsync();
        var grant = await GrantAsync(1, 50, "salary:1:t0");
        await using var ctx = NewContext();
        var (_, admin) = Services(ctx);
        var request = new ReverseTransactionDto { Note = "Paid twice by a bug" };

        var first = await admin.ReverseAsync(grant.PublicId, request, WebStaff, "WebAppLedger");

        var plugin = new KnkCaller(isPluginService: true, isWebUser: false, webUserId: null, actingUserId: Staff);
        foreach (var (caller, component, partial) in new[] { (WebStaff, "WebAppLedger", false), (WebStaff, "WebAppLedger", true), (plugin, "PluginCurrencyAdmin", false) })
        {
            var again = await Assert.ThrowsAsync<CurrencyException>(() =>
                admin.ReverseAsync(grant.PublicId, new ReverseTransactionDto { Note = request.Note, AllowPartial = partial }, caller, component));
            Assert.Equal(CurrencyErrorCode.AlreadyReversed, again.Code);
            var details = Assert.IsType<AlreadyReversedDetailsDto>(again.Details);
            Assert.Equal((first.Posting.PublicId, (int?)Staff, "moderator"), (details.ReversalTransactionPublicId, details.ReversedByUserId, details.ReversedByUsername));
            Assert.Equal(first.Posting.CreatedAt, details.ReversedAt, TimeSpan.FromSeconds(1));
            Assert.Contains("moderator", again.Message);
        }

        Assert.Equal(100, (await UserAsync(1)).Coins);
        Assert.Single(await AuditAsync(AuditAction.CurrencyTransactionReversed));

        var detail = await admin.GetTransactionAsync(grant.PublicId);
        Assert.Equal((first.Posting.PublicId, (int?)Staff, "moderator", false),
            (detail.ReversedByPublicId, detail.ReversedByUserId, detail.ReversedByUsername, detail.Reversible));
        Assert.NotNull(detail.ReversedAt);
    }

    [Fact]
    public async Task Reverse_OfSpentMoney_IsRefused_UnlessPartial()
    {
        await SeedAsync();
        var grant = await GrantAsync(2, 50, "salary:2:t0");
        await using (var spend = NewContext())
        {
            await Services(spend).Currency.SpendAsync(2, Currency.Coins, 30,
                CurrencyContext.ForSystem("KitService", CurrencyReasons.KitPurchase, "kit-purchase:1:2"));
        }
        await using var ctx = NewContext();
        var (_, admin) = Services(ctx);

        var refused = await Assert.ThrowsAsync<CurrencyException>(() =>
            admin.ReverseAsync(grant.PublicId, new ReverseTransactionDto { Note = "Wrong player got it" }, WebStaff, "WebAppLedger"));
        Assert.Equal(CurrencyErrorCode.ReversalWouldGoNegative, refused.Code);
        Assert.Empty(await AuditAsync(AuditAction.CurrencyTransactionReversed));

        await using var ctx2 = NewContext();
        var partial = await Services(ctx2).Admin.ReverseAsync(grant.PublicId,
            new ReverseTransactionDto { Note = "Wrong player got it", AllowPartial = true }, WebStaff, "WebAppLedger");
        Assert.True(partial.Partial);
        Assert.Equal(0, (await UserAsync(2)).Coins);
        Assert.Equal(-20, Assert.Single(partial.Posting.Entries).Amount);
    }

    [Fact]
    public async Task Reverse_NeedsANoteOfTenCharacters_AndCantReverseAReversal()
    {
        await SeedAsync();
        var grant = await GrantAsync(1, 50, "salary:1:t0");
        await using var ctx = NewContext();
        var (_, admin) = Services(ctx);

        await Assert.ThrowsAsync<ArgumentException>(() => admin.ReverseAsync(grant.PublicId, new ReverseTransactionDto { Note = "  oops  " }, WebStaff, "WebAppLedger"));
        var reversal = await admin.ReverseAsync(grant.PublicId, new ReverseTransactionDto { Note = "Paid twice by a bug" }, WebStaff, "WebAppLedger");
        var undo = await Assert.ThrowsAsync<CurrencyException>(() =>
            admin.ReverseAsync(reversal.Posting.PublicId, new ReverseTransactionDto { Note = "Undo that reversal" }, WebStaff, "WebAppLedger"));
        Assert.Equal(CurrencyErrorCode.NotReversible, undo.Code);
    }

    // ===== Per-staff daily grant cap =====

    [Fact]
    public async Task AdminGrants_OverTheDailyCap_AreRefused_AndRemovalsDontCount()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var (currency, _) = Services(ctx);

        await currency.AdminAdjustAsync(new AdminAdjustRequest(1, Currency.Coins, CurrencyOperation.Add, 600), StaffCtx(CurrencyOperation.Add, "a1"));
        await currency.AdminAdjustAsync(new AdminAdjustRequest(1, Currency.Coins, CurrencyOperation.Remove, 500), StaffCtx(CurrencyOperation.Remove, "a2"));
        // Set upward is a grant too: 200 → 500 is +300 (900 of 1,000 used).
        await currency.AdminAdjustAsync(new AdminAdjustRequest(1, Currency.Coins, CurrencyOperation.Set, 500), StaffCtx(CurrencyOperation.Set, "a3"));

        var over = await Assert.ThrowsAsync<CurrencyException>(() =>
            currency.AdminAdjustAsync(new AdminAdjustRequest(2, Currency.Coins, CurrencyOperation.Add, 101), StaffCtx(CurrencyOperation.Add, "a4")));
        Assert.Equal(CurrencyErrorCode.AdminDailyCapExceeded, over.Code);
        Assert.Equal(0, (await UserAsync(2)).Coins);

        // Exactly up to the cap is fine; another staff member has their own allowance.
        await currency.AdminAdjustAsync(new AdminAdjustRequest(2, Currency.Coins, CurrencyOperation.Add, 100), StaffCtx(CurrencyOperation.Add, "a5"));
        await currency.AdminAdjustAsync(new AdminAdjustRequest(2, Currency.Coins, CurrencyOperation.Add, 1_000), StaffCtx(CurrencyOperation.Add, "a6", actor: 1));
        Assert.Equal(1_100, (await UserAsync(2)).Coins);
    }

    [Fact]
    public async Task AdminGrants_WithTheUnlimitedNode_OrWithoutACap_AreNotCapped()
    {
        await SeedAsync();
        _permissions.Setup(p => p.CheckAsync(Staff, StaffPermissions.CurrencyUnlimited))
            .ReturnsAsync(new PermissionCheckResponseDto { Result = PermissionResolutionResult.Granted });
        await using var ctx = NewContext();
        var (currency, _) = Services(ctx);

        await currency.AdminAdjustAsync(new AdminAdjustRequest(2, Currency.Coins, CurrencyOperation.Add, 5_000), StaffCtx(CurrencyOperation.Add, "b1"));
        // Gems policy cap 0 = none; the plugin service with no staff member named isn't capped either.
        await currency.AdminAdjustAsync(new AdminAdjustRequest(2, Currency.Gems, CurrencyOperation.Add, 5_000), StaffCtx(CurrencyOperation.Add, "b2", actor: 1));
        await currency.AdminAdjustAsync(new AdminAdjustRequest(2, Currency.Coins, CurrencyOperation.Add, 5_000),
            StaffCtx(CurrencyOperation.Add, "b3", initiator: CurrencyInitiator.PluginService));

        var bob = await UserAsync(2);
        Assert.Equal((10_000, 5_000), (bob.Coins, bob.Gems));
    }

    private async Task AddBracketAsync(int id, int minExperience, int coinBonus, int gemBonus = 0)
    {
        await using var ctx = NewContext();
        ctx.TitleBrackets.Add(new TitleBracket { Id = id, MaleName = $"T{id}", FemaleName = $"T{id}", MinExperience = minExperience, CoinBonus = coinBonus, GemBonus = gemBonus });
        await ctx.SaveChangesAsync();
    }

    /// <summary>What UserService.AdjustBalancesAsync does for a staff XP add: the adjustment, then
    /// title progression for it (the caller's transaction rolls both back on a refusal).</summary>
    private async Task<Dictionary<int, TitleChangeResultDto>> StaffXpAsync(int userId, long amount, string key, int actor = Staff)
    {
        await using var ctx = NewContext();
        var users = new UserRepository(ctx);
        var (currency, _) = Services(ctx);
        var titles = new TitleProgressionService(currency, users, new TitleService(new TitleBracketRepository(ctx)),
            new UserPermissionGroupService(new UserPermissionGroupRepository(ctx), users, new PermissionGroupRepository(ctx),
                new AuditLogService(new AuditLogRepository(ctx), users)),
            new AuditLogService(new AuditLogRepository(ctx), users));
        var posting = await currency.AdminAdjustAsync(new AdminAdjustRequest(userId, Currency.Experience, CurrencyOperation.Add, amount),
            StaffCtx(CurrencyOperation.Add, key, actor));
        return await titles.ApplyForPostingAsync(posting, actor);
    }

    private async Task<List<CurrencyTransaction>> TitleBonusesAsync()
    {
        await using var ctx = NewContext();
        return await ctx.CurrencyTransactions.AsNoTracking().Include(t => t.Entries)
            .Where(t => t.ReasonCode == CurrencyReasons.TitleBonus).ToListAsync();
    }

    [Fact]
    public async Task TitleBonus_OfAStaffXpIncrease_CountsAgainstTheirDailyCap_AndOverItIsRefused()
    {
        // KNG-21 smoke test: raising XP must not be a way around the per-staff coin cap.
        await SeedAsync();
        await AddBracketAsync(2, 100, coinBonus: 800);
        await using (var ctx = NewContext())
        {
            await Services(ctx).Currency.AdminAdjustAsync(new AdminAdjustRequest(1, Currency.Coins, CurrencyOperation.Add, 300), StaffCtx(CurrencyOperation.Add, "c1"));
        }

        // 300 granted + an 800 bonus > 1,000.
        var over = await Assert.ThrowsAsync<CurrencyException>(() => StaffXpAsync(2, 100, "x1"));
        Assert.Equal(CurrencyErrorCode.AdminDailyCapExceeded, over.Code);
        Assert.Contains("title promotion bonus", over.Message);
        Assert.Empty(await TitleBonusesAsync());
        Assert.Equal(0, (await UserAsync(2)).Coins);

        // Another staff member with room pays it, and it then counts against their allowance.
        const int other = 901;
        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User { Id = other, Username = "helper" });
            ctx.Users.Add(new User { Id = 3, Username = "carol" });
            await ctx.SaveChangesAsync();
        }
        var change = Assert.Single(await StaffXpAsync(3, 100, "x2", other));
        Assert.Equal(800, change.Value.CoinBonusGranted);
        var bonus = Assert.Single(await TitleBonusesAsync());
        Assert.Equal(CurrencyInitiator.System, bonus.Initiator);
        Assert.NotNull(bonus.CorrelationId);
        await using (var ctx = NewContext())
        {
            var currency = Services(ctx).Currency;
            var capped = await Assert.ThrowsAsync<CurrencyException>(() =>
                currency.AdminAdjustAsync(new AdminAdjustRequest(1, Currency.Coins, CurrencyOperation.Add, 201), StaffCtx(CurrencyOperation.Add, "c2", other)));
            Assert.Equal(CurrencyErrorCode.AdminDailyCapExceeded, capped.Code);
            await currency.AdminAdjustAsync(new AdminAdjustRequest(1, Currency.Coins, CurrencyOperation.Add, 200), StaffCtx(CurrencyOperation.Add, "c3", other));
        }
    }

    [Fact]
    public async Task TitleBonus_OfAStaffXpIncrease_WithTheUnlimitedNode_OrFromAGame_IsNotCapped()
    {
        await SeedAsync();
        await AddBracketAsync(2, 100, coinBonus: 5_000, gemBonus: 3);
        _permissions.Setup(p => p.CheckAsync(Staff, StaffPermissions.CurrencyUnlimited))
            .ReturnsAsync(new PermissionCheckResponseDto { Result = PermissionResolutionResult.Granted });

        Assert.Equal(5_000, Assert.Single(await StaffXpAsync(2, 100, "x1")).Value.CoinBonusGranted);

        // A non-staff XP source (siege, salary…) triggers bonuses outside any staff cap.
        _permissions.Setup(p => p.CheckAsync(Staff, StaffPermissions.CurrencyUnlimited))
            .ReturnsAsync(new PermissionCheckResponseDto { Result = PermissionResolutionResult.Denied });
        await using var ctx = NewContext();
        var users = new UserRepository(ctx);
        var (currency, _) = Services(ctx);
        var audit = new AuditLogService(new AuditLogRepository(ctx), users);
        var titles = new TitleProgressionService(currency, users, new TitleService(new TitleBracketRepository(ctx)),
            new UserPermissionGroupService(new UserPermissionGroupRepository(ctx), users, new PermissionGroupRepository(ctx), audit), audit);
        var siege = await currency.PostAsync(new[] { new CurrencyLeg(1, Currency.Experience, 100) },
            CurrencyContext.ForSystem("SiegeMatchService", CurrencyReasons.SiegeReward, "siege-match:1"));
        Assert.Equal(5_000, Assert.Single(await titles.ApplyForPostingAsync(siege, Staff)).Value.CoinBonusGranted);
    }

    // ===== Transfer locks =====

    [Fact]
    public async Task TransferLock_SetAndClear_AreAudited_AndNeedAReason()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var (_, admin) = Services(ctx);

        await Assert.ThrowsAsync<ArgumentException>(() => admin.SetTransferLockAsync(1, "  ", Staff));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => admin.SetTransferLockAsync(404, "Alt funnel", Staff));

        var locked = await admin.SetTransferLockAsync(1, "Suspected alt funnel", Staff);
        Assert.True(locked.Locked);
        Assert.NotNull(locked.LockedAt);
        Assert.Equal("Suspected alt funnel", (await UserAsync(1)).TransferLockReason);
        await admin.SetTransferLockAsync(1, "Suspected alt funnel", Staff); // repeat: no second entry

        var unlocked = await admin.ClearTransferLockAsync(1, Staff);
        Assert.False(unlocked.Locked);
        Assert.Null((await UserAsync(1)).TransferLockReason);

        Assert.Single(await AuditAsync(AuditAction.CurrencyTransferLocked));
        Assert.Single(await AuditAsync(AuditAction.CurrencyTransferUnlocked));
    }

    // ===== Policy =====

    [Fact]
    public async Task Policy_Update_SavesAndAuditsTheChangedFields_AndRejectsBadValues()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var (_, admin) = Services(ctx);
        var gems = (await admin.GetPoliciesAsync()).Single(p => p.Currency == "Gems");
        Assert.False(gems.Transferable);
        Assert.Equal(999_999, gems.HardMaxBalance);

        gems.Transferable = true;
        gems.TransferFeeBasisPoints = 200;
        var saved = await admin.UpdatePolicyAsync(Currency.Gems, gems, Staff);

        Assert.Equal((true, 200, (int?)Staff), (saved.Transferable, saved.TransferFeeBasisPoints, saved.UpdatedByUserId));
        var audit = Assert.Single(await AuditAsync(AuditAction.CurrencyPolicyChanged));
        Assert.Contains("transferable", audit.Details);
        Assert.Contains("transferFeeBasisPoints", audit.Details);
        Assert.DoesNotContain("maxTransfer", audit.Details);

        await admin.UpdatePolicyAsync(Currency.Gems, saved, Staff); // unchanged: nothing to audit
        Assert.Single(await AuditAsync(AuditAction.CurrencyPolicyChanged));

        saved.MaxBalance = 5_000_000; // above the gem cap the database enforces
        await Assert.ThrowsAsync<ArgumentException>(() => admin.UpdatePolicyAsync(Currency.Gems, saved, Staff));
        saved.MaxBalance = 999_999;
        saved.MinSenderTitleBracketId = 77;
        await Assert.ThrowsAsync<ArgumentException>(() => admin.UpdatePolicyAsync(Currency.Gems, saved, Staff));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => admin.UpdatePolicyAsync(Currency.Experience, saved, Staff));
    }

    [Fact]
    public async Task Policy_Update_FromAFormLoadedBeforeTheKillSwitch_IsRefused_AndTransfersStayOff()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var (_, admin) = Services(ctx);
        var loaded = (await admin.GetPoliciesAsync()).Single(p => p.Currency == "Coins");
        Assert.True(loaded.TransfersEnabled);

        // The client echoes the JSON it got; the version must survive that round trip.
        var stale = JsonSerializer.Deserialize<CurrencyPolicyDto>(JsonSerializer.Serialize(loaded))!;
        stale.MaxTransfer = 500_000;

        // Meanwhile a reconciliation mismatch fires the R1 kill switch (in another request).
        await using (var monitorCtx = NewContext())
        {
            var options = new knkwebapi_v2.Configuration.CurrencyMonitorOptions();
            var alerts = new CurrencyAlertService(monitorCtx, new CurrencyReconciler(monitorCtx),
                new CurrencyAnomalyDetector(monitorCtx, Microsoft.Extensions.Options.Options.Create(options)),
                new CurrencyReconciliationState(), new CurrencyMonitorSignals(), NullLogger<CurrencyAlertService>.Instance,
                Microsoft.Extensions.Options.Options.Create(options));
            await alerts.RaiseAsync(new CurrencyAlertDraft(CurrencyAlertRules.Reconciliation, CurrencyAlertSeverity.Critical,
                "mismatch", "R1:test", TimeSpan.FromHours(24)) { DisableTransfersFor = new[] { Currency.Coins } });
        }

        await using var staffCtx = NewContext();
        var (_, staffAdmin) = Services(staffCtx);
        var ex = await Assert.ThrowsAsync<CurrencyException>(() => staffAdmin.UpdatePolicyAsync(Currency.Coins, stale, Staff));
        Assert.Equal(CurrencyErrorCode.PolicyChanged, ex.Code);
        Assert.Contains("safety shut-off", ex.Message);
        var current = Assert.IsType<CurrencyPolicyDto>(ex.Details);
        Assert.False(current.TransfersEnabled);
        Assert.Equal(1_000_000, current.MaxTransfer);

        await using (var check = NewContext())
        {
            var row = await check.CurrencyPolicies.AsNoTracking().SingleAsync(p => p.Currency == Currency.Coins);
            Assert.Equal((false, 1_000_000L), (row.TransfersEnabled, row.MaxTransfer));
        }
        Assert.Empty(await AuditAsync(AuditAction.CurrencyPolicyChanged));

        // Reloaded, the staff member can turn transfers back on deliberately.
        current.TransfersEnabled = true;
        var saved = await staffAdmin.UpdatePolicyAsync(Currency.Coins, current, Staff);
        Assert.True(saved.TransfersEnabled);

        // And the version they saved over is now stale too (another staff member's form).
        await Assert.ThrowsAsync<CurrencyException>(() => staffAdmin.UpdatePolicyAsync(Currency.Coins, current, Staff));
    }

    [Fact]
    public async Task Policy_Update_WithoutAVersion_IsRejected()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var (_, admin) = Services(ctx);
        var gems = (await admin.GetPoliciesAsync()).Single(p => p.Currency == "Gems");
        gems.UpdatedAt = default;
        await Assert.ThrowsAsync<ArgumentException>(() => admin.UpdatePolicyAsync(Currency.Gems, gems, Staff));
    }

    [Fact]
    public void Policy_Version_IsComparedAtTheColumnsMicrosecondPrecision()
    {
        var at = new DateTime(2026, 9, 27, 10, 0, 0, DateTimeKind.Utc).AddTicks(1_234_567);
        var policy = new CurrencyPolicy { UpdatedAt = CurrencyPolicy.VersionStamp(at) };
        Assert.Equal(1_234_560, policy.UpdatedAt.Ticks % TimeSpan.TicksPerSecond);
        Assert.True(policy.IsVersion(DateTime.SpecifyKind(policy.UpdatedAt, DateTimeKind.Unspecified))); // as read from MySQL
        Assert.True(policy.IsVersion(at));
        Assert.False(policy.IsVersion(at.AddTicks(10)));
        Assert.False(policy.IsVersion(at.AddMilliseconds(-1)));
    }
}
