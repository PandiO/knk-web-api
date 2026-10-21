using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Services.Email;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// KNG-206 on a real MySQL: an outbox row is written in the caller's transaction, so a rolled-back
/// change never mails and a committed one always does; email_outbox.DedupeKey is unique.
/// </summary>
[Trait("Category", "requires-mysql")]
public class EmailOutboxMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;

    public EmailOutboxMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private static EmailComposer Composer(knkwebapi_v2.Properties.KnKDbContext ctx)
    {
        var settings = Options.Create(new EmailSettings());
        return new EmailComposer(ctx, new EmailTemplateRenderer(settings.Value, "https://knk.example", strict: true),
            new EmailThrottle(ctx, settings, new MemoryCache(new MemoryCacheOptions())));
    }

    [MySqlFact]
    public async Task RolledBackTransaction_LeavesNoMail_CommittedOneKeepsIt()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        await using (var ctx = _db.NewContext())
        {
            await using var tx = await ctx.Database.BeginTransactionAsync();
            await Composer(ctx).QueueAsync(EmailKind.Test, $"a-{tag}@example.com", new { username = "A", sentAt = DateTime.UtcNow }, $"rollback:{tag}");
            await tx.RollbackAsync();
        }
        await using (var ctx = _db.NewContext())
        {
            await using var tx = await ctx.Database.BeginTransactionAsync();
            await Composer(ctx).QueueAsync(EmailKind.Test, $"b-{tag}@example.com", new { username = "B", sentAt = DateTime.UtcNow }, $"commit:{tag}");
            await tx.CommitAsync();
        }

        await using var check = _db.NewContext();
        Assert.False(await check.EmailOutbox.AnyAsync(m => m.DedupeKey == $"rollback:{tag}"));
        Assert.True(await check.EmailOutbox.AnyAsync(m => m.DedupeKey == $"commit:{tag}"));
    }

    [MySqlFact]
    public async Task DedupeKey_IsUnique()
    {
        var key = "dup:" + Guid.NewGuid().ToString("N");
        await using var ctx = _db.NewContext();
        ctx.EmailOutbox.Add(new knkwebapi_v2.Models.EmailOutboxMessage { Kind = "Test", ToAddress = "x@example.com", Subject = "s", DedupeKey = key, CreatedAt = DateTime.UtcNow, NextAttemptAt = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        ctx.EmailOutbox.Add(new knkwebapi_v2.Models.EmailOutboxMessage { Kind = "Test", ToAddress = "x@example.com", Subject = "s", DedupeKey = key, CreatedAt = DateTime.UtcNow, NextAttemptAt = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }
}
