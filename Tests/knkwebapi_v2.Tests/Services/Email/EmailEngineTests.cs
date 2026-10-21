using System.Net;
using AutoMapper;
using MailKit.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using Moq;
using Xunit;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Email;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Privacy;

namespace knkwebapi_v2.Tests.Services.Email;

/// <summary>An EF InMemory outbox with the real composer, throttle, renderer and dispatcher (KNG-206).</summary>
internal sealed class EmailTestDb : IDisposable
{
    public static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    private readonly ServiceProvider _provider;

    public EmailTestDb(EmailSettings? settings = null)
    {
        Settings = settings ?? new EmailSettings { FromAddress = "no-reply@knightsandkings.net" };
        var name = "email-" + Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddDbContext<KnKDbContext>(o => o.UseInMemoryDatabase(name));
        services.AddSingleton<IEmailTransport>(Transport);
        services.AddSingleton(Options.Create(Settings));
        _provider = services.BuildServiceProvider();
        Db = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(name).Options);
        Renderer = new EmailTemplateRenderer(Settings, "https://knk.example", strict: true);
        Dispatcher = new EmailDispatcher(_provider.GetRequiredService<IServiceScopeFactory>(), Signal, Clock, NullLogger<EmailDispatcher>.Instance);
    }

    public EmailSettings Settings { get; }
    public KnKDbContext Db { get; }
    public ManualClock Clock { get; } = new(Now);
    public FakeEmailTransport Transport { get; } = new();
    public EmailDispatchSignal Signal { get; } = new();
    public EmailTemplateRenderer Renderer { get; }
    public EmailDispatcher Dispatcher { get; }

    public EmailComposer Composer(KnKDbContext? db = null)
    {
        var context = db ?? Db;
        return new EmailComposer(context, Renderer, new EmailThrottle(context, Options.Create(Settings), new MemoryCache(new MemoryCacheOptions())),
            Signal, Clock);
    }

    public Task<EmailOutboxMessage> QueueTestAsync(string to = "steve@example.com", string? key = null, DateTime? expiresAt = null,
        EmailKind kind = EmailKind.Test) =>
        Composer().QueueAsync(kind, to, SampleModel(kind), key ?? Guid.NewGuid().ToString("N"), expiresAt);

    /// <summary>A model with every placeholder of <paramref name="kind"/>'s templates.</summary>
    public static object SampleModel(EmailKind kind) => kind switch
    {
        EmailKind.Invitation => new { inviterName = "Pandi", inviteUrl = "https://knk.example/join?code=X", expiresAt = Now },
        EmailKind.Welcome => new { username = "Steve", linkUrl = "https://knk.example/account/link" },
        EmailKind.MinecraftLinked => new { username = "Steve", minecraftName = "SteveMC" },
        EmailKind.FinishLinking => new { username = "Steve", linkUrl = "https://knk.example/account/link" },
        EmailKind.PasswordReset => new { username = "Steve", resetUrl = "https://knk.example/auth/reset-password?token=T", expiresAt = Now },
        EmailKind.EmailChanged => new { username = "Steve", newEmailHint = "st***@example.com" },
        EmailKind.EmailVerification => new { username = "Steve", verifyUrl = "https://knk.example/verify?t=T", expiresAt = Now },
        EmailKind.PrivacyDeletionConfirm => new { username = "Steve", confirmUrl = "https://knk.example/confirm?token=T", expiresAt = Now },
        EmailKind.PrivacyDeletionScheduled => new { username = "Steve", intro = "You confirmed it.", scheduledAt = Now, accountUrl = "https://knk.example/account" },
        _ => new { username = "Steve", sentAt = Now }
    };

    public void Dispose()
    {
        Db.Dispose();
        _provider.Dispose();
    }
}

public class EmailTemplateRendererTests
{
    private static EmailTemplateRenderer Renderer(bool strict = true, string replyTo = "") =>
        new(new EmailSettings { ReplyToAddress = replyTo }, "https://knk.example/", strict);

    [Fact]
    public void Values_AreHtmlEncodedInTheHtmlPart_AndPlainInTheText()
    {
        var mail = Renderer().Render(EmailKind.EmailChanged, new { username = "<b>Steve & co</b>", newEmailHint = "x" });

        Assert.Contains("Hi &lt;b&gt;Steve &amp; co&lt;/b&gt;,", mail.HtmlBody);
        Assert.DoesNotContain("<b>Steve", mail.HtmlBody);
        Assert.Contains("Hi <b>Steve & co</b>,", mail.TextBody);
    }

