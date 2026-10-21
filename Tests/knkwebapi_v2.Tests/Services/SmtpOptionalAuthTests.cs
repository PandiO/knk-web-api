using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Privacy;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// KNG-211 (E0): SMTP authentication is optional. With Email:SmtpUsername empty the reset and
/// privacy mails are built and handed to the client without credentials (the alpha's internal
/// mailrelay container); with a username set the password is still required.
/// </summary>
public class SmtpOptionalAuthTests
{
    private sealed class CapturingResetService : SmtpPasswordResetDeliveryService
    {
        public SmtpClient? Client;
        public MailMessage? Message;

        public CapturingResetService(EmailSettings settings)
            : base(Options.Create(settings), NullLogger<SmtpPasswordResetDeliveryService>.Instance)
        {
        }

        protected override Task DeliverAsync(SmtpClient client, MailMessage message)
        {
            Client = client;
            Message = new MailMessage(message.From!.Address, message.To[0].Address, message.Subject, message.Body);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingPrivacyService : SmtpPrivacyEmailService
    {
        public SmtpClient? Client;
        public MailMessage? Message;

        public CapturingPrivacyService(EmailSettings settings)
            : base(Options.Create(settings), NullLogger<SmtpPrivacyEmailService>.Instance)
        {
        }

        protected override Task DeliverAsync(SmtpClient client, MailMessage message)
        {
            Client = client;
            Message = new MailMessage(message.From!.Address, message.To[0].Address, message.Subject, message.Body);
            return Task.CompletedTask;
        }
    }

    private static EmailSettings Relay(string username = "", string password = "") => new()
    {
        Provider = "Smtp",
        SmtpHost = "mailrelay",
        SmtpPort = 587,
        UseStartTls = false,
        SmtpUsername = username,
        SmtpPassword = password,
        FromAddress = "knightsandkingsmc@gmail.com"
    };

    [Fact]
    public async Task PasswordReset_WithoutUsername_SendsWithoutCredentials()
    {
        var service = new CapturingResetService(Relay());

        await service.SendPasswordResetAsync("steve@example.com", "Steve", "https://knk.test/reset?token=x");

        Assert.NotNull(service.Client);
        Assert.Null(service.Client!.Credentials);
        Assert.False(service.Client.EnableSsl);
        Assert.Equal("mailrelay", service.Client.Host);
        Assert.Equal("steve@example.com", service.Message!.To[0].Address);
        Assert.Contains("https://knk.test/reset?token=x", service.Message.Body);
    }

    [Fact]
    public async Task PasswordReset_WithUsernameAndPassword_Authenticates()
    {
        var service = new CapturingResetService(Relay("knightsandkingsmc@gmail.com", "app-password"));

        await service.SendEmailChangedNoticeAsync("old@example.com", "Steve", "n***@example.com");

        var credential = Assert.IsType<NetworkCredential>(service.Client!.Credentials);
        Assert.Equal("knightsandkingsmc@gmail.com", credential.UserName);
        Assert.Equal("app-password", credential.Password);
    }

    [Fact]
    public async Task PasswordReset_WithUsernameButNoPassword_StillThrows()
    {
        var service = new CapturingResetService(Relay("knightsandkingsmc@gmail.com"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SendPasswordResetAsync("steve@example.com", "Steve", "https://knk.test/reset"));

        Assert.Contains("Email:SmtpPassword", ex.Message);
        Assert.Null(service.Client);
    }

    [Fact]
    public async Task Privacy_WithoutUsername_SendsWithoutCredentials()
    {
        var service = new CapturingPrivacyService(Relay());

        await service.SendConfirmationAsync("steve@example.com", "Steve", "https://knk.test/confirm", DateTime.UtcNow.AddHours(1));

        Assert.NotNull(service.Client);
        Assert.Null(service.Client!.Credentials);
        Assert.Equal(PrivacyEmailTexts.ConfirmSubject, service.Message!.Subject);
    }

    [Fact]
    public async Task Privacy_WithUsernameButNoPassword_StillThrows()
    {
        var service = new CapturingPrivacyService(Relay("knightsandkingsmc@gmail.com"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SendScheduledAsync("steve@example.com", "Steve", DateTime.UtcNow.AddDays(5), "https://knk.test/account", false));
        Assert.Null(service.Client);
    }

    [Fact]
    public void Resolve_WhitespaceUsername_CountsAsEmpty()
    {
        Assert.Null(SmtpCredentials.Resolve(Relay("   ", "ignored")));
    }
}
