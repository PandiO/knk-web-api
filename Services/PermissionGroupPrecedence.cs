using System.Collections.Generic;
using System.Linq;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services;

/// <summary>
/// Which of a player's PermissionGroups wins when several carry a Game Settings override
/// (KNG-52, docs/specs/game-settings/DESIGN.md §3.8). Since 2026-10-09 (developer decision on
/// D13) this is the order teleport fees and cooldowns use (KNG-41, <see cref="TeleportGroupPolicy.Chain"/>)
/// and permission grants resolve in: the groups from the highest Weight down (ties by lower id),
/// each followed by its parent chain before the next group; a group reached twice keeps its first
/// position. It works on ParentGroupId through a lookup, so the groups need no ParentGroup loaded.
/// </summary>
public static class PermissionGroupPrecedence
{
    /// <summary><paramref name="groups"/> and their parents in precedence order (first wins). Parents
    /// are looked up in <paramref name="byId"/>; a missing parent ends a chain, a cycle is cut where it repeats.</summary>
    public static List<PermissionGroup> Order(IEnumerable<PermissionGroup> groups, IReadOnlyDictionary<int, PermissionGroup> byId)
    {
        var order = new List<PermissionGroup>();
        var seen = new HashSet<int>();
        foreach (var group in groups.GroupBy(g => g.Id).Select(g => g.First())
                     .OrderByDescending(g => g.Weight).ThenBy(g => g.Id))
        {
            var current = group;
            var walked = new HashSet<int>();
            while (current != null && walked.Add(current.Id))
            {
                if (seen.Add(current.Id))
                {
                    order.Add(current);
                }
                current = current.ParentGroupId.HasValue && byId.TryGetValue(current.ParentGroupId.Value, out var parent)
                    ? parent
                    : null;
            }
        }
        return order;
    }
}