    [Fact]
    public void BothParts_UseTheLayout_WithTheReasonAndSupportLine()
    {
        var mail = Renderer(replyTo: "support@knightsandkings.net").Render(EmailKind.PasswordReset, EmailTestDb.SampleModel(EmailKind.PasswordReset));

        Assert.Equal("Reset your Knights & Kings password", mail.Subject);
        Assert.StartsWith("KNIGHTS & KINGS", mail.TextBody);
        Assert.Contains("You get this mail because someone asked to reset the password", mail.TextBody);
        Assert.Contains("support@knightsandkings.net", mail.TextBody);
        Assert.Contains("<!DOCTYPE html>", mail.HtmlBody);
        Assert.Contains("https://knk.example/brand/knk-shield.png", mail.HtmlBody);
        Assert.Contains("href=\"https://knk.example/auth/reset-password?token=T\"", mail.HtmlBody);
        Assert.Contains("2026-10-21 12:00 UTC", mail.TextBody);
        Assert.DoesNotContain("{{", mail.TextBody + mail.HtmlBody);
    }

    [Fact]
    public void AValueLookingLikeAPlaceholder_IsNotParsedAgainByTheLayout()
    {
        var mail = Renderer().Render(EmailKind.EmailChanged, new { username = "{{reason}}", newEmailHint = "x" });
        Assert.Contains("Hi {{reason}},", mail.TextBody);
    }

    [Fact]
    public void UnknownPlaceholder_ThrowsWhenStrict_AndRendersEmptyOtherwise()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["known"] = "yes" };

        Assert.Throws<EmailTemplateException>(() => Renderer(strict: true).Fill("{{known}} {{missing}}", values, false, "t"));
        Assert.Equal("yes ", Renderer(strict: false).Fill("{{known}} {{missing}}", values, false, "t"));
        // A model without the template's placeholder fails the same way.
        Assert.Throws<EmailTemplateException>(() => Renderer(strict: true).Render(EmailKind.PasswordReset, new { username = "Steve" }));
    }

    [Fact]
    public void EveryKind_HasBothTemplates_AndRendersItsModel()
    {
        foreach (var kind in EmailKinds.Every)
        {
            var mail = Renderer().Render(kind, EmailTestDb.SampleModel(kind));
            Assert.False(string.IsNullOrWhiteSpace(mail.Subject), kind.ToString());
            Assert.Contains("Knights &amp; Kings", mail.HtmlBody);
        }
        Assert.Equal(Enum.GetValues<EmailKind>().Length, EmailKinds.Every.Count());
    }
}

public class EmailDispatcherTests : IDisposable
{
    private readonly EmailTestDb _t = new();

    public void Dispose() => _t.Dispose();

    private async Task<EmailOutboxMessage> ReloadAsync(long id)
    {
        _t.Db.ChangeTracker.Clear();
        return await _t.Db.EmailOutbox.SingleAsync(m => m.Id == id);
    }

    [Fact]
    public async Task Send_MarksSent_AndClearsTheBodies()
    {
        var row = await _t.QueueTestAsync();

        Assert.Equal(1, await _t.Dispatcher.RunOnceAsync());

        var sent = Assert.Single(_t.Transport.Sent);
        Assert.Equal("steve@example.com", sent.To);
        Assert.Contains("test mail", sent.TextBody);
        var stored = await ReloadAsync(row.Id);
        Assert.Equal(EmailOutboxStatus.Sent, stored.Status);
        Assert.Equal(EmailTestDb.Now, stored.SentAt);
        Assert.Equal(1, stored.Attempts);
        Assert.Equal($"<{row.Id}@test>", stored.ProviderMessageId);
        Assert.Null(stored.TextBody);
        Assert.Null(stored.HtmlBody);
    }

