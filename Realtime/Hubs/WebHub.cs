using knkwebapi_v2.Attributes;
using knkwebapi_v2.Extensions;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace knkwebapi_v2.Realtime.Hubs;

/// <summary>
/// <c>/api/hubs/web</c> (DESIGN §4.1): logged-in web users, JWT validated like REST (incl. the TokenVersion check);
/// the browser sends the access token as <c>access_token</c> on this path only. Each connection joins
/// <c>user:{id}</c>; group joins are decided by the API, never by the client (DESIGN §7.3). The only client-to-server
/// methods are WT2's (#181) <see cref="JoinTask"/> / <see cref="LeaveTask"/>: the API checks that the caller created the
/// task or holds <c>knk.admin.world</c> before adding <c>task:{id}</c> (DESIGN §7.3); no writes over the hub.
/// </summary>
public sealed class WebHub : Hub
{
    private readonly RealtimeConnections _connections;
    private readonly ILogger<WebHub> _logger;
    private readonly IServiceProvider? _services;

    public WebHub(RealtimeConnections connections, ILogger<WebHub> logger, IServiceProvider? services = null)
    {
        _connections = connections;
        _logger = logger;
        _services = services;
    }

    /// <summary>
    /// WT2 (#181): receive <c>WorldTaskUpdated</c> for one task. True when joined (or already in it); false when the task
    /// doesn't exist or isn't the caller's (created by them, or they hold <c>knk.admin.world</c>).
    /// </summary>
    public async Task<bool> JoinTask(int taskId)
    {
        var connection = _connections.Get(Context.ConnectionId);
        var userId = Context.User?.GetUserId();
        if (connection == null || userId is not > 0 || taskId <= 0) return false;
        if (connection.Groups.Contains(RealtimeGroups.Task(taskId))) return true;
        if (!await MayWatchAsync(userId.Value, taskId)) return false;
        connection.Join(RealtimeGroups.Task(taskId));
        return true;
    }

    public Task LeaveTask(int taskId)
    {
        _connections.Get(Context.ConnectionId)?.Leave(RealtimeGroups.Task(taskId));
        return Task.CompletedTask;
    }

    private async Task<bool> MayWatchAsync(int userId, int taskId)
    {
        var tasks = _services?.GetService<IWorldTaskRepository>();
        if (tasks == null) return false;
        var task = await tasks.GetByIdAsync(taskId);
        if (task == null) return false;
        if (task.CreatedByUserId == userId) return true;
        var permissions = _services?.GetService<IPermissionResolutionService>();
        var check = permissions == null ? null : await permissions.CheckAsync(userId, StaffPermissions.ManageWorld);
        if (check?.Allowed == true) return true;
        _logger.LogInformation("Web hub: user {UserId} may not watch world task {TaskId}", userId, taskId);
        return false;
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
