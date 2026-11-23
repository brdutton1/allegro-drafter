using AllegroDrafter.Core;
using AllegroDrafter.Core.Checks;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace AllegroDrafter.Revit.Model;

/// <summary>
/// Reads everything audit_set needs in one pass. Read-only: no transaction.
/// All judgment lives in Core (SetAuditor), so this only gathers facts.
/// </summary>
internal static class FactsReader
{
    private const int MaxTexts = 4000;
    private const int MaxTextLength = 600;

    private static readonly HashSet<ViewType> PlanTypes = new() { ViewType.FloorPlan, ViewType.CeilingPlan, ViewType.EngineeringPlan, ViewType.AreaPlan };

    public static SetFacts Read(Document doc, AuditSettings settings)
    {
        var facts = new SetFacts();
        var placement = RevitLookup.PlacementMap(doc);
        var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().ToList();
        var sheetById = sheets.ToDictionary(s => s.Id);

        // Views placed on sheets, plus the parents of placed dependent views:
        // annotation in a parent view shows on its dependents.
        var placedViews = RevitLookup.PlaceableViews(doc).Where(v => placement.ContainsKey(v.Id)).ToList();
        var shownViews = new Dictionary<ElementId, (View View, List<string> Sheets)>();
        foreach (var v in placedViews)
        {
            Show(shownViews, v, placement[v.Id]);
            var primary = v.GetPrimaryViewId();
            if (primary != ElementId.InvalidElementId && doc.GetElement(primary) is View parent) Show(shownViews, parent, placement[v.Id]);
        }

        ReadSheets(facts, sheets, placement);
        ReadViews(doc, facts, placedViews, placement);
        ReadSchedules(doc, facts, placement);
        var tagged = ReadTags(doc, shownViews);
        ReadOpenings(doc, facts, settings, tagged);
        ReadRooms(doc, facts, settings);
        ReadLegends(doc, facts, placement);
        ReadWalls(doc, facts);
        ReadAnnotation(doc, facts, shownViews, sheetById);
        return facts;
    }

    private static void Show(Dictionary<ElementId, (View View, List<string> Sheets)> map, View view, List<string> sheets)
    {
        if (!map.TryGetValue(view.Id, out var entry)) map[view.Id] = entry = (view, new List<string>());
        foreach (var s in sheets) if (!entry.Sheets.Contains(s)) entry.Sheets.Add(s);
    }

