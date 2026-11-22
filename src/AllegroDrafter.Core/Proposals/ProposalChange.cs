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
public abstract record ProposalChange
{
    /// <summary>Plain-English line shown in the approval pane.</summary>
    public abstract string Describe();
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