    [Fact]
    public async Task Failure_BacksOff_1_5_15_60_240_Minutes_ThenFailsAfterSixAttempts()
    {
        var row = await _t.QueueTestAsync();
        _t.Transport.FailWith = new InvalidOperationException("relay down");
        var expected = new[] { 1, 5, 15, 60, 240 };

        foreach (var minutes in expected)
        {
            await _t.Dispatcher.RunOnceAsync();
            var pending = await ReloadAsync(row.Id);
            Assert.Equal(EmailOutboxStatus.Pending, pending.Status);
            Assert.Equal(_t.Clock.UtcNow.AddMinutes(minutes), pending.NextAttemptAt);
            Assert.Contains("relay down", pending.LastError);

            // Not due yet: nothing is tried.
            var calls = _t.Transport.Calls;
            await _t.Dispatcher.RunOnceAsync();
            Assert.Equal(calls, _t.Transport.Calls);

            _t.Clock.Advance(TimeSpan.FromMinutes(minutes));
        }

        await _t.Dispatcher.RunOnceAsync();
        var failed = await ReloadAsync(row.Id);
        Assert.Equal(EmailOutboxStatus.Failed, failed.Status);
        Assert.Equal(6, failed.Attempts);
        Assert.Equal(6, _t.Transport.Calls);
        // No token in a Test mail (no ExpiresAt): the bodies stay so staff can retry it.
        Assert.NotNull(failed.TextBody);
    }

    [Fact]
    public async Task Failed_WithAnExpiry_LosesItsBodies()
    {
        _t.Settings.MaxAttempts = 1;
        var row = await _t.QueueTestAsync(kind: EmailKind.PasswordReset, expiresAt: EmailTestDb.Now.AddMinutes(30));
        _t.Transport.FailWith = new InvalidOperationException("auth failed");

        await _t.Dispatcher.RunOnceAsync();

        var failed = await ReloadAsync(row.Id);
        Assert.Equal(EmailOutboxStatus.Failed, failed.Status);
        Assert.Null(failed.TextBody);
        Assert.Null(failed.HtmlBody);
    }

    [Fact]
    public async Task Expired_IsNotSent()
    {
        var row = await _t.QueueTestAsync(kind: EmailKind.PasswordReset, expiresAt: EmailTestDb.Now.AddMinutes(30));
        _t.Clock.Advance(TimeSpan.FromMinutes(31));

        Assert.Equal(0, await _t.Dispatcher.RunOnceAsync());

        Assert.Equal(0, _t.Transport.Calls);
        var expired = await ReloadAsync(row.Id);
        Assert.Equal(EmailOutboxStatus.Expired, expired.Status);
        Assert.Null(expired.TextBody);
    }

    [Fact]
    public async Task OneRun_SendsAtMostTwenty_OldestFirst()
    {
        for (var i = 0; i < 25; i++)
        {
            await _t.QueueTestAsync(to: $"p{i}@example.com");
        }

        Assert.Equal(EmailDispatcher.BatchSize, await _t.Dispatcher.RunOnceAsync());
        Assert.Equal("p0@example.com", _t.Transport.Sent[0].To);
        Assert.Equal(5, await _t.Dispatcher.RunOnceAsync());
    }

    [Fact]
    public async Task Queueing_SignalsTheDispatcher()
    {
        await _t.QueueTestAsync();
        Assert.True(_t.Signal.Reader.TryRead(out _));
    }
}

public class EmailComposerAndThrottleTests : IDisposable
{
    private readonly EmailTestDb _t = new();

    public void Dispose() => _t.Dispose();

    [Fact]
    public async Task Queue_StoresAPendingRow_InTheCallersContext()
    {
        var row = await _t.Composer().QueueAsync(EmailKind.PasswordReset, " Steve@Example.com ",
            EmailTestDb.SampleModel(EmailKind.PasswordReset), "reset:1", EmailTestDb.Now.AddMinutes(30), 7);

        // The caller's own DbContext tracks it, so it commits (or rolls back) with the caller.
        Assert.Contains(row, _t.Db.EmailOutbox.Local);
        Assert.Equal(EmailOutboxStatus.Pending, row.Status);
        Assert.Equal("steve@example.com", row.ToAddress);
        Assert.Equal("PasswordReset", row.Kind);
        Assert.Equal(EmailTestDb.Now.AddMinutes(30), row.ExpiresAt);
        Assert.Equal(7, row.UserId);
        Assert.Equal(EmailTestDb.Now, row.NextAttemptAt);
        Assert.NotNull(row.HtmlBody);
    }

