using AllegroDrafter.Core.Finishing;
using AllegroDrafter.Core.Proposals;
using AllegroDrafter.Core.Standards;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace AllegroDrafter.Revit.Model;

/// <summary>
/// After a batch applies: which sheets now print something different, and what. Read-only.
/// A changed value counts on a sheet when a schedule there prints that parameter, or when a tag
/// there tags the element and the parameter is one the office tags print (TagLabelParameters).
/// New tags, notes and placed views count on the sheet they're on. The rest lives in the model only.
/// </summary>
internal static class ImpactReader
{
    public static List<ChangeImpact> Read(Document doc, Proposal proposal, FinishDraftingSettings settings)
    {
        var ctx = new Ctx(doc, proposal.Id, settings);
        foreach (var change in proposal.Changes)
        {
            switch (change)
            {
                case SetParametersChange c:
                    if (doc.GetElement(new ElementId(c.ElementId)) is { } element)
                        foreach (var v in c.Values) ctx.ValueChanged(element, v.Name);
                    break;

                case SetTypeMarkChange c:
                    if (FixEditor.FindType(doc, c.Category, c.TypeName) is { } symbol)
                    {
                        var instances = new FilteredElementCollector(doc).WhereElementIsNotElementType()
                            .OfCategoryId(symbol.Category.Id).Where(e => e.GetTypeId() == symbol.Id).ToList();
                        foreach (var e in instances) ctx.ValueChanged(e, "Type Mark");
                        if (instances.Count == 0) ctx.ModelOnly($"Type \"{c.TypeName}\"", $"Type Mark {c.TypeMark} (none placed)");
                    }
                    break;

                case ScheduleFieldsChange c:
                    if (RevitLookup.FindSchedule(doc, c.Schedule) is { } fields)
                    {
                        var parts = new List<string>();
                        if (c.Add.Count > 0) parts.Add("columns added: " + string.Join(", ", c.Add.Select(a => a.Field)));
                        if (c.Remove.Count > 0) parts.Add("columns removed: " + string.Join(", ", c.Remove));
                        ctx.OnView(fields.Id, fields.Name, string.Join("; ", parts));
                    }
                    break;

                case ScheduleSortChange c:
                    if (RevitLookup.FindSchedule(doc, c.Schedule) is { } sorted)
                        ctx.OnView(sorted.Id, sorted.Name, "sorting and grouping");
                    break;

                case ApplyViewTemplateChange c:
                    if (RevitLookup.ResolveView(doc, c.ViewName, c.ViewType, out _) is { } view)
                        ctx.OnView(view.Id, view.Name, "view template " + c.TemplateName);
                    break;

                // Counted from the elements they created, below.
                case TagViewChange:
                case PlaceViewChange:
                case TextNoteChange:
                case DimensionViewChange:
                case CreateSheetChange:
                    break;

                default:
                    ctx.ModelOnly(change.Describe(), "");
                    break;
            }
        }

        foreach (var id in proposal.CreatedElementIds)
            if (doc.GetElement(new ElementId(id)) is { } created) ctx.Created(created);

        return ctx.Results();
    }

    private sealed class Ctx
    {
        private readonly Document _doc;
        private readonly string _proposalId;
        private readonly FinishDraftingSettings _settings;
        private readonly Dictionary<ElementId, ViewSheet> _sheets;
        private readonly Dictionary<ElementId, List<ViewSheet>> _placed = new();
        private readonly Dictionary<(long? Sheet, string Where, string What, string? Noun), (ViewSheet? Sheet, HashSet<long> Ids)> _out = new();
        private Dictionary<long, HashSet<ElementId>>? _tagsByElement;
        private List<(ViewSchedule Schedule, ElementId Category, HashSet<string> Fields)>? _schedules;

        public Ctx(Document doc, string proposalId, FinishDraftingSettings settings)
        {
            _doc = doc;
            _proposalId = proposalId;
            _settings = settings;
            _sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .Where(s => !s.IsTemplate && !s.IsPlaceholder).ToDictionary(s => s.Id);
            foreach (var vp in new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>())
                if (_sheets.TryGetValue(vp.SheetId, out var sheet)) Place(vp.ViewId, sheet);
            foreach (var si in new FilteredElementCollector(doc).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>())
                if (!si.IsTitleblockRevisionSchedule && _sheets.TryGetValue(si.OwnerViewId, out var sheet)) Place(si.ScheduleId, sheet);
        }

        private void Place(ElementId viewId, ViewSheet sheet)
        {
            if (!_placed.TryGetValue(viewId, out var list)) _placed[viewId] = list = new List<ViewSheet>();
            if (!list.Contains(sheet)) list.Add(sheet);
        }

        public void OnView(ElementId viewId, string where, string what, IEnumerable<long>? ids = null, string? noun = null)
        {
            var on = _sheets.TryGetValue(viewId, out var self) ? new List<ViewSheet> { self } : _placed.TryGetValue(viewId, out var list) ? list : null;
            if (on is null)
            {
                // Not on any sheet: still a change, just not one to check on paper.
                Add(null, where + " (not on a sheet)", what, ids, noun);
                return;
            }
            foreach (var sheet in on) Add(sheet, where, what, ids, noun);
        }

        public void ModelOnly(string where, string what) => Add(null, where, what, null, null);

