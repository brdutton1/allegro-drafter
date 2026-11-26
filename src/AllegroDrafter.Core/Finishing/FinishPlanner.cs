using System.Text.RegularExpressions;
using AllegroDrafter.Core.Checks;
using AllegroDrafter.Core.Proposals;
using AllegroDrafter.Core.Standards;

namespace AllegroDrafter.Core.Finishing;

/// <summary>A question the rules can't settle. The designer clicks an option (or types, if AllowText).</summary>
/// <param name="Id">Stable across runs ("swing:5124872"), so an answer sticks to its question.</param>
/// <param name="Recommended">The option the rule leans to, shown first. Null when it has no lean.</param>
public sealed record QuestionCard(
    string Id,
    string Step,
    string Prompt,
    IReadOnlyList<string> Options,
    long? ElementId = null,
    string? Recommended = null,
    bool AllowText = false,
    string? TextLabel = null);

public abstract record FinishStep(string StepId, string StepTitle, int StepNumber);

/// <summary>Queue this as a proposal and wait for the designer.</summary>
public sealed record FinishBatch(string StepId, string StepTitle, int StepNumber, ProposalRequest Request) : FinishStep(StepId, StepTitle, StepNumber);

/// <summary>Show these cards and wait for answers.</summary>
public sealed record FinishQuestions(string StepId, string StepTitle, int StepNumber, IReadOnlyList<QuestionCard> Cards) : FinishStep(StepId, StepTitle, StepNumber);

/// <summary>Nothing left the button can do. What's left is listed for the designer.</summary>
public sealed record FinishDone(IReadOnlyList<string> Remaining) : FinishStep("done", "Done", FinishPlanner.Steps.Count + 1);

/// <summary>What the designer said during this run. The model is the rest of the state.</summary>
public sealed class FinishState
{
    public Dictionary<string, string> Answers { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Steps the designer rejected a batch for. Not planned again this run.</summary>
    public HashSet<string> SkippedSteps { get; } = new(StringComparer.OrdinalIgnoreCase);

    public const string Skip = "Skip";
    public const string DoneByHand = "Done by hand";

    public bool Answered(string id) => Answers.ContainsKey(id);
    public string? Answer(string id) => Answers.TryGetValue(id, out var a) ? a : null;
}

/// <summary>
/// Plans the Finish Drafting run one step at a time. Pure: reads facts, the audit and the
/// designer's answers; returns the next batch or the next questions. Every call starts from
/// the model as it is now, so an approved, rejected or undone batch just changes the input.
/// </summary>
public static class FinishPlanner
{
    public const int MaxChanges = 50;

    /// <summary>
    /// Run order. Anything that places a view (cover sheet) comes before view templates, so new
    /// views get a template in the same pass. View templates come before schedules, because a
    /// schedule template replaces the schedule's columns and sorting; the schedule step then puts
    /// the office columns back on top of it instead of being wiped by it.
    /// </summary>
    public static readonly IReadOnlyList<(string Id, string Title)> Steps = new[]
    {
        ("gate", "Wall types"),
        ("parameters", "Parameters"),
        ("type-marks", "Size-code Type Marks"),
        ("cover-sheet", "Cover sheet"),
        ("view-templates", "View templates"),
        ("schedules", "Door and window schedules"),
        ("tempered-egress", "Tempered and egress"),
        ("rooms", "Room numbers and sizes"),
        ("door-swing", "Door swing"),
        ("room-cleanup", "Unplaced rooms"),
        ("tags", "Tags"),
    };

    /// <summary>
    /// Why this batch waits for an Approve click, or null if the office rule applies it on its own.
    /// A batch that deletes always waits, whatever the setting says.
    /// </summary>
    public static string? WaitReason(FinishBatch batch, FinishDraftingSettings? settings)
    {
        if (batch.Request.Changes.Any(c => c.IsDestructive)) return "it deletes";
        var auto = settings?.AutoApplySteps ?? new FinishDraftingSettings().AutoApplySteps;
        return auto.Contains(batch.StepId, StringComparer.OrdinalIgnoreCase) ? null : "the office rule has you check this step";
    }

