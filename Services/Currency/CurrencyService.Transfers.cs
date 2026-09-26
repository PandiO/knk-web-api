using System.Text.Json;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Player transfers (currency DESIGN.md §3.4–§3.6, IMPLEMENTATION_PLAN.md Phase 3). See
    /// <see cref="ICurrencyTransferService"/> for the contract and
    /// <see cref="TransferPolicyEvaluator"/> for the rules.
    /// <para>
    /// A transfer is one PLAYER_TRANSFER posting with a debit leg on the sender and a credit leg
    /// on the recipient (plus a sender → SYS_FEES leg pair when the policy has a fee), posted by
    /// the same ExecuteAsync as every other posting: both rows locked in ascending id order, the
    /// idempotency lookup, the rules — including the daily sums read from the ledger — and the
    /// write, all inside one transaction under those locks.
    /// </para>
    /// </summary>
    public partial class CurrencyService : ICurrencyTransferService
    {
        /// <summary>Holders (exact grant, directly or through a group) are left off the leaderboard.</summary>
        public const string BaltopExemptNode = "knk.baltop.exempt";

        public const int MaxLeaderboardPageSize = 50;

        /// <summary>Source type on a transfer posted by confirming a pending one.</summary>
        public const string PendingTransferSourceType = "PendingTransfer";

        public async Task<TransferResultDto> TransferAsync(TransferRequest request, CurrencyContext ctx, CancellationToken ct = default)
        {
            ValidateTransferRequest(request);
            RequireTransferContext(ctx, request.SenderUserId);
            var policy = await PolicyAsync(request.Currency, ct);
            TransferPolicyEvaluator.CheckStatic(policy, request.Currency, request.Amount, request.BypassLimits);

            var canonical = TransferCanonical(request);

            // A retry of a transfer that already went through.
            var posted = await _repo.FindByIdempotencyAsync(ctx.IdempotencyScope, ctx.IdempotencyKey, ct);
            if (posted != null)
            {
                var replay = await ReplayAsync(posted, Sha256(canonical), ctx, ct);
                return await CompletedAsync(replay, request.SenderUserId, request.RecipientUserId, request.Currency, request.Amount, ct);
            }

            // A retry of a transfer that is (or was) waiting for confirmation.
            var pendingKey = PendingKey(ctx);
            var existingPending = await _repo.FindPendingByKeyAsync(pendingKey, ct);
            if (existingPending != null)
            {
                return await ExistingPendingOutcomeAsync(existingPending, request, ct);
            }

            await RequireTransferPartiesAsync(request, ct);
            var fee = TransferPolicyEvaluator.Fee(policy, request.Amount);
            if (policy!.ConfirmThreshold > 0 && request.Amount >= policy.ConfirmThreshold)
            {
                return await CreatePendingAsync(request, policy, fee, pendingKey, ct);
            }

            return await PostTransferAsync(request, policy, fee, ctx with { Reason = request.Note }, canonical, confirming: null, ct);
        }

        public async Task<TransferResultDto> ConfirmTransferAsync(string pendingPublicId, int senderUserId, CurrencyContext ctx,
            bool bypassLimits = false, CancellationToken ct = default)
        {
            RequireTransferContext(ctx, senderUserId);
            var pending = await FindSendersPendingAsync(pendingPublicId, senderUserId, ct);
            if (pending.Status == CurrencyPendingTransferStatus.Confirmed && pending.ResultTransactionId is long confirmedId)
            {
                return await ConfirmedReplayAsync(pending, confirmedId, ct);
            }

            var now = DateTime.UtcNow;
            await ExpireIfDueAsync(pending, now, ct);
            RequireConfirmable(pending, now);

            var request = new TransferRequest(pending.SenderUserId, pending.RecipientUserId, pending.Currency, pending.Amount,
                pending.Note, bypassLimits);
            var policy = await PolicyAsync(request.Currency, ct);
            TransferPolicyEvaluator.CheckStatic(policy, request.Currency, request.Amount, bypassLimits);
            await RequireTransferPartiesAsync(request, ct);
            var fee = TransferPolicyEvaluator.Fee(policy, request.Amount);

            var confirmCtx = ctx with
            {
                IdempotencyKey = ConfirmKey(pending.PublicId),
                Reason = pending.Note,
                SourceType = PendingTransferSourceType,
                SourceRef = pending.PublicId
            };
            return await PostTransferAsync(request, policy!, fee, confirmCtx, $"transfer-confirm|{pending.PublicId}", pending, ct);
        }

        public async Task<PendingTransferDto> CancelTransferAsync(string pendingPublicId, int senderUserId, CancellationToken ct = default)
        {
            var pending = await FindSendersPendingAsync(pendingPublicId, senderUserId, ct);
            await _users.RunWithUsersLockedAsync(new[] { senderUserId }, async () =>
            {
                await _repo.ReloadPendingAsync(pending, ct);
                switch (pending.Status)
                {
                    case CurrencyPendingTransferStatus.Confirmed:
                        throw new CurrencyException(CurrencyErrorCode.PendingTransferClosed,
                            "That payment was already confirmed.", new { status = pending.Status.ToString() });
                    case CurrencyPendingTransferStatus.Pending:
                        pending.Status = pending.ExpiresAt <= DateTime.UtcNow
                            ? CurrencyPendingTransferStatus.Expired
                            : CurrencyPendingTransferStatus.Cancelled;
                        await _repo.SavePendingChangesAsync(ct);
                        break;
                    // Cancelled or Expired already: nothing to do, same answer.
                }
            });
            var names = await _repo.GetIdentitiesAsync(new[] { pending.RecipientUserId }, ct);
            return ToPendingDto(pending, names, DateTime.UtcNow);
        }

        public async Task<TransferLimitsDto> GetTransferLimitsAsync(int userId, Currency currency, CancellationToken ct = default)
        {
            if (currency is not (Currency.Coins or Currency.Gems))
            {
                throw new CurrencyException(CurrencyErrorCode.NotTransferable, "Only coins and gems have transfer limits.");
            }
            var user = await _users.GetByIdAsync(userId)
                ?? throw new CurrencyException(CurrencyErrorCode.UserNotFound, $"User {userId} doesn't exist.");
            var policy = await PolicyAsync(currency, ct);
            var now = DateTime.UtcNow;
            var limits = new TransferLimitsDto
            {
                UserId = userId,
                Currency = currency.ToString(),
                Locked = user.IsFrozen || user.TransferLockReason != null || !user.IsActive || user.DeletedAt != null
            };
            if (policy == null)
            {
                return limits;
            }

            var sent = await _repo.GetTransfersSentSinceAsync(userId, currency, now.AddHours(-24), ct);
            var sentTotal = sent.Sum(t => t.Amount);
            var title = policy.MinSenderTitleBracketId is int bracketId ? await _repo.GetTitleBracketAsync(bracketId, ct) : null;
            var eligibleFrom = TransferPolicyEvaluator.EligibleFrom(policy, user);

            limits.Transferable = policy.TransfersEnabled && policy.Transferable;
            limits.MinTransfer = Math.Max(1, policy.MinTransfer);
            limits.MaxTransfer = policy.MaxTransfer > 0 ? Math.Min(policy.MaxTransfer, Cap(currency)) : Cap(currency);
            limits.DailySendCap = policy.DailySendCap;
            limits.SentLast24h = sentTotal;
            limits.RemainingToday = policy.DailySendCap > 0 ? Math.Max(0, policy.DailySendCap - sentTotal) : limits.MaxTransfer;
            limits.ConfirmThreshold = policy.ConfirmThreshold;
            limits.TransferFeeBasisPoints = policy.TransferFeeBasisPoints;
            limits.NextTransferAt = TransferPolicyEvaluator.NextTransferAt(policy, await _repo.LastTransferAtAsync(userId, ct), sent, now);
            limits.MinSenderAccountAgeHours = policy.MinSenderAccountAgeHours;
            limits.EligibleFrom = eligibleFrom > now ? eligibleFrom : null;
            limits.RequiredTitleName = title?.MaleName;
            limits.RequiredExperience = title?.MinExperience;
            limits.Eligible = limits.EligibleFrom == null && (title == null || user.ExperiencePoints >= title.MinExperience);
            return limits;
        }

        public async Task<LeaderboardDto> GetLeaderboardAsync(Currency currency, int page, int pageSize, CancellationToken ct = default)
        {
            if (currency is not (Currency.Coins or Currency.Gems))
            {
                throw Invalid("The leaderboard ranks coins or gems.");
            }
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, MaxLeaderboardPageSize);
            var (total, entries) = await _repo.GetLeaderboardAsync(currency, BaltopExemptNode, (page - 1) * pageSize, pageSize, ct);
            return new LeaderboardDto
            {
                Currency = currency.ToString(),
                Page = page,
                PageSize = pageSize,
                TotalCount = total,
                Entries = entries,
                GeneratedAt = DateTime.UtcNow
            };
        }

        // ===== Posting =====

        private async Task<TransferResultDto> PostTransferAsync(TransferRequest request, CurrencyPolicy policy, long fee,
            CurrencyContext ctx, string canonical, CurrencyPendingTransfer? confirming, CancellationToken ct)
        {
            var reason = CurrencyReasons.Find(CurrencyReasons.PlayerTransfer)!;
            var ids = new[] { request.SenderUserId, request.RecipientUserId };

            var posting = await ExecuteAsync(ctx, reason, ids, canonical, async (users, token) =>
            {
                if (confirming != null)
                {
                    // A cancel may have landed while this confirm waited for the sender's lock.
                    await _repo.ReloadPendingAsync(confirming, token);
                    RequireConfirmable(confirming, DateTime.UtcNow);
                }

                var sender = users[request.SenderUserId];
                var recipient = users[request.RecipientUserId];
                await CheckPolicyAsync(policy, request, fee, sender, recipient, token);

                var plan = new EntryPlan();
                plan.AddUserLeg(sender, request.Currency, -request.Amount, CurrencyOperation.Remove);
                plan.AddUserLeg(recipient, request.Currency, request.Amount, CurrencyOperation.Add);
                if (fee > 0)
                {
                    plan.AddUserLeg(sender, request.Currency, -fee, CurrencyOperation.Remove);
                    plan.AddSystemLeg(CurrencyReasons.SysFees, request.Currency, fee);
                }
                var draft = plan.ToDraft();
                draft.FromUserId = sender.Id;
                draft.ToUserId = recipient.Id;
                if (fee > 0 || confirming != null)
                {
                    draft.MetadataJson = JsonSerializer.Serialize(new { fee, feeBasisPoints = policy.TransferFeeBasisPoints, pendingTransferId = confirming?.PublicId });
                }
                return draft;
            }, ct, confirming == null ? null : async tx =>
            {
                confirming.Status = CurrencyPendingTransferStatus.Confirmed;
                confirming.ResultTransactionId = tx.Id;
                await _repo.SavePendingChangesAsync(ct);
            });

            return await CompletedAsync(posting, request.SenderUserId, request.RecipientUserId, request.Currency, request.Amount, ct);
        }

        private async Task<TransferResultDto> CreatePendingAsync(TransferRequest request, CurrencyPolicy policy, long fee,
            string pendingKey, CancellationToken ct)
        {
            CurrencyPendingTransfer? pending = null;
            var created = false;
            var ids = new[] { request.SenderUserId, request.RecipientUserId };
            try
            {
                await _users.RunWithUsersLockedAsync(ids, async () =>
                {
                    // Two identical first attempts serialize on the sender's lock; the second finds the first.
                    pending = await _repo.FindPendingByKeyAsync(pendingKey, ct);
                    if (pending != null)
                    {
                        return;
                    }

                    var users = await _repo.GetUsersForUpdateAsync(ids, ct);
                    var sender = users[request.SenderUserId];
                    var recipient = users[request.RecipientUserId];
                    await CheckPolicyAsync(policy, request, fee, sender, recipient, ct);

                    var now = DateTime.UtcNow;
                    // The latest /pay prompt wins: the sender's older open prompts can no longer be confirmed.
                    foreach (var open in await _repo.GetOpenPendingForSenderAsync(request.SenderUserId, ct))
                    {
                        open.Status = open.ExpiresAt <= now ? CurrencyPendingTransferStatus.Expired : CurrencyPendingTransferStatus.Cancelled;
                    }
                    pending = new CurrencyPendingTransfer
                    {
                        PublicId = CurrencyIds.NewPublicId(),
                        SenderUserId = request.SenderUserId,
                        RecipientUserId = request.RecipientUserId,
                        Currency = request.Currency,
                        Amount = request.Amount,
                        Note = request.Note,
                        IdempotencyKey = pendingKey,
                        Status = CurrencyPendingTransferStatus.Pending,
                        CreatedAt = now,
                        ExpiresAt = now.AddSeconds(Math.Max(10, policy.ConfirmTtlSeconds))
                    };
                    await _repo.AddPendingAsync(pending, ct);
                    created = true;
                });
            }
            catch (DbUpdateException ex) when (_repo.IsUniqueViolation(ex))
            {
                if (pending != null && !created)
                {
                    _repo.DiscardPending(pending);
                }
                var winner = await _repo.FindPendingByKeyAsync(pendingKey, ct);
                if (winner == null)
                {
                    throw;
                }
                return await ExistingPendingOutcomeAsync(winner, request, ct);
            }

            if (!created)
            {
                return await ExistingPendingOutcomeAsync(pending!, request, ct);
            }
            _logger.LogInformation("Pending transfer {PublicId}: {Amount} {Currency} from {Sender} to {Recipient}, expires {ExpiresAt:O}",
                pending!.PublicId, pending.Amount, pending.Currency, pending.SenderUserId, pending.RecipientUserId, pending.ExpiresAt);
            return await PendingResultAsync(pending, ct);
        }

        /// <summary>Reads everything the rules need (under the caller's locks) and applies them.</summary>
        private async Task CheckPolicyAsync(CurrencyPolicy policy, TransferRequest request, long fee, User sender, User recipient, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var since = now.AddHours(-24);
            var input = new TransferPolicyInput
            {
                Policy = policy,
                Currency = request.Currency,
                Amount = request.Amount,
                Fee = fee,
                Sender = sender,
                Recipient = recipient,
                Now = now,
                SentLast24h = await _repo.GetTransfersSentSinceAsync(sender.Id, request.Currency, since, ct),
                RecipientReceivedLast24h = await _repo.SumReceivedSinceAsync(recipient.Id, request.Currency, since, ct),
                SenderLastTransferAt = await _repo.LastTransferAtAsync(sender.Id, ct),
                RequiredTitle = policy.MinSenderTitleBracketId is int bracketId ? await _repo.GetTitleBracketAsync(bracketId, ct) : null,
                BypassLimits = request.BypassLimits
            };
            try
            {
                TransferPolicyEvaluator.Check(input);
            }
            catch (CurrencyException ex)
            {
                // Denials are the input of the probing alert (DESIGN.md §3.9 R9, Phase 5).
                _logger.LogInformation("Transfer refused ({Code}): {Amount} {Currency} from {Sender} to {Recipient}",
                    ex.Code, request.Amount, request.Currency, request.SenderUserId, request.RecipientUserId);
                throw;
            }
        }

        // ===== Results =====

        private async Task<TransferResultDto> CompletedAsync(PostingResult posting, int senderUserId, int recipientUserId,
            Currency currency, long amount, CancellationToken ct)
        {
            var names = await _repo.GetIdentitiesAsync(new[] { senderUserId, recipientUserId }, ct);
            var currencyName = currency.ToString();
            var senderDebit = -posting.Entries.Where(e => e.UserId == senderUserId && e.Currency == currencyName).Sum(e => e.Amount);
            var recipientLeg = posting.Entries.LastOrDefault(e => e.UserId == recipientUserId && e.Currency == currencyName);
            return new TransferResultDto
            {
                Status = TransferResultDto.StatusCompleted,
                TransactionId = posting.TransactionId,
                PublicId = posting.PublicId,
                Replayed = posting.Replayed,
                Currency = currencyName,
                Amount = amount,
                Fee = Math.Max(0, senderDebit - amount),
                SenderUserId = senderUserId,
                SenderUsername = names.TryGetValue(senderUserId, out var s) ? s.Username : null,
                RecipientUserId = recipientUserId,
                RecipientUsername = names.TryGetValue(recipientUserId, out var r) ? r.Username : null,
                RecipientUuid = names.TryGetValue(recipientUserId, out var r2) ? r2.Uuid : null,
                RecipientBalanceAfter = recipientLeg?.BalanceAfter ?? 0,
                SenderBalances = posting.Balances.TryGetValue(senderUserId, out var balances) ? balances : null,
                CreatedAt = posting.CreatedAt
            };
        }

        private async Task<TransferResultDto> PendingResultAsync(CurrencyPendingTransfer pending, CancellationToken ct)
        {
            var names = await _repo.GetIdentitiesAsync(new[] { pending.SenderUserId, pending.RecipientUserId }, ct);
            var balances = await _repo.GetBalancesAsync(new[] { pending.SenderUserId }, ct);
            return new TransferResultDto
            {
                Status = TransferResultDto.StatusPendingConfirmation,
                Currency = pending.Currency.ToString(),
                Amount = pending.Amount,
                Fee = TransferPolicyEvaluator.Fee((await PolicyAsync(pending.Currency, ct)), pending.Amount),
                SenderUserId = pending.SenderUserId,
                SenderUsername = names.TryGetValue(pending.SenderUserId, out var s) ? s.Username : null,
                RecipientUserId = pending.RecipientUserId,
                RecipientUsername = names.TryGetValue(pending.RecipientUserId, out var r) ? r.Username : null,
                SenderBalances = balances.TryGetValue(pending.SenderUserId, out var b) ? b : null,
                Pending = ToPendingDto(pending, names, DateTime.UtcNow),
                CreatedAt = pending.CreatedAt
            };
        }

        /// <summary>A retry of a create request whose key already made a pending transfer.</summary>
        private async Task<TransferResultDto> ExistingPendingOutcomeAsync(CurrencyPendingTransfer pending, TransferRequest request, CancellationToken ct)
        {
            if (pending.SenderUserId != request.SenderUserId || pending.RecipientUserId != request.RecipientUserId
                || pending.Currency != request.Currency || pending.Amount != request.Amount
                || !string.Equals(pending.Note ?? "", request.Note ?? "", StringComparison.Ordinal))
            {
                throw new CurrencyException(CurrencyErrorCode.IdempotencyKeyReuse,
                    "This Idempotency-Key was already used for a different payment.", new { pendingTransferId = pending.PublicId });
            }
            if (pending.Status == CurrencyPendingTransferStatus.Confirmed && pending.ResultTransactionId is long confirmedId)
            {
                return await ConfirmedReplayAsync(pending, confirmedId, ct);
            }
            await ExpireIfDueAsync(pending, DateTime.UtcNow, ct);
            return await PendingResultAsync(pending, ct);
        }

        private async Task<TransferResultDto> ConfirmedReplayAsync(CurrencyPendingTransfer pending, long transactionId, CancellationToken ct)
        {
            var tx = await _repo.FindByIdAsync(transactionId, ct)
                ?? throw new CurrencyException(CurrencyErrorCode.TransactionNotFound, $"Transaction {transactionId} doesn't exist.");
            var balances = await _repo.GetBalancesAsync(new[] { pending.SenderUserId, pending.RecipientUserId }, ct);
            var result = await CompletedAsync(ToResult(tx, balances, replayed: true), pending.SenderUserId, pending.RecipientUserId,
                pending.Currency, pending.Amount, ct);
            return result;
        }

        private static PendingTransferDto ToPendingDto(CurrencyPendingTransfer pending, IReadOnlyDictionary<int, (string Username, string? Uuid)> names, DateTime now) => new()
        {
            PublicId = pending.PublicId,
            Status = pending.Status == CurrencyPendingTransferStatus.Pending && pending.ExpiresAt <= now
                ? CurrencyPendingTransferStatus.Expired.ToString()
                : pending.Status.ToString(),
            Currency = pending.Currency.ToString(),
            Amount = pending.Amount,
            RecipientUserId = pending.RecipientUserId,
            RecipientUsername = names.TryGetValue(pending.RecipientUserId, out var r) ? r.Username : null,
            CreatedAt = pending.CreatedAt,
            ExpiresAt = pending.ExpiresAt,
            ExpiresInSeconds = pending.Status == CurrencyPendingTransferStatus.Pending
                ? Math.Max(0, (int)Math.Ceiling((pending.ExpiresAt - now).TotalSeconds))
                : 0
        };

        // ===== Checks and helpers =====

        private static void ValidateTransferRequest(TransferRequest request)
        {
            if (request == null)
            {
                throw Invalid("A transfer needs a request.");
            }
            if (request.SenderUserId <= 0)
            {
                throw new CurrencyException(CurrencyErrorCode.UserNotFound, $"User {request.SenderUserId} doesn't exist.");
            }
            if (request.RecipientUserId <= 0)
            {
                throw new CurrencyException(CurrencyErrorCode.RecipientNotFound, "That player doesn't exist.");
            }
            if (request.SenderUserId == request.RecipientUserId)
            {
                throw new CurrencyException(CurrencyErrorCode.SelfTransfer, "You can't pay yourself.");
            }
            if (request.Note != null && request.Note.Length > TransferPolicyEvaluator.MaxNoteLength)
            {
                throw Invalid($"The note may be at most {TransferPolicyEvaluator.MaxNoteLength} characters.");
            }
        }

        /// <summary>A transfer is started by the sender themself: reason PLAYER_TRANSFER, initiator Player = sender.</summary>
        private static void RequireTransferContext(CurrencyContext ctx, int senderUserId)
        {
            var reason = RequireReason(ctx);
            if (reason.Code != CurrencyReasons.PlayerTransfer)
            {
                throw Invalid($"A player transfer needs reason code {CurrencyReasons.PlayerTransfer}, not {ctx.ReasonCode}.");
            }
            ValidateContext(ctx, reason);
            if (ctx.Initiator != CurrencyInitiator.Player || ctx.InitiatorUserId != senderUserId)
            {
                throw Invalid("A player transfer must be started by the sending player.");
            }
        }

        /// <summary>Sender and recipient exist (before any lock, for precise errors).</summary>
        private async Task RequireTransferPartiesAsync(TransferRequest request, CancellationToken ct)
        {
            var found = await _repo.GetIdentitiesAsync(new[] { request.SenderUserId, request.RecipientUserId }, ct);
            if (!found.ContainsKey(request.SenderUserId))
            {
                throw new CurrencyException(CurrencyErrorCode.UserNotFound, $"User {request.SenderUserId} doesn't exist.");
            }
            if (!found.ContainsKey(request.RecipientUserId))
            {
                throw new CurrencyException(CurrencyErrorCode.RecipientNotFound, "That player doesn't exist.");
            }
        }

        private async Task<CurrencyPendingTransfer> FindSendersPendingAsync(string publicId, int senderUserId, CancellationToken ct)
        {
            var pending = string.IsNullOrWhiteSpace(publicId) || publicId.Length > 26
                ? null
                : await _repo.FindPendingAsync(publicId.Trim().ToUpperInvariant(), ct);
            // Someone else's pending transfer is reported as missing, not as forbidden.
            if (pending == null || pending.SenderUserId != senderUserId)
            {
                throw new CurrencyException(CurrencyErrorCode.PendingTransferNotFound, "There is no such payment waiting for your confirmation.");
            }
            return pending;
        }

        private async Task ExpireIfDueAsync(CurrencyPendingTransfer pending, DateTime now, CancellationToken ct)
        {
            if (pending.Status == CurrencyPendingTransferStatus.Pending && pending.ExpiresAt <= now)
            {
                pending.Status = CurrencyPendingTransferStatus.Expired;
                await _repo.SavePendingChangesAsync(ct);
            }
        }

        private static void RequireConfirmable(CurrencyPendingTransfer pending, DateTime now)
        {
            if (pending.Status == CurrencyPendingTransferStatus.Expired
                || (pending.Status == CurrencyPendingTransferStatus.Pending && pending.ExpiresAt <= now))
            {
                throw new CurrencyException(CurrencyErrorCode.PendingTransferExpired,
                    "That payment request expired - send it again.", new { status = CurrencyPendingTransferStatus.Expired.ToString() });
            }
            if (pending.Status != CurrencyPendingTransferStatus.Pending)
            {
                throw new CurrencyException(CurrencyErrorCode.PendingTransferClosed,
                    $"That payment request was {pending.Status.ToString().ToLowerInvariant()}.", new { status = pending.Status.ToString() });
            }
        }

        private async Task<CurrencyPolicy?> PolicyAsync(Currency currency, CancellationToken ct)
        {
            var policies = await _repo.GetPoliciesAsync(ct);
            return policies.TryGetValue(currency, out var policy) ? policy : null;
        }

        /// <summary>The pending table's key: scope-qualified, since that table has one global unique index.</summary>
        private static string PendingKey(CurrencyContext ctx)
        {
            var key = ctx.IdempotencyScope + ":" + ctx.IdempotencyKey;
            if (key.Length > 100)
            {
                throw Invalid("The idempotency key is too long for a payment that needs confirmation.");
            }
            return key;
        }

        public static string ConfirmKey(string pendingPublicId) => "transfer-confirm:" + pendingPublicId;

        private static string TransferCanonical(TransferRequest request) =>
            $"transfer|{request.SenderUserId}|{request.RecipientUserId}|{(int)request.Currency}|{request.Amount}"
            + $"|bypass:{request.BypassLimits}|note:{Sha256(request.Note ?? "")}";
    }
}
