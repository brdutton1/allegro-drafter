using AllegroDrafter.Core;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace AllegroDrafter.Revit.Model;

/// <summary>Finds model elements by the exact names the AI uses.</summary>
internal static class RevitLookup
{
    private static readonly HashSet<ViewType> NonPlaceable = new()
    {
        ViewType.DrawingSheet, ViewType.ProjectBrowser, ViewType.SystemBrowser,
        ViewType.Internal, ViewType.Undefined, ViewType.Report, ViewType.CostReport,
        ViewType.LoadsReport, ViewType.PresureLossReport, ViewType.PanelSchedule,
        ViewType.ColumnSchedule,
    };

    public static string TypeName(ElementType type) => Names.FamilyType(type.FamilyName, type.Name);

    public static IEnumerable<View> PlaceableViews(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(IsPlaceable);

    public static bool IsPlaceable(View view)
    {
        if (view.IsTemplate || NonPlaceable.Contains(view.ViewType)) return false;
        if (view is ViewSchedule schedule && (schedule.IsTitleblockRevisionSchedule || schedule.IsInternalKeynoteSchedule)) return false;
        return true;
    }

    /// <summary>Views that can sit on more than one sheet.</summary>
    public static bool IsMultiSheet(View view) => view is ViewSchedule || view.ViewType == ViewType.Legend;

    public static FamilySymbol? FindTitleBlock(Document doc, string name) =>
        new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .WhereElementIsElementType()
            .OfType<FamilySymbol>()
            .FirstOrDefault(s => TypeName(s) == name);

    public static ViewSheet? FindSheet(Document doc, string number) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .FirstOrDefault(s => string.Equals(s.SheetNumber, number, StringComparison.OrdinalIgnoreCase));

    public static View? FindTemplate(Document doc, string name) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .FirstOrDefault(v => v.IsTemplate && v.Name == name);

    /// <summary>Exact-name view lookup. Returns null and an error the AI can act on if missing or ambiguous.</summary>
    public static View? ResolveView(Document doc, string name, string? viewType, out string? error)
    {
        error = null;
        ViewType? wanted = null;
        if (!string.IsNullOrWhiteSpace(viewType))
        {
            if (!Enum.TryParse<ViewType>(viewType, ignoreCase: true, out var parsed))
            {
                error = $"Unknown viewType '{viewType}'. Use the viewType value from list_views (e.g. FloorPlan, CeilingPlan, Elevation, Section, Legend, Schedule).";
                return null;
            }
            wanted = parsed;
        }

        var all = PlaceableViews(doc).ToList();
        var matches = all.Where(v => v.Name == name && (wanted is null || v.ViewType == wanted)).ToList();
        if (matches.Count == 1) return matches[0];

        if (matches.Count > 1)
        {
            var types = string.Join(", ", matches.Select(v => v.ViewType.ToString()).Distinct());
            error = $"More than one view is named \"{name}\" ({types}). Add viewType to say which.";
            return null;
        }

        var closest = Names.ClosestMatch(name, all.Select(v => v.Name).Distinct());
        error = wanted is null
            ? $"No view named \"{name}\" in this model."
            : $"No {wanted} view named \"{name}\" in this model.";
        if (closest is not null) error += $" Did you mean \"{closest}\"?";
        error += " Use list_views for exact names.";
        return null;
    }

    /// <summary>Element id to the sheet numbers it sits on (viewports and schedule instances).</summary>
    public static Dictionary<ElementId, List<string>> PlacementMap(Document doc)
    {
        var map = new Dictionary<ElementId, List<string>>();
        void Add(ElementId viewId, string sheetNumber)
        {
            if (!map.TryGetValue(viewId, out var list)) map[viewId] = list = new List<string>();
            if (!list.Contains(sheetNumber)) list.Add(sheetNumber);
        }

        var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .ToDictionary(s => s.Id, s => s.SheetNumber);

        foreach (var viewport in new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>())
        {
            if (sheets.TryGetValue(viewport.SheetId, out var number)) Add(viewport.ViewId, number);
        }
        foreach (var instance in new FilteredElementCollector(doc).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>())
        {
            if (instance.IsTitleblockRevisionSchedule) continue;
            if (sheets.TryGetValue(instance.OwnerViewId, out var number)) Add(instance.ScheduleId, number);
        }
        return map;
    }

    public static ViewSchedule? FindSchedule(Document doc, string name) =>
        new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
            .FirstOrDefault(s => !s.IsTemplate && s.Name == name);

    public static bool ScheduleNameTaken(Document doc, string name) =>
        new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
            .Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    public static string RoomName(Room room) => room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";

    /// <summary>What the designer sees in the approval pane, e.g. "Door 101 [Single-Flush: 36" x 80"]".</summary>
    public static string ElementLabel(Element element)
    {
        if (element is Room room) return $"Room {room.Number} {RoomName(room)}".TrimEnd();

        var kind = element.Category?.BuiltInCategory switch
        {
            BuiltInCategory.OST_Doors => "Door",
            BuiltInCategory.OST_Windows => "Window",
            _ => element.Category?.Name ?? "Element",
        };
        var mark = element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
        var type = element.Document.GetElement(element.GetTypeId()) is ElementType t ? $" [{TypeName(t)}]" : "";
        return $"{kind} {(string.IsNullOrWhiteSpace(mark) ? $"(no mark, id {element.Id.Value})" : mark)}{type}";
    }

    public static string CategoryLabel(Document doc, ElementId categoryId)
    {
        if (categoryId == ElementId.InvalidElementId) return "Multi-Category";
        try
        {
            return Category.GetCategory(doc, categoryId)?.Name ?? "Unknown";
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            return "Unknown";
        }
    }
}
