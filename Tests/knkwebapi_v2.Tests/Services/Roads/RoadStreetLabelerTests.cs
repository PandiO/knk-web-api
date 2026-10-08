using knkwebapi_v2.Enums;
using knkwebapi_v2.Services.Roads;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Roads;

/// <summary>Road navigation Phase 1: street labels from Structures (DESIGN §5.11, plan D6).</summary>
public class RoadStreetLabelerTests
{
    private static int[] P(int x, int y, int z) => new[] { x, y, z };

    private static RoadStreetLabeler.Edge Edge(int id, int from, int to, int[][] geometry, RoadClass? roadClass = RoadClass.Road,
        int? streetId = null, RoadStreetSource source = RoadStreetSource.None) => new()
    {
        Id = id, FromNodeId = from, ToNodeId = to, Geometry = geometry, RoadClass = roadClass, StreetId = streetId, StreetSource = source
    };

    private static RoadStreetLabeler.Structure House(int id, int streetId, double x, double y, double z) => new()
    {
        Id = id, StreetId = streetId, X = x, Y = y, Z = z
    };

    private static HashSet<int> All(params int[] ids) => ids.ToHashSet();

    [Fact]
    public void Majority_LabelsTheEdgeWithTheStreetHoldingSixtyPercent()
    {
        var edges = new[] { Edge(1, 1, 2, new[] { P(0, 64, 0), P(40, 64, 0) }) };
        var houses = new[]
        {
            House(1, 7, 5, 64, 3), House(2, 7, 15, 64, 3), House(3, 7, 25, 64, 3), // three votes at distance 3
            House(4, 8, 35, 64, 3)                                                   // one for another street
        };

        var result = RoadStreetLabeler.Label(edges, houses, All(1));

        Assert.Equal(7, result.Labels[1]);
        Assert.Empty(result.Conflicts);
    }

    [Fact]
    public void Tie_IsAConflictAndLeavesTheEdgeUnlabelled()
    {
        var edges = new[] { Edge(1, 1, 2, new[] { P(0, 64, 0), P(40, 64, 0) }) };
        var houses = new[] { House(1, 7, 10, 64, 3), House(2, 8, 30, 64, 3) };

        var result = RoadStreetLabeler.Label(edges, houses, All(1));

        Assert.Null(result.Labels[1]);
        var conflict = Assert.Single(result.Conflicts);
        Assert.Contains("Edge 1", conflict);
        Assert.Contains("street 7 50%", conflict);
    }

    [Fact]
    public void Votes_WeighByDistanceAndGoToTheNearestEdgeOnly()
    {
        // A house 2 blocks from edge 1 and 12 from edge 2 votes only on edge 1, with weight 1/2.
        var edges = new[]
        {
            Edge(1, 1, 2, new[] { P(0, 64, 0), P(40, 64, 0) }),
            Edge(2, 3, 4, new[] { P(0, 64, 14), P(40, 64, 14) })
        };
        var houses = new[]
        {
            House(1, 7, 20, 64, 2),   // 2 from edge 1 → weight 0.5 for street 7
            House(2, 8, 20, 64, 5),   // 5 from edge 1 → weight 0.2 for street 8
        };

        var result = RoadStreetLabeler.Label(edges, houses, All(1, 2));

        Assert.Equal(7, result.Labels[1]);
        Assert.Null(result.Labels[2]);
    }

    [Fact]
    public void Votes_IgnoreStructuresTooFarOrOnAnotherLevel()
    {
        var edges = new[] { Edge(1, 1, 2, new[] { P(0, 64, 0), P(40, 64, 0) }) };
        var houses = new[]
        {
            House(1, 7, 20, 64, 17),  // 17 blocks away: outside the 16-block radius
            House(2, 8, 20, 70, 2),   // 6 above the road: another level (|Δy| > 4)
        };

        var result = RoadStreetLabeler.Label(edges, houses, All(1));

        Assert.Null(result.Labels[1]);
        Assert.Empty(result.Conflicts);
    }

    [Fact]
    public void Continuation_FollowsAStraightJunctionButNotANinetyDegreeTurn()
    {
        // Node 2 is a T-junction: edge 1 (west→2) continues straight into edge 2 (2→east);
        // edge 3 leaves the junction to the south.
        var edges = new[]
        {
            Edge(1, 1, 2, new[] { P(0, 64, 0), P(40, 64, 0) }),
            Edge(2, 2, 3, new[] { P(40, 64, 0), P(80, 64, 0) }),
            Edge(3, 2, 4, new[] { P(40, 64, 0), P(40, 64, 40) })
        };
        var houses = new[] { House(1, 7, 20, 64, 3) };

        var result = RoadStreetLabeler.Label(edges, houses, All(1, 2, 3));

        Assert.Equal(7, result.Labels[1]);
        Assert.Equal(7, result.Labels[2]);
        Assert.Null(result.Labels[3]);
    }

