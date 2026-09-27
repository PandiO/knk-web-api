using System.Text.Json;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Staff currency tooling (currency-payments IMPLEMENTATION_PLAN.md Phase 4); see
    /// <see cref="ICurrencyAdminService"/>. Reversals post through ICurrencyService.ReverseAsync,
    /// so the ledger's locking, idempotency and never-below-zero rules apply unchanged.
    /// </summary>
    public class CurrencyAdminService : ICurrencyAdminService
    {
        /// <summary>Longest transfer-lock reason (users.TransferLockReason).</summary>
        public const int MaxLockReasonLength = 200;

        private readonly ICurrencyService _currency;
        private readonly ICurrencyRepository _repo;
        private readonly IUserRepository _users;
        private readonly IAuditLogService _auditLog;
        private readonly ITitleProgressionService _titleProgression;
        private readonly IPlayerNotificationQueue? _notifications;

        public CurrencyAdminService(
            ICurrencyService currency,
            ICurrencyRepository repo,
            IUserRepository users,
            IAuditLogService auditLog,
            ITitleProgressionService titleProgression,
            IPlayerNotificationQueue? notifications = null)
        {
            _currency = currency;
            _repo = repo;
            _users = users;
            _auditLog = auditLog;
            _titleProgression = titleProgression;
            _notifications = notifications;
        }

        /// <summary>The idempotency key of the reversal of transaction <paramref name="transactionId"/>.</summary>
        public static string ReversalKey(long transactionId) => $"reverse:{transactionId}";

        // ===== Transactions =====

        public async Task<CurrencyTransactionDetailDto> GetTransactionAsync(string publicId, CancellationToken ct = default)
        {
            var tx = await FindAsync(publicId, ct);
            var reversal = await _repo.FindReversalOfAsync(tx.Id, ct);
            var reverses = tx.ReversesTransactionId.HasValue ? await _repo.FindByIdAsync(tx.ReversesTransactionId.Value, ct) : null;

            var userIds = tx.Entries.Where(e => e.UserId.HasValue).Select(e => e.UserId!.Value).ToList();
            if (tx.InitiatorUserId.HasValue)
            {
                userIds.Add(tx.InitiatorUserId.Value);
            }
            if (reversal?.InitiatorUserId != null)
            {
                userIds.Add(reversal.InitiatorUserId.Value);
            }
            var names = await _repo.GetIdentitiesAsync(userIds, ct);
            string? NameOf(int? id) => id.HasValue && names.TryGetValue(id.Value, out var who) ? who.Username : null;

            return new CurrencyTransactionDetailDto
            {
                TransactionId = tx.Id,
                PublicId = tx.PublicId,
                CreatedAt = tx.CreatedAt,
                Kind = tx.Kind.ToString(),
                ReasonCode = tx.ReasonCode,
                Reason = tx.Reason,
                SourceType = tx.SourceType,
                SourceRef = tx.SourceRef,
                Initiator = tx.Initiator.ToString(),
                InitiatorUserId = tx.InitiatorUserId,
                InitiatorUsername = NameOf(tx.InitiatorUserId),
                InitiatorComponent = tx.InitiatorComponent,
                IdempotencyScope = tx.IdempotencyScope,
                CorrelationId = tx.CorrelationId,
                MetadataJson = tx.MetadataJson,
                ReversesPublicId = reverses?.PublicId,
                ReversedByPublicId = reversal?.PublicId,
                ReversedAt = reversal?.CreatedAt,
                ReversedByUserId = reversal?.InitiatorUserId,
                ReversedByUsername = NameOf(reversal?.InitiatorUserId),
                Reversible = tx.Kind != CurrencyTransactionKind.Reversal && reversal == null,
                Entries = tx.Entries
                    .OrderBy(e => e.AccountKind).ThenBy(e => e.UserId).ThenBy(e => e.Currency).ThenBy(e => e.Id)
                    .Select(e => new CurrencyEntryDetailDto
                    {
                        EntryId = e.Id,
                        Currency = e.Currency.ToString(),
                        AccountKind = e.AccountKind.ToString(),
                        UserId = e.UserId,
                        Username = NameOf(e.UserId),
                        SystemAccount = e.SystemAccount,
                        Operation = e.Operation.ToString(),
                        Amount = e.Amount,
                        BalanceBefore = e.BalanceBefore,
                        BalanceAfter = e.BalanceAfter
                    })
                    .ToList()
            };
        }

        public async Task<ReversalResultDto> ReverseAsync(string publicId, ReverseTransactionDto request, KnkCaller caller, string component,
            CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var original = await FindAsync(publicId, ct);
            // Already reversed: say so, with when and by whom - also for a retry of the request
            // that reversed it, which the ledger would otherwise replay as a fresh success.
            var prior = await _repo.FindReversalOfAsync(original.Id, ct);
            if (prior != null)
            {
                throw await AlreadyReversedAsync(original, prior, ct);
            }
            var note = RequireNote(request.Note);

            var ctx = CurrencyContext.ForCaller(caller, CurrencyReasons.Reversal, ReversalKey(original.Id), component,
                staffAction: true, reason: note) with
            {
                SourceType = "CurrencyTransaction",
                SourceRef = original.PublicId
            };

            var affected = original.Entries.Where(e => e.UserId.HasValue).Select(e => e.UserId!.Value).Distinct().ToList();
            PostingResult posting = null!;
            var titleChanges = new Dictionary<int, TitleChangeResultDto>();
            // The reversal, its audit entries and the title change of reversed XP commit together.
            try
            {
                await _users.RunWithUsersLockedAsync(affected, async () =>
                {
                    posting = await _currency.ReverseAsync(original.Id, new ReversalOptions(request.AllowPartial), ctx, ct);
                    if (posting.Replayed)
                    {
                        // The same request committed while this one waited for the lock.
                        throw new CurrencyException(CurrencyErrorCode.AlreadyReversed, $"Transaction {original.PublicId} was already reversed.");
                    }
                    titleChanges = await _titleProgression.ApplyForPostingAsync(posting, ctx.InitiatorUserId, ct);
                    foreach (var userId in affected)
                    {
                        await _auditLog.RecordAsync(ctx.InitiatorUserId, userId, AuditAction.CurrencyTransactionReversed, JsonSerializer.Serialize(new
                        {
                            reversedPublicId = original.PublicId,
                            reversedReasonCode = original.ReasonCode,
                            reversalPublicId = posting.PublicId,
                            allowPartial = request.AllowPartial,
                            reason = note,
                            changes = posting.Entries.Where(e => e.UserId == userId)
                                .Select(e => new { currency = e.Currency, amount = e.Amount, before = e.BalanceBefore, after = e.BalanceAfter })
                        }));
                    }
                });
            }
            catch (CurrencyException ex) when (ex.Code == CurrencyErrorCode.AlreadyReversed && ex.Details is not AlreadyReversedDetailsDto
                                               || ex.Code == CurrencyErrorCode.IdempotencyKeyReuse)
            {
                // Lost a race to another reversal (the same request, or one with the other
                // allowPartial under the same reverse: key): report the one that won, as above.
                var winner = await _repo.FindReversalOfAsync(original.Id, ct);
                if (winner == null) throw;
                throw await AlreadyReversedAsync(original, winner, ct);
            }

            foreach (var (userId, change) in titleChanges)
            {
                var who = (await _repo.GetIdentitiesAsync(new[] { userId }, ct)).GetValueOrDefault(userId);
                _notifications?.Enqueue(userId, who.Uuid, who.Username ?? "", PlayerNotificationTypes.TitleChanged, change);
            }

            var originalTotal = original.Entries.Where(e => e.AccountKind == CurrencyAccountKind.User).Sum(e => Math.Abs(e.Amount));
            var reversedTotal = posting.Entries.Sum(e => Math.Abs(e.Amount));
            return new ReversalResultDto
            {
                ReversedPublicId = original.PublicId,
                Posting = posting,
                Partial = reversedTotal < originalTotal
            };
        }

        /// <summary>409 AlreadyReversed with the reversal that exists (AlreadyReversedDetailsDto).</summary>
        private async Task<CurrencyException> AlreadyReversedAsync(CurrencyTransaction original, CurrencyTransaction reversal, CancellationToken ct)
        {
            string? by = null;
            if (reversal.InitiatorUserId.HasValue)
            {
                by = (await _repo.GetIdentitiesAsync(new[] { reversal.InitiatorUserId.Value }, ct))
                    .GetValueOrDefault(reversal.InitiatorUserId.Value).Username;
            }
            var who = by ?? reversal.InitiatorComponent ?? reversal.Initiator.ToString();
            return new CurrencyException(CurrencyErrorCode.AlreadyReversed,
                $"Transaction {original.PublicId} was already reversed on {reversal.CreatedAt:yyyy-MM-dd HH:mm} UTC by {who} (reversal {reversal.PublicId}).",
                new AlreadyReversedDetailsDto
                {
                    ReversalTransactionPublicId = reversal.PublicId,
                    ReversedAt = DateTime.SpecifyKind(reversal.CreatedAt, DateTimeKind.Utc),
                    ReversedByUserId = reversal.InitiatorUserId,
                    ReversedByUsername = by
                });
        }

        private async Task<CurrencyTransaction> FindAsync(string publicId, CancellationToken ct)
        {
            var id = (publicId ?? "").Replace(" ", "").ToUpperInvariant();
            if (id.StartsWith("TX", StringComparison.Ordinal) && id.Length == 28)
            {
                id = id[2..]; // "TX01J…" as shown in-game
            }
            CurrencyTransaction? tx = null;
            if (id.Length == 26 && id.All(char.IsAsciiLetterOrDigit))
            {
                tx = await _repo.FindByPublicIdAsync(id, ct);
            }
            return tx ?? throw new CurrencyException(CurrencyErrorCode.TransactionNotFound, $"There is no transaction {publicId}.");
        }

        private static string RequireNote(string? note)
        {
            var trimmed = note?.Trim() ?? "";
            if (trimmed.Length < AdminAdjustmentCategories.MinNoteLength)
            {
                throw new ArgumentException($"Say why in at least {AdminAdjustmentCategories.MinNoteLength} characters.", nameof(note));
            }
            if (trimmed.Length > AdminAdjustmentCategories.MaxNoteLength)
            {
                throw new ArgumentException($"The note may be at most {AdminAdjustmentCategories.MaxNoteLength} characters.", nameof(note));
            }
            return trimmed;
        }

        // ===== Transfer locks =====

        public async Task<TransferLockDto> GetTransferLockAsync(int userId, CancellationToken ct = default) =>
            ToLockDto(await RequireUserAsync(userId));

        public async Task<TransferLockDto> SetTransferLockAsync(int userId, string reason, int? actorUserId, CancellationToken ct = default)
        {
            var trimmed = reason?.Trim() ?? "";
            if (trimmed.Length == 0)
            {
                throw new ArgumentException("A reason is required to lock a player's transfers.", nameof(reason));
            }
            if (trimmed.Length > MaxLockReasonLength)
            {
                throw new ArgumentException($"The reason may be at most {MaxLockReasonLength} characters.", nameof(reason));
            }
            var user = await RequireUserAsync(userId);
            if (user.TransferLockReason == trimmed)
            {
                return ToLockDto(user); // repeated request
            }
            var previous = user.TransferLockReason;
            user.TransferLockReason = trimmed;
            user.TransferLockedAt ??= DateTime.UtcNow;
            await _users.UpdateUserAsync(user);
            await _auditLog.RecordAsync(actorUserId, userId, AuditAction.CurrencyTransferLocked,
                JsonSerializer.Serialize(new { reason = trimmed, previousReason = previous }));
            return ToLockDto(user);
        }

        public async Task<TransferLockDto> ClearTransferLockAsync(int userId, int? actorUserId, CancellationToken ct = default)
        {
            var user = await RequireUserAsync(userId);
            if (user.TransferLockReason == null)
            {
                return ToLockDto(user);
            }
            var previous = user.TransferLockReason;
            user.TransferLockReason = null;
            user.TransferLockedAt = null;
            await _users.UpdateUserAsync(user);
            await _auditLog.RecordAsync(actorUserId, userId, AuditAction.CurrencyTransferUnlocked,
                JsonSerializer.Serialize(new { previousReason = previous }));
            return ToLockDto(user);
        }

        private async Task<User> RequireUserAsync(int userId)
        {
            if (userId <= 0) throw new ArgumentException("Invalid user id.", nameof(userId));
            return await _users.GetByIdAsync(userId) ?? throw new KeyNotFoundException($"User with ID {userId} not found.");
        }

        private static TransferLockDto ToLockDto(User user) => new()
        {
            UserId = user.Id,
            Username = user.Username,
            Locked = user.TransferLockReason != null,
            Reason = user.TransferLockReason,
            LockedAt = user.TransferLockedAt
        };

        // ===== Policy =====

        public async Task<List<CurrencyPolicyDto>> GetPoliciesAsync(CancellationToken ct = default) =>
            (await _repo.GetPoliciesAsync(ct)).Values.OrderBy(p => p.Currency).Select(ToPolicyDto).ToList();

        public async Task<CurrencyPolicyDto> UpdatePolicyAsync(Currency currency, CurrencyPolicyDto request, int? actorUserId, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var policy = await _repo.GetPolicyForUpdateAsync(currency, ct)
                ?? throw new KeyNotFoundException($"There is no {currency} policy.");
            // Optimistic concurrency: the edit must be based on the row as it is now. A form loaded
            // before the R1 kill switch (or another staff member's save) would otherwise undo it.
            if (request.UpdatedAt == default)
            {
                throw new ArgumentException("updatedAt (the version of the policy you loaded) is required.", nameof(request));
            }
            if (!policy.IsVersion(request.UpdatedAt))
            {
                throw new CurrencyException(CurrencyErrorCode.PolicyChanged,
                    $"The {currency} policy was changed since you loaded it{(policy.UpdatedByUserId == null ? " (possibly by an automatic safety shut-off)" : "")}. Reload it and review before saving again.",
                    ToPolicyDto(policy));
            }
            ValidatePolicy(currency, request);
            if (request.MinSenderTitleBracketId.HasValue && await _repo.GetTitleBracketAsync(request.MinSenderTitleBracketId.Value, ct) == null)
            {
                throw new ArgumentException($"Title bracket {request.MinSenderTitleBracketId} doesn't exist.", nameof(request));
            }

            var before = ToPolicyDto(policy);
            policy.TransfersEnabled = request.TransfersEnabled;
            policy.Transferable = request.Transferable;
            policy.MinTransfer = request.MinTransfer;
            policy.MaxTransfer = request.MaxTransfer;
            policy.DailySendCap = request.DailySendCap;
            policy.DailyReceiveCap = request.DailyReceiveCap;
            policy.ConfirmThreshold = request.ConfirmThreshold;
            policy.ConfirmTtlSeconds = request.ConfirmTtlSeconds;
            policy.CooldownSeconds = request.CooldownSeconds;
            policy.MaxTransfersPerHour = request.MaxTransfersPerHour;
            policy.MinSenderAccountAgeHours = request.MinSenderAccountAgeHours;
            policy.MinSenderTitleBracketId = request.MinSenderTitleBracketId;
            policy.TransferFeeBasisPoints = request.TransferFeeBasisPoints;
            policy.MaxBalance = request.MaxBalance;
            policy.AdminDailyGrantCapPerActor = request.AdminDailyGrantCapPerActor;
            policy.SignupGrant = request.SignupGrant;
            var after = ToPolicyDto(policy);

            var changes = PolicyChanges(before, after);
            if (changes.Count == 0)
            {
                return before;
            }
            policy.UpdatedAt = CurrencyPolicy.VersionStamp(DateTime.UtcNow);
            policy.UpdatedByUserId = actorUserId;
            await _repo.SavePolicyAsync(ct);

            // A server-wide setting has no target player: the entry is filed under the staff member.
            if (actorUserId is > 0)
            {
                await _auditLog.RecordAsync(actorUserId, actorUserId.Value, AuditAction.CurrencyPolicyChanged,
                    JsonSerializer.Serialize(new { currency = currency.ToString(), changes }));
            }
            return ToPolicyDto(policy);
        }

        private static void ValidatePolicy(Currency currency, CurrencyPolicyDto p)
        {
            var cap = CurrencyService.Cap(currency);
            void Require(bool ok, string message)
            {
                if (!ok) throw new ArgumentException(message);
            }
            Require(p.MinTransfer >= 1 && p.MinTransfer <= cap, $"The minimum transfer must be 1–{cap:N0}.");
            Require(p.MaxTransfer >= p.MinTransfer && p.MaxTransfer <= cap, $"The maximum transfer must be between the minimum and {cap:N0}.");
            Require(p.DailySendCap >= 0 && p.DailyReceiveCap >= 0, "Daily caps can't be negative (0 = no cap).");
            Require(p.ConfirmThreshold >= 1, "The confirmation threshold must be at least 1.");
            Require(p.ConfirmTtlSeconds is >= 10 and <= 600, "The confirmation window must be 10–600 seconds.");
            Require(p.CooldownSeconds is >= 0 and <= 86_400, "The cooldown must be 0–86,400 seconds.");
            Require(p.MaxTransfersPerHour >= 0, "Transfers per hour can't be negative (0 = no limit).");
            Require(p.MinSenderAccountAgeHours is >= 0 and <= 8_760, "The minimum account age must be 0–8,760 hours.");
            Require(p.TransferFeeBasisPoints is >= 0 and <= 5_000, "The transfer fee must be 0–5,000 basis points (0–50 %).");
            Require(p.MaxBalance >= 1 && p.MaxBalance <= cap, $"The maximum balance must be 1–{cap:N0} (the database cap).");
            Require(p.AdminDailyGrantCapPerActor >= 0, "The staff grant cap can't be negative (0 = no cap).");
            Require(p.SignupGrant >= 0 && p.SignupGrant <= p.MaxBalance, "The signup grant must be between 0 and the maximum balance.");
        }

        private static Dictionary<string, object?> PolicyChanges(CurrencyPolicyDto before, CurrencyPolicyDto after)
        {
            var changes = new Dictionary<string, object?>();
            void Diff<T>(string name, T from, T to)
            {
                if (!EqualityComparer<T>.Default.Equals(from, to)) changes[name] = new { from, to };
            }
            Diff("transfersEnabled", before.TransfersEnabled, after.TransfersEnabled);
            Diff("transferable", before.Transferable, after.Transferable);
            Diff("minTransfer", before.MinTransfer, after.MinTransfer);
            Diff("maxTransfer", before.MaxTransfer, after.MaxTransfer);
            Diff("dailySendCap", before.DailySendCap, after.DailySendCap);
            Diff("dailyReceiveCap", before.DailyReceiveCap, after.DailyReceiveCap);
            Diff("confirmThreshold", before.ConfirmThreshold, after.ConfirmThreshold);
            Diff("confirmTtlSeconds", before.ConfirmTtlSeconds, after.ConfirmTtlSeconds);
            Diff("cooldownSeconds", before.CooldownSeconds, after.CooldownSeconds);
            Diff("maxTransfersPerHour", before.MaxTransfersPerHour, after.MaxTransfersPerHour);
            Diff("minSenderAccountAgeHours", before.MinSenderAccountAgeHours, after.MinSenderAccountAgeHours);
            Diff("minSenderTitleBracketId", before.MinSenderTitleBracketId, after.MinSenderTitleBracketId);
            Diff("transferFeeBasisPoints", before.TransferFeeBasisPoints, after.TransferFeeBasisPoints);
            Diff("maxBalance", before.MaxBalance, after.MaxBalance);
            Diff("adminDailyGrantCapPerActor", before.AdminDailyGrantCapPerActor, after.AdminDailyGrantCapPerActor);
            Diff("signupGrant", before.SignupGrant, after.SignupGrant);
            return changes;
        }

        private static CurrencyPolicyDto ToPolicyDto(CurrencyPolicy p) => new()
        {
            Currency = p.Currency.ToString(),
            TransfersEnabled = p.TransfersEnabled,
            Transferable = p.Transferable,
            MinTransfer = p.MinTransfer,
            MaxTransfer = p.MaxTransfer,
            DailySendCap = p.DailySendCap,
            DailyReceiveCap = p.DailyReceiveCap,
            ConfirmThreshold = p.ConfirmThreshold,
            ConfirmTtlSeconds = p.ConfirmTtlSeconds,
            CooldownSeconds = p.CooldownSeconds,
            MaxTransfersPerHour = p.MaxTransfersPerHour,
            MinSenderAccountAgeHours = p.MinSenderAccountAgeHours,
            MinSenderTitleBracketId = p.MinSenderTitleBracketId,
            TransferFeeBasisPoints = p.TransferFeeBasisPoints,
            MaxBalance = p.MaxBalance,
            AdminDailyGrantCapPerActor = p.AdminDailyGrantCapPerActor,
            SignupGrant = p.SignupGrant,
            UpdatedAt = p.UpdatedAt,
            UpdatedByUserId = p.UpdatedByUserId,
            HardMaxBalance = CurrencyService.Cap(p.Currency)
        };
    }
}
