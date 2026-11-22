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
    public sealed record Extracted(List<PlanWall> Walls, List<PlanOpening> Openings, Dictionary<string, Reference> References, List<string> Notes, double Z);

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

        var exterior = new FilteredElementCollector(doc, view.Id).OfClass(typeof(Wall)).Cast<Wall>()
            .Where(w => w.LevelId == level.Id && w.WallType.Function == WallFunction.Exterior)
            .ToList();
        if (exterior.Count == 0) notes.Add($"No walls with type function Exterior on {level.Name} in this view. Check the wall types' Function parameter.");

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

            var faceRef = HostObjectUtils.GetSideFaces(wall, ShellLayerType.Exterior).FirstOrDefault();
            var face = faceRef is null ? null : wall.GetGeometryObjectFromReference(faceRef) as PlanarFace;
            if (faceRef is null || face is null)
            {
                noFace++;
                continue;
            }

            var northSouth = Math.Abs(b.X - a.X) < PlanWall.Tolerance;
            var planWall = new PlanWall(key, a.X, a.Y, b.X, b.Y, northSouth ? face.Origin.X : face.Origin.Y);
            walls.Add(planWall);
            references[key] = faceRef;
            if (!planWall.IsOrthogonal) continue;

            foreach (var insertId in wall.FindInserts(true, false, false, false))
            {
                if (doc.GetElement(insertId) is not FamilyInstance fi || fi.Location is not LocationPoint lp) continue;
                if (fi.Category?.BuiltInCategory is not (BuiltInCategory.OST_Doors or BuiltInCategory.OST_Windows)) continue;
                var centerRef = fi.GetReferences(FamilyInstanceReferenceType.CenterLeftRight).FirstOrDefault();
                if (centerRef is null)
                {
                    noCenter++;
                    continue;
                }
                var openingKey = $"o{fi.Id.Value}";
                openings.Add(new PlanOpening(openingKey, key, planWall.IsNorthSouth ? lp.Point.Y : lp.Point.X));
                references[openingKey] = centerRef;
            }
        }

        if (curtain > 0) notes.Add($"{curtain} curtain/stacked exterior wall(s) skipped.");
        if (noFace > 0) notes.Add($"{noFace} wall(s) had no usable exterior face and were skipped.");
        if (noCenter > 0) notes.Add($"{noCenter} door/window famil(ies) have no Center (Left/Right) reference and weren't located. Fix the family.");
        return new Extracted(walls, openings, references, notes, level.Elevation);
    }

    public static DimensionPlan Plan(Extracted data, DimensionViewChange change) =>
        DimensionPlanner.Plan(data.Walls, data.Openings, change.Strategies.ToList(), change.OffsetFeet, change.StepFeet);

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
