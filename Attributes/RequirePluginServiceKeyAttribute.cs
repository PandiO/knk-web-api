using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace knkwebapi_v2.Attributes
{
    /// <summary>
    /// Restricts an endpoint to the Minecraft plugin's service client (siege match writes,
    /// docs/specs/siege-minigame/DESIGN.md §11.2) with a shared key, <b>opt-in</b>:
    /// <list type="bullet">
    /// <item><c>Security:PluginServiceKey</c> empty or missing (the default): the endpoint stays open,
    /// like every other endpoint the plugin calls today (its default <c>api.auth.type</c> is
    /// <c>none</c>, so requiring a JWT would lock it out).</item>
    /// <item>Set: the request must carry that key in the <c>Security:PluginServiceKeyHeader</c> header
    /// (default <c>X-API-Key</c>, what the plugin's <c>api.auth.type: apikey</c> sends), else 401.</item>
    /// </list>
    /// With <see cref="AllowAdmins"/>, a caller whose JWT carries the Admin role (the
    /// <c>RequireAdmin</c> policy's rule) is let through without the key too.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class RequirePluginServiceKeyAttribute : Attribute, IAuthorizationFilter
    {
        public const string KeySetting = "Security:PluginServiceKey";
        public const string HeaderSetting = "Security:PluginServiceKeyHeader";
        public const string DefaultHeader = "X-API-Key";

        /// <summary>Also accept an authenticated admin (role "Admin") without the key.</summary>
        public bool AllowAdmins { get; set; }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var configuration = context.HttpContext.RequestServices.GetService<IConfiguration>();
            var expected = configuration?[KeySetting];
            if (string.IsNullOrEmpty(expected)) return;

            var header = configuration?[HeaderSetting];
            if (string.IsNullOrWhiteSpace(header)) header = DefaultHeader;

            var user = context.HttpContext.User;
            if (AllowAdmins && user?.Identity?.IsAuthenticated == true
                && (user.IsInRole("Admin") || user.Claims.Any(c => c.Type == "role" && c.Value == "Admin")))
                return;

            var supplied = context.HttpContext.Request.Headers[header].FirstOrDefault();
            if (supplied == null || !FixedTimeEquals(supplied, expected))
            {
                context.Result = new UnauthorizedObjectResult(new
                {
                    code = "PluginServiceKeyRequired",
                    message = $"This endpoint accepts only the plugin's service client ({header} header)."
                });
            }
        }

        private static bool FixedTimeEquals(string a, string b) =>
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    }
}
