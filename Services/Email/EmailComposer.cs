using System.Threading.Channels;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.Email
{
    /// <summary>Wakes EmailDispatcher when a mail was queued, so it doesn't wait for its next tick.</summary>
    public sealed class EmailDispatchSignal
    {
        private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite
        });

        public void Signal() => _channel.Writer.TryWrite(true);

        public ChannelReader<bool> Reader => _channel.Reader;
    }

    public interface IEmailThrottle
    {
        /// <summary>Why a new <paramref name="kind"/> mail to <paramref name="toAddress"/> must be
        /// suppressed now, or null when it may go out.</summary>
        Task<string?> CheckAsync(EmailKind kind, string toAddress, DateTime now, CancellationToken ct = default);
    }

    /// <summary>
    /// The outbound throttle (DESIGN.md § 4.2): per recipient at most 3 mails an hour per throttle
    /// class and 6 in total, and at most Email:DailyCap mails across everyone in 24 hours. Counts
    /// the outbox rows that weren't suppressed themselves.
    /// </summary>
    public sealed class EmailThrottle : IEmailThrottle
    {
        public const int PerClassPerHour = 3;
        public const int PerRecipientPerHour = 6;

        private readonly KnKDbContext _db;
        private readonly EmailSettings _settings;
        private readonly IMemoryCache? _cache;
        private readonly ILogger _logger;

        public EmailThrottle(KnKDbContext db, IOptions<EmailSettings> settings, IMemoryCache? cache = null,
            ILogger<EmailThrottle>? logger = null)
        {
            _db = db;
            _settings = settings.Value;
            _cache = cache;
            _logger = logger ?? (ILogger)NullLogger.Instance;
        }

        public async Task<string?> CheckAsync(EmailKind kind, string toAddress, DateTime now, CancellationToken ct = default)
        {
            var hourAgo = now.AddHours(-1);
            var recent = await _db.EmailOutbox.AsNoTracking()
                .Where(m => m.ToAddress == toAddress && m.CreatedAt > hourAgo && m.Status != EmailOutboxStatus.Suppressed)
                .Select(m => m.Kind)
                .ToListAsync(ct);

            var throttleClass = EmailKinds.Info(kind).ThrottleClass;
            var sameClass = recent.Count(k => Enum.TryParse<EmailKind>(k, out var other) && EmailKinds.Info(other).ThrottleClass == throttleClass);
            if (sameClass >= PerClassPerHour)
            {
                return $"{PerClassPerHour} {throttleClass} mails to this address in the last hour";
            }
            if (recent.Count >= PerRecipientPerHour)
            {
                return $"{PerRecipientPerHour} mails to this address in the last hour";
            }

            var dayAgo = now.AddDays(-1);
            var cap = Math.Max(1, _settings.DailyCap);
            var lastDay = await _db.EmailOutbox.AsNoTracking()
                .CountAsync(m => m.CreatedAt > dayAgo && m.Status != EmailOutboxStatus.Suppressed, ct);
            if (lastDay >= cap)
            {
                RaiseDailyCapAlert(cap, now);
                return $"the daily cap of {cap} mails (Email:DailyCap) is reached";
            }
            return null;
        }

        private void RaiseDailyCapAlert(int cap, DateTime now)
        {
            // Once a day is enough; the suppressed rows show on /admin/email either way.
            var key = $"email-daily-cap:{now:yyyyMMdd}";
            if (_cache != null && _cache.TryGetValue(key, out _)) return;
            _cache?.Set(key, true, TimeSpan.FromDays(1));
            _logger.LogError("Email daily cap reached: {Cap} mails in 24 hours. New mails are suppressed until the count drops; raise Email:DailyCap if this is expected", cap);
        }
    }

    public interface IEmailComposer
    {
        /// <summary>
        /// Renders a <paramref name="kind"/> mail and adds it to the outbox in the caller's
        /// DbContext and saves, so inside a transaction it commits or rolls back with the caller's
        /// change. A <paramref name="dedupeKey"/> already in the outbox returns that row instead.
        /// Over the throttle the row is stored as Suppressed (no bodies) and logged.
        /// </summary>
        Task<EmailOutboxMessage> QueueAsync(EmailKind kind, string to, object model, string dedupeKey,
            DateTime? expiresAt = null, int? userId = null, CancellationToken ct = default);
    }

    public sealed class EmailComposer : IEmailComposer
    {
        private readonly KnKDbContext _db;
        private readonly IEmailTemplateRenderer _renderer;
        private readonly IEmailThrottle _throttle;
        private readonly EmailDispatchSignal? _signal;
        private readonly TimeProvider _time;
        private readonly ILogger _logger;

        public EmailComposer(KnKDbContext db, IEmailTemplateRenderer renderer, IEmailThrottle throttle,
            EmailDispatchSignal? signal = null, TimeProvider? time = null, ILogger<EmailComposer>? logger = null)
        {
            _db = db;
            _renderer = renderer;
            _throttle = throttle;
            _signal = signal;
            _time = time ?? TimeProvider.System;
            _logger = logger ?? (ILogger)NullLogger.Instance;
        }

        public async Task<EmailOutboxMessage> QueueAsync(EmailKind kind, string to, object model, string dedupeKey,
            DateTime? expiresAt = null, int? userId = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(to)) throw new ArgumentException("A recipient is required.", nameof(to));
            if (string.IsNullOrWhiteSpace(dedupeKey)) throw new ArgumentException("A dedupe key is required.", nameof(dedupeKey));

            var existing = await _db.EmailOutbox.FirstOrDefaultAsync(m => m.DedupeKey == dedupeKey, ct);
            if (existing != null)
            {
                return existing;
            }

            var address = to.Trim().ToLowerInvariant();
            var now = _time.GetUtcNow().UtcDateTime;
            var rendered = _renderer.Render(kind, model);
            var suppressedBecause = await _throttle.CheckAsync(kind, address, now, ct);

            var row = new EmailOutboxMessage
            {
                Kind = kind.ToString(),
                ToAddress = address,
                Subject = Truncate(rendered.Subject, 200)!,
                TextBody = suppressedBecause == null ? rendered.TextBody : null,
                HtmlBody = suppressedBecause == null ? rendered.HtmlBody : null,
                Status = suppressedBecause == null ? EmailOutboxStatus.Pending : EmailOutboxStatus.Suppressed,
                NextAttemptAt = now,
                ExpiresAt = expiresAt,
                LastError = suppressedBecause == null ? null : Truncate("Suppressed: " + suppressedBecause, 1000),
                CreatedAt = now,
                UserId = userId,
                DedupeKey = Truncate(dedupeKey, 120)!
            };
            _db.EmailOutbox.Add(row);
            await _db.SaveChangesAsync(ct);

            if (suppressedBecause != null)
            {
                _logger.LogWarning("Mail {Kind} to {To} suppressed: {Reason}", kind, EmailMask.Mask(address), suppressedBecause);
            }
            else
            {
                _signal?.Signal();
            }
            return row;
        }

        public static string? Truncate(string? value, int max) =>
            value == null || value.Length <= max ? value : value[..max];
    }
}
