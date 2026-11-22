using AllegroDrafter.Core;
using AllegroDrafter.Core.Proposals;
using Autodesk.Revit.DB;

namespace AllegroDrafter.Revit.Model;

/// <summary>Adds and removes schedule columns, checked against what Revit can actually schedule.</summary>
internal static class ScheduleEditor
{
    public static List<string> Check(Document doc, ScheduleFieldsChange change)
    {
        var errors = new List<string>();
        var schedule = RevitLookup.FindSchedule(doc, change.Schedule);
        if (schedule is null)
        {
            errors.Add($"No schedule named \"{change.Schedule}\". Use list_schedules for exact names.");
            return errors;
        }

        var definition = schedule.Definition;
        var columns = Columns(definition);
        var available = definition.GetSchedulableFields().Select(f => f.GetName(doc)).Distinct().ToList();

        foreach (var name in change.Remove)
        {
            if (!columns.Remove(name))
                errors.Add($"\"{change.Schedule}\" has no column \"{name}\". Its columns: {string.Join(", ", Columns(definition))}.");
        }

        foreach (var add in change.Add)
        {
            if (columns.Contains(add.Field))
            {
                errors.Add($"\"{change.Schedule}\" already has \"{add.Field}\".");
                continue;
            }
            if (!available.Contains(add.Field))
            {
                var closest = Names.ClosestMatch(add.Field, available);
                errors.Add($"\"{add.Field}\" can't be scheduled in \"{change.Schedule}\".{(closest is null ? "" : $" Did you mean \"{closest}\"?")}");
                continue;
            }
            if (add.After is not null && !columns.Contains(add.After))
            {
                errors.Add($"Can't insert after \"{add.After}\": \"{change.Schedule}\" has no such column. Its columns: {string.Join(", ", columns)}.");
                continue;
            }
            columns.Insert(add.After is null ? columns.Count : columns.IndexOf(add.After) + 1, add.Field);
        }
        return errors;
    }

    public static void Apply(Document doc, ScheduleFieldsChange change)
    {
        var schedule = RevitLookup.FindSchedule(doc, change.Schedule) ?? throw new InvalidOperationException($"Schedule \"{change.Schedule}\" not found.");
        var definition = schedule.Definition;

        foreach (var name in change.Remove)
        {
            var id = definition.GetFieldOrder().First(f => definition.GetField(f).GetName() == name);
            definition.RemoveField(id);
        }

        foreach (var add in change.Add)
        {
            var field = definition.GetSchedulableFields().First(f => f.GetName(doc) == add.Field);
            if (add.After is null)
            {
                definition.AddField(field);
                continue;
            }
            var order = definition.GetFieldOrder();
            var index = order.ToList().FindIndex(f => definition.GetField(f).GetName() == add.After);
            definition.InsertField(field, index + 1);
        }
    }

    private static List<string> Columns(ScheduleDefinition definition) =>
        definition.GetFieldOrder().Select(id => definition.GetField(id).GetName()).ToList();
}
