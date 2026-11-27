namespace AllegroDrafter.Core.Drafting;

/// <summary>
/// Inside and outside of the building from its exterior walls, by the plan's shape rather than by how
/// each wall was drawn (a wall modeled flipped still faces the way the outline says).
///
/// How: lay a fine grid over the plan, paint every exterior wall onto it (thickened so small gaps at
/// corners seal), and flood-fill from beyond the building. Every open cell the flood can't reach is
/// inside. That doesn't care whether walls meet end to end: walls that butt into the middle of
/// another, run past a corner, overlap, or stand alone (a deck or garden wall) all come out right.
/// 25-0083 proved the need: an outline traced from wall ends fell apart on a real model.
/// </summary>
public sealed class Footprint
{
    private const double Cell = 0.25;      // feet per grid cell
    private const double Seal = 0.75;      // walls painted this far either side of the centerline: seals gaps up to 1'-6"
    private const double Margin = 4.0;     // open band around the building so the flood starts outside

    private readonly IReadOnlyList<(double X1, double Y1, double X2, double Y2)> _segments;
    private readonly double _x0, _y0;
    private readonly int _w, _h;
    private readonly byte[] _grid;         // 0 open-inside, 1 wall, 2 outside
    private readonly int _insideCells;

    public Footprint(IEnumerable<PlanWall> walls)
    {
        _segments = walls.Where(w => w.IsOrthogonal).Select(w => (w.X1, w.Y1, w.X2, w.Y2)).ToList();
        if (_segments.Count == 0)
        {
            _grid = Array.Empty<byte>();
            return;
        }
        _x0 = _segments.Min(s => Math.Min(s.X1, s.X2)) - Margin;
        _y0 = _segments.Min(s => Math.Min(s.Y1, s.Y2)) - Margin;
        var x1 = _segments.Max(s => Math.Max(s.X1, s.X2)) + Margin;
        var y1 = _segments.Max(s => Math.Max(s.Y1, s.Y2)) + Margin;
        _w = (int)Math.Ceiling((x1 - _x0) / Cell) + 1;
        _h = (int)Math.Ceiling((y1 - _y0) / Cell) + 1;
        _grid = new byte[_w * _h];

        foreach (var (ax, ay, bx, by) in _segments)
        {
            var i0 = Col(Math.Min(ax, bx) - Seal); var i1 = Col(Math.Max(ax, bx) + Seal);
            var j0 = Row(Math.Min(ay, by) - Seal); var j1 = Row(Math.Max(ay, by) + Seal);
            for (var j = j0; j <= j1; j++)
                for (var i = i0; i <= i1; i++)
                    _grid[j * _w + i] = 1;
        }

        // Flood from the border: everything reachable without crossing a wall is outside.
        var queue = new Queue<int>();
        void Seed(int i, int j)
        {
            var k = j * _w + i;
            if (_grid[k] != 0) return;
            _grid[k] = 2;
            queue.Enqueue(k);
        }
        for (var i = 0; i < _w; i++) { Seed(i, 0); Seed(i, _h - 1); }
        for (var j = 0; j < _h; j++) { Seed(0, j); Seed(_w - 1, j); }
        while (queue.Count > 0)
        {
            var k = queue.Dequeue();
            int i = k % _w, j = k / _w;
            if (i > 0) Seed(i - 1, j);
            if (i < _w - 1) Seed(i + 1, j);
            if (j > 0) Seed(i, j - 1);
            if (j < _h - 1) Seed(i, j + 1);
        }
        _insideCells = _grid.Count(c => c == 0);
    }

    /// <summary>How many straight walls the outline was built from.</summary>
    public int Count => _segments.Count;

    /// <summary>False when the walls don't enclose anything (a gap wider than 1'-6", or no walls): inside can't be told.</summary>
    public bool Encloses => _insideCells > 0;

    public bool IsInside(double x, double y) => CellAt(x, y) == 0;

    private bool IsOutside(double x, double y) => CellAt(x, y) is 2 or null;

    private byte? CellAt(double x, double y)
    {
        if (_grid.Length == 0) return null;
        var i = (int)Math.Floor((x - _x0) / Cell);
        var j = (int)Math.Floor((y - _y0) / Cell);
        if (i < 0 || j < 0 || i >= _w || j >= _h) return null;
        return _grid[j * _w + i];
    }

    private int Col(double x) => Math.Clamp((int)Math.Floor((x - _x0) / Cell), 0, _w - 1);
    private int Row(double y) => Math.Clamp((int)Math.Floor((y - _y0) / Cell), 0, _h - 1);

    /// <summary>
    /// The side a wall's outside looks toward: step off the wall's midpoint past its painted band on each
    /// side; the side that's outside, with inside on the other, is where it faces. Null for a wall with
    /// outside on both sides (a deck wall) or inside on both (an interior wall someone marked exterior).
    /// </summary>
    public Side? FacingOf(PlanWall wall)
    {
        if (!wall.IsOrthogonal || !Encloses) return null;
        var step = Seal + 2 * Cell;
        var (plus, minus) = wall.IsNorthSouth ? (Side.East, Side.West) : (Side.North, Side.South);
        var (dx, dy) = wall.IsNorthSouth ? (step, 0.0) : (0.0, step);
        // Vote at a few spots along the wall, so a bay or jog at one spot doesn't decide it:
        // a spot votes for the side that's outside when the other side is inside.
        int plusVotes = 0, minusVotes = 0;
        foreach (var t in new[] { 0.25, 0.5, 0.75 })
        {
            var px = wall.X1 + (wall.X2 - wall.X1) * t;
            var py = wall.Y1 + (wall.Y2 - wall.Y1) * t;
            if (IsOutside(px + dx, py + dy) && IsInside(px - dx, py - dy)) plusVotes++;
            if (IsOutside(px - dx, py - dy) && IsInside(px + dx, py + dy)) minusVotes++;
        }
        return plusVotes > minusVotes ? plus : minusVotes > plusVotes ? minus : null;
    }

    /// <summary>
    /// For a point outside the building: which side of it the point is on, looking along the given axis
    /// (horizontal = check north/south). The side whose ray meets no wall, with walls the other way.
    /// Null if the point is inside, or open both ways (past an open side, in a courtyard).
    /// </summary>
    public Side? SideOf(double x, double y, bool checkNorthSouth)
    {
        if (Count == 0 || (Encloses && IsInside(x, y))) return null;
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
