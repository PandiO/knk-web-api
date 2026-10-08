using knkwebapi_v2.Services.Roads;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Roads;

/// <summary>Road navigation Phase 1: union-find components (DESIGN §5.8).</summary>
public class RoadComponentsTests
{
    [Fact]
    public void Compute_GroupsConnectedNodesUnderTheSmallestId()
    {
        var components = RoadComponents.Compute(new[] { 10, 4, 7, 12, 3 }, new[] { (10, 4), (7, 10), (12, 3) });

        Assert.Equal(4, components[10]);
        Assert.Equal(4, components[4]);
        Assert.Equal(4, components[7]);
        Assert.Equal(3, components[12]);
        Assert.Equal(3, components[3]);
    }

    [Fact]
    public void Compute_AnIsolatedNodeIsItsOwnComponent()
    {
        var components = RoadComponents.Compute(new[] { 5, 6 }, System.Array.Empty<(int, int)>());

        Assert.Equal(5, components[5]);
        Assert.Equal(6, components[6]);
    }

    [Fact]
    public void Compute_IgnoresEdgesToUnknownNodes()
    {
        var components = RoadComponents.Compute(new[] { 1, 2 }, new[] { (1, 99), (2, 1) });

        Assert.Equal(2, components.Count);
        Assert.Equal(1, components[2]);
    }

    [Fact]
    public void Compute_LongChainsAndCyclesCollapseToOne()
    {
        var nodes = Enumerable.Range(1, 1000).ToArray();
        var edges = nodes.Skip(1).Select(n => (n, n - 1)).Append((1000, 1)).ToArray();

        var components = RoadComponents.Compute(nodes, edges);

        Assert.All(nodes, n => Assert.Equal(1, components[n]));
    }
}
