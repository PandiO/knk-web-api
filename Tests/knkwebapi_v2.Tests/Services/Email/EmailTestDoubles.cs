using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Email;

namespace knkwebapi_v2.Tests.Services.Email;

/// <summary>A mail as a caller queued it: the kind, recipient, model values and keys.</summary>
public sealed record QueuedEmail(EmailKind Kind, string To, IReadOnlyDictionary<string, string> Values, string DedupeKey,
    DateTime? ExpiresAt, int? UserId);

/// <summary>An IEmailComposer that records what is queued (and renders it strictly, so a
/// placeholder the template doesn't know fails the test) without a database.</summary>
public sealed class CapturingEmailComposer : IEmailComposer
{
    private readonly EmailTemplateRenderer _renderer = new(new knkwebapi_v2.Configuration.EmailSettings(), "http://localhost:3000", strict: true);
    private long _nextId;

    public List<QueuedEmail> Queued { get; } = new();

    public Task<EmailOutboxMessage> QueueAsync(EmailKind kind, string to, object model, string dedupeKey,
        DateTime? expiresAt = null, int? userId = null, CancellationToken ct = default)
    {
        var rendered = _renderer.Render(kind, model);
        Queued.Add(new QueuedEmail(kind, to, EmailTemplateRenderer.ToValues(model), dedupeKey, expiresAt, userId));
        return Task.FromResult(new EmailOutboxMessage
        {
            Id = ++_nextId,
            Kind = kind.ToString(),
            ToAddress = to,
            Subject = rendered.Subject,
            TextBody = rendered.TextBody,
            HtmlBody = rendered.HtmlBody,
            DedupeKey = dedupeKey,
            ExpiresAt = expiresAt,
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
            NextAttemptAt = DateTime.UtcNow
        });
    }
}

/// <summary>An IEmailTransport that records sends, or throws while <see cref="FailWith"/> is set.</summary>
public sealed class FakeEmailTransport : IEmailTransport
{
    public List<OutgoingEmail> Sent { get; } = new();

    public Exception? FailWith { get; set; }

    public int Calls { get; private set; }

    public Task<string?> SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        Calls++;
        if (FailWith != null) throw FailWith;
        Sent.Add(email);
        return Task.FromResult<string?>($"<{email.Id}@test>");
    }
}

/// <summary>A clock the test moves by hand.</summary>
public sealed class ManualClock : TimeProvider
{
    public ManualClock(DateTime utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTime UtcNow { get; set; }

    public void Advance(TimeSpan by) => UtcNow += by;

    public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(UtcNow, DateTimeKind.Utc));
}
