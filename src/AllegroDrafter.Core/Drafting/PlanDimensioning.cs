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

    public static DimensionPlan Plan(
        IReadOnlyList<PlanWall> exterior,
        IReadOnlyList<PlanOpening> openings,
        IReadOnlyList<PlanInteriorWall> interior,
        IReadOnlyCollection<string> strategies,
        double offsetFeet = 4.0,
        double stepFeet = 2.0,
        InteriorBasis basis = InteriorBasis.Face)
    {
        var unknown = strategies.Where(s => !AllStrategies.Contains(s)).ToList();
        if (unknown.Count > 0) throw new ArgumentException($"Unknown strategy: {string.Join(", ", unknown)}. Use {string.Join(", ", AllStrategies)}.");

        var strings = new List<DimensionString>();
        var notes = new List<string>();
        var exteriorTiers = strategies.Where(s => s != Interior).ToList();
        if (exteriorTiers.Count > 0)
        {
            var ext = DimensionPlanner.Plan(exterior, openings, exteriorTiers, offsetFeet, stepFeet);
            strings.AddRange(ext.Strings);
            notes.AddRange(ext.Notes);
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

    public static InteriorBasis ParseBasis(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "face" => InteriorBasis.Face,
        "center" or "centerline" => InteriorBasis.Center,
        _ => throw new ArgumentException($"Interior basis '{value}' isn't known. Use face or center."),
    };
}
