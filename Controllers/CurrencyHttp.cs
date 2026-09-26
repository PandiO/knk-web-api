using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
        /// A refused posting as a response: 404 for an unknown user, 409 when the request conflicts
        /// with the current state (stale expectedCurrent, reused key, already reversed), 400 for
        /// everything the caller must change (amount, funds, cap, malformed request).
        /// </summary>
        public static IActionResult ToResult(ControllerBase controller, CurrencyException ex)
        {
            var body = new { error = ex.Code.ToString(), code = ex.Code.ToString(), message = ex.Message, details = ex.Details };
            return ex.Code switch
            {
                CurrencyErrorCode.UserNotFound or CurrencyErrorCode.TransactionNotFound => controller.NotFound(body),
                CurrencyErrorCode.ExpectedBalanceMismatch or CurrencyErrorCode.IdempotencyKeyReuse
                    or CurrencyErrorCode.AlreadyReversed => controller.Conflict(body),
                _ => controller.BadRequest(body)
            };
        }
    }
}
