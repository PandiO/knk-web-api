using System.Collections.Concurrent;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services;

/// <summary>
/// Thread-safe in-memory IPlayerNotificationQueue. Registered as a singleton so every scoped
/// UserService instance writes to the same queue the plugin polls.
/// </summary>
public class InMemoryPlayerNotificationQueue : IPlayerNotificationQueue
{
    /// <summary>
    /// How long a notification for a player who stays offline is kept for their next join -
    /// long enough to cover "promoted from the web app while they were away", short enough that
    /// a returning player isn't greeted by a stale promotion days later.
    /// </summary>
    public static readonly TimeSpan DefaultTimeToLive = TimeSpan.FromHours(24);

    /// <summary>Hard cap so a plugin that never polls can't grow the queue unbounded.</summary>
    public const int DefaultMaxPending = 1000;

    private readonly ConcurrentDictionary<long, PlayerNotificationDto> _pending = new();
    private readonly TimeSpan _timeToLive;
    private readonly int _maxPending;
    private readonly Func<DateTime> _utcNow;
    private long _nextId;

    public InMemoryPlayerNotificationQueue()
        : this(DefaultTimeToLive, DefaultMaxPending, () => DateTime.UtcNow)
    {
    }

    public InMemoryPlayerNotificationQueue(TimeSpan timeToLive, int maxPending, Func<DateTime> utcNow)
    {
        _timeToLive = timeToLive;
        _maxPending = maxPending;
        _utcNow = utcNow;
    }

    public long Enqueue(int userId, string? uuid, string username, string type, TitleChangeResultDto? titleChange)
    {
        var id = Interlocked.Increment(ref _nextId);
        _pending[id] = new PlayerNotificationDto
        {
            Id = id,
            UserId = userId,
            Uuid = uuid,
            Username = username,
            Type = type,
            TitleChange = titleChange,
            CreatedAt = _utcNow()
        };
        Prune();
        return id;
    }

    public IReadOnlyList<PlayerNotificationDto> GetPending()
    {
        Prune();
        return _pending.Values.OrderBy(n => n.Id).ToList();
    }

    public int Acknowledge(IEnumerable<long> ids)
    {
        return ids.Distinct().Count(id => _pending.TryRemove(id, out _));
    }

    private void Prune()
    {
        var cutoff = _utcNow() - _timeToLive;
        foreach (var notification in _pending.Values)
        {
            if (notification.CreatedAt < cutoff)
            {
                _pending.TryRemove(notification.Id, out _);
            }
        }

        var overflow = _pending.Count - _maxPending;
        if (overflow > 0)
        {
            foreach (var oldestId in _pending.Keys.OrderBy(id => id).Take(overflow))
            {
                _pending.TryRemove(oldestId, out _);
            }
        }
    }
}
