using System.Text.Json.Nodes;
using AllegroDrafter.Core.Drafting;
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
            ToolNames.ProposeTags => ParseTags(args),
            ToolNames.ProposeCallouts => ParseCallouts(args),
            ToolNames.ProposeNotes => ParseNotes(args),
            ToolNames.ProposeDimensions => ParseDimensions(args),
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

    private static List<ProposalChange> ParseTags(JsonObject args)
    {
        var list = Args.RequiredArray(args, "views", "arguments");
        var changes = new List<ProposalChange>();
        for (var i = 0; i < list.Count; i++)
        {
            var path = $"views[{i}]";
            var item = Args.ObjectAt(list, i, "views");
            var category = Args.RequiredString(item, "category", path).ToLowerInvariant();
            if (!ToolNames.TaggableCategories.Contains(category))
                throw new ToolArgumentException($"{path}.category must be one of: {string.Join(", ", ToolNames.TaggableCategories)}.");
            changes.Add(new TagViewChange(
                Args.RequiredString(item, "viewName", path),
                category,
                Args.OptionalString(item, "tagType", path),
                Args.OptionalString(item, "viewType", path),
                Args.OptionalBool(item, "leader", path),
                Args.OptionalBool(item, "untaggedOnly", path, fallback: true)));
        }
        return changes;
    }

    private static List<ProposalChange> ParseCallouts(JsonObject args)
    {
        var list = Args.RequiredArray(args, "callouts", "arguments");
        var changes = new List<ProposalChange>();
        var placements = new List<ProposalChange>();
        for (var i = 0; i < list.Count; i++)
        {
            var path = $"callouts[{i}]";
            var item = Args.ObjectAt(list, i, "callouts");
            var name = Args.OptionalString(item, "name", path);
            var vft = Args.OptionalString(item, "viewFamilyType", path);
            var reference = Args.OptionalString(item, "referenceView", path);

            if (reference is not null && (name is not null || vft is not null))
                throw new ToolArgumentException($"{path}: a reference callout (referenceView) points at an existing view, so it takes no name or viewFamilyType.");
            if (reference is null && (name is null || vft is null))
                throw new ToolArgumentException($"{path}: give name + viewFamilyType for a new detail callout, or referenceView to point at an existing detail.");

            var room = Args.OptionalString(item, "aroundRoomNumber", path);
            var ids = Args.OptionalLongArray(item, "aroundElementIds", path);
            var minX = Args.OptionalDouble(item, "minX", path);
            var minY = Args.OptionalDouble(item, "minY", path);
            var maxX = Args.OptionalDouble(item, "maxX", path);
            var maxY = Args.OptionalDouble(item, "maxY", path);
            var hasBox = minX.HasValue || minY.HasValue || maxX.HasValue || maxY.HasValue;
            var modes = (room is not null ? 1 : 0) + (ids is { Count: > 0 } ? 1 : 0) + (hasBox ? 1 : 0);
            if (modes != 1)
                throw new ToolArgumentException($"{path}: give exactly one of aroundRoomNumber, aroundElementIds, or minX/minY/maxX/maxY.");
            if (hasBox && (minX is null || minY is null || maxX is null || maxY is null || minX >= maxX || minY >= maxY))
                throw new ToolArgumentException($"{path}: minX/minY/maxX/maxY must all be given, with min < max.");
            var margin = Args.OptionalDouble(item, "marginFeet", path) ?? 1.0;
            if (margin < 0) throw new ToolArgumentException($"{path}.marginFeet can't be negative.");

            var scaleValue = Args.OptionalDouble(item, "scale", path);
            int? scale = null;
            if (scaleValue is not null)
            {
                if (scaleValue < 1 || scaleValue != Math.Floor(scaleValue.Value))
                    throw new ToolArgumentException($"{path}.scale must be a whole number like 24 (for 1/2\" = 1'-0\").");
                scale = (int)scaleValue;
            }
            var template = Args.OptionalString(item, "templateName", path);
            if (reference is not null && (scale is not null || template is not null))
                throw new ToolArgumentException($"{path}: scale and templateName only apply to new detail callouts.");

            changes.Add(new CalloutChange(
                Args.RequiredString(item, "parentView", path),
                new CalloutArea(room, ids, minX, minY, maxX, maxY, margin),
                Args.OptionalString(item, "parentViewType", path),
                name, vft, reference, template, scale));

            var sheet = Args.OptionalString(item, "sheetNumber", path);
            if (sheet is not null)
            {
                if (reference is not null) throw new ToolArgumentException($"{path}.sheetNumber: a reference callout has no new view to place.");
                var x = Args.OptionalDouble(item, "xFeet", path);
                var y = Args.OptionalDouble(item, "yFeet", path);
                if (x.HasValue != y.HasValue) throw new ToolArgumentException($"{path}: give both xFeet and yFeet, or neither to auto-arrange.");
                placements.Add(new PlaceViewChange(sheet, name!, null, x, y));
            }
        }
        changes.AddRange(placements);
        return changes;
    }

    private static List<ProposalChange> ParseNotes(JsonObject args)
    {
        var list = Args.RequiredArray(args, "notes", "arguments");
        var changes = new List<ProposalChange>();
        for (var i = 0; i < list.Count; i++)
        {
            var path = $"notes[{i}]";
            var item = Args.ObjectAt(list, i, "notes");
            var x = Args.OptionalDouble(item, "xFeet", path);
            var y = Args.OptionalDouble(item, "yFeet", path);
            if (x is null || y is null) throw new ToolArgumentException($"{path}: xFeet and yFeet are required (view coordinates in feet).");
            changes.Add(new TextNoteChange(
                Args.RequiredString(item, "viewName", path),
                Args.RequiredString(item, "text", path),
                Args.RequiredString(item, "textType", path),
                x.Value, y.Value,
                Args.OptionalString(item, "viewType", path),
                Args.OptionalLong(item, "leaderToElementId", path)));
        }
        return changes;
    }

    private static List<ProposalChange> ParseDimensions(JsonObject args)
    {
        var list = Args.RequiredArray(args, "views", "arguments");
        var changes = new List<ProposalChange>();
        for (var i = 0; i < list.Count; i++)
        {
            var path = $"views[{i}]";
            var item = Args.ObjectAt(list, i, "views");
            var strategies = Args.OptionalStringArray(item, "strategies", path)?.Select(s => s.ToLowerInvariant()).Distinct().ToList()
                ?? DimensionPlanner.Strategies.ToList();
            if (strategies.Count == 0) throw new ToolArgumentException($"{path}.strategies can't be empty.");
            var bad = strategies.Where(s => !DimensionPlanner.Strategies.Contains(s)).ToList();
            if (bad.Count > 0) throw new ToolArgumentException($"{path}.strategies: unknown '{string.Join("', '", bad)}'. Use {string.Join(", ", DimensionPlanner.Strategies)}.");
            var offset = Args.OptionalDouble(item, "offsetFeet", path) ?? 4.0;
            var step = Args.OptionalDouble(item, "stepFeet", path) ?? 2.0;
            if (offset <= 0 || step <= 0) throw new ToolArgumentException($"{path}: offsetFeet and stepFeet must be positive.");
            changes.Add(new DimensionViewChange(
                Args.RequiredString(item, "viewName", path),
                strategies,
                Args.OptionalString(item, "dimensionType", path),
                Args.OptionalString(item, "viewType", path),
                offset, step,
                Args.OptionalBool(item, "allowExisting", path)));
        }
        return changes;
    }
}
