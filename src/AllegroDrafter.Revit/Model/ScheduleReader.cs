using AllegroDrafter.Core.Proposals;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace AllegroDrafter.Revit.Model;

/// <summary>Read-only queries behind list_schedules, list_elements and read_legend.</summary>
internal static class ScheduleReader
{
    private const int MaxRows = 600;

    private static readonly Dictionary<BuiltInCategory, string[]> DefaultParameters = new()
    {
        [BuiltInCategory.OST_Doors] = new[] { "Mark", "Type Mark", "Width", "Height", "Comments" },
        [BuiltInCategory.OST_Windows] = new[] { "Mark", "Type Mark", "Width", "Height", "Sill Height", "Comments" },
        [BuiltInCategory.OST_Rooms] = new[] { "Number", "Name", "Area" },
    };

    public static object Schedules(Document doc)
    {
        var placement = RevitLookup.PlacementMap(doc);
        return new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
            .Where(RevitLookup.IsPlaceable)
            .OrderBy(s => s.Name)
            .Select(s =>
            {
                var definition = s.Definition;
                return new
                {
                    name = s.Name,
                    category = RevitLookup.CategoryLabel(doc, definition.CategoryId),
                    isKeySchedule = definition.IsKeySchedule ? true : (bool?)null,
                    fields = definition.GetFieldOrder()
                        .Select(id => definition.GetField(id))
                        .Select(f => f.IsHidden ? $"{f.GetName()} (hidden)" : f.GetName())
                        .ToList(),
                    template = s.ViewTemplateId == ElementId.InvalidElementId ? null : doc.GetElement(s.ViewTemplateId)?.Name,
                    onSheets = placement.TryGetValue(s.Id, out var sheets) ? sheets : null,
                };
            })
            .ToList();
    }

    public static object Elements(Document doc, string category, string? level, IReadOnlyList<string>? parameters, bool includeParameterList, IReadOnlyList<string> finishParameters)
    {
        if (!ParameterWriter.Categories.TryGetValue(category, out var bic))
            throw new ToolArgumentException($"category must be one of: {string.Join(", ", ParameterWriter.Categories.Keys)}.");

        var names = parameters is { Count: > 0 }
            ? parameters.ToList()
            : DefaultParameters[bic].Concat(bic == BuiltInCategory.OST_Rooms ? finishParameters : Array.Empty<string>()).Distinct().ToList();

        var elements = new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType().ToElements()
            .Where(e => level is null || LevelName(doc, e) == level)
            .OrderBy(e => LevelElevation(doc, e))
            .ThenBy(e => RevitLookup.ElementLabel(e), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = elements.Take(MaxRows).Select(e =>
        {
            var values = new Dictionary<string, string?>();
            foreach (var name in names)
            {
                var instance = e.LookupParameter(name);
                if (instance is not null)
                {
                    values[name] = ParameterWriter.Read(instance);
                    continue;
                }
                var typeParameter = doc.GetElement(e.GetTypeId())?.LookupParameter(name);
                values[name] = typeParameter is null ? "(no such parameter)" : $"{ParameterWriter.Read(typeParameter)} (type)";
            }

            var row = new Dictionary<string, object?>
            {
                ["elementId"] = e.Id.Value,
                ["label"] = RevitLookup.ElementLabel(e),
                ["level"] = LevelName(doc, e),
                ["values"] = values,
            };
            if (e is FamilyInstance fi && bic == BuiltInCategory.OST_Doors)
            {
                row["fromRoom"] = RoomRef(fi.FromRoom);
                row["toRoom"] = RoomRef(fi.ToRoom);
            }
            if (e is FamilyInstance window && bic == BuiltInCategory.OST_Windows)
            {
                row["room"] = RoomRef(window.ToRoom ?? window.FromRoom);
            }
            if (e is Room room && (room.Location is null || room.Area <= 0))
            {
                row["state"] = room.Location is null ? "unplaced" : "not enclosed";
            }
            return row;
        }).ToList();

        object? parameterList = null;
        if (includeParameterList)
        {
            parameterList = elements.Take(25)
                .SelectMany(e => e.Parameters.Cast<Parameter>())
                .GroupBy(p => p.Definition.Name)
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    name = g.Key,
                    storage = g.First().StorageType.ToString(),
                    readOnly = g.All(p => p.IsReadOnly) ? true : (bool?)null,
                })
                .ToList();
        }

