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
            ToolNames.ProposeSchedules => ParseSchedules(args),
            ToolNames.ProposeParameterValues => ParseParameterValues(args),
            ToolNames.ProposeTags => ParseTags(args),
            ToolNames.ProposeCallouts => ParseCallouts(args),
            ToolNames.ProposeNotes => ParseNotes(args),
            ToolNames.ProposeDimensions => ParseDimensions(args),
            ToolNames.ProposeScheduleFields => ParseScheduleFields(args),
            ToolNames.ProposeTypeMarks => FixRequestParser.TypeMarks(args),
            ToolNames.ProposeParameters => FixRequestParser.Parameters(args),
            ToolNames.ProposeScheduleSort => FixRequestParser.ScheduleSort(args),
            ToolNames.ProposeRoomCleanup => FixRequestParser.RoomCleanup(args),
            ToolNames.ProposeWallFunction => FixRequestParser.WallFunctions(args),
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
            if (item.ContainsKey("templateName") || item.ContainsKey("category"))
                throw new ToolArgumentException($"{path}: schedules are made by copying an office schedule (sourceSchedule). The drafter doesn't apply view templates (office rule).");
            if (source is null)
                throw new ToolArgumentException($"{path}: give sourceSchedule, the office schedule to copy.");

            changes.Add(new CreateScheduleChange(name, source));

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
            if (item.ContainsKey("templateName"))
                throw new ToolArgumentException($"{path}.templateName: the drafter doesn't apply view templates (office rule). Leave it out; the designer sets templates.");
            if (reference is not null && scale is not null)
                throw new ToolArgumentException($"{path}: scale only applies to new detail callouts.");
            string? template = null;

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
                ?? PlanDimensioning.AllStrategies.ToList();
            if (strategies.Count == 0) throw new ToolArgumentException($"{path}.strategies can't be empty.");
            var bad = strategies.Where(s => !PlanDimensioning.AllStrategies.Contains(s)).ToList();
            if (bad.Count > 0) throw new ToolArgumentException($"{path}.strategies: unknown '{string.Join("', '", bad)}'. Use {string.Join(", ", PlanDimensioning.AllStrategies)}.");
            // Spacing: model feet if given, otherwise office paper spacing (3/8"), scaled to the view in Revit.
            var offsetFeet = Args.OptionalDouble(item, "offsetFeet", path);
            var stepFeet = Args.OptionalDouble(item, "stepFeet", path);
            var offsetPaper = Args.OptionalDouble(item, "offsetPaperInches", path);
            var stepPaper = Args.OptionalDouble(item, "stepPaperInches", path);
            if (offsetFeet is not null && offsetPaper is not null) throw new ToolArgumentException($"{path}: give offsetFeet or offsetPaperInches, not both.");
            if (stepFeet is not null && stepPaper is not null) throw new ToolArgumentException($"{path}: give stepFeet or stepPaperInches, not both.");
            if (offsetFeet is null) offsetPaper ??= DimensionViewChange.OfficeSpacingPaperInches;
            if (stepFeet is null) stepPaper ??= DimensionViewChange.OfficeSpacingPaperInches;
            var offset = offsetFeet ?? 4.0;
            var step = stepFeet ?? 2.0;
            if (offset <= 0 || step <= 0 || offsetPaper <= 0 || stepPaper <= 0) throw new ToolArgumentException($"{path}: offsetFeet and stepFeet must be positive.");
            var basis = Args.OptionalString(item, "interiorBasis", path)?.ToLowerInvariant();
            if (basis is not null && !PlanDimensioning.Bases.Contains(basis))
                throw new ToolArgumentException($"{path}.interiorBasis must be one of: {string.Join(", ", PlanDimensioning.Bases)}.");
            var sides = Args.OptionalStringArray(item, "sides", path)?.Select(s => s.Trim().ToLowerInvariant()).Distinct().ToList();
            if (sides is { Count: 0 }) throw new ToolArgumentException($"{path}.sides can't be empty; leave it out for every side.");
            var badSides = sides?.Where(s => !PlanDimensioning.SideNames.Contains(s)).ToList() ?? new List<string>();
            if (badSides.Count > 0) throw new ToolArgumentException($"{path}.sides: unknown '{string.Join("', '", badSides)}'. Use {string.Join(", ", PlanDimensioning.SideNames)}.");
            changes.Add(new DimensionViewChange(
                Args.RequiredString(item, "viewName", path),
                strategies,
                Args.OptionalString(item, "dimensionType", path),
                Args.OptionalString(item, "viewType", path),
                offset, step,
                Args.OptionalBool(item, "allowExisting", path),
                InteriorBasis: basis,
                Sides: sides,
                OffsetPaperInches: offsetPaper,
                StepPaperInches: stepPaper));
        }
        return changes;
    }

    private static List<ProposalChange> ParseScheduleFields(JsonObject args)
    {
        var list = Args.RequiredArray(args, "schedules", "arguments");
        var changes = new List<ProposalChange>();
        for (var i = 0; i < list.Count; i++)
        {
            var path = $"schedules[{i}]";
            var item = Args.ObjectAt(list, i, "schedules");
            var add = new List<FieldToAdd>();
            var addArray = Args.OptionalArray(item, "add", path);
            if (addArray is not null)
            {
                for (var j = 0; j < addArray.Count; j++)
                {
                    var field = Args.ObjectAt(addArray, j, $"{path}.add");
                    var fieldPath = $"{path}.add[{j}]";
                    var after = Args.OptionalString(field, "after", fieldPath);
                    var before = Args.OptionalString(field, "before", fieldPath);
                    if (after is not null && before is not null)
                        throw new ToolArgumentException($"{fieldPath}: give after or before, not both.");
                    add.Add(new FieldToAdd(Args.RequiredString(field, "field", fieldPath), after, before));
                }
            }
            var remove = Args.OptionalStringArray(item, "remove", path)?.ToList() ?? new List<string>();
            if (add.Count == 0 && remove.Count == 0) throw new ToolArgumentException($"{path}: give fields to add and/or remove.");
            changes.Add(new ScheduleFieldsChange(Args.RequiredString(item, "schedule", path), add, remove));
        }
        return changes;
    }
}