    public static FinishStep Next(SetFacts facts, AuditReport report, StandardsProfile? profile, FinishState state)
    {
        var s = profile?.Manual.Audit ?? new AuditSettings();
        var ctx = new Ctx(facts, report, profile, s, state);
        for (var i = 0; i < Steps.Count; i++)
        {
            var (id, title) = Steps[i];
            if (state.SkippedSteps.Contains(id)) continue;
            var step = id switch
            {
                "gate" => Gate(ctx),
                "parameters" => Parameters(ctx),
                "type-marks" => TypeMarks(ctx),
                "schedules" => Schedules(ctx),
                "tempered-egress" => TemperedEgress(ctx),
                "rooms" => Rooms(ctx),
                "door-swing" => DoorSwing(ctx),
                "room-cleanup" => RoomCleanup(ctx),
                "view-templates" => ViewTemplates(ctx),
                "tags" => Tags(ctx),
                "cover-sheet" => CoverSheet(ctx),
                _ => null,
            };
            if (step is null) continue;
            return step switch
            {
                Planned.Batch b => new FinishBatch(id, title, i + 1, b.Request),
                Planned.Ask q => new FinishQuestions(id, title, i + 1, q.Cards),
                _ => throw new InvalidOperationException(),
            };
        }
        return new FinishDone(Remaining(report));
    }

    // ------------------------------------------------------------------ steps

    private sealed record Ctx(SetFacts Facts, AuditReport Report, StandardsProfile? Profile, AuditSettings S, FinishState State)
    {
        public CheckResult? Check(string id) => Report.Checks.FirstOrDefault(c => c.Id == id);
        public IEnumerable<Finding> Findings(string id) => Check(id)?.Findings ?? Enumerable.Empty<Finding>();
    }

    private abstract record Planned
    {
        public sealed record Batch(ProposalRequest Request) : Planned;
        public sealed record Ask(IReadOnlyList<QuestionCard> Cards) : Planned;
    }

    private static Planned? Batch(string title, string rationale, IEnumerable<ProposalChange> changes)
    {
        var list = changes.Take(MaxChanges).ToList();
        return list.Count == 0 ? null : new Planned.Batch(new ProposalRequest(title, rationale, list));
    }

    /// <summary>Decided changes first; then cards nobody has answered; nothing if both are empty.</summary>
    private static Planned? BatchOrAsk(string title, string rationale, List<ProposalChange> changes, IEnumerable<QuestionCard> cards, FinishState state)
    {
        if (changes.Count > 0) return Batch(title, rationale, changes);
        var open = cards.Where(c => !state.Answered(c.Id)).ToList();
        return open.Count > 0 ? new Planned.Ask(open) : null;
    }

    private static Planned? Gate(Ctx c)
    {
        var changes = new List<ProposalChange>();
        var cards = new List<QuestionCard>();
        foreach (var f in c.Findings("wall-function"))
        {
            if (f.Target is null) continue;
            var id = "wall:" + f.Target;
            if (f.Suggestion is not null)
            {
                if (c.State.Answer(id) is not FinishState.Skip) changes.Add(new SetWallFunctionChange(f.Target, f.Suggestion));
                continue;
            }
            var answer = c.State.Answer(id);
            if (answer is "Interior") changes.Add(new SetWallFunctionChange(f.Target, "Interior"));
            else if (answer is null)
                cards.Add(new QuestionCard(id, "gate", f.Message, new[] { "Interior", "Keep Exterior" }, f.ElementId));
        }
        return BatchOrAsk("Wall types: Function",
            "A door or window counts as exterior only in an Exterior wall. These wall types say the wrong thing, which throws off tags and exterior dimensions.",
            changes, cards, c.State);
    }

    private static Planned? Parameters(Ctx c)
    {
        var s = c.S;
        var doors = c.Facts.Openings.Where(o => o.Category == "doors").ToList();
        var windows = c.Facts.Openings.Where(o => o.Category == "windows").ToList();
        var changes = new List<ProposalChange>();

        var temperedOn = new List<string>();
        if (doors.Count > 0 && doors.Any(d => !d.HasParameter(s.TemperedParameter))) temperedOn.Add("doors");
        if (windows.Count > 0 && windows.Any(w => !w.HasParameter(s.TemperedParameter))) temperedOn.Add("windows");
        if (temperedOn.Count > 0) changes.Add(new CreateSharedParameterChange(s.TemperedParameter, "yesno", temperedOn));
        if (windows.Count > 0 && windows.Any(w => !w.HasParameter(s.EgressParameter)))
            changes.Add(new CreateSharedParameterChange(s.EgressParameter, "yesno", new[] { "windows" }));
        if (doors.Count > 0 && doors.Any(d => !d.HasParameter(s.SwingParameter)))
            changes.Add(new CreateSharedParameterChange(s.SwingParameter, "text", new[] { "doors" }));

        var placed = c.Facts.Rooms.Where(r => r.State == "placed").ToList();
        if (placed.Count > 0 && placed.Any(r => !r.HasParameter(s.RoomSizeParameter)))
            changes.Add(new CreateSharedParameterChange(s.RoomSizeParameter, "text", new[] { "rooms" }));

        return Batch("Parameters the set needs",
            "Instance parameters the schedules and tags read. They're empty until the next steps fill them.", changes);
    }

