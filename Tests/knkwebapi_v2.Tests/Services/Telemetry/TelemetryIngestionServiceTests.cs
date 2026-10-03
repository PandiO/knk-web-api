using System.Text.Json;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Telemetry;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Telemetry;

/// <summary>
/// Diagnostic ingestion (IMPLEMENTATION_PLAN.md §3.3; link 6 acceptance criterion 1): envelope
/// validation, payload allowlists (forbidden data never stored), dedupe by eventId, enhanced events
/// only for targets, bounded queue drops.
/// </summary>
public class TelemetryIngestionServiceTests : IDisposable
{
    private readonly TelemetryTestDb _db = new();

    public void Dispose() => _db.Dispose();

    private static JsonElement J(object value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public async Task ValidEvents_AreQueued_WithTheCataloguesLevelAndDefaults()
    {
        var queue = _db.Queue();
        var dto = TelemetryTestDb.Event("siege.match_join");
        dto.MatchId = 12;
        dto.CorrelationId = "corr-1";
        dto.Payload = new Dictionary<string, JsonElement> { ["lobbyId"] = J(4), ["teamId"] = J(2) };

        var result = await _db.Ingestion(queue).IngestAsync(new[] { dto });

        Assert.Equal((1, 0, 0), (result.Accepted, result.Duplicates, result.Rejected.Count));
        var queued = Assert.Single(queue.Drain(10));
        Assert.Equal(("siege.match_join", TelemetryLevel.Baseline, TelemetrySource.Plugin), (queued.Name, queued.Level, queued.Source));
        Assert.Equal(("siege", "match_join", TelemetryOutcome.Succeeded), (queued.Feature, queued.Action, queued.Outcome));
        Assert.Equal((12, "corr-1", TelemetryTestDb.Now), (queued.MatchId, queued.CorrelationId, queued.ReceivedAt));
        Assert.Equal("{\"lobbyId\":4,\"teamId\":2}", queued.PayloadJson);
    }

    [Fact]
    public async Task ForbiddenData_NeverReachesTheStore()
    {
        var queue = _db.Queue();
        var dto = TelemetryTestDb.Event("command.result");
        dto.Payload = new Dictionary<string, JsonElement>
        {
            ["command"] = J("pay"),
            ["args"] = J("bob 100"),                 // command arguments
            ["message"] = J("hello, my password is x"), // chat text
            ["ip"] = J("10.0.0.1"),
            ["token"] = J("abc"),
            ["body"] = J(new { raw = "json" })
        };

        await _db.Ingestion(queue).IngestAsync(new[] { dto });

        var stored = Assert.Single(queue.Drain(10)).PayloadJson!;
        Assert.Equal("{\"command\":\"pay\"}", stored);
        Assert.DoesNotContain("bob", stored);
        Assert.DoesNotContain("password", stored);
        Assert.DoesNotContain("10.0.0.1", stored);
    }

    [Fact]
    public async Task FreeTextInCodeFields_IsRejected()
    {
        var reason = TelemetryTestDb.Event();
        reason.ReasonCode = "player said hello world";
        var action = TelemetryTestDb.Event();
        action.Action = "/pay bob 100";

        var result = await _db.Ingestion(_db.Queue()).IngestAsync(new[] { reason, action });

        Assert.Equal(new[] { "InvalidCode", "InvalidCode" }, result.Rejected.Select(r => r.Code));
        Assert.Equal(0, result.Accepted);
    }

    [Fact]
    public async Task Payload_KeepsScalarsOnly_TruncatesLongStrings_AndCapsKeys()
    {
        var queue = _db.Queue();
        var dto = TelemetryTestDb.Event("menu.click");
        _db.Context.TelemetryEnhancedTargets.Add(new TelemetryEnhancedTarget { UserId = 1, ExpiresAt = TelemetryTestDb.Now.AddHours(1) });
        _db.Context.SaveChanges();
        dto.Payload = new Dictionary<string, JsonElement>
        {
            ["menuKey"] = J(new string('m', 300)),
            ["slot"] = J(13),
            ["itemKey"] = J(new[] { "a", "b" }),
            ["clickType"] = J(true)
        };

        await _db.Ingestion(queue).IngestAsync(new[] { dto });

        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Assert.Single(queue.Drain(10)).PayloadJson!)!;
        Assert.Equal(128, payload["menuKey"].GetString()!.Length);
        Assert.Equal(13, payload["slot"].GetInt32());
        Assert.True(payload["clickType"].GetBoolean());
        Assert.False(payload.ContainsKey("itemKey"));
    }

    [Theory]
    [InlineData("chat.message", null, "succeeded", "UnknownEvent")]
    [InlineData("api.request_failed", null, "failed", "NotPluginEvent")]
    [InlineData("session.join", "enhanced", "succeeded", "LevelMismatch")]
    [InlineData("session.join", null, "maybe", "InvalidOutcome")]
    [InlineData("session.join", null, "2", "InvalidOutcome")]
    public async Task InvalidEnvelopes_AreRejectedOneByOne(string name, string? level, string outcome, string code)
    {
        var bad = TelemetryTestDb.Event(name, outcome: outcome, level: level);
        var good = TelemetryTestDb.Event();

        var result = await _db.Ingestion(_db.Queue()).IngestAsync(new[] { bad, good });

        var rejection = Assert.Single(result.Rejected);
        Assert.Equal((0, code, bad.EventId), (rejection.Index, rejection.Code, rejection.EventId));
        Assert.Equal(1, result.Accepted);
    }

