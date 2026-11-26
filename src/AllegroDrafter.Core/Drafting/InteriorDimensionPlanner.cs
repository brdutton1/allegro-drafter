namespace AllegroDrafter.Core.Drafting;

/// <summary>An interior wall in plan: centerline ends and thickness, in feet.</summary>
public sealed record PlanInteriorWall(string Key, double X1, double Y1, double X2, double Y2, double Thickness)
{
    public bool IsNorthSouth => Math.Abs(X2 - X1) < PlanWall.Tolerance && Math.Abs(Y2 - Y1) >= PlanWall.Tolerance;
    public bool IsEastWest => Math.Abs(Y2 - Y1) < PlanWall.Tolerance && Math.Abs(X2 - X1) >= PlanWall.Tolerance;
    public bool IsOrthogonal => IsNorthSouth || IsEastWest;
    public double MinX => Math.Min(X1, X2);
    public double MaxX => Math.Max(X1, X2);
    public double MinY => Math.Min(Y1, Y2);
    public double MaxY => Math.Max(Y1, Y2);

    /// <summary>Where the wall sits across the string: X for a north-south wall, Y for an east-west one.</summary>
    public double Center => IsNorthSouth ? X1 : Y1;

    /// <summary>The span the wall covers along its length: Y for north-south, X for east-west.</summary>
    public (double Lo, double Hi) Span => IsNorthSouth ? (MinY, MaxY) : (MinX, MaxX);
}

/// <summary>What an interior string measures to on each interior wall.</summary>
public enum InteriorBasis
{
    /// <summary>Wall centerline.</summary>
    Center,

    /// <summary>
    /// The face of the wall toward the start of the string (west or south face): one consistent face,
    /// which is what "face of stud" interior dimensions read as when the wall is all core.
    /// </summary>
    Face,
}

/// <summary>
/// Plans interior dimension strings that locate every straight interior wall, once across each
/// direction it can be located in, chained to the exterior faces so the builder can lay it out from
/// the slab edge.
///
/// Rules (a drafter's):
/// - A north-south wall is located by an east-west string (measuring X), and the reverse.
/// - Strings run through the building where they cross the most walls still unlocated
///   (interval stabbing, fewest strings), never along a wall, and inside each wall's length.
/// - Every wall the string crosses is on it, so the string reads continuously, outside face to outside face.
/// - Angled and curved walls are skipped and reported.
/// </summary>
public static class InteriorDimensionPlanner
{
    public const string Strategy = "interior";

    /// <summary>Exterior face refs start with this, so a string's ends are easy to check.</summary>
    public const string ExteriorPrefix = "ext:";

    private const double Same = 1.0 / 192;

    public static DimensionPlan Plan(
        IReadOnlyList<PlanInteriorWall> interior,
        IReadOnlyList<PlanWall> exterior,
        InteriorBasis basis = InteriorBasis.Center,
        double clearanceFeet = 1.5,
        double insetFeet = 1.0)
    {
        if (clearanceFeet < 0 || insetFeet < 0) throw new ArgumentException("clearanceFeet and insetFeet can't be negative.");
        var notes = new List<string>();
        var skipped = interior.Count(w => !w.IsOrthogonal);
        if (skipped > 0) notes.Add($"{skipped} angled or curved interior wall(s) skipped. Locate those by hand.");

        var strings = new List<DimensionString>();
        // East-west strings (horizontal, measuring X) locate north-south walls; then the other way.
        foreach (var horizontal in new[] { true, false })
        {
            var located = interior.Where(w => horizontal ? w.IsNorthSouth : w.IsEastWest).ToList();
            if (located.Count == 0) continue;
            // Walls the string must not run along: everything parallel to the string.
            var along = interior.Where(w => horizontal ? w.IsEastWest : w.IsNorthSouth).Select(w => w.Center)
                .Concat(exterior.Where(w => horizontal ? w.IsEastWest : w.IsNorthSouth).Select(w => w.FaceCoord))
                .ToList();
            var bounding = exterior.Where(w => horizontal ? w.IsNorthSouth : w.IsEastWest).ToList();

            var uncovered = located.OrderBy(w => w.Span.Hi).ToList();
            var lines = new List<double>();
            while (uncovered.Count > 0)
            {
                var first = uncovered[0];
                var (lo, hi) = Inset(first.Span, insetFeet);
                var at = Clear(lo, hi, along, clearanceFeet);
                if (at is null)
                {
                    at = (lo + hi) / 2;
                    notes.Add($"{Label(first)}: no spot clear of other walls to run the string; it runs close to a wall.");
                }
                lines.Add(at.Value);
                uncovered.RemoveAll(w => Contains(Inset(w.Span, insetFeet), at.Value));
            }

            foreach (var at in lines.Distinct().OrderBy(v => v))
            {
                var refs = new List<DimensionRef>();
                foreach (var w in located.Where(w => Contains(w.Span, at)))
                    refs.Add(new DimensionRef(w.Key, basis == InteriorBasis.Center ? w.Center : w.Center - w.Thickness / 2));

                // Close it to the exterior faces the line crosses on either side.
                var crossing = bounding.Where(b => horizontal ? b.MinY - Same <= at && at <= b.MaxY + Same : b.MinX - Same <= at && at <= b.MaxX + Same).ToList();
                var first = refs.Min(r => r.Position);
                var last = refs.Max(r => r.Position);
                var start = crossing.Where(b => b.FaceCoord < first).MaxBy(b => b.FaceCoord);
                var end = crossing.Where(b => b.FaceCoord > last).MinBy(b => b.FaceCoord);
                if (start is not null) refs.Add(new DimensionRef(ExteriorPrefix + start.Key, start.FaceCoord));
                if (end is not null) refs.Add(new DimensionRef(ExteriorPrefix + end.Key, end.FaceCoord));
                if (start is null || end is null)
                    notes.Add($"String at {(horizontal ? "Y" : "X")} = {at:0.##}: no exterior wall on {(start is null && end is null ? "either end" : start is null ? "one end" : "the other end")}; it doesn't close to the outside.");

                var cleaned = Dedupe(refs);
                if (cleaned.Count < 2) continue;
                strings.Add(new DimensionString(horizontal ? Side.North : Side.East, Strategy, at, cleaned));
            }
        }
        return new DimensionPlan(strings, notes);
    }

