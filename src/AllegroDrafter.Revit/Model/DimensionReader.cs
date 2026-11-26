using AllegroDrafter.Core.Proposals;
using Autodesk.Revit.DB;

namespace AllegroDrafter.Revit.Model;

/// <summary>
/// Read-only: how the designer dimensioned a plan. For every string: which side of the building,
/// where it lands on the sheet, its order out from the building, its type, its values, and what
/// each witness line measures to (which wall layer face, which door/window reference, grid...).
/// This is the rulebook the drafter copies; it also checks the view-to-sheet orientation.
/// </summary>
internal static class DimensionReader
{
    private const double Eps = 1.0 / 768; // 1/64"

    public static object Read(Document doc, string viewName, string? viewType)
    {
        var view = RevitLookup.ResolveView(doc, viewName, viewType, out var error) ?? throw new ToolArgumentException(error ?? $"View \"{viewName}\" not found.");
        var scale = view.Scale > 0 ? view.Scale : 1;
        var viewport = new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>().FirstOrDefault(v => v.ViewId == view.Id);
        var sheet = viewport is null ? null : doc.GetElement(viewport.SheetId) as ViewSheet;
        var rotation = viewport?.Rotation ?? ViewportRotation.None;

        // The building's outline from the walls in the view: a string outside it is exterior, inside is interior.
        var wallBoxes = new FilteredElementCollector(doc, view.Id).OfClass(typeof(Wall)).Cast<Wall>()
            .Where(w => w.WallType.Function is WallFunction.Exterior or WallFunction.Foundation)
            .Select(w => w.get_BoundingBox(null)).Where(b => b is not null).ToList();
        double minX = wallBoxes.Count > 0 ? wallBoxes.Min(b => b!.Min.X) : 0, maxX = wallBoxes.Count > 0 ? wallBoxes.Max(b => b!.Max.X) : 0;
        double minY = wallBoxes.Count > 0 ? wallBoxes.Min(b => b!.Min.Y) : 0, maxY = wallBoxes.Count > 0 ? wallBoxes.Max(b => b!.Max.Y) : 0;

        var strings = new List<Row>();
        var other = new List<object>();
        foreach (var d in new FilteredElementCollector(doc, view.Id).OfClass(typeof(Dimension)).Cast<Dimension>().Where(d => d.OwnerViewId == view.Id))
        {
            var typeName = d.DimensionType is { } t ? $"{t.StyleType}: {t.Name}" : "(none)";
            if (d.Curve is not Line line)
            {
                other.Add(new { id = d.Id.Value, type = typeName, kind = d.DimensionShape.ToString() });
                continue;
            }
            var dir = line.Direction;
            var modelHorizontal = Math.Abs(dir.Y) < 0.01;
            var modelVertical = Math.Abs(dir.X) < 0.01;
            string side;
            double coord, beyond;
            if (modelHorizontal)
            {
                coord = line.Origin.Y;
                (side, beyond) = coord > maxY ? ("north", coord - maxY) : coord < minY ? ("south", minY - coord) : ("inside", 0);
            }
            else if (modelVertical)
            {
                coord = line.Origin.X;
                (side, beyond) = coord > maxX ? ("east", coord - maxX) : coord < minX ? ("west", minX - coord) : ("inside", 0);
            }
            else
            {
                coord = 0;
                side = "angled";
                beyond = 0;
            }

            var refs = new List<object>();
            foreach (Reference r in d.References) refs.Add(Describe(doc, r));
            var values = d.NumberOfSegments > 0
                ? d.Segments.Cast<DimensionSegment>().Select(s => s.ValueString).ToList()
                : new List<string> { d.ValueString };
            strings.Add(new Row(side, beyond, new
            {
                id = d.Id.Value,
                type = typeName,
                side,
                onSheet = side is "inside" or "angled" ? side : SheetSide(side, view, rotation),
                runs = modelHorizontal ? "east-west" : modelVertical ? "north-south" : "angled",
                lineAtFeet = Math.Round(coord, 4),
                outFromBuildingFeet = Math.Round(beyond, 3),
                outFromBuildingPaperInches = Math.Round(beyond * 12 / scale, 3),
                segments = values,
                references = refs,
            }));
        }

        // Order each side's strings out from the building: tier 1 is closest.
        var ordered = strings.GroupBy(s => s.Side).SelectMany(g => g.OrderBy(s => s.Beyond).Select((s, i) => new { tier = g.Key is "inside" or "angled" ? (int?)null : i + 1, s.Data })).ToList();

        return new
        {
            view = view.Name,
            scale = $"1:{view.Scale}",
            viewUp = SideName(view.UpDirection),
            viewRight = SideName(view.RightDirection),
            sheet = sheet?.SheetNumber,
            viewportRotation = rotation.ToString(),
            buildingExtentsFeet = new { minX = Math.Round(minX, 3), maxX = Math.Round(maxX, 3), minY = Math.Round(minY, 3), maxY = Math.Round(maxY, 3) },
            count = strings.Count,
            strings = ordered,
            notLinear = other,
        };
    }

    private sealed record Row(string Side, double Beyond, object Data);

