using AllegroDrafter.Core;
using AllegroDrafter.Core.Drafting;
using AllegroDrafter.Core.Proposals;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace AllegroDrafter.Revit.Model;

/// <summary>Tags: find what to tag, check, and place.</summary>
internal static class Tagging
{
    private static readonly Dictionary<string, (BuiltInCategory Element, BuiltInCategory Tag)> Categories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["doors"] = (BuiltInCategory.OST_Doors, BuiltInCategory.OST_DoorTags),
        ["windows"] = (BuiltInCategory.OST_Windows, BuiltInCategory.OST_WindowTags),
        ["rooms"] = (BuiltInCategory.OST_Rooms, BuiltInCategory.OST_RoomTags),
        ["walls"] = (BuiltInCategory.OST_Walls, BuiltInCategory.OST_WallTags),
    };

    private static readonly HashSet<ViewType> TaggableViews = new()
    {
        ViewType.FloorPlan, ViewType.CeilingPlan, ViewType.AreaPlan, ViewType.EngineeringPlan,
        ViewType.Elevation, ViewType.Section, ViewType.Detail,
    };

    public static string? CheckView(View view, string category)
    {
        if (!Categories.ContainsKey(category)) return $"Can't tag '{category}'.";
        if (!TaggableViews.Contains(view.ViewType)) return $"\"{view.Name}\" is a {view.ViewType} view. Tags go in plans, elevations, sections and details.";
        if (category.Equals("rooms", StringComparison.OrdinalIgnoreCase) && view is not ViewPlan) return "Room tags only go in plan views.";
        return null;
    }

    public static FamilySymbol? ResolveTagType(Document doc, string category, string? name, out string? error)
    {
        error = null;
        var tagCategory = Categories[category].Tag;
        if (name is null)
        {
            var symbol = doc.GetElement(doc.GetDefaultFamilyTypeId(new ElementId(tagCategory))) as FamilySymbol;
            if (symbol is null) error = $"This project has no default {category} tag type. Name one from get_standards tagTypes.";
            return symbol;
        }

        var match = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
            .FirstOrDefault(s => RevitLookup.TypeName(s) == name);
        if (match is null)
        {
            error = $"Tag type '{name}' is in the office standards but not loaded in this project. Load the tag family first.";
            return null;
        }
        var actual = match.Category?.BuiltInCategory;
        var multiOk = actual == BuiltInCategory.OST_MultiCategoryTags && !category.Equals("rooms", StringComparison.OrdinalIgnoreCase);
        if (actual != tagCategory && !multiOk)
        {
            error = $"'{name}' is a {match.Category?.Name} type, not a {category} tag.";
            return null;
        }
        return match;
    }

    public static List<Element> Candidates(Document doc, View view, string category, bool untaggedOnly)
    {
        var bic = Categories[category].Element;
        var elements = new FilteredElementCollector(doc, view.Id).OfCategory(bic).WhereElementIsNotElementType().ToElements()
            .Where(e => e is not Room room || (room.Location is not null && room.Area > 0))
            .Where(e => view.ViewType != ViewType.Elevation || FacesElevation(e, view))
            .ToList();
        if (!untaggedOnly) return elements;

        var tagged = bic == BuiltInCategory.OST_Rooms
            ? new FilteredElementCollector(doc, view.Id).OfCategory(BuiltInCategory.OST_RoomTags).OfType<RoomTag>()
                .Select(t => t.TaggedLocalRoomId).ToHashSet()
            : new FilteredElementCollector(doc, view.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>()
                .SelectMany(t => t.GetTaggedLocalElementIds()).ToHashSet();
        return elements.Where(e => !tagged.Contains(e.Id)).ToList();
    }

    /// <summary>
    /// An elevation "sees" every door and window inside its extents, even ones behind
    /// walls. Only tag openings in exterior walls whose outside face points at the viewer.
    /// </summary>
    private static bool FacesElevation(Element e, View view)
    {
        if (e is not FamilyInstance fi) return true; // walls and other categories: unchanged
        if (fi.Host is not Wall wall || wall.WallType.Function != WallFunction.Exterior) return false;
        return wall.Orientation.DotProduct(view.ViewDirection) > 0.5; // within ~60 degrees of facing the viewer
    }

    public static List<long> Tag(Document doc, View view, TagViewChange change)
    {
        var symbol = ResolveTagType(doc, change.Category, change.TagType, out var error) ?? throw new InvalidOperationException(error);
        if (!symbol.IsActive) symbol.Activate();
        var created = new List<long>();
        foreach (var element in Candidates(doc, view, change.Category, change.UntaggedOnly))
        {
            if (element is Room room)
            {
                var p = ((LocationPoint)room.Location).Point;
                var tag = doc.Create.NewRoomTag(new LinkElementId(room.Id), new UV(p.X, p.Y), view.Id);
                if (tag.GetTypeId() != symbol.Id) tag.ChangeTypeId(symbol.Id);
                created.Add(tag.Id.Value);
            }
            else
            {
                var tag = IndependentTag.Create(doc, symbol.Id, view.Id, new Reference(element), change.Leader, TagOrientation.Horizontal, Geometry.Anchor(element, view));
                created.Add(tag.Id.Value);
            }
        }
        return created;
    }
}

