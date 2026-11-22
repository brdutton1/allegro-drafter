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
            }
        }
        return errors;
    }
}