    private static Planned? TypeMarks(Ctx c)
    {
        var changes = new List<ProposalChange>();
        var cards = new List<QuestionCard>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in c.Findings("type-marks").Concat(c.Findings("operation-codes")))
        {
            if (f.Target is null || !seen.Add(f.Target)) continue;
            var type = c.Facts.OpeningTypes.FirstOrDefault(t => t.Key == f.Target);
            if (type is null) continue;
            var id = "mark:" + type.Category + ":" + f.Target;
            var answer = c.State.Answer(id);
            if (answer is FinishState.Skip) continue;
            var mark = f.Suggestion ?? answer;
            if (mark is not null) { changes.Add(new SetTypeMarkChange(type.Category, f.Target, mark)); continue; }
            cards.Add(new QuestionCard(id, "type-marks", f.Message, new[] { FinishState.Skip }, f.ElementId,
                AllowText: true, TextLabel: "Type Mark (size code + operation, e.g. 3080 EXT)"));
        }
        return BatchOrAsk("Size-code Type Marks",
            "Type Mark is what the tags and schedules print. Each one is the size code from the family's real width and height, plus its operation.",
            changes, cards, c.State);
    }

    private static Planned? Schedules(Ctx c)
    {
        var s = c.S;
        var fieldChanges = new List<ProposalChange>();
        var sortChanges = new List<ProposalChange>();
        foreach (var (category, required) in new[] { ("doors", s.DoorScheduleFields), ("windows", s.WindowScheduleFields) })
        {
            var schedule = c.Facts.Schedules.FirstOrDefault(x => x.Category == category && !x.IsKeySchedule && x.OnSheets.Count > 0);
            if (schedule is null) continue; // creating a missing schedule is a designer call; the audit says so
            var oneLinePerDoor = category == "doors" && s.DoorScheduleOneLinePerDoor;

            var need = oneLinePerDoor ? required.Where(r => !Same(r, "Count")).ToList() : required;
            var missing = need.Where(r => !schedule.Fields.Any(x => Same(x.Name, r))).ToList();
            if (missing.Count > 0)
            {
                // Each missing column goes right after the one before it in the office order, so the schedule reads left to right.
                var adds = new List<FieldToAdd>();
                for (var i = 0; i < need.Count; i++)
                {
                    if (!missing.Contains(need[i])) continue;
                    var after = need.Take(i).LastOrDefault(prev => schedule.Fields.Any(x => Same(x.Name, prev)) || adds.Any(a => Same(a.Field, prev)));
                    adds.Add(after is null && schedule.Fields.Count > 0 ? new FieldToAdd(need[i], Before: schedule.Fields[0].Name) : new FieldToAdd(need[i], After: after));
                }
                fieldChanges.Add(new ScheduleFieldsChange(schedule.Name, adds, Array.Empty<string>()));
                continue; // sort after the columns exist
            }

            var group = s.ScheduleGroupField;
            if (oneLinePerDoor)
            {
                var sortBy = new List<string> { group };
                var from = schedule.Fields.FirstOrDefault(x => x.Name.StartsWith("From Room", StringComparison.OrdinalIgnoreCase))?.Name;
                if (from is not null) sortBy.Add(from);
                if (!schedule.SortGroupFields.SequenceEqual(sortBy, StringComparer.OrdinalIgnoreCase) || !schedule.IsItemized || !schedule.FirstGroupCounts)
                    sortChanges.Add(new ScheduleSortChange(schedule.Name, sortBy, Itemized: true, GroupHeaders: true));
            }
            else if (!Same(schedule.SortGroupFields.FirstOrDefault(), group) || schedule.IsItemized)
            {
                var sortBy = new List<string> { group };
                if (category == "windows")
                    sortBy.AddRange(new[] { s.TemperedParameter, s.EgressParameter }.Where(p => schedule.Fields.Any(x => Same(x.Name, p))));
                sortChanges.Add(new ScheduleSortChange(schedule.Name, sortBy, Itemized: false));
            }
        }
        if (fieldChanges.Count > 0)
            return Batch("Schedule columns", "The columns the builder reads: size code, where it goes, swing, tempered, egress.", fieldChanges);
        return Batch("Schedule layout",
            "Doors: a size-code header with the count, then one line per door showing where it goes. Windows: one row per size code with a count.", sortChanges);
    }

