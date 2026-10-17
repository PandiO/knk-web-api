using System;
using System.Collections.Generic;
using System.Linq;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services;

/// <summary>
/// Which of a player's PermissionGroups wins when several apply: Game Settings overrides (KNG-52,
/// docs/specs/game-settings/DESIGN.md §3.8), teleport fees and cooldowns (KNG-41,
/// <see cref="TeleportGroupPolicy.Chain"/>) and permission grants (PermissionResolutionService) all use this order.
/// <para>
/// Developer decision 2026-10-12 (hub DESIGN §16.7, decision 13, option (i)): the hierarchy comes first, then weight.
/// A held group that another held group inherits from (directly or further up) is never a starting point: it is
/// reached through that group's chain, after it. The remaining groups start in Weight order (highest first, ties by
/// lower id), each followed by its parent chain before the next; a group reached twice keeps its first position.
/// So a chain is ranked by the group the player actually holds, and a group always comes before its parents.
/// </para>
/// </summary>
public static class PermissionGroupPrecedence
{
    /// <summary><paramref name="groups"/> and their parents in precedence order (first wins). Parents
    /// are looked up in <paramref name="byId"/>; a missing parent ends a chain, a cycle is cut where it repeats.</summary>
    public static List<PermissionGroup> Order(IEnumerable<PermissionGroup> groups, IReadOnlyDictionary<int, PermissionGroup> byId)
    {
        return Chain(groups, g => g.ParentGroupId.HasValue && byId.TryGetValue(g.ParentGroupId.Value, out var parent)
            ? parent
            : null);
    }

    /// <summary>
    /// <paramref name="held"/> and their parents in precedence order, with <paramref name="parentOf"/> giving a group's
    /// parent (null at the top). A cycle is cut where it repeats.
    /// </summary>
    public static List<PermissionGroup> Chain(IEnumerable<PermissionGroup> held, Func<PermissionGroup, PermissionGroup?> parentOf)
    {
        var distinct = held.Where(g => g != null).GroupBy(g => g.Id).Select(g => g.First()).ToList();
        var inherited = new HashSet<int>();
        foreach (var group in distinct)
        {
            var walked = new HashSet<int> { group.Id };
            for (var parent = parentOf(group); parent != null && walked.Add(parent.Id); parent = parentOf(parent))
            {
                inherited.Add(parent.Id);
            }
        }

        var byWeight = distinct.OrderByDescending(g => g.Weight).ThenBy(g => g.Id).ToList();
        // Held groups inherited by another held group come later, through that group's chain; in a parent cycle every
        // group is "inherited", so they then start in weight order as before.
        var starts = byWeight.Where(g => !inherited.Contains(g.Id)).Concat(byWeight.Where(g => inherited.Contains(g.Id)));

        var order = new List<PermissionGroup>();
        var seen = new HashSet<int>();
        foreach (var group in starts)
        {
            var current = group;
            var walked = new HashSet<int>();
            while (current != null && walked.Add(current.Id))
            {
                if (seen.Add(current.Id))
                {
                    order.Add(current);
                }
                current = parentOf(current);
            }
        }
        return order;
    }
}
