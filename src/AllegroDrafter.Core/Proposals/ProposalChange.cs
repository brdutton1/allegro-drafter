using System.Text.Json.Serialization;

namespace AllegroDrafter.Core.Proposals;

/// <summary>
/// One change the AI wants to make. Changes are data only. Nothing touches
/// the Revit model until the designer approves the proposal that holds them.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(CreateSheetChange), "createSheet")]
[JsonDerivedType(typeof(PlaceViewChange), "placeView")]
[JsonDerivedType(typeof(ApplyViewTemplateChange), "applyViewTemplate")]
[JsonDerivedType(typeof(CreateScheduleChange), "createSchedule")]
[JsonDerivedType(typeof(SetParametersChange), "setParameters")]
[JsonDerivedType(typeof(TagViewChange), "tagView")]
[JsonDerivedType(typeof(CalloutChange), "callout")]
[JsonDerivedType(typeof(TextNoteChange), "textNote")]
[JsonDerivedType(typeof(DimensionViewChange), "dimensionView")]
[JsonDerivedType(typeof(ScheduleFieldsChange), "scheduleFields")]
[JsonDerivedType(typeof(SetTypeMarkChange), "setTypeMark")]
[JsonDerivedType(typeof(CreateSharedParameterChange), "createSharedParameter")]
[JsonDerivedType(typeof(ScheduleSortChange), "scheduleSort")]
[JsonDerivedType(typeof(DeleteRoomsChange), "deleteRooms")]
[JsonDerivedType(typeof(SetWallFunctionChange), "setWallFunction")]
public abstract record ProposalChange
{
    /// <summary>Plain-English line shown in the approval pane.</summary>
    public abstract string Describe();

    /// <summary>Deletes something. The pane asks for a second click before applying.</summary>
    [JsonIgnore]
    public virtual bool IsDestructive => false;
}

public sealed record CreateSheetChange(string Number, string Name, string TitleBlock) : ProposalChange
{
    public override string Describe() => $"Create sheet {Number} \"{Name}\" ({TitleBlock})";
}

/// <summary>Places a view, legend or schedule on a sheet. No coordinates = auto-arrange.</summary>
public sealed record PlaceViewChange(
    string SheetNumber,
    string ViewName,
    string? ViewType = null,
    double? XFeet = null,
    double? YFeet = null) : ProposalChange
{
    [JsonIgnore]
    public bool HasPosition => XFeet.HasValue && YFeet.HasValue;

    public override string Describe()
    {
        var view = ViewType is null ? $"\"{ViewName}\"" : $"{ViewType} \"{ViewName}\"";
        var where = HasPosition ? $" at ({XFeet * 12:0.##}\", {YFeet * 12:0.##}\")" : " (auto-arranged)";
        return $"Place {view} on sheet {SheetNumber}{where}";
    }
}

public sealed record ApplyViewTemplateChange(string ViewName, string TemplateName, string? ViewType = null) : ProposalChange
{
    public override string Describe()
    {
        var view = ViewType is null ? $"\"{ViewName}\"" : $"{ViewType} \"{ViewName}\"";
        return $"Apply view template \"{TemplateName}\" to {view}";
    }
}

/// <summary>
/// Creates a schedule view. Either copies an existing office schedule (SourceSchedule),
/// or creates one for a category and applies a schedule view template (Category + TemplateName).
/// </summary>
public sealed record CreateScheduleChange(
    string Name,
    string? SourceSchedule = null,
    string? Category = null,
    string? TemplateName = null) : ProposalChange
{
    [JsonIgnore]
    public bool IsCopy => SourceSchedule is not null;

    public override string Describe() => IsCopy
        ? $"Create schedule \"{Name}\" as a copy of \"{SourceSchedule}\""
        : $"Create {Category} schedule \"{Name}\" with template \"{TemplateName}\"";
}

public sealed record ParameterValue(string Name, string Value);

/// <summary>Sets instance parameters on one door, window or room.</summary>
public sealed record SetParametersChange(long ElementId, string ElementLabel, IReadOnlyList<ParameterValue> Values) : ProposalChange
{
    public override string Describe()
    {
        var values = string.Join(", ", Values.Select(v => v.Value.Length == 0 ? $"{v.Name} = (blank)" : $"{v.Name} = {v.Value}"));
        return $"{ElementLabel}: {values}";
    }

