namespace AllegroDrafter.Core.Drafting;

public enum Side { North, South, East, West }

/// <summary>
/// An exterior wall in plan, in feet. FaceCoord is the exterior face position:
/// X for a north-south wall, Y for an east-west wall.
/// Facing: the side its exterior face looks toward (from the wall's orientation in the model).
/// With Facing on every wall the planner sorts walls by the way they face, which is right on
/// L, U and T plans; without it, it falls back to splitting the building at its midline.
/// </summary>
public sealed record PlanWall(string Key, double X1, double Y1, double X2, double Y2, double FaceCoord, Side? Facing = null)
{
    public const double Tolerance = 0.01; // feet, about 1/8"

    /// <summary>Wall ends closer than this are joined (centerlines of joined walls meet within a wall thickness).</summary>
    public const double JoinTolerance = 1.0;

    public bool Touches(PlanWall other) =>
        Ends.Any(a => other.Ends.Any(b => Math.Abs(a.X - b.X) <= JoinTolerance && Math.Abs(a.Y - b.Y) <= JoinTolerance));

    private (double X, double Y)[] Ends => new[] { (X1, Y1), (X2, Y2) };

    public bool IsNorthSouth => Math.Abs(X2 - X1) < Tolerance && Math.Abs(Y2 - Y1) >= Tolerance;
    public bool IsEastWest => Math.Abs(Y2 - Y1) < Tolerance && Math.Abs(X2 - X1) >= Tolerance;
    public bool IsOrthogonal => IsNorthSouth || IsEastWest;
    public double MinX => Math.Min(X1, X2);
    public double MaxX => Math.Max(X1, X2);
    public double MinY => Math.Min(Y1, Y2);
    public double MaxY => Math.Max(Y1, Y2);
}

/// <summary>A door or window in an exterior wall. Center is X for an east-west wall, Y for a north-south wall.</summary>
public sealed record PlanOpening(string Key, string WallKey, double Center);

/// <summary>
/// A vertical edge face of a floor, deck or slab in plan, in feet. NormalAlongX: the face runs
/// north-south and locates an X position (east-west strings); otherwise it locates Y. Span is how far
/// the face runs along its length (Y for NormalAlongX), so the planner knows which side it's on.
/// </summary>
public sealed record PlanEdge(string Key, double Position, bool NormalAlongX, double SpanLo, double SpanHi);

/// <summary>
/// A column in plan (porch post, timber column), center and half its size, in feet. Strings measure
/// to its centerline: reference KeyX locates it in X (east-west strings), KeyY in Y.
/// </summary>
public sealed record PlanColumn(string Key, double X, double Y, double HalfSize = 0.5)
{
    public string KeyX => Key + ":x";
    public string KeyY => Key + ":y";
}

public sealed record DimensionRef(string Key, double Position);

/// <summary>
/// One dimension string. For North/South sides the line runs east-west at Y = LineCoord
/// and Refs are X positions. For East/West sides the line runs north-south at X = LineCoord.
/// </summary>
public sealed record DimensionString(Side Side, string Strategy, double LineCoord, IReadOnlyList<DimensionRef> Refs)
{
    public bool IsHorizontal => Side is Side.North or Side.South;
}

public sealed record DimensionPlan(IReadOnlyList<DimensionString> Strings, IReadOnlyList<string> Notes);

/// <summary>
/// Plans exterior dimension strings for a rectilinear floor plan, in three tiers
/// from the building out: openings, walls (jogs), overall.
///
/// Rules (the same ones a drafter uses):
/// - overall: the two outermost exterior faces on each side.
/// - walls: every exterior face of walls running toward that side (captures jogs).
/// - openings: door/window centerlines in walls facing that side, closed by the outermost faces.
/// Angled and curved walls are skipped and reported, never guessed.
/// </summary>
public static class DimensionPlanner
{
    public const string Overall = "overall";
    public const string Walls = "walls";
    public const string Openings = "openings";
    public const string Columns = "columns";

    /// <summary>
    /// Tiers from the building out. Office order (Bryan, 2026-11-26): openings, walls, overall.
    /// On a side with porch columns, the column string comes first, nearest the columns.
    /// </summary>
    public static readonly IReadOnlyList<string> Strategies = new[] { Columns, Openings, Walls, Overall };

    private const double Same = 1.0 / 192; // 1/16" - references closer than this are the same point

