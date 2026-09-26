using System.Text.Json;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// KNG-18 Phase 3: the server-side PM log (docs/specs/private-messages/DESIGN.md §3.1/§3.2) -
/// batches are idempotent on ClientMessageId and capped at 200, user ids come from the UUIDs,
/// the read path filters/pages newest first, and every read writes a PrivateMessagesViewed audit
/// entry. Real repositories over the in-memory provider.
/// </summary>
public class PrivateMessageLogServiceTests
{
    private const string AliceUuid = "00000000-0000-0000-0000-00000000000a";
    private const string BobUuid = "00000000-0000-0000-0000-00000000000b";
    private const string CarolUuid = "00000000-0000-0000-0000-00000000000c";

    private readonly string _dbName = $"PrivateMessageLog_{Guid.NewGuid()}";

    public PrivateMessageLogServiceTests()
    {
        using var db = NewContext();
        db.Users.AddRange(
            new User { Id = 1, Username = "alice", Uuid = AliceUuid },
            new User { Id = 2, Username = "bob", Uuid = BobUuid },
            new User { Id = 3, Username = "carol", Uuid = CarolUuid },
            new User { Id = 9, Username = "owner" });
        db.SaveChanges();
    }

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_dbName).Options);

    private static PrivateMessageLogService NewService(KnKDbContext db) =>
        new(new PrivateMessageLogRepository(db),
            new AuditLogService(new AuditLogRepository(db), new UserRepository(db)),
            NullLogger<PrivateMessageLogService>.Instance);

    private async Task<PrivateMessageLogBatchResultDto> AddAsync(params CreatePrivateMessageLogEntryDto[] entries)
    {
        await using var db = NewContext();
        return await NewService(db).AddBatchAsync(entries);
    }

    private async Task<PagedResultDto<PrivateMessageLogEntryDto>> SearchAsync(PrivateMessageLogQueryDto query, int? viewer = 9)
    {
        await using var db = NewContext();
        return await NewService(db).SearchAsync(query, viewer);
    }

    private static CreatePrivateMessageLogEntryDto Pm(string? fromUuid, string fromName, string? toUuid, string toName,
        string text, DateTime? at = null, Guid? id = null,
        PrivateMessageOutcome outcome = PrivateMessageOutcome.Delivered, bool viaReply = false) => new()
    {
        ClientMessageId = id ?? Guid.NewGuid(),
        SentAt = at ?? DateTime.UtcNow,
        SenderUuid = fromUuid,
        SenderName = fromName,
        RecipientUuid = toUuid,
        RecipientName = toName,
        Content = text,
        Outcome = outcome,
        ViaReply = viaReply
    };

    // ===== Batch =====

    [Fact]
    public async Task Batch_StoresEntries_WithUserIdsResolvedFromUuids()
    {
        var at = new DateTime(2026, 9, 20, 19, 4, 5, DateTimeKind.Utc);
        var result = await AddAsync(
            Pm(AliceUuid, "Alice", BobUuid, "Bob", "hi", at, viaReply: true),
            Pm(null, "CONSOLE", AliceUuid.ToUpperInvariant(), "Alice", "server says hi", at),
            Pm(BobUuid, "Bob", "00000000-0000-0000-0000-0000000000ff", "Stranger", "who?", at,
                outcome: PrivateMessageOutcome.BlockedIgnored));

        Assert.Equal((3, 0), (result.Accepted, result.Duplicates));
        await using var db = NewContext();
        var rows = await db.PrivateMessageLogEntries.OrderBy(e => e.Id).ToListAsync();
        Assert.Equal((1, 2, "Alice", "Bob", "hi", true), (rows[0].SenderUserId, rows[0].RecipientUserId,
            rows[0].SenderName, rows[0].RecipientName, rows[0].Content, rows[0].ViaReply));
        Assert.Equal(at, rows[0].SentAt);
        Assert.Equal((null, 1), (rows[1].SenderUserId, rows[1].RecipientUserId));
        Assert.Equal((2, (int?)null), (rows[2].SenderUserId, rows[2].RecipientUserId));
        Assert.Equal(PrivateMessageOutcome.BlockedIgnored, rows[2].Outcome);
    }

    [Fact]
    public async Task Batch_Resent_StoresNothingTwice()
    {
        var first = Pm(AliceUuid, "Alice", BobUuid, "Bob", "one");
        var second = Pm(AliceUuid, "Alice", BobUuid, "Bob", "two");

        Assert.Equal((1, 0), Pair(await AddAsync(first)));
        // The plugin timed out and re-sent the same entries with one new one; one id also twice
        // within the batch.
        Assert.Equal((1, 2), Pair(await AddAsync(first, second, second)));

        await using var db = NewContext();
        Assert.Equal(2, await db.PrivateMessageLogEntries.CountAsync());
    }

    private static (int, int) Pair(PrivateMessageLogBatchResultDto r) => (r.Accepted, r.Duplicates);

    [Fact]
    public async Task Batch_Over200_IsRefusedWhole()
    {
        var entries = Enumerable.Range(0, PrivateMessageLogService.MaxBatchSize + 1)
            .Select(i => Pm(AliceUuid, "Alice", BobUuid, "Bob", "spam " + i)).ToArray();

        await Assert.ThrowsAsync<ArgumentException>(() => AddAsync(entries));

        Assert.Equal((200, 0), Pair(await AddAsync(entries.Take(200).ToArray())));
    }

    public static IEnumerable<object[]> MalformedEntries() => new[]
    {
        new object[] { Pm(AliceUuid, "Alice", BobUuid, "Bob", "x", id: Guid.Empty) },
        new object[] { Pm(AliceUuid, "Alice", BobUuid, "Bob", "x", at: default(DateTime)) },
        new object[] { Pm(AliceUuid, " ", BobUuid, "Bob", "x") },
        new object[] { Pm(AliceUuid, "Alice", BobUuid, "Bob", null!) },
        new object[] { Pm("not-a-uuid", "Alice", BobUuid, "Bob", "x") },
        new object[] { Pm(AliceUuid, "Alice", BobUuid, "Bob", "x", outcome: (PrivateMessageOutcome)42) },
    };

    [Theory]
    [MemberData(nameof(MalformedEntries))]
    public async Task Batch_WithAMalformedEntry_StoresNothing(CreatePrivateMessageLogEntryDto bad)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => AddAsync(Pm(AliceUuid, "Alice", BobUuid, "Bob", "fine"), bad));

        await using var db = NewContext();
        Assert.Equal(0, await db.PrivateMessageLogEntries.CountAsync());
    }

    [Fact]
    public async Task Batch_CapsLongFields_AndPullsFutureTimestampsBack()
    {
        var farFuture = DateTime.UtcNow.AddDays(400);
        await AddAsync(Pm(AliceUuid, "Alice_with_a_far_too_long_name", BobUuid, "Bob", new string('x', 2000), farFuture));

        await using var db = NewContext();
        var row = await db.PrivateMessageLogEntries.SingleAsync();
        Assert.Equal(PrivateMessageLogEntry.NameMaxLength, row.SenderName.Length);
        Assert.Equal(PrivateMessageLogEntry.ContentMaxLength, row.Content.Length);
        Assert.True(row.SentAt <= DateTime.UtcNow.AddMinutes(1), "a future SentAt would outlive its retention");
    }

    [Fact]
    public async Task Batch_Empty_IsANoOp()
    {
        Assert.Equal((0, 0), Pair(await AddAsync()));
    }

    // ===== Search =====

    private async Task SeedConversationAsync()
    {
        var t = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        await AddAsync(
            Pm(AliceUuid, "Alice", BobUuid, "Bob", "a->b 1", t),
            Pm(BobUuid, "Bob", AliceUuid, "Alice", "b->a 2", t.AddMinutes(1)),
            Pm(AliceUuid, "Alice", CarolUuid, "Carol", "a->c 3", t.AddMinutes(2)),
            Pm(CarolUuid, "Carol", BobUuid, "Bob", "c->b 4", t.AddMinutes(3)),
            Pm(null, "CONSOLE", AliceUuid, "Alice", "console->a 5", t.AddDays(1)));
    }

    [Fact]
    public async Task Search_AParticipant_SentAndReceived_NewestFirst()
    {
        await SeedConversationAsync();

        var page = await SearchAsync(new PrivateMessageLogQueryDto { ParticipantUserId = 1 });

        Assert.Equal(new[] { "console->a 5", "a->c 3", "b->a 2", "a->b 1" }, page.Items.Select(i => i.Content));
        Assert.Equal(4, page.TotalCount);
        Assert.Equal("Delivered", page.Items[0].Outcome);
        Assert.Equal(DateTimeKind.Utc, page.Items[0].SentAt.Kind);
    }

    [Fact]
    public async Task Search_OneConversation_AndADateRange()
    {
        await SeedConversationAsync();
        var t = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

        var withBob = await SearchAsync(new PrivateMessageLogQueryDto { ParticipantUserId = 1, OtherUserId = 2 });
        Assert.Equal(new[] { "b->a 2", "a->b 1" }, withBob.Items.Select(i => i.Content));

        var window = await SearchAsync(new PrivateMessageLogQueryDto
        {
            ParticipantUserId = 1, From = t.AddMinutes(1), To = t.AddMinutes(2)
        });
        Assert.Equal(new[] { "b->a 2" }, window.Items.Select(i => i.Content));
    }

    [Fact]
    public async Task Search_Pages_AndCapsThePageSize()
    {
        await SeedConversationAsync();

        var second = await SearchAsync(new PrivateMessageLogQueryDto { ParticipantUserId = 1, PageNumber = 2, PageSize = 3 });
        Assert.Equal(new[] { "a->b 1" }, second.Items.Select(i => i.Content));
        Assert.Equal((4, 2, 3), (second.TotalCount, second.PageNumber, second.PageSize));

        var huge = await SearchAsync(new PrivateMessageLogQueryDto { ParticipantUserId = 1, PageSize = 5000 });
        Assert.Equal(PrivateMessageLogService.MaxPageSize, huge.PageSize);
    }

    [Fact]
    public async Task Search_WritesAPrivateMessagesViewedAuditEntry()
    {
        await SeedConversationAsync();

        await SearchAsync(new PrivateMessageLogQueryDto { ParticipantUserId = 1, OtherUserId = 2 }, viewer: 9);

        await using var db = NewContext();
        var audit = await db.AuditLogEntries.SingleAsync();
        Assert.Equal((AuditAction.PrivateMessagesViewed, (int?)9, 1), (audit.Action, audit.ActorUserId, audit.TargetUserId));
        using var details = JsonDocument.Parse(audit.Details!);
        Assert.Equal(2, details.RootElement.GetProperty("otherUserId").GetInt32());
        Assert.Equal(2, details.RootElement.GetProperty("shown").GetInt32());
    }

    [Fact]
    public async Task Search_WithoutAParticipant_IsRefusedAndNotAudited()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => SearchAsync(new PrivateMessageLogQueryDto()));

        await using var db = NewContext();
        Assert.Equal(0, await db.AuditLogEntries.CountAsync());
    }

    [Fact]
    public void PrivateMessagesViewed_KeepsItsCrossBranchNumber()
    {
        // 12-14 are other features' (teleport, lootboxes); 15-16 are this feature's.
        Assert.Equal(15, (int)AuditAction.PrivateMessagesViewed);
    }
}