    private static (double Lo, double Hi) Inset((double Lo, double Hi) span, double inset)
    {
        var room = Math.Max(0, (span.Hi - span.Lo) / 2 - Same);
        var i = Math.Min(inset, room);
        return (span.Lo + i, span.Hi - i);
    }

    private static bool Contains((double Lo, double Hi) span, double v) => span.Lo - Same <= v && v <= span.Hi + Same;

    /// <summary>The highest point in [lo, hi] at least clearance from every wall running along the string.</summary>
    private static double? Clear(double lo, double hi, IReadOnlyList<double> along, double clearance)
    {
        bool Ok(double v) => along.All(a => Math.Abs(a - v) >= clearance - Same);
        if (Ok(hi)) return hi;
        // Candidates: just below each blocked zone's lower edge and just above its upper edge, inside [lo, hi].
        var candidates = along.SelectMany(a => new[] { a - clearance, a + clearance })
            .Where(v => v >= lo - Same && v <= hi + Same)
            .Select(v => Math.Clamp(v, lo, hi))
            .Append(lo)
            .OrderByDescending(v => v);
        foreach (var v in candidates) if (Ok(v)) return v;
        return null;
    }

    private static string Label(PlanInteriorWall w) => $"Wall {w.Key.TrimStart('w')}";

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

/// <summary>
/// Do the strings close? On each side every tier must start and end on the overall string's ends,
/// so a builder adding up any tier gets the overall. Interior strings must end on exterior faces.
/// </summary>
public static class DimensionClosure
{
    private const double Same = 1.0 / 192;

    public static IReadOnlyList<string> Problems(IReadOnlyList<DimensionString> strings)
    {
        var problems = new List<string>();
        foreach (var side in strings.Where(s => s.Strategy != InteriorDimensionPlanner.Strategy).GroupBy(s => s.Side))
        {
            var overall = side.FirstOrDefault(s => s.Strategy == DimensionPlanner.Overall);
            if (overall is null)
            {
                problems.Add($"{side.Key}: no overall string, so the tiers can't be checked against it.");
                continue;
            }
            var lo = overall.Refs.First().Position;
            var hi = overall.Refs.Last().Position;
            foreach (var s in side.Where(s => s != overall))
            {
                if (Math.Abs(s.Refs.First().Position - lo) > Same || Math.Abs(s.Refs.Last().Position - hi) > Same)
                    problems.Add($"{side.Key} {s.Strategy} string doesn't start and end on the overall ({Feet(hi - lo)}); it won't add up.");
            }
        }
        foreach (var s in strings.Where(s => s.Strategy == InteriorDimensionPlanner.Strategy))
        {
            if (!s.Refs.First().Key.StartsWith(InteriorDimensionPlanner.ExteriorPrefix) || !s.Refs.Last().Key.StartsWith(InteriorDimensionPlanner.ExteriorPrefix))
                problems.Add($"Interior string at {(s.IsHorizontal ? "Y" : "X")} = {s.LineCoord:0.##} doesn't end on exterior faces at both ends.");
        }
        return problems;
    }

    /// <summary>Feet and inches to the nearest 1/16", e.g. 40'-6 1/2".</summary>
    public static string Feet(double feet)
    {
        var sixteenths = (int)Math.Round(Math.Abs(feet) * 192);
        var ft = sixteenths / 192;
        var rem = sixteenths % 192;
        var inches = rem / 16;
        var frac = rem % 16;
        var fracText = frac == 0 ? "" : " " + Reduce(frac, 16);
        return $"{(feet < 0 ? "-" : "")}{ft}'-{inches}{fracText}\"";
    }

    private static string Reduce(int n, int d)
    {
        while (n % 2 == 0 && d % 2 == 0) { n /= 2; d /= 2; }
        return $"{n}/{d}";
    }
}