    // Records compare lists by reference. Compare by content so tests and dedupe behave.
    public bool Equals(SetParametersChange? other) =>
        other is not null && ElementId == other.ElementId && ElementLabel == other.ElementLabel && Values.SequenceEqual(other.Values);

    public override int GetHashCode() => HashCode.Combine(ElementId, ElementLabel, Values.Count);
}

/// <summary>Tags every (untagged) element of one category visible in a view.</summary>
public sealed record TagViewChange(
    string ViewName,
    string Category,
    string? TagType = null,
    string? ViewType = null,
    bool Leader = false,
    bool UntaggedOnly = true,
    int? ExpectedCount = null) : ProposalChange
{
    public override string Describe()
    {
        var count = ExpectedCount is null ? "" : $"{ExpectedCount} ";
        var which = UntaggedOnly ? "untagged " : "";
        var tag = TagType ?? "the default tag";
        return $"Tag {count}{which}{Category} in \"{ViewName}\" with {tag}{(Leader ? " (with leaders)" : "")}";
    }
}

/// <summary>Where a callout goes: around a room, around elements, or an explicit plan rectangle (feet).</summary>
public sealed record CalloutArea(
    string? RoomNumber = null,
    IReadOnlyList<long>? ElementIds = null,
    double? MinX = null,
    double? MinY = null,
    double? MaxX = null,
    double? MaxY = null,
    double MarginFeet = 1.0)
{
    public string Describe() =>
        RoomNumber is not null ? $"around room {RoomNumber}"
        : ElementIds is { Count: > 0 } ? $"around {ElementIds.Count} element(s)"
        : $"at ({MinX:0.##}, {MinY:0.##}) to ({MaxX:0.##}, {MaxY:0.##})";

    public bool Equals(CalloutArea? other) =>
        other is not null && RoomNumber == other.RoomNumber && MinX == other.MinX && MinY == other.MinY && MaxX == other.MaxX
        && MaxY == other.MaxY && MarginFeet == other.MarginFeet
        && (ElementIds ?? Array.Empty<long>()).SequenceEqual(other.ElementIds ?? Array.Empty<long>());

    public override int GetHashCode() => HashCode.Combine(RoomNumber, MinX, MinY, MaxX, MaxY, MarginFeet);
}

/// <summary>
/// A callout in a plan view. Detail callout: creates a new view (Name + ViewFamilyType).
/// Reference callout: points at an existing view such as a standard detail (ReferenceView).
/// </summary>
public sealed record CalloutChange(
    string ParentView,
    CalloutArea Area,
    string? ParentViewType = null,
    string? Name = null,
    string? ViewFamilyType = null,
    string? ReferenceView = null,
    string? TemplateName = null,
    int? Scale = null) : ProposalChange
{
    [JsonIgnore]
    public bool IsReference => ReferenceView is not null;

    public override string Describe() => IsReference
        ? $"Reference callout to \"{ReferenceView}\" in \"{ParentView}\" {Area.Describe()}"
        : $"Callout \"{Name}\" ({ViewFamilyType}{(Scale is null ? "" : $", 1:{Scale}")}{(TemplateName is null ? "" : $", template \"{TemplateName}\"")}) in \"{ParentView}\" {Area.Describe()}";
}

/// <summary>A text note, optionally with a leader to an element. Coordinates are plan/drafting view feet.</summary>
public sealed record TextNoteChange(
    string ViewName,
    string Text,
    string TextType,
    double XFeet,
    double YFeet,
    string? ViewType = null,
    long? LeaderToElementId = null) : ProposalChange
{
    public override string Describe()
    {
        var text = Text.Length > 60 ? Text[..57] + "..." : Text;
        var leader = LeaderToElementId is null ? "" : $" with leader to element {LeaderToElementId}";
        return $"Note \"{text.Replace("\n", " / ")}\" ({TextType}) in \"{ViewName}\"{leader}";
    }
}