/// <summary>Detail and reference callouts in plan views.</summary>
internal static class Callouts
{
    public static ViewPlan? ResolveParent(Document doc, CalloutChange change, out string? error)
    {
        var view = RevitLookup.ResolveView(doc, change.ParentView, change.ParentViewType, out error);
        if (view is null) return null;
        if (view is ViewPlan plan && view.ViewType is ViewType.FloorPlan or ViewType.CeilingPlan or ViewType.AreaPlan) return plan;
        error = $"\"{view.Name}\" is a {view.ViewType} view. Callouts are supported in plan views for now.";
        return null;
    }

    public static ViewFamilyType? ResolveType(Document doc, ViewPlan parent, string name, out string? error)
    {
        error = null;
        var type = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().FirstOrDefault(t => t.Name == name);
        if (type is null)
        {
            error = $"View type '{name}' is in the office standards but not in this project.";
            return null;
        }
        var parentFamily = (doc.GetElement(parent.GetTypeId()) as ViewFamilyType)?.ViewFamily;
        if (type.ViewFamily != ViewFamily.Detail && type.ViewFamily != parentFamily)
        {
            error = $"'{name}' is a {type.ViewFamily} type. A callout in a {parent.ViewType} needs a Detail type or a {parentFamily} type.";
            return null;
        }
        return type;
    }

    public static (XYZ Min, XYZ Max)? Area(Document doc, ViewPlan parent, CalloutArea area, out string? error)
    {
        error = null;
        double minX, minY, maxX, maxY;
        if (area.RoomNumber is not null)
        {
            var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).OfType<Room>()
                .Where(r => r.Number == area.RoomNumber && r.Location is not null).ToList();
            var room = rooms.FirstOrDefault(r => r.LevelId == parent.GenLevel?.Id) ?? rooms.FirstOrDefault();
            var box = room?.get_BoundingBox(null);
            if (box is null)
            {
                error = $"No placed room numbered {area.RoomNumber}.";
                return null;
            }
            (minX, minY, maxX, maxY) = (box.Min.X, box.Min.Y, box.Max.X, box.Max.Y);
        }
        else if (area.ElementIds is { Count: > 0 })
        {
            var boxes = new List<BoundingBoxXYZ>();
            foreach (var id in area.ElementIds)
            {
                var box = doc.GetElement(new ElementId(id))?.get_BoundingBox(null);
                if (box is null)
                {
                    error = $"Element {id} doesn't exist or has no extents.";
                    return null;
                }
                boxes.Add(box);
            }
            (minX, minY, maxX, maxY) = (boxes.Min(b => b.Min.X), boxes.Min(b => b.Min.Y), boxes.Max(b => b.Max.X), boxes.Max(b => b.Max.Y));
        }
        else
        {
            (minX, minY, maxX, maxY) = (area.MinX!.Value, area.MinY!.Value, area.MaxX!.Value, area.MaxY!.Value);
        }

