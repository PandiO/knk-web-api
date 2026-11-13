using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace knkwebapi_v2.Attributes
{
    /// <summary>
    /// The access rule of <c>api/Relationships/{entityType}/{id}/{property}/…</c> (PandiO/knk-workspace#188, the #280
    /// model): Select, Unlink and Add are edits, so they need the type's change nodes (its write node and, since #374,
    /// <c>knk.admin.content</c>: <see cref="DashboardTypeAccess.ChangeNodes"/>) and, when the related row's own column
    /// changes (a child moved under this one), the related type's change nodes too: every node in
    /// <see cref="RelationshipDescriptor.Nodes"/>. Web users only (the
    /// game server doesn't edit links). Anonymous → 401, the plugin key alone → 403, an unknown type or relationship →
    /// 404, a missing node → 403 <c>{ code: "NoWriteAccess", entityType, nodes, message }</c>. Never deletes anything,
    /// so <c>knk.admin.delete</c> is not involved.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class RequireRelationshipWriteAttribute : TypeFilterAttribute, IKnkAccessRule
    {
        public RequireRelationshipWriteAttribute() : base(typeof(RelationshipWriteFilter))
        {
        }
    }

    public sealed class RelationshipWriteFilter : IAsyncAuthorizationFilter
    {
        public const string TypeRouteKey = "entityType";
        public const string PropertyRouteKey = "property";

        private readonly IPermissionResolutionService _permissions;
        private readonly KnKDbContext _db;

        public RelationshipWriteFilter(IPermissionResolutionService permissions, KnKDbContext db)
        {
            _permissions = permissions;
            _db = db;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var caller = context.HttpContext.GetKnkCaller();
            if (caller.WebUserId is not { } userId)
            {
                context.Result = caller.IsPluginService
                    ? PluginServiceAuth.Forbidden("Links are changed from the web app.")
                    : PluginServiceAuth.Unauthorized(context.HttpContext);
                return;
            }

            var typeName = context.RouteData.Values[TypeRouteKey]?.ToString();
            var property = context.RouteData.Values[PropertyRouteKey]?.ToString();
            var type = DashboardAccess.Find(typeName);
            if (type == null)
            {
                context.Result = new NotFoundObjectResult(new { error = "UnknownEntityType", message = "Not a dashboard entity type." });
                return;
            }
            var relationship = property == null ? null : RelationshipCatalog.Find(_db.Model, type.TypeName, property);
            if (relationship == null)
            {
                context.Result = new NotFoundObjectResult(new
                {
                    error = "UnknownRelationship",
                    message = $"{type.TypeName} has no relationship '{property}'."
                });
                return;
            }

            var missing = new List<string>();
            foreach (var node in relationship.Nodes)
            {
                var check = await _permissions.CheckAsync(userId, node);
                if (check?.Allowed != true) missing.Add(node);
            }
            if (missing.Count > 0)
            {
                context.Result = Refused(type.TypeName, relationship.Nodes);
            }
        }

        /// <summary>The 403 for a link change without the write node(s); the web app shows "Requires …".</summary>
        public static IActionResult Refused(string typeName, IReadOnlyList<string> nodes) =>
            new ObjectResult(new
            {
                error = "Forbidden",
                code = "NoWriteAccess",
                entityType = typeName,
                nodes,
                message = $"Changing links of {typeName} requires {string.Join(" and ", nodes)}."
            })
            { StatusCode = StatusCodes.Status403Forbidden };
    }
}
