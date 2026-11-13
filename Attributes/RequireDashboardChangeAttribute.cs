using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace knkwebapi_v2.Attributes
{
    /// <summary>
    /// The create/edit rule of a dashboard type, read from <see cref="DashboardAccess"/> (PandiO/knk-workspace#374): a
    /// web user needs every node of the type's <see cref="DashboardTypeAccess.ChangeNodes"/> (all-of; the write node and
    /// <c>knk.admin.content</c>, except the Players types and RoadProfile). On the primary create and update actions of
    /// each dashboard type (pinned by <c>Security/DashboardChangeAccessRulesTests</c>), next to the controller's own
    /// area rule. The plugin key passes (it checks in-game nodes itself), anonymous → 401, a missing node → 403
    /// <c>{ error, code: "NoWriteAccess", entityType, nodes, message }</c>, the same shape as a refused link change.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class RequireDashboardChangeAttribute : TypeFilterAttribute, IKnkAccessRule
    {
        public RequireDashboardChangeAttribute(string typeName) : base(typeof(DashboardChangeFilter))
        {
            TypeName = typeName;
            Arguments = new object[] { typeName };
        }

        /// <summary>The registry type name (<c>Town</c>, <c>SiegeTeam</c>, …).</summary>
        public string TypeName { get; }
    }

    public sealed class DashboardChangeFilter : IAsyncAuthorizationFilter
    {
        private readonly string _typeName;
        private readonly IPermissionResolutionService _permissions;

        public DashboardChangeFilter(string typeName, IPermissionResolutionService permissions)
        {
            _typeName = typeName;
            _permissions = permissions;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var caller = context.HttpContext.GetKnkCaller();
            if (caller.IsPluginService)
            {
                return;
            }

            if (caller.WebUserId is not { } userId)
            {
                context.Result = PluginServiceAuth.Unauthorized(context.HttpContext);
                return;
            }

            var type = DashboardAccess.Find(_typeName);
            if (type == null)
            {
                // A typo in the attribute: refuse rather than fall open (the rules test pins every name).
                context.Result = PluginServiceAuth.Forbidden("This endpoint has no access rule.");
                return;
            }

            foreach (var node in type.ChangeNodes)
            {
                var check = await _permissions.CheckAsync(userId, node);
                if (check?.Allowed != true)
                {
                    context.Result = Refused(type.TypeName, type.ChangeNodes);
                    return;
                }
            }
        }

        /// <summary>The 403 for a create or edit without every change node; the web app shows "Requires …".</summary>
        public static IActionResult Refused(string typeName, IReadOnlyList<string> nodes) =>
            new ObjectResult(new
            {
                error = "Forbidden",
                code = "NoWriteAccess",
                entityType = typeName,
                nodes,
                message = $"Creating or editing {typeName} requires {string.Join(" and ", nodes)}."
            })
            { StatusCode = StatusCodes.Status403Forbidden };
    }
}
