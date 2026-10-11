using System;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace knkwebapi_v2.Attributes
{
    /// <summary>
    /// Limits an endpoint to holders of an owner node (KNG-34 D12, L1-17, D24,
    /// IMPLEMENTATION_PLAN.md §6). The caller is the JWT web user, or the in-game player the plugin
    /// names in X-Acting-User-Id; nobody → 401. The node must resolve to Granted. For the nodes in
    /// <see cref="OwnerPermissions.ExactGrantOnly"/> the deciding grant must also be the node itself
    /// — a <c>*</c>, <c>knk.*</c> or <c>knk.owner.*</c> grant gets 403 — so wildcards never unlock
    /// personal diagnostic data or GDPR deletion. Grant those nodes directly on the user (a
    /// user-level wildcard is checked before group grants and would decide first).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class RequireOwnerPermissionAttribute : TypeFilterAttribute
    {
        public RequireOwnerPermissionAttribute(string node) : base(typeof(RequireOwnerPermissionFilter))
        {
            Node = node;
            Arguments = new object[] { node };
        }

        public string Node { get; }
    }

    public class RequireOwnerPermissionFilter : IAsyncAuthorizationFilter
    {
        private readonly string _node;
        private readonly IPermissionResolutionService _permissions;

        public RequireOwnerPermissionFilter(string node, IPermissionResolutionService permissions)
        {
            _node = node;
            _permissions = permissions;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var caller = context.HttpContext.GetKnkCaller();
            var userId = caller.IsWebUser ? caller.WebUserId : caller.IsPluginService ? caller.ActingUserId : null;
            if (userId == null)
            {
                context.Result = PluginServiceAuth.Unauthorized(context.HttpContext);
                return;
            }

            var check = await _permissions.CheckAsync(userId.Value, _node);
            if (OwnerPermissions.ExactGrantOnly.Contains(_node))
            {
                if (!IsExactGrant(check?.Result, check?.MatchedNode, _node))
                {
                    context.Result = PluginServiceAuth.Forbidden($"Requires an explicit grant of the owner permission {_node}.");
                }
            }
            else if (check?.Result != PermissionResolutionResult.Granted)
            {
                context.Result = PluginServiceAuth.Forbidden($"Requires the owner permission {_node}.");
            }
        }

        /// <summary>Granted, and decided by a grant of exactly <paramref name="node"/>.</summary>
        public static bool IsExactGrant(PermissionResolutionResult? result, string? matchedNode, string node) =>
            result == PermissionResolutionResult.Granted && string.Equals(matchedNode, node, StringComparison.Ordinal);
    }
}
