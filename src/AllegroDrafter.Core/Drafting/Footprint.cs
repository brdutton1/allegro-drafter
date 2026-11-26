namespace AllegroDrafter.Core.Drafting;

/// <summary>
/// The building's outline from its exterior wall centerlines, used to tell inside from outside by
/// the plan's shape, not by how each wall was drawn. A wall modeled with its exterior side flipped
/// still faces the way the plan says (25-0083: flipped walls sent a string to the wrong facade).
/// Point-in-outline is the even/odd ray test, so the walls should close a loop.
/// </summary>
public sealed class Footprint
{
    private readonly IReadOnlyList<(double X1, double Y1, double X2, double Y2)> _segments;

    public Footprint(IEnumerable<PlanWall> walls)
    {
        // Only walls on a closed loop make the outline. A deck or garden wall with a loose end would
        // flip inside and outside for everything beside it, so walls with a dangling end are dropped
        // until nothing dangles.
        var loop = walls.Where(w => w.IsOrthogonal).ToList();
        while (true)
        {
            var dangling = loop.Where(w => !EndJoined(w, w.X1, w.Y1, loop) || !EndJoined(w, w.X2, w.Y2, loop)).ToList();
            if (dangling.Count == 0) break;
            loop = loop.Except(dangling).ToList();
        }
        _segments = loop.Select(w => (w.X1, w.Y1, w.X2, w.Y2)).ToList();
    }

    private static bool EndJoined(PlanWall wall, double x, double y, IReadOnlyList<PlanWall> all) =>
        all.Any(o => !ReferenceEquals(o, wall) && (Near(o.X1, o.Y1, x, y) || Near(o.X2, o.Y2, x, y)));

    private static bool Near(double x1, double y1, double x2, double y2) =>
        Math.Abs(x1 - x2) <= PlanWall.JoinTolerance && Math.Abs(y1 - y2) <= PlanWall.JoinTolerance;

    public int Count => _segments.Count;

    /// <summary>Even/odd: a ray to +X crossing the outline an odd number of times starts inside.</summary>
    public bool IsInside(double x, double y)
    {
        var inside = false;
        foreach (var (x1, y1, x2, y2) in _segments)
        {
            if ((y1 > y) == (y2 > y)) continue; // the segment doesn't straddle the ray's height
            var cross = x1 + (y - y1) / (y2 - y1) * (x2 - x1);
            if (cross > x) inside = !inside;
        }
        return inside;
    }

    /// <summary>
    /// The side a wall's outside looks toward: step off the wall's midpoint on each side and see
    /// which point is outside the outline. Null if both or neither are (a wall not on the loop).
    /// </summary>
    public Side? FacingOf(PlanWall wall, double step = 0.75)
    {
        if (!wall.IsOrthogonal) return null;
        var mx = (wall.X1 + wall.X2) / 2;
        var my = (wall.Y1 + wall.Y2) / 2;
        // Nudge along the wall a hair so the test never sits exactly on a corner.
        var (ax, ay) = wall.IsNorthSouth ? (0.0, 0.013) : (0.013, 0.0);
        var (plus, minus) = wall.IsNorthSouth ? (Side.East, Side.West) : (Side.North, Side.South);
        var (dx, dy) = wall.IsNorthSouth ? (step, 0.0) : (0.0, step);
        var plusOut = !IsInside(mx + dx + ax, my + dy + ay);
        var minusOut = !IsInside(mx - dx + ax, my - dy + ay);
        return plusOut == minusOut ? null : plusOut ? plus : minus;
    }

    /// <summary>
    /// For a point outside the building: which side of the building it's on, looking along the given
    /// axis (horizontal = check north/south). The side whose ray never meets a wall, with walls the
    /// other way. Null if the point is inside, or open both ways.
    /// </summary>
    public Side? SideOf(double x, double y, bool checkNorthSouth)
    {
        if (IsInside(x, y)) return null;
        if (checkNorthSouth)
        {
            var north = Crossings(x, y, 0, 1);
            var south = Crossings(x, y, 0, -1);
            if (north == 0 && south > 0) return Side.North;
            if (south == 0 && north > 0) return Side.South;
        }
        else
        {
            var east = Crossings(x, y, 1, 0);
            var west = Crossings(x, y, -1, 0);
            if (east == 0 && west > 0) return Side.East;
            if (west == 0 && east > 0) return Side.West;
        }
        return null;
    }

    /// <summary>Distance from the point to the first wall the ray meets, or null if it meets none.</summary>
    public double? DistanceTo(double x, double y, int dx, int dy)
    {
        double? best = null;
        foreach (var (x1, y1, x2, y2) in _segments)
        {
            double? d = null;
            if (dx != 0 && Math.Abs(x2 - x1) < PlanWall.Tolerance && Math.Min(y1, y2) <= y && y <= Math.Max(y1, y2))
                d = (x1 - x) * dx;
            else if (dy != 0 && Math.Abs(y2 - y1) < PlanWall.Tolerance && Math.Min(x1, x2) <= x && x <= Math.Max(x1, x2))
                d = (y1 - y) * dy;
            if (d is > 0 && (best is null || d < best)) best = d;
        }
        return best;
    }

    private int Crossings(double x, double y, int dx, int dy)
    {
        var n = 0;
        foreach (var (x1, y1, x2, y2) in _segments)
        {
            if (dx != 0 && Math.Abs(x2 - x1) < PlanWall.Tolerance && Math.Min(y1, y2) <= y && y <= Math.Max(y1, y2) && (x1 - x) * dx > 0) n++;
            if (dy != 0 && Math.Abs(y2 - y1) < PlanWall.Tolerance && Math.Min(x1, x2) <= x && x <= Math.Max(x1, x2) && (y1 - y) * dy > 0) n++;
        }
        return n;
    }
}
