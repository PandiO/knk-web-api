using System.Threading.Tasks;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Delivers password reset links to users.
    /// </summary>
    public interface IPasswordResetDeliveryService
    {
        /// <summary>
        /// Sends a password reset link to a recipient email.
        /// </summary>
        Task SendPasswordResetAsync(string recipientEmail, string? username, string resetUrl);

        /// <summary>
        /// Tells the previous address that the account's email was changed (closed-alpha WP6,
        /// decision D8: no verification during the alpha, so the old owner at least hears of it).
        /// </summary>
        Task SendEmailChangedNoticeAsync(string previousEmail, string? username, string newEmailHint);
    }
}