    [Fact]
    public async Task SameDedupeKey_ReturnsTheExistingRow()
    {
        var first = await _t.QueueTestAsync(key: "test:1");
        var second = await _t.QueueTestAsync(key: "test:1");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await _t.Db.EmailOutbox.CountAsync());
    }

    [Fact]
    public async Task FourthResetInAnHour_IsSuppressed_WithoutBodies()
    {
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(EmailOutboxStatus.Pending, (await _t.QueueTestAsync(kind: EmailKind.PasswordReset)).Status);
        }

        var fourth = await _t.QueueTestAsync(kind: EmailKind.PasswordReset);

        Assert.Equal(EmailOutboxStatus.Suppressed, fourth.Status);
        Assert.Null(fourth.TextBody);
        Assert.Null(fourth.HtmlBody);
        Assert.StartsWith("Suppressed: 3 password-reset mails", fourth.LastError);
        // Another class still goes out, and an hour later resets do again.
        Assert.Equal(EmailOutboxStatus.Pending, (await _t.QueueTestAsync(kind: EmailKind.EmailChanged)).Status);
        _t.Clock.Advance(TimeSpan.FromMinutes(61));
        Assert.Equal(EmailOutboxStatus.Pending, (await _t.QueueTestAsync(kind: EmailKind.PasswordReset)).Status);
    }

    [Fact]
    public async Task SeventhMailInAnHour_ToOneAddress_IsSuppressed()
    {
        var kinds = new[] { EmailKind.PasswordReset, EmailKind.PasswordReset, EmailKind.EmailChanged, EmailKind.EmailChanged, EmailKind.Test, EmailKind.Test };
        foreach (var kind in kinds)
        {
            Assert.Equal(EmailOutboxStatus.Pending, (await _t.QueueTestAsync(kind: kind)).Status);
        }

        var seventh = await _t.QueueTestAsync(kind: EmailKind.Welcome);

        Assert.Equal(EmailOutboxStatus.Suppressed, seventh.Status);
        Assert.StartsWith("Suppressed: 6 mails", seventh.LastError);
        Assert.Equal(EmailOutboxStatus.Pending, (await _t.QueueTestAsync(to: "alex@example.com", kind: EmailKind.Welcome)).Status);
    }

    [Fact]
    public async Task DailyCap_SuppressesEveryoneOnceReached()
    {
        _t.Settings.DailyCap = 3;
        for (var i = 0; i < 3; i++)
        {
            await _t.QueueTestAsync(to: $"p{i}@example.com");
        }

        var over = await _t.QueueTestAsync(to: "new@example.com");

        Assert.Equal(EmailOutboxStatus.Suppressed, over.Status);
        Assert.Contains("Email:DailyCap", over.LastError);
        _t.Clock.Advance(TimeSpan.FromHours(25));
        Assert.Equal(EmailOutboxStatus.Pending, (await _t.QueueTestAsync(to: "new@example.com")).Status);
    }
}

public class EmailTransportTests
{
    private static OutgoingEmail Mail() => new(5, "Test", "steve@example.com", null, "Subject", "Hello https://x/reset?token=SECRET", "<p>Hello</p>");

    [Fact]
    public void Credentials_AreOptional_ButAUsernameNeedsAPassword()
    {
        Assert.Null(SmtpCredentials.Resolve(new EmailSettings { SmtpUsername = "" }));
        var ex = Assert.Throws<InvalidOperationException>(() => SmtpCredentials.Resolve(new EmailSettings { SmtpUsername = "me@gmail.com" }));
        Assert.Contains("Email:SmtpPassword", ex.Message);
        var credentials = SmtpCredentials.Resolve(new EmailSettings { SmtpUsername = "me@gmail.com", SmtpPassword = "app-password" });
        Assert.Equal("me@gmail.com", credentials!.UserName);
    }

    private sealed class CapturingSmtpTransport : SmtpEmailTransport
    {
        public CapturingSmtpTransport(EmailSettings settings) : base(Options.Create(settings))
        {
        }

        public MimeMessage? Message { get; private set; }
        public NetworkCredential? Credentials { get; private set; }
        public SecureSocketOptions SocketOptions { get; private set; }

