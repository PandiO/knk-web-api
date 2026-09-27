using Xunit;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// The player-transfer rules (currency-payments IMPLEMENTATION_PLAN.md Phase 3 "policy matrix"),
/// on the seeded policy values and the developer's decisions (DESIGN.md §5): gems never
/// transferable, 0% fee, senders ≥ 48 h old holding Peasant (2,500 XP).
/// </summary>
public class TransferPolicyEvaluatorTests
{
    private static readonly DateTime Now = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TitleBracket Peasant = new() { Id = 1, MaleName = "Peasant", FemaleName = "Peasant", MinExperience = 2500 };

    public static CurrencyPolicy CoinPolicy() => new()
    {
        Currency = Currency.Coins, TransfersEnabled = true, Transferable = true, MinTransfer = 10, MaxTransfer = 1_000_000,
        DailySendCap = 2_000_000, DailyReceiveCap = 4_000_000, ConfirmThreshold = 100_000, ConfirmTtlSeconds = 60,
        CooldownSeconds = 5, MaxTransfersPerHour = 20, MinSenderAccountAgeHours = 48, MinSenderTitleBracketId = 1,
        TransferFeeBasisPoints = 0, MaxBalance = 999_999_999
    };

    public static CurrencyPolicy GemPolicy() => new()
    {
        Currency = Currency.Gems, TransfersEnabled = true, Transferable = false, MinTransfer = 1, MaxTransfer = 100,
        DailySendCap = 500, DailyReceiveCap = 500, ConfirmThreshold = 10, ConfirmTtlSeconds = 60,
        CooldownSeconds = 5, MaxTransfersPerHour = 10, MinSenderAccountAgeHours = 48, MinSenderTitleBracketId = 1,
        MaxBalance = 999_999
    };

    private static User Sender(int coins = 50_000, int xp = 3000, double ageHours = 100) => new()
    {
        Id = 1, Username = "alice", Coins = coins, Gems = 20, ExperiencePoints = xp, CreatedAt = Now.AddHours(-ageHours)
    };

    private static User Recipient(int coins = 0) => new() { Id = 2, Username = "bob", Coins = coins, CreatedAt = Now.AddDays(-1) };

    private static TransferPolicyInput Input(long amount = 1000, CurrencyPolicy? policy = null, User? sender = null, User? recipient = null,
        Currency currency = Currency.Coins, IReadOnlyList<(DateTime, long)>? sent = null, long received = 0, DateTime? last = null,
        bool bypass = false, TitleBracket? title = null, bool noTitle = false) => new()
    {
        Policy = policy ?? CoinPolicy(),
        Currency = currency,
        Amount = amount,
        Fee = 0,
        Sender = sender ?? Sender(),
        Recipient = recipient ?? Recipient(),
        Now = Now,
        SentLast24h = sent ?? Array.Empty<(DateTime, long)>(),
        RecipientReceivedLast24h = received,
        SenderLastTransferAt = last,
        RequiredTitle = noTitle ? null : title ?? Peasant,
        BypassLimits = bypass
    };

    private static CurrencyErrorCode Refusal(TransferPolicyInput input) =>
        Assert.Throws<CurrencyException>(() => TransferPolicyEvaluator.Check(input)).Code;

    [Fact]
    public void AnOrdinaryTransfer_Passes() => TransferPolicyEvaluator.Check(Input());

    [Theory]
    [InlineData(9)]
    [InlineData(1_000_001)]
    [InlineData(0)]
    [InlineData(-5)]
    public void AmountOutsideMinAndMax_IsRefused(long amount) => Assert.Equal(CurrencyErrorCode.AmountOutOfRange, Refusal(Input(amount)));

    [Fact]
    public void Gems_AreNeverTransferable_ByDefault() =>
        Assert.Equal(CurrencyErrorCode.NotTransferable, Refusal(Input(5, GemPolicy(), currency: Currency.Gems)));