    [Fact]
    public async Task OldFutureAndAnonymousEnvelopes_AreRejected()
    {
        var old = TelemetryTestDb.Event(at: TelemetryTestDb.Now.AddDays(-8));
        var future = TelemetryTestDb.Event(at: TelemetryTestDb.Now.AddMinutes(10));
        var noId = TelemetryTestDb.Event();
        noId.EventId = Guid.Empty;
        var noServer = TelemetryTestDb.Event();
        noServer.ServerName = " ";

        var result = await _db.Ingestion(_db.Queue()).IngestAsync(new[] { old, future, noId, noServer });

        Assert.Equal(new[] { "TooOld", "InFuture", "InvalidEnvelope", "InvalidEnvelope" }, result.Rejected.Select(r => r.Code));
    }

    [Fact]
    public async Task Duplicates_WithinTheBatchAndAlreadyStored_AreCountedNotQueued()
    {
        var stored = TelemetryTestDb.Stored("session.join", 1, TelemetryTestDb.Now.AddMinutes(-5));
        _db.Context.TelemetryEvents.Add(stored);
        _db.Context.SaveChanges();
        var again = TelemetryTestDb.Event();
        again.EventId = stored.EventId;
        var fresh = TelemetryTestDb.Event();
        var queue = _db.Queue();

        var result = await _db.Ingestion(queue).IngestAsync(new[] { again, fresh, fresh });

        Assert.Equal((1, 2), (result.Accepted, result.Duplicates));
        Assert.Equal(fresh.EventId, Assert.Single(queue.Drain(10)).EventId);
    }

    [Fact]
    public async Task EnhancedEvents_OnlyForTargetedPlayersOrTestRuns()
    {
        _db.Context.TelemetryEnhancedTargets.AddRange(
            new TelemetryEnhancedTarget { UserId = 1, ExpiresAt = TelemetryTestDb.Now.AddHours(1) },
            new TelemetryEnhancedTarget { UserId = 2, ExpiresAt = TelemetryTestDb.Now.AddHours(-1) },   // expired
            new TelemetryEnhancedTarget { TestRunId = 7, ExpiresAt = TelemetryTestDb.Now.AddHours(1) });
        _db.Context.SaveChanges();
        var targeted = TelemetryTestDb.Event("movement.sample", userId: 1);
        var expired = TelemetryTestDb.Event("movement.sample", userId: 2);
        var viaRun = TelemetryTestDb.Event("movement.sample", userId: 3);
        viaRun.TestRunId = 7;
        var other = TelemetryTestDb.Event("movement.sample", userId: 3);

        var result = await _db.Ingestion(_db.Queue()).IngestAsync(new[] { targeted, expired, viaRun, other });

        Assert.Equal(2, result.Accepted);
        Assert.Equal(new[] { 1, 3 }, result.Rejected.Select(r => r.Index));
        Assert.All(result.Rejected, r => Assert.Equal("NotEnhancedTarget", r.Code));
    }

    [Fact]
    public async Task FullQueue_DropsAndCounts_NeverWaits()
    {
        var queue = _db.Queue(capacity: 2);

        var result = await _db.Ingestion(queue).IngestAsync(Enumerable.Range(0, 5).Select(_ => TelemetryTestDb.Event()).ToList());

        Assert.Equal((2, 3), (result.Accepted, result.Dropped));
        Assert.Equal(3, queue.DroppedSinceStart);
    }

    [Fact]
    public void ApiEvents_AreQueued_WithAllowlistedPayload_AndSkippedWhenDisabled()
    {
        var queue = _db.Queue();
        var service = _db.Ingestion(queue);

        Assert.True(service.RecordApiEvent("api.request_failed", TelemetryOutcome.Failed, "request_failed", 2, "corr 1", "http_500",
            new Dictionary<string, object?> { ["route"] = "api/users/{id:int}", ["status"] = 500, ["path"] = "/api/users/2?x=secret" }));
        var e = Assert.Single(queue.Drain(10));
        Assert.Equal((TelemetrySource.Api, "api", 2, (string?)null), (e.Source, e.ServerName, e.UserId, e.CorrelationId));
        Assert.Equal("{\"route\":\"api/users/{id:int}\",\"status\":500}", e.PayloadJson);

        Assert.False(service.RecordApiEvent("nope.nope", TelemetryOutcome.Info, "x", null, null, null, new Dictionary<string, object?>()));
        _db.TelemetryOptions.Enabled = false;
        Assert.False(service.RecordApiEvent("api.request_failed", TelemetryOutcome.Failed, "x", null, null, null, new Dictionary<string, object?>()));
    }
}
