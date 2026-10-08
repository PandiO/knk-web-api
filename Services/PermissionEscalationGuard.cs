using knkwebapi_v2.Attributes;
using knkwebapi_v2.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Services
{
    /// <summary>The outcome of an escalation check: allowed, or refused with the reason to show.</summary>
    public sealed record EscalationCheck(bool Allowed, string? Message)
    {
        public static readonly EscalationCheck Ok = new(true, null);
        public static EscalationCheck Refused(string message) => new(false, message);
    }

    /// <summary>
    /// Stops staff from handing out more than they hold (closed-alpha hardening WP3): a web user
    /// may only grant, deny or revoke a node they hold themselves, may only assign or remove a group
    /// whose every node they hold, and may never change their own permissions unless they hold
    /// <c>*</c> (the owner). The game server is not checked here: the plugin does its own in-game
    /// node checks before calling (see <see cref="EscalationGuardHttp"/>).
    /// </summary>
    public interface IPermissionEscalationGuard
    {
        /// <summary>May <paramref name="actorId"/> grant, deny or revoke <paramref name="node"/> on
        /// the holder (a user or a group) <paramref name="targetHolderId"/>?</summary>
        Task<EscalationCheck> CanGrantNodeAsync(int actorId, int targetHolderId, string node);

        /// <summary>May <paramref name="actorId"/> add <paramref name="targetUserId"/> to, or remove them
        /// from, group <paramref name="groupId"/>?</summary>
        Task<EscalationCheck> CanAssignGroupAsync(int actorId, int targetUserId, int groupId);

        /// <summary>May <paramref name="actorId"/> make <paramref name="parentGroupId"/> the parent of
        /// a group? A child inherits every node of its parent chain, so the actor must hold them all.</summary>
        Task<EscalationCheck> CanInheritFromGroupAsync(int actorId, int parentGroupId);
    }

    public class PermissionEscalationGuard : IPermissionEscalationGuard
    {
        public const string Wildcard = "*";
        public const string NotHeldMessage = "You can't grant a permission you don't have.";
        public const string SelfMessage = "You can't change your own permissions.";
        public const string GroupNotHeldMessage = "You can't hand out a group with permissions you don't have.";

        private readonly IPermissionResolutionService _permissions;
        private readonly IPermissionGroupRepository _groups;

        public PermissionEscalationGuard(IPermissionResolutionService permissions, IPermissionGroupRepository groups)
        {
            _permissions = permissions;
            _groups = groups;
        }

        public async Task<EscalationCheck> CanGrantNodeAsync(int actorId, int targetHolderId, string node)
        {
            var holdsWildcard = await HoldsAsync(actorId, Wildcard);
            if (actorId == targetHolderId && !holdsWildcard)
            {
                return EscalationCheck.Refused(SelfMessage);
            }

            // Invalid input is the service's to reject (400), not an escalation.
            if (string.IsNullOrWhiteSpace(node))
            {
                return EscalationCheck.Ok;
            }

            // A deny (value=false) needs the node too: otherwise staff could strip a node from
            // someone above them. "*" can only come from someone holding "*".
            return holdsWildcard || await HoldsAsync(actorId, node.Trim())
                ? EscalationCheck.Ok
                : EscalationCheck.Refused(NotHeldMessage);
        }

        public async Task<EscalationCheck> CanAssignGroupAsync(int actorId, int targetUserId, int groupId)
        {
            var holdsWildcard = await HoldsAsync(actorId, Wildcard);
            if (actorId == targetUserId && !holdsWildcard)
            {
                return EscalationCheck.Refused(SelfMessage);
            }
            return holdsWildcard ? EscalationCheck.Ok : await HoldsEveryGroupNodeAsync(actorId, groupId);
        }

        public async Task<EscalationCheck> CanInheritFromGroupAsync(int actorId, int parentGroupId)
        {
            return await HoldsAsync(actorId, Wildcard) ? EscalationCheck.Ok : await HoldsEveryGroupNodeAsync(actorId, parentGroupId);
        }

        private async Task<EscalationCheck> HoldsEveryGroupNodeAsync(int actorId, int groupId)
        {
            var now = DateTime.UtcNow;
            var nodes = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<int>();
            int? currentId = groupId;
            while (currentId.HasValue && visited.Add(currentId.Value))
            {
                var group = await _groups.GetByIdAsync(currentId.Value);
                if (group == null)
                {
                    break; // an unknown group is the service's 404, not an escalation
                }
                foreach (var grant in group.Grants ?? new List<Models.PermissionGrant>())
                {
                    if (grant.ExpiresAt == null || grant.ExpiresAt > now)
                    {
                        nodes.Add(grant.Node);
                    }
                }
                currentId = group.ParentGroupId;
            }

            foreach (var node in nodes)
            {
                if (!await HoldsAsync(actorId, node))
                {
                    return EscalationCheck.Refused(GroupNotHeldMessage);
                }
            }
            return EscalationCheck.Ok;
        }

        private async Task<bool> HoldsAsync(int userId, string node)
        {
            var check = await _permissions.CheckAsync(userId, node);
            return check?.Allowed == true;
        }
    }

    /// <summary>Runs an escalation check for the current request: the plugin bypasses it, a web
    /// user gets 403 EscalationRefused when it fails.</summary>
    public static class EscalationGuardHttp
    {
        public static async Task<IActionResult?> RefuseIfEscalationAsync(HttpContext? httpContext, Func<int, Task<EscalationCheck>> check)
        {
            var caller = httpContext?.GetKnkCaller();
            if (caller?.IsPluginService == true)
            {
                return null;
            }
            if (caller?.WebUserId == null)
            {
                return new UnauthorizedObjectResult(new { error = "Unauthorized", message = "Log in to use this." });
            }

            var result = await check(caller.WebUserId.Value);
            return result.Allowed
                ? null
                : new ObjectResult(new { error = "EscalationRefused", message = result.Message })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
        }
    }
}
