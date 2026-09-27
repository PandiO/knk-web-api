using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// HTTP plumbing shared by the routes that post to the currency ledger (currency-payments
    /// DESIGN.md §3.4): the Idempotency-Key header and the mapping of CurrencyException codes to
    /// status codes.
    /// </summary>
    internal static class CurrencyHttp
    {
        public const string IdempotencyKeyHeader = "Idempotency-Key";

        public const int MaxClientKeyLength = CurrencyClientKeys.MaxLength;

        private static readonly Regex ClientKeyPattern = new($"^[A-Za-z0-9:_.\\-]{{1,{MaxClientKeyLength}}}$", RegexOptions.Compiled);

        /// <summary>
        /// The request's Idempotency-Key. Null when absent; <paramref name="error"/> is set when it
        /// is present but malformed, or absent and <paramref name="required"/>.
        /// </summary>
        public static string? ReadKey(ControllerBase controller, bool required, out IActionResult? error)
        {
            error = null;
            var value = controller.Request?.Headers[IdempotencyKeyHeader].ToString();
            if (string.IsNullOrEmpty(value))
            {
                if (required)
                {
                    error = controller.BadRequest(new { error = "IdempotencyKeyRequired", message = $"The {IdempotencyKeyHeader} header is required (a new random value per action, the same value on a retry)." });
                }
                return null;
            }
            if (!ClientKeyPattern.IsMatch(value))
            {
                error = controller.BadRequest(new { error = "InvalidIdempotencyKey", message = $"{IdempotencyKeyHeader} must be 1–{MaxClientKeyLength} characters of A–Z, a–z, 0–9, ':', '_', '.', '-'." });
                return null;
            }
            return value;
        }

        /// <summary>
        /// The in-game nodes a logged-in staff member needs for one balance change (the /knk user
        /// nodes; the plugin checks them itself before calling): the changed currency's node, and
        /// for an XP increase (Add, or Set above the current value) the coins and gems nodes too,
        /// since the title bonuses it can trigger pay out coins and gems (KNG-21). An XP decrease
        /// only needs the XP node.
        /// </summary>
        public static IEnumerable<string> BalanceNodes(Currency currency, bool increasesExperience)
        {
            switch (currency)
            {
                case Currency.Coins:
                    yield return StaffPermissions.UserCoins;
                    break;
                case Currency.Gems:
                    yield return StaffPermissions.UserGems;
                    break;
                default:
                    yield return StaffPermissions.UserXp;
                    if (increasesExperience)
                    {
                        yield return StaffPermissions.UserCoins;
                        yield return StaffPermissions.UserGems;
                    }
                    break;
            }
        }

        /// <summary>
        /// Whether an XP change raises the balance: always for Add, never for Remove, and for Set
        /// when the target is above <paramref name="currentExperience"/> (read when needed).
        /// </summary>
        public static async Task<bool> IncreasesExperienceAsync(Currency currency, CurrencyOperation mode, long amount,
            Func<Task<long?>> currentExperience)
        {
            if (currency != Currency.Experience || mode == CurrencyOperation.Remove)
            {
                return false;
            }
            if (mode == CurrencyOperation.Add)
            {
                return true;
            }
            var current = await currentExperience();
            // An unknown player is reported as such by the posting itself.
            return current == null || amount > current.Value;
        }

        /// <summary>
        /// A refused posting as a response: 404 for an unknown user, 409 when the request conflicts
        /// with the current state (stale expectedCurrent or policy version, reused key, already reversed, a pending
        /// transfer that is closed or expired), 422 for a player transfer the policy refuses
        /// or a staff grant over the daily cap (currency DESIGN.md §3.4/§3.5), 400 for everything the caller must change (amount,
        /// funds, cap, malformed request).
        /// </summary>
        public static IActionResult ToResult(ControllerBase controller, CurrencyException ex)
        {
            var body = new { error = ex.Code.ToString(), code = ex.Code.ToString(), message = ex.Message, details = ex.Details };
            return ex.Code switch
            {
                CurrencyErrorCode.UserNotFound or CurrencyErrorCode.TransactionNotFound
                    or CurrencyErrorCode.RecipientNotFound or CurrencyErrorCode.PendingTransferNotFound => controller.NotFound(body),
                CurrencyErrorCode.ExpectedBalanceMismatch or CurrencyErrorCode.IdempotencyKeyReuse
                    or CurrencyErrorCode.AlreadyReversed or CurrencyErrorCode.PendingTransferExpired
                    or CurrencyErrorCode.PendingTransferClosed or CurrencyErrorCode.PolicyChanged => controller.Conflict(body),
                CurrencyErrorCode.NotTransferable or CurrencyErrorCode.SelfTransfer or CurrencyErrorCode.AccountLocked
                    or CurrencyErrorCode.CooldownActive or CurrencyErrorCode.DailyCapExceeded
                    or CurrencyErrorCode.RecipientDailyCapExceeded or CurrencyErrorCode.NewAccountRestricted
                    or CurrencyErrorCode.TransfersDisabled or CurrencyErrorCode.AdminDailyCapExceeded => controller.UnprocessableEntity(body),
                _ => controller.BadRequest(body)
            };
        }
    }
}