    public static DimensionPlan Plan(
        IReadOnlyList<PlanWall> walls,
        IReadOnlyList<PlanOpening> openings,
        IReadOnlyCollection<string> strategies,
        double offsetFeet = 4.0,
        double stepFeet = 2.0,
        IReadOnlyList<PlanColumn>? columns = null,
        IReadOnlyList<PlanEdge>? edges = null)
    {
        var notes = new List<string>();
        columns ??= Array.Empty<PlanColumn>();
        edges ??= Array.Empty<PlanEdge>();
        var unknown = strategies.Where(s => !Strategies.Contains(s)).ToList();
        if (unknown.Count > 0) throw new ArgumentException($"Unknown strategy: {string.Join(", ", unknown)}. Use {string.Join(", ", Strategies)}.");
        if (offsetFeet <= 0 || stepFeet <= 0) throw new ArgumentException("offsetFeet and stepFeet must be positive.");

        var skipped = walls.Count(w => !w.IsOrthogonal);
        if (skipped > 0) notes.Add($"{skipped} angled or curved exterior wall(s) skipped. Dimension those by hand.");

        var straight = walls.Where(w => w.IsOrthogonal).ToList();
        if (straight.Count == 0)
        {
            notes.Add("No straight exterior walls found in this view.");
            return new DimensionPlan(Array.Empty<DimensionString>(), notes);
        }

        var minX = straight.Min(w => w.MinX);
        var maxX = straight.Max(w => w.MaxX);
        var minY = straight.Min(w => w.MinY);
        var maxY = straight.Max(w => w.MaxY);
        var midX = (minX + maxX) / 2;
        var midY = (minY + maxY) / 2;

        var northSouth = straight.Where(w => w.IsNorthSouth).ToList();
        var eastWest = straight.Where(w => w.IsEastWest).ToList();
        var wallsByKey = straight.ToDictionary(w => w.Key);

        // Facing known for every wall: sort by which way each wall's exterior face looks.
        var byFacing = straight.All(w => w.Facing is not null);
        if (!byFacing && straight.Any(w => w.Facing is not null))
            notes.Add("Some walls had no exterior direction; the building was split at its midline instead. Check the strings on L or U plans.");

        // Columns outside the house belong to the side they stand on (a porch post north of the house
        // is on the north strings). A corner post can be on two sides. Columns inside are interior work.
        // Spacing is off the outside faces (3/8" off the outside of finish), not the centerlines.
        var faceNorth = eastWest.Count > 0 ? eastWest.Max(w => w.FaceCoord) : maxY;
        var faceSouth = eastWest.Count > 0 ? eastWest.Min(w => w.FaceCoord) : minY;
        var faceEast = northSouth.Count > 0 ? northSouth.Max(w => w.FaceCoord) : maxX;
        var faceWest = northSouth.Count > 0 ? northSouth.Min(w => w.FaceCoord) : minX;

        var footprint = new Footprint(straight);
        var columnsBySide = new Dictionary<Side, List<PlanColumn>>();
        void Put(Side s, PlanColumn c)
        {
            if (!columnsBySide.TryGetValue(s, out var list)) columnsBySide[s] = list = new List<PlanColumn>();
            if (!list.Contains(c)) list.Add(c);
        }
        foreach (var c in columns)
        {
            if (footprint.Count > 0 && footprint.IsInside(c.X, c.Y)) continue; // interior column: not an exterior string
            // Open sky on one side only: that's its side (a post in front of a facade, or in a notch).
            foreach (var axisNorthSouth in new[] { true, false })
                if (footprint.SideOf(c.X, c.Y, axisNorthSouth) is { } s) Put(s, c);
            // Past the outermost faces (a corner post): every side it stands beyond.
            if (c.Y > faceNorth) Put(Side.North, c);
            if (c.Y < faceSouth) Put(Side.South, c);
            if (c.X > faceEast) Put(Side.East, c);
            if (c.X < faceWest) Put(Side.West, c);
        }

        var strings = new List<DimensionString>();
        foreach (var side in new[] { Side.North, Side.South, Side.East, Side.West })
        {
            var horizontal = side is Side.North or Side.South;
            var sideColumns = columnsBySide.TryGetValue(side, out var sc) ? sc : new List<PlanColumn>();
            // Floor, deck and slab edges on this side: the faces across the string (they locate along it) on any
            // part of a floor that runs out past the house here, and the faces along it that the strings must clear.
            var faceHere = side switch { Side.North => faceNorth, Side.South => faceSouth, Side.East => faceEast, _ => faceWest };
            bool Beyond(double v) => side is Side.North or Side.East ? v > faceHere + Same : v < faceHere - Same;
            var sideEdges = edges.Where(e => e.NormalAlongX == horizontal && (Beyond(e.SpanHi) || Beyond(e.SpanLo))).ToList();
            var clearEdges = edges.Where(e => e.NormalAlongX != horizontal && Beyond(e.Position)).Select(e => e.Position);

            // The strings clear everything on this side: the outermost wall face, column or floor edge.
            var extreme = side switch
            {
                Side.North => sideColumns.Select(c => c.Y + c.HalfSize).Concat(clearEdges).Append(faceNorth).Max(),
                Side.South => sideColumns.Select(c => c.Y - c.HalfSize).Concat(clearEdges).Append(faceSouth).Min(),
                Side.East => sideColumns.Select(c => c.X + c.HalfSize).Concat(clearEdges).Append(faceEast).Max(),
                _ => sideColumns.Select(c => c.X - c.HalfSize).Concat(clearEdges).Append(faceWest).Min(),
            };
            // Walls perpendicular to this side's dimension line give the faces we measure between.
            var perpendicular = horizontal ? northSouth : eastWest;
            if (perpendicular.Count < 2)
            {
                notes.Add($"{side}: fewer than two {(horizontal ? "north-south" : "east-west")} exterior walls, no string.");
                continue;
            }

            // Walls parallel to the dimension line whose exterior faces this side: the facade seen from here.
            var parallel = horizontal ? eastWest : northSouth;
            var facade = byFacing
                ? parallel.Where(w => w.Facing == side).ToList()
                : parallel.Where(w => side switch
                {
                    Side.North => w.MinY >= midY,
                    Side.South => w.MaxY <= midY,
                    Side.East => w.MinX >= midX,
                    _ => w.MaxX <= midX,
                }).ToList();

            // Perpendicular walls that make the jogs in that facade: they join a facade wall.
            // Midline fallback: perpendicular walls on this half of the building.
            var nearSide = byFacing
                ? perpendicular.Where(w => facade.Any(f => f.Touches(w))).ToList()
                : perpendicular.Where(w => side switch
                {
                    Side.North => w.MaxY >= midY,
                    Side.South => w.MinY <= midY,
                    Side.East => w.MaxX >= midX,
                    _ => w.MinX <= midX,
                }).ToList();

            var outer = new[]
            {
                perpendicular.MinBy(w => w.FaceCoord)!,
                perpendicular.MaxBy(w => w.FaceCoord)!,
            };

            // Every tier on this side closes to the same two ends: the outermost wall face or floor edge each way
            // (a deck running past the house makes the overall deck edge to deck edge, the way the office draws it).
            var lowEnd = sideEdges.Where(e => e.Position < outer[0].FaceCoord - Same).MinBy(e => e.Position) is { } le
                ? new DimensionRef(le.Key, le.Position) : new DimensionRef(outer[0].Key, outer[0].FaceCoord);
            var highEnd = sideEdges.Where(e => e.Position > outer[1].FaceCoord + Same).MaxBy(e => e.Position) is { } he
                ? new DimensionRef(he.Key, he.Position) : new DimensionRef(outer[1].Key, outer[1].FaceCoord);
            var ends = new[] { lowEnd, highEnd };
            var outerFaces = outer.Select(w => new DimensionRef(w.Key, w.FaceCoord));

            var overallPositions = Dedupe(ends).Select(r => r.Position).ToList();
            var tier = 0;
            foreach (var strategy in Strategies.Where(strategies.Contains))
            {
                var refs = new List<DimensionRef>();
                switch (strategy)
                {
                    case Columns:
                        if (sideColumns.Count == 0) continue; // no posts on this side: no column string
                        refs.AddRange(ends);
                        refs.AddRange(outerFaces);
                        refs.AddRange(sideColumns.Select(c => horizontal ? new DimensionRef(c.KeyX, c.X) : new DimensionRef(c.KeyY, c.Y)));
                        break;

                    case Overall:
                        refs.AddRange(ends);
                        break;

                    case Walls:
                        refs.AddRange(ends);
                        refs.AddRange(outerFaces);
                        refs.AddRange(nearSide.Select(w => new DimensionRef(w.Key, w.FaceCoord)));
                        // Where the deck or slab steps along this side, that's a jog too.
                        refs.AddRange(sideEdges.Select(e => new DimensionRef(e.Key, e.Position)));
                        break;

                    case Openings:
                        // Openings live in the facade walls of this side.
                        var facing = facade.Select(w => w.Key).ToHashSet();
                        var inSide = openings.Where(o => facing.Contains(o.WallKey) && wallsByKey.ContainsKey(o.WallKey)).ToList();
                        if (inSide.Count == 0) continue; // Nothing to locate: no opening string on this side.
                        refs.AddRange(ends);
                        refs.AddRange(outerFaces);
                        refs.AddRange(inSide.Select(o => new DimensionRef(o.Key, o.Center)));
                        break;
                }

                var cleaned = Dedupe(refs);
                if (cleaned.Count < 2) continue;
                // On a straight facade the jog string is the overall string. Don't draw it twice.
                if (strategy == Walls && strategies.Contains(Overall) && cleaned.Select(r => r.Position).SequenceEqual(overallPositions)) continue;

                var offset = offsetFeet + tier * stepFeet;
                var line = side is Side.North or Side.East ? extreme + offset : extreme - offset;
                strings.Add(new DimensionString(side, strategy, line, cleaned));
                tier++;
            }
        }

        return new DimensionPlan(strings, notes);
    }

    /// <summary>Sort by position and drop references at the same point (keeps the first key).</summary>
    private static List<DimensionRef> Dedupe(IEnumerable<DimensionRef> refs)
    {
        var result = new List<DimensionRef>();
        foreach (var r in refs.OrderBy(r => r.Position))
        {
            if (result.Count > 0 && Math.Abs(result[^1].Position - r.Position) < Same) continue;
            result.Add(r);
        }
        return result;
    }
}