    private static void ReadSheets(SetFacts facts, List<ViewSheet> sheets, Dictionary<ElementId, List<string>> placement)
    {
        var itemsPerSheet = placement.Values.SelectMany(s => s).GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        foreach (var s in sheets.OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase))
        {
            facts.Sheets.Add(new SheetFact(s.SheetNumber, s.Name, itemsPerSheet.TryGetValue(s.SheetNumber, out var n) ? n : 0, s.IsPlaceholder));
        }
    }

    private static void ReadViews(Document doc, SetFacts facts, List<View> placedViews, Dictionary<ElementId, List<string>> placement)
    {
        foreach (var v in placedViews.Where(v => v is not ViewSchedule || v.ViewType == ViewType.Schedule))
        {
            facts.PlacedViews.Add(new ViewFact(
                v.Name,
                v.ViewType.ToString(),
                SafeScale(v),
                v.ViewTemplateId == ElementId.InvalidElementId ? null : doc.GetElement(v.ViewTemplateId)?.Name,
                placement[v.Id]));
        }
    }

    private static void ReadSchedules(Document doc, SetFacts facts, Dictionary<ElementId, List<string>> placement)
    {
        foreach (var s in new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>().Where(RevitLookup.IsPlaceable))
        {
            var d = s.Definition;
            var fields = d.GetFieldOrder().Select(id => d.GetField(id)).Select(f => new ScheduleFieldFact(f.GetName(), f.IsHidden)).ToList();
            var sortFields = d.GetSortGroupFields().Select(sg => d.GetField(sg.FieldId).GetName()).ToList();
            facts.Schedules.Add(new ScheduleFact(
                s.Name,
                CategoryKey(doc, d.CategoryId),
                d.IsKeySchedule,
                d.IsItemized,
                fields,
                sortFields,
                placement.TryGetValue(s.Id, out var on) ? on : new List<string>()));
        }
    }

    /// <summary>Element id to (tagged on a shown plan, tagged on a shown elevation).</summary>
    private static Dictionary<ElementId, (bool Plan, bool Elevation)> ReadTags(Document doc, Dictionary<ElementId, (View View, List<string> Sheets)> shownViews)
    {
        var result = new Dictionary<ElementId, (bool Plan, bool Elevation)>();
        foreach (var tag in new FilteredElementCollector(doc).OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
        {
            if (!shownViews.TryGetValue(tag.OwnerViewId, out var shown)) continue;
            var isPlan = PlanTypes.Contains(shown.View.ViewType);
            var isElevation = shown.View.ViewType == ViewType.Elevation;
            if (!isPlan && !isElevation) continue;
            foreach (var id in tag.GetTaggedLocalElementIds())
            {
                result.TryGetValue(id, out var current);
                result[id] = (current.Plan || isPlan, current.Elevation || isElevation);
            }
        }
        return result;
    }

    private static void ReadOpenings(Document doc, SetFacts facts, AuditSettings settings, Dictionary<ElementId, (bool Plan, bool Elevation)> tagged)
    {
        foreach (var (key, category) in new[] { ("doors", BuiltInCategory.OST_Doors), ("windows", BuiltInCategory.OST_Windows) })
        {
            var parameterNames = (key == "doors"
                    ? new[] { settings.SwingParameter, settings.TemperedParameter }
                    : new[] { settings.TemperedParameter, settings.EgressParameter })
                .Concat(settings.TemperedReasonParameters)
                .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();

            var instances = new FilteredElementCollector(doc).OfCategory(category).WhereElementIsNotElementType()
                .OfType<FamilyInstance>().ToList();

            foreach (var group in instances.GroupBy(i => i.Symbol.Id))
            {
                var symbol = group.First().Symbol;
                var first = group.First();
                facts.OpeningTypes.Add(new OpeningTypeFact(
                    key,
                    RevitLookup.TypeName(symbol),
                    symbol.FamilyName,
                    symbol.Name,
                    symbol.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK)?.AsString(),
                    Width(symbol) ?? Width(first),
                    Height(symbol) ?? Height(first),
                    group.Count()));
            }

            foreach (var fi in instances)
            {
                var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in parameterNames)
                {
                    var p = fi.LookupParameter(name) ?? fi.Symbol.LookupParameter(name);
                    if (p is not null) values[name] = ParameterWriter.Read(p);
                }
                var point = (fi.Location as LocationPoint)?.Point;
                tagged.TryGetValue(fi.Id, out var tags);
                facts.Openings.Add(new OpeningFact(
                    fi.Id.Value,
                    key,
                    RevitLookup.ElementLabel(fi),
                    RevitLookup.TypeName(fi.Symbol),
                    (doc.GetElement(fi.LevelId) as Level)?.Name,
                    point?.X,
                    point?.Y,
                    Width(fi.Symbol) ?? Width(fi),
                    Height(fi.Symbol) ?? Height(fi),
                    Length(fi, BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM),
                    fi.Host is Wall wall && wall.WallType.Function == WallFunction.Exterior,
                    fi.FromRoom?.Id.Value,
                    fi.ToRoom?.Id.Value,
                    tags.Plan,
                    tags.Elevation,
                    values,
                    fi.Host is Wall host ? RevitLookup.TypeName(host.WallType) : null));
            }
        }
    }

    private static void ReadRooms(Document doc, SetFacts facts, AuditSettings settings)
    {
        var wetRooms = new HashSet<long>();
        foreach (var fixture in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                     .WhereElementIsNotElementType().OfType<FamilyInstance>())
        {
            var name = RevitLookup.TypeName(fixture.Symbol);
            if (!settings.WetFixtureKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase))) continue;
            if (fixture.Room is Room room) wetRooms.Add(room.Id.Value);
        }

        foreach (var r in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().OfType<Room>())
        {
            var state = r.Location is null ? "unplaced" : r.Area <= 0 ? "not enclosed" : "placed";
            facts.Rooms.Add(new RoomFact(r.Id.Value, r.Number ?? "", RevitLookup.RoomName(r), r.Level?.Name, Math.Round(r.Area, 1), state, wetRooms.Contains(r.Id.Value)));
        }
    }

    private static void ReadLegends(Document doc, SetFacts facts, Dictionary<ElementId, List<string>> placement)
    {
        var wallsLabel = Category.GetCategory(doc, BuiltInCategory.OST_Walls)?.Name ?? "Walls";
        foreach (var legend in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate && v.ViewType == ViewType.Legend))
        {
            var wallTypes = new List<string>();
            foreach (var component in new FilteredElementCollector(doc, legend.Id).OfCategory(BuiltInCategory.OST_LegendComponents).WhereElementIsNotElementType())
            {
                // Reads like "Walls : Basic Wall : Interior 2x4 Wall".
                var text = component.get_Parameter(BuiltInParameter.LEGEND_COMPONENT)?.AsValueString();
                if (string.IsNullOrWhiteSpace(text)) continue;
                var parts = text.Split(" : ");
                if (parts.Length < 3 || !string.Equals(parts[0].Trim(), wallsLabel, StringComparison.OrdinalIgnoreCase)) continue;
                var type = Names.FamilyType(parts[1].Trim(), string.Join(" : ", parts.Skip(2)).Trim());
                if (!wallTypes.Contains(type)) wallTypes.Add(type);
            }
            facts.Legends.Add(new LegendFact(legend.Name, placement.TryGetValue(legend.Id, out var on) ? on : new List<string>(), wallTypes));
        }
    }

    private static void ReadWalls(Document doc, SetFacts facts)
    {
        var walls = new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>().ToList();
        facts.ModeledWallTypes = walls.Select(w => RevitLookup.TypeName(w.WallType)).Distinct().OrderBy(n => n).ToList();
        facts.WallTypes = walls
            .GroupBy(w => w.WallType.Id)
            .Select(g =>
            {
                var type = g.First().WallType;
                var length = g.Sum(w => w.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? 0);
                return new WallTypeFact(RevitLookup.TypeName(type), type.Function.ToString(), type.Kind.ToString(), g.Count(), Math.Round(length, 1));
            })
            .OrderBy(t => t.Name)
            .ToList();
    }

    private static void ReadAnnotation(Document doc, SetFacts facts, Dictionary<ElementId, (View View, List<string> Sheets)> shownViews, Dictionary<ElementId, ViewSheet> sheetById)
    {
        // Owner view -> (display name, sheet numbers). Sheets themselves count too.
        (string Name, IReadOnlyList<string> Sheets)? Where(ElementId owner)
        {
            if (sheetById.TryGetValue(owner, out var sheet)) return ($"sheet {sheet.SheetNumber}", new[] { sheet.SheetNumber });
            if (shownViews.TryGetValue(owner, out var shown)) return (shown.View.Name, shown.Sheets);
            return null;
        }

        var usage = new Dictionary<(string Kind, ElementId TypeId), (string Name, double? Height, int Count, string? Sample)>();
        void Count(string kind, ElementType? type, string viewName)
        {
            if (type is null) return;
            var k = (kind, type.Id);
            if (usage.TryGetValue(k, out var u)) usage[k] = u with { Count = u.Count + 1 };
            else usage[k] = (type.Name, TextHeightInches(type), 1, viewName);
        }

        var texts = 0;
        foreach (var note in new FilteredElementCollector(doc).OfClass(typeof(TextNote)).Cast<TextNote>())
        {
            if (Where(note.OwnerViewId) is not { } where) continue;
            Count("text", doc.GetElement(note.GetTypeId()) as ElementType, where.Name);
            if (texts++ < MaxTexts)
            {
                var text = note.Text ?? "";
                facts.Texts.Add(new TextFact(text.Length > MaxTextLength ? text[..MaxTextLength] : text, where.Name, where.Sheets));
            }
        }
        if (texts > MaxTexts) facts.ReadWarnings.Add($"Read the first {MaxTexts} of {texts} text notes for the reference and cover sheet checks.");

        foreach (var dim in new FilteredElementCollector(doc).OfClass(typeof(Dimension)).Cast<Dimension>())
        {
            if (Where(dim.OwnerViewId) is not { } where) continue;
            Count("dimension", dim.DimensionType, where.Name);
        }

        facts.AnnotationTypes = usage
            .Select(kv => new AnnotationTypeUsage(kv.Key.Kind, kv.Value.Name, kv.Value.Height, kv.Value.Count, kv.Value.Sample))
            .ToList();
    }

    private static string CategoryKey(Document doc, ElementId categoryId)
    {
        var value = categoryId.Value;
        if (value == (long)BuiltInCategory.OST_Doors) return "doors";
        if (value == (long)BuiltInCategory.OST_Windows) return "windows";
        if (value == (long)BuiltInCategory.OST_Sheets) return "sheets";
        if (value == (long)BuiltInCategory.OST_Rooms) return "rooms";
        return RevitLookup.CategoryLabel(doc, categoryId);
    }

    internal static double? Width(Element e) =>
        Length(e, BuiltInParameter.DOOR_WIDTH) ?? Length(e, BuiltInParameter.WINDOW_WIDTH) ?? Length(e, BuiltInParameter.FAMILY_WIDTH_PARAM) ?? Named(e, "Width");

    internal static double? Height(Element e) =>
        Length(e, BuiltInParameter.DOOR_HEIGHT) ?? Length(e, BuiltInParameter.WINDOW_HEIGHT) ?? Length(e, BuiltInParameter.FAMILY_HEIGHT_PARAM) ?? Named(e, "Height");

    private static double? Length(Element e, BuiltInParameter bip)
    {
        var p = e.get_Parameter(bip);
        return p is { HasValue: true, StorageType: StorageType.Double } && p.AsDouble() > 0 ? p.AsDouble() : null;
    }

    private static double? Named(Element e, string name)
    {
        var p = e.LookupParameter(name);
        return p is { HasValue: true, StorageType: StorageType.Double } && p.AsDouble() > 0 ? p.AsDouble() : null;
    }

    /// <summary>TEXT_SIZE is stored in feet at paper size.</summary>
    private static double? TextHeightInches(ElementType type)
    {
        var p = type.get_Parameter(BuiltInParameter.TEXT_SIZE);
        return p is { HasValue: true, StorageType: StorageType.Double } ? Math.Round(p.AsDouble() * 12, 5) : null;
    }

    private static int? SafeScale(View view)
    {
        if (view is ViewSchedule) return null;
        try
        {
            return view.Scale > 0 ? view.Scale : null;
        }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException)
        {
            return null;
        }
    }
}