        protected override Task DeliverAsync(MimeMessage message, NetworkCredential? credentials, SecureSocketOptions socketOptions, CancellationToken ct)
        {
            Message = message;
            Credentials = credentials;
            SocketOptions = socketOptions;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Smtp_WithoutAUsername_SendsWithoutAuthentication_AndSetsTheHeaders()
    {
        var transport = new CapturingSmtpTransport(new EmailSettings
        {
            SmtpHost = "mailrelay", UseStartTls = false, SmtpUsername = "",
            FromAddress = "no-reply@knightsandkings.net", ReplyToAddress = "support@knightsandkings.net"
        });

        var messageId = await transport.SendAsync(Mail());

        Assert.Null(transport.Credentials);
        Assert.Equal(SecureSocketOptions.None, transport.SocketOptions);
        var message = transport.Message!;
        Assert.Equal("no-reply@knightsandkings.net", message.From.Mailboxes.Single().Address);
        Assert.Equal("Knights & Kings", message.From.Mailboxes.Single().Name);
        Assert.Equal("support@knightsandkings.net", message.ReplyTo.Mailboxes.Single().Address);
        Assert.Equal("auto-generated", message.Headers["Auto-Submitted"]);
        Assert.EndsWith("@knightsandkings.net", message.MessageId);
        Assert.Equal(message.MessageId, messageId);
        Assert.Equal("<p>Hello</p>", message.HtmlBody);
        Assert.StartsWith("Hello", message.TextBody);
    }

    [Fact]
    public async Task Smtp_WithAUsernameButNoPassword_Refuses()
    {
        var transport = new CapturingSmtpTransport(new EmailSettings { SmtpUsername = "me@gmail.com", FromAddress = "me@gmail.com" });
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => transport.SendAsync(Mail()));
        Assert.Contains("Email:SmtpPassword", ex.Message);
        Assert.Null(transport.Message);
    }

    [Theory]
    [InlineData(587, true, SecureSocketOptions.StartTlsWhenAvailable)]
    [InlineData(587, false, SecureSocketOptions.None)]
    [InlineData(465, false, SecureSocketOptions.SslOnConnect)]
    public void Smtp_SocketOptions_FollowTheSettings(int port, bool startTls, SecureSocketOptions expected)
    {
        Assert.Equal(expected, SmtpEmailTransport.SocketOptions(new EmailSettings { SmtpPort = port, UseStartTls = startTls }));
    }

    [Fact]
    public async Task Pickup_WritesAnEmlFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "knk-pickup-" + Guid.NewGuid().ToString("N"));
        try
        {
            var transport = new PickupEmailTransport(Options.Create(new EmailSettings { PickupDirectory = directory, FromAddress = "no-reply@knightsandkings.net" }));
            await transport.SendAsync(Mail());

            var file = Assert.Single(Directory.GetFiles(directory, "*.eml"));
            var message = await MimeMessage.LoadAsync(file);
            Assert.Equal("Subject", message.Subject);
            Assert.Equal("steve@example.com", message.To.Mailboxes.Single().Address);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Log_OutsideDevelopment_NeverLogsTheBodyOrTheFullAddress()
    {
        var logger = new ListLogger<LogEmailTransport>();
        await new LogEmailTransport(logger, development: false).SendAsync(Mail());

        var line = Assert.Single(logger.Lines);
        Assert.DoesNotContain("SECRET", line);
        Assert.DoesNotContain("steve@example.com", line);
        Assert.Contains("st***@example.com", line);

        var devLogger = new ListLogger<LogEmailTransport>();
        await new LogEmailTransport(devLogger, development: true).SendAsync(Mail());
        Assert.Contains("SECRET", Assert.Single(devLogger.Lines));
    }
}

/// <summary>Collects formatted log lines.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    public List<string> Lines { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Lines.Add(formatter(state, exception));
}

public class EmailStartupGuardTests
{
    private static WebApplicationBuilder Builder(string environment, Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Configuration.Sources.Clear();
        settings["ConnectionStrings:MySqlDbConnection"] = "Server=localhost;Database=x;User=u;Password=p";
        settings["Security:Jwt:Secret"] = new string('k', 48);
        builder.Configuration.AddInMemoryCollection(settings);
        return builder;
    }

    [Theory]
    [InlineData("Log", null, null)]
    [InlineData(null, "Log", null)]
    [InlineData(null, null, null)]
    public void Production_RefusesTheLogTransport_WithoutAllowLogTransport(string? provider, string? transport, string? allow)
    {
        var builder = Builder(Environments.Production, new()
        {
            ["Email:Provider"] = provider, ["Email:Transport"] = transport, ["Email:AllowLogTransport"] = allow
        });
        var ex = Assert.Throws<InvalidOperationException>(() => StartupSecurity.ApplySecretChecks(builder));
        Assert.Contains("AllowLogTransport", ex.Message);
    }