        var z = parent.GenLevel?.Elevation ?? 0;
        var m = area.MarginFeet;
        return (new XYZ(minX - m, minY - m, z), new XYZ(maxX + m, maxY + m, z));
    }

    public static View Create(Document doc, CalloutChange change)
    {
        var parent = ResolveParent(doc, change, out var error) ?? throw new InvalidOperationException(error);
        var (min, max) = Area(doc, parent, change.Area, out error) ?? throw new InvalidOperationException(error);

        if (change.IsReference)
        {
            var target = RevitLookup.ResolveView(doc, change.ReferenceView!, null, out error) ?? throw new InvalidOperationException(error);
            ViewSection.CreateReferenceCallout(doc, parent.Id, target.Id, min, max);
            return target;
        }

        var type = ResolveType(doc, parent, change.ViewFamilyType!, out error) ?? throw new InvalidOperationException(error);
        var view = ViewSection.CreateCallout(doc, parent.Id, type.Id, min, max);
        view.Name = change.Name!;
        if (change.Scale is int scale) view.Scale = scale;
        if (change.TemplateName is not null)
        {
            var template = RevitLookup.FindTemplate(doc, change.TemplateName) ?? throw new InvalidOperationException($"Template '{change.TemplateName}' not found.");
            if (!view.IsValidViewTemplate(template.Id)) throw new InvalidOperationException($"Template '{template.Name}' doesn't fit callout \"{view.Name}\".");
            view.ViewTemplateId = template.Id;
        }
        return view;
    }

    public static bool ViewNameTaken(Document doc, string name) =>
        new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Any(v => !v.IsTemplate && string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Text notes with optional leaders.</summary>
internal static class Notes
{
    private static readonly HashSet<ViewType> NoteViews = new()
    {
        ViewType.FloorPlan, ViewType.CeilingPlan, ViewType.AreaPlan, ViewType.DraftingView, ViewType.Legend,
    };

    public static string? CheckView(View view, TextNoteChange change)
    {
        if (!NoteViews.Contains(view.ViewType)) return $"\"{view.Name}\" is a {view.ViewType} view. Notes are supported in plan, drafting and legend views for now.";
        if (change.LeaderToElementId is not null && view is not ViewPlan) return "Leaders to elements only work in plan views.";
        return null;
    }

    public static TextNoteType? ResolveType(Document doc, string name) =>
        new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().FirstOrDefault(t => t.Name == name);

    public static TextNote Create(Document doc, View view, TextNoteChange change)
    {
        var type = ResolveType(doc, change.TextType) ?? throw new InvalidOperationException($"Text type '{change.TextType}' not found.");
        var z = (view as ViewPlan)?.GenLevel?.Elevation ?? 0;
        var note = TextNote.Create(doc, view.Id, new XYZ(change.XFeet, change.YFeet, z), change.Text.Replace("\r\n", "\r").Replace('\n', '\r'), type.Id);
        if (change.LeaderToElementId is long id)
        {
            var target = doc.GetElement(new ElementId(id)) ?? throw new InvalidOperationException($"Element {id} not found.");
            var end = Geometry.Anchor(target, view);
            var leader = note.AddLeader(end.X < change.XFeet ? TextNoteLeaderTypes.TNLT_STRAIGHT_L : TextNoteLeaderTypes.TNLT_STRAIGHT_R);
            leader.End = new XYZ(end.X, end.Y, z);
        }
        return note;
    }
}

/// <summary>Exterior dimension strings: pull walls/openings from the plan, plan in Core, draw in Revit.</summary>
internal static class Dimensioning
{
    public sealed record Extracted(List<PlanWall> Walls, List<PlanOpening> Openings, Dictionary<string, Reference> References, List<string> Notes, double Z)
    {
        public List<PlanInteriorWall> Interior { get; } = new();

        /// <summary>Structural and architectural columns in the view (porch posts, timber columns).</summary>
        public List<PlanColumn> Columns { get; } = new();

        /// <summary>Vertical edge faces of floors, decks and slabs near the view's level.</summary>
        public List<PlanEdge> Edges { get; } = new();

        /// <summary>The model side at the top of the view on the sheet (north unless the view is rotated).</summary>
        public Side ViewTop { get; init; } = Side.North;

        /// <summary>The view scale (48 for 1/4" = 1'-0"), to turn paper spacing into model feet.</summary>
        public int Scale { get; init; } = 48;
    }

    private static Side SideOf(XYZ v) =>
        Math.Abs(v.X) >= Math.Abs(v.Y) ? (v.X >= 0 ? Side.East : Side.West) : (v.Y >= 0 ? Side.North : Side.South);

    private static bool IsOutside(WallFunction f) => f is WallFunction.Exterior or WallFunction.Foundation;

    /// <summary>Which way a wall's exterior face looks, from its orientation (flips included).</summary>
    private static Side FacingOf(Wall wall)
    {
        var o = wall.Orientation;
        return Math.Abs(o.X) >= Math.Abs(o.Y) ? (o.X >= 0 ? Side.East : Side.West) : (o.Y >= 0 ? Side.North : Side.South);
    }

    public static ViewPlan? ResolvePlan(Document doc, DimensionViewChange change, out string? error)
    {
        var view = RevitLookup.ResolveView(doc, change.ViewName, change.ViewType, out error);
        if (view is null) return null;
        if (view is ViewPlan plan && view.ViewType == ViewType.FloorPlan && plan.GenLevel is not null) return plan;
        error = $"\"{view.Name}\" is a {view.ViewType} view. Exterior dimensions are for floor plans.";
        return null;
    }

    public static bool HasDimensions(Document doc, View view) =>
        new FilteredElementCollector(doc, view.Id).OfClass(typeof(Dimension)).Cast<Dimension>().Any(d => d.OwnerViewId == view.Id);

    public static DimensionType? ResolveType(Document doc, string name) =>
        new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>()
            .FirstOrDefault(t => $"{t.StyleType}: {t.Name}" == name && t.StyleType == DimensionStyleType.Linear);

    public static Extracted Extract(Document doc, ViewPlan view)
    {
        var walls = new List<PlanWall>();
        var openings = new List<PlanOpening>();
        var references = new Dictionary<string, Reference>();
        var notes = new List<string>();
        var level = view.GenLevel!;
        int curtain = 0, noFace = 0, noCenter = 0;
        var straight = new List<(Wall Wall, string Key, XYZ A, XYZ B)>();
        var missingCenter = new List<string>();

        // Foundation walls count as the outside on a foundation plan: their exterior face is the face of concrete.
        var exterior = new FilteredElementCollector(doc, view.Id).OfClass(typeof(Wall)).Cast<Wall>()
            .Where(w => w.LevelId == level.Id && IsOutside(w.WallType.Function))
            .ToList();
        if (exterior.Count == 0) notes.Add($"No walls with type function Exterior or Foundation on {level.Name} in this view. Check the wall types' Function parameter.");

        foreach (var wall in exterior)
        {
            if (wall.WallType.Kind != WallKind.Basic)
            {
                curtain++;
                continue;
            }
            if (wall.Location is not LocationCurve { Curve: var curve }) continue;
            var a = curve.GetEndPoint(0);
            var b = curve.GetEndPoint(1);
            var key = $"w{wall.Id.Value}";

            if (curve is not Line)
            {
                // Report it to the planner as non-orthogonal so it's counted and skipped.
                walls.Add(new PlanWall(key, a.X, a.Y, b.X + 1, b.Y + 1, 0));
                continue;
            }

            straight.Add((wall, key, a, b));
        }

        // Which side is outside comes from the plan's shape, not from how each wall was drawn:
        // a wall modeled flipped still faces the way the outline says.
        var footprint = new Footprint(straight.Select(s => new PlanWall(s.Key, s.A.X, s.A.Y, s.B.X, s.B.Y, 0)));
        var flipped = 0;
        foreach (var (wall, key, a, b) in straight)
        {
            var northSouth = Math.Abs(b.X - a.X) < PlanWall.Tolerance;
            var probe = new PlanWall(key, a.X, a.Y, b.X, b.Y, 0);
            var facing = footprint.FacingOf(probe) ?? FacingOf(wall);
            if (probe.IsOrthogonal && footprint.FacingOf(probe) is { } byShape && byShape != FacingOf(wall)) flipped++;

            // The outside face (outside of finish): of the two side faces, the one further toward the facing side.
            Reference? faceRef = null;
            double faceCoord = 0;
            var outward = facing is Side.North or Side.East ? 1 : -1;
            foreach (var shell in new[] { ShellLayerType.Exterior, ShellLayerType.Interior })
            {
                var r = HostObjectUtils.GetSideFaces(wall, shell).FirstOrDefault();
                if (r is null || wall.GetGeometryObjectFromReference(r) is not PlanarFace f) continue;
                var coord = northSouth ? f.Origin.X : f.Origin.Y;
                if (faceRef is null || coord * outward > faceCoord * outward) { faceRef = r; faceCoord = coord; }
            }
            if (faceRef is null)
            {
                noFace++;
                continue;
            }

            var planWall = new PlanWall(key, a.X, a.Y, b.X, b.Y, faceCoord, facing);
            walls.Add(planWall);
            references[key] = faceRef;
            references[InteriorDimensionPlanner.ExteriorPrefix + key] = faceRef; // interior strings close to the same face
            if (!planWall.IsOrthogonal) continue;

            foreach (var insertId in wall.FindInserts(true, false, false, false))
            {
                if (doc.GetElement(insertId) is not FamilyInstance fi || fi.Location is not LocationPoint lp) continue;
                if (fi.Category?.BuiltInCategory is not (BuiltInCategory.OST_Doors or BuiltInCategory.OST_Windows)) continue;
                var centerRef = fi.GetReferences(FamilyInstanceReferenceType.CenterLeftRight).FirstOrDefault();
                if (centerRef is null)
                {
                    noCenter++;
                    missingCenter.Add($"{fi.Symbol?.FamilyName}: {fi.Symbol?.Name}");
                    continue;
                }
                var openingKey = $"o{fi.Id.Value}";
                openings.Add(new PlanOpening(openingKey, key, planWall.IsNorthSouth ? lp.Point.Y : lp.Point.X));
                references[openingKey] = centerRef;
            }
        }
        if (flipped > 0) notes.Add($"{flipped} exterior wall(s) are modeled with their exterior side facing in; measured to their true outside face.");
        if (footprint.Count > 0 && straight.Count > 0 && walls.Count(w => w.IsOrthogonal) > 0 && straight.All(s => footprint.FacingOf(new PlanWall(s.Key, s.A.X, s.A.Y, s.B.X, s.B.Y, 0)) is null))
            notes.Add("The exterior walls don't close a loop on this level, so outside was read from how the walls were drawn. Check the strings.");
        if (curtain > 0) notes.Add($"{curtain} curtain/stacked exterior wall(s) skipped.");
        if (noFace > 0) notes.Add($"{noFace} wall(s) had no usable exterior face and were skipped.");
        if (noCenter > 0) notes.Add($"{noCenter} door/window(s) weren't located: no Center (Left/Right) reference in {string.Join(", ", missingCenter.Distinct())}. Fix the family.");
        var data = new Extracted(walls, openings, references, notes, level.Elevation) { ViewTop = SideOf(view.UpDirection), Scale = view.Scale > 0 ? view.Scale : 48 };
        ExtractInterior(doc, view, data);
        ExtractColumns(doc, view, data);
        ExtractFloorEdges(doc, view, data);
        return data;
    }

    /// <summary>
    /// Interior walls on the view's level: straight, basic, Function not Exterior. Each gets the
    /// reference to its west (north-south wall) or south (east-west wall) face, the one face every
    /// interior string measures to, so the numbers read consistently across the plan.
    /// </summary>
    private static void ExtractInterior(Document doc, ViewPlan view, Extracted data)
    {
        var level = view.GenLevel!;
        int angled = 0, noFace = 0;
        foreach (var wall in new FilteredElementCollector(doc, view.Id).OfClass(typeof(Wall)).Cast<Wall>()
                     .Where(w => w.LevelId == level.Id && !IsOutside(w.WallType.Function) && w.WallType.Function != WallFunction.Retaining && w.WallType.Kind == WallKind.Basic))
        {
            if (wall.Location is not LocationCurve { Curve: Line line }) { angled++; continue; }
            var a = line.GetEndPoint(0);
            var b = line.GetEndPoint(1);
            var key = $"i{wall.Id.Value}";
            var planWall = new PlanInteriorWall(key, a.X, a.Y, b.X, b.Y, wall.Width);
            if (!planWall.IsOrthogonal) { angled++; continue; }

            Reference? best = null;
            var bestCoord = double.MaxValue;
            foreach (var shell in new[] { ShellLayerType.Exterior, ShellLayerType.Interior })
            {
                var r = HostObjectUtils.GetSideFaces(wall, shell).FirstOrDefault();
                if (r is null || wall.GetGeometryObjectFromReference(r) is not PlanarFace f) continue;
                var coord = planWall.IsNorthSouth ? f.Origin.X : f.Origin.Y;
                if (coord < bestCoord) { best = r; bestCoord = coord; }
            }
            if (best is null) { noFace++; continue; }
            data.Interior.Add(planWall);
            data.References[key] = best;
        }
        if (angled > 0) data.Notes.Add($"{angled} angled or curved interior wall(s) skipped. Locate those by hand.");
        if (noFace > 0) data.Notes.Add($"{noFace} interior wall(s) had no usable face and weren't located.");
    }

    /// <summary>
    /// Columns visible in the view (structural or architectural), measured at their centerlines.
    /// Center (Left/Right) is the plane across the family's hand direction, Center (Front/Back) the one
    /// across its facing, so each is matched to the axis it locates. Columns turned off square are skipped and said.
    /// </summary>
    private static void ExtractColumns(Document doc, ViewPlan view, Extracted data)
    {
        int turned = 0, noCenter = 0;
        var seen = new HashSet<long>();
        foreach (var category in new[] { BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Columns })
        {
            foreach (var fi in new FilteredElementCollector(doc, view.Id).OfCategory(category).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>())
            {
                if (!seen.Add(fi.Id.Value) || fi.Location is not LocationPoint lp) continue;
                var hand = fi.HandOrientation;
                var handAlongX = Math.Abs(hand.X) > 0.999;
                var handAlongY = Math.Abs(hand.Y) > 0.999;
                if (!handAlongX && !handAlongY) { turned++; continue; }
                var lr = fi.GetReferences(FamilyInstanceReferenceType.CenterLeftRight).FirstOrDefault();
                var fb = fi.GetReferences(FamilyInstanceReferenceType.CenterFrontBack).FirstOrDefault();
                var refX = handAlongX ? lr : fb;
                var refY = handAlongX ? fb : lr;
                if (refX is null || refY is null) { noCenter++; continue; }

                var box = fi.get_BoundingBox(view) ?? fi.get_BoundingBox(null);
                var half = box is null ? 0.5 : Math.Max(box.Max.X - box.Min.X, box.Max.Y - box.Min.Y) / 2;
                var column = new PlanColumn($"c{fi.Id.Value}", lp.Point.X, lp.Point.Y, half);
                data.Columns.Add(column);
                data.References[column.KeyX] = refX;
                data.References[column.KeyY] = refY;
            }
        }
        if (turned > 0) data.Notes.Add($"{turned} column(s) are turned off square and weren't located. Dimension those by hand.");
        if (noCenter > 0) data.Notes.Add($"{noCenter} column(s) have no center references in their family and weren't located. Fix the family.");
    }

    /// <summary>
    /// Edge faces of floors, decks and slabs (Floors category) visible in the view and within 4' of its
    /// level: the vertical, square-to-the-grid faces of each floor's solid, with the references Revit
    /// dimensions to. The planner keeps only the edges that run out past the house.
    /// </summary>
    private static void ExtractFloorEdges(Document doc, ViewPlan view, Extracted data)
    {
        var levelZ = view.GenLevel!.Elevation;
        var options = new Options { ComputeReferences = true, View = view };
        foreach (var floor in new FilteredElementCollector(doc, view.Id).OfCategory(BuiltInCategory.OST_Floors).WhereElementIsNotElementType())
        {
            if (doc.GetElement(floor.LevelId) is Level lv && Math.Abs(lv.Elevation - levelZ) > 4.0) continue;
            var n = 0;
            foreach (var solid in Solids(floor.get_Geometry(options)))
            {
                foreach (Face f in solid.Faces)
                {
                    if (f is not PlanarFace pf || pf.Reference is null || Math.Abs(pf.FaceNormal.Z) > 0.01) continue;
                    var alongX = Math.Abs(pf.FaceNormal.X) > 0.999;
                    var alongY = Math.Abs(pf.FaceNormal.Y) > 0.999;
                    if (!alongX && !alongY) continue;
                    var points = pf.EdgeLoops.Cast<EdgeArray>().SelectMany(loop => loop.Cast<Edge>()).SelectMany(e => e.Tessellate()).ToList();
                    if (points.Count == 0) continue;
                    var (lo, hi) = alongX ? (points.Min(p => p.Y), points.Max(p => p.Y)) : (points.Min(p => p.X), points.Max(p => p.X));
                    var key = $"f{floor.Id.Value}:{n++}";
                    data.Edges.Add(new PlanEdge(key, alongX ? pf.Origin.X : pf.Origin.Y, alongX, lo, hi));
                    data.References[key] = pf.Reference;
                }
            }
        }
    }

    private static IEnumerable<Solid> Solids(GeometryElement? geometry)
    {
        if (geometry is null) yield break;
        foreach (var obj in geometry)
        {
            if (obj is Solid { Volume: > 0 } s) yield return s;
            else if (obj is GeometryInstance gi)
                foreach (var inner in Solids(gi.GetInstanceGeometry())) yield return inner;
        }
    }

    public static DimensionPlan Plan(Extracted data, DimensionViewChange change)
    {
        var basis = PlanDimensioning.ParseBasis(change.InteriorBasis);
        var sides = PlanDimensioning.ResolveSides(change.Sides, data.ViewTop);
        var plan = PlanDimensioning.Plan(data.Walls, data.Openings, data.Interior, change.Strategies.ToList(), change.OffsetAt(data.Scale), change.StepAt(data.Scale), basis, sides, data.Columns, data.Edges);
        if (sides is not null && change.Sides!.Any(s => s is "top" or "bottom" or "left" or "right") && data.ViewTop != Side.North)
            plan = plan with { Notes = plan.Notes.Append($"This view is rotated: the top of the sheet is model {data.ViewTop.ToString().ToLowerInvariant()}.").ToList() };
        if (basis == InteriorBasis.Center && change.Strategies.Contains(PlanDimensioning.Interior))
        {
            // Centerline references need a check in Revit first (plan: spike 1). Until then the strings
            // measure to the west/south face, and say so.
            return plan with { Notes = plan.Notes.Append("Interior strings measure to the west/south face of each wall; centerline dimensions come after they're checked in Revit.").ToList() };
        }
        return plan;
    }

    /// <summary>Draws every string. A string Revit refuses is rolled back alone and reported.</summary>
    public static (List<long> Created, List<string> Notes) Draw(Document doc, ViewPlan view, DimensionViewChange change)
    {
        var data = Extract(doc, view);
        var plan = Plan(data, change);
        var notes = data.Notes.Concat(plan.Notes).ToList();
        DimensionType? type = null;
        if (change.DimensionType is not null)
            type = ResolveType(doc, change.DimensionType) ?? throw new InvalidOperationException($"Dimension type '{change.DimensionType}' not found.");

        var created = new List<long>();
        var refused = 0;
        foreach (var s in plan.Strings)
        {
            var array = new ReferenceArray();
            foreach (var r in s.Refs) array.Append(data.References[r.Key]);
            var lo = s.Refs.First().Position - 1;
            var hi = s.Refs.Last().Position + 1;
            var line = s.IsHorizontal
                ? Line.CreateBound(new XYZ(lo, s.LineCoord, data.Z), new XYZ(hi, s.LineCoord, data.Z))
                : Line.CreateBound(new XYZ(s.LineCoord, lo, data.Z), new XYZ(s.LineCoord, hi, data.Z));

            using var sub = new SubTransaction(doc);
            sub.Start();
            try
            {
                var dim = type is null ? doc.Create.NewDimension(view, line, array) : doc.Create.NewDimension(view, line, array, type);
                sub.Commit();
                created.Add(dim.Id.Value);
            }
            catch (Exception)
            {
                sub.RollBack();
                refused++;
            }
        }
        if (refused > 0) notes.Add($"Revit refused {refused} string(s); those sides need hand dimensions.");
        return (created, notes);
    }
}

internal static class Geometry
{
    /// <summary>A sensible point to tag or point at: insertion point, curve midpoint, or box center.</summary>
    public static XYZ Anchor(Element element, View view)
    {
        switch (element.Location)
        {
            case LocationPoint lp:
                return lp.Point;
            case LocationCurve lc:
                return lc.Curve.Evaluate(0.5, true);
        }
        var box = element.get_BoundingBox(view) ?? element.get_BoundingBox(null);
        return box is null ? XYZ.Zero : (box.Min + box.Max) / 2;
    }
}
