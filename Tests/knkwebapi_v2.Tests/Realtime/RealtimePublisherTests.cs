using Xunit;
using knkwebapi_v2.Realtime;
using knkwebapi_v2.Realtime.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Tests.Realtime;

/// <summary>PandiO/knk-workspace#178: the bounded publish queue and per-connection seq (DESIGN §4.2–§4.3).</summary>
public class RealtimePublisherTests
{
    private sealed class RecordingTransport : IRealtimeTransport
    {
        public List<(RealtimeHubKind Hub, string ConnectionId, RealtimeEnvelope Envelope)> Sent { get; } = new();
        public HashSet<string> Failing { get; } = new();

        public Task SendAsync(RealtimeHubKind hub, string connectionId, RealtimeEnvelope envelope, CancellationToken cancellationToken)
        {
            if (Failing.Contains(connectionId)) throw new InvalidOperationException("socket gone");
            Sent.Add((hub, connectionId, envelope));
            return Task.CompletedTask;
        }
    }

    private static RealtimePublisher NewPublisher(int capacity = 100) =>
        new(Options.Create(new RealtimeOptions { ChannelCapacity = capacity }), NullLogger<RealtimePublisher>.Instance);

    private static async Task<List<RealtimeMessage>> Drain(RealtimePublisher publisher)
    {
        var list = new List<RealtimeMessage>();
        while (publisher.Reader.TryRead(out var message)) list.Add(message);
        await Task.CompletedTask;
        return list;
    }

    [Fact]
    public async Task Publish_QueuesWithoutWaiting_AndAddressesTheRightGroup()
    {
        var publisher = NewPublisher();

        publisher.PublishToGameServers("A", new { x = 1 });
        publisher.PublishToGameServers("B", null, serverId: "s1", actor: new RealtimeActor(7, "web"), v: 2);
        publisher.PublishToWebUser(42, "C", null);
        publisher.PublishToConnection(RealtimeHubKind.Game, "conn-1", "D", null);

        var messages = await Drain(publisher);
        Assert.Equal(new[] { "A", "B", "C", "D" }, messages.Select(m => m.Type));
        Assert.Equal(RealtimeGroups.Servers, messages[0].Group);
        Assert.Equal("server:s1", messages[1].Group);
        Assert.Equal(2, messages[1].V);
        Assert.Equal(7, messages[1].Actor!.UserId);
        Assert.Equal((RealtimeHubKind.Web, "user:42"), (messages[2].Hub, messages[2].Group));
        Assert.Equal((null, "conn-1"), (messages[3].Group, messages[3].ConnectionId));
    }

    [Fact]
    public async Task FullQueue_DropsTheOldest()
    {
        var publisher = NewPublisher(capacity: 2);

        publisher.PublishToGameServers("first", null);
        publisher.PublishToGameServers("second", null);
        publisher.PublishToGameServers("third", null);

        Assert.Equal(new[] { "second", "third" }, (await Drain(publisher)).Select(m => m.Type));
        Assert.Equal(1, publisher.DroppedCount);
    }

    [Fact]
    public void Publish_WithoutType_Throws()
    {
        Assert.Throws<ArgumentException>(() => NewPublisher().PublishToGameServers(" ", null));
    }

