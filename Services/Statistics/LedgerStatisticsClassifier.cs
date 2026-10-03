using System;
using System.Collections.Generic;

namespace knkwebapi_v2.Services.Statistics
{
    /// <summary>How a ledger reason counts for the economy statistics (DESIGN.md §F.5).</summary>
    public enum LedgerBucket
    {
        /// <summary>Gameplay income: counts toward *_earned (and xp_gained for XP legs).</summary>
        Earned,

        /// <summary>Gameplay spending: counts toward *_spent.</summary>
        Spent,

        /// <summary>Neither (transfers, staff corrections, signup, merges, purchases).</summary>
        Excluded,

        /// <summary>Negates the bucket of the reversed transaction, on the reversal's date.</summary>
        Reversal
    }

    /// <summary>
    /// Classifies currency reason codes for the economy and XP statistics (KNG-34 L1-5/L1-6, DESIGN.md
    /// §F.5). Every code in <see cref="CurrencyReasons"/> is listed explicitly; a code missing here
    /// falls into <see cref="LedgerBucket.Excluded"/> (safe default) and fails
    /// LedgerStatisticsClassifierTests, so a new reason code needs a decision (the D13 guard).
    /// </summary>
    public static class LedgerStatisticsClassifier
    {
        private static readonly Dictionary<string, LedgerBucket> Buckets = new(StringComparer.Ordinal)
        {
            [CurrencyReasons.Salary] = LedgerBucket.Earned,
            [CurrencyReasons.SiegeReward] = LedgerBucket.Earned,
            [CurrencyReasons.TitleBonus] = LedgerBucket.Earned,
            [CurrencyReasons.DiscoveryReward] = LedgerBucket.Earned,
            [CurrencyReasons.LootboxReward] = LedgerBucket.Earned,
            [CurrencyReasons.EventReward] = LedgerBucket.Earned,

            [CurrencyReasons.KitClaimCost] = LedgerBucket.Spent,
            [CurrencyReasons.KitPurchase] = LedgerBucket.Spent,
            [CurrencyReasons.LootboxPurchase] = LedgerBucket.Spent,
            [CurrencyReasons.TeleportFee] = LedgerBucket.Spent,
            [CurrencyReasons.TransferFee] = LedgerBucket.Spent,

            [CurrencyReasons.SignupGrant] = LedgerBucket.Excluded,
            [CurrencyReasons.PlayerTransfer] = LedgerBucket.Excluded,
            [CurrencyReasons.AdminGrant] = LedgerBucket.Excluded,
            [CurrencyReasons.AdminTake] = LedgerBucket.Excluded,
            [CurrencyReasons.AdminSet] = LedgerBucket.Excluded,
            [CurrencyReasons.MergeForfeit] = LedgerBucket.Excluded,
            [CurrencyReasons.MergeCarryover] = LedgerBucket.Excluded,
            [CurrencyReasons.PremiumTopup] = LedgerBucket.Excluded,

            [CurrencyReasons.Reversal] = LedgerBucket.Reversal,
        };

        public static LedgerBucket Classify(string? reasonCode) =>
            reasonCode != null && Buckets.TryGetValue(reasonCode, out var bucket) ? bucket : LedgerBucket.Excluded;

        /// <summary>Whether the code has an explicit decision (the guard test's check).</summary>
        public static bool IsClassified(string reasonCode) => Buckets.ContainsKey(reasonCode);
    }
}