    [Theory]
    [InlineData("Production", "Log", "true")]
    [InlineData("Production", "Smtp", null)]
    [InlineData("Production", "Pickup", null)]
    [InlineData("Development", "Log", null)]
    public void Starts_WithSmtp_OrAnAllowedLogTransport_OrInDevelopment(string environment, string provider, string? allow)
    {
        var builder = Builder(environment, new() { ["Email:Provider"] = provider, ["Email:AllowLogTransport"] = allow });
        StartupSecurity.ApplySecretChecks(builder);
    }

    [Fact]
    public void Transport_WinsOverProvider()
    {
        Assert.Equal("Smtp", new EmailSettings { Provider = "Log", Transport = "smtp" }.EffectiveTransport());
        Assert.Equal("Log", new EmailSettings { Provider = "Smtp", Transport = "Log" }.EffectiveTransport());
        Assert.Equal("Smtp", new EmailSettings { Provider = "Smtp" }.EffectiveTransport());
        Assert.Equal("Log", new EmailSettings { Provider = "", Transport = "" }.EffectiveTransport());
    }
}

public class EmailOutboxAdminServiceTests : IDisposable
{
    private readonly EmailTestDb _t = new();

    public void Dispose() => _t.Dispose();

    private EmailOutboxAdminService Service() => new(_t.Db, _t.Composer(), Options.Create(_t.Settings), _t.Signal, _t.Clock);

    [Fact]
    public async Task List_MasksRecipients_NeverReturnsBodies_AndFilters()
    {
        await _t.QueueTestAsync(to: "steve@example.com");
        await _t.QueueTestAsync(to: "alex@example.com", kind: EmailKind.PasswordReset);
        await _t.Dispatcher.RunOnceAsync();
        await _t.QueueTestAsync(to: "carol@example.com");

        var page = await Service().ListAsync(1, 25, null, null);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal("ca***@example.com", page.Items[0].ToMasked);
        Assert.Equal("Log", page.Transport);
        Assert.Equal(3, page.Last24Hours);
        Assert.Equal(300, page.DailyCap);
        var json = System.Text.Json.JsonSerializer.Serialize(page);
        Assert.DoesNotContain("example.com\"", json.Replace("***@example.com\"", ""));
        Assert.DoesNotContain("Body", json, StringComparison.OrdinalIgnoreCase);

        Assert.Single((await Service().ListAsync(1, 25, "pending", null)).Items);
        Assert.Single((await Service().ListAsync(1, 25, null, "PasswordReset")).Items);
    }

    [Fact]
    public async Task SendTest_GoesToTheCallersOwnAddress_Or400WithoutOne()
    {
        _t.Db.Users.AddRange(
            new User { Id = 1, Username = "Pandi", Email = "pandi@example.com", CreatedAt = DateTime.UtcNow },
            new User { Id = 2, Username = "NoMail", CreatedAt = DateTime.UtcNow });
        await _t.Db.SaveChangesAsync();

        var ok = await Service().SendTestAsync(1);
        Assert.Equal(EmailOutboxActionOutcome.Ok, ok.Outcome);
        Assert.Equal("Test", ok.Item!.Kind);
        Assert.Equal("pa***@example.com", ok.Item.ToMasked);

        Assert.Equal(EmailOutboxActionOutcome.NoEmail, (await Service().SendTestAsync(2)).Outcome);
    }

