using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Everything the transfer rules look at, read under the sender's and recipient's row locks
    /// (so two concurrent sends from one player can't both pass a cap).
    /// </summary>
    public sealed record TransferPolicyInput
    {
        public required CurrencyPolicy? Policy { get; init; }
        public required Currency Currency { get; init; }
        public required long Amount { get; init; }
        public required long Fee { get; init; }
        public required User Sender { get; init; }
        public required User Recipient { get; init; }
        public required DateTime Now { get; init; }

        /// <summary>The sender's transfers of this currency in the last 24 h, oldest first (amount received).</summary>
        public IReadOnlyList<(DateTime CreatedAt, long Amount)> SentLast24h { get; init; } = Array.Empty<(DateTime, long)>();

        /// <summary>What the recipient received from transfers of this currency in the last 24 h.</summary>
        public long RecipientReceivedLast24h { get; init; }

        /// <summary>The sender's last transfer of any currency.</summary>
        public DateTime? SenderLastTransferAt { get; init; }

        /// <summary>The title bracket the policy requires of senders (null: no title gate, or the bracket is gone).</summary>
        public TitleBracket? RequiredTitle { get; init; }

        /// <summary>knk.pay.bypass: skips the per-transfer maximum, daily caps, cooldown, hourly
        /// limit and the age/title gate. Never the kill switch, transferability, locks, the
        /// minimum, the balance or the recipient's balance cap.</summary>
        public bool BypassLimits { get; init; }
    }

    /// <summary>
    /// The player-transfer rules (currency DESIGN.md §3.5, developer decisions §5: gems never
    /// transferable, 0% fee, senders ≥ 48 h old holding at least the Peasant title). Pure: it
    /// only reads <see cref="TransferPolicyInput"/>, so every rule is unit-tested without a
    /// database. Throws the first rule a transfer breaks as a <see cref="CurrencyException"/>;
    /// messages are written for the sender and never reveal the recipient's balance.
    /// </summary>
    public static class TransferPolicyEvaluator
    {
        /// <summary>Longest player memo on a transfer.</summary>
        public const int MaxNoteLength = 200;

        /// <summary>The fee (to SYS_FEES) on top of <paramref name="amount"/>, rounded down.</summary>
        public static long Fee(CurrencyPolicy? policy, long amount)
        {
            var basisPoints = policy?.TransferFeeBasisPoints ?? 0;
            if (basisPoints <= 0 || amount <= 0)
            {
                return 0;
            }
            return checked(amount * Math.Min(basisPoints, 10_000) / 10_000);
        }

        /// <summary>The balance cap a transfer may fill a recipient up to: the policy's MaxBalance may only lower the hard cap.</summary>
        public static long RecipientCap(CurrencyPolicy? policy, Currency currency)
        {
            var hard = CurrencyService.Cap(currency);
            return policy is { MaxBalance: > 0 } ? Math.Min(policy.MaxBalance, hard) : hard;
        }

        /// <summary>
        /// Rules that don't depend on the users' state: currency, kill switch, transferability,
        /// amount bounds. Checked before any lock is taken, and again under it.
        /// </summary>
        public static void CheckStatic(CurrencyPolicy? policy, Currency currency, long amount, bool bypassLimits)
        {
            if (currency == Currency.Experience || !Enum.IsDefined(currency))
            {
                throw new CurrencyException(CurrencyErrorCode.NotTransferable, "Experience can't be transferred.");
            }
            if (policy == null || !policy.TransfersEnabled)
            {
                throw new CurrencyException(CurrencyErrorCode.TransfersDisabled,
                    $"{Title(currency)} transfers are switched off right now.", new { currency = currency.ToString() });
            }
            if (!policy.Transferable)
            {
                throw new CurrencyException(CurrencyErrorCode.NotTransferable,
                    $"{Title(currency)}s can't be transferred.", new { currency = currency.ToString() });
            }
            var min = Math.Max(1, policy.MinTransfer);
            var max = policy.MaxTransfer > 0 ? Math.Min(policy.MaxTransfer, CurrencyService.Cap(currency)) : CurrencyService.Cap(currency);
            if (amount < min || (!bypassLimits && amount > max) || amount > CurrencyService.Cap(currency))
            {
                throw new CurrencyException(CurrencyErrorCode.AmountOutOfRange,
                    $"You can send between {min:N0} and {max:N0} {Name(currency)} at a time.",
                    new { currency = currency.ToString(), min, max });
            }
        }

        /// <summary>Every rule, in the order a player should hear about them.</summary>
        public static void Check(TransferPolicyInput input)
        {
            var policy = input.Policy;
            CheckStatic(policy, input.Currency, input.Amount, input.BypassLimits);
            var currency = input.Currency;
            var sender = input.Sender;
            var recipient = input.Recipient;

            if (sender.Id == recipient.Id)
            {
                throw new CurrencyException(CurrencyErrorCode.SelfTransfer, "You can't pay yourself.");
            }
            if (!recipient.IsActive || recipient.DeletedAt != null)
            {
                throw new CurrencyException(CurrencyErrorCode.RecipientNotFound, "That player doesn't exist.");
            }
            if (!sender.IsActive || sender.DeletedAt != null || sender.IsFrozen || sender.TransferLockReason != null)
            {
                throw new CurrencyException(CurrencyErrorCode.AccountLocked,
                    "Your account can't send payments right now.", new { side = "sender" });
            }
            if (recipient.IsFrozen || recipient.TransferLockReason != null)
            {
                throw new CurrencyException(CurrencyErrorCode.AccountLocked,
                    $"{recipient.Username} can't receive payments right now.", new { side = "recipient" });
            }

            if (!input.BypassLimits)
            {
                CheckEligibility(policy!, sender, input.RequiredTitle, input.Now, currency);
                CheckPace(policy!, input, currency);
            }

            // Funds, then the recipient's cap (never revealing the recipient's balance).
            var total = checked(input.Amount + input.Fee);
            var balance = Balance(sender, currency);
            if (balance < total)
            {
                throw new CurrencyException(CurrencyErrorCode.InsufficientFunds,
                    $"You only have {balance:N0} {Name(currency)}.",
                    new { currency = currency.ToString(), balance, required = total });
            }
            if (Balance(recipient, currency) + input.Amount > RecipientCap(policy, currency))
            {
                throw new CurrencyException(CurrencyErrorCode.BalanceCapExceeded,
                    $"{recipient.Username} can't hold that many more {Name(currency)}.", new { side = "recipient" });
            }
        }

        /// <summary>Account age and title gate (DESIGN.md §5 Q5); receiving is never restricted.</summary>
        public static void CheckEligibility(CurrencyPolicy policy, User sender, TitleBracket? requiredTitle, DateTime now, Currency currency)
        {
            var eligibleFrom = EligibleFrom(policy, sender);
            var titleShort = requiredTitle != null && sender.ExperiencePoints < requiredTitle.MinExperience;
            if ((eligibleFrom != null && eligibleFrom > now) || titleShort)
            {
                throw new CurrencyException(CurrencyErrorCode.NewAccountRestricted,
                    $"Your account must be {policy.MinSenderAccountAgeHours}h old"
                    + (requiredTitle != null ? $" and reach the {requiredTitle.MaleName} title" : "")
                    + $" before sending {Name(currency)}.",
                    new
                    {
                        hours = policy.MinSenderAccountAgeHours,
                        eligibleFrom = eligibleFrom > now ? eligibleFrom : null,
                        title = requiredTitle?.MaleName,
                        requiredExperience = requiredTitle?.MinExperience
                    });
            }
        }

        private static void CheckPace(CurrencyPolicy policy, TransferPolicyInput input, Currency currency)
        {
            var next = NextTransferAt(policy, input.SenderLastTransferAt, input.SentLast24h, input.Now);
            if (next != null)
            {
                var seconds = (int)Math.Ceiling((next.Value - input.Now).TotalSeconds);
                throw new CurrencyException(CurrencyErrorCode.CooldownActive,
                    $"Slow down - you can send again in {Math.Max(1, seconds)}s.",
                    new { retryAt = next.Value, seconds = Math.Max(1, seconds) });
            }

            var sent = input.SentLast24h.Sum(t => t.Amount);
            if (policy.DailySendCap > 0 && sent + input.Amount > policy.DailySendCap)
            {
                var remaining = Math.Max(0, policy.DailySendCap - sent);
                throw new CurrencyException(CurrencyErrorCode.DailyCapExceeded,
                    $"Daily limit reached - you can send {remaining:N0} more {Name(currency)} in the next 24h.",
                    new { currency = currency.ToString(), cap = policy.DailySendCap, sent, remaining, resetsAt = ResetsAt(input.SentLast24h, policy.DailySendCap, input.Amount) });
            }
            if (policy.DailyReceiveCap > 0 && input.RecipientReceivedLast24h + input.Amount > policy.DailyReceiveCap)
            {
                throw new CurrencyException(CurrencyErrorCode.RecipientDailyCapExceeded,
                    $"{input.Recipient.Username} can't receive that much more {Name(currency)} today.",
                    new { currency = currency.ToString() });
            }
        }

        /// <summary>When the cooldown and the hourly limit next allow a transfer; null = now.</summary>
        public static DateTime? NextTransferAt(CurrencyPolicy policy, DateTime? lastTransferAt,
            IReadOnlyList<(DateTime CreatedAt, long Amount)> sentLast24h, DateTime now)
        {
            DateTime? next = null;
            if (policy.CooldownSeconds > 0 && lastTransferAt != null)
            {
                var cooldownEnds = AsUtc(lastTransferAt.Value).AddSeconds(policy.CooldownSeconds);
                if (cooldownEnds > now) next = cooldownEnds;
            }
            if (policy.MaxTransfersPerHour > 0)
            {
                var lastHour = sentLast24h.Where(t => t.CreatedAt > now.AddHours(-1)).OrderBy(t => t.CreatedAt).ToList();
                if (lastHour.Count >= policy.MaxTransfersPerHour)
                {
                    // The oldest one that has to leave the window before another fits.
                    var frees = AsUtc(lastHour[lastHour.Count - policy.MaxTransfersPerHour].CreatedAt).AddHours(1);
                    if (next == null || frees > next) next = frees;
                }
            }
            return next;
        }

        /// <summary>When the account-age rule stops applying; null when there is none.</summary>
        public static DateTime? EligibleFrom(CurrencyPolicy policy, User sender) =>
            policy.MinSenderAccountAgeHours > 0 ? AsUtc(sender.CreatedAt).AddHours(policy.MinSenderAccountAgeHours) : null;

        /// <summary>When enough of the window's transfers have aged out for <paramref name="amount"/> to fit.</summary>
        private static DateTime? ResetsAt(IReadOnlyList<(DateTime CreatedAt, long Amount)> window, long cap, long amount)
        {
            var total = window.Sum(t => t.Amount);
            foreach (var (createdAt, sent) in window.OrderBy(t => t.CreatedAt))
            {
                total -= sent;
                if (total + amount <= cap)
                {
                    return AsUtc(createdAt).AddHours(24);
                }
            }
            return null; // bigger than the cap itself
        }

        /// <summary>Times are stored in UTC; MySQL hands them back unspecified. Mark them so the
        /// JSON carries a "Z" and clients don't read them as local time.</summary>
        private static DateTime AsUtc(DateTime value) =>
            value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

        private static long Balance(User user, Currency currency) => currency == Currency.Gems ? user.Gems : user.Coins;

        public static string Name(Currency currency) => currency == Currency.Gems ? "gems" : "coins";

        private static string Title(Currency currency) => currency == Currency.Gems ? "Gem" : "Coin";
    }
}