    /// <summary>Where a model side lands on the sheet: through the view's up/right, then the viewport's rotation.</summary>
    private static string SheetSide(string modelSide, View view, ViewportRotation rotation)
    {
        var v = modelSide switch { "north" => XYZ.BasisY, "south" => -XYZ.BasisY, "east" => XYZ.BasisX, _ => -XYZ.BasisX };
        var up = v.DotProduct(view.UpDirection);
        var right = v.DotProduct(view.RightDirection);
        var inView = Math.Abs(up) >= Math.Abs(right) ? (up > 0 ? 0 : 2) : (right > 0 ? 1 : 3); // 0 top, 1 right, 2 bottom, 3 left
        var turned = rotation switch
        {
            ViewportRotation.Clockwise => (inView + 1) % 4,
            ViewportRotation.Counterclockwise => (inView + 3) % 4,
            _ => inView,
        };
        return turned switch { 0 => "top", 1 => "right", 2 => "bottom", _ => "left" };
    }

    private static string SideName(XYZ v) =>
        Math.Abs(v.X) >= Math.Abs(v.Y) ? (v.X >= 0 ? "east" : "west") : (v.Y >= 0 ? "north" : "south");

    private static object Describe(Document doc, Reference r)
    {
        if (r.LinkedElementId != ElementId.InvalidElementId) return new { what = "linked model element" };
        var e = doc.GetElement(r.ElementId);
        switch (e)
        {
            case null:
                return new { what = "unknown" };

            case Wall wall:
                return new { element = wall.Id.Value, category = "wall", wallType = wall.WallType.Name, wallFunction = wall.WallType.Function.ToString(), measuresTo = WallFace(wall, r) };

            case FamilyInstance fi:
            {
                string refType;
                try { refType = fi.GetReferenceType(r).ToString(); } catch { refType = "unknown"; }
                string? refName = null;
                try { refName = fi.GetReferenceName(r); } catch { /* not every reference has a name */ }
                var typeMark = fi.Symbol?.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK)?.AsString();
                return new
                {
                    element = fi.Id.Value,
                    category = fi.Category?.Name,
                    family = $"{fi.Symbol?.FamilyName}: {fi.Symbol?.Name}",
                    typeMark,
                    measuresTo = refType switch
                    {
                        "CenterLeftRight" => "center (left/right)",
                        "Left" or "Right" => $"{refType.ToLowerInvariant()} edge",
                        "NotAReference" => refName ?? "a named family reference",
                        _ => refName is null ? refType : $"{refType} ({refName})",
                    },
                };
            }

            case Grid grid: return new { element = grid.Id.Value, category = "grid", name = grid.Name, measuresTo = "grid line" };
            case Level level: return new { element = level.Id.Value, category = "level", name = level.Name, measuresTo = "level" };
            case ReferencePlane plane: return new { element = plane.Id.Value, category = "reference plane", name = plane.Name, measuresTo = "reference plane" };
            default: return new { element = e.Id.Value, category = e.Category?.Name, measuresTo = r.ElementReferenceType.ToString() };
        }
    }

    /// <summary>
    /// Which face of the wall a witness line is on: matched against the wall type's layer boundaries,
    /// counted from the exterior. Face of stud = the exterior face of the core.
    /// </summary>
    private static string WallFace(Wall wall, Reference r)
    {
        var geom = wall.GetGeometryObjectFromReference(r);
        if (geom is not PlanarFace face)
            return $"not a face ({r.ElementReferenceType}); reference '{Safe(() => r.ConvertToStableRepresentation(wall.Document))}'";

        var orient = wall.Orientation;
        var along = Math.Abs(face.FaceNormal.DotProduct(orient));
        if (along < 0.01) return "end of wall";
        if (along < 0.99) return "angled face";

        var extRef = HostObjectUtils.GetSideFaces(wall, ShellLayerType.Exterior).FirstOrDefault();
        if (extRef is null || wall.GetGeometryObjectFromReference(extRef) is not PlanarFace extFace) return "side face (layers unknown)";
        var extOffset = extFace.Origin.DotProduct(orient);
        var depth = extOffset - face.Origin.DotProduct(orient); // feet in from the exterior face

        var cs = wall.WallType.GetCompoundStructure();
        if (cs is null) return $"side face {In(depth)} in from the exterior face";
        var layers = cs.GetLayers();
        var firstCore = cs.GetFirstCoreLayerIndex();
        var lastCore = cs.GetLastCoreLayerIndex();
        var at = 0.0;
        for (var i = 0; i <= layers.Count; i++)
        {
            if (Math.Abs(depth - at) <= Eps)
            {
                if (i == 0) return "exterior face (outside of finish)";
                if (i == layers.Count) return "interior face (inside of finish)";
                var label = $"between {Layer(wall.Document, layers[i - 1])} and {Layer(wall.Document, layers[i])}";
                if (i == firstCore) return "exterior face of core (face of stud): " + label;
                if (i == lastCore + 1) return "interior face of core: " + label;
                return label;
            }
            if (i < layers.Count) at += layers[i].Width;
        }
        return $"side face {In(depth)} in from the exterior face (not on a layer boundary)";
    }

    private static string Layer(Document doc, CompoundStructureLayer layer)
    {
        var material = doc.GetElement(layer.MaterialId)?.Name;
        return material is null ? layer.Function.ToString() : $"{layer.Function} [{material}]";
    }

    private static string In(double feet) => $"{Math.Round(feet * 12, 3)}\"";

    private static string Safe(Func<string> f)
    {
        try { return f(); } catch { return "?"; }
    }
}
