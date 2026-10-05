using System.Collections.Generic;
using System.Linq;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services;

/// <summary>
/// Which of a player's PermissionGroups wins when several carry a Game Settings override
/// (KNG-52, docs/specs/game-settings/DESIGN.md §3.8, developer decision 2026-10-05: "hierarchy
/// first, weight second"): the group deeper in the parent hierarchy first - so a child beats the
/// group it inherits from - then the higher Weight, then the lower id.
/// </summary>
public static class PermissionGroupPrecedence
{
    /// <summary>How many parents a group has, following ParentGroupId through <paramref name="byId"/>;
    /// a missing parent ends the chain, a cycle is cut where it repeats.</summary>
    public static int Depth(PermissionGroup group, IReadOnlyDictionary<int, PermissionGroup> byId)
    {
        var seen = new HashSet<int> { group.Id };
        var depth = 0;
        var parentId = group.ParentGroupId;
        while (parentId.HasValue && byId.TryGetValue(parentId.Value, out var parent) && seen.Add(parent.Id))
        {
            depth++;
            parentId = parent.ParentGroupId;
        }
        return depth;
    }

    /// <summary><paramref name="groups"/> in precedence order (first wins).</summary>
    public static List<PermissionGroup> Order(IEnumerable<PermissionGroup> groups, IReadOnlyDictionary<int, PermissionGroup> byId)
    {
        return groups
            .GroupBy(g => g.Id).Select(g => g.First())
            .OrderByDescending(g => Depth(g, byId))
            .ThenByDescending(g => g.Weight)
            .ThenBy(g => g.Id)
            .ToList();
    }
}
