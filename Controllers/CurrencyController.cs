using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// Player-facing currency routes (currency-payments DESIGN.md §3.4, IMPLEMENTATION_PLAN.md
    /// Phase 3): balances, the leaderboard, transfer limits, player transfers with their
    /// confirmation step, and a player's own ledger history (KNG-23 folded in). Staff routes
    /// (adjustments, reversals, locks, policy, the all-players event log) are Phase 4.
    /// <para>
    /// Transfers are in-game only (DESIGN.md §5 Q4): only the game server may call them, and it
    /// must name the sending player in X-Acting-User-Id, so a request can't move someone else's
    /// money. The plugin checks the knk.pay / knk.balance / knk.baltop / knk.transactions nodes
    /// in-game before calling; the key alone passes here (same convention as every
    /// RequireServiceOrPermission route). Web users may read their own balance, limits and
    /// history; reading someone else's needs knk.admin.currency.history.
    /// </para>
    /// </summary>
    [ApiController]
    [Route("api/currency")]
    public class CurrencyController : ControllerBase
    {
        /// <summary>Player nodes, granted to the Default group (migration SeedCurrencyPlayerNodes).</summary>
        public const string PayNode = "knk.pay";
        public const string BalanceNode = "knk.balance";
        public const string BalanceOthersNode = "knk.balance.others";
        public const string BaltopNode = "knk.baltop";
        public const string TransactionsNode = "knk.transactions";

        /// <summary>Staff: read any player's balance, limits and ledger history.</summary>
        public const string CurrencyHistoryNode = "knk.admin.currency.history";

        private static readonly TimeSpan LeaderboardCacheTime = TimeSpan.FromSeconds(60);

        private readonly ICurrencyService _currency;
        private readonly ICurrencyTransferService _transfers;
        private readonly IPermissionResolutionService _permissions;
        private readonly IPlayerNotificationQueue? _notifications;
        private readonly IMemoryCache? _cache;
        private readonly ILogger<CurrencyController>? _logger;

        public CurrencyController(
            ICurrencyService currency,
            ICurrencyTransferService transfers,
            IPermissionResolutionService permissions,
            IPlayerNotificationQueue? notifications = null,
            IMemoryCache? cache = null,
            ILogger<CurrencyController>? logger = null)
        {
            _currency = currency;
            _transfers = transfers;
            _permissions = permissions;
            _notifications = notifications;
            _cache = cache;
            _logger = logger;
        }

        /// <summary>A player's current coins, gems and XP (never a cached value).</summary>
        /// <response code="200">Balances</response>
        /// <response code="401">Neither the game server nor logged in</response>
        /// <response code="403">Someone else's, without knk.admin.currency.history</response>
        /// <response code="404">No such user</response>
        [HttpGet("balances/{userId:int}")]
        public async Task<IActionResult> GetBalances(int userId, CancellationToken ct)
        {
            var denied = await AuthorizeOwnOrStaffAsync(userId);
            if (denied != null) return denied;
            try
            {
                return Ok(await _currency.GetBalancesAsync(userId, ct));
            }
            catch (CurrencyException ex)
            {
                return CurrencyHttp.ToResult(this, ex);
            }
        }

        /// <summary>
        /// Richest players (/baltop): active accounts only, leaving out transfer-locked ones and
        /// holders of knk.baltop.exempt. Cached for 60 s.
        /// </summary>
        /// <param name="currency">coins (default) or gems</param>
        /// <param name="page">1-based</param>
        /// <param name="pageSize">1–50, default 10</param>
        [RequireServiceOrPermission(BaltopNode)]
        [HttpGet("leaderboard")]
        public async Task<IActionResult> GetLeaderboard([FromQuery] string? currency = null, [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10, CancellationToken ct = default)
        {
            var parsed = ParseCurrency(currency, Currency.Coins, allowExperience: false);
            if (parsed == null) return BadRequest(new { error = "InvalidCurrency", message = "currency must be coins or gems." });
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, CurrencyService.MaxLeaderboardPageSize);

            var key = $"currency:leaderboard:{parsed}:{page}:{pageSize}";
            if (_cache != null && _cache.TryGetValue(key, out LeaderboardDto? cached) && cached != null)
            {
                return Ok(cached);
            }
            try
            {
                var board = await _transfers.GetLeaderboardAsync(parsed.Value, page, pageSize, ct);
                _cache?.Set(key, board, LeaderboardCacheTime);
                return Ok(board);
            }
            catch (CurrencyException ex)
            {
                return CurrencyHttp.ToResult(this, ex);
            }
        }

        /// <summary>What the player may send now: min/max, remaining daily amount, cooldown, confirmation threshold, eligibility.</summary>
        /// <param name="userId">The (would-be) sender</param>
        /// <param name="currency">coins (default) or gems</param>
        [HttpGet("limits/{userId:int}")]
        public async Task<IActionResult> GetLimits(int userId, [FromQuery] string? currency = null, CancellationToken ct = default)
        {
            var denied = await AuthorizeOwnOrStaffAsync(userId);
            if (denied != null) return denied;
            var parsed = ParseCurrency(currency, Currency.Coins, allowExperience: false);
            if (parsed == null) return BadRequest(new { error = "InvalidCurrency", message = "currency must be coins or gems." });
            try
            {
                return Ok(await _transfers.GetTransferLimitsAsync(userId, parsed.Value, ct));
            }
            catch (CurrencyException ex)
            {
                return CurrencyHttp.ToResult(this, ex);
            }
        }

        /// <summary>
        /// Sends coins from one player to another (/pay). Game server only, with X-Acting-User-Id
        /// = senderUserId and an Idempotency-Key per /pay (the same on a retry: a retry returns
        /// the first outcome and moves nothing again). At or above the policy's confirmation
        /// threshold nothing moves yet: the response is 202 with the pending transfer to confirm.
        /// </summary>
        /// <response code="200">Completed (or replayed)</response>
        /// <response code="202">Needs confirmation: POST transfers/pending/{id}/confirm</response>
        /// <response code="400">Missing Idempotency-Key, bad amount, not enough coins, recipient at their cap</response>
        /// <response code="403">Not the game server acting for the sender</response>
        /// <response code="404">No such recipient</response>
        /// <response code="409">Idempotency-Key used for a different payment</response>
        /// <response code="422">Refused by the transfer policy (code: NotTransferable, SelfTransfer, AccountLocked,
        /// CooldownActive, DailyCapExceeded, RecipientDailyCapExceeded, NewAccountRestricted, TransfersDisabled)</response>
        [RequirePluginService]
        [HttpPost("transfers")]
        public async Task<IActionResult> CreateTransfer([FromBody] CreateTransferDto request, CancellationToken ct)
        {
            if (request == null) return BadRequest(new { error = "InvalidRequest", message = "Request body is required" });
            var denied = RequireActingSender(request.SenderUserId);
            if (denied != null) return denied;
            var key = CurrencyHttp.ReadKey(this, required: true, out var keyError);
            if (keyError != null) return keyError;

            try
            {
                var ctx = CurrencyContext.ForCaller(HttpContext.GetKnkCaller(), CurrencyReasons.PlayerTransfer, key!,
                    "PluginPayCommand", staffAction: false);
                var result = await _transfers.TransferAsync(new TransferRequest(request.SenderUserId, request.RecipientUserId,
                    request.Currency, request.Amount, string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
                    request.BypassLimits), ctx, ct);
                return TransferResponse(result);
            }
            catch (CurrencyException ex)
            {
                return CurrencyHttp.ToResult(this, ex);
            }
        }

        /// <summary>
        /// Confirms the acting player's pending transfer: every rule is checked again now. A
        /// second confirm returns the first result (replayed).
        /// </summary>
        /// <param name="publicId">The pending transfer's id from the 202</param>
        /// <param name="bypassLimits">The sender holds knk.pay.bypass in-game</param>
        /// <response code="200">Completed (or replayed)</response>
        /// <response code="404">No such pending transfer of this player</response>
        /// <response code="409">Cancelled (PendingTransferClosed) or past its window (PendingTransferExpired)</response>
        /// <response code="422">Refused by the transfer policy now</response>
        [RequirePluginService]
        [HttpPost("transfers/pending/{publicId}/confirm")]
        public async Task<IActionResult> ConfirmTransfer(string publicId, [FromQuery] bool bypassLimits = false, CancellationToken ct = default)
        {
            var caller = HttpContext.GetKnkCaller();
            var denied = RequireActingPlayer(caller) ?? RequireWellFormedPendingId(publicId);
            if (denied != null) return denied;
            try
            {
                // The posting key comes from the pending id (a second confirm replays the first),
                // so no client Idempotency-Key is needed here.
                var ctx = CurrencyContext.ForCaller(caller, CurrencyReasons.PlayerTransfer,
                    CurrencyService.ConfirmKey(publicId.ToUpperInvariant()), "PluginPayCommand", staffAction: false);
                var result = await _transfers.ConfirmTransferAsync(publicId, caller.ActingUserId!.Value, ctx, bypassLimits, ct);
                return TransferResponse(result);
            }
            catch (CurrencyException ex)
            {
                return CurrencyHttp.ToResult(this, ex);
            }
        }

        /// <summary>Cancels the acting player's pending transfer (repeatable).</summary>
        /// <response code="200">The pending transfer, now Cancelled (or already Cancelled/Expired)</response>
        /// <response code="404">No such pending transfer of this player</response>
        /// <response code="409">Already confirmed</response>
        [RequirePluginService]
        [HttpPost("transfers/pending/{publicId}/cancel")]
        public async Task<IActionResult> CancelTransfer(string publicId, CancellationToken ct)
        {
            var caller = HttpContext.GetKnkCaller();
            var denied = RequireActingPlayer(caller) ?? RequireWellFormedPendingId(publicId);
            if (denied != null) return denied;
            try
            {
                return Ok(await _transfers.CancelTransferAsync(publicId, caller.ActingUserId!.Value, ct));
            }
            catch (CurrencyException ex)
            {
                return CurrencyHttp.ToResult(this, ex);
            }
        }

        /// <summary>
        /// A player's ledger history (/transactions, /knk user &lt;player&gt; history, the web
        /// account page): newest first, one line per change of coins, gems or XP, with the
        /// balance before/after, reason, initiator and — for transfers — the other player.
        /// </summary>
        /// <param name="userId">Whose history</param>
        /// <param name="currency">coins, gems or xp; all when omitted</param>
        /// <param name="page">1-based</param>
        /// <param name="pageSize">1–100, default 10</param>
        [HttpGet("users/{userId:int}/transactions")]
        public async Task<IActionResult> GetTransactions(int userId, [FromQuery] string? currency = null, [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10, CancellationToken ct = default)
        {
            var denied = await AuthorizeOwnOrStaffAsync(userId);
            if (denied != null) return denied;
            Currency? filter = null;
            if (!string.IsNullOrWhiteSpace(currency))
            {
                filter = ParseCurrency(currency, Currency.Coins, allowExperience: true);
                if (filter == null) return BadRequest(new { error = "InvalidCurrency", message = "currency must be coins, gems or xp." });
            }
            try
            {
                await _currency.GetBalancesAsync(userId, ct); // 404 for an unknown user rather than an empty page
                var history = await _currency.GetHistoryAsync(new LedgerQuery
                {
                    UserId = userId,
                    Currency = filter,
                    Page = Math.Max(1, page),
                    PageSize = Math.Clamp(pageSize, 1, 100)
                }, ct);
                return Ok(history);
            }
            catch (CurrencyException ex)
            {
                return CurrencyHttp.ToResult(this, ex);
            }
        }

        // ===== Helpers =====

        private IActionResult TransferResponse(TransferResultDto result)
        {
            if (result.Status == TransferResultDto.StatusPendingConfirmation)
            {
                return StatusCode(StatusCodes.Status202Accepted, result);
            }
            if (!result.Replayed)
            {
                NotifyRecipient(result);
            }
            return Ok(result);
        }

        /// <summary>Queues the recipient's in-game "you received" message (shown now if online,
        /// else on their next join). Best-effort: the payment itself is already committed.</summary>
        private void NotifyRecipient(TransferResultDto result)
        {
            if (_notifications == null || result.PublicId == null)
            {
                return;
            }
            try
            {
                _notifications.EnqueuePayment(result.RecipientUserId, result.RecipientUuid, result.RecipientUsername ?? "", new PaymentNotificationDto
                {
                    Amount = result.Amount,
                    Currency = result.Currency,
                    FromUserId = result.SenderUserId,
                    FromUsername = result.SenderUsername,
                    TransactionPublicId = result.PublicId,
                    BalanceAfter = result.RecipientBalanceAfter
                });
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not queue the PaymentReceived notification for transfer {PublicId}", result.PublicId);
            }
        }

        /// <summary>The game server acting for <paramref name="senderUserId"/>; anything else can't send.</summary>
        private IActionResult? RequireActingSender(int senderUserId)
        {
            var caller = HttpContext.GetKnkCaller();
            var denied = RequireActingPlayer(caller);
            if (denied != null) return denied;
            return caller.ActingUserId == senderUserId
                ? null
                : StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden", message = "X-Acting-User-Id must be the sending player." });
        }

        /// <summary>Pending ids are 26-character ULIDs; anything else can't be one (and mustn't reach a ledger key).</summary>
        private IActionResult? RequireWellFormedPendingId(string publicId) =>
            !string.IsNullOrEmpty(publicId) && publicId.Length == 26 && publicId.All(char.IsAsciiLetterOrDigit)
                ? null
                : NotFound(new { error = nameof(CurrencyErrorCode.PendingTransferNotFound), code = nameof(CurrencyErrorCode.PendingTransferNotFound),
                    message = "There is no such payment waiting for your confirmation." });

        private IActionResult? RequireActingPlayer(KnkCaller caller) =>
            caller.IsPluginService && caller.ActingUserId is > 0
                ? null
                : BadRequest(new { error = "ActingUserRequired", message = $"{PluginServiceAuth.ActingUserHeader} must name the player." });

        /// <summary>The game server, the user themself (JWT), or a web user holding knk.admin.currency.history.</summary>
        private async Task<IActionResult?> AuthorizeOwnOrStaffAsync(int userId)
        {
            var caller = HttpContext.GetKnkCaller();
            if (caller.IsPluginService)
            {
                return null;
            }
            if (caller.WebUserId == null)
            {
                return Unauthorized(new { error = "Unauthorized", message = "Log in to use this." });
            }
            if (caller.WebUserId == userId)
            {
                return null;
            }
            var check = await _permissions.CheckAsync(caller.WebUserId.Value, CurrencyHistoryNode);
            return check?.Allowed == true
                ? null
                : StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden", message = $"Requires the {CurrencyHistoryNode} permission." });
        }

        /// <summary>"coins"/"gems"/"xp" (any case, also the enum names); null when unrecognised.</summary>
        internal static Currency? ParseCurrency(string? value, Currency fallback, bool allowExperience)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }
            return value.Trim().ToLowerInvariant() switch
            {
                "coins" or "coin" => Currency.Coins,
                "gems" or "gem" => Currency.Gems,
                "xp" or "experience" or "exp" when allowExperience => Currency.Experience,
                _ => null
            };
        }
    }
}