    [Fact]
    public void Gems_Transferable_WhenThePolicySwitchIsOn()
    {
        var policy = GemPolicy();
        policy.Transferable = true;
        TransferPolicyEvaluator.Check(Input(5, policy, currency: Currency.Gems));
    }

    [Fact]
    public void Experience_IsNeverTransferable() =>
        Assert.Equal(CurrencyErrorCode.NotTransferable, Refusal(Input(100, currency: Currency.Experience)));

    [Fact]
    public void KillSwitch_RefusesEvenWithBypass()
    {
        var policy = CoinPolicy();
        policy.TransfersEnabled = false;
        Assert.Equal(CurrencyErrorCode.TransfersDisabled, Refusal(Input(policy: policy, bypass: true)));
    }

    [Fact]
    public void MissingPolicy_MeansTransfersDisabled()
    {
        var input = Input() with { Policy = null };
        Assert.Equal(CurrencyErrorCode.TransfersDisabled, Refusal(input));
    }

    [Fact]
    public void SelfTransfer_IsRefused()
    {
        var sender = Sender();
        Assert.Equal(CurrencyErrorCode.SelfTransfer, Refusal(Input(sender: sender, recipient: sender)));
    }

    [Fact]
    public void DeletedOrInactiveRecipient_IsNotFound()
    {
        var gone = Recipient();
        gone.IsActive = false;
        Assert.Equal(CurrencyErrorCode.RecipientNotFound, Refusal(Input(recipient: gone)));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void FrozenOrLockedSender_CantSend(bool frozen, bool locked)
    {
        var sender = Sender();
        sender.IsFrozen = frozen;
        sender.TransferLockReason = locked ? "investigation" : null;
        var ex = Assert.Throws<CurrencyException>(() => TransferPolicyEvaluator.Check(Input(sender: sender, bypass: true)));
        Assert.Equal(CurrencyErrorCode.AccountLocked, ex.Code);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void FrozenOrLockedRecipient_CantReceive(bool frozen, bool locked)
    {
        var recipient = Recipient();
        recipient.IsFrozen = frozen;
        recipient.TransferLockReason = locked ? "alt farm" : null;
        Assert.Equal(CurrencyErrorCode.AccountLocked, Refusal(Input(recipient: recipient)));
    }

    [Fact]
    public void SenderYoungerThan48h_IsRestricted() =>
        Assert.Equal(CurrencyErrorCode.NewAccountRestricted, Refusal(Input(sender: Sender(ageHours: 47.9))));

    [Fact]
    public void SenderBelowPeasant_IsRestricted()
    {
        var ex = Assert.Throws<CurrencyException>(() => TransferPolicyEvaluator.Check(Input(sender: Sender(xp: 2499))));
        Assert.Equal(CurrencyErrorCode.NewAccountRestricted, ex.Code);
        Assert.Contains("Peasant", ex.Message);
    }

    [Fact]
    public void NoTitleGate_WhenTheBracketIsMissing() => TransferPolicyEvaluator.Check(Input(sender: Sender(xp: 0), noTitle: true));

    [Fact]
    public void NewAccount_CanStillReceive() => TransferPolicyEvaluator.Check(Input(recipient: new User { Id = 2, Username = "new", CreatedAt = Now }));

    [Fact]
    public void Cooldown_RefusesASecondSendWithinFiveSeconds()
    {
        var ex = Assert.Throws<CurrencyException>(() => TransferPolicyEvaluator.Check(Input(last: Now.AddSeconds(-3))));
        Assert.Equal(CurrencyErrorCode.CooldownActive, ex.Code);
        TransferPolicyEvaluator.Check(Input(last: Now.AddSeconds(-5)));
    }

    [Fact]
    public void HourlyLimit_RefusesThe21stTransferInAnHour()
    {
        var sent = Enumerable.Range(1, 20).Select(i => (Now.AddMinutes(-i * 2), 10L)).ToList();
        Assert.Equal(CurrencyErrorCode.CooldownActive, Refusal(Input(sent: sent, last: Now.AddMinutes(-2))));
    }

    [Fact]
    public void DailySendCap_CountsTheLast24Hours_AndReportsWhatIsLeft()
    {
        var sent = new List<(DateTime, long)> { (Now.AddHours(-20), 1_000_000), (Now.AddHours(-10), 900_000) };
        var ex = Assert.Throws<CurrencyException>(() => TransferPolicyEvaluator.Check(Input(100_001, sent: sent, sender: Sender(coins: 5_000_000))));
        Assert.Equal(CurrencyErrorCode.DailyCapExceeded, ex.Code);
        Assert.Contains("100,000", ex.Message);
        TransferPolicyEvaluator.Check(Input(100_000, sent: sent, sender: Sender(coins: 5_000_000)));
    }

    [Fact]
    public void DailyReceiveCap_IsChecked()
    {
        Assert.Equal(CurrencyErrorCode.RecipientDailyCapExceeded, Refusal(Input(1000, received: 3_999_500)));
    }

    [Fact]
    public void Bypass_SkipsCapsCooldownAndAgeGate_ButNotFundsOrLocks()
    {
        var young = Sender(coins: 5_000_000, xp: 0, ageHours: 1);
        var sent = new List<(DateTime, long)> { (Now.AddHours(-1), 2_000_000) };
        TransferPolicyEvaluator.Check(Input(1_500_000, sender: young, sent: sent, last: Now.AddSeconds(-1), bypass: true));

        Assert.Equal(CurrencyErrorCode.InsufficientFunds, Refusal(Input(1_500_000, sender: Sender(coins: 10), bypass: true)));
        Assert.Equal(CurrencyErrorCode.AmountOutOfRange, Refusal(Input(5, bypass: true)));
    }

    [Fact]
    public void InsufficientFunds_ReportsTheSendersOwnBalance()
    {
        var ex = Assert.Throws<CurrencyException>(() => TransferPolicyEvaluator.Check(Input(1000, sender: Sender(coins: 999))));
        Assert.Equal(CurrencyErrorCode.InsufficientFunds, ex.Code);
        Assert.Contains("999", ex.Message);
    }

    [Fact]
    public void RecipientAtCap_IsRefused_WithoutRevealingTheirBalance()
    {
        var rich = Recipient(coins: 999_999_500);
        var ex = Assert.Throws<CurrencyException>(() => TransferPolicyEvaluator.Check(Input(1000, recipient: rich)));
        Assert.Equal(CurrencyErrorCode.BalanceCapExceeded, ex.Code);
        Assert.DoesNotContain("999", ex.Message);
    }

    [Theory]
    [InlineData(0, 12345, 0)]
    [InlineData(200, 12345, 246)]
    [InlineData(10_000, 500, 500)]
    public void Fee_IsBasisPointsRoundedDown(int basisPoints, long amount, long expected)
    {
        var policy = CoinPolicy();
        policy.TransferFeeBasisPoints = basisPoints;
        Assert.Equal(expected, TransferPolicyEvaluator.Fee(policy, amount));
    }

    [Fact]
    public void NextTransferAt_IsTheLaterOfCooldownAndHourlyWindow()
    {
        var policy = CoinPolicy();
        Assert.Null(TransferPolicyEvaluator.NextTransferAt(policy, null, Array.Empty<(DateTime, long)>(), Now));
        Assert.Equal(Now.AddSeconds(2), TransferPolicyEvaluator.NextTransferAt(policy, Now.AddSeconds(-3), Array.Empty<(DateTime, long)>(), Now));

        var sent = Enumerable.Range(0, 20).Select(i => (Now.AddMinutes(-50 + i), 10L)).ToList();
        Assert.Equal(Now.AddMinutes(10), TransferPolicyEvaluator.NextTransferAt(policy, Now.AddMinutes(-31), sent, Now));
    }
}
