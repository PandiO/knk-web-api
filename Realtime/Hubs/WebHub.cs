using knkwebapi_v2.Extensions;
using Microsoft.AspNetCore.SignalR;

namespace knkwebapi_v2.Realtime.Hubs;

/// <summary>
/// <c>/api/hubs/web</c> (DESIGN §4.1): logged-in web users, JWT validated like REST (incl. the TokenVersion check);
/// the browser sends the access token as <c>access_token</c> on this path only. Each connection joins
/// <c>user:{id}</c>; group joins are decided by the API, never by the client (DESIGN §7.3). No client-to-server
/// methods yet: the first consumer (world tasks, WT2) adds its own.
/// </summary>
public sealed class WebHub : Hub
{
    private readonly RealtimeConnections _connections;
    private readonly ILogger<WebHub> _logger;

    public WebHub(RealtimeConnections connections, ILogger<WebHub> logger)
    {
        _connections = connections;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.GetUserId();
        if (userId is not > 0)
        {
            _logger.LogWarning("Web hub connection {ConnectionId} refused: no user id in the token", Context.ConnectionId);
            Context.Abort();
            return;
        }
        _connections.Add(new RealtimeConnection(Context.ConnectionId, RealtimeHubKind.Web,
            new[] { RealtimeGroups.User(userId.Value) }, userId: userId.Value));
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _connections.Remove(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
