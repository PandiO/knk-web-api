using System.Collections.Generic;

namespace knkwebapi_v2.Services.Roads;

/// <summary>
/// Connected components of the road graph (docs/specs/navigation/DESIGN.md §5.8): union-find
/// over nodes and edges. A component's id is the smallest node id in it, so ids are stable
/// across rebuilds as long as that node survives. Pure; the service writes the changed ids back.
/// </summary>
public static class RoadComponents
{
    /// <summary>nodeId → componentId for every node in <paramref name="nodeIds"/>. Edges whose
    /// ends aren't in the node set are ignored.</summary>
    public static Dictionary<int, int> Compute(IEnumerable<int> nodeIds, IEnumerable<(int fromNodeId, int toNodeId)> edges)
    {
        var parent = new Dictionary<int, int>();
        foreach (var id in nodeIds)
        {
            parent[id] = id;
        }

        foreach (var (from, to) in edges)
        {
            if (!parent.ContainsKey(from) || !parent.ContainsKey(to))
            {
                continue;
            }
            var rootFrom = Find(parent, from);
            var rootTo = Find(parent, to);
            if (rootFrom == rootTo)
            {
                continue;
            }
            // Keep the smaller id as the root, so the root is the component id.
            if (rootFrom < rootTo)
            {
                parent[rootTo] = rootFrom;
            }
            else
            {
                parent[rootFrom] = rootTo;
            }
        }

        var result = new Dictionary<int, int>(parent.Count);
        foreach (var id in parent.Keys)
        {
            result[id] = Find(parent, id);
        }
        return result;
    }

    private static int Find(Dictionary<int, int> parent, int id)
    {
        var root = id;
        while (parent[root] != root)
        {
            root = parent[root];
        }
        // Path compression.
        while (parent[id] != root)
        {
            var next = parent[id];
            parent[id] = root;
            id = next;
        }
        return root;
    }
}
