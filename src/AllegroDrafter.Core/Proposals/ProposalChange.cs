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
