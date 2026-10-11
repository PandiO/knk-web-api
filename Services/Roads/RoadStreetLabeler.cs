using System;
using System.Collections.Generic;
using System.Linq;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Services.Roads;

/// <summary>
/// Street labels from the Structures along a road (docs/specs/navigation/DESIGN.md §5.11, run in
/// the API per plan D6). Pure: the caller loads the tile's edges, the ring of neighbouring edges
/// for continuation, and the structures near the tile, and writes the result back.
/// <list type="number">
/// <item>Every structure votes for its street on the nearest edge within 16 blocks and |Δy| ≤ 4,
/// weight 1/distance.</item>
/// <item>An editable edge gets the street with ≥ 60% of its vote weight; otherwise it is a conflict.</item>
/// <item>An unlabelled editable edge inherits the street of the labelled edge it continues most
/// straightly at either end (&lt; 35° bearing change, same road class, same level), until nothing changes.</item>
/// <item>Manual labels are never changed; edges outside <c>editableEdgeIds</c> only provide context.</item>
/// </list>
/// </summary>
public static class RoadStreetLabeler
{
    public const double VoteRadius = 16.0;
    public const int VoteMaxDeltaY = 4;
    public const double MajorityShare = 0.6;
    public const double ContinuationMaxBearingChange = 35.0;
    public const int ContinuationMaxDeltaY = 4;

    public sealed class Edge
    {
        public int Id { get; init; }
        public int FromNodeId { get; init; }
        public int ToNodeId { get; init; }
        public int[][] Geometry { get; init; } = Array.Empty<int[]>();
        /// <summary>The road class of the edge's profile; null when unmatched.</summary>
        public RoadClass? RoadClass { get; init; }
        public int? StreetId { get; init; }
        public RoadStreetSource StreetSource { get; init; }
    }

    public sealed class Structure
    {
        public int Id { get; init; }
        public int StreetId { get; init; }
        public double X { get; init; }
        public double Y { get; init; }
        public double Z { get; init; }
    }

    public sealed class Result
    {
        /// <summary>The street of every editable edge after labelling (null = unlabelled); Manual
        /// edges keep theirs.</summary>
        public Dictionary<int, int?> Labels { get; } = new();

        /// <summary>One line per edge whose votes reached no 60% majority.</summary>
        public List<string> Conflicts { get; } = new();
    }

    public static Result Label(IReadOnlyList<Edge> edges, IReadOnlyList<Structure> structures, IReadOnlySet<int> editableEdgeIds)
    {
        var result = new Result();
        var byId = edges.ToDictionary(e => e.Id);

        // Current labels: Manual everywhere, existing labels on the fixed (ring) edges, nothing on
        // the editable Inferred/None edges - they are recomputed from scratch.
        var labels = new Dictionary<int, int?>();
        foreach (var edge in edges)
        {
            labels[edge.Id] = edge.StreetSource == RoadStreetSource.Manual || !editableEdgeIds.Contains(edge.Id)
                ? edge.StreetId
                : null;
        }

        // 1. Votes.
        var votes = new Dictionary<int, Dictionary<int, double>>();
        foreach (var structure in structures)
        {
            Edge? nearest = null;
            var nearestDistance = double.PositiveInfinity;
            foreach (var edge in edges)
            {
                var closest = RoadGeometry.ClosestPointOnPolyline(structure.X, structure.Y, structure.Z, edge.Geometry);
                if (closest.distance > VoteRadius || Math.Abs(closest.y - structure.Y) > VoteMaxDeltaY)
                {
                    continue;
                }
                if (closest.distance < nearestDistance)
                {
                    nearestDistance = closest.distance;
                    nearest = edge;
                }
            }
            if (nearest == null)
            {
                continue;
            }
            var weight = 1.0 / Math.Max(nearestDistance, 0.5);
            if (!votes.TryGetValue(nearest.Id, out var perStreet))
            {
                votes[nearest.Id] = perStreet = new Dictionary<int, double>();
            }
            perStreet[structure.StreetId] = perStreet.GetValueOrDefault(structure.StreetId) + weight;
        }

        // 2. Majority.
        foreach (var (edgeId, perStreet) in votes.OrderBy(v => v.Key))
        {
            var edge = byId[edgeId];
            if (!editableEdgeIds.Contains(edgeId) || edge.StreetSource == RoadStreetSource.Manual)
            {
                continue;
            }
            var total = perStreet.Values.Sum();
            var best = perStreet.OrderByDescending(v => v.Value).ThenBy(v => v.Key).First();
            if (best.Value >= MajorityShare * total)
            {
                labels[edgeId] = best.Key;
            }
            else
            {
                var shares = perStreet.OrderByDescending(v => v.Value).ThenBy(v => v.Key)
                    .Select(v => $"street {v.Key} {Math.Round(100.0 * v.Value / total)}%");
                result.Conflicts.Add($"Edge {edgeId}: no street has a {MajorityShare:P0} majority ({string.Join(", ", shares)})");
            }
        }

        // 3. Continuation through junctions, to a fixed point.
        var byNode = new Dictionary<int, List<Edge>>();
        foreach (var edge in edges)
        {
            Add(byNode, edge.FromNodeId, edge);
            Add(byNode, edge.ToNodeId, edge);
        }

        bool changed;
        do
        {
            changed = false;
            foreach (var edge in edges.OrderBy(e => e.Id))
            {
                if (!editableEdgeIds.Contains(edge.Id) || labels[edge.Id] != null || edge.Geometry.Length < 2)
                {
                    continue;
                }

                int? inherited = null;
                var bestChange = ContinuationMaxBearingChange;
                foreach (var nodeId in new[] { edge.FromNodeId, edge.ToNodeId })
                {
                    var leaving = LeavingBearing(edge, nodeId);
                    var y = EndY(edge, nodeId);
                    foreach (var other in byNode[nodeId])
                    {
                        if (other.Id == edge.Id || labels[other.Id] == null || other.Geometry.Length < 2)
                        {
                            continue;
                        }
                        if (other.RoadClass != edge.RoadClass || Math.Abs(EndY(other, nodeId) - y) > ContinuationMaxDeltaY)
                        {
                            continue;
                        }
                        // The other edge arrives at the node travelling opposite to its leaving bearing.
                        var arriving = (LeavingBearing(other, nodeId) + 180.0) % 360.0;
                        var change = RoadGeometry.BearingChange(arriving, leaving);
                        if (change < bestChange)
                        {
                            bestChange = change;
                            inherited = labels[other.Id];
                        }
                    }
                }

                if (inherited != null)
                {
                    labels[edge.Id] = inherited;
                    changed = true;
                }
            }
        } while (changed);

        foreach (var id in editableEdgeIds)
        {
            if (labels.TryGetValue(id, out var street))
            {
                result.Labels[id] = street;
            }
        }
        return result;
    }

