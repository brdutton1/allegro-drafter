namespace AllegroDrafter.Core.Drafting;

/// <summary>
/// One entry point for a floor plan's dimensions: the three exterior tiers plus the interior
/// strings. The tool, the model check and the apply all plan through here, so what the designer
/// approves is exactly what gets drawn.
/// </summary>
public static class PlanDimensioning
{
    public const string Interior = InteriorDimensionPlanner.Strategy;

    /// <summary>Every strategy, in drawing order: exterior tiers from the building out, then interior.</summary>
    public static readonly IReadOnlyList<string> AllStrategies = DimensionPlanner.Strategies.Append(Interior).ToList();

    public static readonly IReadOnlyList<string> Bases = new[] { "face", "center" };

    /// <summary>Side names a request may use: view-relative (as it sits on the sheet) or model directions.</summary>
    public static readonly IReadOnlyList<string> SideNames = new[] { "top", "bottom", "left", "right", "north", "south", "east", "west" };

    /// <summary>
    /// Turns requested side names into model sides. top/bottom/left/right use the view's orientation:
    /// viewTop is the model side that's at the top of the sheet (north unless the view is rotated).
    /// </summary>
    public static IReadOnlyList<Side>? ResolveSides(IReadOnlyCollection<string>? names, Side viewTop)
    {
        if (names is not { Count: > 0 }) return null;
        var right = Clockwise(viewTop);
        return names.Select(n => n.Trim().ToLowerInvariant() switch
        {
            "top" => viewTop,
            "bottom" => Opposite(viewTop),
            "right" => right,
            "left" => Opposite(right),
            "north" => Side.North,
            "south" => Side.South,
            "east" => Side.East,
            "west" => Side.West,
            var bad => throw new ArgumentException($"Unknown side '{bad}'. Use {string.Join(", ", SideNames)}."),
        }).Distinct().ToList();
    }

    public static Side Opposite(Side s) => s switch { Side.North => Side.South, Side.South => Side.North, Side.East => Side.West, _ => Side.East };

    /// <summary>The side 90 degrees clockwise: what's on the right when this side is at the top.</summary>
    public static Side Clockwise(Side s) => s switch { Side.North => Side.East, Side.East => Side.South, Side.South => Side.West, _ => Side.North };

    public static DimensionPlan Plan(
        IReadOnlyList<PlanWall> exterior,
        IReadOnlyList<PlanOpening> openings,
        IReadOnlyList<PlanInteriorWall> interior,
        IReadOnlyCollection<string> strategies,
        double offsetFeet = 4.0,
        double stepFeet = 2.0,
        InteriorBasis basis = InteriorBasis.Face,
        IReadOnlyCollection<Side>? onlySides = null)
    {
        var unknown = strategies.Where(s => !AllStrategies.Contains(s)).ToList();
        if (unknown.Count > 0) throw new ArgumentException($"Unknown strategy: {string.Join(", ", unknown)}. Use {string.Join(", ", AllStrategies)}.");

        var strings = new List<DimensionString>();
        var notes = new List<string>();
        var exteriorTiers = strategies.Where(s => s != Interior).ToList();
        if (exteriorTiers.Count > 0)
        {
            var ext = DimensionPlanner.Plan(exterior, openings, exteriorTiers, offsetFeet, stepFeet);
            // Tiers are spaced per side, so leaving sides out doesn't move the ones kept.
            strings.AddRange(onlySides is null ? ext.Strings : ext.Strings.Where(s => onlySides.Contains(s.Side)));
            notes.AddRange(onlySides is null ? ext.Notes : ext.Notes.Where(n => !SideNote(n) || onlySides.Any(s => n.StartsWith(s + ":"))));
        }
        if (strategies.Contains(Interior))
        {
            var inner = InteriorDimensionPlanner.Plan(interior, exterior.Where(w => w.IsOrthogonal).ToList(), basis);
            strings.AddRange(inner.Strings);
            notes.AddRange(inner.Notes);
        }
        var closure = DimensionClosure.Problems(strings);
        notes.AddRange(closure.Select(p => "Check: " + p));
        return new DimensionPlan(strings, notes);
    }

    private static bool SideNote(string note) => Enum.GetNames<Side>().Any(s => note.StartsWith(s + ":"));

    public static InteriorBasis ParseBasis(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "face" => InteriorBasis.Face,
        "center" or "centerline" => InteriorBasis.Center,
        _ => throw new ArgumentException($"Interior basis '{value}' isn't known. Use face or center."),
    };
}
