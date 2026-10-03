namespace knkwebapi_v2.Services.Statistics
{
    /// <summary>How a ledger leg counts for the economy statistics (DESIGN.md §F.5).</summary>
    public enum LedgerBucket
    {
        /// <summary>A gain: counts toward *_earned (and xp_gained for XP legs).</summary>
        Earned,

        /// <summary>A loss: counts toward *_spent (XP losses are not a statistic).</summary>
        Spent,

        /// <summary>Changes nothing (a zero leg, or a reversal whose origin is unknown).</summary>
        Excluded
    }

    /// <summary>
    /// Classifies ledger legs for the economy and XP statistics (KNG-34, DESIGN.md §F.5). Developer
    /// decision 2026-10-03 (D16): earned/spent include <b>every</b> way of gaining or losing a
    /// balance — gameplay, transfers, staff adjustments, signup grant, merges and premium top-ups — so
    /// a leg counts by its sign, whatever its reason code.
    /// <para>A reversal is a correction, not a new gain or loss: it stays in the bucket of the
    /// original transaction it ultimately undoes (a reversed grant lowers earned, a reversed spend
    /// lowers spent, a reversal of that reversal raises it again). The legs of one user alternate
    /// sign along a reversal chain, so the original's sign follows from the reversal's sign and its
    /// depth (1 = reverses an original, 2 = reverses a reversal, …).</para>
    /// </summary>
    public static class LedgerStatisticsClassifier
    {
        /// <param name="reversalDepth">For a REVERSAL leg: hops to the original transaction (≥ 1);
        /// null when the chain could not be resolved (the leg is then excluded).</param>
        public static LedgerBucket Classify(string? reasonCode, long amount, int? reversalDepth = 1)
        {
            if (amount == 0) return LedgerBucket.Excluded;
            if (reasonCode != CurrencyReasons.Reversal)
            {
                return amount > 0 ? LedgerBucket.Earned : LedgerBucket.Spent;
            }
            if (reversalDepth is not > 0) return LedgerBucket.Excluded;
            var originalWasGain = reversalDepth.Value % 2 == 1 ? amount < 0 : amount > 0;
            return originalWasGain ? LedgerBucket.Earned : LedgerBucket.Spent;
        }
    }
}
