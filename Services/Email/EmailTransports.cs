using System.Net;
using knkwebapi_v2.Configuration;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Utils;

namespace knkwebapi_v2.Services.Email
{
    /// <summary>One outbox row on its way out.</summary>
    public sealed record OutgoingEmail(long Id, string Kind, string To, string? ToName, string Subject, string? TextBody, string? HtmlBody);

    /// <summary>Hands a mail to the outside world. Throws when it can't (EmailDispatcher retries);
    /// returns the Message-ID it was sent with, if any.</summary>
    public interface IEmailTransport
    {
        Task<string?> SendAsync(OutgoingEmail email, CancellationToken ct = default);
    }

    /// <summary>"st***@example.com": enough to recognise an address in a log or on /admin/email.</summary>
    public static class EmailMask
    {
        public static string Mask(string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return "***";
            var at = email.IndexOf('@');
            if (at <= 0) return "***";
            var local = email[..at];
            return (local.Length <= 2 ? local[..1] : local[..2]) + "***" + email[at..];
        }
    }

    /// <summary>Builds the MIME message both real transports send (DESIGN.md § 4.2 headers).</summary>
    public static class EmailMessageFactory
    {
        public const string DefaultDomain = "knightsandkings.net";

        public static MimeMessage Build(OutgoingEmail email, EmailSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.FromAddress))
            {
                throw new InvalidOperationException("Email:FromAddress is not set.");
            }

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress.Trim()));
            message.To.Add(new MailboxAddress(email.ToName ?? string.Empty, email.To));
            if (!string.IsNullOrWhiteSpace(settings.ReplyToAddress))
            {
                message.ReplyTo.Add(MailboxAddress.Parse(settings.ReplyToAddress.Trim()));
            }
            message.Subject = email.Subject;
            message.Headers.Add("Auto-Submitted", "auto-generated");
            message.MessageId = MimeUtils.GenerateMessageId(DomainOf(settings.FromAddress));

            var body = new BodyBuilder { TextBody = email.TextBody, HtmlBody = email.HtmlBody };
            message.Body = body.ToMessageBody();
            return message;
        }

        private static string DomainOf(string address)
        {
            var at = address.LastIndexOf('@');
            return at > 0 && at < address.Length - 1 ? address[(at + 1)..].Trim() : DefaultDomain;
        }
    }

    /// <summary>
    /// SMTP authentication is optional (KNG-211): with <c>Email:SmtpUsername</c> empty the mail goes
    /// out without authenticating, for an internal relay such as the alpha's mailrelay container.
    /// With a username set, the password is still required.
    /// </summary>
    public static class SmtpCredentials
    {
        public static NetworkCredential? Resolve(EmailSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.SmtpUsername))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(settings.SmtpPassword))
            {
                throw new InvalidOperationException(
                    "Email:SmtpUsername is set but Email:SmtpPassword is not. Set Email__SmtpPassword via environment variable or user-secrets, or clear Email:SmtpUsername to send without authentication.");
            }

            return new NetworkCredential(settings.SmtpUsername, settings.SmtpPassword);
        }
    }

    /// <summary>Sends through an SMTP server with MailKit (STARTTLS when offered, or plain for an internal relay).</summary>
    public class SmtpEmailTransport : IEmailTransport
    {
        private readonly EmailSettings _settings;

        public SmtpEmailTransport(IOptions<EmailSettings> settings)
        {
            _settings = settings.Value;
        }

        public async Task<string?> SendAsync(OutgoingEmail email, CancellationToken ct = default)
        {
            var credentials = SmtpCredentials.Resolve(_settings);
            var message = EmailMessageFactory.Build(email, _settings);
            await DeliverAsync(message, credentials, SocketOptions(_settings), ct);
            return message.MessageId;
        }

        public static SecureSocketOptions SocketOptions(EmailSettings settings) =>
            settings.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect
            : settings.UseStartTls ? SecureSocketOptions.StartTlsWhenAvailable
            : SecureSocketOptions.None;

        /// <summary>Connects, authenticates only with credentials, and sends. A seam for tests.</summary>
        protected virtual async Task DeliverAsync(MimeMessage message, NetworkCredential? credentials, SecureSocketOptions socketOptions, CancellationToken ct)
        {
            using var client = new MailKit.Net.Smtp.SmtpClient { Timeout = 30_000 };
            await client.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, socketOptions, ct);
            if (credentials != null)
            {
                await client.AuthenticateAsync(credentials, ct);
            }
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
        }
    }

    /// <summary>Logs instead of sending. Links (the text body) only in Development; elsewhere just
    /// kind, masked recipient and subject (allowed there only with Email:AllowLogTransport).</summary>
    public class LogEmailTransport : IEmailTransport
    {
        private readonly ILogger<LogEmailTransport> _logger;
        private readonly bool _development;

        public LogEmailTransport(ILogger<LogEmailTransport> logger, IWebHostEnvironment? environment = null)
            : this(logger, environment?.IsDevelopment() ?? false)
        {
        }

        public LogEmailTransport(ILogger<LogEmailTransport> logger, bool development)
        {
            _logger = logger;
            _development = development;
        }

        public Task<string?> SendAsync(OutgoingEmail email, CancellationToken ct = default)
        {
            if (_development)
            {
                _logger.LogInformation("Mail {Kind} to {To}: {Subject}\n{TextBody}", email.Kind, email.To, email.Subject, email.TextBody);
            }
            else
            {
                _logger.LogInformation("Mail {Kind} to {To}: {Subject} (Log transport: not sent)", email.Kind, EmailMask.Mask(email.To), email.Subject);
            }
            return Task.FromResult<string?>(null);
        }
    }

    /// <summary>Writes each mail as an .eml file to Email:PickupDirectory (dev and tests).</summary>
    public class PickupEmailTransport : IEmailTransport
    {
        private readonly EmailSettings _settings;

        public PickupEmailTransport(IOptions<EmailSettings> settings)
        {
            _settings = settings.Value;
        }

        public async Task<string?> SendAsync(OutgoingEmail email, CancellationToken ct = default)
        {
            var directory = string.IsNullOrWhiteSpace(_settings.PickupDirectory) ? "mail-pickup" : _settings.PickupDirectory;
            Directory.CreateDirectory(directory);
            var message = EmailMessageFactory.Build(email, _settings);
            var path = Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMddHHmmss}-{email.Id}-{Guid.NewGuid():N}.eml");
            await message.WriteToAsync(path, ct);
            return message.MessageId;
        }
    }
}
