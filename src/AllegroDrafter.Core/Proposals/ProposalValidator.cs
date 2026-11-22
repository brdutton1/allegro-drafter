using AllegroDrafter.Core.Standards;

namespace AllegroDrafter.Core.Proposals;

/// <summary>
/// Checks a proposal against the office standards before the designer ever sees it.
/// Model-specific checks (does the view exist, is the sheet number free) run in Revit.
/// </summary>
public static class ProposalValidator
{
    public const int MaxChangesPerProposal = 50;

    public static List<string> Validate(ProposalRequest request, StandardsProfile? profile)
    {
        var errors = new List<string>();
        if (profile is null)
        {
            errors.Add("No office standards profile is loaded. In Revit, open the office template and run Allegro > Export Standards, then make it the active profile.");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(request.Title)) errors.Add("title is required.");
        if (request.Changes.Count == 0) errors.Add("A proposal needs at least one change.");
        if (request.Changes.Count > MaxChangesPerProposal)
        {
            errors.Add($"This proposal has {request.Changes.Count} changes. The limit is {MaxChangesPerProposal} so the designer can actually review it. Split it into smaller proposals (for example by discipline or level).");
        }

        var titleBlocks = profile.TitleBlocks;
        var templates = profile.ViewTemplates.Select(t => t.Name).Distinct().ToList();
        var sheetNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var templateTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var placements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scheduleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var elementIds = new HashSet<long>();
        var scheduleTemplates = profile.ViewTemplates.Where(t => t.ViewType == "Schedule").Select(t => t.Name).Distinct().ToList();
        var finishParameters = new HashSet<string>(profile.Manual.FinishParameters ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        var tagTypes = profile.TagTypes.Select(t => t.Name).Distinct().ToList();
        var viewFamilyTypes = profile.ViewFamilyTypes.Select(t => t.Name).Distinct().ToList();
        var newViewNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var taggedViews = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dimensionedViews = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var finishCodes = (profile.Manual.FinishCodes ?? new List<FinishCode>()).Select(f => f.Code.Trim()).Where(c => c.Length > 0).Distinct().ToList();

        for (var i = 0; i < request.Changes.Count; i++)
        {
            var prefix = $"Change {i + 1}: ";
            switch (request.Changes[i])
            {
                case CreateSheetChange sheet:
                    if (!titleBlocks.Contains(sheet.TitleBlock, StringComparer.Ordinal))
                        errors.Add(prefix + Names.UnknownNameMessage("Title block", sheet.TitleBlock, titleBlocks));
                    if (!sheetNumbers.Add(sheet.Number))
                        errors.Add(prefix + $"Sheet number {sheet.Number} appears more than once in this proposal.");
                    break;

                case PlaceViewChange place:
                    var key = $"{place.SheetNumber}|{place.ViewType}|{place.ViewName}";
                    if (!placements.Add(key))
                        errors.Add(prefix + $"\"{place.ViewName}\" is placed on sheet {place.SheetNumber} more than once.");
                    break;

                case ApplyViewTemplateChange template:
                    if (!templates.Contains(template.TemplateName, StringComparer.Ordinal))
                        errors.Add(prefix + Names.UnknownNameMessage("View template", template.TemplateName, templates));
                    if (!templateTargets.Add($"{template.ViewType}|{template.ViewName}"))
                        errors.Add(prefix + $"View \"{template.ViewName}\" gets a template more than once in this proposal.");
                    break;

                case CreateScheduleChange schedule:
                    if (!scheduleNames.Add(schedule.Name))
                        errors.Add(prefix + $"Schedule \"{schedule.Name}\" is created more than once in this proposal.");
                    if (schedule.IsCopy)
                    {
                        if (!profile.Schedules.Contains(schedule.SourceSchedule!, StringComparer.Ordinal))
                            errors.Add(prefix + Names.UnknownNameMessage("Schedule", schedule.SourceSchedule!, profile.Schedules));
                    }
                    else if (!scheduleTemplates.Contains(schedule.TemplateName!, StringComparer.Ordinal))
                    {
                        errors.Add(prefix + Names.UnknownNameMessage("Schedule view template", schedule.TemplateName!, scheduleTemplates));
                    }
                    break;

                case TagViewChange tag:
                    if (tag.TagType is not null && !tagTypes.Contains(tag.TagType, StringComparer.Ordinal))
                        errors.Add(prefix + Names.UnknownNameMessage("Tag type", tag.TagType, tagTypes));
                    if (!taggedViews.Add($"{tag.ViewType}|{tag.ViewName}|{tag.Category}"))
                        errors.Add(prefix + $"{tag.Category} in \"{tag.ViewName}\" are tagged more than once in this proposal.");
                    break;

                case CalloutChange callout:
                    if (!callout.IsReference)
                    {
                        if (!viewFamilyTypes.Contains(callout.ViewFamilyType!, StringComparer.Ordinal))
                            errors.Add(prefix + Names.UnknownNameMessage("View type", callout.ViewFamilyType!, viewFamilyTypes));
                        if (!newViewNames.Add(callout.Name!))
                            errors.Add(prefix + $"View \"{callout.Name}\" is created more than once in this proposal.");
                    }
                    if (callout.TemplateName is not null && !templates.Contains(callout.TemplateName, StringComparer.Ordinal))
                        errors.Add(prefix + Names.UnknownNameMessage("View template", callout.TemplateName, templates));
                    break;

                case TextNoteChange note:
                    if (!profile.TextNoteTypes.Contains(note.TextType, StringComparer.Ordinal))
                        errors.Add(prefix + Names.UnknownNameMessage("Text type", note.TextType, profile.TextNoteTypes));
                    break;

                case DimensionViewChange dims:
                    if (dims.DimensionType is not null && !profile.DimensionTypes.Contains(dims.DimensionType, StringComparer.Ordinal))
                        errors.Add(prefix + Names.UnknownNameMessage("Dimension type", dims.DimensionType, profile.DimensionTypes));
                    if (!dimensionedViews.Add($"{dims.ViewType}|{dims.ViewName}"))
                        errors.Add(prefix + $"\"{dims.ViewName}\" is dimensioned more than once in this proposal.");
                    break;

                case SetParametersChange set:
                    if (!elementIds.Add(set.ElementId))
                        errors.Add(prefix + $"Element {set.ElementId} appears more than once in this proposal. Put all its values in one entry.");
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var value in set.Values)
                    {
                        if (!seen.Add(value.Name))
                            errors.Add(prefix + $"{set.ElementLabel}: \"{value.Name}\" is set twice.");
                        if (finishCodes.Count > 0 && finishParameters.Contains(value.Name))
                        {
                            foreach (var code in FinishTokens(value.Value).Where(t => !finishCodes.Contains(t, StringComparer.Ordinal)))
                                errors.Add(prefix + $"{set.ElementLabel}, {value.Name}: " + Names.UnknownNameMessage("Finish code", code, finishCodes));
                        }
                    }
                    break;
            }
        }
        return errors;
    }

    /// <summary>
    /// Splits a finish value into codes. "P1/T2" (paint above tile) and "T1, T2" are both allowed.
    /// Blank means "clear it" and has no codes.
    /// </summary>
    public static IEnumerable<string> FinishTokens(string value) =>
        value.Split(new[] { '/', ',', ';', '+' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
