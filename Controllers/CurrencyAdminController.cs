using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// Staff currency routes (currency-payments DESIGN.md §3.4, IMPLEMENTATION_PLAN.md Phase 4,
    /// KNG-23 folded in): the all-players balance event log, transaction detail and reversal,
    /// staff adjustments with a mandatory reason, transfer locks and the per-currency policy.
    /// <para>
    /// Nodes (DESIGN.md §3.8): knk.admin.currency.history (event log, detail),
    /// .reverse, .lock, .policy; adjustments need knk.admin.user.manage plus
    /// knk.admin.user.coins|gems|xp for the balance changed (the in-game /knk user nodes). Routes
    /// the plugin also calls (/knk currency reverse|lock|unlock, detail) take
    /// RequireServiceOrPermission: the game server's key passes and the plugin checks the staff
    /// member's node in-game first, as for every other staff route. Web-only routes take
    /// RequirePermission.
    /// </para>
    /// </summary>
    [ApiController]
    [Route("api/currency/admin")]
    public class CurrencyAdminController : ControllerBase
    {
        private readonly ICurrencyService _currency;
        private readonly ICurrencyAdminService _admin;
        private readonly IUserService _users;
        private readonly IPermissionResolutionService _permissions;

        public CurrencyAdminController(
            ICurrencyService currency,
            ICurrencyAdminService admin,
            IUserService users,
            IPermissionResolutionService permissions)
        {
            _currency = currency;
            _admin = admin;
            _users = users;
            _permissions = permissions;
        }

        /// <summary>
        /// The balance event log (KNG-23): every change of every player's coins, gems and XP, one
        /// row per player and currency, with server-side filters, sorting and paging. Also the
        /// player profile's "Balance history" tab (userId).
        /// </summary>
        /// <param name="currency">coins, gems or xp; all when omitted</param>
        /// <param name="recipient">Part of the affected player's username</param>
        /// <param name="userId">Exactly this player</param>
        /// <param name="initiator">Part of the initiating staff member's username or system component</param>
        /// <param name="initiatorType">Player, Admin, System or PluginService</param>
        /// <param name="source">Source type, e.g. SiegeMatch, Kit, TitleBracket</param>
        /// <param name="sourceRef">Source id (with source)</param>
        /// <param name="reason">Reason code, e.g. SALARY, ADMIN_GRANT</param>
        /// <param name="kind">Grant, Spend, Transfer, AdminAdjust, Reversal, Migration, Merge</param>
        /// <param name="transaction">A transaction's public id</param>
        /// <param name="from">UTC, inclusive</param>
        /// <param name="to">UTC, exclusive</param>
        /// <param name="sort">createdAt (default), amount, recipient, currency, operation, balanceAfter, reason, initiator</param>
        /// <param name="dir">desc (default) or asc</param>
        /// <param name="page">1-based</param>
        /// <param name="pageSize">1–200, default 50</param>
        /// <response code="200">A page of ledger lines</response>
        /// <response code="400">An unknown currency, kind, initiator type or sort</response>
        [RequirePermission(StaffPermissions.CurrencyHistory)]
        [HttpGet("ledger")]
        public async Task<IActionResult> GetLedger(
            [FromQuery] string? currency = null,
            [FromQuery] string? recipient = null,
            [FromQuery] int? userId = null,
            [FromQuery] string? initiator = null,
            [FromQuery] string? initiatorType = null,
            [FromQuery] string? source = null,
            [FromQuery] string? sourceRef = null,
            [FromQuery] string? reason = null,
            [FromQuery] string? kind = null,
            [FromQuery] string? transaction = null,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] string? sort = null,
            [FromQuery] string? dir = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken ct = default)
        {
            Currency? currencyFilter = null;
            if (!string.IsNullOrWhiteSpace(currency))
            {
                currencyFilter = CurrencyController.ParseCurrency(currency, Currency.Coins, allowExperience: true);
                if (currencyFilter == null) return BadRequest(new { error = "InvalidCurrency", message = "currency must be coins, gems or xp." });
            }
            CurrencyInitiator? initiatorFilter = null;
            if (!string.IsNullOrWhiteSpace(initiatorType))
            {
                if (!Enum.TryParse<CurrencyInitiator>(initiatorType, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                    return BadRequest(new { error = "InvalidInitiatorType", message = "initiatorType must be Player, Admin, System or PluginService." });
                initiatorFilter = parsed;
            }
            CurrencyTransactionKind? kindFilter = null;
            if (!string.IsNullOrWhiteSpace(kind))
            {
                if (!Enum.TryParse<CurrencyTransactionKind>(kind, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                    return BadRequest(new { error = "InvalidKind", message = $"kind must be one of {string.Join(", ", Enum.GetNames<CurrencyTransactionKind>())}." });
                kindFilter = parsed;
            }
            var sortBy = ParseSort(sort);
            if (sortBy == null) return BadRequest(new { error = "InvalidSort", message = "sort must be createdAt, amount, recipient, currency, operation, balanceAfter, reason or initiator." });
            var descending = !string.Equals(dir?.Trim(), "asc", StringComparison.OrdinalIgnoreCase);

            var history = await _currency.GetHistoryAsync(new LedgerQuery
            {
                UserId = userId is > 0 ? userId : null,
                Currency = currencyFilter,
                UserSearch = Trimmed(recipient),
                InitiatorSearch = Trimmed(initiator),
                Initiator = initiatorFilter,
                SourceType = Trimmed(source),
                SourceRef = Trimmed(sourceRef),
                ReasonCode = Trimmed(reason)?.ToUpperInvariant(),
                Kind = kindFilter,
                TransactionPublicId = NormalizePublicId(transaction),
                From = from.HasValue ? AsUtc(from.Value) : null,
                To = to.HasValue ? AsUtc(to.Value) : null,
                Sort = sortBy.Value,
                Descending = descending,
                Page = Math.Max(1, page),
                PageSize = Math.Clamp(pageSize, 1, 200)
            }, ct);
            return Ok(history);
        }

        /// <summary>One transaction with every leg (players and system accounts) and its reversal links.</summary>
        /// <response code="200">The transaction</response>
        /// <response code="404">No such transaction</response>
        [RequireServiceOrPermission(StaffPermissions.CurrencyHistory)]
        [HttpGet("transactions/{publicId}")]
        public async Task<IActionResult> GetTransaction(string publicId, CancellationToken ct)
        {
            try
            {
                return Ok(await _admin.GetTransactionAsync(publicId, ct));
            }
            catch (CurrencyException ex)
            {
                return CurrencyHttp.ToResult(this, ex);
            }
        }

        /// <summary>
        /// Reverses a transaction (DESIGN.md D11): posts its mirror as a REVERSAL, once. It never
        /// takes a balance below zero: when a player has spent some of it, the reversal is refused
        /// (400 ReversalWouldGoNegative) unless allowPartial, which reverses what is
        /// there and records the shortfall. A retried request returns the same reversal.
        /// </summary>
        /// <response code="200">Reversed (or replayed)</response>
        /// <response code="400">Note shorter than 10 characters; would go negative; not reversible</response>
        /// <response code="404">No such transaction</response>
        /// <response code="409">Already reversed</response>
        [RequireServiceOrPermission(StaffPermissions.CurrencyReverse)]
        [HttpPost("transactions/{publicId}/reverse")]
        public async Task<IActionResult> Reverse(string publicId, [FromBody] ReverseTransactionDto request, CancellationToken ct)
        {
            if (request == null) return BadRequest(new { error = "InvalidRequest", message = "Request body is required" });
            try
            {
                var caller = HttpContext.GetKnkCaller();
                var result = await _admin.ReverseAsync(publicId, request, caller,
                    caller.IsWebUser ? "WebAppLedger" : "PluginCurrencyAdmin", ct);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = "ValidationFailed", message = ex.Message });
            }
            catch (CurrencyException ex)
            {
                return CurrencyHttp.ToResult(this, ex);
            }
        }

        /// <summary>
        /// A staff Add/Remove/Set of one player's coins, gems or XP (DESIGN.md §3.4) with a reason
        /// category and a note of at least 10 characters, and an Idempotency-Key (new per action,
        /// the same on a retry). Set is computed server-side under the row lock; expectedCurrent
        /// refuses a set from a stale screen (409). Grants count against the staff member's daily
        /// cap (422 AdminDailyCapExceeded) unless they hold knk.admin.currency.unlimited; so do the
        /// coin/gem title bonuses an XP increase triggers, and over the cap the whole change is
        /// refused. An XP increase (Add, or Set above the current value) needs knk.admin.user.xp,
        /// .coins and .gems from a logged-in caller (KNG-21).
        /// </summary>
        /// <response code="200">Applied (or replayed): balances before/after and the ledger id</response>
        /// <response code="400">Bad category, note, amount, funds or cap; missing Idempotency-Key</response>
        /// <response code="403">Without the node for that balance (knk.admin.user.coins/gems/xp; an XP increase needs all three)</response>
        /// <response code="404">No such player</response>
        /// <response code="409">expectedCurrent didn't match, or the key was used for a different request</response>
        /// <response code="422">Over the staff member's daily grant cap</response>
        [RequireServiceOrPermission(StaffPermissions.ManageUsers)]
        [HttpPost("adjustments")]
        public async Task<IActionResult> Adjust([FromBody] AdminAdjustmentDto request, CancellationToken ct)
        {
            if (request == null) return BadRequest(new { error = "InvalidRequest", message = "Request body is required" });
            var caller = HttpContext.GetKnkCaller();
            if (!Enum.IsDefined(request.Currency) || !Enum.IsDefined(request.Mode))
            {
                return BadRequest(new { error = "ValidationFailed", message = "currency must be Coins, Gems or Experience and mode Add, Remove or Set." });
            }
            var denied = await RequireBalanceNodeAsync(caller, request);
            if (denied != null) return denied;

            var category = request.Category?.Trim().ToUpperInvariant() ?? "";
            if (!AdminAdjustmentCategories.All.TryGetValue(category, out var categoryLabel))
            {
                return BadRequest(new { error = "ValidationFailed", message = $"category must be one of {string.Join(", ", AdminAdjustmentCategories.All.Keys)}." });
            }
            var note = request.Note?.Trim() ?? "";
            if (note.Length < AdminAdjustmentCategories.MinNoteLength || note.Length > AdminAdjustmentCategories.MaxNoteLength)
            {
                return BadRequest(new { error = "ValidationFailed", message = $"Say why in {AdminAdjustmentCategories.MinNoteLength}–{AdminAdjustmentCategories.MaxNoteLength} characters." });
            }
            var key = CurrencyHttp.ReadKey(this, required: true, out var keyError);
            if (keyError != null) return keyError;

            try
            {
                var metadata = JsonSerializer.Serialize(new { category });
                var ctx = CurrencyContext.ForCaller(caller, CurrencyReasons.ForAdminMode(request.Mode), key!,
                    caller.IsWebUser ? "WebAppAdminAdjustment" : "PluginUserAdmin",
                    staffAction: true, reason: $"{categoryLabel}: {note}") with
                {
                    MetadataJson = metadata
                };
                var result = await _users.AdjustBalancesAsync(request.TargetUserId, new[]
                {
                    new BalanceChangeDto
                    {
                        Currency = request.Currency,
                        Mode = request.Mode,
                        Amount = request.Amount,
                        ExpectedCurrent = request.ExpectedCurrent
                    }
                }, ctx, metadata, request.NotifyPlayer);
                return Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { error = "UserNotFound", message = $"User with ID {request.TargetUserId} not found" });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = "ValidationFailed", message = ex.Message });
            }
            catch (CurrencyException ex)
            {
                return CurrencyHttp.ToResult(this, ex);
            }
            catch (BalanceCapExceededException ex)
            {
                return BadRequest(new { error = BalanceCapExceededException.Code, message = ex.Message });
            }
        }

        /// <summary>Whether the player's transfers are locked, and why.</summary>
        [RequireServiceOrPermission(StaffPermissions.ManageUsers)]
        [HttpGet("users/{userId:int}/transfer-lock")]
        public Task<IActionResult> GetTransferLock(int userId, CancellationToken ct) =>
            LockResultAsync(() => _admin.GetTransferLockAsync(userId, ct));

        /// <summary>Locks the player's transfers (they can neither send nor receive; system grants still arrive).</summary>
        /// <response code="200">Locked</response>
        /// <response code="400">No reason, or longer than 200 characters</response>
        /// <response code="404">No such player</response>
        [RequireServiceOrPermission(StaffPermissions.CurrencyLock)]
        [HttpPut("users/{userId:int}/transfer-lock")]
        public Task<IActionResult> LockTransfers(int userId, [FromBody] SetTransferLockDto request, CancellationToken ct) =>
            LockResultAsync(() => _admin.SetTransferLockAsync(userId, request?.Reason ?? "", HttpContext.GetKnkCaller().ActorUserId, ct));

        /// <summary>Lifts the player's transfer lock.</summary>
        [RequireServiceOrPermission(StaffPermissions.CurrencyLock)]
        [HttpDelete("users/{userId:int}/transfer-lock")]
        public Task<IActionResult> UnlockTransfers(int userId, CancellationToken ct) =>
            LockResultAsync(() => _admin.ClearTransferLockAsync(userId, HttpContext.GetKnkCaller().ActorUserId, ct));

        /// <summary>The coin and gem policies (transfer rules, staff grant cap, signup grant).</summary>
        [RequirePermission(StaffPermissions.CurrencyPolicy)]
        [HttpGet("policy")]
        public async Task<IActionResult> GetPolicies(CancellationToken ct) => Ok(await _admin.GetPoliciesAsync(ct));

        /// <summary>
        /// Edits a currency's policy; every change is audit-logged (CurrencyPolicyChanged). The body's
        /// updatedAt must be the value last loaded (optimistic concurrency), so a stale form can't
        /// undo a change made meanwhile - notably the R1 kill switch turning transfers off.
        /// </summary>
        /// <param name="currency">coins or gems</param>
        /// <response code="200">The saved policy</response>
        /// <response code="400">A value out of range, or no updatedAt</response>
        /// <response code="404">No policy for that currency</response>
        /// <response code="409">PolicyChanged: the policy changed since it was loaded; details hold the current one</response>
        [RequirePermission(StaffPermissions.CurrencyPolicy)]
        [HttpPut("policy/{currency}")]
        public async Task<IActionResult> UpdatePolicy(string currency, [FromBody] CurrencyPolicyDto request, CancellationToken ct)
        {
            if (request == null) return BadRequest(new { error = "InvalidRequest", message = "Request body is required" });
            var parsed = CurrencyController.ParseCurrency(currency, Currency.Coins, allowExperience: false);
            if (parsed == null || string.IsNullOrWhiteSpace(currency))
            {
                return BadRequest(new { error = "InvalidCurrency", message = "currency must be coins or gems." });
            }
            try
            {
                return Ok(await _admin.UpdatePolicyAsync(parsed.Value, request, HttpContext.GetKnkCaller().ActorUserId, ct));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = "PolicyNotFound", message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = "ValidationFailed", message = ex.Message });
            }
            catch (CurrencyException ex)
            {
                return CurrencyHttp.ToResult(this, ex);
            }
        }

        // ===== Helpers =====

        private async Task<IActionResult> LockResultAsync(Func<Task<TransferLockDto>> work)
        {
            try
            {
                return Ok(await work());
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = "UserNotFound", message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = "ValidationFailed", message = ex.Message });
            }
        }

        /// <summary>A web caller needs the in-game node of the balance it changes, and for an XP
        /// increase the coins and gems nodes too (KNG-21); the plugin checks them in-game.</summary>
        private async Task<IActionResult?> RequireBalanceNodeAsync(KnkCaller caller, AdminAdjustmentDto request)
        {
            if (caller.IsPluginService)
            {
                return null;
            }
            if (caller.WebUserId == null)
            {
                return Unauthorized(new { error = "Unauthorized", message = "Log in to use this." });
            }
            var increasesExperience = await CurrencyHttp.IncreasesExperienceAsync(request.Currency, request.Mode, request.Amount,
                async () => (await _users.GetByIdAsync(request.TargetUserId))?.ExperiencePoints);
            foreach (var node in CurrencyHttp.BalanceNodes(request.Currency, increasesExperience))
            {
                var check = await _permissions.CheckAsync(caller.WebUserId.Value, node);
                if (check?.Allowed != true)
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden", message = $"Requires the {node} permission." });
                }
            }
            return null;
        }

        internal static LedgerSort? ParseSort(string? value) => (value?.Trim().ToLowerInvariant() ?? "") switch
        {
            "" or "createdat" or "date" or "time" => LedgerSort.CreatedAt,
            "amount" => LedgerSort.Amount,
            "recipient" or "user" or "player" => LedgerSort.Recipient,
            "currency" => LedgerSort.Currency,
            "operation" => LedgerSort.Operation,
            "balanceafter" or "balance" => LedgerSort.BalanceAfter,
            "reason" or "reasoncode" => LedgerSort.ReasonCode,
            "initiator" => LedgerSort.Initiator,
            _ => null
        };

        private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        /// <summary>A public id as typed: any case, with or without the in-game "TX " prefix.</summary>
        private static string? NormalizePublicId(string? value)
        {
            var id = Trimmed(value)?.Replace(" ", "").ToUpperInvariant();
            return id != null && id.Length == 28 && id.StartsWith("TX", StringComparison.Ordinal) ? id[2..] : id;
        }

        private static DateTime AsUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}
