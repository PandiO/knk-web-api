using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace knkwebapi_v2.Attributes
{
    /// <summary>Permission nodes that gate web-app admin areas.</summary>
    public static class StaffPermissions
    {
        /// <summary>
        /// Web moderation (player list, player profile, audit log). The same node that opens the
        /// in-game Player manager (user-management DESIGN.md §8), so staff who manage players
        /// in-game get the web moderation pages too; also matched by knk.admin.* and *.
        /// </summary>
        public const string ManageUsers = "knk.admin.user.manage";
    }

    /// <summary>
    /// Limits an endpoint to logged-in users who hold <c>node</c> in the in-house permission
    /// system: 401 without a JWT identity, 403 when the node doesn't resolve to granted. Use only
    /// on endpoints knk-plugin doesn't call — the plugin calls anonymously (no JWT) and would be
    /// locked out.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class RequirePermissionAttribute : TypeFilterAttribute
    {
        public RequirePermissionAttribute(string node) : base(typeof(RequirePermissionFilter))
        {
            Arguments = new object[] { node };
        }
    }

    public class RequirePermissionFilter : IAsyncAuthorizationFilter
    {
        private readonly string _node;
        private readonly IPermissionResolutionService _permissions;

        public RequirePermissionFilter(string node, IPermissionResolutionService permissions)
        {
            _node = node;
            _permissions = permissions;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var userId = UserIdFrom(context.HttpContext.User);
            if (userId == null)
            {
                context.Result = new UnauthorizedObjectResult(new { error = "Unauthorized", message = "Log in to use this." });
                return;
            }

            var check = await _permissions.CheckAsync(userId.Value, _node);
            if (check?.Allowed != true)
            {
                context.Result = new ObjectResult(new { error = "Forbidden", message = $"Requires the {_node} permission." })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }
        }

        private static int? UserIdFrom(ClaimsPrincipal? principal)
        {
            if (principal?.Identity?.IsAuthenticated != true)
            {
                return null;
            }
            var claim = principal.FindFirst("uid")
                ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)
                ?? principal.FindFirst(ClaimTypes.NameIdentifier);
            return claim != null && int.TryParse(claim.Value, out var id) ? id : null;
        }
    }
}
