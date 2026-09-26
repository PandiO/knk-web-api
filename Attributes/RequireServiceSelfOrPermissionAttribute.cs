using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace knkwebapi_v2.Attributes
{
    /// <summary>
    /// Like <see cref="RequireServiceOrPermissionAttribute"/>, but a logged-in web user may also
    /// read their own data: passes for knk-plugin (a valid X-API-Key), for a web user whose JWT id
    /// equals the route's <c>userIdRouteKey</c> value (default <c>userId</c>), and for a web user
    /// who holds <c>node</c>. Anonymous → 401, anyone else → 403. For per-player reads that the
    /// game server, the player's own account page and staff all use (e.g. a player's discoveries).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class RequireServiceSelfOrPermissionAttribute : TypeFilterAttribute
    {
        public RequireServiceSelfOrPermissionAttribute(string node, string userIdRouteKey = "userId")
            : base(typeof(RequireServiceSelfOrPermissionFilter))
        {
            Node = node;
            UserIdRouteKey = userIdRouteKey;
            Arguments = new object[] { node, userIdRouteKey };
        }

        public string Node { get; }

        public string UserIdRouteKey { get; }
    }

    public class RequireServiceSelfOrPermissionFilter : IAsyncAuthorizationFilter
    {
        private readonly string _node;
        private readonly string _userIdRouteKey;
        private readonly IPermissionResolutionService _permissions;

        public RequireServiceSelfOrPermissionFilter(string node, string userIdRouteKey, IPermissionResolutionService permissions)
        {
            _node = node;
            _userIdRouteKey = userIdRouteKey;
            _permissions = permissions;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var caller = context.HttpContext.GetKnkCaller();
            if (caller.IsPluginService)
            {
                return;
            }

            if (caller.WebUserId == null)
            {
                context.Result = PluginServiceAuth.Unauthorized(context.HttpContext);
                return;
            }

            if (context.RouteData.Values.TryGetValue(_userIdRouteKey, out var routeValue)
                && int.TryParse(routeValue?.ToString(), out var routeUserId)
                && routeUserId == caller.WebUserId.Value)
            {
                return;
            }

            var check = await _permissions.CheckAsync(caller.WebUserId.Value, _node);
            if (check?.Allowed != true)
            {
                context.Result = PluginServiceAuth.Forbidden($"Only this player or staff with the {_node} permission can see this.");
            }
        }
    }
}
