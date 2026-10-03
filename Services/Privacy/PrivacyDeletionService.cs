using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.Privacy
{
    public enum PrivacyOutcome
    {
        Ok,
        NotFound,
        UserNotFound,
        /// <summary>The user already has a pending request (returned).</summary>
        AlreadyPending,
        /// <summary>The request is not pending (cancel/execute of a cancelled or completed request).</summary>
        NotPending
    }

    public sealed record PrivacyResult(PrivacyOutcome Outcome, PrivacyDeletionRequestDto? Request = null);

    public interface IPrivacyDeletionService
    {
        Task<List<PrivacyDeletionRequestDto>> GetRequestsAsync(PrivacyRequestStatus? status, CancellationToken ct = default);

        Task<PrivacyResult> GetRequestAsync(int id, CancellationToken ct = default);

        /// <summary>Records a request due DeletionDueDays from now (audited).</summary>
        Task<PrivacyResult> RequestAsync(int ownerUserId, int userId, string? note, CancellationToken ct = default);

        Task<PrivacyResult> CancelAsync(int ownerUserId, int id, CancellationToken ct = default);

        /// <summary>
        /// Executes a pending request (or, with <paramref name="dryRun"/>, only counts what it would
        /// remove). Executing a completed request again changes nothing and returns its stored result
        /// (idempotent). <paramref name="executorUserId"/> null = the due-date job.
        /// </summary>
        Task<PrivacyResult> ExecuteAsync(int id, int? executorUserId, bool dryRun, CancellationToken ct = default);

        /// <summary>Executes every pending request within AutoExecuteBeforeDueDays of its due date; returns how many ran.</summary>
        Task<int> ExecuteDueAsync(DateTime now, CancellationToken ct = default);
    }

    /// <summary>
    /// GDPR erasure (KNG-34 D12, DESIGN.md §F.14, L1-18). Deletes every KNG-34 row of the player
    /// and of the accounts merged into them (statistics, sessions, settings, profile incl. leaderboard
    /// exclusion, title history, kill pairs as killer or victim, leaderboard snapshot entries,
    /// diagnostic events and enhanced targets, discoveries) and pseudonymizes their users rows. The
    /// ledger, Siege match rows and the audit log are kept (accounting records / other players'
    /// history / 180-day retention). Irreversible: owner node only, a dry run shows the counts first,
    /// the request keeps only counts.
    /// </summary>
    public sealed class PrivacyDeletionService : IPrivacyDeletionService
    {
        public const string ErasureReason = "GDPR erasure";

        private readonly IPrivacyRepository _repository;
        private readonly ICurrencyService _currency;
        private readonly IAuditLogService _audit;
        private readonly PrivacyOptions _options;
        private readonly Func<DateTime> _clock;

        public PrivacyDeletionService(IPrivacyRepository repository, ICurrencyService currency, IAuditLogService audit,
            IOptions<PrivacyOptions>? options = null)
            : this(repository, currency, audit, options?.Value ?? new PrivacyOptions(), () => DateTime.UtcNow)
        {
        }

        public PrivacyDeletionService(IPrivacyRepository repository, ICurrencyService currency, IAuditLogService audit,
            PrivacyOptions options, Func<DateTime> clock)
        {
            _repository = repository;
            _currency = currency;
            _audit = audit;
            _options = options;
            _clock = clock;
        }

        public async Task<List<PrivacyDeletionRequestDto>> GetRequestsAsync(PrivacyRequestStatus? status, CancellationToken ct = default)
        {
            var requests = await _repository.GetRequestsAsync(status, ct);
            var names = await _repository.GetUsernamesAsync(requests.Select(r => r.UserId).ToList(), ct);
            return requests.Select(r => ToDto(r, names)).ToList();
        }

        public async Task<PrivacyResult> GetRequestAsync(int id, CancellationToken ct = default)
        {
            var request = await _repository.GetRequestAsync(id, ct);
            return request == null ? new PrivacyResult(PrivacyOutcome.NotFound) : new PrivacyResult(PrivacyOutcome.Ok, await DtoAsync(request, ct));
        }

        public async Task<PrivacyResult> RequestAsync(int ownerUserId, int userId, string? note, CancellationToken ct = default)
        {
            var user = await _repository.GetUserAsync(userId, ct);
            if (user == null) return new PrivacyResult(PrivacyOutcome.UserNotFound);
            var pending = await _repository.GetPendingRequestOfUserAsync(userId, ct);
            if (pending != null) return new PrivacyResult(PrivacyOutcome.AlreadyPending, await DtoAsync(pending, ct));

            var now = _clock();
            var request = new PrivacyDeletionRequest
            {
                UserId = userId,
                RequestedAt = now,
                DueAt = now.AddDays(Math.Max(1, _options.DeletionDueDays)),
                Status = PrivacyRequestStatus.Pending,
                RequestedByUserId = ownerUserId,
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
            };
            await _repository.AddRequestAsync(request, ct);
            await _audit.RecordAsync(ownerUserId, userId, AuditAction.PrivacyDeletionRequested,
                JsonSerializer.Serialize(new { requestId = request.Id, dueAt = request.DueAt }));
            return new PrivacyResult(PrivacyOutcome.Ok, await DtoAsync(request, ct));
        }

        public async Task<PrivacyResult> CancelAsync(int ownerUserId, int id, CancellationToken ct = default)
        {
            var request = await _repository.GetRequestAsync(id, ct);
            if (request == null) return new PrivacyResult(PrivacyOutcome.NotFound);
            if (request.Status == PrivacyRequestStatus.Cancelled) return new PrivacyResult(PrivacyOutcome.Ok, await DtoAsync(request, ct));
            if (request.Status != PrivacyRequestStatus.Pending) return new PrivacyResult(PrivacyOutcome.NotPending, await DtoAsync(request, ct));
            request.Status = PrivacyRequestStatus.Cancelled;
            await _repository.SaveChangesAsync(ct);
            await _audit.RecordAsync(ownerUserId, request.UserId, AuditAction.PrivacyDeletionCancelled,
                JsonSerializer.Serialize(new { requestId = request.Id }));
            return new PrivacyResult(PrivacyOutcome.Ok, await DtoAsync(request, ct));
        }

        public async Task<PrivacyResult> ExecuteAsync(int id, int? executorUserId, bool dryRun, CancellationToken ct = default)
        {
            var request = await _repository.GetRequestAsync(id, ct);
            if (request == null) return new PrivacyResult(PrivacyOutcome.NotFound);
            if (request.Status == PrivacyRequestStatus.Completed) return new PrivacyResult(PrivacyOutcome.Ok, await DtoAsync(request, ct));
            if (request.Status != PrivacyRequestStatus.Pending) return new PrivacyResult(PrivacyOutcome.NotPending, await DtoAsync(request, ct));

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
                return new PrivacyResult(PrivacyOutcome.Ok, preview);
            }

            var now = _clock();
            var result = await _repository.InTransactionAsync(async () =>
            {
                var deleted = await _repository.EraseUserDataAsync(userIds, ct);
                var pseudonymized = await _repository.PseudonymizeUsersAsync(userIds, now, ErasureReason, ct);
                var summary = new PrivacyDeletionResultDto { UserIds = userIds, Deleted = deleted, PseudonymizedUsers = pseudonymized };
                request.Status = PrivacyRequestStatus.Completed;
                request.ExecutedAt = now;
                request.ExecutedByUserId = executorUserId;
                request.ResultJson = JsonSerializer.Serialize(summary);
                await _repository.SaveChangesAsync(ct);
                return summary;
            }, ct);
            await _audit.RecordAsync(executorUserId, request.UserId, AuditAction.PrivacyDeletionExecuted,
                JsonSerializer.Serialize(new { requestId = request.Id, automatic = executorUserId == null, result.PseudonymizedUsers,
                    deleted = result.Deleted.Values.Sum() }));
            return new PrivacyResult(PrivacyOutcome.Ok, await DtoAsync(request, ct));
        }

        public async Task<int> ExecuteDueAsync(DateTime now, CancellationToken ct = default)
        {
            if (!_options.AutoExecuteEnabled) return 0;
            var due = await _repository.GetPendingDueAsync(now.AddDays(Math.Max(0, _options.AutoExecuteBeforeDueDays)), ct);
            var executed = 0;
            foreach (var request in due)
            {
                var result = await ExecuteAsync(request.Id, null, dryRun: false, ct);
                if (result.Outcome == PrivacyOutcome.Ok) executed++;
            }
            return executed;
        }

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
            RequestedAt = r.RequestedAt,
            DueAt = r.DueAt,
            AutoExecuteAt = r.Status == PrivacyRequestStatus.Pending && _options.AutoExecuteEnabled
                ? r.DueAt.AddDays(-Math.Max(0, _options.AutoExecuteBeforeDueDays))
                : null,
            Status = r.Status,
            RequestedByUserId = r.RequestedByUserId,
            Note = r.Note,
            ExecutedAt = r.ExecutedAt,
            ExecutedByUserId = r.ExecutedByUserId,
            Result = string.IsNullOrEmpty(r.ResultJson) ? null : JsonSerializer.Deserialize<PrivacyDeletionResultDto>(r.ResultJson)
        };
    }
}
