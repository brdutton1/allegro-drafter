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

                default:
                    errors.Add(prefix + $"Unsupported change type {changes[i].GetType().Name}.");
                    break;
            }
        }
        return errors;
    }
}
