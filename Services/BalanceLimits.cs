namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Bounds every coin/gem/XP write stays within (docs/specs/currency-payments/DESIGN.md §3.1
    /// invariant 5, audit A8/A9). The same bounds are CHECK constraints on the users table
    /// (KnKDbContext), so a path that skips this still can't store an out-of-range balance.
    /// Arithmetic is done in checked long math, so an overflow can't wrap round to a small or
    /// negative number.
    /// </summary>
    public static class BalanceLimits
    {
        public const int MaxCoins = 999_999_999;
        public const int MaxGems = 999_999;
        public const int MaxExperience = int.MaxValue;

        /// <summary><paramref name="current"/> + <paramref name="delta"/> coins, or an
        /// InvalidOperationException ("Insufficient coins…" below zero, BalanceCapExceeded above the cap).</summary>
        public static int ApplyCoins(int current, long delta) => Apply("coins", current, delta, MaxCoins);

        public static int ApplyGems(int current, long delta) => Apply("gems", current, delta, MaxGems);

        public static int ApplyExperience(int current, long delta) => Apply("experience points", current, delta, MaxExperience);

        private static int Apply(string what, int current, long delta, int max)
        {
            long result;
            try
            {
                result = checked(current + delta);
            }
            catch (OverflowException)
            {
                throw new BalanceCapExceededException(what, current, delta, max);
            }

            if (result < 0)
            {
                throw new InvalidOperationException($"Insufficient {what}. Current: {current}, Attempted change: {delta}");
            }
            if (result > max)
            {
                throw new BalanceCapExceededException(what, current, delta, max);
            }
            return (int)result;
        }

        /// <summary>
        /// A reward computed from multipliers (salary, title bonus) as a whole amount: rounded half
        /// away from zero, never negative (a multiplier made negative by a direct DB edit pays
        /// nothing), and a BalanceCapExceeded error rather than an OverflowException when absurd
        /// multipliers push it past what a balance could ever hold.
        /// </summary>
        public static long ToWholeAmount(Func<decimal> compute, string what)
        {
            decimal value;
            try
            {
                value = Math.Round(compute(), MidpointRounding.AwayFromZero);
            }
            catch (OverflowException)
            {
                throw new BalanceCapExceededException(what);
            }

            if (value <= 0)
            {
                return 0;
            }
            if (value > int.MaxValue)
            {
                throw new BalanceCapExceededException(what);
            }
            return (long)value;
        }
    }

    /// <summary>
    /// A write would push a balance above its cap (BalanceLimits). An InvalidOperationException
    /// so existing "operation not possible" handling still applies; the message starts with
    /// <see cref="Code"/> so API callers can tell it apart.
    /// </summary>
    public class BalanceCapExceededException : InvalidOperationException
    {
        public const string Code = "BalanceCapExceeded";

        public BalanceCapExceededException(string what, long current, long delta, long max)
            : base($"{Code}: {what} can't go above {max:N0} (current {current:N0}, change {delta:+#,0;-#,0;0}).")
        {
        }

        public BalanceCapExceededException(string what)
            : base($"{Code}: the {what} is larger than any balance can hold.")
        {
        }
    }
}
