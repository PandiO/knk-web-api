using knkwebapi_v2.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace knkwebapi_v2.Tests.Security;

public class AccountMailQueueTests
{
    [Fact]
    public void Enqueue_WhenFull_ReportsTheDrop()
    {
        var queue = new AccountMailQueue(NullLogger<AccountMailQueue>.Instance);
        var mail = new AccountMail(AccountMailKind.PasswordReset, "player@example.test", "player", "https://example.test/reset");

        for (var i = 0; i < AccountMailQueue.Capacity; i++)
        {
            Assert.True(queue.Enqueue(mail));
        }

        // A full queue must say so: the caller logs it instead of believing the mail was queued.
        Assert.False(queue.Enqueue(mail));
    }
}
