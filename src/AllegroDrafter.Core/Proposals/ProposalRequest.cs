using System.Text.Json.Nodes;
using AllegroDrafter.Core.Mcp;

namespace AllegroDrafter.Core.Proposals;

public sealed record ProposalRequest(string Title, string Rationale, IReadOnlyList<ProposalChange> Changes);

/// <summary>Turns propose_* tool arguments into a list of changes.</summary>
public static class ProposalRequestParser
{
    public static ProposalRequest Parse(string toolName, JsonObject args)
    {
        var title = Args.RequiredString(args, "title", "arguments");
        var rationale = Args.OptionalString(args, "rationale", "arguments") ?? "";
        var changes = toolName switch
        {
            ToolNames.ProposeSheets => ParseSheets(args, allowViews: false),
            ToolNames.ProposeSheetSet => ParseSheets(args, allowViews: true),
            ToolNames.ProposePlaceViews => ParsePlacements(args),
            ToolNames.ProposeViewTemplates => ParseTemplates(args),
            ToolNames.ProposeSchedules => ParseSchedules(args),
            ToolNames.ProposeParameterValues => ParseParameterValues(args),
            _ => throw new ToolArgumentException($"'{toolName}' is not a proposal tool."),
        };
        return new ProposalRequest(title, rationale, changes);
    }

    private static List<ProposalChange> ParseSheets(JsonObject args, bool allowViews)
    {
        var changes = new List<ProposalChange>();
        var placements = new List<ProposalChange>();
        var sheets = Args.RequiredArray(args, "sheets", "arguments");
        for (var i = 0; i < sheets.Count; i++)
        {
            var path = $"sheets[{i}]";
            var sheet = Args.ObjectAt(sheets, i, "sheets");
            var number = Args.RequiredString(sheet, "number", path);
            changes.Add(new CreateSheetChange(
                number,
                Args.RequiredString(sheet, "name", path),
                Args.RequiredString(sheet, "titleBlock", path)));

            var views = Args.OptionalArray(sheet, "views", path);
            if (views is null) continue;
            if (!allowViews) throw new ToolArgumentException($"{path}.views is not allowed here. Use propose_sheet_set to create sheets and place views together.");
            for (var j = 0; j < views.Count; j++)
            {
                var viewPath = $"{path}.views[{j}]";
                placements.Add(ParsePlacement(Args.ObjectAt(views, j, $"{path}.views"), viewPath, number));
            }
        }
        // Sheets first, then placements, so every placement's sheet already exists when it runs.
        changes.AddRange(placements);
        return changes;
    }

    private static List<ProposalChange> ParsePlacements(JsonObject args)
    {
        var list = Args.RequiredArray(args, "placements", "arguments");
        var changes = new List<ProposalChange>();
        for (var i = 0; i < list.Count; i++)
        {
            var path = $"placements[{i}]";
            var item = Args.ObjectAt(list, i, "placements");
            changes.Add(ParsePlacement(item, path, Args.RequiredString(item, "sheetNumber", path)));
        }
        return changes;
    }

    private static PlaceViewChange ParsePlacement(JsonObject item, string path, string sheetNumber)
    {
        var x = Args.OptionalDouble(item, "xFeet", path);
        var y = Args.OptionalDouble(item, "yFeet", path);
        if (x.HasValue != y.HasValue) throw new ToolArgumentException($"{path}: give both xFeet and yFeet, or neither to auto-arrange.");
        return new PlaceViewChange(
            sheetNumber,
            Args.RequiredString(item, "viewName", path),
            Args.OptionalString(item, "viewType", path),
            x,
            y);
    }

    private static List<ProposalChange> ParseTemplates(JsonObject args)
    {
        var list = Args.RequiredArray(args, "assignments", "arguments");
        var changes = new List<ProposalChange>();
        for (var i = 0; i < list.Count; i++)
        {
            var path = $"assignments[{i}]";
            var item = Args.ObjectAt(list, i, "assignments");
            changes.Add(new ApplyViewTemplateChange(
                Args.RequiredString(item, "viewName", path),
                Args.RequiredString(item, "templateName", path),
                Args.OptionalString(item, "viewType", path)));
        }
        return changes;
    }

    private static List<ProposalChange> ParseSchedules(JsonObject args)
    {
        var list = Args.RequiredArray(args, "schedules", "arguments");
        var changes = new List<ProposalChange>();
        var placements = new List<ProposalChange>();
        for (var i = 0; i < list.Count; i++)
        {
            var path = $"schedules[{i}]";
            var item = Args.ObjectAt(list, i, "schedules");
            var name = Args.RequiredString(item, "name", path);
            var source = Args.OptionalString(item, "sourceSchedule", path);
            var category = Args.OptionalString(item, "category", path)?.ToLowerInvariant();
            var template = Args.OptionalString(item, "templateName", path);

            if (source is not null && (category is not null || template is not null))
                throw new ToolArgumentException($"{path}: give either sourceSchedule (copy an office schedule) or category + templateName, not both.");
            if (source is null && (category is null || template is null))
                throw new ToolArgumentException($"{path}: give sourceSchedule, or both category and templateName.");
            if (category is not null && !ToolNames.EditableCategories.Contains(category))
                throw new ToolArgumentException($"{path}.category must be one of: {string.Join(", ", ToolNames.EditableCategories)}.");

            changes.Add(new CreateScheduleChange(name, source, category, template));

            var sheet = Args.OptionalString(item, "sheetNumber", path);
            if (sheet is not null)
            {
                var x = Args.OptionalDouble(item, "xFeet", path);
                var y = Args.OptionalDouble(item, "yFeet", path);
                if (x.HasValue != y.HasValue) throw new ToolArgumentException($"{path}: give both xFeet and yFeet, or neither to auto-arrange.");
                placements.Add(new PlaceViewChange(sheet, name, "Schedule", x, y));
            }
        }
        // Create every schedule before placing any of them.
        changes.AddRange(placements);
        return changes;
    }

    private static List<ProposalChange> ParseParameterValues(JsonObject args)
    {
        var list = Args.RequiredArray(args, "elements", "arguments");
        var changes = new List<ProposalChange>();
        for (var i = 0; i < list.Count; i++)
        {
            var path = $"elements[{i}]";
            var item = Args.ObjectAt(list, i, "elements");
            var id = Args.RequiredLong(item, "elementId", path);
            // The add-in replaces this with the real label from the model before the designer sees it.
            var label = Args.OptionalString(item, "label", path) ?? $"Element {id}";
            var values = Args.RequiredStringMap(item, "values", path)
                .Select(v => new ParameterValue(v.Name, v.Value))
                .ToList();
            changes.Add(new SetParametersChange(id, label, values));
        }
        return changes;
    }
}