    [Fact]
    public async Task Dispatch_NumbersMessagesPerConnection()
    {
        var connections = new RealtimeConnections();
        connections.Add(new RealtimeConnection("g1", RealtimeHubKind.Game, new[] { RealtimeGroups.Servers, RealtimeGroups.Server("s1") }, "s1"));
        connections.Add(new RealtimeConnection("g2", RealtimeHubKind.Game, new[] { RealtimeGroups.Servers, RealtimeGroups.Server("s2") }, "s2"));
        connections.Add(new RealtimeConnection("w1", RealtimeHubKind.Web, new[] { RealtimeGroups.User(42) }, userId: 42));
        var transport = new RecordingTransport();
        var dispatcher = new RealtimeDispatchService(NewPublisher(), connections, transport, NullLogger<RealtimeDispatchService>.Instance);
        var at = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

        await dispatcher.DispatchAsync(new RealtimeMessage(RealtimeHubKind.Game, RealtimeGroups.Servers, null, "A", 1, null, null, at));
        await dispatcher.DispatchAsync(new RealtimeMessage(RealtimeHubKind.Game, RealtimeGroups.Server("s1"), null, "B", 1, null, null, at));
        await dispatcher.DispatchAsync(new RealtimeMessage(RealtimeHubKind.Game, RealtimeGroups.Servers, null, "C", 1, new { id = 3 }, null, at));
        await dispatcher.DispatchAsync(new RealtimeMessage(RealtimeHubKind.Web, RealtimeGroups.User(42), null, "D", 1, null, null, at));

        Assert.Equal(new[] { ("A", 1L), ("B", 2L), ("C", 3L) },
            transport.Sent.Where(s => s.ConnectionId == "g1").Select(s => (s.Envelope.Type, s.Envelope.Seq)));
        Assert.Equal(new[] { ("A", 1L), ("C", 2L) },
            transport.Sent.Where(s => s.ConnectionId == "g2").Select(s => (s.Envelope.Type, s.Envelope.Seq)));
        var web = Assert.Single(transport.Sent, s => s.ConnectionId == "w1");
        Assert.Equal((RealtimeHubKind.Web, "D", 1L), (web.Hub, web.Envelope.Type, web.Envelope.Seq));
        Assert.Equal(at, web.Envelope.At);
    }

    [Fact]
    public async Task FailedSend_StillUsesItsSeq_SoTheReceiverSeesAGap()
    {
        var connections = new RealtimeConnections();
        var connection = new RealtimeConnection("g1", RealtimeHubKind.Game, new[] { RealtimeGroups.Servers }, "s1");
        connections.Add(connection);
        var transport = new RecordingTransport();
        var dispatcher = new RealtimeDispatchService(NewPublisher(), connections, transport, NullLogger<RealtimeDispatchService>.Instance);
        var at = DateTime.UtcNow;

        transport.Failing.Add("g1");
        Assert.Equal(0, await dispatcher.DispatchAsync(new RealtimeMessage(RealtimeHubKind.Game, RealtimeGroups.Servers, null, "A", 1, null, null, at)));
        transport.Failing.Clear();
        Assert.Equal(1, await dispatcher.DispatchAsync(new RealtimeMessage(RealtimeHubKind.Game, RealtimeGroups.Servers, null, "B", 1, null, null, at)));

        Assert.Equal(2, Assert.Single(transport.Sent).Envelope.Seq);
        Assert.Equal(2, connection.LastSeq);
    }

    [Fact]
    public async Task Dispatch_ToAConnectionOfTheOtherHubOrAGoneOne_SendsNothing()
    {
        var connections = new RealtimeConnections();
        connections.Add(new RealtimeConnection("w1", RealtimeHubKind.Web, new[] { RealtimeGroups.User(1) }, userId: 1));
        var transport = new RecordingTransport();
        var dispatcher = new RealtimeDispatchService(NewPublisher(), connections, transport, NullLogger<RealtimeDispatchService>.Instance);

        Assert.Equal(0, await dispatcher.DispatchAsync(new RealtimeMessage(RealtimeHubKind.Game, null, "w1", "A", 1, null, null, DateTime.UtcNow)));
        Assert.Equal(0, await dispatcher.DispatchAsync(new RealtimeMessage(RealtimeHubKind.Web, null, "gone", "A", 1, null, null, DateTime.UtcNow)));
        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task DispatchService_SendsWhatIsPublished()
    {
        var publisher = NewPublisher();
        var connections = new RealtimeConnections();
        connections.Add(new RealtimeConnection("g1", RealtimeHubKind.Game, new[] { RealtimeGroups.Servers }, "s1"));
        var transport = new RecordingTransport();
        var dispatcher = new RealtimeDispatchService(publisher, connections, transport, NullLogger<RealtimeDispatchService>.Instance);

        await dispatcher.StartAsync(CancellationToken.None);
        publisher.PublishToGameServers("Hello", new { a = 1 });
        for (var i = 0; i < 100 && transport.Sent.Count == 0; i++) await Task.Delay(20);
        await dispatcher.StopAsync(CancellationToken.None);

        Assert.Equal("Hello", Assert.Single(transport.Sent).Envelope.Type);
    }
}
