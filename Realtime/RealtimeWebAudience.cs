using System.Collections.Concurrent;
using knkwebapi_v2.Services;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Realtime;

/// <summary>
/// S4 (PandiO/knk-workspace#183): who may receive a node-gated web event (<see cref="RealtimeMessage.RequiredNode"/>).
/// The same check <c>[RequirePermission(node)]</c> makes on REST (<see cref="IPermissionResolutionService.CheckAsync"/>),
/// so a live update never reaches someone the matching GET would answer 403.
/// </summary>
public interface IRealtimeWebAudience
{
    ValueTask<bool> HoldsAsync(int userId, string node, CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IRealtimeWebAudience"/> with a short per-(user, node) cache (<see cref="RealtimeOptions.WebPermissionCacheSeconds"/>,
/// 30 s), so a burst of events doesn't hit the database once per connection and message: a node taken away stops the
/// events within that time (REST answers 403 at once). A check that fails counts as "no" and is not cached.
/// Singleton; each check runs in its own scope.
/// </summary>
public sealed class RealtimeWebAudience : IRealtimeWebAudience
{
    private readonly ConcurrentDictionary<(int UserId, string Node), (bool Allowed, long ExpiresTicks)> _cache = new();
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _time;
    private readonly TimeSpan _ttl;
    private readonly ILogger<RealtimeWebAudience>? _logger;

    public RealtimeWebAudience(IServiceScopeFactory scopes, IOptions<RealtimeOptions> options, TimeProvider? time = null,
        ILogger<RealtimeWebAudience>? logger = null)
    {
        _scopes = scopes;
        _time = time ?? TimeProvider.System;
        _ttl = TimeSpan.FromSeconds(Math.Max(0, options.Value.WebPermissionCacheSeconds));
        _logger = logger;
    }

    public async ValueTask<bool> HoldsAsync(int userId, string node, CancellationToken cancellationToken = default)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(node)) return false;
        var key = (userId, node);
        var now = _time.GetUtcNow().UtcTicks;
        if (_cache.TryGetValue(key, out var cached) && cached.ExpiresTicks > now) return cached.Allowed;

        bool allowed;
        try
        {
            using var scope = _scopes.CreateScope();
            var permissions = scope.ServiceProvider.GetRequiredService<IPermissionResolutionService>();
            allowed = (await permissions.CheckAsync(userId, node))?.Allowed == true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger?.LogWarning(ex, "Realtime: permission check of user {UserId} for {Node} failed; not sending", userId, node);
            return false;
        }
        if (_ttl > TimeSpan.Zero)
        {
            _cache[key] = (allowed, now + _ttl.Ticks);
        }
        return allowed;
    }

    /// <summary>Drops the cached answers (all, or one user's).</summary>
    public void Forget(int? userId = null)
    {
        foreach (var key in _cache.Keys)
        {
            if (userId == null || key.UserId == userId) _cache.TryRemove(key, out _);
        }
    }
}
