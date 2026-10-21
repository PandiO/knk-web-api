using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using knkwebapi_v2.Services.Email;

namespace knkwebapi_v2.Services.Privacy
{
    /// <summary>Emails of the GDPR deletion flow (DESIGN.md §F.14), queued in the mail outbox
    /// (KNG-206); the transport (Email:Transport) decides whether they are sent or logged.</summary>
    public interface IPrivacyEmailService
    {
        /// <summary>The link a player must open to confirm their own deletion request.</summary>
        Task SendConfirmationAsync(string recipientEmail, string username, string confirmUrl, DateTime expiresAtUtc);

        /// <summary>The deletion is scheduled; it can still be cancelled on the account page until then.</summary>
        Task SendScheduledAsync(string recipientEmail, string username, DateTime scheduledAtUtc, string accountUrl, bool filedByStaff);
    }

    /// <summary>Queues the privacy mails through <see cref="IEmailComposer"/> (templates
    /// privacy-deletion-confirm / privacy-deletion-scheduled).</summary>
    public sealed class PrivacyEmailService : IPrivacyEmailService
    {
        private readonly IEmailComposer _composer;

        public PrivacyEmailService(IEmailComposer composer)
        {
            _composer = composer;
        }

        public Task SendConfirmationAsync(string recipientEmail, string username, string confirmUrl, DateTime expiresAtUtc) =>
            // One mail per confirmation token: the key is a hash of the link, never the token itself.
            _composer.QueueAsync(EmailKind.PrivacyDeletionConfirm, recipientEmail,
                new { username, confirmUrl, expiresAt = expiresAtUtc },
                $"privacy-confirm:{ShortHash(confirmUrl)}", expiresAtUtc);

        public Task SendScheduledAsync(string recipientEmail, string username, DateTime scheduledAtUtc, string accountUrl, bool filedByStaff) =>
            _composer.QueueAsync(EmailKind.PrivacyDeletionScheduled, recipientEmail,
                new
                {
                    username,
                    intro = filedByStaff
                        ? "A staff member filed a request, on your behalf, to delete your Knights & Kings account data."
                        : "You confirmed the deletion of your Knights & Kings account data.",
                    scheduledAt = scheduledAtUtc,
                    accountUrl
                },
                $"privacy-scheduled:{ShortHash(recipientEmail.Trim().ToLowerInvariant())}:{scheduledAtUtc.Ticks}");

        private static string ShortHash(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..32].ToLowerInvariant();
    }
}
