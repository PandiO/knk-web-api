using Xunit;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Tests.Services;

public class InMemoryPlayerNotificationQueueTests
{
    private DateTime _now = new(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);

    private InMemoryPlayerNotificationQueue CreateQueue(int maxPending = 100) =>
        new(TimeSpan.FromHours(24), maxPending, () => _now);

    private static TitleChangeResultDto Promotion() => new()
    {
        Direction = "promotion",
        FromTitleName = "Novice",
        ToTitleName = "Apprentice",
        CrossedTitles = new List<TitleCrossingDto>()
    };

    [Fact]
    public void Enqueue_ThenGetPending_ReturnsItOldestFirst()
    {
        var queue = CreateQueue();

        var first = queue.Enqueue(1, "uuid-1", "alice", PlayerNotificationTypes.TitleChanged, Promotion());
        var second = queue.Enqueue(2, null, "bob", PlayerNotificationTypes.TitleChanged, Promotion());

        var pending = queue.GetPending();
        Assert.Equal(new[] { first, second }, pending.Select(n => n.Id));
        Assert.Equal("uuid-1", pending[0].Uuid);
        Assert.Equal("alice", pending[0].Username);
        Assert.Equal("Apprentice", pending[0].TitleChange!.ToTitleName);
    }

    [Fact]
    public void Acknowledge_RemovesOnlyTheGivenIds()
    {
        var queue = CreateQueue();
        var first = queue.Enqueue(1, "uuid-1", "alice", PlayerNotificationTypes.TitleChanged, Promotion());
        var second = queue.Enqueue(2, "uuid-2", "bob", PlayerNotificationTypes.TitleChanged, Promotion());

        var removed = queue.Acknowledge(new[] { first, first, 999L });

        Assert.Equal(1, removed);
        Assert.Equal(new[] { second }, queue.GetPending().Select(n => n.Id));
    }

    [Fact]
    public void GetPending_DropsNotificationsOlderThanTheTimeToLive()
    {
        var queue = CreateQueue();
        queue.Enqueue(1, "uuid-1", "alice", PlayerNotificationTypes.TitleChanged, Promotion());
        _now = _now.AddHours(23);
        var fresh = queue.Enqueue(2, "uuid-2", "bob", PlayerNotificationTypes.TitleChanged, Promotion());
        _now = _now.AddHours(2);

        Assert.Equal(new[] { fresh }, queue.GetPending().Select(n => n.Id));
    }

    [Fact]
    public void Enqueue_BeyondMaxPending_DropsTheOldest()
    {
        var queue = CreateQueue(maxPending: 2);
        queue.Enqueue(1, "uuid-1", "alice", PlayerNotificationTypes.TitleChanged, Promotion());
        var second = queue.Enqueue(2, "uuid-2", "bob", PlayerNotificationTypes.TitleChanged, Promotion());
        var third = queue.Enqueue(3, "uuid-3", "carol", PlayerNotificationTypes.TitleChanged, Promotion());

        Assert.Equal(new[] { second, third }, queue.GetPending().Select(n => n.Id));
    }
}
