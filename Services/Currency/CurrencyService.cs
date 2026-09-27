using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// The currency ledger (docs/specs/currency-payments/DESIGN.md §3, IMPLEMENTATION_PLAN.md
    /// Phase 1, KNG-23 folded in). See <see cref="ICurrencyService"/> for the contract.
    /// <para>
    /// Every posting runs inside IUserRepository.RunWithUsersLockedAsync: one transaction (or the
    /// caller's), user rows locked FOR UPDATE in ascending id order, then — under the lock — the
    /// idempotency lookup, the balance checks, and a single SaveChanges that writes the changed
    /// balance columns and the ledger rows together. Because the lookup happens after the lock,
    /// two identical requests for the same user serialize and the second one replays; the unique
    /// (IdempotencyScope, IdempotencyKey) index catches the rest (same key, different users), and
    /// the loser replays the winner's committed row.
    /// </para>
    /// </summary>
    public partial class CurrencyService : ICurrencyService
    {
        private static readonly Regex KeyPattern = new("^[A-Za-z0-9:_.\\-]{1,100}$", RegexOptions.Compiled);
        private const int MaxMetadataLength = 16_000;

        private readonly ICurrencyRepository _repo;
        private readonly IUserRepository _users;
        private readonly ILogger<CurrencyService> _logger;
        private readonly IPermissionResolutionService? _permissions;
        private readonly CurrencyMetrics? _metrics;
        private readonly CurrencyMonitorSignals? _signals;

        /// <param name="permissions">Resolves knk.admin.currency.unlimited for the per-staff daily
        /// grant cap; without it (some tests) nobody is exempt.</param>
        /// <param name="metrics">OpenTelemetry counters (currency Phase 5); optional.</param>
        /// <param name="signals">Where a refusal at the balance cap is reported for alert R8
        /// (currency Phase 5); optional.</param>
        public CurrencyService(ICurrencyRepository repo, IUserRepository users, ILogger<CurrencyService> logger,
            IPermissionResolutionService? permissions = null, CurrencyMetrics? metrics = null,
            CurrencyMonitorSignals? signals = null)
        {
            _repo = repo;
            _users = users;
            _logger = logger;
            _permissions = permissions;
            _metrics = metrics;
            _signals = signals;
        }

        /// <summary>Window of the per-staff grant cap (CurrencyPolicy.AdminDailyGrantCapPerActor).</summary>
        public static readonly TimeSpan AdminGrantCapWindow = TimeSpan.FromHours(24);

        // ===== Postings =====

        public Task<PostingResult> PostAsync(IReadOnlyList<CurrencyLeg> legs, CurrencyContext ctx, CancellationToken ct = default) =>
            ObserveAsync(() => PostCoreAsync(legs, ctx, ct), ctx);

        private Task<PostingResult> PostCoreAsync(IReadOnlyList<CurrencyLeg> legs, CurrencyContext ctx, CancellationToken ct)
        {
            var reason = RequireReason(ctx);
            if (reason.Kind is not (CurrencyTransactionKind.Grant or CurrencyTransactionKind.Spend or CurrencyTransactionKind.Merge))
            {
                throw Invalid($"{reason.Code} can't be posted this way (use AdminAdjustAsync, ReverseAsync or the transfer API).");
            }
            ValidateContext(ctx, reason);

            if (legs == null || legs.Count == 0)
            {
                throw Invalid("A posting needs at least one leg.");
            }
            if (legs.GroupBy(l => (l.UserId, l.Currency)).Any(g => g.Count() > 1))
            {
                throw Invalid("Each user and currency may appear only once in a posting.");
            }
            foreach (var leg in legs)
            {
                if (leg.UserId <= 0)
                {
                    throw new CurrencyException(CurrencyErrorCode.UserNotFound, $"User {leg.UserId} doesn't exist.");
                }
                RequireAmountInRange(leg.Currency, leg.Amount == long.MinValue ? long.MaxValue : Math.Abs(leg.Amount), allowZero: false);
                if (reason.Direction == CurrencyReasonDirection.Credit && leg.Amount < 0
                    || reason.Direction == CurrencyReasonDirection.Debit && leg.Amount > 0)
                {
                    throw new CurrencyException(CurrencyErrorCode.AmountOutOfRange,
                        $"{reason.Code} only {(reason.Direction == CurrencyReasonDirection.Credit ? "adds to" : "takes from")} balances (leg for user {leg.UserId}: {leg.Amount}).");
                }
            }

            // The source is part of the request: the same key reused for another kit (or
            // teleport, lootbox, …) at the same price is a different request, not a replay that
            // reports "paid" for something that never was.
            var canonical = "post|" + reason.Code + "|" + string.Join(";", legs
                .OrderBy(l => l.UserId).ThenBy(l => l.Currency)
                .Select(l => $"{l.UserId}:{(int)l.Currency}:{l.Amount}"))
                + $"|source:{ctx.SourceType}:{ctx.SourceRef}";

            return ExecuteAsync(ctx, reason, legs.Select(l => l.UserId), canonical, (users, _) =>
            {
                var plan = new EntryPlan();
                foreach (var leg in legs)
                {
                    plan.AddUserLeg(users[leg.UserId], leg.Currency, leg.Amount, leg.Amount < 0 ? CurrencyOperation.Remove : CurrencyOperation.Add);
                }
                plan.BalanceWithSystemAccount(reason.SystemAccount!);
                return Task.FromResult(plan.ToDraft());
            }, ct);
        }

        public Task<PostingResult> GrantAsync(int userId, Currency currency, long amount, CurrencyContext ctx, CancellationToken ct = default) =>
            ObserveAsync(() => GrantCoreAsync(userId, currency, amount, ctx, ct), ctx);

        private Task<PostingResult> GrantCoreAsync(int userId, Currency currency, long amount, CurrencyContext ctx, CancellationToken ct)
        {
            var reason = RequireReason(ctx);
            if (reason.Direction != CurrencyReasonDirection.Credit || reason.Kind == CurrencyTransactionKind.AdminAdjust)
            {
                throw Invalid($"{reason.Code} isn't a grant reason.");
            }
            RequireAmountInRange(currency, amount, allowZero: false);
            return PostAsync(new[] { new CurrencyLeg(userId, currency, amount) }, ctx, ct);
        }

        public Task<PostingResult> SpendAsync(int userId, Currency currency, long amount, CurrencyContext ctx, CancellationToken ct = default) =>
            ObserveAsync(() => SpendCoreAsync(userId, currency, amount, ctx, ct), ctx);

        private Task<PostingResult> SpendCoreAsync(int userId, Currency currency, long amount, CurrencyContext ctx, CancellationToken ct)
        {
            var reason = RequireReason(ctx);
            if (reason.Direction != CurrencyReasonDirection.Debit || reason.Kind == CurrencyTransactionKind.AdminAdjust)
            {
                throw Invalid($"{reason.Code} isn't a spend reason.");
            }
            RequireAmountInRange(currency, amount, allowZero: false);
            return PostAsync(new[] { new CurrencyLeg(userId, currency, -amount) }, ctx, ct);
        }

        public Task<PostingResult> AdminAdjustAsync(AdminAdjustRequest req, CurrencyContext ctx, CancellationToken ct = default) =>
            ObserveAsync(() => AdminAdjustCoreAsync(req, ctx, ct), ctx);

        private Task<PostingResult> AdminAdjustCoreAsync(AdminAdjustRequest req, CurrencyContext ctx, CancellationToken ct)
        {
            var reason = RequireReason(ctx);
            if (ctx.ReasonCode != CurrencyReasons.ForAdminMode(req.Mode))
            {
                throw Invalid($"A staff {req.Mode} needs reason code {CurrencyReasons.ForAdminMode(req.Mode)}, not {ctx.ReasonCode}.");
            }
            if (ctx.Initiator == CurrencyInitiator.System)
            {
                throw Invalid("A staff adjustment needs a person or the game server as initiator, not a system component.");
            }
            ValidateContext(ctx, reason);
            if (req.UserId <= 0)
            {
                throw new CurrencyException(CurrencyErrorCode.UserNotFound, $"User {req.UserId} doesn't exist.");
            }
            RequireAmountInRange(req.Currency, req.Amount, allowZero: req.Mode == CurrencyOperation.Set);
            if (req.ExpectedCurrent.HasValue)
            {
                RequireAmountInRange(req.Currency, req.ExpectedCurrent.Value, allowZero: true);
            }

            var canonical = $"admin|{reason.Code}|{req.UserId}:{(int)req.Currency}:{req.Mode}:{req.Amount}|expected:{req.ExpectedCurrent?.ToString() ?? "-"}";

            // Per-staff daily grant cap (DESIGN.md §3.5, §5 Q9): the staff member's row is locked
            // too, so their concurrent grants to different players are counted one after another.
            var capActor = ctx.InitiatorUserId is > 0 && ctx.Initiator != CurrencyInitiator.PluginService ? ctx.InitiatorUserId : null;

            return ExecuteAsync(ctx, reason, new[] { req.UserId }, canonical, async (users, token) =>
            {
                var user = users[req.UserId];
                var current = Balance(user, req.Currency);
                if (req.ExpectedCurrent.HasValue && req.ExpectedCurrent.Value != current)
                {
                    throw new CurrencyException(CurrencyErrorCode.ExpectedBalanceMismatch,
                        $"The {Name(req.Currency)} balance is {current:N0}, not the expected {req.ExpectedCurrent.Value:N0}; reload and try again.",
                        new { expected = req.ExpectedCurrent.Value, actual = current });
                }
                var delta = req.Mode switch
                {
                    CurrencyOperation.Add => req.Amount,
                    CurrencyOperation.Remove => -req.Amount,
                    _ => checked(req.Amount - current)
                };

                if (delta > 0 && capActor.HasValue)
                {
                    await RequireWithinAdminGrantCapAsync(capActor.Value, req.Currency, delta, token);
                }

                var plan = new EntryPlan();
                plan.AddUserLeg(user, req.Currency, delta, req.Mode);
                plan.BalanceWithSystemAccount(reason.SystemAccount!);
                return plan.ToDraft();
            }, ct, alsoLock: capActor.HasValue ? new[] { capActor.Value } : null);
        }

        /// <summary>
        /// Refuses a staff grant that takes <paramref name="actorUserId"/>'s rolling 24 h total of
        /// <paramref name="currency"/> added through adjustments above the policy's
        /// AdminDailyGrantCapPerActor (0 or no policy row = no cap), unless they hold
        /// knk.admin.currency.unlimited. Runs under the staff member's row lock.
        /// </summary>
        private async Task RequireWithinAdminGrantCapAsync(int actorUserId, Currency currency, long amount, CancellationToken ct)
        {
            var policies = await _repo.GetPoliciesAsync(ct);
            var cap = policies.TryGetValue(currency, out var policy) ? policy.AdminDailyGrantCapPerActor : 0;
            if (cap <= 0)
            {
                return;
            }
            var granted = await _repo.SumAdminGrantedSinceAsync(actorUserId, currency, DateTime.UtcNow - AdminGrantCapWindow, ct);
            if (checked(granted + amount) <= cap)
            {
                return;
            }
            if (_permissions != null
                && (await _permissions.CheckAsync(actorUserId, Attributes.StaffPermissions.CurrencyUnlimited))?.Allowed == true)
            {
                return;
            }
            var remaining = Math.Max(0, cap - granted);
            throw new CurrencyException(CurrencyErrorCode.AdminDailyCapExceeded,
                $"That would pass your daily staff grant limit of {cap:N0} {Name(currency)}: you granted {granted:N0} in the last 24 hours, {remaining:N0} left. Staff with {Attributes.StaffPermissions.CurrencyUnlimited} have no limit.",
                new { currency = currency.ToString(), cap, grantedLast24h = granted, remaining, requested = amount });
        }

        public Task<PostingResult> ReverseAsync(long transactionId, ReversalOptions opts, CurrencyContext ctx, CancellationToken ct = default) =>
            ObserveAsync(() => ReverseCoreAsync(transactionId, opts, ctx, ct), ctx);

        private async Task<PostingResult> ReverseCoreAsync(long transactionId, ReversalOptions opts, CurrencyContext ctx, CancellationToken ct)
        {
            var reason = RequireReason(ctx);
            if (reason.Kind != CurrencyTransactionKind.Reversal)
            {
                throw Invalid($"A reversal needs reason code {CurrencyReasons.Reversal}, not {ctx.ReasonCode}.");
            }
            ValidateContext(ctx, reason);
            opts ??= new ReversalOptions();

            var original = await _repo.FindByIdAsync(transactionId, ct)
                ?? throw new CurrencyException(CurrencyErrorCode.TransactionNotFound, $"Transaction {transactionId} doesn't exist.");
            if (original.Kind == CurrencyTransactionKind.Reversal)
            {
                throw new CurrencyException(CurrencyErrorCode.NotReversible,
                    $"Transaction {original.PublicId} is itself a reversal; correct it with a new adjustment instead.");
            }

            var userLegs = original.Entries.Where(e => e.AccountKind == CurrencyAccountKind.User).OrderBy(e => e.Id).ToList();
            var canonical = $"reverse|{original.Id}|partial:{opts.AllowPartial}";

            return await ExecuteAsync(ctx, reason, userLegs.Select(e => e.UserId!.Value), canonical, async (users, token) =>
            {
                var prior = await _repo.FindReversalOfAsync(original.Id, token);
                if (prior != null)
                {
                    throw new CurrencyException(CurrencyErrorCode.AlreadyReversed,
                        $"Transaction {original.PublicId} was already reversed by {prior.PublicId}.", new { reversalPublicId = prior.PublicId });
                }

                var plan = new EntryPlan();
                var shortfall = new List<object>();
                var clamped = new HashSet<Currency>();
                foreach (var leg in userLegs)
                {
                    var user = users[leg.UserId!.Value];
                    var mirror = -leg.Amount;
                    var available = plan.PendingBalance(user, leg.Currency);
                    if (available + mirror < 0)
                    {
                        if (!opts.AllowPartial)
                        {
                            throw new CurrencyException(CurrencyErrorCode.ReversalWouldGoNegative,
                                $"Reversing {original.PublicId} would take user {user.Id}'s {Name(leg.Currency)} below zero (has {available:N0}, needs {-mirror:N0}).",
                                new { userId = user.Id, currency = leg.Currency.ToString(), balance = available, required = -mirror });
                        }
                        shortfall.Add(new { userId = user.Id, currency = leg.Currency.ToString(), amount = -(available + mirror) });
                        clamped.Add(leg.Currency);
                        mirror = -available;
                    }
                    plan.AddUserLeg(user, leg.Currency, mirror, mirror < 0 ? CurrencyOperation.Remove : CurrencyOperation.Add);
                }

                foreach (var currency in userLegs.Select(e => e.Currency).Distinct())
                {
                    var accounts = original.Entries
                        .Where(e => e.AccountKind == CurrencyAccountKind.System && e.Currency == currency)
                        .ToList();
                    var accountNames = accounts.Select(e => e.SystemAccount!).Distinct().ToList();
                    // A clamped debit may only shrink what a single system account takes back.
                    // If the reversal also credits a player in this currency (a transfer's sender,
                    // also when it paid a fee to SYS_FEES), balancing the shortfall against the
                    // system account would mint it for that player; with several (or no) system
                    // accounts it would move it onto someone else.
                    var creditsAPlayer = userLegs.Any(e => e.Currency == currency && e.Amount < 0);
                    if (clamped.Contains(currency) && (accountNames.Count != 1 || creditsAPlayer))
                    {
                        throw new CurrencyException(CurrencyErrorCode.ReversalWouldGoNegative,
                            $"Transaction {original.PublicId} can't be partially reversed.");
                    }
                    if (accountNames.Count == 1)
                    {
                        plan.BalanceWithSystemAccount(accountNames[0], currency);
                    }
                    else
                    {
                        foreach (var account in accounts)
                        {
                            plan.AddSystemLeg(account.SystemAccount!, currency, -account.Amount);
                        }
                    }
                }

                var draft = plan.ToDraft();
                draft.ReversesTransactionId = original.Id;
                draft.MetadataJson = ReversalMetadata(original, shortfall, ctx.MetadataJson);
                return draft;
            }, ct);
        }

        // ===== Reads =====

        public async Task<PostingResult?> FindAsync(string scope, string idempotencyKey, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(scope) || string.IsNullOrEmpty(idempotencyKey))
            {
                return null;
            }
            var existing = await _repo.FindByIdempotencyAsync(scope, idempotencyKey, ct);
            if (existing == null)
            {
                return null;
            }
            var userIds = existing.Entries.Where(e => e.UserId.HasValue).Select(e => e.UserId!.Value);
            return ToResult(existing, await _repo.GetBalancesAsync(userIds, ct), replayed: true);
        }

        public async Task<IReadOnlyDictionary<Currency, CurrencyPolicy>> GetPoliciesAsync(CancellationToken ct = default) =>
            await _repo.GetPoliciesAsync(ct);

        public async Task<BalancesDto> GetBalancesAsync(int userId, CancellationToken ct = default)
        {
            var balances = await _repo.GetBalancesAsync(new[] { userId }, ct);
            return balances.TryGetValue(userId, out var dto)
                ? dto
                : throw new CurrencyException(CurrencyErrorCode.UserNotFound, $"User {userId} doesn't exist.");
        }

        public async Task<PagedResultDto<LedgerLineDto>> GetHistoryAsync(LedgerQuery q, CancellationToken ct = default)
        {
            var query = q with
            {
                Page = Math.Max(1, q.Page),
                PageSize = Math.Clamp(q.PageSize, 1, 200)
            };
            var page = await _repo.SearchLinesAsync(query, ct);
            return new PagedResultDto<LedgerLineDto>
            {
                Items = page.Items,
                TotalCount = page.TotalCount,
                PageNumber = page.PageNumber,
                PageSize = page.PageSize
            };
        }

        // ===== Core =====

        private sealed class PostingDraft
        {
            public List<CurrencyEntry> Entries { get; init; } = new();
            public long? ReversesTransactionId { get; set; }
            public string? MetadataJson { get; set; }

            /// <summary>Player transfers: the sender and recipient (denormalized on the header).</summary>
            public int? FromUserId { get; set; }
            public int? ToUserId { get; set; }
        }

        private async Task<PostingResult> ExecuteAsync(
            CurrencyContext ctx,
            CurrencyReasonInfo reason,
            IEnumerable<int> userIds,
            string canonicalRequest,
            Func<Dictionary<int, User>, CancellationToken, Task<PostingDraft>> build,
            CancellationToken ct,
            Func<CurrencyTransaction, Task>? afterPost = null,
            IEnumerable<int>? alsoLock = null)
        {
            var ids = userIds.Distinct().OrderBy(id => id).ToList();
            // Rows locked with the posting's users (ascending together) that aren't part of the
            // posting and needn't exist, e.g. the staff member whose daily grant cap is checked.
            var lockIds = alsoLock == null ? ids : ids.Concat(alsoLock).Distinct().OrderBy(id => id).ToList();
            var requestHash = Sha256(canonicalRequest);
            PostingResult? result = null;
            CurrencyTransaction? pending = null;
            var waiting = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                await _users.RunWithUsersLockedAsync(lockIds, async () =>
                {
                    _metrics?.RecordLockWait(waiting.Elapsed);
                    var existing = await _repo.FindByIdempotencyAsync(ctx.IdempotencyScope, ctx.IdempotencyKey, ct);
                    if (existing != null)
                    {
                        result = await ReplayAsync(existing, requestHash, ctx, ct);
                        return;
                    }

                    var users = await _repo.GetUsersForUpdateAsync(ids, ct);
                    var missing = ids.Where(id => !users.ContainsKey(id)).ToList();
                    if (missing.Count > 0)
                    {
                        throw new CurrencyException(CurrencyErrorCode.UserNotFound, $"User {string.Join(", ", missing)} doesn't exist.");
                    }

                    var draft = await build(users, ct);
                    pending = new CurrencyTransaction
                    {
                        PublicId = CurrencyIds.NewPublicId(),
                        Kind = reason.Kind,
                        ReasonCode = reason.Code,
                        Reason = string.IsNullOrWhiteSpace(ctx.Reason) ? reason.Description : ctx.Reason.Trim(),
                        SourceType = ctx.SourceType,
                        SourceRef = ctx.SourceRef,
                        IdempotencyScope = ctx.IdempotencyScope,
                        IdempotencyKey = ctx.IdempotencyKey,
                        RequestHash = requestHash,
                        Initiator = ctx.Initiator,
                        InitiatorUserId = ctx.InitiatorUserId,
                        InitiatorComponent = ctx.InitiatorComponent,
                        MetadataJson = draft.MetadataJson ?? ctx.MetadataJson,
                        CorrelationId = ctx.CorrelationId,
                        ReversesTransactionId = draft.ReversesTransactionId,
                        FromUserId = draft.FromUserId,
                        ToUserId = draft.ToUserId,
                        CreatedAt = DateTime.UtcNow,
                        Entries = draft.Entries
                    };
                    await _repo.AddTransactionAsync(pending, ct);
                    if (afterPost != null)
                    {
                        // Same transaction and locks: e.g. a pending transfer marked confirmed.
                        await afterPost(pending);
                    }
                    result = ToResult(pending, users.Values.Select(ToBalances).ToDictionary(b => b.UserId), replayed: false);

                    _logger.LogInformation("Ledger {PublicId}: {Reason} ({Kind}) for {Users}, key {Scope}/{Key}",
                        pending.PublicId, pending.ReasonCode, pending.Kind, string.Join(",", ids), ctx.IdempotencyScope, ctx.IdempotencyKey);
                });
            }
            catch (DbUpdateException ex) when (_repo.IsUniqueViolation(ex))
            {
                var reversed = pending?.ReversesTransactionId;
                if (pending != null)
                {
                    _repo.Discard(pending);
                }
                // A first attempt with the same key committed while this one held other rows'
                // locks: return what it posted. Otherwise it was the one-reversal-only index.
                var existing = await _repo.FindByIdempotencyAsync(ctx.IdempotencyScope, ctx.IdempotencyKey, ct);
                if (existing != null)
                {
                    return await ReplayAsync(existing, requestHash, ctx, ct);
                }
                if (reversed != null)
                {
                    throw new CurrencyException(CurrencyErrorCode.AlreadyReversed, $"Transaction {reversed} was already reversed.");
                }
                throw;
            }
            catch
            {
                if (pending != null)
                {
                    _repo.Discard(pending);
                }
                throw;
            }

            if (pending != null && result is { Replayed: false })
            {
                _metrics?.RecordPosting(pending.ReasonCode, pending.Kind, pending.Entries
                    .Where(e => e.AccountKind == CurrencyAccountKind.User)
                    .Select(e => (e.Currency, e.Amount)));
            }
            return result!;
        }

        /// <summary>
        /// Runs a public posting method so a refusal is counted once (knk.currency.denials) and a
        /// refusal at the balance cap is reported for alert R8, however deeply the public methods
        /// call each other. Synchronous validation errors surface as a faulted task.
        /// </summary>
        private async Task<T> ObserveAsync<T>(Func<Task<T>> work, CurrencyContext? ctx)
        {
            try
            {
                return await work();
            }
            catch (CurrencyException ex) when (!ex.Data.Contains(ObservedMarker))
            {
                ex.Data[ObservedMarker] = true;
                _metrics?.RecordDenial(ex.Code);
                if (ex.Code == CurrencyErrorCode.BalanceCapExceeded && _signals != null)
                {
                    var (userId, currency) = CapHitSubject(ex.Details);
                    _signals.RecordCapHit(userId, currency, ctx?.ReasonCode);
                }
                throw;
            }
        }

        private const string ObservedMarker = "knk.currency.observed";

        /// <summary>The user and currency named in a BalanceCapExceeded refusal's details, when present.</summary>
        private static (int? UserId, Currency? Currency) CapHitSubject(object? details)
        {
            if (details == null)
            {
                return (null, null);
            }
            try
            {
                var json = JsonSerializer.SerializeToElement(details);
                int? userId = json.TryGetProperty("userId", out var u) && u.TryGetInt32(out var id) ? id : null;
                Currency? currency = json.TryGetProperty("currency", out var c) && c.ValueKind == JsonValueKind.String
                    && Enum.TryParse<Currency>(c.GetString(), out var parsed) ? parsed : null;
                return (userId, currency);
            }
            catch (NotSupportedException)
            {
                return (null, null);
            }
        }

        private async Task<PostingResult> ReplayAsync(CurrencyTransaction existing, string requestHash, CurrencyContext ctx, CancellationToken ct)
        {
            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
            {
                _logger.LogWarning("Idempotency key {Scope}/{Key} reused for a different request (original {PublicId})",
                    ctx.IdempotencyScope, ctx.IdempotencyKey, existing.PublicId);
                throw new CurrencyException(CurrencyErrorCode.IdempotencyKeyReuse,
                    $"Idempotency key '{ctx.IdempotencyKey}' was already used for a different request ({existing.PublicId}).",
                    new { originalPublicId = existing.PublicId });
            }
            var userIds = existing.Entries.Where(e => e.UserId.HasValue).Select(e => e.UserId!.Value);
            var balances = await _repo.GetBalancesAsync(userIds, ct);
            _metrics?.RecordReplay(existing.ReasonCode);
            return ToResult(existing, balances, replayed: true);
        }

        private static PostingResult ToResult(CurrencyTransaction tx, Dictionary<int, BalancesDto> balances, bool replayed) => new()
        {
            TransactionId = tx.Id,
            PublicId = tx.PublicId,
            Replayed = replayed,
            ReasonCode = tx.ReasonCode,
            CreatedAt = tx.CreatedAt,
            Entries = tx.Entries
                .Where(e => e.AccountKind == CurrencyAccountKind.User)
                .OrderBy(e => e.UserId).ThenBy(e => e.Currency)
                .Select(e => new PostedEntryDto
                {
                    UserId = e.UserId!.Value,
                    Currency = e.Currency.ToString(),
                    Operation = e.Operation.ToString(),
                    Amount = e.Amount,
                    BalanceBefore = e.BalanceBefore!.Value,
                    BalanceAfter = e.BalanceAfter!.Value
                })
                .ToList(),
            Balances = balances
        };

        private static BalancesDto ToBalances(User user) => new()
        {
            UserId = user.Id,
            Coins = user.Coins,
            Gems = user.Gems,
            ExperiencePoints = user.ExperiencePoints
        };

        /// <summary>
        /// Collects the legs of one posting and checks each user leg against the balance it will
        /// see (including earlier legs of the same posting), without touching any user until all
        /// legs pass; then applies them and adds the system legs.
        /// </summary>
        private sealed class EntryPlan
        {
            private readonly List<(User User, Currency Currency, long Amount, CurrencyOperation Op, long Before, long After)> _userLegs = new();
            private readonly List<CurrencyEntry> _systemLegs = new();

            public long PendingBalance(User user, Currency currency)
            {
                var last = _userLegs.LastOrDefault(l => l.User.Id == user.Id && l.Currency == currency);
                return last.User != null ? last.After : Balance(user, currency);
            }

            public void AddUserLeg(User user, Currency currency, long amount, CurrencyOperation op)
            {
                var before = PendingBalance(user, currency);
                long after;
                try
                {
                    after = checked(before + amount);
                }
                catch (OverflowException)
                {
                    throw CapExceeded(user, currency, before, amount);
                }
                if (after < 0)
                {
                    throw new CurrencyException(CurrencyErrorCode.InsufficientFunds,
                        $"User {user.Id} has {before:N0} {Name(currency)}, {-amount:N0} needed.",
                        new { userId = user.Id, currency = currency.ToString(), balance = before, required = -amount });
                }
                if (after > Cap(currency))
                {
                    throw CapExceeded(user, currency, before, amount);
                }
                _userLegs.Add((user, currency, amount, op, before, after));
            }

            public void AddSystemLeg(string account, Currency currency, long amount) =>
                _systemLegs.Add(new CurrencyEntry
                {
                    Currency = currency,
                    AccountKind = CurrencyAccountKind.System,
                    SystemAccount = account,
                    Operation = amount < 0 ? CurrencyOperation.Remove : CurrencyOperation.Add,
                    Amount = amount
                });

            /// <summary>One system leg per currency (or just <paramref name="only"/>) that zeroes the user legs.</summary>
            public void BalanceWithSystemAccount(string account, Currency? only = null)
            {
                foreach (var group in _userLegs.GroupBy(l => l.Currency).Where(g => only == null || g.Key == only))
                {
                    AddSystemLeg(account, group.Key, -group.Sum(l => l.Amount));
                }
            }

            public PostingDraft ToDraft()
            {
                var entries = new List<CurrencyEntry>();
                foreach (var leg in _userLegs)
                {
                    SetBalance(leg.User, leg.Currency, leg.After);
                    entries.Add(new CurrencyEntry
                    {
                        Currency = leg.Currency,
                        AccountKind = CurrencyAccountKind.User,
                        UserId = leg.User.Id,
                        Operation = leg.Op,
                        Amount = leg.Amount,
                        BalanceBefore = leg.Before,
                        BalanceAfter = leg.After
                    });
                }
                entries.AddRange(_systemLegs);

                // Double entry (DESIGN.md §3.1 invariant 2). A failure here is a bug, not a user error.
                if (entries.GroupBy(e => e.Currency).Any(g => g.Sum(e => e.Amount) != 0))
                {
                    throw new InvalidOperationException("Ledger posting is unbalanced; nothing was written.");
                }
                return new PostingDraft { Entries = entries };
            }

            private static CurrencyException CapExceeded(User user, Currency currency, long before, long amount) =>
                new(CurrencyErrorCode.BalanceCapExceeded,
                    $"User {user.Id}'s {Name(currency)} can't go above {Cap(currency):N0} (has {before:N0}, change {amount:+#,0;-#,0;0}).",
                    new { userId = user.Id, currency = currency.ToString(), balance = before, amount, cap = Cap(currency) });
        }

        // ===== Helpers =====

        public static long Cap(Currency currency) => currency switch
        {
            Currency.Coins => BalanceLimits.MaxCoins,
            Currency.Gems => BalanceLimits.MaxGems,
            Currency.Experience => BalanceLimits.MaxExperience,
            _ => throw new ArgumentOutOfRangeException(nameof(currency))
        };

        private static long Balance(User user, Currency currency) => currency switch
        {
            Currency.Coins => user.Coins,
            Currency.Gems => user.Gems,
            Currency.Experience => user.ExperiencePoints,
            _ => throw new ArgumentOutOfRangeException(nameof(currency))
        };

        private static void SetBalance(User user, Currency currency, long value)
        {
            switch (currency)
            {
                case Currency.Coins: user.Coins = checked((int)value); break;
                case Currency.Gems: user.Gems = checked((int)value); break;
                case Currency.Experience: user.ExperiencePoints = checked((int)value); break;
                default: throw new ArgumentOutOfRangeException(nameof(currency));
            }
        }

        private static string Name(Currency currency) => currency switch
        {
            Currency.Coins => "coins",
            Currency.Gems => "gems",
            _ => "experience points"
        };

        private static void RequireAmountInRange(Currency currency, long amount, bool allowZero)
        {
            if (!Enum.IsDefined(currency))
            {
                throw Invalid($"Unknown currency {(int)currency}.");
            }
            if (amount < (allowZero ? 0 : 1) || amount > Cap(currency))
            {
                throw new CurrencyException(CurrencyErrorCode.AmountOutOfRange,
                    $"Amount must be between {(allowZero ? 0 : 1)} and {Cap(currency):N0} {Name(currency)} (got {amount:N0}).");
            }
        }

        private static CurrencyReasonInfo RequireReason(CurrencyContext ctx)
        {
            if (ctx == null)
            {
                throw Invalid("A posting needs a CurrencyContext.");
            }
            return CurrencyReasons.Find(ctx.ReasonCode) ?? throw Invalid($"Unknown reason code '{ctx.ReasonCode}'.");
        }

        private static void ValidateContext(CurrencyContext ctx, CurrencyReasonInfo reason)
        {
            if (string.IsNullOrEmpty(ctx.IdempotencyKey) || !KeyPattern.IsMatch(ctx.IdempotencyKey))
            {
                throw Invalid("The idempotency key must be 1–100 characters of A–Z, a–z, 0–9, ':', '_', '.', '-'.");
            }
            if (string.IsNullOrWhiteSpace(ctx.IdempotencyScope) || ctx.IdempotencyScope.Length > 40)
            {
                throw Invalid("The idempotency scope must be 1–40 characters.");
            }
            if (reason.Kind is CurrencyTransactionKind.AdminAdjust or CurrencyTransactionKind.Reversal
                && string.IsNullOrWhiteSpace(ctx.Reason))
            {
                throw Invalid($"{reason.Code} needs a written reason.");
            }
            if (ctx.Reason != null && ctx.Reason.Trim().Length > 500)
            {
                throw Invalid("The reason may be at most 500 characters.");
            }

            switch (ctx.Initiator)
            {
                case CurrencyInitiator.System when string.IsNullOrWhiteSpace(ctx.InitiatorComponent):
                    throw Invalid("A system posting must name its component (e.g. SalaryService).");
                case CurrencyInitiator.Player or CurrencyInitiator.Admin when ctx.InitiatorUserId is not > 0:
                    throw Invalid("A player or staff posting must name the acting user.");
                case var initiator when !Enum.IsDefined(initiator):
                    throw Invalid($"Unknown initiator {(int)initiator}.");
            }
            if (ctx.InitiatorComponent?.Length > 60) throw Invalid("The initiator component may be at most 60 characters.");
            if (ctx.SourceType?.Length > 40) throw Invalid("The source type may be at most 40 characters.");
            if (ctx.SourceRef?.Length > 64) throw Invalid("The source reference may be at most 64 characters.");
            if (ctx.CorrelationId?.Length > 64) throw Invalid("The correlation id may be at most 64 characters.");

            if (ctx.MetadataJson != null)
            {
                if (ctx.MetadataJson.Length > MaxMetadataLength)
                {
                    throw Invalid($"Metadata may be at most {MaxMetadataLength} characters.");
                }
                try
                {
                    using var _ = JsonDocument.Parse(ctx.MetadataJson);
                }
                catch (JsonException)
                {
                    throw Invalid("Metadata must be valid JSON.");
                }
            }
        }

        private static string? ReversalMetadata(CurrencyTransaction original, List<object> shortfall, string? callerMetadata)
        {
            if (shortfall.Count == 0)
            {
                return callerMetadata;
            }
            var node = new JsonObject
            {
                ["reversedPublicId"] = original.PublicId,
                ["partial"] = true,
                ["shortfall"] = JsonSerializer.SerializeToNode(shortfall)
            };
            if (callerMetadata != null)
            {
                node["callerMetadata"] = JsonNode.Parse(callerMetadata);
            }
            return node.ToJsonString();
        }

        private static string Sha256(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

        private static CurrencyException Invalid(string message) => new(CurrencyErrorCode.InvalidRequest, message);
    }
}
