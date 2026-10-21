using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using knkwebapi_v2.Configuration;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Sends password reset emails over SMTP (e.g. Gmail with an app password).
    /// </summary>
    public class SmtpPasswordResetDeliveryService : IPasswordResetDeliveryService
    {
        private readonly EmailSettings _settings;
        private readonly ILogger<SmtpPasswordResetDeliveryService> _logger;

        public SmtpPasswordResetDeliveryService(IOptions<EmailSettings> settings, ILogger<SmtpPasswordResetDeliveryService> logger)
        {
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task SendPasswordResetAsync(string recipientEmail, string? username, string resetUrl)
        {
            var greetingName = string.IsNullOrWhiteSpace(username) ? "there" : username;
            await SendAsync(recipientEmail, "Reset your Knights & Kings password",
                $"Hi {greetingName},\n\n" +
                "We received a request to reset your Knights & Kings password. " +
                $"Click the link below to choose a new password:\n\n{resetUrl}\n\n" +
                "If you didn't request this, you can safely ignore this email.");
            _logger.LogInformation("Password reset email sent to {Email} via SMTP", recipientEmail);
        }

        public async Task SendEmailChangedNoticeAsync(string previousEmail, string? username, string newEmailHint)
        {
            var greetingName = string.IsNullOrWhiteSpace(username) ? "there" : username;
            await SendAsync(previousEmail, "Your Knights & Kings email was changed",
                $"Hi {greetingName},\n\n" +
                $"The email address of your Knights & Kings account was just changed to {newEmailHint}. " +
                "Every session was signed out.\n\n" +
                "If you didn't do this, contact the staff team right away.");
            _logger.LogInformation("Email-changed notice sent to {Email} via SMTP", previousEmail);
        }

        private async Task SendAsync(string recipientEmail, string subject, string body)
        {
            var credentials = SmtpCredentials.Resolve(_settings);

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
                Credentials = credentials
            };

            await DeliverAsync(client, message);
        }

        /// <summary>Hands the built message to the client. A seam for tests; production sends it.</summary>
        protected virtual Task DeliverAsync(SmtpClient client, MailMessage message) => client.SendMailAsync(message);
    }

    /// <summary>
    /// SMTP authentication is optional (KNG-211): with <c>Email:SmtpUsername</c> empty the mail goes
    /// out without credentials, for an internal relay such as the alpha's mailrelay container. With
    /// a username set, the password is still required.
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
}