/// <summary>
/// Rule-based dimension strings on a floor plan: exterior tiers (openings, walls, overall) and
/// interior strings locating every interior wall (InteriorBasis: "face" or "center").
/// </summary>
public sealed record DimensionViewChange(
    string ViewName,
    IReadOnlyList<string> Strategies,
    string? DimensionType = null,
    string? ViewType = null,
    double OffsetFeet = 4.0,
    double StepFeet = 2.0,
    bool AllowExisting = false,
    int? ExpectedStrings = null,
    string? InteriorBasis = null,
    IReadOnlyList<string>? Sides = null,
    double? OffsetPaperInches = null,
    double? StepPaperInches = null,
    string? OpeningsTo = null,
    string? WallsTo = null) : ProposalChange
{
    public bool OpeningEdges => string.Equals(OpeningsTo, "edges", StringComparison.OrdinalIgnoreCase);

    /// <summary>Office spacing (2026-11-26): first string 3/8" off the outside face on paper, 3/8" between tiers.</summary>
    public const double OfficeSpacingPaperInches = 0.375;

    /// <summary>Model feet from the face to the first string at this view scale (paper inches win over feet).</summary>
    public double OffsetAt(int scale) => OffsetPaperInches is { } p ? p * scale / 12.0 : OffsetFeet;

    /// <summary>Model feet between tiers at this view scale.</summary>
    public double StepAt(int scale) => StepPaperInches is { } p ? p * scale / 12.0 : StepFeet;

    public override string Describe()
    {
        var count = ExpectedStrings is null ? "" : $"{ExpectedStrings} ";
        var what = Strategies.All(s => s == "interior") ? "interior" : Strategies.Contains("interior") ? "exterior and interior" : "exterior";
        var basis = Strategies.Contains("interior") ? $", interior walls to {InteriorBasis ?? "face"}" : "";
        var sides = Sides is { Count: > 0 } ? $", {string.Join("/", Sides)} side only" : "";
        if (OffsetPaperInches is { } o) sides += $", {Paper(o)} off the wall and {Paper(StepPaperInches ?? o)} apart on paper";
        if (OpeningEdges) sides += ", openings to their edges";
        if (string.Equals(WallsTo, "coreFace", StringComparison.OrdinalIgnoreCase)) sides += ", walls to face of stud";
        return $"Add {count}{what} dimension string(s) to \"{ViewName}\": {string.Join(" + ", Strategies)}{sides}{basis}{(DimensionType is null ? "" : $" ({DimensionType})")}";
    }

    private static string Paper(double inches) => Math.Abs(inches * 8 - Math.Round(inches * 8)) < 1e-9 && inches < 1 ? $"{(int)Math.Round(inches * 8)}/8\"".Replace("2/8", "1/4").Replace("4/8", "1/2").Replace("6/8", "3/4") : $"{inches}\"";

    public bool Equals(DimensionViewChange? other) =>
        other is not null && ViewName == other.ViewName && ViewType == other.ViewType && DimensionType == other.DimensionType
        && OffsetFeet == other.OffsetFeet && StepFeet == other.StepFeet && AllowExisting == other.AllowExisting
        && ExpectedStrings == other.ExpectedStrings && InteriorBasis == other.InteriorBasis && Strategies.SequenceEqual(other.Strategies)
        && (Sides ?? Array.Empty<string>()).SequenceEqual(other.Sides ?? Array.Empty<string>())
        && OffsetPaperInches == other.OffsetPaperInches && StepPaperInches == other.StepPaperInches
        && OpeningsTo == other.OpeningsTo && WallsTo == other.WallsTo;

    public override int GetHashCode() => HashCode.Combine(ViewName, ViewType, Strategies.Count);
}

public sealed record FieldToAdd(string Field, string? After = null, string? Before = null)
{
    public string Describe() =>
        After is not null ? $"\"{Field}\" after \"{After}\""
        : Before is not null ? $"\"{Field}\" before \"{Before}\""
        : $"\"{Field}\"";
}

/// <summary>Adds and removes columns on an existing schedule (e.g. add Mark to the door schedule).</summary>
public sealed record ScheduleFieldsChange(string Schedule, IReadOnlyList<FieldToAdd> Add, IReadOnlyList<string> Remove) : ProposalChange
{
    public override string Describe()
    {
        var parts = new List<string>();
        if (Add.Count > 0)
            parts.Add("add " + string.Join(", ", Add.Select(a => a.Describe())));
        if (Remove.Count > 0)
            parts.Add("remove " + string.Join(", ", Remove.Select(r => $"\"{r}\"")));
        return $"Schedule \"{Schedule}\": {string.Join("; ", parts)}";
    }

    public bool Equals(ScheduleFieldsChange? other) =>
        other is not null && Schedule == other.Schedule && Add.SequenceEqual(other.Add) && Remove.SequenceEqual(other.Remove);

    public override int GetHashCode() => HashCode.Combine(Schedule, Add.Count, Remove.Count);
}
