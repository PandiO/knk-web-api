using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using knkwebapi_v2.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.Privacy
{
    /// <summary>Emails of the GDPR deletion flow (DESIGN.md §F.14). Chosen like the password reset
    /// mail by Email:Provider ("Smtp" sends, anything else logs).</summary>
    public interface IPrivacyEmailService
    {
        /// <summary>The link a player must open to confirm their own deletion request.</summary>
        Task SendConfirmationAsync(string recipientEmail, string username, string confirmUrl, DateTime expiresAtUtc);

        /// <summary>The deletion is scheduled; it can still be cancelled on the account page until then.</summary>
        Task SendScheduledAsync(string recipientEmail, string username, DateTime scheduledAtUtc, string accountUrl, bool filedByStaff);
    }

    public static class PrivacyEmailTexts
    {
        public const string ConfirmSubject = "Confirm the deletion of your Knights & Kings data";
        public const string ScheduledSubject = "Your Knights & Kings data will be deleted";

        public static string ConfirmBody(string username, string confirmUrl, DateTime expiresAtUtc) =>
            $"Hi {username},\n\n" +
            "You asked us to delete your Knights & Kings account data: your statistics, settings, discoveries, " +
            "private-message logs and your username, email and Minecraft link. This cannot be undone.\n\n" +
            $"To confirm, open this link before {expiresAtUtc:yyyy-MM-dd HH:mm} UTC:\n\n{confirmUrl}\n\n" +
            "After you confirm, the deletion waits five days so you can still cancel it on your account page.\n\n" +
            "If you didn't ask for this, ignore this email and nothing will happen.";

        public static string ScheduledBody(string username, DateTime scheduledAtUtc, string accountUrl, bool filedByStaff) =>
            $"Hi {username},\n\n" +
            (filedByStaff
                ? "A staff member filed a request, on your behalf, to delete your Knights & Kings account data.\n\n"
                : "You confirmed the deletion of your Knights & Kings account data.\n\n") +
            $"It will be deleted on {scheduledAtUtc:yyyy-MM-dd HH:mm} UTC. Until then you can cancel it on your account page:\n\n" +
            $"{accountUrl}\n\n" +
            "After that the deletion cannot be undone.";
    }

    public sealed class LogPrivacyEmailService : IPrivacyEmailService
    {
        private readonly ILogger<LogPrivacyEmailService> _logger;

        public LogPrivacyEmailService(ILogger<LogPrivacyEmailService> logger)
        {
            _logger = logger;
        }

        public Task SendConfirmationAsync(string recipientEmail, string username, string confirmUrl, DateTime expiresAtUtc)
        {
            _logger.LogInformation("Data deletion confirmation for {Username} <{Email}>: {Url} (valid until {ExpiresAt:u})",
                username, recipientEmail, confirmUrl, expiresAtUtc);
            return Task.CompletedTask;
        }

        public Task SendScheduledAsync(string recipientEmail, string username, DateTime scheduledAtUtc, string accountUrl, bool filedByStaff)
        {
            _logger.LogInformation("Data deletion of {Username} <{Email}> scheduled for {ScheduledAt:u} (staff: {Staff})",
                username, recipientEmail, scheduledAtUtc, filedByStaff);
            return Task.CompletedTask;
        }
    }

    public sealed class SmtpPrivacyEmailService : IPrivacyEmailService
    {
        private readonly EmailSettings _settings;
        private readonly ILogger<SmtpPrivacyEmailService> _logger;

        public SmtpPrivacyEmailService(IOptions<EmailSettings> settings, ILogger<SmtpPrivacyEmailService> logger)
        {
            _settings = settings.Value;
            _logger = logger;
        }

        public Task SendConfirmationAsync(string recipientEmail, string username, string confirmUrl, DateTime expiresAtUtc) =>
            SendAsync(recipientEmail, PrivacyEmailTexts.ConfirmSubject, PrivacyEmailTexts.ConfirmBody(username, confirmUrl, expiresAtUtc));

        public Task SendScheduledAsync(string recipientEmail, string username, DateTime scheduledAtUtc, string accountUrl, bool filedByStaff) =>
            SendAsync(recipientEmail, PrivacyEmailTexts.ScheduledSubject,
                PrivacyEmailTexts.ScheduledBody(username, scheduledAtUtc, accountUrl, filedByStaff));

        private async Task SendAsync(string recipientEmail, string subject, string body)
        {
            if (string.IsNullOrWhiteSpace(_settings.SmtpUsername) || string.IsNullOrWhiteSpace(_settings.SmtpPassword))
            {
                throw new InvalidOperationException(
                    "Email:SmtpUsername/Email:SmtpPassword are not configured. Set Email__SmtpPassword via environment variable or user-secrets.");
            }

            using var message = new MailMessage
            {
                From = new MailAddress(_settings.FromAddress, _settings.FromName),
                Subject = subject,
                Body = body,
                IsBodyHtml = false
            };
            message.To.Add(recipientEmail);

            using var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
            {
                EnableSsl = _settings.UseStartTls,
                Credentials = new NetworkCredential(_settings.SmtpUsername, _settings.SmtpPassword)
            };

            await client.SendMailAsync(message);
            _logger.LogInformation("Privacy email \"{Subject}\" sent to {Email} via SMTP", subject, recipientEmail);
        }
    }
}
