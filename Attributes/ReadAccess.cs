using System.Collections.Concurrent;
using System.Reflection;
using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace knkwebapi_v2.Attributes
{
    /// <summary>
    /// The read categories of the dashboard access model (knk-workspace docs/specs/dashboard-access-model/DESIGN.md
    /// § 4.6.1, Appendix A, PandiO/knk-workspace#280 AM2). Every read endpoint (GET/HEAD, a POST search, or a POST that
    /// carries a read rule of its own) has exactly one; <see cref="ReadAccess.Of(Type, MethodInfo)"/> works it out from
    /// the attributes and ReadAccessRulesTests pins it per endpoint.
    /// </summary>
    public enum ReadCategory
    {
        /// <summary>F: one of the type's read nodes (<see cref="DashboardReadsAttribute"/>), or the plugin key.</summary>
        Full,
        /// <summary>L: any area node or the plugin key (pickers and labels, <c>api/Lookup</c>).</summary>
        Lookup,
        /// <summary>R: any area node or the plugin key (form/display configurations, metadata, validation).</summary>
        FormRuntime,
        /// <summary>O: any area node, plus the ownership the action itself checks (drafts, own world tasks and sessions).</summary>
        OwnRecords,
        /// <summary>P: <c>[AllowAnonymous]</c>.</summary>
        Public,
        /// <summary>G: <c>[RequirePluginService]</c>, the game server only.</summary>
        GameServer,
        /// <summary>U: the action's (or controller's) own stricter rule: a node, self-or-node, owner node or <c>[Authorize]</c>.</summary>
        Stricter,
        /// <summary>
        /// D: outside the access model, the default rule (any logged-in user or the plugin key): reads of settings,
        /// currency, roads and the like that aren't dashboard types. Pinned per endpoint, so a new read has to decide.
        /// </summary>
        Default,
    }

    /// <summary>The effective read rule of one action: its category and, for <see cref="ReadCategory.Full"/>, the nodes.</summary>
    public sealed record ReadRule(ReadCategory Category, IReadOnlyList<string> Nodes, string? Label);

    /// <summary>Marks the access model's own read attributes, so the class rules know an action decided for itself.</summary>
    public interface IKnkReadRule
    {
    }

    /// <summary>
    /// Category F (DESIGN.md § 4.6.5): reads of a dashboard type need one of its read nodes from
    /// <see cref="DashboardAccess"/> (the area node plus the cross-area page nodes of § 4.6.2), or the plugin key.
    /// <list type="bullet">
    ///   <item>On a controller it gates GET/HEAD and POST searches (<see cref="DefaultCallerRequiredFilter.IsSearch"/>).
    ///     Writes keep their own rules: this is <b>not</b> an <see cref="IKnkAccessRule"/>, so a write without a rule
    ///     still gets the default-deny 403.</item>
    ///   <item>On an action it overrides the controller's (other nodes, an area instead of a type) and also makes a
    ///     read-like POST (<c>Domains/world/resolve</c>) a read, which <c>RequireServiceOrPermissionForWrites</c> then
    ///     leaves alone.</item>
    ///   <item>It steps aside for an action with <c>[AllowAnonymous]</c>, <c>[RequirePluginService]</c>, a stricter rule
    ///     of its own or an any-area-node rule (<see cref="ReadAccess.Of(Type, MethodInfo)"/>).</item>
    /// </list>
    /// Checks go through <see cref="IPermissionReadCache"/> (30 s per user and node, dropped when grants change).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class DashboardReadsAttribute : TypeFilterAttribute, IKnkReadRule
    {
        /// <summary>A registry type: its read nodes.</summary>
        public DashboardReadsAttribute(string typeName) : base(typeof(DashboardReadsFilter))
        {
            var entry = DashboardAccess.Find(typeName)
                ?? throw new ArgumentException($"{typeName} is not in DashboardAccess.", nameof(typeName));
            TypeName = entry.TypeName;
            Nodes = entry.ReadNodes.ToArray();
            Label = entry.TypeName;
            Arguments = new object[] { Nodes, Label };
        }

        /// <summary>Records of an area that aren't a dashboard type (menus, world tasks, builder tools): the area node.</summary>
        public DashboardReadsAttribute(DashboardArea area) : base(typeof(DashboardReadsFilter))
        {
            Area = area;
            Nodes = new[] { DashboardAccess.AreaNodeOf(area) };
            Label = area.ToString();
            Arguments = new object[] { Nodes, Label };
        }

        public string? TypeName { get; }

        public DashboardArea? Area { get; }

        /// <summary>Any one of these lets a web user read.</summary>
        public string[] Nodes { get; }

        /// <summary>The type name or the area, for messages and the pinned test.</summary>
        public string Label { get; }
    }

    public sealed class DashboardReadsFilter : IAsyncAuthorizationFilter
    {
        private readonly string[] _nodes;
        private readonly string _label;
        private readonly IPermissionResolutionService _permissions;
        private readonly IPermissionReadCache _cache;

        public DashboardReadsFilter(string[] nodes, string label, IPermissionResolutionService permissions, IPermissionReadCache cache)
        {
            _nodes = nodes;
            _label = label;
            _permissions = permissions;
            _cache = cache;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            if (!ReadAccess.IsReadRequest(context)) return;

            // Class and action may both carry one; only the rule that decides for this action checks.
            var rule = ReadAccess.Of(context.ActionDescriptor);
            if (rule != null && (rule.Category != ReadCategory.Full || !rule.Nodes.SequenceEqual(_nodes, StringComparer.Ordinal)))
            {
                return;
            }

            var caller = context.HttpContext.GetKnkCaller();
            if (caller.IsPluginService) return;
            if (caller.WebUserId is not { } userId)
            {
                context.Result = PluginServiceAuth.Unauthorized(context.HttpContext);
                return;
            }

            if (!await _cache.HoldsAnyAsync(_permissions, userId, _nodes, context.HttpContext))
            {
                context.Result = ReadAccess.Refused(_label, _nodes);
            }
        }
    }

    /// <summary>
    /// Categories L, R and O (DESIGN.md § 4.6.3, § 4.6.4): the caller holds <b>any</b> area node
    /// (<see cref="DashboardAccess.AreaNodes"/>), or is the plugin (<see cref="RequireServiceOrAnyAreaNodeAttribute"/>).
    /// An <see cref="IKnkAccessRule"/>: on a POST it is the action's explicit rule, and
    /// <c>RequireServiceOrPermissionForWrites</c> skips an action that carries one (the validation POSTs are reads).
    /// On a controller it gates every action, writes included; a read that decides otherwise (a stricter rule,
    /// <see cref="DashboardReadsAttribute"/> on the action, <c>[AllowAnonymous]</c>) is left to that rule.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public abstract class AnyAreaNodeRuleAttribute : TypeFilterAttribute, IKnkAccessRule, IKnkReadRule
    {
        protected AnyAreaNodeRuleAttribute(ReadCategory use, bool pluginPasses) : base(typeof(AnyAreaNodeFilter))
        {
            if (use is not (ReadCategory.Lookup or ReadCategory.FormRuntime or ReadCategory.OwnRecords))
            {
                throw new ArgumentOutOfRangeException(nameof(use), use, "Any-area-node rules are for lookups, form runtime and own records.");
            }
            Use = use;
            PluginPasses = pluginPasses;
            Arguments = new object[] { use, pluginPasses };
        }

        /// <summary>Lookup, FormRuntime or OwnRecords.</summary>
        public ReadCategory Use { get; }

        /// <summary>Whether the plugin key alone passes.</summary>
        public bool PluginPasses { get; }
    }

    /// <summary>Any area node, or the plugin key (categories L, R and O for the world-task and workflow reads the plugin makes).</summary>
    public sealed class RequireServiceOrAnyAreaNodeAttribute : AnyAreaNodeRuleAttribute
    {
        public RequireServiceOrAnyAreaNodeAttribute(ReadCategory use = ReadCategory.FormRuntime) : base(use, pluginPasses: true)
        {
        }
    }

    /// <summary>Any area node, logged-in web users only (form drafts, which belong to the caller's JWT, #186).</summary>
    public sealed class RequireAnyAreaNodeAttribute : AnyAreaNodeRuleAttribute
    {
        public RequireAnyAreaNodeAttribute(ReadCategory use = ReadCategory.OwnRecords) : base(use, pluginPasses: false)
        {
        }
    }

    public sealed class AnyAreaNodeFilter : IAsyncAuthorizationFilter
    {
        private readonly ReadCategory _use;
        private readonly bool _pluginPasses;
        private readonly IPermissionResolutionService _permissions;
        private readonly IPermissionReadCache _cache;

        public AnyAreaNodeFilter(ReadCategory use, bool pluginPasses, IPermissionResolutionService permissions, IPermissionReadCache cache)
        {
            _use = use;
            _pluginPasses = pluginPasses;
            _permissions = permissions;
            _cache = cache;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any()) return;

            if (ReadAccess.IsReadRequest(context))
            {
                var rule = ReadAccess.Of(context.ActionDescriptor);
                if (rule != null && rule.Category != _use) return;
            }

            var caller = context.HttpContext.GetKnkCaller();
            if (caller.IsPluginService && _pluginPasses) return;
            if (caller.WebUserId is not { } userId)
            {
                context.Result = PluginServiceAuth.Unauthorized(context.HttpContext);
                return;
            }

            if (!await _cache.HoldsAnyAsync(_permissions, userId, DashboardAccess.AreaNodes, context.HttpContext))
            {
                context.Result = PluginServiceAuth.Forbidden(
                    "Requires a staff area permission (any of " + string.Join(", ", DashboardAccess.AreaNodes) + ").");
            }
        }
    }

    /// <summary>Works out an action's read category from its attributes (DESIGN.md § 4.6.5); cached per action.</summary>
    public static class ReadAccess
    {
        private static readonly ConcurrentDictionary<(Type, MethodInfo), ReadRule> Rules = new();

        /// <summary>The rule of a controller action, or null for anything else (a filter then checks on its own terms).</summary>
        public static ReadRule? Of(ActionDescriptor action) =>
            action is ControllerActionDescriptor controllerAction
                ? Of(controllerAction.ControllerTypeInfo.AsType(), controllerAction.MethodInfo)
                : null;

        /// <summary>
        /// The first that applies: <c>[AllowAnonymous]</c> → P; <c>[RequirePluginService]</c> → G; on the action an
        /// any-area-node rule → L/R/O, another access rule or <c>[Authorize]</c> → U, <see cref="DashboardReadsAttribute"/>
        /// → F; then the same on the controller; otherwise D. <c>RequireServiceOrPermissionForWrites</c> and
        /// <c>RequireServiceOrLoggedIn</c> don't decide reads.
        /// </summary>
        public static ReadRule Of(Type controller, MethodInfo method) => Rules.GetOrAdd((controller, method), key => Resolve(key.Item1, key.Item2));

        private static ReadRule Resolve(Type controller, MethodInfo method)
        {
            var onMethod = method.GetCustomAttributes(true);
            var onClass = controller.GetCustomAttributes(true);

            if (onMethod.Concat(onClass).OfType<IAllowAnonymous>().Any()) return new ReadRule(ReadCategory.Public, Array.Empty<string>(), null);
            if (onMethod.Concat(onClass).OfType<RequirePluginServiceAttribute>().Any()) return new ReadRule(ReadCategory.GameServer, Array.Empty<string>(), null);

            return Decide(onMethod) ?? Decide(onClass) ?? new ReadRule(ReadCategory.Default, Array.Empty<string>(), null);
        }

        private static ReadRule? Decide(object[] attributes)
        {
            if (attributes.OfType<AnyAreaNodeRuleAttribute>().FirstOrDefault() is { } anyArea)
            {
                return new ReadRule(anyArea.Use, DashboardAccess.AreaNodes, null);
            }
            if (attributes.Any(IsStricter))
            {
                return new ReadRule(ReadCategory.Stricter, Array.Empty<string>(), null);
            }
            if (attributes.OfType<DashboardReadsAttribute>().FirstOrDefault() is { } reads)
            {
                return new ReadRule(ReadCategory.Full, reads.Nodes, reads.Label);
            }
            return null;
        }

        private static bool IsStricter(object attribute) =>
            attribute is IAuthorizeData
            || (attribute is IKnkAccessRule and not IKnkReadRule and not RequireServiceOrPermissionForWritesAttribute
                and not RequireServiceOrLoggedInAttribute and not RequirePluginServiceAttribute);

        /// <summary>
        /// Whether this request reads: GET/HEAD, a POST search, or a POST whose action carries a read rule of its own
        /// (<see cref="DashboardReadsAttribute"/> or an any-area-node rule: <c>world/resolve</c>, the validation POSTs).
        /// </summary>
        public static bool IsReadRequest(AuthorizationFilterContext context)
        {
            var method = context.HttpContext.Request.Method;
            if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method)) return true;
            if (!HttpMethods.IsPost(method)) return false;
            return DefaultCallerRequiredFilter.IsSearch(context.ActionDescriptor) || HasActionReadRule(context.ActionDescriptor);
        }

        /// <summary>The action itself (not its controller) carries <see cref="DashboardReadsAttribute"/> or an any-area-node rule.</summary>
        public static bool HasActionReadRule(ActionDescriptor action) =>
            action is ControllerActionDescriptor controllerAction
            && controllerAction.MethodInfo.GetCustomAttributes(true).OfType<IKnkReadRule>().Any();

        /// <summary>The 403 for a full read without a read node; <c>code</c> lets the web app say "You don't have access to …".</summary>
        public static IActionResult Refused(string label, IReadOnlyList<string> nodes) =>
            new ObjectResult(new
            {
                error = "Forbidden",
                code = "NoReadAccess",
                entityType = label,
                nodes,
                message = $"You don't have access to {label}. Requires one of: {string.Join(", ", nodes)}."
            })
            { StatusCode = StatusCodes.Status403Forbidden };
    }
}
