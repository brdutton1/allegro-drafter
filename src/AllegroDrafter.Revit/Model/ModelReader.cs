using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;

namespace AllegroDrafter.Revit.Model;

/// <summary>Read-only queries behind the list_* tools. Output is compact on purpose (token cost).</summary>
internal static class ModelReader
{
    private const int MaxRows = 600;

    public static object ProjectInfo(UIApplication app, Document doc)
    {
        var info = doc.ProjectInformation;
        var views = RevitLookup.PlaceableViews(doc).Count();
        return new
        {
            title = doc.Title,
            path = string.IsNullOrEmpty(doc.PathName) ? "(not saved yet)" : doc.PathName,
            revitVersion = app.Application.VersionNumber,
            isWorkshared = doc.IsWorkshared,
            projectName = info?.Name,
            projectNumber = info?.Number,
            client = info?.ClientName,
            address = info?.Address,
            status = info?.Status,
            activeView = doc.ActiveView?.Name,
            counts = new
            {
                sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).GetElementCount(),
                views,
                levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).GetElementCount(),
                rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().GetElementCount(),
            },
        };
    }

    public static object Levels(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
            .OrderBy(l => l.Elevation)
            .Select(l => new { name = l.Name, elevationFeet = Math.Round(l.Elevation, 4) })
            .ToList();

    public static object Views(Document doc, string? viewType, bool unplacedOnly)
    {
        ViewType? filter = null;
        if (!string.IsNullOrWhiteSpace(viewType))
        {
            if (!Enum.TryParse<ViewType>(viewType, true, out var parsed))
                throw new Core.Proposals.ToolArgumentException($"Unknown viewType '{viewType}'.");
            filter = parsed;
        }

        var placement = RevitLookup.PlacementMap(doc);
        var rows = RevitLookup.PlaceableViews(doc)
            .Where(v => filter is null || v.ViewType == filter)
            .Select(v => new
            {
                v,
                sheets = placement.TryGetValue(v.Id, out var s) ? s : null,
            })
            .Where(x => !unplacedOnly || x.sheets is null)
            .OrderBy(x => x.v.ViewType.ToString()).ThenBy(x => x.v.Name)
            .ToList();

        return new
        {
            count = rows.Count,
            truncated = rows.Count > MaxRows,
            views = rows.Take(MaxRows).Select(x => new
            {
                name = x.v.Name,
                viewType = x.v.ViewType.ToString(),
                level = (x.v as ViewPlan)?.GenLevel?.Name,
                scale = SafeScale(x.v),
                template = x.v.ViewTemplateId == ElementId.InvalidElementId ? null : doc.GetElement(x.v.ViewTemplateId)?.Name,
                onSheets = x.sheets,
            }),
        };
    }

    private static string? SafeScale(View view)
    {
        if (view is ViewSchedule) return null;
        try
        {
            return view.Scale > 0 ? $"1:{view.Scale}" : null;
        }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException)
        {
            return null;
        }
    }

    public static object Sheets(Document doc)
    {
        var placement = RevitLookup.PlacementMap(doc);
        var viewNames = RevitLookup.PlaceableViews(doc).ToDictionary(v => v.Id, v => v.Name);
        var byNumber = placement
            .SelectMany(kv => kv.Value.Select(number => (number, name: viewNames.TryGetValue(kv.Key, out var n) ? n : null)))
            .Where(x => x.name is not null)
            .GroupBy(x => x.number, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.name!).OrderBy(n => n).ToList(), StringComparer.OrdinalIgnoreCase);

        return new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase)
            .Select(s => new
            {
                number = s.SheetNumber,
                name = s.Name,
                isPlaceholder = s.IsPlaceholder ? true : (bool?)null,
                titleBlock = TitleBlockName(doc, s),
                views = byNumber.TryGetValue(s.SheetNumber, out var list) ? list : new List<string>(),
            })
            .ToList();
    }

    public static string? TitleBlockName(Document doc, ViewSheet sheet)
    {
        var instance = new FilteredElementCollector(doc, sheet.Id)
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .WhereElementIsNotElementType()
            .OfType<FamilyInstance>()
            .FirstOrDefault();
        return instance is null ? null : RevitLookup.TypeName(instance.Symbol);
    }

    public static object Rooms(Document doc, string? level)
    {
        var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .OfType<Room>()
            .Where(r => level is null || r.Level?.Name == level)
            .OrderBy(r => r.Level?.Elevation ?? 0).ThenBy(r => r.Number)
            .Select(r => new
            {
                number = r.Number,
                name = RevitLookup.RoomName(r),
                level = r.Level?.Name,
                areaSqFt = Math.Round(r.Area, 1),
                state = r.Location is null ? "unplaced" : r.Area <= 0 ? "not enclosed" : null,
            })
            .ToList();
        return new { count = rooms.Count, rooms = rooms.Take(MaxRows), truncated = rooms.Count > MaxRows };
    }

    private static readonly (string Key, BuiltInCategory Category)[] CountedCategories =
    {
        ("walls", BuiltInCategory.OST_Walls),
        ("doors", BuiltInCategory.OST_Doors),
        ("windows", BuiltInCategory.OST_Windows),
        ("floors", BuiltInCategory.OST_Floors),
        ("roofs", BuiltInCategory.OST_Roofs),
        ("ceilings", BuiltInCategory.OST_Ceilings),
        ("stairs", BuiltInCategory.OST_Stairs),
        ("casework", BuiltInCategory.OST_Casework),
        ("plumbingFixtures", BuiltInCategory.OST_PlumbingFixtures),
    };

    public static object CountElements(Document doc)
    {
        var result = new Dictionary<string, object>();
        foreach (var (key, category) in CountedCategories)
        {
            var elements = new FilteredElementCollector(doc).OfCategory(category).WhereElementIsNotElementType().ToElements();
            var byType = elements
                .GroupBy(e => doc.GetElement(e.GetTypeId()) is ElementType t ? RevitLookup.TypeName(t) : "(no type)")
                .OrderByDescending(g => g.Count())
                .Select(g => key == "walls"
                    ? (object)new { type = g.Key, count = g.Count(), lengthFeet = Math.Round(g.Sum(WallLength), 1) }
                    : new { type = g.Key, count = g.Count() })
                .ToList();
            result[key] = new { total = elements.Count, byType };
        }
        return result;
    }

    private static double WallLength(Element element) =>
        element.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? 0;
}
