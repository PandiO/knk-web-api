using System;
using System.Collections.Generic;

namespace knkwebapi_v2.Services.Roads;

/// <summary>
/// Pure 3D geometry over integer block positions ([x, y, z] triples), the shape of
/// RoadEdge.GeometryJson (docs/specs/navigation/DESIGN.md §3.6). No geometry helper existed in
/// the API before road navigation.
/// </summary>
public static class RoadGeometry
{
    public static double Distance(int[] a, int[] b) =>
        Distance(a[0], a[1], a[2], b[0], b[1], b[2]);

    public static double Distance(double x1, double y1, double z1, double x2, double y2, double z2)
    {
        double dx = x2 - x1, dy = y2 - y1, dz = z2 - z1;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>Sum of the segment lengths; 0 for fewer than two points.</summary>
    public static double PolylineLength(IReadOnlyList<int[]> geometry)
    {
        double length = 0;
        for (var i = 1; i < geometry.Count; i++)
        {
            length += Distance(geometry[i - 1], geometry[i]);
        }
        return length;
    }

    /// <summary>The point of segment a-b closest to p, and the distance to it.</summary>
    public static (double x, double y, double z, double distance) ClosestPointOnSegment(
        double px, double py, double pz, int[] a, int[] b)
    {
        double ax = a[0], ay = a[1], az = a[2];
        double dx = b[0] - ax, dy = b[1] - ay, dz = b[2] - az;
        var lengthSquared = dx * dx + dy * dy + dz * dz;
        var t = lengthSquared <= 0
            ? 0
            : Math.Clamp(((px - ax) * dx + (py - ay) * dy + (pz - az) * dz) / lengthSquared, 0, 1);
        double cx = ax + t * dx, cy = ay + t * dy, cz = az + t * dz;
        return (cx, cy, cz, Distance(px, py, pz, cx, cy, cz));
    }

    public static double PointSegmentDistance(double px, double py, double pz, int[] a, int[] b) =>
        ClosestPointOnSegment(px, py, pz, a, b).distance;

    /// <summary>The closest point of the polyline to p (a single point counts as a degenerate
    /// polyline); distance is +Inf for an empty polyline.</summary>
    public static (double x, double y, double z, double distance) ClosestPointOnPolyline(
        double px, double py, double pz, IReadOnlyList<int[]> geometry)
    {
        if (geometry.Count == 0)
        {
            return (px, py, pz, double.PositiveInfinity);
        }
        if (geometry.Count == 1)
        {
            var p = geometry[0];
            return (p[0], p[1], p[2], Distance(px, py, pz, p[0], p[1], p[2]));
        }

        (double x, double y, double z, double distance) best = (0, 0, 0, double.PositiveInfinity);
        for (var i = 1; i < geometry.Count; i++)
        {
            var candidate = ClosestPointOnSegment(px, py, pz, geometry[i - 1], geometry[i]);
            if (candidate.distance < best.distance)
            {
                best = candidate;
            }
        }
        return best;
    }

    public static double DistanceToPolyline(double px, double py, double pz, IReadOnlyList<int[]> geometry) =>
        ClosestPointOnPolyline(px, py, pz, geometry).distance;

    public static (int minX, int minY, int minZ, int maxX, int maxY, int maxZ) BoundingBox(IReadOnlyList<int[]> geometry)
    {
        if (geometry.Count == 0)
        {
            return (0, 0, 0, 0, 0, 0);
        }
        int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
        foreach (var p in geometry)
        {
            minX = Math.Min(minX, p[0]); maxX = Math.Max(maxX, p[0]);
            minY = Math.Min(minY, p[1]); maxY = Math.Max(maxY, p[1]);
            minZ = Math.Min(minZ, p[2]); maxZ = Math.Max(maxZ, p[2]);
        }
        return (minX, minY, minZ, maxX, maxY, maxZ);
    }

    /// <summary>Horizontal (x/z plane) bearing from a to b in degrees, [0, 360): 0 = +x, 90 = +z.
    /// NaN when a and b share x and z.</summary>
    public static double Bearing(int[] from, int[] to) => Bearing(from[0], from[2], to[0], to[2]);

    public static double Bearing(double fromX, double fromZ, double toX, double toZ)
    {
        double dx = toX - fromX, dz = toZ - fromZ;
        if (dx == 0 && dz == 0)
        {
            return double.NaN;
        }
        var degrees = Math.Atan2(dz, dx) * 180.0 / Math.PI;
        return degrees < 0 ? degrees + 360.0 : degrees;
    }

    /// <summary>The smaller angle between two bearings, [0, 180]; 180 when either is NaN.</summary>
    public static double BearingChange(double a, double b)
    {
        if (double.IsNaN(a) || double.IsNaN(b))
        {
            return 180.0;
        }
        var diff = Math.Abs(a - b) % 360.0;
        return diff > 180.0 ? 360.0 - diff : diff;
    }

    /// <summary>Bearing of the polyline leaving its first point (over the first segment that
    /// moves horizontally), or NaN for a vertical/degenerate polyline.</summary>
    public static double BearingAtStart(IReadOnlyList<int[]> geometry)
    {
        for (var i = 1; i < geometry.Count; i++)
        {
            var bearing = Bearing(geometry[0], geometry[i]);
            if (!double.IsNaN(bearing))
            {
                return bearing;
            }
        }
        return double.NaN;
    }

    /// <summary>Bearing of the polyline leaving its last point backwards (towards its start).</summary>
    public static double BearingAtEnd(IReadOnlyList<int[]> geometry)
    {
        var last = geometry.Count - 1;
        for (var i = last - 1; i >= 0; i--)
        {
            var bearing = Bearing(geometry[last], geometry[i]);
            if (!double.IsNaN(bearing))
            {
                return bearing;
            }
        }
        return double.NaN;
    }

    /// <summary>True when every point is an [x, y, z] triple.</summary>
    public static bool IsWellFormed(int[][]? geometry)
    {
        if (geometry == null)
        {
            return false;
        }
        foreach (var p in geometry)
        {
            if (p == null || p.Length != 3)
            {
                return false;
            }
        }
        return true;
    }

    public static int[][] Reversed(int[][] geometry)
    {
        var copy = (int[][])geometry.Clone();
        Array.Reverse(copy);
        return copy;
    }
}
