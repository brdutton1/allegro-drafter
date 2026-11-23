namespace AllegroDrafter.Core.Proposals;

// Changes that fix what audit_set finds. Same rules as every other change:
// data only, checked against standards and the live model, applied in one
// transaction after the designer approves.

/// <summary>
/// Sets the Type Mark on one door or window type, the size code the tags and
/// schedules read (e.g. "376" to "3080"). It changes every placed instance of
/// the type, so the pane shows the count and the type's real size.
/// </summary>
/// <param name="TypeName">"Family: Type", exactly as audit_set and count_elements report it.</param>
/// <param name="SizeOverride">
/// Why the designer wants a size code that doesn't match the family's Width/Height
/// (e.g. "use the size in the type name; the family's Height parameter is wrong").
/// Only set when the designer said so. Shown in the pane, so approving it is their call.
/// </param>
public sealed record SetTypeMarkChange(
    string Category,
    string TypeName,
    string TypeMark,
    string? CurrentMark = null,
    string? Size = null,
    int? InstanceCount = null,
    string? SizeOverride = null) : ProposalChange
{
    public override string Describe()
    {
        var kind = Category == "windows" ? "Window" : "Door";
        var facts = new List<string>();
        if (InstanceCount is int n) facts.Add($"{n} placed");
        if (Size is not null) facts.Add(SizeOverride is null ? Size : $"family reads {Size}");
        var detail = facts.Count == 0 ? "" : $" ({string.Join(", ", facts)})";
        var from = string.IsNullOrWhiteSpace(CurrentMark) ? "(blank)" : CurrentMark;
        var overridden = SizeOverride is null ? "" : $". SIZE CHECK OVERRIDDEN: {SizeOverride}";
        return $"{kind} type \"{TypeName}\"{detail}: Type Mark {from} -> {TypeMark}{overridden}";
    }
}

/// <summary>
/// Sets a wall type's Function (Interior, Exterior, ...). An opening counts as exterior
/// only when its host wall type is Exterior, so this decides which doors and windows get
/// elevation tags and exterior dimensions. Changes every wall of the type.
/// </summary>
/// <param name="TypeName">"Basic Wall: Interior 2x6 Wall (INSULATION)", exactly as audit_set reports it.</param>
public sealed record SetWallFunctionChange(
    string TypeName,
    string Function,
    string? CurrentFunction = null,
    int? InstanceCount = null) : ProposalChange
{
    /// <summary>Revit's WallFunction values, in the spelling the add-in expects.</summary>
    public static readonly IReadOnlyList<string> Functions = new[] { "Interior", "Exterior", "Foundation", "Retaining", "Soffit", "CoreShaft" };

    public override string Describe()
    {
        var count = InstanceCount is int n ? $" ({n} wall{(n == 1 ? "" : "s")})" : "";
        var from = CurrentFunction ?? "?";
        return $"Wall type \"{TypeName}\"{count}: Function {from} -> {Function}";
    }
}

/// <summary>
/// Adds an instance parameter to the project for the given categories, from the
/// Allegro Drafter shared parameter file, so schedules and tags can use it.
/// </summary>
/// <param name="DataType">"yesno" or "text".</param>
public sealed record CreateSharedParameterChange(string Name, string DataType, IReadOnlyList<string> Categories) : ProposalChange
{
    public override string Describe() =>
        $"Add {(DataType == "yesno" ? "Yes/No" : "text")} instance parameter \"{Name}\" to {string.Join(", ", Categories.Select(Cap))}";

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    public bool Equals(CreateSharedParameterChange? other) =>
        other is not null && Name == other.Name && DataType == other.DataType && Categories.SequenceEqual(other.Categories);

    public override int GetHashCode() => HashCode.Combine(Name, DataType, Categories.Count);
}

/// <summary>
/// Replaces a schedule's sort/group order, and optionally turns itemizing on or off.
/// GroupHeaders puts a header row (the first field's value, e.g. "3080") and a footer
/// with the count above and below each group: order by the header, install by the lines.
/// </summary>
public sealed record ScheduleSortChange(string Schedule, IReadOnlyList<string> SortBy, bool? Itemized = null, bool? GroupHeaders = null) : ProposalChange
{
    public override string Describe()
    {
        var sort = "sort by " + string.Join(", then ", SortBy.Select(s => $"\"{s}\""));
        var rows = Itemized switch
        {
            true => "; one row per element",
            false => "; one row per group, with Count",
            null => "",
        };
        var groups = GroupHeaders switch
        {
            true => $"; a header and a count for each \"{SortBy[0]}\"",
            false => "; no group headers",
            null => "",
        };
        return $"Schedule \"{Schedule}\": {sort}{rows}{groups}";
    }

    public bool Equals(ScheduleSortChange? other) =>
        other is not null && Schedule == other.Schedule && Itemized == other.Itemized && GroupHeaders == other.GroupHeaders && SortBy.SequenceEqual(other.SortBy);

    public override int GetHashCode() => HashCode.Combine(Schedule, Itemized, GroupHeaders, SortBy.Count);
}

/// <summary>
/// Deletes rooms that aren't placed on a plan or aren't enclosed. Placed rooms are refused.
/// With AllUnplaced, the add-in fills in the ids at propose time so the designer
/// approves an exact list.
/// </summary>
public sealed record DeleteRoomsChange(IReadOnlyList<long> RoomIds, bool AllUnplaced = false, string? Summary = null) : ProposalChange
{
    [System.Text.Json.Serialization.JsonIgnore]
    public override bool IsDestructive => true;

    public override string Describe()
    {
        var what = AllUnplaced && RoomIds.Count == 0 ? "every unplaced and unenclosed room" : $"{RoomIds.Count} unplaced or unenclosed room(s)";
        return $"Delete {what}{(Summary is null ? "" : $": {Summary}")}";
    }

    public bool Equals(DeleteRoomsChange? other) =>
        other is not null && AllUnplaced == other.AllUnplaced && Summary == other.Summary && RoomIds.SequenceEqual(other.RoomIds);

    public override int GetHashCode() => HashCode.Combine(AllUnplaced, RoomIds.Count);
}
