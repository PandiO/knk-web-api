using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.Privacy
{
    public enum PrivacyOutcome
    {
        Ok,
        NotFound,
        /// <summary>No such user, or the account is already erased/inactive.</summary>
        UserNotFound,
        /// <summary>The user already has an open request (returned).</summary>
        AlreadyPending,
        /// <summary>The request is not in a state that allows this (e.g. cancel of a completed request).</summary>
        NotPending,
        /// <summary>A player's own request needs an email address on the account.</summary>
        EmailRequired,
        /// <summary>The confirmation email could not be sent; the request stays unconfirmed (retry).</summary>
        EmailFailed,
        /// <summary>Unknown, used or expired confirmation link.</summary>
        InvalidToken,
        /// <summary>Execution before the end of the grace period.</summary>
        GracePeriod
    }

    public sealed record PrivacyResult(PrivacyOutcome Outcome, PrivacyDeletionRequestDto? Request = null);

    public interface IPrivacyDeletionService
    {
        Task<List<PrivacyDeletionRequestDto>> GetRequestsAsync(PrivacyRequestStatus? status, CancellationToken ct = default);

        Task<PrivacyResult> GetRequestAsync(int id, CancellationToken ct = default);

        /// <summary>The user's open request (awaiting confirmation or scheduled), or NotFound.</summary>
        Task<PrivacyResult> GetOpenRequestOfUserAsync(int userId, CancellationToken ct = default);

        /// <summary>
        /// A player's own request: emails a confirmation link (valid ConfirmationHours). Asking again
        /// while unconfirmed sends a fresh link (old links stop working; at most one email per
        /// ResendCooldownSeconds). A scheduled request is returned as AlreadyPending.
        /// </summary>
        Task<PrivacyResult> RequestOwnAsync(int userId, CancellationToken ct = default);

        /// <summary>Confirms a player's request from the emailed link: scheduled GraceDays later.</summary>
        Task<PrivacyResult> ConfirmAsync(string token, CancellationToken ct = default);

        /// <summary>
        /// Staff or the owner file a request for a player: no email confirmation, scheduled GraceDays
        /// later (an unconfirmed request of the player is taken over). The player is emailed when the
        /// account has an address and can still cancel.
        /// </summary>
        Task<PrivacyResult> FileForPlayerAsync(int filedByUserId, PrivacyRequestSource source, int userId, string? note,
            CancellationToken ct = default);

        /// <summary>Cancels an open request (staff/owner). Idempotent for a cancelled one.</summary>
        Task<PrivacyResult> CancelAsync(int cancelledByUserId, int id, CancellationToken ct = default);

        /// <summary>The player cancels their own open request.</summary>
        Task<PrivacyResult> CancelOwnAsync(int userId, CancellationToken ct = default);

        /// <summary>
        /// Executes a scheduled request whose grace period is over (or, with <paramref name="dryRun"/>,
        /// only counts what it would remove — allowed any time while open). Executing a completed
        /// request again changes nothing and returns its stored result (idempotent).
        /// <paramref name="executorUserId"/> null = the scheduled job.
        /// </summary>
        Task<PrivacyResult> ExecuteAsync(int id, int? executorUserId, bool dryRun, CancellationToken ct = default);

        /// <summary>Executes every scheduled request whose grace period is over; returns how many ran.</summary>
        Task<int> ExecuteScheduledAsync(DateTime now, CancellationToken ct = default);

        /// <summary>Marks unconfirmed requests whose link expired as Expired; returns how many.</summary>
        Task<int> ExpireConfirmationsAsync(DateTime now, CancellationToken ct = default);
    }

    /// <summary>
    /// GDPR erasure (KNG-34 D12, DESIGN.md §F.14, developer decisions 2026-10-03). Requests come from
    /// the player (web app, confirmed by an emailed link) or from staff/the owner for a player (no
    /// email step). A confirmed request runs automatically after a grace period of GraceDays, during
    /// which the player or staff can cancel it; nothing is ever deleted without such a request (no
    /// removal of inactive players).
    /// <para>Execution deletes the player's KNG-34 data, discoveries, private-message logs, link codes,
    /// permission grants and group memberships and the audit rows about them, for the player and every
    /// account merged into them, and pseudonymizes their users rows. Ledger and Siege match rows stay
    /// (accounting records / other players' history) on the anonymous account, which never gets
    /// statistics again. The request keeps only counts.</para>
    /// </summary>
    public sealed class PrivacyDeletionService : IPrivacyDeletionService
    {
        public const string ErasureReason = PrivacyErasure.Reason;

        private readonly IPrivacyRepository _repository;
        private readonly ICurrencyService _currency;
        private readonly IAuditLogService _audit;
        private readonly IPrivacyEmailService _email;
        private readonly PrivacyOptions _options;
        private readonly string _frontendBaseUrl;
        private readonly IMemoryCache _cache;
        private readonly ILogger _logger;
        private readonly Func<DateTime> _clock;

        public PrivacyDeletionService(IPrivacyRepository repository, ICurrencyService currency, IAuditLogService audit,
            IPrivacyEmailService email, IMemoryCache cache, IOptions<PrivacyOptions>? options = null,
            IOptions<SecuritySettings>? security = null, ILogger<PrivacyDeletionService>? logger = null)
            : this(repository, currency, audit, email, options?.Value ?? new PrivacyOptions(), () => DateTime.UtcNow,
                security?.Value.PasswordResetFrontendBaseUrl, cache, logger)
        {
        }

        public PrivacyDeletionService(IPrivacyRepository repository, ICurrencyService currency, IAuditLogService audit,
            IPrivacyEmailService email, PrivacyOptions options, Func<DateTime> clock, string? fallbackFrontendBaseUrl = null,
            IMemoryCache? cache = null, ILogger? logger = null)
        {
            _repository = repository;
            _currency = currency;
            _audit = audit;
            _email = email;
            _options = options;
            _clock = clock;
            var baseUrl = !string.IsNullOrWhiteSpace(options.FrontendBaseUrl) ? options.FrontendBaseUrl
                : !string.IsNullOrWhiteSpace(fallbackFrontendBaseUrl) ? fallbackFrontendBaseUrl
                : "http://localhost:3000";
            _frontendBaseUrl = baseUrl.TrimEnd('/');
            _cache = cache ?? new MemoryCache(new MemoryCacheOptions());
            _logger = logger ?? NullLogger.Instance;
        }

        private TimeSpan Grace => TimeSpan.FromDays(Math.Max(0, _options.GraceDays));

        private TimeSpan DueAfter => TimeSpan.FromDays(Math.Max(1, _options.DeletionDueDays));

        public string AccountUrl => $"{_frontendBaseUrl}/account";

        public string ConfirmUrl(string token) => $"{_frontendBaseUrl}/account/delete-data/confirm?token={Uri.EscapeDataString(token)}";

        // ------------------------------------------------------------------ reads

        public async Task<List<PrivacyDeletionRequestDto>> GetRequestsAsync(PrivacyRequestStatus? status, CancellationToken ct = default)
        {
            var requests = await _repository.GetRequestsAsync(status, ct);
            var names = await _repository.GetUsernamesAsync(requests.Select(r => r.UserId).ToList(), ct);
            return requests.Select(r => ToDto(r, names)).ToList();
        }

        public async Task<PrivacyResult> GetRequestAsync(int id, CancellationToken ct = default)
        {
            var request = await _repository.GetRequestAsync(id, ct);
            return request == null ? new PrivacyResult(PrivacyOutcome.NotFound) : Ok(await DtoAsync(request, ct));
        }

        public async Task<PrivacyResult> GetOpenRequestOfUserAsync(int userId, CancellationToken ct = default)
        {
            var request = await _repository.GetOpenRequestOfUserAsync(userId, ct);
            return request == null ? new PrivacyResult(PrivacyOutcome.NotFound) : Ok(await DtoAsync(request, ct));
        }

        // ------------------------------------------------------------------ player's own request

        public async Task<PrivacyResult> RequestOwnAsync(int userId, CancellationToken ct = default)
        {
            var user = await _repository.GetUserAsync(userId, ct);
            if (!IsLiveAccount(user)) return new PrivacyResult(PrivacyOutcome.UserNotFound);
            var open = await _repository.GetOpenRequestOfUserAsync(userId, ct);
            if (open is { Status: PrivacyRequestStatus.Pending }) return new PrivacyResult(PrivacyOutcome.AlreadyPending, await DtoAsync(open, ct));
            if (string.IsNullOrWhiteSpace(user!.Email)) return new PrivacyResult(PrivacyOutcome.EmailRequired);

            var now = _clock();
            var cooldownKey = $"privacy-confirm:{userId}";
            if (open != null && _cache.TryGetValue(cooldownKey, out _))
            {
                return Ok(await DtoAsync(open, ct)); // a link was just sent; don't send another yet
            }

            var token = NewToken();
            var request = open ?? new PrivacyDeletionRequest
            {
                UserId = userId,
                Source = PrivacyRequestSource.Player,
                RequestedByUserId = userId,
                Status = PrivacyRequestStatus.AwaitingConfirmation
            };
            request.RequestedAt = now;
            request.DueAt = now + DueAfter;
            request.ConfirmationTokenHash = HashToken(token);
            request.ConfirmationExpiresAt = now.AddHours(Math.Max(1, _options.ConfirmationHours));
            if (open == null)
            {
                await _repository.AddRequestAsync(request, ct);
                await _audit.RecordAsync(userId, userId, AuditAction.PrivacyDeletionRequested,
                    JsonSerializer.Serialize(new { requestId = request.Id, source = request.Source.ToString(), status = request.Status.ToString() }));
            }
            else
            {
                await _repository.SaveChangesAsync(ct);
            }

            try
            {
                await _email.SendConfirmationAsync(user.Email!, user.Username, ConfirmUrl(token), request.ConfirmationExpiresAt.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Data deletion confirmation email for user {UserId} failed", userId);
                return new PrivacyResult(PrivacyOutcome.EmailFailed, await DtoAsync(request, ct));
            }
            _cache.Set(cooldownKey, true, TimeSpan.FromSeconds(Math.Max(5, _options.ResendCooldownSeconds)));
            return Ok(await DtoAsync(request, ct));
        }

        public async Task<PrivacyResult> ConfirmAsync(string token, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(token)) return new PrivacyResult(PrivacyOutcome.InvalidToken);
            var request = await _repository.GetRequestByTokenHashAsync(HashToken(token.Trim()), ct);
            var now = _clock();
            if (request == null || request.Status != PrivacyRequestStatus.AwaitingConfirmation
                || request.ConfirmationExpiresAt == null || request.ConfirmationExpiresAt <= now)
            {
                return new PrivacyResult(PrivacyOutcome.InvalidToken);
            }
            var user = await _repository.GetUserAsync(request.UserId, ct);
            if (!IsLiveAccount(user)) return new PrivacyResult(PrivacyOutcome.InvalidToken);

            Schedule(request, now);
            await _repository.SaveChangesAsync(ct);
            await _audit.RecordAsync(request.UserId, request.UserId, AuditAction.PrivacyDeletionConfirmed,
                JsonSerializer.Serialize(new { requestId = request.Id, scheduledAt = request.ScheduledAt }));
            await NotifyScheduledAsync(user!, request, filedByStaff: false);
            return Ok(await DtoAsync(request, ct));
        }

        public async Task<PrivacyResult> CancelOwnAsync(int userId, CancellationToken ct = default)
        {
            var open = await _repository.GetOpenRequestOfUserAsync(userId, ct);
            if (open == null) return new PrivacyResult(PrivacyOutcome.NotFound);
            return await CancelRequestAsync(userId, open, ct);
        }

        // ------------------------------------------------------------------ staff / owner

        public async Task<PrivacyResult> FileForPlayerAsync(int filedByUserId, PrivacyRequestSource source, int userId, string? note,
            CancellationToken ct = default)
        {
            if (source == PrivacyRequestSource.Player) throw new ArgumentOutOfRangeException(nameof(source));
            var user = await _repository.GetUserAsync(userId, ct);
            if (!IsLiveAccount(user)) return new PrivacyResult(PrivacyOutcome.UserNotFound);
            var open = await _repository.GetOpenRequestOfUserAsync(userId, ct);
            if (open is { Status: PrivacyRequestStatus.Pending }) return new PrivacyResult(PrivacyOutcome.AlreadyPending, await DtoAsync(open, ct));

            var now = _clock();
            var request = open ?? new PrivacyDeletionRequest { UserId = userId };
            request.Source = source;
            request.RequestedByUserId = filedByUserId;
            request.RequestedAt = now;
            request.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            Schedule(request, now);
            if (open == null)
            {
                await _repository.AddRequestAsync(request, ct);
            }
            else
            {
                await _repository.SaveChangesAsync(ct); // takes over the player's unconfirmed request
            }
            await _audit.RecordAsync(filedByUserId, userId, AuditAction.PrivacyDeletionRequested,
                JsonSerializer.Serialize(new { requestId = request.Id, source = source.ToString(), scheduledAt = request.ScheduledAt }));
            await NotifyScheduledAsync(user!, request, filedByStaff: true);
            return Ok(await DtoAsync(request, ct));
        }

        public async Task<PrivacyResult> CancelAsync(int cancelledByUserId, int id, CancellationToken ct = default)
        {
            var request = await _repository.GetRequestAsync(id, ct);
            if (request == null) return new PrivacyResult(PrivacyOutcome.NotFound);
            return await CancelRequestAsync(cancelledByUserId, request, ct);
        }

        private async Task<PrivacyResult> CancelRequestAsync(int cancelledByUserId, PrivacyDeletionRequest request, CancellationToken ct)
        {
            if (request.Status == PrivacyRequestStatus.Cancelled) return Ok(await DtoAsync(request, ct));
            if (request.Status is not (PrivacyRequestStatus.Pending or PrivacyRequestStatus.AwaitingConfirmation))
            {
                return new PrivacyResult(PrivacyOutcome.NotPending, await DtoAsync(request, ct));
            }
            request.Status = PrivacyRequestStatus.Cancelled;
            request.CancelledAt = _clock();
            request.CancelledByUserId = cancelledByUserId;
            request.ConfirmationTokenHash = null;
            await _repository.SaveChangesAsync(ct);
            await _audit.RecordAsync(cancelledByUserId, request.UserId, AuditAction.PrivacyDeletionCancelled,
                JsonSerializer.Serialize(new { requestId = request.Id, byPlayer = cancelledByUserId == request.UserId }));
            return Ok(await DtoAsync(request, ct));
        }

        // ------------------------------------------------------------------ execution

        public async Task<PrivacyResult> ExecuteAsync(int id, int? executorUserId, bool dryRun, CancellationToken ct = default)
        {
            var request = await _repository.GetRequestAsync(id, ct);
            if (request == null) return new PrivacyResult(PrivacyOutcome.NotFound);
            if (request.Status == PrivacyRequestStatus.Completed) return Ok(await DtoAsync(request, ct));
            var open = request.Status is PrivacyRequestStatus.Pending or PrivacyRequestStatus.AwaitingConfirmation;
            if (!open || (!dryRun && request.Status != PrivacyRequestStatus.Pending))
            {
                return new PrivacyResult(PrivacyOutcome.NotPending, await DtoAsync(request, ct));
            }

            var userIds = new List<int> { request.UserId };
            userIds.AddRange((await _currency.GetMergedAccountIdsAsync(request.UserId, ct)).Where(i => i != request.UserId));

            if (dryRun)
            {
                var preview = await DtoAsync(request, ct);
                preview.Result = new PrivacyDeletionResultDto
                {
                    DryRun = true,
                    UserIds = userIds,
                    Deleted = await _repository.CountUserDataAsync(userIds, ct),
                    PseudonymizedUsers = userIds.Count
                };
                return Ok(preview);
            }

            var now = _clock();
            if (request.ScheduledAt == null || request.ScheduledAt > now)
            {
                return new PrivacyResult(PrivacyOutcome.GracePeriod, await DtoAsync(request, ct));
            }

            var result = await _repository.InTransactionAsync(async () =>
            {
                var deleted = await _repository.EraseUserDataAsync(userIds, ct);
                var pseudonymized = await _repository.PseudonymizeUsersAsync(userIds, now, ErasureReason, ct);
                var summary = new PrivacyDeletionResultDto { UserIds = userIds, Deleted = deleted, PseudonymizedUsers = pseudonymized };
                request.Status = PrivacyRequestStatus.Completed;
                request.ExecutedAt = now;
                request.ExecutedByUserId = executorUserId;
                request.ConfirmationTokenHash = null;
                request.Note = null;
                request.ResultJson = JsonSerializer.Serialize(summary);
                await _repository.SaveChangesAsync(ct);
                return summary;
            }, ct);
            // Recorded after the erasure (which removes the audit rows about the player); names no one.
            await _audit.RecordAsync(executorUserId, request.UserId, AuditAction.PrivacyDeletionExecuted,
                JsonSerializer.Serialize(new { requestId = request.Id, automatic = executorUserId == null, result.PseudonymizedUsers,
                    deleted = result.Deleted.Values.Sum() }));
            return Ok(await DtoAsync(request, ct));
        }

        public async Task<int> ExecuteScheduledAsync(DateTime now, CancellationToken ct = default)
        {
            if (!_options.AutoExecuteEnabled) return 0;
            var due = await _repository.GetScheduledDueAsync(now, ct);
            var executed = 0;
            foreach (var request in due)
            {
                var result = await ExecuteAsync(request.Id, null, dryRun: false, ct);
                if (result.Outcome == PrivacyOutcome.Ok) executed++;
            }
            return executed;
        }

        public async Task<int> ExpireConfirmationsAsync(DateTime now, CancellationToken ct = default)
        {
            var stale = await _repository.GetExpiredConfirmationsAsync(now, ct);
            foreach (var request in stale)
            {
                request.Status = PrivacyRequestStatus.Expired;
                request.ConfirmationTokenHash = null;
            }
            if (stale.Count > 0) await _repository.SaveChangesAsync(ct);
            return stale.Count;
        }

        // ------------------------------------------------------------------ helpers

        private void Schedule(PrivacyDeletionRequest request, DateTime now)
        {
            request.Status = PrivacyRequestStatus.Pending;
            request.ConfirmedAt = now;
            request.ScheduledAt = now + Grace;
            request.DueAt = now + DueAfter;
            request.ConfirmationTokenHash = null;
            request.ConfirmationExpiresAt = null;
        }

        private async Task NotifyScheduledAsync(User user, PrivacyDeletionRequest request, bool filedByStaff)
        {
            if (string.IsNullOrWhiteSpace(user.Email) || request.ScheduledAt == null) return;
            try
            {
                await _email.SendScheduledAsync(user.Email, user.Username, request.ScheduledAt.Value, AccountUrl, filedByStaff);
            }
            catch (Exception ex)
            {
                // Informational only: the request is scheduled either way.
                _logger.LogWarning(ex, "Data deletion scheduled-email for user {UserId} failed", user.Id);
            }
        }

        private static bool IsLiveAccount(User? user) =>
            user != null && user.IsActive && user.DeletedReason != PrivacyErasure.Reason;

        private static string NewToken() =>
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static string HashToken(string rawToken) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();

        private static PrivacyResult Ok(PrivacyDeletionRequestDto dto) => new(PrivacyOutcome.Ok, dto);

        private async Task<PrivacyDeletionRequestDto> DtoAsync(PrivacyDeletionRequest request, CancellationToken ct)
        {
            var names = await _repository.GetUsernamesAsync(new[] { request.UserId }, ct);
            return ToDto(request, names);
        }

        private PrivacyDeletionRequestDto ToDto(PrivacyDeletionRequest r, IReadOnlyDictionary<int, string> names) => new()
        {
            Id = r.Id,
            UserId = r.UserId,
            Username = names.TryGetValue(r.UserId, out var n) ? n : null,
            Source = r.Source,
            RequestedAt = r.RequestedAt,
            DueAt = r.DueAt,
            Status = r.Status,
            RequestedByUserId = r.RequestedByUserId,
            Note = r.Note,
            ConfirmationExpiresAt = r.Status == PrivacyRequestStatus.AwaitingConfirmation ? r.ConfirmationExpiresAt : null,
            ConfirmedAt = r.ConfirmedAt,
            ScheduledAt = r.ScheduledAt,
            AutoExecute = r.Status == PrivacyRequestStatus.Pending && _options.AutoExecuteEnabled,
            CancelledAt = r.CancelledAt,
            CancelledByUserId = r.CancelledByUserId,
            ExecutedAt = r.ExecutedAt,
            ExecutedByUserId = r.ExecutedByUserId,
            Result = string.IsNullOrEmpty(r.ResultJson) ? null : JsonSerializer.Deserialize<PrivacyDeletionResultDto>(r.ResultJson)
        };
    }
}