    [Fact]
    public async Task Retry_PutsAFailedMailBack_UnlessItsContentWasRemoved()
    {
        _t.Settings.MaxAttempts = 1;
        _t.Transport.FailWith = new InvalidOperationException("down");
        var plain = await _t.QueueTestAsync();
        var withToken = await _t.QueueTestAsync(kind: EmailKind.PasswordReset, expiresAt: EmailTestDb.Now.AddMinutes(30));
        await _t.Dispatcher.RunOnceAsync();
        _t.Db.ChangeTracker.Clear();
        while (_t.Signal.Reader.TryRead(out _))
        {
        }

        var retried = await Service().RetryAsync(plain.Id);
        Assert.Equal(EmailOutboxActionOutcome.Ok, retried.Outcome);
        Assert.Equal("Pending", retried.Item!.Status);
        Assert.Equal(0, retried.Item.Attempts);
        Assert.True(_t.Signal.Reader.TryRead(out _));

        Assert.Equal(EmailOutboxActionOutcome.NotRetryable, (await Service().RetryAsync(withToken.Id)).Outcome);
        Assert.Equal(EmailOutboxActionOutcome.NotRetryable, (await Service().RetryAsync(plain.Id)).Outcome); // Pending now
        Assert.Equal(EmailOutboxActionOutcome.NotFound, (await Service().RetryAsync(999)).Outcome);

        _t.Transport.FailWith = null;
        Assert.Equal(1, await _t.Dispatcher.RunOnceAsync());
    }

    [Fact]
    public async Task Controller_MapsTheOutcomes()
    {
        var service = new Mock<IEmailOutboxAdminService>();
        service.Setup(s => s.RetryAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new EmailOutboxActionResult(EmailOutboxActionOutcome.NotFound));
        service.Setup(s => s.RetryAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(new EmailOutboxActionResult(EmailOutboxActionOutcome.NotRetryable));
        service.Setup(s => s.SendTestAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(new EmailOutboxActionResult(EmailOutboxActionOutcome.NoEmail));
        var controller = new EmailOutboxController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                        new[] { new System.Security.Claims.Claim("uid", "5") }, "test"))
                }
            }
        };

        Assert.IsType<NotFoundObjectResult>(await controller.Retry(1));
        Assert.IsType<ConflictObjectResult>(await controller.Retry(2));
        Assert.IsType<BadRequestObjectResult>(await controller.SendTest());
    }
}

public class EmailCallSiteTests
{
    [Fact]
    public async Task PrivacyMails_QueueTheirKinds_WithTheTokenExpiry_AndNoTokenInTheKey()
    {
        var composer = new CapturingEmailComposer();
        var service = new PrivacyEmailService(composer);
        var expires = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);

        await service.SendConfirmationAsync("steve@example.com", "Steve", "https://knk.example/account/delete-data/confirm?token=SECRET", expires);
        await service.SendScheduledAsync("steve@example.com", "Steve", expires.AddDays(5), "https://knk.example/account", filedByStaff: true);

        var confirm = composer.Queued[0];
        Assert.Equal(EmailKind.PrivacyDeletionConfirm, confirm.Kind);
        Assert.Equal(expires, confirm.ExpiresAt);
        Assert.DoesNotContain("SECRET", confirm.DedupeKey);
        var scheduled = composer.Queued[1];
        Assert.Equal(EmailKind.PrivacyDeletionScheduled, scheduled.Kind);
        Assert.Null(scheduled.ExpiresAt);
        Assert.Contains("A staff member filed a request", scheduled.Values["intro"]);
    }

    [Fact]
    public async Task StaffEmailChange_NoticesTheOldAddress()
    {
        var users = new Mock<IUserService>();
        users.Setup(s => s.GetByIdAsync(3)).ReturnsAsync(new UserDto { Id = 3, Username = "Steve", Email = "steve@example.com" });
        users.Setup(s => s.CheckEmailTakenAsync("steve.new@example.com", 3)).ReturnsAsync((false, (int?)null));
        var composer = new CapturingEmailComposer();
        var controller = new UsersController(users.Object, new Mock<IMapper>().Object, new Mock<IPermissionResolutionService>().Object,
            new Mock<ISalaryService>().Object, new Mock<IUserProfileSummaryService>().Object, new Mock<IUserPermissionGroupService>().Object,
            new Mock<IPermissionGrantService>().Object, new Mock<IPermissionEscalationGuard>().Object, emailComposer: composer);

        var result = await controller.UpdateEmail(3, new UpdateEmailDto { NewEmail = "steve.new@example.com" });

        Assert.IsType<NoContentResult>(result);
        var notice = Assert.Single(composer.Queued);
        Assert.Equal(EmailKind.EmailChanged, notice.Kind);
        Assert.Equal("steve@example.com", notice.To);
        Assert.Equal("st***@example.com", notice.Values["newEmailHint"]);
        Assert.Equal(3, notice.UserId);
        Assert.StartsWith("email-changed:3:", notice.DedupeKey);
    }
}
