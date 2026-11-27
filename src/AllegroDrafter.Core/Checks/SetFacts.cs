namespace AllegroDrafter.Core.Checks;

/// <summary>
/// Everything the set audit needs, read from the model in one pass.
/// Plain data, so every check runs (and is tested) without Revit.
/// Lengths are in feet unless the name says otherwise.
/// </summary>
public sealed class SetFacts
{
    public List<SheetFact> Sheets { get; set; } = new();
    public List<ViewFact> PlacedViews { get; set; } = new();
    public List<ScheduleFact> Schedules { get; set; } = new();
    public List<OpeningTypeFact> OpeningTypes { get; set; } = new();
    public List<OpeningFact> Openings { get; set; } = new();
    public List<RoomFact> Rooms { get; set; } = new();
    public List<LegendFact> Legends { get; set; } = new();
    public List<string> ModeledWallTypes { get; set; } = new();
    public List<WallTypeFact> WallTypes { get; set; } = new();
    public List<Finishing.DoorGeometry> DoorGeometry { get; set; } = new();
    public List<AnnotationTypeUsage> AnnotationTypes { get; set; } = new();
    public List<TextFact> Texts { get; set; } = new();
    public List<PlanStringFact> PlanStrings { get; set; } = new();
    public List<TagTextFact> TagTexts { get; set; } = new();

    /// <summary>Things the reader couldn't get, reported so a silent gap doesn't look like a pass.</summary>
    public List<string> ReadWarnings { get; set; } = new();
}

public sealed record SheetFact(string Number, string Name, int PlacedItemCount, bool IsPlaceholder = false);

/// <summary>
/// An exterior dimension string on a placed floor plan: the side of the building it's on (by the plan's
/// outline) and where its first and last witness lines fall along it, in model feet.
/// </summary>
public sealed record PlanStringFact(string ViewName, string Side, double Lo, double Hi, int Segments, long ElementId);

/// <summary>What a door or window tag prints, in a view placed on a sheet.</summary>
public sealed record TagTextFact(long TagId, long ElementId, string Category, string ViewName, string Text);

/// <summary>A view placed on at least one sheet.</summary>
public sealed record ViewFact(string Name, string ViewType, int? ScaleDenominator, string? Template, IReadOnlyList<string> OnSheets);

/// <param name="Category">"doors", "windows", "sheets", or another category label.</param>
public sealed record ScheduleFact(
    string Name,
    string Category,
    bool IsKeySchedule,
    bool IsItemized,
    IReadOnlyList<ScheduleFieldFact> Fields,
    IReadOnlyList<string> SortGroupFields,
    IReadOnlyList<string> OnSheets,
    bool FirstGroupCounts = false,
    IReadOnlyList<long>? ElementIds = null)
{
    /// <summary>One line per element with a count under each group: the Count column isn't needed.</summary>
    public bool CountsInGroupFooters => IsItemized && FirstGroupCounts;
}

public sealed record ScheduleFieldFact(string Name, bool IsHidden);

/// <summary>A door or window type that has at least one instance in the model.</summary>
/// <param name="Key">"Family: Type", the office name format.</param>
public sealed record OpeningTypeFact(
    string Category,
    string Key,
    string Family,
    string TypeName,
    string? TypeMark,
    double? WidthFeet,
    double? HeightFeet,
    int InstanceCount);

public sealed record OpeningFact(
    long ElementId,
    string Category,
    string Label,
    string TypeKey,
    string? Level,
    double? X,
    double? Y,
    double? WidthFeet,
    double? HeightFeet,
    double? SillFeet,
    bool IsExterior,
    long? FromRoomId,
    long? ToRoomId,
    bool TaggedInPlacedPlan,
    bool TaggedInPlacedElevation,
    IReadOnlyDictionary<string, string?> Parameters,
    string? HostWallType = null)
{
    /// <summary>The room a window serves: inside of an exterior wall is usually ToRoom.</summary>
    public long? ServedRoomId => ToRoomId ?? FromRoomId;

    /// <summary>Parameter value; null if the parameter is missing or blank. Use <see cref="HasParameter"/> to tell those apart.</summary>
    public string? Value(string name) => Parameters.TryGetValue(name, out var v) ? v : null;

    public bool HasParameter(string name) => Parameters.ContainsKey(name);
}

/// <param name="State">"placed", "unplaced", "not enclosed" (or redundant).</param>
/// <param name="X">Room location point (plan feet), for numbering order. Null when unplaced.</param>
public sealed record RoomFact(long ElementId, string Number, string Name, string? Level, double AreaSqFt, string State, bool HasWetFixture,
    double? X = null, double? Y = null, IReadOnlyDictionary<string, string?>? Parameters = null)
{
    public string? Value(string name) => Parameters is not null && Parameters.TryGetValue(name, out var v) ? v : null;
    public bool HasParameter(string name) => Parameters?.ContainsKey(name) == true;
}

/// <summary>A wall type with at least one wall in the model.</summary>
/// <param name="Name">"Basic Wall: Interior 2x6 Wall (INSULATION)".</param>
/// <param name="Function">Revit's type Function: Interior, Exterior, Foundation, Retaining, Soffit, CoreShaft.</param>
/// <param name="Kind">Basic, Curtain or Stacked.</param>
public sealed record WallTypeFact(string Name, string Function, string Kind, int InstanceCount, double LengthFeet);

public sealed record LegendFact(string Name, IReadOnlyList<string> OnSheets, IReadOnlyList<string> WallTypes);

/// <param name="Kind">"text" or "dimension".</param>
public sealed record AnnotationTypeUsage(string Kind, string TypeName, double? HeightInches, int Count, string? SampleView);

/// <summary>A text note on a sheet, or in a view placed on one.</summary>
public sealed record TextFact(string Text, string ViewName, IReadOnlyList<string> SheetNumbers);
