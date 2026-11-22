using System.Text.Json.Nodes;
using AllegroDrafter.Core.Mcp;

namespace AllegroDrafter.Core.Proposals;

/// <summary>Parses the fix tools: propose_type_marks, propose_parameters, propose_schedule_sort, propose_room_cleanup.</summary>
internal static class FixRequestParser
{
    private static readonly string[] OpeningCategories = { "doors", "windows" };
    private static readonly string[] DataTypes = { "yesno", "text" };

    public static List<ProposalChange> TypeMarks(JsonObject args)
    {
        var list = Args.RequiredArray(args, "types", "arguments");
        var changes = new List<ProposalChange>();
        for (var i = 0; i < list.Count; i++)
        {
            var path = $"types[{i}]";
            var item = Args.ObjectAt(list, i, "types");
            var category = Args.RequiredString(item, "category", path).ToLowerInvariant();
            if (!OpeningCategories.Contains(category))
                throw new ToolArgumentException($"{path}.category must be doors or windows.");
            var mark = Args.RequiredString(item, "typeMark", path).Trim();
            var sizeOverride = Args.OptionalString(item, "sizeOverride", path)?.Trim();
            if (sizeOverride is { Length: 0 }) throw new ToolArgumentException($"{path}.sizeOverride can't be blank. Say why the designer wants this size, or leave it out.");
            changes.Add(new SetTypeMarkChange(
                category,
                Args.RequiredString(item, "typeName", path).Trim(),
                System.Text.RegularExpressions.Regex.Replace(mark.ToUpperInvariant(), @"\s+", " "),
                SizeOverride: sizeOverride));
        }
        return changes;
    }

    public static List<ProposalChange> Parameters(JsonObject args)
    {
        var list = Args.RequiredArray(args, "parameters", "arguments");
        var changes = new List<ProposalChange>();
        for (var i = 0; i < list.Count; i++)
        {
            var path = $"parameters[{i}]";
            var item = Args.ObjectAt(list, i, "parameters");
            var name = Args.RequiredString(item, "name", path).Trim();
            if (name.Length == 0) throw new ToolArgumentException($"{path}.name can't be blank.");
            var type = (Args.OptionalString(item, "type", path) ?? "yesno").ToLowerInvariant();
            if (!DataTypes.Contains(type)) throw new ToolArgumentException($"{path}.type must be yesno or text.");
            var categories = Args.OptionalStringArray(item, "categories", path)?.Select(c => c.ToLowerInvariant()).Distinct().ToList();
            if (categories is null || categories.Count == 0) throw new ToolArgumentException($"{path}.categories is required, e.g. [\"doors\",\"windows\"].");
            var bad = categories.Where(c => !ToolNames.ParameterCategories.Contains(c)).ToList();
            if (bad.Count > 0) throw new ToolArgumentException($"{path}.categories: '{string.Join("', '", bad)}' not allowed. Use {string.Join(", ", ToolNames.ParameterCategories)}.");
            changes.Add(new CreateSharedParameterChange(name, type, categories));
        }
        return changes;
    }

    public static List<ProposalChange> ScheduleSort(JsonObject args)
    {
        var list = Args.RequiredArray(args, "schedules", "arguments");
        var changes = new List<ProposalChange>();
        for (var i = 0; i < list.Count; i++)
        {
            var path = $"schedules[{i}]";
            var item = Args.ObjectAt(list, i, "schedules");
            var sortBy = Args.OptionalStringArray(item, "sortBy", path)?.Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            if (sortBy is null || sortBy.Count == 0) throw new ToolArgumentException($"{path}.sortBy needs at least one field, e.g. [\"Type Mark\"].");
            var repeated = sortBy.GroupBy(s => s, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (repeated.Count > 0) throw new ToolArgumentException($"{path}.sortBy lists \"{string.Join("\", \"", repeated)}\" more than once.");
            bool? itemized = item.ContainsKey("itemized") ? Args.OptionalBool(item, "itemized", path) : null;
            changes.Add(new ScheduleSortChange(Args.RequiredString(item, "schedule", path), sortBy, itemized));
        }
        return changes;
    }

    public static List<ProposalChange> RoomCleanup(JsonObject args)
    {
        var all = Args.OptionalBool(args, "allUnplaced", "arguments");
        var ids = Args.OptionalLongArray(args, "roomIds", "arguments")?.Distinct().ToList();
        if (all && ids is { Count: > 0 }) throw new ToolArgumentException("Give roomIds or allUnplaced, not both.");
        if (!all && ids is not { Count: > 0 }) throw new ToolArgumentException("Give roomIds (from audit_set or list_rooms), or allUnplaced: true.");
        return new List<ProposalChange> { new DeleteRoomsChange(ids ?? new List<long>(), all) };
    }
}
