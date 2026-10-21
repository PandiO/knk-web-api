using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.Email
{
    public enum EmailOutboxActionOutcome
    {
        Ok,
        NotFound,
        NoEmail,
        NotRetryable
    }

    public sealed record EmailOutboxActionResult(EmailOutboxActionOutcome Outcome, EmailOutboxItemDto? Item = null, string? Message = null);

    public interface IEmailOutboxAdminService
    {
        Task<EmailOutboxPageDto> ListAsync(int pageNumber, int pageSize, string? status, string? kind, CancellationToken ct = default);

        /// <summary>Queues a Test mail to the caller's own address.</summary>
        Task<EmailOutboxActionResult> SendTestAsync(int userId, CancellationToken ct = default);

        /// <summary>Puts a Failed mail back to Pending (only one that still has its bodies).</summary>
        Task<EmailOutboxActionResult> RetryAsync(long id, CancellationToken ct = default);
    }

    /// <summary>The /admin/email page (KNG-206): the mail log, a test mail, and Retry.</summary>
    public sealed class EmailOutboxAdminService : IEmailOutboxAdminService
    {
        public const int MaxPageSize = 100;

        private readonly KnKDbContext _db;
        private readonly IEmailComposer _composer;
        private readonly EmailSettings _settings;
        private readonly EmailDispatchSignal? _signal;
        private readonly TimeProvider _time;

        public EmailOutboxAdminService(KnKDbContext db, IEmailComposer composer, IOptions<EmailSettings> settings,
            EmailDispatchSignal? signal = null, TimeProvider? time = null)
        {
            _db = db;
            _composer = composer;
            _settings = settings.Value;
            _signal = signal;
            _time = time ?? TimeProvider.System;
        }

        public async Task<EmailOutboxPageDto> ListAsync(int pageNumber, int pageSize, string? status, string? kind, CancellationToken ct = default)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

            var query = _db.EmailOutbox.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<EmailOutboxStatus>(status, ignoreCase: true, out var parsed))
            {
                query = query.Where(m => m.Status == parsed);
            }
            if (!string.IsNullOrWhiteSpace(kind))
            {
                var kindName = kind.Trim();
                query = query.Where(m => m.Kind == kindName);
            }

            var total = await query.CountAsync(ct);
            var rows = await query
                .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
                .Skip((pageNumber - 1) * pageSize).Take(pageSize)
                .Select(m => new
                {
                    m.Id, m.Kind, m.ToAddress, m.Subject, m.Status, m.Attempts, m.NextAttemptAt, m.ExpiresAt,
                    m.LastError, m.CreatedAt, m.SentAt, m.UserId,
                    HasBody = m.TextBody != null || m.HtmlBody != null
                })
                .ToListAsync(ct);

            var dayAgo = _time.GetUtcNow().UtcDateTime.AddDays(-1);
            return new EmailOutboxPageDto
            {
                Items = rows.Select(m => new EmailOutboxItemDto
                {
                    Id = m.Id,
                    Kind = m.Kind,
                    ToMasked = EmailMask.Mask(m.ToAddress),
                    Subject = m.Subject,
                    Status = m.Status.ToString(),
                    Attempts = m.Attempts,
                    NextAttemptAt = m.NextAttemptAt,
                    ExpiresAt = m.ExpiresAt,
                    LastError = m.LastError,
                    CreatedAt = m.CreatedAt,
                    SentAt = m.SentAt,
                    UserId = m.UserId,
                    CanRetry = m.Status == EmailOutboxStatus.Failed && m.HasBody
                }).ToList(),
                TotalCount = total,
                PageNumber = pageNumber,
                PageSize = pageSize,
                Transport = _settings.EffectiveTransport(),
                Last24Hours = await _db.EmailOutbox.AsNoTracking()
                    .CountAsync(m => m.CreatedAt > dayAgo && m.Status != EmailOutboxStatus.Suppressed, ct),
                DailyCap = _settings.DailyCap
            };
        }

        public async Task<EmailOutboxActionResult> SendTestAsync(int userId, CancellationToken ct = default)
        {
            var user = await _db.Users.AsNoTracking().Where(u => u.Id == userId)
                .Select(u => new { u.Username, u.Email }).FirstOrDefaultAsync(ct);
            if (user == null)
            {
                return new EmailOutboxActionResult(EmailOutboxActionOutcome.NotFound, Message: "Your account was not found.");
            }
            if (string.IsNullOrWhiteSpace(user.Email))
            {
                return new EmailOutboxActionResult(EmailOutboxActionOutcome.NoEmail, Message: "Your account has no email address.");
            }

            var now = _time.GetUtcNow().UtcDateTime;
            var row = await _composer.QueueAsync(EmailKind.Test, user.Email,
                new { username = user.Username, sentAt = now }, $"test:{userId}:{now.Ticks}", userId: userId, ct: ct);
            return new EmailOutboxActionResult(EmailOutboxActionOutcome.Ok, ToDto(row));
        }

        public async Task<EmailOutboxActionResult> RetryAsync(long id, CancellationToken ct = default)
        {
            var row = await _db.EmailOutbox.FirstOrDefaultAsync(m => m.Id == id, ct);
            if (row == null)
            {
                return new EmailOutboxActionResult(EmailOutboxActionOutcome.NotFound, Message: $"Mail {id} not found.");
            }
            if (row.Status != EmailOutboxStatus.Failed)
            {
                return new EmailOutboxActionResult(EmailOutboxActionOutcome.NotRetryable, ToDto(row), "Only a Failed mail can be retried.");
            }
            if (row.TextBody == null && row.HtmlBody == null)
            {
                return new EmailOutboxActionResult(EmailOutboxActionOutcome.NotRetryable, ToDto(row),
                    "This mail carried a link or code and its content was removed; the player has to request it again.");
            }

            row.Status = EmailOutboxStatus.Pending;
            row.Attempts = 0;
            row.NextAttemptAt = _time.GetUtcNow().UtcDateTime;
            await _db.SaveChangesAsync(ct);
            _signal?.Signal();
            return new EmailOutboxActionResult(EmailOutboxActionOutcome.Ok, ToDto(row));
        }

        public static EmailOutboxItemDto ToDto(EmailOutboxMessage m) => new()
        {
            Id = m.Id,
            Kind = m.Kind,
            ToMasked = EmailMask.Mask(m.ToAddress),
            Subject = m.Subject,
            Status = m.Status.ToString(),
            Attempts = m.Attempts,
            NextAttemptAt = m.NextAttemptAt,
            ExpiresAt = m.ExpiresAt,
            LastError = m.LastError,
            CreatedAt = m.CreatedAt,
            SentAt = m.SentAt,
            UserId = m.UserId,
            CanRetry = m.Status == EmailOutboxStatus.Failed && (m.TextBody != null || m.HtmlBody != null)
        };
    }
}
