using knkwebapi_v2.Enums;
using knkwebapi_v2.Services.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Telemetry;

/// <summary>
/// The bounded write queue and its background writer (IMPLEMENTATION_PLAN.md §4/§7; link 6
/// acceptance criterion 1): capacity bound with drop-newest, drop counters, a
/// <c>telemetry.queue_dropped</c> record, batch writes that never store an event id twice.
/// </summary>
public class TelemetryWriteQueueTests : IDisposable
{
    private readonly TelemetryTestDb _db = new();

    public void Dispose() => _db.Dispose();

    private TelemetryWriterService Writer(TelemetryWriteQueue queue, int batchSize = 1000) =>
        new(_db.Provider(queue), queue, NullLogger<TelemetryWriterService>.Instance,
            Options.Create(new knkwebapi_v2.Configuration.DiagnosticTelemetryOptions { WriteBatchSize = batchSize }), new TelemetryMetrics());

    [Fact]
    public void Queue_IsBounded_DropsNewest_AndCountsDrops()
    {
        var queue = new TelemetryWriteQueue(2);
        var first = TelemetryTestDb.Stored("session.join", 1, TelemetryTestDb.Now);
        var second = TelemetryTestDb.Stored("session.leave", 1, TelemetryTestDb.Now);

        Assert.True(queue.TryEnqueue(first));
        Assert.True(queue.TryEnqueue(second));
        Assert.False(queue.TryEnqueue(TelemetryTestDb.Stored("menu.opened", 1, TelemetryTestDb.Now)));

        Assert.Equal((2, 2, 1L), (queue.Depth, queue.Capacity, queue.DroppedSinceStart));
        Assert.Equal(1, queue.TakeUnreportedDrops());
        Assert.Equal(0, queue.TakeUnreportedDrops());
        Assert.Equal(new[] { first, second }, queue.Drain(10));
        Assert.Equal(0, queue.Depth);
    }

    [Fact]
    public async Task Writer_InsertsQueuedEvents_InBatches_AndRecordsTheLastWrite()
    {
        var queue = new TelemetryWriteQueue(100);
        for (var i = 0; i < 5; i++) queue.TryEnqueue(TelemetryTestDb.Stored("session.join", 1, TelemetryTestDb.Now.AddSeconds(i)));

        var written = await Writer(queue, batchSize: 2).FlushAsync(TelemetryTestDb.Now);

        Assert.Equal(5, written);
        Assert.Equal(5, _db.NewContext().TelemetryEvents.Count());
        Assert.NotNull(queue.LastWriteAt);
        Assert.Equal(0, queue.Depth);
    }

    [Fact]
    public async Task Writer_NeverStoresAnEventIdTwice()
    {
        var queue = new TelemetryWriteQueue(100);
        var e = TelemetryTestDb.Stored("session.join", 1, TelemetryTestDb.Now);
        queue.TryEnqueue(e);
        await Writer(queue).FlushAsync(TelemetryTestDb.Now);

        var replay = TelemetryTestDb.Stored("session.join", 1, TelemetryTestDb.Now);
        replay.EventId = e.EventId;
        queue.TryEnqueue(replay);
        var written = await Writer(queue).FlushAsync(TelemetryTestDb.Now);

        Assert.Equal(0, written);
        Assert.Single(_db.NewContext().TelemetryEvents.Where(x => x.EventId == e.EventId));
    }

    [Fact]
    public async Task Drops_AreRecordedAsOneQueueDroppedEvent()
    {
        var queue = new TelemetryWriteQueue(1);
        queue.TryEnqueue(TelemetryTestDb.Stored("session.join", 1, TelemetryTestDb.Now));
        queue.TryEnqueue(TelemetryTestDb.Stored("session.join", 1, TelemetryTestDb.Now));
        queue.TryEnqueue(TelemetryTestDb.Stored("session.join", 1, TelemetryTestDb.Now));

        await Writer(queue).FlushAsync(TelemetryTestDb.Now);

        var dropped = _db.NewContext().TelemetryEvents.AsNoTracking().Single(x => x.Name == "telemetry.queue_dropped");
        Assert.Equal((TelemetrySource.Api, "{\"dropped\":2}"), (dropped.Source, dropped.PayloadJson));
        Assert.Equal(2, _db.NewContext().TelemetryEvents.Count());
        Assert.Equal(2, queue.DroppedSinceStart);
    }

    [Fact]
    public async Task EmptyQueue_WritesNothing()
    {
        Assert.Equal(0, await Writer(new TelemetryWriteQueue(10)).FlushAsync(TelemetryTestDb.Now));
        Assert.Empty(_db.NewContext().TelemetryEvents);
    }
}