    private static Planned? TemperedEgress(Ctx c)
    {
        var s = c.S;
        var tempered = s.TemperedParameter;
        var egress = s.EgressParameter;
        var bedroom = SafeRegex(s.BedroomPattern);
        var rooms = c.Facts.Rooms.ToDictionary(r => r.ElementId);
        var candidates = c.Findings("tempered").Where(f => f.ElementId is not null).ToDictionary(f => f.ElementId!.Value);
        var reasonParam = s.TemperedReasonParameters.FirstOrDefault() ?? "Comments";

        var changes = new List<ProposalChange>();
        var cards = new List<QuestionCard>();
        foreach (var o in c.Facts.Openings.OrderBy(o => o.Category).ThenBy(o => o.Label))
        {
            var values = new List<ParameterValue>();
            var t = o.Value(tempered);
            if (o.HasParameter(tempered))
            {
                var glassDoor = o.Category == "doors" && s.GlassDoorKeywords.Any(k => o.TypeKey.Contains(k, StringComparison.OrdinalIgnoreCase));
                if (glassDoor && !IsYes(t) && candidates.ContainsKey(o.ElementId)) values.Add(new(tempered, "Yes"));
                else if (o.Category == "windows" && candidates.TryGetValue(o.ElementId, out var why))
                {
                    var id = "tempered:" + o.ElementId;
                    var answer = c.State.Answer(id);
                    if (answer == "Yes") values.Add(new(tempered, "Yes"));
                    else if (answer is not null && answer.StartsWith("No:", StringComparison.Ordinal))
                    {
                        values.Add(new(tempered, "No"));
                        values.Add(new(ReasonParameter(o, s, reasonParam), answer[3..].Trim()));
                    }
                    else if (answer is null)
                        cards.Add(new QuestionCard(id, "tempered-egress", why.Message, new[] { "Yes", "No" }, o.ElementId, "Yes",
                            AllowText: true, TextLabel: "If No: why (goes in " + reasonParam + ")"));
                }
                else if (string.IsNullOrWhiteSpace(t) && !glassDoor) values.Add(new(tempered, "No"));
            }
            if (o.Category == "windows" && o.HasParameter(egress) && string.IsNullOrWhiteSpace(o.Value(egress)))
            {
                var inBedroom = o.ServedRoomId is long rid && rooms.TryGetValue(rid, out var room) && bedroom?.IsMatch(room.Name) == true;
                var opens = SizeCode.Parse(c.Facts.OpeningTypes.FirstOrDefault(x => x.Key == o.TypeKey)?.TypeMark)?.Operation;
                var egressYes = inBedroom && opens is not null && s.EgressOperations.Contains(opens, StringComparer.OrdinalIgnoreCase)
                    && (o.SillFeet is not double sill || sill * 12 <= s.EgressMaxSillInches + 1e-6);
                values.Add(new(egress, egressYes ? "Yes" : "No"));
            }
            if (values.Count > 0) changes.Add(new SetParametersChange(o.ElementId, o.Label, values));
        }
        return BatchOrAsk("Tempered and egress",
            "Glass doors: Tempered = Yes. Bedroom windows that open: Egress = Yes. Everything else: No. Windows that look like hazardous locations are asked, not guessed.",
            changes, cards, c.State);
    }

    private static Planned? Rooms(Ctx c)
    {
        var s = c.S;
        var placed = c.Facts.Rooms.Where(r => r.State == "placed").ToList();
        if (placed.Count == 0) return null;
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var level in placed.Select(r => r.Level ?? "").Distinct())
            if (c.State.Answer("entry:" + level) is { } entry && entry != FinishState.Skip) overrides[level] = entry;

