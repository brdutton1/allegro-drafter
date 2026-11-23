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
    public static List<string> Check(Document doc, IReadOnlyList<ProposalChange> changes, Core.Checks.AuditSettings? audit = null)
    {
        var errors = new List<string>();
        audit ??= new Core.Checks.AuditSettings();

        // Type Marks are checked together: collisions depend on the whole batch.
        var marks = changes.OfType<SetTypeMarkChange>().ToList();
        if (marks.Count > 0) errors.AddRange(FixEditor.CheckTypeMarks(doc, marks, audit).Select(e => "Type Marks: " + e));
        var newSheets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var placedInProposal = new HashSet<ElementId>();
        var templated = new HashSet<ElementId>();
        var newViews = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

                    if (newViews.Contains(place.ViewName))
                        break; // Created earlier in this proposal (schedule or callout view).

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
                    newViews.Add(schedule.Name);
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

                case ScheduleFieldsChange fields:
                    errors.AddRange(ScheduleEditor.Check(doc, fields).Select(e => prefix + e));
                    break;

                case TagViewChange tag:
                {
                    var view = RevitLookup.ResolveView(doc, tag.ViewName, tag.ViewType, out var viewError);
                    if (view is null)
                    {
                        errors.Add(prefix + viewError);
                        break;
                    }
                    var problem = Tagging.CheckView(view, tag.Category);
                    if (problem is not null)
                    {
                        errors.Add(prefix + problem);
                        break;
                    }
                    if (Tagging.ResolveTagType(doc, tag.Category, tag.TagType, out var tagError) is null)
                    {
                        errors.Add(prefix + tagError);
                        break;
                    }
                    if (Tagging.Candidates(doc, view, tag.Category, tag.UntaggedOnly).Count == 0)
                        errors.Add(prefix + $"Nothing to tag: no {(tag.UntaggedOnly ? "untagged " : "")}{tag.Category} are visible in \"{view.Name}\".");
                    break;
                }

                case CalloutChange callout:
                {
                    var parent = Callouts.ResolveParent(doc, callout, out var parentError);
                    if (parent is null)
                    {
                        errors.Add(prefix + parentError);
                        break;
                    }
                    if (Callouts.Area(doc, parent, callout.Area, out var areaError) is null) errors.Add(prefix + areaError);

                    if (callout.IsReference)
                    {
                        var target = RevitLookup.ResolveView(doc, callout.ReferenceView!, null, out var targetError);
                        if (target is null) errors.Add(prefix + targetError);
                        else if (target.ViewType is not (ViewType.DraftingView or ViewType.Detail or ViewType.FloorPlan or ViewType.CeilingPlan))
                            errors.Add(prefix + $"\"{target.Name}\" is a {target.ViewType} view. A reference callout in a plan can point at a drafting view, detail or plan.");
                        break;
                    }

                    if (Callouts.ResolveType(doc, parent, callout.ViewFamilyType!, out var typeError) is null) errors.Add(prefix + typeError);
                    if (Callouts.ViewNameTaken(doc, callout.Name!) || !newViews.Add(callout.Name!))
                        errors.Add(prefix + $"A view named \"{callout.Name}\" already exists. Pick another name.");
                    if (callout.TemplateName is not null && RevitLookup.FindTemplate(doc, callout.TemplateName) is null)
                        errors.Add(prefix + $"View template '{callout.TemplateName}' is in the office standards but not in this project.");
                    break;
                }

                case TextNoteChange note:
                {
                    var view = RevitLookup.ResolveView(doc, note.ViewName, note.ViewType, out var viewError);
                    if (view is null)
                    {
                        errors.Add(prefix + viewError);
                        break;
                    }
                    var problem = Notes.CheckView(view, note);
                    if (problem is not null) errors.Add(prefix + problem);
                    if (Notes.ResolveType(doc, note.TextType) is null)
                        errors.Add(prefix + $"Text type '{note.TextType}' is in the office standards but not in this project.");
                    if (note.LeaderToElementId is long id && doc.GetElement(new ElementId(id)) is null)
                        errors.Add(prefix + $"Leader target element {id} doesn't exist.");
                    break;
                }

                case DimensionViewChange dims:
                {
                    var plan = Dimensioning.ResolvePlan(doc, dims, out var planError);
                    if (plan is null)
                    {
                        errors.Add(prefix + planError);
                        break;
                    }
                    if (!dims.AllowExisting && Dimensioning.HasDimensions(doc, plan))
                        errors.Add(prefix + $"\"{plan.Name}\" already has dimensions. Adding more would double up. Set allowExisting to add anyway.");
                    if (dims.DimensionType is not null && Dimensioning.ResolveType(doc, dims.DimensionType) is null)
                        errors.Add(prefix + $"Linear dimension type '{dims.DimensionType}' isn't in this project.");
                    var data = Dimensioning.Extract(doc, plan);
                    var planned = Dimensioning.Plan(data, dims);
                    if (planned.Strings.Count == 0)
                        errors.Add(prefix + $"No dimension strings possible in \"{plan.Name}\". " + string.Join(" ", data.Notes.Concat(planned.Notes)));
                    break;
                }

                case SetTypeMarkChange:
                    break; // checked as a batch above

                case CreateSharedParameterChange parameter:
                    errors.AddRange(FixEditor.CheckParameter(doc, parameter).Select(e => prefix + e));
                    break;

                case ScheduleSortChange sort:
                    errors.AddRange(FixEditor.CheckSort(doc, sort).Select(e => prefix + e));
                    break;

                case DeleteRoomsChange rooms:
                    errors.AddRange(FixEditor.CheckRooms(doc, rooms).Select(e => prefix + e));
                    break;

                case SetWallFunctionChange wall:
                    errors.AddRange(FixEditor.CheckWallFunction(doc, wall).Select(e => prefix + e));
                    break;

                default:
                    errors.Add(prefix + $"Unsupported change type {changes[i].GetType().Name}.");
                    break;
            }
        }
        return errors;
    }

    /// <summary>
    /// Fills in what the designer needs to judge a change: real element labels from the model,
    /// how many tags will be placed, how many dimension strings will be drawn.
    /// </summary>
    public static IReadOnlyList<ProposalChange> Annotate(Document doc, IReadOnlyList<ProposalChange> changes) =>
        changes.Select(change => change switch
            {
                SetParametersChange set when doc.GetElement(new ElementId(set.ElementId)) is { } element
                    => set with { ElementLabel = RevitLookup.ElementLabel(element) },
                TagViewChange tag when RevitLookup.ResolveView(doc, tag.ViewName, tag.ViewType, out _) is { } view
                    => tag with { ExpectedCount = Tagging.Candidates(doc, view, tag.Category, tag.UntaggedOnly).Count },
                DimensionViewChange dims when Dimensioning.ResolvePlan(doc, dims, out _) is { } plan
                    => dims with { ExpectedStrings = Dimensioning.Plan(Dimensioning.Extract(doc, plan), dims).Strings.Count },
                SetTypeMarkChange mark => FixEditor.Annotate(doc, mark),
                DeleteRoomsChange rooms => FixEditor.Annotate(doc, rooms),
                SetWallFunctionChange wall => FixEditor.Annotate(doc, wall),
                _ => change,
            })
            .ToList();
}