    [Fact]
    public void Continuation_IteratesToAFixedPointAlongTheRoad()
    {
        var edges = new[]
        {
            Edge(1, 1, 2, new[] { P(0, 64, 0), P(40, 64, 0) }),
            Edge(2, 2, 3, new[] { P(40, 64, 0), P(80, 64, 2) }),
            Edge(3, 3, 4, new[] { P(80, 64, 2), P(120, 64, 6) }),
            Edge(4, 4, 5, new[] { P(120, 64, 6), P(160, 64, 6) })
        };
        var houses = new[] { House(1, 7, 20, 64, 3) };

        var result = RoadStreetLabeler.Label(edges, houses, All(1, 2, 3, 4));

        Assert.All(new[] { 1, 2, 3, 4 }, id => Assert.Equal(7, result.Labels[id]));
    }

    [Fact]
    public void Continuation_RequiresTheSameRoadClass()
    {
        var edges = new[]
        {
            Edge(1, 1, 2, new[] { P(0, 64, 0), P(40, 64, 0) }, RoadClass.Main),
            Edge(2, 2, 3, new[] { P(40, 64, 0), P(80, 64, 0) }, RoadClass.Path)
        };
        var houses = new[] { House(1, 7, 20, 64, 3) };

        var result = RoadStreetLabeler.Label(edges, houses, All(1, 2));

        Assert.Equal(7, result.Labels[1]);
        Assert.Null(result.Labels[2]);
    }

    [Fact]
    public void Manual_LabelsAreNeverChangedButStillContinue()
    {
        var edges = new[]
        {
            Edge(1, 1, 2, new[] { P(0, 64, 0), P(40, 64, 0) }, streetId: 9, source: RoadStreetSource.Manual),
            Edge(2, 2, 3, new[] { P(40, 64, 0), P(80, 64, 0) })
        };
        var houses = new[] { House(1, 7, 20, 64, 3), House(2, 7, 30, 64, 3) }; // votes for 7 on the Manual edge

        var result = RoadStreetLabeler.Label(edges, houses, All(1, 2));

        Assert.Equal(9, result.Labels[1]);
        Assert.Equal(9, result.Labels[2]);
        Assert.Empty(result.Conflicts);
    }

    [Fact]
    public void FixedRingEdges_ProvideContinuationButGetNoLabel()
    {
        // Edge 1 belongs to the neighbouring tile (not editable) and is already labelled; the
        // tile's edge 2 continues it. A house near the ring edge doesn't change the ring edge.
        var edges = new[]
        {
            Edge(1, 1, 2, new[] { P(0, 64, 0), P(40, 64, 0) }, streetId: 5, source: RoadStreetSource.Inferred),
            Edge(2, 2, 3, new[] { P(40, 64, 0), P(80, 64, 0) })
        };
        var houses = new[] { House(1, 7, 20, 64, 3) };

        var result = RoadStreetLabeler.Label(edges, houses, All(2));

        Assert.Equal(5, result.Labels[2]);
        Assert.False(result.Labels.ContainsKey(1));
    }

    [Fact]
    public void Relabelling_DropsAnInferredLabelWhoseStructuresAreGone()
    {
        var edges = new[] { Edge(1, 1, 2, new[] { P(0, 64, 0), P(40, 64, 0) }, streetId: 5, source: RoadStreetSource.Inferred) };

        var result = RoadStreetLabeler.Label(edges, System.Array.Empty<RoadStreetLabeler.Structure>(), All(1));

        Assert.Null(result.Labels[1]);
    }

    [Fact]
    public void Propagate_FollowsTheRoadAndStopsAtAnotherManualStreet()
    {
        var edges = new[]
        {
            Edge(1, 1, 2, new[] { P(0, 64, 0), P(40, 64, 0) }),
            Edge(2, 2, 3, new[] { P(40, 64, 0), P(80, 64, 0) }),
            Edge(3, 3, 4, new[] { P(80, 64, 0), P(120, 64, 0) }, streetId: 9, source: RoadStreetSource.Manual),
            Edge(4, 4, 5, new[] { P(120, 64, 0), P(160, 64, 0) }),
            Edge(5, 2, 6, new[] { P(40, 64, 0), P(40, 64, 40) }) // side street at node 2
        };

        var order = RoadStreetLabeler.Propagate(edges, 1, 7);

        Assert.Equal(new[] { 2 }, order);
    }

    [Fact]
    public void Propagate_RunsBothWaysFromTheStartEdge()
    {
        var edges = new[]
        {
            Edge(1, 1, 2, new[] { P(0, 64, 0), P(40, 64, 0) }),
            Edge(2, 2, 3, new[] { P(40, 64, 0), P(80, 64, 0) }),
            Edge(3, 3, 4, new[] { P(80, 64, 0), P(120, 64, 0) })
        };

        var order = RoadStreetLabeler.Propagate(edges, 2, 7);

        Assert.Equal(new[] { 1, 3 }, order.OrderBy(id => id));
    }
}