    /// <summary>The edges an unlabelled edge would inherit from when a label is propagated along the
    /// road from <paramref name="startEdgeId"/> (DESIGN §5.11 rule 3 applied outward): every edge
    /// reachable through straight continuations, stopping at edges that carry a different Manual
    /// street. Used by the edge update's <c>propagate</c>.</summary>
    public static List<int> Propagate(IReadOnlyList<Edge> edges, int startEdgeId, int streetId)
    {
        var byId = edges.ToDictionary(e => e.Id);
        var byNode = new Dictionary<int, List<Edge>>();
        foreach (var edge in edges)
        {
            Add(byNode, edge.FromNodeId, edge);
            Add(byNode, edge.ToNodeId, edge);
        }

        var visited = new HashSet<int> { startEdgeId };
        var order = new List<int>();
        var queue = new Queue<int>();
        queue.Enqueue(startEdgeId);
        while (queue.Count > 0)
        {
            var current = byId[queue.Dequeue()];
            if (current.Geometry.Length < 2)
            {
                continue;
            }
            foreach (var nodeId in new[] { current.FromNodeId, current.ToNodeId })
            {
                var arriving = (LeavingBearing(current, nodeId) + 180.0) % 360.0;
                var y = EndY(current, nodeId);
                Edge? straightest = null;
                var bestChange = ContinuationMaxBearingChange;
                foreach (var other in byNode[nodeId])
                {
                    if (visited.Contains(other.Id) || other.Geometry.Length < 2)
                    {
                        continue;
                    }
                    if (other.RoadClass != current.RoadClass || Math.Abs(EndY(other, nodeId) - y) > ContinuationMaxDeltaY)
                    {
                        continue;
                    }
                    var change = RoadGeometry.BearingChange(arriving, LeavingBearing(other, nodeId));
                    if (change < bestChange)
                    {
                        bestChange = change;
                        straightest = other;
                    }
                }
                if (straightest == null)
                {
                    continue;
                }
                if (straightest.StreetSource == RoadStreetSource.Manual && straightest.StreetId != streetId)
                {
                    continue;
                }
                visited.Add(straightest.Id);
                order.Add(straightest.Id);
                queue.Enqueue(straightest.Id);
            }
        }
        return order;
    }

    private static void Add(Dictionary<int, List<Edge>> byNode, int nodeId, Edge edge)
    {
        if (!byNode.TryGetValue(nodeId, out var list))
        {
            byNode[nodeId] = list = new List<Edge>();
        }
        list.Add(edge);
    }

    private static double LeavingBearing(Edge edge, int nodeId) =>
        nodeId == edge.FromNodeId ? RoadGeometry.BearingAtStart(edge.Geometry) : RoadGeometry.BearingAtEnd(edge.Geometry);

    private static int EndY(Edge edge, int nodeId) =>
        nodeId == edge.FromNodeId ? edge.Geometry[0][1] : edge.Geometry[^1][1];
}
