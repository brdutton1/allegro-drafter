namespace AllegroDrafter.Core.Drafting;

public enum Side { North, South, East, West }

/// <summary>
/// An exterior wall in plan, in feet. FaceCoord is the exterior face position:
/// X for a north-south wall, Y for an east-west wall.
/// </summary>
public sealed record PlanWall(string Key, double X1, double Y1, double X2, double Y2, double FaceCoord)
{
    public const double Tolerance = 0.01; // feet, about 1/8"

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
    public static readonly IReadOnlyList<string> Strategies = new[] { Openings, Walls, Overall };

    private const double Same = 1.0 / 192; // 1/16" - references closer than this are the same point

    public static DimensionPlan Plan(
        IReadOnlyList<PlanWall> walls,
        IReadOnlyList<PlanOpening> openings,
        IReadOnlyCollection<string> strategies,
        double offsetFeet = 4.0,
        double stepFeet = 2.0)
    {
        var notes = new List<string>();
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

        var strings = new List<DimensionString>();
        foreach (var side in new[] { Side.North, Side.South, Side.East, Side.West })
        {
            var horizontal = side is Side.North or Side.South;
            // Walls perpendicular to this side's dimension line give the faces we measure between.
            var perpendicular = horizontal ? northSouth : eastWest;
            if (perpendicular.Count < 2)
            {
                notes.Add($"{side}: fewer than two {(horizontal ? "north-south" : "east-west")} exterior walls, no string.");
                continue;
            }

            // Walls on this half of the building.
            var nearSide = perpendicular.Where(w => side switch
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

            var overallPositions = Dedupe(outer.Select(w => new DimensionRef(w.Key, w.FaceCoord))).Select(r => r.Position).ToList();
            var tier = 0;
            foreach (var strategy in Strategies.Where(strategies.Contains))
            {
                var refs = new List<DimensionRef>();
                switch (strategy)
                {
                    case Overall:
                        refs.AddRange(outer.Select(w => new DimensionRef(w.Key, w.FaceCoord)));
                        break;

                    case Walls:
                        refs.AddRange(outer.Concat(nearSide).Select(w => new DimensionRef(w.Key, w.FaceCoord)));
                        break;

                    case Openings:
                        // Openings live in walls parallel to the dimension line, on this side's half.
                        var facing = (horizontal ? eastWest : northSouth).Where(w => side switch
                        {
                            Side.North => w.MinY >= midY,
                            Side.South => w.MaxY <= midY,
                            Side.East => w.MinX >= midX,
                            _ => w.MaxX <= midX,
                        }).Select(w => w.Key).ToHashSet();
                        var inSide = openings.Where(o => facing.Contains(o.WallKey) && wallsByKey.ContainsKey(o.WallKey)).ToList();
                        if (inSide.Count == 0) continue; // Nothing to locate: no opening string on this side.
                        refs.AddRange(outer.Select(w => new DimensionRef(w.Key, w.FaceCoord)));
                        refs.AddRange(inSide.Select(o => new DimensionRef(o.Key, o.Center)));
                        break;
                }

                var cleaned = Dedupe(refs);
                if (cleaned.Count < 2) continue;
                // On a straight facade the jog string is the overall string. Don't draw it twice.
                if (strategy == Walls && strategies.Contains(Overall) && cleaned.Select(r => r.Position).SequenceEqual(overallPositions)) continue;

                var offset = offsetFeet + tier * stepFeet;
                var line = side switch
                {
                    Side.North => maxY + offset,
                    Side.South => minY - offset,
                    Side.East => maxX + offset,
                    _ => minX - offset,
                };
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
