using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Attributes
{
    /// <summary>
    /// Marks an attribute as an explicit access rule for an endpoint (closed-alpha hardening WP1).
    /// <see cref="DefaultCallerRequiredFilter"/> refuses a write (non-GET) action that carries none
    /// of these, <c>[Authorize]</c> or <c>[AllowAnonymous]</c>.
    /// </summary>
    public interface IKnkAccessRule
    {
    }

    /// <summary>
    /// Default-deny for every MVC action (closed-alpha hardening WP1, decision D6), registered
    /// globally in Program.cs:
    /// <list type="number">
    /// <item>An action (or its controller) with <c>[AllowAnonymous]</c> passes.</item>
    /// <item>Otherwise the caller must be knk-plugin (a valid X-API-Key) or a logged-in web user,
    /// else 401.</item>
    /// <item>A write (anything but GET/HEAD/OPTIONS) also needs an explicit rule
    /// (<see cref="IKnkAccessRule"/> or <c>[Authorize]</c>), else 403 "This endpoint has no access
    /// rule." and an error in the log, so a new endpoint can't ship open by accident.</item>
    /// </list>
    /// Reads without a rule stay "any logged-in user or the game server". Minimal APIs
    /// (MapHealthChecks) never reach MVC filters and are unaffected.
    /// </summary>
    public class DefaultCallerRequiredFilter : IAsyncAuthorizationFilter
    {
        private readonly ILogger<DefaultCallerRequiredFilter> _logger;

        public DefaultCallerRequiredFilter(ILogger<DefaultCallerRequiredFilter> logger)
        {
            _logger = logger;
        }

        public Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var metadata = context.ActionDescriptor.EndpointMetadata;
            if (metadata.OfType<IAllowAnonymous>().Any())
            {
                return Task.CompletedTask;
            }

            var caller = context.HttpContext.GetKnkCaller();
            if (!caller.IsPluginService && !caller.IsWebUser)
            {
                context.Result = PluginServiceAuth.Unauthorized(context.HttpContext);
                return Task.CompletedTask;
            }

            if (IsRead(context.HttpContext.Request.Method) || HasAccessRule(metadata))
            {
                return Task.CompletedTask;
            }

            var action = context.ActionDescriptor is ControllerActionDescriptor descriptor
                ? $"{descriptor.ControllerTypeInfo.Name}.{descriptor.MethodInfo.Name}"
                : context.ActionDescriptor.DisplayName;
            _logger.LogError(
                "Refused {Method} {Path}: action {Action} is a write without an access rule. Add RequirePermission, RequireServiceOrPermission, RequirePluginService, RequireServiceSelfOrPermission, Authorize or AllowAnonymous.",
                context.HttpContext.Request.Method, context.HttpContext.Request.Path, action);
            context.Result = PluginServiceAuth.Forbidden("This endpoint has no access rule.");
            return Task.CompletedTask;
        }

        /// <summary>GET, HEAD and OPTIONS are reads; everything else is a write.</summary>
        public static bool IsRead(string method) =>
            HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);

        /// <summary>True when the metadata (controller + action attributes) holds an explicit rule.</summary>
        public static bool HasAccessRule(IEnumerable<object> metadata) =>
            metadata.Any(m => m is IKnkAccessRule or IAuthorizeData or IAllowAnonymous);
    }

    /// <summary>
    /// Passes knk-plugin (a valid X-API-Key) and any logged-in web user; anonymous → 401. For
    /// writes where the action itself decides what the caller may do (e.g. a player's own link
    /// code), so the rule is explicit rather than an open write.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class RequireServiceOrLoggedInAttribute : Attribute, IAuthorizationFilter, IKnkAccessRule
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var caller = context.HttpContext.GetKnkCaller();
            if (!caller.IsPluginService && !caller.IsWebUser)
            {
                context.Result = PluginServiceAuth.Unauthorized(context.HttpContext);
            }
        }
    }

    /// <summary>
    /// Class-level form of <see cref="RequireServiceOrPermissionAttribute"/> that only gates
    /// writes (any method but GET/HEAD/OPTIONS): the write needs knk-plugin or a web user holding
    /// <c>node</c>, while reads keep the default "logged in or the game server" rule. Used on
    /// the generic CRUD controllers (closed-alpha hardening WP1.3), where every write needs the
    /// controller's node and the reads stay open to logged-in users. A POST search is a write
    /// under this rule.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class RequireServiceOrPermissionForWritesAttribute : TypeFilterAttribute, IKnkAccessRule
    {
        public RequireServiceOrPermissionForWritesAttribute(string node) : base(typeof(RequireServiceOrPermissionForWritesFilter))
        {
            Node = node;
            Arguments = new object[] { node };
        }

        public string Node { get; }
    }

    public class RequireServiceOrPermissionForWritesFilter : IAsyncAuthorizationFilter
    {
        private readonly RequireServiceOrPermissionFilter _inner;

        public RequireServiceOrPermissionForWritesFilter(string node, IPermissionResolutionService permissions)
        {
            _inner = new RequireServiceOrPermissionFilter(node, permissions);
        }

        public Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            return DefaultCallerRequiredFilter.IsRead(context.HttpContext.Request.Method)
                ? Task.CompletedTask
                : _inner.OnAuthorizationAsync(context);
        }
    }
}
