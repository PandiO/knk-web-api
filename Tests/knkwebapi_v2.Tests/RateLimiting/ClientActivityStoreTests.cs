using Xunit;
using knkwebapi_v2.Models.ClientActivity;
using knkwebapi_v2.Services;
using FluentAssertions;
using knkwebapi_v2.Tests.Services.Email;

namespace knkwebapi_v2.Tests;

public class ClientActivityStoreTests
{
    [Fact]
    public void RecordsRequestsIntoRollingBuckets()
    {
        var store = new InMemoryClientActivityStore(maxClients: 10);
        var client = new ClientInfo { ClientId = "c1", ClientType = "web-admin" };

        var now = DateTime.UtcNow;
        for (int i = 0; i < 5; i++)
        {
            store.RecordRequest(client, new RequestInfo
            {
                Method = "GET",
                RouteTemplate = "/api/test",
                StatusCode = 200,
                DurationMs = 100,
                TimestampUtc = now
            });
        }

        var snapshot = store.GetClient("web-admin", "c1")!;
        snapshot.TotalRequestsLast60Min.Should().Be(5);
        snapshot.SuccessRequestsLast60Min.Should().Be(5);
        snapshot.ErrorRequestsLast60Min.Should().Be(0);
        snapshot.AvgDurationMsLast60Min.Should().Be(100);
        snapshot.BucketsLast60Minutes.Count(b => b != null).Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public void GetClientsReturnsOnlyActiveWithinTimeWindow()
    {
        var store = new InMemoryClientActivityStore(maxClients: 10);
        var client1 = new ClientInfo { ClientId = "c1", ClientType = "web-admin" };
        var client2 = new ClientInfo { ClientId = "c2", ClientType = "paper-plugin" };
        
        store.RecordRequest(client1, new RequestInfo { Method = "GET", RouteTemplate = "/test", StatusCode = 200, DurationMs = 50 });
        store.RecordRequest(client2, new RequestInfo { Method = "POST", RouteTemplate = "/test2", StatusCode = 201, DurationMs = 100 });

        var activeClients = store.GetClients(TimeSpan.FromMinutes(5));
        activeClients.Count.Should().Be(2);
    }

    // PandiO/knk-workspace#99: a minute slot is reused only within its own minute, and the Last60Min totals
    // leave out what is older than 60 minutes.
    [Fact]
    public void SameMinuteSlotAnHourLater_StartsANewBucket()
    {
        var clock = new ManualClock(new DateTime(2026, 11, 2, 10, 15, 30, DateTimeKind.Utc));
        var store = new InMemoryClientActivityStore(maxClients: 10, time: clock);
        var client = new ClientInfo { ClientId = "c1", ClientType = "web-admin" };

        store.RecordRequest(client, Request(200));
        store.RecordRequest(client, Request(500));
        clock.Advance(TimeSpan.FromHours(1));
        store.RecordRequest(client, Request(200));

        var snapshot = store.GetClient("web-admin", "c1")!;
        snapshot.TotalRequestsLast60Min.Should().Be(1);
        snapshot.ErrorRequestsLast60Min.Should().Be(0);
        snapshot.BucketsLast60Minutes[15]!.BucketStartUtc.Should().Be(new DateTime(2026, 11, 2, 11, 15, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void RequestsOlderThanSixtyMinutes_DropOutOfTheTotals()
    {
        var clock = new ManualClock(new DateTime(2026, 11, 2, 10, 15, 30, DateTimeKind.Utc));
        var store = new InMemoryClientActivityStore(maxClients: 10, time: clock);
        var client = new ClientInfo { ClientId = "c1", ClientType = "web-admin" };

        store.RecordRequest(client, Request(200));
        clock.Advance(TimeSpan.FromMinutes(30));
        store.RecordRequest(client, Request(200));
        store.GetClient("web-admin", "c1")!.TotalRequestsLast60Min.Should().Be(2);

        clock.Advance(TimeSpan.FromMinutes(31));
        store.GetClient("web-admin", "c1")!.TotalRequestsLast60Min.Should().Be(1);

        clock.Advance(TimeSpan.FromMinutes(30));
        store.GetClient("web-admin", "c1")!.TotalRequestsLast60Min.Should().Be(0);
    }

    private static RequestInfo Request(int status) => new()
    {
        Method = "GET", RouteTemplate = "/api/test", StatusCode = status, DurationMs = 10, TimestampUtc = DateTime.UtcNow
    };
}
