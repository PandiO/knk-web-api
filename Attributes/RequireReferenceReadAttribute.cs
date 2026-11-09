using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace knkwebapi_v2.Attributes
{
    /// <summary>
    /// The access rule of <c>GET api/EntityReferences/{entityType}/{id}/rows</c> (PandiO/knk-workspace#44, the #280 model):
    /// what uses a record is part of reading that record, so the caller needs one of the route type's read nodes
    /// (<see cref="DashboardTypeAccess.ReadNodes"/>). Web users only (the game server deletes nothing through the
    /// dashboard). Anonymous → 401, the plugin key alone → 403, a type outside the registry → 404, no read node → 403
    /// <c>NoReadAccess</c> (as <see cref="ReadAccess.Refused"/>). The rows themselves are trimmed per type by
    /// <see cref="IEntityReferenceRowsService"/>. A custom rule because the nodes depend on the route's type, so the
    /// read is category U (its own rule) in ReadAccessRulesTests.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class RequireReferenceReadAttribute : TypeFilterAttribute, IKnkAccessRule
    {
        public RequireReferenceReadAttribute() : base(typeof(ReferenceReadFilter))
        {
        }
    }

    public sealed class ReferenceReadFilter : IAsyncAuthorizationFilter
    {
        public const string TypeRouteKey = "entityType";

        private readonly IPermissionResolutionService _permissions;
        private readonly IPermissionReadCache _cache;

        public ReferenceReadFilter(IPermissionResolutionService permissions, IPermissionReadCache cache)
        {
            _permissions = permissions;
            _cache = cache;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var caller = context.HttpContext.GetKnkCaller();
            if (caller.WebUserId is not { } userId)
            {
                context.Result = caller.IsPluginService
                    ? PluginServiceAuth.Forbidden("What uses a record is listed in the web app only.")
                    : PluginServiceAuth.Unauthorized(context.HttpContext);
                return;
            }

            var type = DashboardAccess.Find(context.RouteData.Values[TypeRouteKey]?.ToString());
            if (type == null)
            {
                context.Result = new NotFoundObjectResult(new { error = "UnknownEntityType", message = "Not a dashboard entity type." });
                return;
            }

            if (!await _cache.HoldsAnyAsync(_permissions, userId, type.ReadNodes, context.HttpContext))
            {
                context.Result = ReadAccess.Refused(type.TypeName, type.ReadNodes);
            }
        }
    }
}
