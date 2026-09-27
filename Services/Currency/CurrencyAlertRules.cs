using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// The anomaly rules of the currency monitor (docs/specs/currency-payments/DESIGN.md §3.9).
    /// The ids are stored in currency_alerts.Rule; never rename one.
    /// </summary>
    public static class CurrencyAlertRules
    {
        /// <summary>Balance columns disagree with the ledger (reconciler). Critical; switches transfers off.</summary>
        public const string Reconciliation = "R1";

        /// <summary>A transaction whose legs don't sum to zero (reconciler). Critical.</summary>
        public const string UnbalancedTransaction = "R2";

        /// <summary>Transfers from many brand-new accounts into one player. High.</summary>
        public const string Funnel = "R3";

        /// <summary>Money going back and forth between two players. Medium.</summary>
        public const string PingPong = "R4";

        /// <summary>An unusually large net coin inflow within an hour. High.</summary>
        public const string Velocity = "R5";

        /// <summary>A large staff adjustment, or many adjustments by one staff member. High.</summary>
        public const string Admin = "R6";

        /// <summary>A reason code minting far more than usual. Medium.</summary>
        public const string MintRate = "R7";

        /// <summary>A posting refused because a balance would pass its cap. Medium.</summary>
        public const string CapHit = "R8";

        /// <summary>A player whose transfers keep being refused. Low.</summary>
        public const string Probing = "R9";

        /// <summary>Short names for staff screens.</summary>
        public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
        {
            [Reconciliation] = "Reconciliation mismatch",
            [UnbalancedTransaction] = "Unbalanced transaction",
            [Funnel] = "Funnel",
            [PingPong] = "Ping-pong",
            [Velocity] = "Velocity",
            [Admin] = "Staff adjustments",
            [MintRate] = "Mint rate",
            [CapHit] = "Balance cap hit",
            [Probing] = "Probing"
        };

        public static string NameOf(string rule) => Names.TryGetValue(rule, out var name) ? name : rule;

        /// <summary>Long enough to mean "once, ever" for a finding about one transaction.</summary>
        public static readonly TimeSpan Forever = TimeSpan.FromDays(3650);
    }

    /// <summary>
    /// A finding before it is stored. <see cref="DedupKey"/> + <see cref="SuppressFor"/>: a finding
    /// with the same rule and key as an alert raised within SuppressFor is dropped, so a lasting
    /// condition raises one alert per window instead of one per monitor cycle.
    /// </summary>
    public sealed record CurrencyAlertDraft(
        string Rule,
        CurrencyAlertSeverity Severity,
        string Summary,
        string DedupKey,
        TimeSpan SuppressFor)
    {
        public int? UserId { get; init; }

        public long? TransactionId { get; init; }

        /// <summary>Serialized into currency_alerts.DetailsJson.</summary>
        public object? Details { get; init; }

        /// <summary>R1: currencies whose player transfers are switched off (DESIGN.md §4 D8).</summary>
        public IReadOnlyList<Currency> DisableTransfersFor { get; init; } = Array.Empty<Currency>();
    }
}
