using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Services.Interfaces
{
    // Siege shop L4 (knk-workspace docs/specs/siege-shop/DESIGN.md § 5.6, § 6.2.3-6.2.4; PandiO/knk-workspace#869).
    public interface ISiegeWeaponUnlockPurchaseService
    {
        /// <summary>Every template with the player's unlock state. KeyNotFoundException for an unknown user.</summary>
        Task<SiegeWeaponUnlocksOverviewDto> GetOverviewAsync(int userId);

        /// <summary>
        /// Checks, then charges, then unlocks (W67), in one transaction under the player's row lock. Idempotent per
        /// player and template: a second call returns the unlock with <c>alreadyUnlocked</c> and charges nothing.
        /// KeyNotFoundException for an unknown user or template; <see cref="SiegeWeaponUnlockRefusedException"/> when
        /// the title level or the coins are short (nothing charged).
        /// </summary>
        Task<SiegeWeaponUnlockPurchaseResultDto> PurchaseAsync(int userId, int templateId, CurrencyContext context);
    }

    /// <summary>A refused unlock purchase (409): <see cref="Code"/> is <c>NeedsTitle</c> or <c>InsufficientFunds</c>.</summary>
    public sealed class SiegeWeaponUnlockRefusedException : InvalidOperationException
    {
        public SiegeWeaponUnlockRefusedException(string code, string message) : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }
}
