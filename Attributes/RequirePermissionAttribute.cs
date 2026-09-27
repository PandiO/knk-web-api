using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using knkwebapi_v2.Services;
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

        /// <summary>
        /// Web lootbox administration: types, specials, spawn areas, the singleton configuration and the
        /// item-instance lookup (knk-workspace docs/specs/lootboxes/DESIGN.md §3.3, §3.6). Web-only: the in-game
        /// admin commands use the knk.lootbox.admin.* nodes. Also matched by knk.admin.* and *.
        /// </summary>
        public const string ManageLootboxes = "knk.admin.lootbox.manage";

        /// <summary>
        /// Domain discovery administration: reward rules, per-domain overrides, statistics and
        /// resetting a player's discovery. The same node as the in-game /knk discovery subcommand
        /// (docs/specs/domain-discovery/DESIGN.md §3.6); also matched by knk.admin.* and *.
        /// </summary>
        public const string ManageDiscovery = "knk.admin.discovery";

        // The in-game /knk user nodes (knk-plugin plugin.yml), checked for web callers of the
        // same writes (KNG-22). All are children of knk.admin, so knk.admin.* and * match too.
        public const string UserCoins = "knk.admin.user.coins";
        public const string UserGems = "knk.admin.user.gems";
        public const string UserXp = "knk.admin.user.xp";
        public const string UserSalary = "knk.admin.user.salary";
        public const string UserGroups = "knk.admin.user.group";
        public const string UserPermissions = "knk.admin.user.perm";
        public const string Freeze = "knk.freeze";
        public const string Unfreeze = "knk.unfreeze";

        // Kits (docs/specs/kits/DESIGN.md §4.3): knk.kit.manage gates Kit CRUD, knk.kit.give a
        // staff grant.
        public const string ManageKits = "knk.kit.manage";
        public const string GiveKits = "knk.kit.give";

        /// <summary>Economy settings (salary configuration, currency policy) —
        /// docs/specs/currency-payments/DESIGN.md §3.8.</summary>
        public const string CurrencyPolicy = "knk.admin.currency.policy";

        // The rest of the knk.admin.currency umbrella (currency DESIGN.md §3.8, Phase 4).
        /// <summary>Read any player's ledger history and the all-players balance event log.</summary>
        public const string CurrencyHistory = "knk.admin.currency.history";
        /// <summary>Reverse a ledger transaction.</summary>
        public const string CurrencyReverse = "knk.admin.currency.reverse";
        /// <summary>Lock or unlock a player's transfers.</summary>
        public const string CurrencyLock = "knk.admin.currency.lock";
        /// <summary>Exempt from the per-staff daily grant cap (CurrencyPolicy.AdminDailyGrantCapPerActor).</summary>
        public const string CurrencyUnlimited = "knk.admin.currency.unlimited";
        /// <summary>See and acknowledge currency anomaly alerts, run the reconciliation, get the
        /// in-game alert notices (currency Phase 5).</summary>
        public const string CurrencyAlerts = "knk.admin.currency.alerts";

        /// <summary>Siege setup: SiegeConfiguration, scenarios, lobbies, teams, objectives,
        /// spawnpoints (the web-app forms; plugin.yml "/siege admin manage"). Scenario rewards
        /// are a currency lever (currency DESIGN.md §3.4).</summary>
        public const string SiegeManage = "knk.siege.admin.manage";

        /// <summary>Gate administration (plugin.yml knk.gate.admin): the structure-level gate overrides.</summary>
        public const string GateAdmin = "knk.gate.admin";

        /// <summary>Server-wide admin settings with no narrower node (audit-log retention).</summary>
        public const string ServerConfig = "knk.admin.config";

        /// <summary>Read players' private messages (docs/specs/private-messages/DESIGN.md §3.5) -
        /// owner group; every read is audited.</summary>
        public const string ReadPrivateMessages = "knk.pmlog.read";

        /// <summary>Road network administration: profiles, seeds, node/edge review and street
        /// labels (docs/specs/navigation/DESIGN.md §7, plan D1). The same node as the in-game
        /// /knk road subcommand; also matched by knk.admin.* and *.</summary>
        public const string RoadManage = "knk.admin.road";
    }

    /// <summary>
    /// Limits an endpoint to logged-in users who hold <c>node</c> in the in-house permission
    /// system: 401 without a JWT identity, 403 when the node doesn't resolve to granted. Use only
    /// on endpoints knk-plugin doesn't call — the plugin has no JWT and would be locked out; for
    /// endpoints both call, use RequireServiceOrPermission instead.
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

        /// <summary>The logged-in user's id from their JWT claims, or null.</summary>
        public static int? UserIdFrom(ClaimsPrincipal? principal)
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
