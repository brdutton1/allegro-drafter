using AllegroDrafter.Core.Proposals;
using Autodesk.Revit.DB;

namespace AllegroDrafter.Revit.Model;

/// <summary>
/// Checks a proposal against the live model. Runs when the AI proposes (so bad
/// proposals never reach the designer) and again right before applying (in case
/// someone changed the model in between).
/// </summary>
internal static class ModelChecker
{
    public static List<string> Check(Document doc, IReadOnlyList<ProposalChange> changes)
    {
        var errors = new List<string>();
        var newSheets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var placedInProposal = new HashSet<ElementId>();
        var templated = new HashSet<ElementId>();
        var newSchedules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var placement = RevitLookup.PlacementMap(doc);

        for (var i = 0; i < changes.Count; i++)
        {
            var prefix = $"Change {i + 1}: ";
            switch (changes[i])
            {
                case CreateSheetChange sheet:
                {
                    var existing = RevitLookup.FindSheet(doc, sheet.Number);
                    if (existing is not null)
                        errors.Add(prefix + $"Sheet {sheet.Number} already exists (\"{existing.Name}\"). Pick another number or use propose_place_views for the existing sheet.");
                    newSheets.Add(sheet.Number);
                    if (RevitLookup.FindTitleBlock(doc, sheet.TitleBlock) is null)
                        errors.Add(prefix + $"Title block '{sheet.TitleBlock}' is in the office standards but not loaded in this project. Load the family (or Transfer Project Standards) first.");
                    break;
                }

                case PlaceViewChange place:
                {
                    var sheet = RevitLookup.FindSheet(doc, place.SheetNumber);
                    var sheetIsNew = newSheets.Contains(place.SheetNumber);
                    if (sheet is null && !sheetIsNew)
                    {
                        errors.Add(prefix + $"Sheet {place.SheetNumber} doesn't exist and isn't created earlier in this proposal.");
                        break;
                    }

                    if (newSchedules.Contains(place.ViewName) && (place.ViewType is null || place.ViewType.Equals("Schedule", StringComparison.OrdinalIgnoreCase)))
                        break; // Created earlier in this proposal. Schedules can go on any sheet.

                    var view = RevitLookup.ResolveView(doc, place.ViewName, place.ViewType, out var viewError);
                    if (view is null)
                    {
                        errors.Add(prefix + viewError);
                        break;
                    }

                    if (RevitLookup.IsMultiSheet(view))
                    {
                        if (sheet is not null && placement.TryGetValue(view.Id, out var onSheets) && onSheets.Contains(sheet.SheetNumber, StringComparer.OrdinalIgnoreCase))
                            errors.Add(prefix + $"\"{view.Name}\" is already on sheet {sheet.SheetNumber}.");
                        break;
                    }

                    if (placement.TryGetValue(view.Id, out var sheets))
                    {
                        errors.Add(prefix + $"\"{view.Name}\" is already on sheet {string.Join(", ", sheets)}. A {view.ViewType} view can only be on one sheet. Duplicate the view first if you need it twice.");
                        break;
                    }
                    if (!placedInProposal.Add(view.Id))
                    {
                        errors.Add(prefix + $"\"{view.Name}\" is placed on more than one sheet in this proposal.");
                        break;
                    }
                    if (sheet is not null && !Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id))
                        errors.Add(prefix + $"Revit won't allow \"{view.Name}\" on sheet {sheet.SheetNumber} (empty view or unsupported view type).");
                    break;
                }

                case ApplyViewTemplateChange assign:
                {
                    var view = RevitLookup.ResolveView(doc, assign.ViewName, assign.ViewType, out var viewError);
                    if (view is null)
                    {
                        errors.Add(prefix + viewError);
                        break;
                    }
                    if (!templated.Add(view.Id))
                    {
                        errors.Add(prefix + $"\"{view.Name}\" gets a template more than once in this proposal.");
                        break;
                    }
                    var template = RevitLookup.FindTemplate(doc, assign.TemplateName);
                    if (template is null)
                    {
                        errors.Add(prefix + $"View template '{assign.TemplateName}' is in the office standards but not in this project. Transfer it from the office template first.");
                        break;
                    }
                    if (!view.IsValidViewTemplate(template.Id))
                        errors.Add(prefix + $"Template '{template.Name}' ({template.ViewType}) doesn't fit {view.ViewType} view \"{view.Name}\".");
                    break;
                }

                case CreateScheduleChange schedule:
                {
                    newSchedules.Add(schedule.Name);
                    if (RevitLookup.ScheduleNameTaken(doc, schedule.Name))
                        errors.Add(prefix + $"A schedule named \"{schedule.Name}\" already exists. Use it, or pick another name.");

                    if (schedule.IsCopy)
                    {
                        var source = RevitLookup.FindSchedule(doc, schedule.SourceSchedule!);
                        if (source is null)
                            errors.Add(prefix + $"Schedule \"{schedule.SourceSchedule}\" is in the office standards but not in this project. Transfer it from the office template first.");
                        else if (!source.CanViewBeDuplicated(ViewDuplicateOption.Duplicate))
                            errors.Add(prefix + $"Revit won't duplicate \"{source.Name}\".");
                    }
                    else
                    {
                        if (!ParameterWriter.Categories.ContainsKey(schedule.Category ?? ""))
                            errors.Add(prefix + $"Unknown category '{schedule.Category}'.");
                        var template = RevitLookup.FindTemplate(doc, schedule.TemplateName!);
                        if (template is null)
                            errors.Add(prefix + $"Schedule view template '{schedule.TemplateName}' is in the office standards but not in this project. Transfer it from the office template first.");
                        else if (template.ViewType != ViewType.Schedule)
                            errors.Add(prefix + $"'{template.Name}' is a {template.ViewType} template, not a schedule template.");
                    }
                    break;
                }

                case SetParametersChange set:
                {
                    var element = doc.GetElement(new ElementId(set.ElementId));
                    if (element is null)
                    {
                        errors.Add(prefix + $"Element id {set.ElementId} doesn't exist (deleted, or the id is from another model). Re-read with list_elements.");
                        break;
                    }
                    if (!ParameterWriter.IsEditableCategory(element))
                    {
                        errors.Add(prefix + $"Element {set.ElementId} is a {element.Category?.Name ?? "non-category element"}. Only doors, windows and rooms can be edited.");
                        break;
                    }
                    var label = RevitLookup.ElementLabel(element);
                    foreach (var value in set.Values)
                    {
                        var problem = ParameterWriter.Check(element, value.Name, value.Value);
                        if (problem is not null) errors.Add(prefix + $"{label}: {problem}");
                    }
                    break;
                }

                default:
                    errors.Add(prefix + $"Unsupported change type {changes[i].GetType().Name}.");
                    break;
            }
        }
        return errors;
    }

    /// <summary>Swaps the AI's element labels for the real ones from the model, so the designer reviews the truth.</summary>
    public static IReadOnlyList<ProposalChange> WithModelLabels(Document doc, IReadOnlyList<ProposalChange> changes) =>
        changes.Select(change => change is SetParametersChange set && doc.GetElement(new ElementId(set.ElementId)) is { } element
                ? set with { ElementLabel = RevitLookup.ElementLabel(element) }
                : change)
            .ToList();
}
