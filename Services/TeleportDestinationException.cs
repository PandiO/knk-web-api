using System;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// A refused warp or teleport charge (docs/specs/teleport/DESIGN.md §3.7.2/§3.7.3). The
    /// controller answers 409 with <c>{ error = Code, message = Message }</c>; the plugin shows
    /// the message to the player.
    /// </summary>
    public class TeleportDestinationException : InvalidOperationException
    {
        /// <summary>Disabled, no location, entry closed, or not a domain at all.</summary>
        public const string NotAvailable = "NotAvailable";
        public const string TitleTooLow = "TitleTooLow";
        public const string PremiumTooLow = "PremiumTooLow";
        public const string NotDiscovered = "NotDiscovered";
        public const string InsufficientGems = "InsufficientGems";
        public const string InsufficientCoins = "InsufficientCoins";
        /// <summary>Not enough XP for a group's XP price (KNG-41).</summary>
        public const string InsufficientExperience = "InsufficientExperience";

        /// <summary>The key's charge was refunded, or the key was voided by a refund that came first.</summary>
        public const string Refunded = "Refunded";

        /// <summary>The key was already used for another charge (other player, domain or amount).</summary>
        public const string IdempotencyKeyReuse = "IdempotencyKeyReuse";

        /// <summary>The charge can't be made for another ledger reason (e.g. a balance cap on refund).</summary>
        public const string LedgerRefused = "LedgerRefused";

        public TeleportDestinationException(string code, string message) : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }
}