        var numbering = RoomNumbering.Number(placed, s, overrides);
        var cards = new List<QuestionCard>();
        var numbers = new Dictionary<long, string>();
        foreach (var level in numbering)
        {
            if (level.Blocked)
            {
                var id = "entry:" + level.Level;
                if (c.State.Answer(id) is FinishState.Skip) continue;
                var names = placed.Where(r => (r.Level ?? "") == level.Level).Select(r => r.Name).Distinct().OrderBy(n => n).ToList();
                cards.Add(new QuestionCard(id, "rooms", $"{level.Level}: {level.Question}", names.Append(FinishState.Skip).ToList()));
                continue;
            }
            foreach (var a in level.Assignments) numbers[a.RoomId] = a.NewNumber;
        }

        var changes = new List<ProposalChange>();
        foreach (var r in placed.OrderBy(r => numbers.TryGetValue(r.ElementId, out var n) ? n : "~"))
        {
            var split = RoomSizeSplit.Split(r.Number);
            var values = new List<ParameterValue>();
            if (split.Size is not null && r.HasParameter(s.RoomSizeParameter) && string.IsNullOrWhiteSpace(r.Value(s.RoomSizeParameter)))
                values.Add(new(s.RoomSizeParameter, split.Size));
            if (split.Note is not null)
            {
                var comments = r.Value("Comments");
                if (comments is null || !comments.Contains(split.Note, StringComparison.OrdinalIgnoreCase))
                    values.Add(new("Comments", string.IsNullOrWhiteSpace(comments) ? split.Note : comments.Trim() + " / " + split.Note));
            }
            if (numbers.TryGetValue(r.ElementId, out var number) && number != r.Number) values.Add(new("Number", number));
            // A size in Number with nowhere to go yet: leave the number alone until Room Size exists.
            if (split.Size is not null && !r.HasParameter(s.RoomSizeParameter)) values.RemoveAll(v => v.Name == "Number");
            if (values.Count > 0) changes.Add(new SetParametersChange(r.ElementId, $"Room \"{r.Name}\"", values));
        }
        return BatchOrAsk("Room numbers and sizes",
            "Numbers by floor (main 101 up), from the entry going clockwise. Sizes move out of Number into Room Size, which the room tag shows. Notes go to Comments.",
            changes, cards, c.State);
    }

    private static Planned? DoorSwing(Ctx c)
    {
        var s = c.S;
        var changes = new List<ProposalChange>();
        var cards = new List<QuestionCard>();
        var options = new[] { "LH", "RH", "LHR", "RHR", "POCKET", "SLIDING", FinishState.Skip };
        foreach (var o in c.Facts.Openings.Where(o => o.Category == "doors" && o.HasParameter(s.SwingParameter) && string.IsNullOrWhiteSpace(o.Value(s.SwingParameter))).OrderBy(o => o.Label))
        {
            var id = "swing:" + o.ElementId;
            var answer = c.State.Answer(id);
            if (answer is FinishState.Skip) continue;
            if (answer is not null) { changes.Add(new SetParametersChange(o.ElementId, o.Label, new[] { new ParameterValue(s.SwingParameter, answer) })); continue; }
            var geometry = c.Facts.DoorGeometry.FirstOrDefault(g => g.ElementId == o.ElementId);
            var result = geometry is null ? null : DoorHanding.Derive(geometry, s);
            if (result?.Value is not null)
                changes.Add(new SetParametersChange(o.ElementId, o.Label, new[] { new ParameterValue(s.SwingParameter, result.Value) }));
            else
                cards.Add(new QuestionCard(id, "door-swing", result?.Question ?? $"{o.Label}: which way does it swing?", options, o.ElementId));
        }
        return BatchOrAsk("Door swing",
            "Read from each door's hand and facing in the model. LH/RH standing on the side the door swings away from; exterior doors read from outside (R = swings out). Spot-check three doors on the plan before approving. If a family reads backwards, add it to manual.audit.handingFlipFamilies.",
            changes, cards, c.State);
    }

    private static Planned? RoomCleanup(Ctx c)
    {
        var leftovers = c.Facts.Rooms.Where(r => r.State is "unplaced" or "not enclosed").ToList();
        if (leftovers.Count == 0) return null;
        // Placed-but-not-enclosed rooms are kept (office rule): fix their boundaries instead.
        var unplaced = leftovers.Where(r => r.State == "unplaced").Select(r => r.ElementId).ToList();
        if (unplaced.Count == 0) return null;
        var names = string.Join(", ", leftovers.Where(r => r.State == "unplaced").GroupBy(r => r.Name).OrderByDescending(g => g.Count()).Take(5).Select(g => g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key));
        return Batch($"Delete {unplaced.Count} unplaced rooms",
            "Rooms not placed on any plan. They clutter the room schedule and the takeoff. Placed rooms are never deleted.",
            new ProposalChange[] { new DeleteRoomsChange(unplaced, false, names) });
    }

    private static readonly string[] ViewKeywords = { "FOUNDATION", "FRAMING", "SITE", "ROOF", "CEILING", "STRUCTURAL", "ELECTRICAL" };

    private static Planned? ViewTemplates(Ctx c)
    {
        var templates = c.Profile?.ViewTemplates ?? new List<ViewTemplateInfo>();
        if (templates.Count == 0) return null; // no office list to choose from; the audit reports it
        var required = c.S.TemplateRequiredViewTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var views = c.Facts.PlacedViews.Where(v => v.Template is null && required.Contains(v.ViewType)).ToList();

        var changes = new List<ProposalChange>();
        var cards = new List<QuestionCard>();

        // Revit only takes a template made for the same kind of view. When the office has none for a
        // kind, that's a template fix, not something to guess per job: say so once per kind and move on.
        bool HasChoices(string viewType) => templates.Any(t => Same(t.ViewType, viewType));
        foreach (var kind in views.Where(v => !HasChoices(v.ViewType)).GroupBy(v => v.ViewType, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key))
        {
            var id = $"template:{kind.Key}:none";
            if (c.State.Answered(id)) continue;
            var names = string.Join(", ", kind.Take(4).Select(v => v.Name)) + (kind.Count() > 4 ? ", ..." : "");
            cards.Add(new QuestionCard(id, "view-templates",
                $"The office template has no view template for {kind.Key} views ({names}), so the button can't apply one. Make one in the office template and re-export standards, or set these by hand.",
                new[] { FinishState.DoneByHand, FinishState.Skip }));
        }

        foreach (var group in views.Where(v => HasChoices(v.ViewType)).GroupBy(v => (v.ViewType, Keyword: ViewKeywords.FirstOrDefault(k => v.Name.Contains(k, StringComparison.OrdinalIgnoreCase)) ?? "")).OrderBy(g => g.Key.ViewType).ThenBy(g => g.Key.Keyword))
        {
            var (viewType, keyword) = group.Key;
            var choices = templates.Where(t => Same(t.ViewType, viewType)).Select(t => t.Name).ToList();
            var id = $"template:{viewType}:{keyword}";
            var rule = c.S.ViewTemplateRules.FirstOrDefault(r => Same(r.ViewType, viewType) && (string.IsNullOrEmpty(r.NameContains) ? keyword == "" : keyword.Equals(r.NameContains, StringComparison.OrdinalIgnoreCase)));
            var pick = rule?.Template ?? c.State.Answer(id);
            if (pick == FinishState.Skip) continue;
            if (pick is not null && choices.Contains(pick))
            {
                changes.AddRange(group.OrderBy(v => v.Name).Select(v => new ApplyViewTemplateChange(v.Name, pick, v.ViewType)));
                continue;
            }
            var lean = choices.OrderByDescending(n => keyword != "" && n.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(n => n.StartsWith("ADC", StringComparison.OrdinalIgnoreCase)).First();
            var what = keyword == "" ? $"{Plural(group.Count(), viewType + " view")}" : $"{Plural(group.Count(), keyword.ToLowerInvariant() + " " + viewType + " view")}";
            var sample = string.Join(", ", group.Take(4).Select(v => v.Name)) + (group.Count() > 4 ? ", ..." : "");
            cards.Add(new QuestionCard(id, "view-templates",
                $"Which office template for {what} ({sample})? Save the answer in manual.audit.viewTemplateRules to skip this question next job.",
                choices.OrderByDescending(n => n == lean).Append(FinishState.Skip).ToList(), Recommended: lean));
        }
        return BatchOrAsk("View templates",
            "Every placed view gets an office template, so lineweights, scale and visibility match sheet to sheet.",
            changes, cards, c.State);
    }

    private static Planned? Tags(Ctx c)
    {
        if (c.Check("tags") is not { Status: CheckStatus.Fail or CheckStatus.Warn }) return null;
        var planTypes = new[] { "FloorPlan" };
        var changes = new List<ProposalChange>();
        var needPlan = c.Facts.Openings.Any(o => !o.TaggedInPlacedPlan && !(o.SillFeet is double sill && sill * 12 >= c.S.PlanCutHeightInches));
        var needElevation = c.Facts.Openings.Any(o => o.IsExterior && !o.TaggedInPlacedElevation);
        var notPlanTagViews = ViewKeywords.Concat(new[] { "DECO", "LAYOUT", "FURNITURE", "AREA", "ENLARGED" }).ToList();
        foreach (var v in c.Facts.PlacedViews.OrderBy(v => v.Name))
        {
            var plan = planTypes.Contains(v.ViewType) && needPlan && !notPlanTagViews.Any(k => v.Name.Contains(k, StringComparison.OrdinalIgnoreCase));
            var elevation = v.ViewType == "Elevation" && needElevation && (v.ScaleDenominator ?? 0) >= 48;
            if (!plan && !elevation) continue;
            changes.Add(new TagViewChange(v.Name, "doors", ViewType: v.ViewType));
            changes.Add(new TagViewChange(v.Name, "windows", ViewType: v.ViewType));
        }
        return Batch("Door and window tags",
            "Untagged doors and windows on the floor plans, and exterior ones on the exterior elevations. Views with nothing to tag are left out.", changes);
    }

    private static Planned? CoverSheet(Ctx c)
    {
        var s = c.S;
        var cover = c.Facts.Sheets.FirstOrDefault(x => Same(x.Number, s.CoverSheetNumber));
        if (cover is null) return null;
        var changes = new List<ProposalChange>();
        var cards = new List<QuestionCard>();

        var sheetIndexPlaced = c.Facts.Schedules.Any(x => x.Category == "sheets" && x.OnSheets.Contains(cover.Number, StringComparer.OrdinalIgnoreCase));
        if (!sheetIndexPlaced)
        {
            var indexes = c.Facts.Schedules.Where(x => x.Category == "sheets").Select(x => x.Name).ToList();
            var named = indexes.FirstOrDefault(n => Same(n, s.SheetIndexSchedule));
            var id = "sheet-index";
            var pick = named ?? c.State.Answer(id);
            if (pick is not null && pick != FinishState.Skip && indexes.Contains(pick))
                changes.Add(new PlaceViewChange(cover.Number, pick, "Schedule"));
            else if (pick is null && indexes.Count > 0)
                cards.Add(new QuestionCard(id, "cover-sheet", $"Which sheet list goes on {cover.Number} as the sheet index?", indexes.Append(FinishState.Skip).ToList()));
        }

        var cs = c.Check("cover-sheet");
        foreach (var f in cs?.Findings ?? new List<Finding>())
        {
            var phrase = s.CoverSheetMustContain.FirstOrDefault(p => f.Message.Contains(p, StringComparison.OrdinalIgnoreCase));
            if (phrase is null) continue;
            cards.Add(new QuestionCard("cover-note:" + phrase, "cover-sheet",
                $"{cover.Number} needs a \"{phrase}\" note in the general notes. Add it to the office general notes legend once so every job has it. Done?",
                new[] { FinishState.DoneByHand, FinishState.Skip }));
        }
        return BatchOrAsk("Cover sheet", "The sheet index goes on the cover so the builder sees the whole set on page one.", changes, cards, c.State);
    }

    // ---------------------------------------------------------------- helpers

    private static IReadOnlyList<string> Remaining(AuditReport report) =>
        report.Checks.Where(c => c.Status == CheckStatus.Fail)
            .Select(c => $"{c.Title}: {c.Summary}")
            .ToList();

    private static string ReasonParameter(OpeningFact o, AuditSettings s, string fallback) =>
        s.TemperedReasonParameters.FirstOrDefault(o.HasParameter) ?? fallback;

    private static Regex? SafeRegex(string pattern)
    {
        try { return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
        catch (ArgumentException) { return null; }
    }

    private static bool IsYes(string? v) => v is not null && v.Trim().ToLowerInvariant() is "yes" or "y" or "true" or "1";
    private static bool Same(string? a, string? b) => a is not null && b is not null && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    private static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";
}