        return new
        {
            category,
            count = elements.Count,
            truncated = elements.Count > MaxRows ? true : (bool?)null,
            parameterList,
            elements = rows,
        };
    }

    public static object ReadLegend(Document doc, string? name)
    {
        var candidates = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
            .Where(v => !v.IsTemplate && v.ViewType is ViewType.Legend or ViewType.DraftingView)
            .ToList();

        if (string.IsNullOrWhiteSpace(name))
        {
            return new
            {
                legends = candidates.Where(v => v.ViewType == ViewType.Legend).Select(v => v.Name).OrderBy(n => n).ToList(),
                draftingViews = candidates.Where(v => v.ViewType == ViewType.DraftingView).Select(v => v.Name).OrderBy(n => n).ToList(),
            };
        }

        var view = candidates.FirstOrDefault(v => v.Name == name);
        if (view is null)
        {
            var closest = Core.Names.ClosestMatch(name, candidates.Select(v => v.Name));
            throw new ToolArgumentException($"No legend or drafting view named \"{name}\".{(closest is null ? "" : $" Did you mean \"{closest}\"?")} Call read_legend with no name to list them.");
        }

        var items = new List<(double X, double Y, object Item)>();

        foreach (var note in new FilteredElementCollector(doc, view.Id).OfClass(typeof(TextNote)).Cast<TextNote>())
        {
            items.Add((note.Coord.X, note.Coord.Y, new { kind = "text", text = note.Text.Trim() }));
        }

        foreach (var component in new FilteredElementCollector(doc, view.Id).OfCategory(BuiltInCategory.OST_LegendComponents).WhereElementIsNotElementType())
        {
            var (x, y) = Center(component, view);
            items.Add((x, y, new { kind = "legendComponent", type = component.get_Parameter(BuiltInParameter.LEGEND_COMPONENT)?.AsValueString() }));
        }

        foreach (var symbol in new FilteredElementCollector(doc, view.Id).OfCategory(BuiltInCategory.OST_GenericAnnotation).WhereElementIsNotElementType().OfType<FamilyInstance>())
        {
            var (x, y) = Center(symbol, view);
            var texts = symbol.Parameters.Cast<Parameter>()
                .Where(p => p.StorageType == StorageType.String && !string.IsNullOrWhiteSpace(p.AsString()))
                .ToDictionary(p => p.Definition.Name, p => p.AsString());
            items.Add((x, y, new { kind = "symbol", type = RevitLookup.TypeName(symbol.Symbol), values = texts }));
        }

        // Reading order: top to bottom, then left to right (rows within ~1/8").
        var ordered = items
            .OrderByDescending(i => Math.Round(i.Y * 96))
            .ThenBy(i => i.X)
            .Take(MaxRows)
            .Select(i => i.Item)
            .ToList();

        return new { name = view.Name, viewType = view.ViewType.ToString(), count = items.Count, items = ordered };
    }

    private static (double X, double Y) Center(Element element, View view)
    {
        var box = element.get_BoundingBox(view);
        return box is null ? (0, 0) : ((box.Min.X + box.Max.X) / 2, (box.Min.Y + box.Max.Y) / 2);
    }

    private static string? RoomRef(Room? room) => room is null ? null : $"{room.Number} {RevitLookup.RoomName(room)}".Trim();

    private static Level? LevelOf(Document doc, Element e) => e switch
    {
        Room r => r.Level,
        _ => doc.GetElement(e.LevelId) as Level,
    };

    private static string? LevelName(Document doc, Element e) => LevelOf(doc, e)?.Name;

    private static double LevelElevation(Document doc, Element e) => LevelOf(doc, e)?.Elevation ?? double.MaxValue;
}
