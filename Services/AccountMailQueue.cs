using System.Threading.Channels;

namespace knkwebapi_v2.Services
{
    public enum AccountMailKind
    {
        PasswordReset,
        EmailChangedNotice
    }

    /// <summary>One account email to send; <see cref="Payload"/> is the reset URL or the new-address hint.</summary>
    public sealed record AccountMail(AccountMailKind Kind, string To, string? Username, string Payload);

    /// <summary>
    /// Account emails go through this in-memory queue (closed-alpha hardening WP6.7, SEC-13): the
    /// request that triggers one answers at once and the same way whether or not SMTP works, and
    /// <see cref="AccountMailSender"/> delivers in the background. A restart loses queued mail;
    /// the player can simply ask again.
    /// </summary>
    public interface IAccountMailQueue
    {
        /// <summary>False when the queue is full (the mail is dropped and logged).</summary>
        bool Enqueue(AccountMail mail);

        ChannelReader<AccountMail> Reader { get; }
    }

    public class AccountMailQueue : IAccountMailQueue
    {
        public const int Capacity = 500;

        // FullMode.Wait: TryWrite then answers false when the queue is full (it never blocks), so a
        // dropped mail is reported and logged. DropWrite would make TryWrite report success while
        // discarding the mail.
        private readonly Channel<AccountMail> _channel = Channel.CreateBounded<AccountMail>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });
        private readonly ILogger<AccountMailQueue> _logger;

        public AccountMailQueue(ILogger<AccountMailQueue> logger)
        {
            _logger = logger;
        }

        public ChannelReader<AccountMail> Reader => _channel.Reader;

        public bool Enqueue(AccountMail mail)
        {
            if (_channel.Writer.TryWrite(mail))
            {
                return true;
            }
            _logger.LogError("Account mail queue is full: dropped a {Kind} mail", mail.Kind);
            return false;
        }
    }

    /// <summary>Sends queued account mail through IPasswordResetDeliveryService; failures are logged, never thrown.</summary>
    public class AccountMailSender : BackgroundService
    {
        private readonly IAccountMailQueue _queue;
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<AccountMailSender> _logger;

        public AccountMailSender(IAccountMailQueue queue, IServiceScopeFactory scopes, ILogger<AccountMailSender> logger)
        {
            _queue = queue;
            _scopes = scopes;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await foreach (var mail in _queue.Reader.ReadAllAsync(stoppingToken))
                {
                    await SendOneAsync(mail);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }

        public async Task SendOneAsync(AccountMail mail)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var delivery = scope.ServiceProvider.GetRequiredService<IPasswordResetDeliveryService>();
                switch (mail.Kind)
                {
                    case AccountMailKind.PasswordReset:
                        await delivery.SendPasswordResetAsync(mail.To, mail.Username, mail.Payload);
                        break;
                    case AccountMailKind.EmailChangedNotice:
                        await delivery.SendEmailChangedNoticeAsync(mail.To, mail.Username, mail.Payload);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send a {Kind} mail", mail.Kind);
            }
        }
    }
}
