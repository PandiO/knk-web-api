using knkwebapi_v2.Configuration;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.Email
{
    /// <summary>
    /// Sends the outbox (DESIGN.md § 4.2): every 5 seconds, or at once when IEmailComposer signals,
    /// it takes up to 20 Pending rows that are due. A row past its ExpiresAt becomes Expired
    /// without a send. A failed send is retried after 1 min, 5 min, 15 min, 1 h and 4 h, and the
    /// row is Failed after Email:MaxAttempts (6) attempts. Sent and Expired rows lose their bodies;
    /// a Failed row keeps them only when it carries no token (no ExpiresAt), so staff can retry it.
    /// </summary>
    public class EmailDispatcher : BackgroundService
    {
        public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(5);
        public const int BatchSize = 20;

        /// <summary>The wait after the 1st, 2nd, … failed attempt.</summary>
        public static readonly IReadOnlyList<TimeSpan> Backoff = new[]
        {
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1), TimeSpan.FromHours(4)
        };

        private readonly IServiceScopeFactory _scopes;
        private readonly EmailDispatchSignal _signal;
        private readonly TimeProvider _time;
        private readonly ILogger<EmailDispatcher> _logger;
        private readonly TimeSpan _interval;
        private readonly ConnectivityAwareFailureLogger _failures;

        public EmailDispatcher(IServiceScopeFactory scopes, EmailDispatchSignal signal, TimeProvider time,
            ILogger<EmailDispatcher> logger, TimeSpan? interval = null)
        {
            _scopes = scopes;
            _signal = signal;
            _time = time;
            _logger = logger;
            _interval = interval ?? DefaultInterval;
            _failures = new ConnectivityAwareFailureLogger(logger, "Email dispatch");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(_interval, _time);
            Task<bool>? tick = null;
            Task<bool>? wake = null;
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await RunOnceAsync(stoppingToken);
                        _failures.Succeeded();
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _failures.Failed(ex, "Email dispatch failed");
                    }

                    tick ??= timer.WaitForNextTickAsync(stoppingToken).AsTask();
                    wake ??= _signal.Reader.WaitToReadAsync(stoppingToken).AsTask();
                    var done = await Task.WhenAny(tick, wake);
                    if (done == tick)
                    {
                        if (!await tick) break;
                        tick = null;
                    }
                    else
                    {
                        await wake;
                        wake = null;
                    }
                    while (_signal.Reader.TryRead(out _))
                    {
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }

        /// <summary>One pass over the due rows. Returns how many were sent.</summary>
        public async Task<int> RunOnceAsync(CancellationToken ct = default)
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KnKDbContext>();
            var transport = scope.ServiceProvider.GetRequiredService<IEmailTransport>();
            var maxAttempts = Math.Max(1, scope.ServiceProvider.GetService<IOptions<EmailSettings>>()?.Value.MaxAttempts ?? 6);

            var now = _time.GetUtcNow().UtcDateTime;
            var due = await db.EmailOutbox
                .Where(m => m.Status == EmailOutboxStatus.Pending && m.NextAttemptAt <= now)
                .OrderBy(m => m.NextAttemptAt).ThenBy(m => m.Id)
                .Take(BatchSize)
                .ToListAsync(ct);

            var sent = 0;
            foreach (var row in due)
            {
                now = _time.GetUtcNow().UtcDateTime;
                if (row.ExpiresAt.HasValue && row.ExpiresAt.Value <= now)
                {
                    row.Status = EmailOutboxStatus.Expired;
                    ClearBodies(row);
                    _logger.LogWarning("Mail {Kind} #{Id} to {To} expired before it could be sent", row.Kind, row.Id, EmailMask.Mask(row.ToAddress));
                }
                else
                {
                    row.Attempts++;
                    try
                    {
                        row.ProviderMessageId = EmailComposer.Truncate(await transport.SendAsync(
                            new OutgoingEmail(row.Id, row.Kind, row.ToAddress, row.ToName, row.Subject, row.TextBody, row.HtmlBody), ct), 255);
                        row.Status = EmailOutboxStatus.Sent;
                        row.SentAt = _time.GetUtcNow().UtcDateTime;
                        row.LastError = null;
                        ClearBodies(row);
                        sent++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    {
                        row.LastError = EmailComposer.Truncate($"{ex.GetType().Name}: {ex.Message}", 1000);
                        if (row.Attempts >= maxAttempts)
                        {
                            row.Status = EmailOutboxStatus.Failed;
                            if (row.ExpiresAt.HasValue) ClearBodies(row);
                            _logger.LogError("Mail {Kind} #{Id} to {To} failed after {Attempts} attempts: {Error}",
                                row.Kind, row.Id, EmailMask.Mask(row.ToAddress), row.Attempts, row.LastError);
                        }
                        else
                        {
                            row.NextAttemptAt = now + Backoff[Math.Min(row.Attempts, Backoff.Count) - 1];
                            _logger.LogWarning("Mail {Kind} #{Id} to {To}: attempt {Attempts} failed, next try at {Next:u}: {Error}",
                                row.Kind, row.Id, EmailMask.Mask(row.ToAddress), row.Attempts, row.NextAttemptAt, row.LastError);
                        }
                    }
                }
                // One save per row: a crash mid-batch never sends the earlier rows again.
                await db.SaveChangesAsync(ct);
            }
            return sent;
        }

        private static void ClearBodies(EmailOutboxMessage row)
        {
            row.TextBody = null;
            row.HtmlBody = null;
        }
    }
}
