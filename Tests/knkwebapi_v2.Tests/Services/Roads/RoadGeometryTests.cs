using knkwebapi_v2.Services.Roads;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Roads;

/// <summary>Road navigation Phase 1: the pure geometry helpers behind validation, stitching and labelling.</summary>
public class RoadGeometryTests
{
    private static int[] P(int x, int y, int z) => new[] { x, y, z };

    [Fact]
    public void PolylineLength_SumsTheSegments()
    {
        var length = RoadGeometry.PolylineLength(new[] { P(0, 64, 0), P(3, 64, 4), P(3, 66, 4), P(3, 66, 10) });

        Assert.Equal(5 + 2 + 6, length, 6);
        Assert.Equal(0, RoadGeometry.PolylineLength(new[] { P(1, 1, 1) }));
    }

    [Fact]
    public void ClosestPointOnSegment_ClampsToTheEnds()
    {
        var mid = RoadGeometry.ClosestPointOnSegment(5, 64, 3, P(0, 64, 0), P(10, 64, 0));
        Assert.Equal((5.0, 64.0, 0.0, 3.0), (mid.x, mid.y, mid.z, mid.distance));

        var beyond = RoadGeometry.ClosestPointOnSegment(14, 64, 3, P(0, 64, 0), P(10, 64, 0));
        Assert.Equal((10.0, 64.0, 0.0, 5.0), (beyond.x, beyond.y, beyond.z, beyond.distance));

        var degenerate = RoadGeometry.ClosestPointOnSegment(3, 64, 4, P(0, 64, 0), P(0, 64, 0));
        Assert.Equal(5.0, degenerate.distance);
    }

    [Fact]
    public void DistanceToPolyline_TakesTheNearestSegment()
    {
        var polyline = new[] { P(0, 64, 0), P(10, 64, 0), P(10, 64, 10) };

        Assert.Equal(2.0, RoadGeometry.DistanceToPolyline(12, 64, 7, polyline), 6);
        Assert.Equal(1.0, RoadGeometry.DistanceToPolyline(4, 65, 0, polyline), 6);
        Assert.Equal(double.PositiveInfinity, RoadGeometry.DistanceToPolyline(0, 0, 0, System.Array.Empty<int[]>()));
    }

    [Fact]
    public void BoundingBox_CoversEveryPoint()
    {
        var box = RoadGeometry.BoundingBox(new[] { P(3, 70, -2), P(-5, 64, 9), P(0, 66, 0) });

        Assert.Equal((-5, 64, -2, 3, 70, 9), box);
    }

    [Theory]
    [InlineData(0, 0, 10, 0, 0.0)]
    [InlineData(0, 0, 0, 10, 90.0)]
    [InlineData(0, 0, -10, 0, 180.0)]
    [InlineData(0, 0, 0, -10, 270.0)]
    [InlineData(0, 0, 10, 10, 45.0)]
    public void Bearing_IsHorizontalAndClockwiseFromPlusX(int fromX, int fromZ, int toX, int toZ, double expected)
    {
        Assert.Equal(expected, RoadGeometry.Bearing(fromX, fromZ, toX, toZ), 6);
    }

    [Fact]
    public void Bearing_IgnoresHeightAndIsNaNForAVerticalStep()
    {
        Assert.Equal(0.0, RoadGeometry.Bearing(P(0, 64, 0), P(10, 90, 0)), 6);
        Assert.True(double.IsNaN(RoadGeometry.Bearing(P(0, 64, 0), P(0, 70, 0))));
    }

    [Theory]
    [InlineData(10, 350, 20)]
    [InlineData(350, 10, 20)]
    [InlineData(0, 180, 180)]
    [InlineData(90, 90, 0)]
    [InlineData(double.NaN, 90, 180)]
    public void BearingChange_IsTheSmallerAngle(double a, double b, double expected)
    {
        Assert.Equal(expected, RoadGeometry.BearingChange(a, b), 6);
    }

    [Fact]
    public void BearingAtEnds_SkipVerticalSegments()
    {
        var polyline = new[] { P(0, 64, 0), P(0, 66, 0), P(10, 66, 0), P(10, 66, 10) };

        Assert.Equal(0.0, RoadGeometry.BearingAtStart(polyline), 6);   // leaves along +x
        Assert.Equal(270.0, RoadGeometry.BearingAtEnd(polyline), 6);   // leaves the end back along -z
    }

    [Fact]
    public void IsWellFormed_RequiresTriples()
    {
        Assert.True(RoadGeometry.IsWellFormed(new[] { P(0, 0, 0), P(1, 1, 1) }));
        Assert.False(RoadGeometry.IsWellFormed(new[] { new[] { 0, 0 } }));
        Assert.False(RoadGeometry.IsWellFormed(null));
    }

    [Fact]
    public void Reversed_DoesNotTouchTheInput()
    {
        var input = new[] { P(0, 0, 0), P(1, 1, 1), P(2, 2, 2) };

        var reversed = RoadGeometry.Reversed(input);

        Assert.Equal(new[] { 2, 2, 2 }, reversed[0]);
        Assert.Equal(new[] { 0, 0, 0 }, input[0]);
    }
}
