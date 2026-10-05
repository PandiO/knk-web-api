using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>
    /// Domain warps and paid teleports (docs/specs/teleport/DESIGN.md §3.5/§3.7, KNG-17 Phase 5).
    /// Everything that matters is decided here, not by the plugin: which domains are destinations,
    /// who may use them (title, premium tier, discovery) and what they cost. Charges go through the
    /// currency ledger (ICurrencyService, reason TELEPORT_FEE) with the plugin's idempotency key.
    /// Refusals throw <see cref="TeleportDestinationException"/>.
    /// </summary>
    public interface ITeleportDestinationService
    {
        /// <summary>
        /// Every destination (TeleportEnabled, has a Location, AllowEntry) with its lock state for
        /// this player, Towns first, then Districts, Structures, GateStructures, each by name.
        /// KeyNotFoundException for an unknown user.
        /// </summary>
        Task<List<TeleportDestinationDto>> ListForUserAsync(int userId);

        /// <summary>
        /// Re-evaluates the destination for the player and charges its gem price once per
        /// idempotency key. A retry with the same key returns the first charge (Replayed); a key
        /// that was refunded (or voided by a refund that came first) is refused with Refunded.
        /// </summary>
        Task<TeleportChargeResultDto> ChargeAsync(int domainId, TeleportChargeRequestDto request);

        /// <summary>Charges a /tpa or /tpahere coin fee once per idempotency key.</summary>
        Task<TeleportChargeResultDto> ChargeRequestFeeAsync(TeleportRequestFeeDto request);

        /// <summary>Charges the flat coin fee of a player's own /back once per idempotency key (KNG-42).</summary>
        Task<TeleportChargeResultDto> ChargeBackFeeAsync(TeleportBackFeeDto request);

        /// <summary>The fee of a /spawn as the player's permission groups price it (KNG-41); free when none does.</summary>
        Task<TeleportChargeResultDto> ChargeSpawnFeeAsync(TeleportSpawnFeeDto request);

        /// <summary>The player's teleport fees and cooldowns from their permission groups (KNG-41).</summary>
        Task<TeleportPolicyDto> GetPolicyAsync(int userId);

        /// <summary>
        /// Gives back what the TELEPORT_FEE charge made under this key took (a ledger reversal).
        /// Repeating it is harmless. When nothing was charged under the key, the key is voided for
        /// a while so a charge that arrives late with it is refused.
        /// </summary>
        Task<TeleportRefundResultDto> RefundAsync(TeleportRefundRequestDto request);

        /// <summary>
        /// Checks the warp settings a Town/District/Structure form sends (price range, title bracket
        /// exists, premium group exists and is a premium tier). ArgumentException when invalid;
        /// nothing to check when TeleportEnabled is null (the form doesn't carry the fields).
        /// </summary>
        Task ValidateSettingsAsync(IDomainTeleportSettingsDto settings);
    }
}