        private void Add(ViewSheet? sheet, string where, string what, IEnumerable<long>? ids, string? noun)
        {
            var key = (sheet?.Id.Value, where, what, noun);
            if (!_out.TryGetValue(key, out var entry)) _out[key] = entry = (sheet, new HashSet<long>());
            if (ids is not null) entry.Ids.UnionWith(ids);
        }

        /// <summary>A parameter value changed on an element: where does that print?</summary>
        public void ValueChanged(Element element, string parameter)
        {
            var noun = Noun(element);
            var printed = false;
            var category = element.Category?.Id;
            foreach (var (schedule, scheduleCategory, fields) in Schedules())
            {
                if (category is null || scheduleCategory != category || !fields.Contains(parameter) || !_placed.ContainsKey(schedule.Id)) continue;
                OnView(schedule.Id, schedule.Name, parameter, new[] { element.Id.Value }, noun);
                printed = true;
            }
            if (_settings.TagLabelParameters.Contains(parameter, StringComparer.OrdinalIgnoreCase)
                && TagsByElement().TryGetValue(element.Id.Value, out var views))
            {
                foreach (var viewId in views)
                {
                    if (!_placed.ContainsKey(viewId) || _doc.GetElement(viewId) is not View view) continue;
                    OnView(viewId, view.Name, $"{noun} tags show {parameter}", new[] { element.Id.Value }, noun);
                    printed = true;
                }
            }
            if (!printed) Add(null, $"{Capital(noun)}s", $"{parameter} (no placed schedule or tag prints it)", new[] { element.Id.Value }, noun);
        }

        /// <summary>Something the batch created: a tag, note, dimension, viewport or sheet.</summary>
        public void Created(Element e)
        {
            switch (e)
            {
                case ViewSheet sheet:
                    Add(sheet, "Sheet", "new sheet", null, null);
                    return;
                case Viewport vp when _sheets.TryGetValue(vp.SheetId, out var vpSheet):
                    Add(vpSheet, _doc.GetElement(vp.ViewId)?.Name ?? "View", "placed on the sheet", new[] { e.Id.Value }, null);
                    return;
                case ScheduleSheetInstance si when _sheets.TryGetValue(si.OwnerViewId, out var siSheet):
                    Add(siSheet, _doc.GetElement(si.ScheduleId)?.Name ?? "Schedule", "placed on the sheet", new[] { e.Id.Value }, null);
                    return;
            }
            var owner = e.OwnerViewId;
            if (owner is null || owner == ElementId.InvalidElementId || _doc.GetElement(owner) is not View ownerView)
            {
                Add(null, RevitLookup.ElementLabel(e), "created", null, null);
                return;
            }
            var (what, noun) = e switch
            {
                IndependentTag or SpatialElementTag => ("tags placed", "tag"),
                TextNote => ("notes added", "note"),
                Dimension => ("dimension strings drawn", "string"),
                _ => ("added", "element"),
            };
            OnView(owner, ownerView.Name, what, new[] { e.Id.Value }, noun);
        }

        public List<ChangeImpact> Results() =>
            _out.Select(kv => new ChangeImpact(
                    _proposalId,
                    kv.Value.Sheet?.SheetNumber,
                    kv.Value.Sheet?.Name,
                    kv.Value.Sheet?.Id.Value,
                    kv.Key.Where,
                    kv.Key.What,
                    kv.Value.Ids.OrderBy(i => i).ToList(),
                    kv.Key.Noun))
                .ToList();

        private List<(ViewSchedule, ElementId, HashSet<string>)> Schedules()
        {
            if (_schedules is not null) return _schedules;
            _schedules = new();
            foreach (var s in new FilteredElementCollector(_doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>())
            {
                if (s.IsTemplate || s.IsTitleblockRevisionSchedule || s.Definition is null) continue;
                var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < s.Definition.GetFieldCount(); i++)
                {
                    var f = s.Definition.GetField(i);
                    if (!f.IsHidden) fields.Add(f.GetName());
                }
                _schedules.Add((s, s.Definition.CategoryId, fields));
            }
            return _schedules;
        }

        /// <summary>Element id to the views it's tagged in. Built once per batch.</summary>
        private Dictionary<long, HashSet<ElementId>> TagsByElement()
        {
            if (_tagsByElement is not null) return _tagsByElement;
            _tagsByElement = new();
            void Add(long id, ElementId view)
            {
                if (!_tagsByElement.TryGetValue(id, out var set)) _tagsByElement[id] = set = new HashSet<ElementId>();
                set.Add(view);
            }
            foreach (var tag in new FilteredElementCollector(_doc).OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
                foreach (var id in tag.GetTaggedLocalElementIds()) Add(id.Value, tag.OwnerViewId);
            foreach (var tag in new FilteredElementCollector(_doc).OfCategory(BuiltInCategory.OST_RoomTags).OfType<RoomTag>())
                if (tag.TaggedLocalRoomId is { } room && room != ElementId.InvalidElementId) Add(room.Value, tag.OwnerViewId);
            return _tagsByElement;
        }

        private static string Noun(Element e) => (BuiltInCategory)(e.Category?.Id.Value ?? 0) switch
        {
            BuiltInCategory.OST_Doors => "door",
            BuiltInCategory.OST_Windows => "window",
            BuiltInCategory.OST_Rooms => "room",
            BuiltInCategory.OST_Walls => "wall",
            _ => "element",
        };

        private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    }
}
