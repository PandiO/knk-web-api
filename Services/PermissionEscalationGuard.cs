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
    /// <para>Dashboard access model (PandiO/knk-workspace#280, DESIGN.md § 4.4): nodes on
    /// <see cref="PermissionEscalationGuard.OwnerGrantOnly"/> (bulk delete) and every wildcard that covers them
    /// (<c>knk.admin.*</c>, <c>knk.admin.delete.*</c>, <c>knk.*</c>) are handed out only by <c>*</c>, also through a group
    /// that grants them. Groups ranked at or above the actor's own highest group can't be changed by them either:
    /// not their grants, not their settings, not deleted (developer 2026-11-08: Staff makes no group changes above
    /// its rank).</para>
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

        /// <summary>May <paramref name="actorId"/> create, edit or delete a group? The group as it is
        /// (<paramref name="groupId"/>, null for a new one) and as it will be (<paramref name="newWeight"/>, null when
        /// deleting) must both rank below the actor's own highest group.</summary>
        Task<EscalationCheck> CanChangeGroupAsync(int actorId, int? groupId, int? newWeight);
    }

    public class PermissionEscalationGuard : IPermissionEscalationGuard
    {
        public const string Wildcard = "*";
        public const string NotHeldMessage = "You can't grant a permission you don't have.";
        public const string SelfMessage = "You can't change your own permissions.";
        public const string GroupNotHeldMessage = "You can't hand out a group with permissions you don't have.";
        public const string RankMessage = "You can only hand out or remove groups ranked below your own.";
        public const string GroupRankMessage = "You can only change groups ranked below your own.";
        public const string OwnerOnlyMessage = "Only the owner can hand out bulk delete.";

        /// <summary>
        /// Nodes only <c>*</c> may grant, deny or revoke (decision 4 of #280), directly, through a wildcard that covers
        /// them or through a group that grants either. Denies of a covered node are owner-only too: a deny of
        /// <c>knk.admin.*</c> would also take bulk delete away.
        /// </summary>
        public static readonly IReadOnlyList<string> OwnerGrantOnly = new[] { StaffPermissions.DeleteBulk };

        /// <summary>
        /// Whether <paramref name="node"/> is, or as a wildcard covers, an <see cref="OwnerGrantOnly"/> node: the same
        /// matching as PermissionResolutionService (a trailing <c>*</c> matches every node starting with what comes
        /// before it, and the bare node without its <c>.*</c>).
        /// </summary>
        public static bool CoversOwnerGrantOnly(string? node)
        {
            if (string.IsNullOrWhiteSpace(node)) return false;
            var grant = node.Trim();
            return OwnerGrantOnly.Any(owned => Covers(grant, owned));
        }

        private static bool Covers(string grant, string node)
        {
            if (string.Equals(grant, node, StringComparison.Ordinal)) return true;
            if (!grant.EndsWith('*')) return false;
            var prefix = grant[..^1];
            if (prefix.Length == 0) return true;
            var bare = prefix.EndsWith('.') ? prefix[..^1] : prefix;
            return node.StartsWith(prefix, StringComparison.Ordinal) || string.Equals(node, bare, StringComparison.Ordinal);
        }

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

            if (holdsWildcard)
            {
                return EscalationCheck.Ok;
            }
            // A group's grants are changed only by someone ranked above it, like its members (#280: Staff
            // makes no group changes above its rank). Holder ids are shared by users and groups.
            var targetGroup = await _groups.GetByIdAsync(targetHolderId);
            if (targetGroup != null && targetGroup.Weight >= await HighestRankAsync(actorId))
            {
                return EscalationCheck.Refused(GroupRankMessage);
            }

            // A deny (value=false) needs the node too: otherwise staff could strip a node from
            // someone above them. "*" can only come from someone holding "*".
            if (!await HoldsAsync(actorId, node.Trim()))
            {
                return EscalationCheck.Refused(NotHeldMessage);
            }
            // Bulk delete (and any wildcard covering it) is the owner's to hand out, even by someone who holds it
            // (#280 decision 4).
            return CoversOwnerGrantOnly(node) ? EscalationCheck.Refused(OwnerOnlyMessage) : EscalationCheck.Ok;
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

        public async Task<EscalationCheck> CanChangeGroupAsync(int actorId, int? groupId, int? newWeight)
        {
            if (await HoldsAsync(actorId, Wildcard))
            {
                return EscalationCheck.Ok;
            }
            var rank = await HighestRankAsync(actorId);
            if (groupId is int id && await _groups.GetByIdAsync(id) is { } group && group.Weight >= rank)
            {
                return EscalationCheck.Refused(GroupRankMessage);
            }
            return newWeight is int weight && weight >= rank
                ? EscalationCheck.Refused(GroupRankMessage)
                : EscalationCheck.Ok;
        }

        private async Task<EscalationCheck> HoldsEveryGroupNodeAsync(int actorId, int groupId, bool skipPremium = false)
        {
            var now = DateTime.UtcNow;
            var nodes = new HashSet<string>(StringComparer.Ordinal);
            var carriesOwnerOnlyNode = false;
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
                    // A deny only takes something away, so handing it out is never an escalation.
                    if (!grant.Value || (grant.ExpiresAt != null && grant.ExpiresAt <= now))
                    {
                        continue;
                    }
                    // A group that carries bulk delete is the owner's to hand out, premium or not (#280).
                    carriesOwnerOnlyNode |= CoversOwnerGrantOnly(grant.Node);
                    if (!(skipPremium && group.IsPremiumTier))
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
            return carriesOwnerOnlyNode ? EscalationCheck.Refused(OwnerOnlyMessage) : EscalationCheck.Ok;
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
