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
    /// ranked below their own highest group (Weight) whose every granted node they hold (denies don't
    /// count; premium-tier groups' own nodes are exempt for holders of
    /// knk.admin.user.group.premium), and may never change their own permissions unless they hold
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
        public const string RankMessage = "You can only hand out or remove groups ranked below your own.";

        private readonly IPermissionResolutionService _permissions;
        private readonly IPermissionGroupRepository _groups;
        private readonly IUserPermissionGroupRepository _memberships;

        public PermissionEscalationGuard(IPermissionResolutionService permissions, IPermissionGroupRepository groups,
            IUserPermissionGroupRepository memberships)
        {
            _permissions = permissions;
            _groups = groups;
            _memberships = memberships;
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
            if (holdsWildcard)
            {
                return EscalationCheck.Ok;
            }
            // Rank: only groups below the actor's own highest group, so an Admin can make Moderators
            // and Staff but not other Admins, and can't remove a peer's or a superior's group.
            var target = await _groups.GetByIdAsync(groupId);
            if (target != null && target.Weight >= await HighestRankAsync(actorId))
            {
                return EscalationCheck.Refused(RankMessage);
            }
            // A premium rank's perks don't make staff more powerful, so knk.admin.user.group.premium
            // lets staff hand them out without holding them. Any non-premium group in the rank's
            // parent chain still needs every node held, so a premium rank can't smuggle staff nodes.
            var skipPremium = await HoldsAsync(actorId, Attributes.StaffPermissions.UserPremiumGroups);
            return await HoldsEveryGroupNodeAsync(actorId, groupId, skipPremium);
        }

        public async Task<EscalationCheck> CanInheritFromGroupAsync(int actorId, int parentGroupId)
        {
            return await HoldsAsync(actorId, Wildcard) ? EscalationCheck.Ok : await HoldsEveryGroupNodeAsync(actorId, parentGroupId);
        }

        private async Task<EscalationCheck> HoldsEveryGroupNodeAsync(int actorId, int groupId, bool skipPremium = false)
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
                foreach (var grant in skipPremium && group.IsPremiumTier ? new List<Models.PermissionGrant>() : group.Grants ?? new List<Models.PermissionGrant>())
                {
                    // A deny only takes something away, so handing it out is never an escalation.
                    if (grant.Value && (grant.ExpiresAt == null || grant.ExpiresAt > now))
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

        /// <summary>The Weight of the actor's highest active group; no group means no rank.</summary>
        private async Task<int> HighestRankAsync(int actorId)
        {
            var now = DateTime.UtcNow;
            var weights = (await _memberships.GetByUserAsync(actorId))
                .Where(m => m.PermissionGroup != null && (m.ExpiresAt == null || m.ExpiresAt > now))
                .Select(m => m.PermissionGroup.Weight)
                .ToList();
            return weights.Count == 0 ? int.MinValue : weights.Max();
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
