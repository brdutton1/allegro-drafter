using AllegroDrafter.Core;
using AllegroDrafter.Core.Checks;
using AllegroDrafter.Core.Proposals;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace AllegroDrafter.Revit.Model;

/// <summary>
/// Checks and applies the fix changes (Type Marks, shared parameters, schedule
/// sorting, room cleanup). Check runs at propose time and again right before
/// applying; Apply only runs inside the approved proposal's transaction.
/// </summary>
internal static class FixEditor
{
    private static readonly Dictionary<string, BuiltInCategory> OpeningCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["doors"] = BuiltInCategory.OST_Doors,
        ["windows"] = BuiltInCategory.OST_Windows,
    };

    public const string SharedParameterGroup = "Allegro Drafter";

    // ------------------------------------------------------------ Type Marks

    public static FamilySymbol? FindType(Document doc, string category, string typeName) =>
        OpeningCategories.TryGetValue(category, out var bic)
            ? new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsElementType().OfType<FamilySymbol>()
                .FirstOrDefault(s => RevitLookup.TypeName(s) == typeName)
            : null;

    public static List<string> CheckTypeMarks(Document doc, IReadOnlyList<SetTypeMarkChange> marks, AuditSettings audit)
    {
        var errors = new List<string>();
        var proposed = new Dictionary<ElementId, SetTypeMarkChange>();
        foreach (var m in marks)
        {
            var symbol = FindType(doc, m.Category, m.TypeName);
            if (symbol is null)
            {
                var names = OpeningCategories.TryGetValue(m.Category, out var bic)
                    ? new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsElementType().OfType<FamilySymbol>().Select(RevitLookup.TypeName).ToList()
                    : new List<string>();
                var closest = Names.ClosestMatch(m.TypeName, names);
                errors.Add($"No {m.Category} type \"{m.TypeName}\" in this model.{(closest is null ? "" : $" Did you mean \"{closest}\"?")} Use the exact name from audit_set.");
                continue;
            }
            proposed[symbol.Id] = m;

            var parameter = symbol.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK);
            if (parameter is null || parameter.IsReadOnly)
            {
                errors.Add($"\"{m.TypeName}\" has no editable Type Mark.");
                continue;
            }

            var code = SizeCode.Parse(m.TypeMark);
            if (code is null) continue; // the standards check already reported it
            var w = FactsReader.Width(symbol);
            var h = FactsReader.Height(symbol);
            if (w is null || h is null)
            {
                errors.Add($"Can't read Width/Height on \"{m.TypeName}\", so \"{m.TypeMark}\" can't be checked against its size. Set it in Revit by hand.");
                continue;
            }
            var (cw, ch, off) = code.Closest(w.Value, h.Value);
            if (off > audit.TypeMarkToleranceInches + 1e-9)
                errors.Add($"\"{m.TypeMark}\" means {SizeCode.FeetInches(cw)} x {SizeCode.FeetInches(ch)}, but \"{m.TypeName}\" is {SizeCode.FeetInches(w.Value)} x {SizeCode.FeetInches(h.Value)}. Use the size the type really is.");
        }

        // After the change, no two placed types in a category may share a mark.
        foreach (var (category, bic) in OpeningCategories)
        {
            if (!marks.Any(m => m.Category == category)) continue;
            var placedTypes = new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType().OfType<FamilyInstance>()
                .Select(i => i.Symbol).GroupBy(s => s.Id).Select(g => g.First()).ToList();
            var finals = placedTypes
                .Select(s => (Symbol: s, Mark: proposed.TryGetValue(s.Id, out var p) ? p.TypeMark : s.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK)?.AsString()))
                .Where(x => !string.IsNullOrWhiteSpace(x.Mark))
                .GroupBy(x => Normalize(x.Mark!))
                .Where(g => g.Count() > 1 && g.Any(x => proposed.ContainsKey(x.Symbol.Id)));
            foreach (var g in finals)
            {
                errors.Add($"Type Mark \"{g.Key}\" would be on {g.Count()} {category} types: {string.Join(", ", g.Select(x => $"\"{RevitLookup.TypeName(x.Symbol)}\""))}. The schedule would merge them. Give each its own mark.");
            }
        }
        return errors;
    }

    public static SetTypeMarkChange Annotate(Document doc, SetTypeMarkChange m)
    {
        var symbol = FindType(doc, m.Category, m.TypeName);
        if (symbol is null) return m;
        var w = FactsReader.Width(symbol);
        var h = FactsReader.Height(symbol);
        var count = new FilteredElementCollector(doc).OfCategory(OpeningCategories[m.Category]).WhereElementIsNotElementType()
            .OfType<FamilyInstance>().Count(i => i.Symbol.Id == symbol.Id);
        return m with
        {
            CurrentMark = symbol.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK)?.AsString(),
            Size = w is null || h is null ? null : $"{SizeCode.FeetInches(w.Value)} x {SizeCode.FeetInches(h.Value)}",
            InstanceCount = count,
        };
    }

    public static void ApplyTypeMark(Document doc, SetTypeMarkChange m)
    {
        var symbol = FindType(doc, m.Category, m.TypeName) ?? throw new InvalidOperationException($"Type \"{m.TypeName}\" not found.");
        var parameter = symbol.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK) ?? throw new InvalidOperationException($"\"{m.TypeName}\" has no Type Mark.");
        if (!parameter.Set(m.TypeMark)) throw new InvalidOperationException($"Revit refused Type Mark \"{m.TypeMark}\" on \"{m.TypeName}\".");
    }

    // ------------------------------------------------------ Shared parameters

    public static List<string> CheckParameter(Document doc, CreateSharedParameterChange p)
    {
        var errors = new List<string>();
        if (BoundNames(doc).Contains(p.Name))
        {
            errors.Add($"The project already has a parameter named \"{p.Name}\". Use it (set values with propose_parameter_values) instead of adding another.");
            return errors;
        }
        foreach (var category in p.Categories)
        {
            if (!ParameterWriter.Categories.TryGetValue(category, out var bic)) { errors.Add($"Unknown category '{category}'."); continue; }
            var cat = Category.GetCategory(doc, bic);
            if (cat is null || !cat.AllowsBoundParameters) { errors.Add($"{category} can't take project parameters."); continue; }

            var sample = new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType().FirstOrDefault(e => e.LookupParameter(p.Name) is not null
                || doc.GetElement(e.GetTypeId())?.LookupParameter(p.Name) is not null);
            if (sample is not null)
                errors.Add($"Some {category} families already have a parameter named \"{p.Name}\" (e.g. {RevitLookup.ElementLabel(sample)}). A second one with the same name would confuse schedules and tags. Rename one in the family, or use the existing one.");
        }
        return errors;
    }

    public static void ApplyParameter(Document doc, CreateSharedParameterChange p)
    {
        var app = doc.Application;
        var path = Path.Combine(new AppPaths().Root, "AllegroDrafterSharedParameters.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path)) File.WriteAllText(path, "");

        var original = app.SharedParametersFilename;
        try
        {
            app.SharedParametersFilename = path;
            var file = app.OpenSharedParameterFile() ?? throw new InvalidOperationException($"Revit couldn't open the shared parameter file {path}.");
            var group = file.Groups.get_Item(SharedParameterGroup) ?? file.Groups.Create(SharedParameterGroup);
            var spec = p.DataType == "yesno" ? SpecTypeId.Boolean.YesNo : SpecTypeId.String.Text;
            var definition = group.Definitions.get_Item(p.Name) as ExternalDefinition
                ?? group.Definitions.Create(new ExternalDefinitionCreationOptions(p.Name, spec)
                {
                    Visible = true,
                    Description = "Added by Allegro Drafter for schedules and tags.",
                }) as ExternalDefinition
                ?? throw new InvalidOperationException($"Couldn't create \"{p.Name}\" in the shared parameter file.");
            if (definition.GetDataType() != spec)
                throw new InvalidOperationException($"\"{p.Name}\" already exists in {path} with a different type. Rename it there or pick another name.");

            var categories = new CategorySet();
            foreach (var c in p.Categories) categories.Insert(Category.GetCategory(doc, ParameterWriter.Categories[c]));
            var binding = app.Create.NewInstanceBinding(categories);
            if (!doc.ParameterBindings.Insert(definition, binding, GroupTypeId.IdentityData))
                throw new InvalidOperationException($"Revit refused to add \"{p.Name}\" to the project.");
        }
        finally
        {
            app.SharedParametersFilename = original;
        }
    }

    private static HashSet<string> BoundNames(Document doc)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var it = doc.ParameterBindings.ForwardIterator();
        while (it.MoveNext())
        {
            if (it.Key is Definition d) names.Add(d.Name);
        }
        return names;
    }

    // --------------------------------------------------------- Schedule sort

    public static List<string> CheckSort(Document doc, ScheduleSortChange sort)
    {
        var schedule = RevitLookup.FindSchedule(doc, sort.Schedule);
        if (schedule is null) return new() { $"No schedule named \"{sort.Schedule}\". Use list_schedules for exact names." };
        var columns = Columns(schedule.Definition);
        return sort.SortBy
            .Where(f => !columns.Contains(f, StringComparer.Ordinal))
            .Select(f => $"\"{sort.Schedule}\" has no column \"{f}\" to sort by. Add it first with propose_schedule_fields. Its columns: {string.Join(", ", columns)}.")
            .ToList();
    }

    public static void ApplySort(Document doc, ScheduleSortChange sort)
    {
        var schedule = RevitLookup.FindSchedule(doc, sort.Schedule) ?? throw new InvalidOperationException($"Schedule \"{sort.Schedule}\" not found.");
        var d = schedule.Definition;
        d.ClearSortGroupFields();
        foreach (var name in sort.SortBy)
        {
            var id = d.GetFieldOrder().First(f => d.GetField(f).GetName() == name);
            d.AddSortGroupField(new ScheduleSortGroupField(id, ScheduleSortOrder.Ascending));
        }
        if (sort.Itemized is bool itemized) d.IsItemized = itemized;
    }

    private static List<string> Columns(ScheduleDefinition d) => d.GetFieldOrder().Select(id => d.GetField(id).GetName()).ToList();

    // ---------------------------------------------------------- Room cleanup

    private static bool IsLeftover(Room r) => r.Location is null || r.Area <= 0;

    private static List<Room> Leftovers(Document doc) =>
        new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().OfType<Room>().Where(IsLeftover).ToList();

    public static List<string> CheckRooms(Document doc, DeleteRoomsChange change)
    {
        var errors = new List<string>();
        if (change.RoomIds.Count == 0)
        {
            if (change.AllUnplaced && Leftovers(doc).Count == 0) errors.Add("There are no unplaced or unenclosed rooms to delete.");
            return errors;
        }
        foreach (var id in change.RoomIds)
        {
            var element = doc.GetElement(new ElementId(id));
            if (element is not Room room) errors.Add($"Element {id} isn't a room.");
            else if (!IsLeftover(room)) errors.Add($"Room {RevitLookup.ElementLabel(room)} is placed with {Math.Round(room.Area, 1)} sf. Only unplaced or unenclosed rooms can be deleted here.");
        }
        return errors;
    }

    public static DeleteRoomsChange Annotate(Document doc, DeleteRoomsChange change)
    {
        var rooms = change.RoomIds.Count == 0 && change.AllUnplaced
            ? Leftovers(doc)
            : change.RoomIds.Select(id => doc.GetElement(new ElementId(id))).OfType<Room>().ToList();
        var groups = rooms.GroupBy(r => string.IsNullOrWhiteSpace(RevitLookup.RoomName(r)) ? "(no name)" : RevitLookup.RoomName(r))
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).ToList();
        var summary = string.Join(", ", groups.Take(10).Select(g => g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key));
        if (groups.Count > 10) summary += $", +{groups.Skip(10).Sum(g => g.Count())} more";
        return change with { RoomIds = rooms.Select(r => r.Id.Value).OrderBy(i => i).ToList(), Summary = summary };
    }

    public static int ApplyRooms(Document doc, DeleteRoomsChange change)
    {
        var ids = change.RoomIds.Select(id => new ElementId(id)).Where(id => doc.GetElement(id) is Room).ToList();
        if (ids.Count > 0) doc.Delete(ids);
        return ids.Count;
    }

    private static string Normalize(string s) => System.Text.RegularExpressions.Regex.Replace(s.Trim().ToUpperInvariant(), @"\s+", " ");
}
