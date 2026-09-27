using System.Security.Cryptography;
using System.Text;
using knkwebapi_v2.Extensions;
using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace knkwebapi_v2.Attributes
{
    /// <summary>
    /// Who is calling, resolved once per request (docs/specs/currency-payments/DESIGN.md §1.4 A1,
    /// KNG-22): knk-plugin (a valid X-API-Key), a logged-in web user (JWT), or nobody.
    /// </summary>
    public sealed class KnkCaller
    {
        public KnkCaller(bool isPluginService, bool isWebUser, int? webUserId, int? actingUserId)
        {
            IsPluginService = isPluginService;
            IsWebUser = isWebUser;
            WebUserId = webUserId;
            ActingUserId = isPluginService ? actingUserId : null;
        }

        /// <summary>The request carries the plugin's shared key (Security:PluginApiKey), or the
        /// development-only opt-out is on (Security:AllowUnauthenticatedPluginCalls).</summary>
        public bool IsPluginService { get; }

        /// <summary>The request carries a valid JWT.</summary>
        public bool IsWebUser { get; }

        /// <summary>The JWT's user id; null for the plugin and anonymous callers.</summary>
        public int? WebUserId { get; }

        /// <summary>The in-game staff member the plugin names in X-Acting-User-Id. Only ever set
        /// on a plugin-service request, so nobody else can attribute a change to someone.</summary>
        public int? ActingUserId { get; }

        /// <summary>
        /// Who an audited change is made by: the logged-in web user (who can never act as someone
        /// else), else the in-game staff member the plugin names, else null (system).
        /// </summary>
        public int? ActorUserId => IsWebUser ? WebUserId : ActingUserId;
    }

    /// <summary>
    /// The shared-key check between knk-plugin and this API (KNG-22). The plugin sends
    /// <c>X-API-Key: &lt;Security:PluginApiKey&gt;</c> on every request (plugin config.yml
    /// <c>api.auth.type: apikey</c>). With the key unset the plugin is not trusted at all (fail
    /// closed), unless the API runs in Development and <c>Security:AllowUnauthenticatedPluginCalls</c>
    /// is true, which treats every anonymous caller as the plugin.
    /// </summary>
    public static class PluginServiceAuth
    {
        public const string ApiKeyHeader = "X-API-Key";
        public const string ActingUserHeader = "X-Acting-User-Id";
        public const string ApiKeyConfigKey = "Security:PluginApiKey";
        public const string AllowUnauthenticatedConfigKey = "Security:AllowUnauthenticatedPluginCalls";

        private const string CallerItemKey = "knk.caller";

        /// <summary>The request's caller, resolved on first use and cached for the request.</summary>
        public static KnkCaller GetKnkCaller(this HttpContext httpContext)
        {
            if (httpContext.Items.TryGetValue(CallerItemKey, out var cached) && cached is KnkCaller caller)
            {
                return caller;
            }
            caller = Resolve(httpContext);
            httpContext.Items[CallerItemKey] = caller;
            return caller;
        }

        private static KnkCaller Resolve(HttpContext httpContext)
        {
            var isWebUser = httpContext.User?.Identity?.IsAuthenticated == true;
            var webUserId = isWebUser ? httpContext.User!.GetUserId() : null;

            var isPluginService = KeyStatus(httpContext) is PluginKeyStatus.Valid or PluginKeyStatus.DevelopmentBypass;
            int? actingUserId = int.TryParse(httpContext.Request.Headers[ActingUserHeader].ToString(), out var id) && id > 0
                ? id
                : null;

            return new KnkCaller(isPluginService, isWebUser, webUserId, actingUserId);
        }

        internal enum PluginKeyStatus { Valid, DevelopmentBypass, Missing, Wrong, NotConfigured }

        internal static PluginKeyStatus KeyStatus(HttpContext httpContext)
        {
            var configuration = httpContext.RequestServices?.GetService(typeof(IConfiguration)) as IConfiguration;
            var requiredKey = configuration?[ApiKeyConfigKey];
            var sentKey = httpContext.Request.Headers[ApiKeyHeader].ToString();

            if (string.IsNullOrEmpty(requiredKey))
            {
                var environment = httpContext.RequestServices?.GetService(typeof(IHostEnvironment)) as IHostEnvironment;
                var optedOut = bool.TryParse(configuration?[AllowUnauthenticatedConfigKey], out var allow) && allow;
                return environment?.IsDevelopment() == true && optedOut
                    ? PluginKeyStatus.DevelopmentBypass
                    : PluginKeyStatus.NotConfigured;
            }

            if (string.IsNullOrEmpty(sentKey))
            {
                return PluginKeyStatus.Missing;
            }

            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(sentKey), Encoding.UTF8.GetBytes(requiredKey))
                ? PluginKeyStatus.Valid
                : PluginKeyStatus.Wrong;
        }

        /// <summary>The 401 for a caller that is neither the plugin nor logged in. Says why the key
        /// didn't count, so a misconfigured plugin shows the reason in its log.</summary>
        internal static IActionResult Unauthorized(HttpContext httpContext)
        {
            var message = KeyStatus(httpContext) switch
            {
                PluginKeyStatus.NotConfigured => "Log in to use this. (Game-server calls are refused: Security:PluginApiKey is not set on the API.)",
                PluginKeyStatus.Wrong => "The X-API-Key header doesn't match the API's Security:PluginApiKey.",
                _ => "Log in to use this."
            };
            return new UnauthorizedObjectResult(new { error = "Unauthorized", message });
        }

        internal static IActionResult Forbidden(string message) =>
            new ObjectResult(new { error = "Forbidden", message }) { StatusCode = StatusCodes.Status403Forbidden };
    }

    /// <summary>
    /// Limits an endpoint to knk-plugin (a valid X-API-Key) or to a logged-in web user who holds
    /// <c>node</c> in the in-house permission system (KNG-22). Anonymous → 401, a web user
    /// without the node → 403. The plugin does its own in-game node checks before calling, so
    /// the key alone passes. Read the caller (and the audit actor) with
    /// <c>HttpContext.GetKnkCaller()</c>. Several of these on one action must all pass.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class RequireServiceOrPermissionAttribute : TypeFilterAttribute
    {
        public RequireServiceOrPermissionAttribute(string node) : base(typeof(RequireServiceOrPermissionFilter))
        {
            Node = node;
            Arguments = new object[] { node };
        }

        public string Node { get; }
    }

    public class RequireServiceOrPermissionFilter : IAsyncAuthorizationFilter
    {
        private readonly string _node;
        private readonly IPermissionResolutionService _permissions;

        public RequireServiceOrPermissionFilter(string node, IPermissionResolutionService permissions)
        {
            _node = node;
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

            var check = await _permissions.CheckAsync(caller.WebUserId.Value, _node);
            if (check?.Allowed != true)
            {
                context.Result = PluginServiceAuth.Forbidden($"Requires the {_node} permission.");
            }
        }
    }

    /// <summary>
    /// Limits an endpoint to knk-plugin (a valid X-API-Key): routes the game server calls for a
    /// player (presence, salary on join, kit claims) that no web page uses. Anonymous → 401,
    /// a logged-in web user → 403.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class RequirePluginServiceAttribute : Attribute, IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var caller = context.HttpContext.GetKnkCaller();
            if (caller.IsPluginService)
            {
                return;
            }

            context.Result = caller.IsWebUser
                ? PluginServiceAuth.Forbidden("Only the game server can call this.")
                : PluginServiceAuth.Unauthorized(context.HttpContext);
        }
    }
}
