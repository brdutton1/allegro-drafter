using System.Text.RegularExpressions;
using AllegroDrafter.Core.Proposals;

namespace AllegroDrafter.Core.Checks;

public enum CheckStatus { Pass, Warn, Fail, Skip }

public enum Severity { Warn, Fail }

/// <param name="Target">What a fix applies to, e.g. the "Family: Type" name for Type Mark findings.</param>
/// <param name="Suggestion">A value ready to propose, e.g. the Type Mark for propose_type_marks.</param>
public sealed record Finding(Severity Severity, string Message, long? ElementId = null, string? Target = null, string? Suggestion = null);

/// <summary>The Type Mark the audit would propose for a door or window type, or why it can't.</summary>
public sealed record MarkSuggestion(string? Mark, string Reason);

public sealed record CheckResult(string Id, string Title, CheckStatus Status, string Summary, int FindingCount, IReadOnlyList<Finding> Findings);

public sealed record AuditSummary(string Verdict, int Pass, int Warn, int Fail, int Skip);

public sealed record AuditReport(AuditSummary Summary, IReadOnlyList<CheckResult> Checks, IReadOnlyList<string> Notes);

/// <summary>
/// Read-only plan set audit: the expert drafter's punch list before issue.
/// Every check runs on <see cref="SetFacts"/>, so nothing here touches Revit
/// and nothing here changes the model.
/// </summary>
public static class SetAuditor
{
    private sealed record Ctx(
        SetFacts Facts,
        AuditSettings Settings,
        IReadOnlyCollection<string> OfficeTemplates,
        IReadOnlyDictionary<(string Category, string Key), MarkSuggestion> Marks);

    private sealed record CheckDef(string Id, string Title, Func<Ctx, CheckResult> Run);

    private static readonly CheckDef[] Catalog =
    {
        new("type-marks", "Size codes match the door and window sizes", TypeMarks),
        new("operation-codes", "Operation codes are office codes, no duplicate Type Marks", OperationCodes),
        new("schedules", "Door and window schedules report from Type Mark", Schedules),
        new("opening-parameters", "Doors and windows carry swing, tempered and egress values", OpeningParameters),
        new("tags", "Every door and window is tagged on a sheet", Tags),
        new("wall-function", "Wall types have the right Function (interior vs exterior)", WallFunction),
        new("wall-legend", "Wall legend matches the walls in the model", WallLegend),
        new("sheets", "Sheet numbers, names and sheet index", Sheets),
        new("view-scales", "Plans, elevations and sections are at a readable scale", ViewScales),
        new("text-heights", "Text and dimensions are at least 1/8\" tall", TextHeights),
        new("rooms", "Rooms are placed, named and numbered", Rooms),
        new("door-rooms", "Every door is next to a room", DoorRooms),
        new("view-templates", "Placed views use office view templates", ViewTemplates),
        new("dimension-closure", "Exterior dimension strings close to the overall", DimensionClosureCheck),
        new("egress", "Every bedroom has an egress window (IRC R310)", Egress),
        new("tempered", "Glazing that needs tempering is marked (IRC R308.4)", Tempered),
        new("references", "No hand-typed detail references", References),
        new("cover-sheet", "Cover sheet has the sheet index and required notes", CoverSheet),
    };

    public static IReadOnlyList<string> CheckIds => Catalog.Select(c => c.Id).ToList();

    public static AuditReport Run(SetFacts facts, AuditSettings? settings, IReadOnlyCollection<string>? officeTemplates = null, IReadOnlyCollection<string>? only = null)
    {
        settings ??= new AuditSettings();
        if (only is { Count: > 0 })
        {
            var unknown = only.Where(id => !Catalog.Any(c => c.Id == id)).ToList();
            if (unknown.Count > 0)
                throw new ToolArgumentException($"Unknown check '{string.Join("', '", unknown)}'. Valid checks: {string.Join(", ", CheckIds)}.");
        }

        var ctx = new Ctx(facts, settings, officeTemplates ?? Array.Empty<string>(), SuggestMarks(facts.OpeningTypes, settings));
        var disabled = new HashSet<string>(settings.DisabledChecks ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        var results = Catalog
            .Where(c => only is { Count: > 0 } ? only.Contains(c.Id) : !disabled.Contains(c.Id))
            .Select(c => c.Run(ctx))
            .ToList();

        var pass = results.Count(r => r.Status == CheckStatus.Pass);
        var warn = results.Count(r => r.Status == CheckStatus.Warn);
        var fail = results.Count(r => r.Status == CheckStatus.Fail);
        var skip = results.Count(r => r.Status == CheckStatus.Skip);
        var verdict = fail > 0
            ? $"Not ready to issue. {fail} check(s) failed."
            : warn > 0 ? $"No failures. Review {warn} warning(s) before issuing." : "Ready to issue on everything this audit checks.";

        var notes = new List<string>(facts.ReadWarnings)
        {
            "This audit reads model data only. It checks that exterior plan strings close to their overall, but not interior dimensions, tag label text, stair glazing (R308.4.6) or structural content yet.",
        };
        return new AuditReport(new AuditSummary(verdict, pass, warn, fail, skip), results, notes);
    }

    // ------------------------------------------------------------------ checks

    private static CheckResult TypeMarks(Ctx c)
    {
        var f = new List<Finding>();
        var tolerance = c.Settings.TypeMarkToleranceInches;
        foreach (var t in c.Facts.OpeningTypes.OrderBy(t => t.Category).ThenBy(t => t.Key))
        {
            var where = $"{Kind(t.Category)} type \"{t.Key}\" ({t.InstanceCount} placed)";
            Finding Fix(string message) => WithSuggestion(c, t, message);
            if (string.IsNullOrWhiteSpace(t.TypeMark))
            {
                f.Add(Fix($"{where} has no Type Mark."));
                continue;
            }
            var code = SizeCode.Parse(t.TypeMark);
            if (code is null)
            {
                f.Add(Fix($"{where} has Type Mark \"{t.TypeMark}\", which is not a size code."));
                continue;
            }
            if (t.WidthFeet is not double w || t.HeightFeet is not double h)
            {
                f.Add(new(Severity.Warn, $"{where}: couldn't read the type's Width/Height, so \"{t.TypeMark}\" wasn't checked against the geometry.", Target: t.Key));
                continue;
            }
            var (cw, ch, off) = code.Closest(w, h);
            if (off > tolerance + 1e-9)
                f.Add(Fix($"{where}: Type Mark \"{t.TypeMark}\" says {SizeCode.FeetInches(cw)} x {SizeCode.FeetInches(ch)}, but the type doesn't match. Fix whichever is wrong."));
        }
        return Make(c, "type-marks", f, $"{c.Facts.OpeningTypes.Count} door and window types. Every Type Mark is a size code that matches its type.");
    }

    private static CheckResult OperationCodes(Ctx c)
    {
        var f = new List<Finding>();
        foreach (var t in c.Facts.OpeningTypes.OrderBy(t => t.Category).ThenBy(t => t.Key))
        {
            var code = SizeCode.Parse(t.TypeMark);
            if (code is null) continue; // type-marks reports it
            var isWindow = t.Category == "windows";
            var allowed = isWindow ? c.Settings.WindowOperations : c.Settings.DoorOperations;
            if (code.Operation is null)
            {
                if (isWindow && c.Settings.WindowOperationRequired)
                    f.Add(WithSuggestion(c, t, $"Window type \"{t.Key}\": Type Mark \"{t.TypeMark}\" has no operation code. Add one of {string.Join(", ", allowed)}."));
                continue;
            }
            if (!allowed.Contains(code.Operation, StringComparer.OrdinalIgnoreCase))
            {
                var closest = Names.ClosestMatch(code.Operation, allowed);
                f.Add(WithSuggestion(c, t,
                    $"{Kind(t.Category)} type \"{t.Key}\": \"{code.Operation}\" in \"{t.TypeMark}\" is not an office operation code.{(closest is null ? "" : $" Did you mean {closest}?")} Allowed: {string.Join(", ", allowed)}."));
            }
        }

        foreach (var group in c.Facts.OpeningTypes
                     .Where(t => !string.IsNullOrWhiteSpace(t.TypeMark))
                     .GroupBy(t => (t.Category, Mark: Normalize(t.TypeMark!)))
                     .Where(g => g.Count() > 1)
                     .OrderBy(g => g.Key.Mark))
        {
            f.Add(new(Severity.Fail,
                $"Type Mark \"{group.First().TypeMark}\" is on {group.Count()} different {group.Key.Category} types: {string.Join(", ", group.Select(t => $"\"{t.Key}\""))}. The schedule merges them into one row and the tags can't tell them apart."));
        }
        return Make(c, "operation-codes", f, "Every operation code is an office code. No two types share a Type Mark.");
    }

    private static CheckResult Schedules(Ctx c)
    {
        var f = new List<Finding>();
        foreach (var (category, required) in new[] { ("doors", c.Settings.DoorScheduleFields), ("windows", c.Settings.WindowScheduleFields) })
        {
            var all = c.Facts.Schedules.Where(s => s.Category == category && !s.IsKeySchedule).ToList();
            if (all.Count == 0)
            {
                f.Add(new(Severity.Fail, $"No {Singular(category)} schedule in the model."));
                continue;
            }
            var placed = all.Where(s => s.OnSheets.Count > 0).ToList();
            if (placed.Count == 0)
                f.Add(new(Severity.Fail, $"{Cap(Singular(category))} schedule {Quote(all.Select(s => s.Name))} isn't on any sheet."));

            foreach (var s in placed.Count > 0 ? placed : all)
            {
                // Itemized with a count under each size code: the count lives in the footer, not a column.
                var need = s.CountsInGroupFooters ? required.Where(r => !Same(r, "Count")).ToList() : required;
                var missing = need.Where(r => !s.Fields.Any(x => Same(x.Name, r))).ToList();
                var hidden = need.Where(r => s.Fields.Any(x => Same(x.Name, r) && x.IsHidden)).ToList();
                if (missing.Count > 0)
                    f.Add(new(Severity.Fail, $"\"{s.Name}\" is missing {Join(missing)}. It has {Join(s.Fields.Where(x => !x.IsHidden).Select(x => x.Name))}."));
                if (hidden.Count > 0)
                    f.Add(new(Severity.Fail, $"\"{s.Name}\" hides {Join(hidden)}. Hidden fields don't print."));

                var group = c.Settings.ScheduleGroupField;
                if (!string.IsNullOrWhiteSpace(group))
                {
                    var first = s.SortGroupFields.FirstOrDefault();
                    if (!Same(first, group))
                        f.Add(new(Severity.Fail,
                            $"\"{s.Name}\" sorts by {(first is null ? "nothing" : $"\"{first}\"")} first. Sort/group by \"{group}\" first, so each row reads like a tag on the plan."));
                }
            }
        }
        return Make(c, "schedules", f, $"Door and window schedules are on sheets, sorted by {c.Settings.ScheduleGroupField}, with every required column.");
    }

    private static CheckResult OpeningParameters(Ctx c)
    {
        var f = new List<Finding>();
        var s = c.Settings;
        var rules = new[]
        {
            ("doors", s.SwingParameter, true),
            ("doors", s.TemperedParameter, false),
            ("windows", s.TemperedParameter, false),
            ("windows", s.EgressParameter, false),
        };
        // Root causes first (a parameter that doesn't exist), then the blanks.
        var blanks = new List<Finding>();
        foreach (var (category, parameter, blankFails) in rules)
        {
            if (string.IsNullOrWhiteSpace(parameter)) continue;
            var openings = c.Facts.Openings.Where(o => o.Category == category).ToList();
            if (openings.Count == 0) continue;

            var without = openings.Where(o => !o.HasParameter(parameter)).ToList();
            if (without.Count > 0)
            {
                f.Add(new(Severity.Fail,
                    $"{without.Count} of {openings.Count} {category} have no \"{parameter}\" parameter (e.g. {Join(without.Take(3).Select(o => o.Label))}). " +
                    (blankFails ? $"Add \"{parameter}\" to those families or as a project parameter." : $"Add a Yes/No project parameter \"{parameter}\" for {Cap(category)}, so the schedule can show it.")));
            }

            var severity = blankFails ? Severity.Fail : Severity.Warn;
            var blank = openings.Where(o => o.HasParameter(parameter) && string.IsNullOrWhiteSpace(o.Value(parameter))).ToList();
            if (blank.Count > 1 && blank.Count == openings.Count)
                blanks.Add(new(severity, $"\"{parameter}\" is blank on all {openings.Count} {category}."));
            else
                blanks.AddRange(blank.Select(o => new Finding(severity, $"{o.Label}: \"{parameter}\" is not set.", o.ElementId)));
        }
        f.AddRange(blanks);
        return Make(c, "opening-parameters", f, $"Every door has {s.SwingParameter} and {s.TemperedParameter}. Every window has {s.TemperedParameter} and {s.EgressParameter}.");
    }

    private static CheckResult Tags(Ctx c)
    {
        var f = new List<Finding>();
        var cut = c.Settings.PlanCutHeightInches / 12.0;
        foreach (var o in c.Facts.Openings.OrderBy(o => o.Category).ThenBy(o => o.Label))
        {
            // Clerestories and transoms sit above the plan cut. They don't show on the plan, so the elevation tag is the one that counts.
            var aboveCut = o.Category == "windows" && o.SillFeet is double sill && sill >= cut;
            if (aboveCut)
            {
                if (!o.TaggedInPlacedElevation && !o.TaggedInPlacedPlan)
                    f.Add(new(Severity.Fail, $"{o.Label} sits above the plan cut (sill {SizeCode.FeetInches(o.SillFeet!.Value)}), so it only shows on elevations, and it isn't tagged on any elevation that's on a sheet.", o.ElementId));
            }
            else if (!o.TaggedInPlacedPlan)
                f.Add(new(Severity.Fail, $"{o.Label} is not tagged on any plan that's on a sheet.", o.ElementId));
            else if (o.IsExterior && !o.TaggedInPlacedElevation)
                f.Add(new(c.Settings.RequireElevationTags ? Severity.Fail : Severity.Warn, $"{o.Label} is exterior but not tagged on any elevation that's on a sheet.", o.ElementId));
        }
        return Make(c, "tags", f, $"All {c.Facts.Openings.Count} doors and windows are tagged: on a placed plan, and exterior or above-cut ones on a placed elevation.");
    }

    private static CheckResult WallLegend(Ctx c)
    {
        var f = new List<Finding>();
        var legends = c.Facts.Legends.Where(l => c.Settings.WallLegendNames.Any(n => Same(n, l.Name))).ToList();
        if (legends.Count == 0)
            return Make(c, "wall-legend", new() { new(Severity.Warn, $"No wall legend found (looked for {Join(c.Settings.WallLegendNames)}).") }, "");

        var placed = legends.Where(l => l.OnSheets.Count > 0).ToList();
        if (placed.Count == 0)
            f.Add(new(Severity.Warn, $"Wall legend {Quote(legends.Select(l => l.Name))} isn't on any sheet. Checked it anyway."));

        var used = placed.Count > 0 ? placed : legends;
        var legendTypes = used.SelectMany(l => l.WallTypes).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var modeled = c.Facts.ModeledWallTypes.Where(t => !IsExempt(t, c.Settings.LegendExemptWallTypes)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var legendNames = Quote(used.Select(l => l.Name));

        foreach (var t in legendTypes.Where(t => !modeled.Contains(t) && !IsExempt(t, c.Settings.LegendExemptWallTypes)).OrderBy(t => t))
            f.Add(new(Severity.Fail, $"{legendNames} shows \"{t}\", but no wall in the model uses it."));
        foreach (var t in modeled.Where(t => !legendTypes.Contains(t)).OrderBy(t => t))
            f.Add(new(Severity.Fail, $"\"{t}\" is in the model but not in {legendNames}. Add it, or list it in manual.audit.legendExemptWallTypes if it's a modeling aid."));

        return Make(c, "wall-legend", f, $"Every wall type in the model is in the wall legend, and the legend shows nothing extra.");
    }

    private static CheckResult Sheets(Ctx c)
    {
        var f = new List<Finding>();
        Regex? pattern = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(c.Settings.SheetNumberPattern))
                pattern = new Regex(c.Settings.SheetNumberPattern, RegexOptions.CultureInvariant);
        }
        catch (ArgumentException)
        {
            f.Add(new(Severity.Warn, $"manual.audit.sheetNumberPattern \"{c.Settings.SheetNumberPattern}\" isn't a valid regex. Sheet numbers weren't checked."));
        }

        foreach (var s in c.Facts.Sheets.Where(s => !s.IsPlaceholder).OrderBy(s => s.Number, StringComparer.OrdinalIgnoreCase))
        {
            if (pattern is not null && !pattern.IsMatch(s.Number))
                f.Add(new(Severity.Fail, $"Sheet \"{s.Number}\" ({s.Name}) doesn't fit the office numbering pattern."));
            if (LooksUnfinished(s.Name))
                f.Add(new(Severity.Warn, $"Sheet {s.Number} name \"{s.Name}\" looks unfinished."));
            if (s.PlacedItemCount == 0)
                f.Add(new(Severity.Warn, $"Sheet {s.Number} ({s.Name}) has nothing on it."));
        }

        if (!c.Facts.Schedules.Any(x => x.Category == "sheets" && x.OnSheets.Count > 0))
            f.Add(new(Severity.Fail, "No sheet index (sheet list schedule) is placed on any sheet."));

        return Make(c, "sheets", f, $"{c.Facts.Sheets.Count} sheets. Numbers fit the pattern, none is empty, and a sheet index is placed.");
    }

    private static readonly HashSet<string> ScaledViewTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "FloorPlan", "CeilingPlan", "EngineeringPlan", "AreaPlan", "Elevation", "Section",
    };

    private static CheckResult ViewScales(Ctx c)
    {
        var f = new List<Finding>();
        foreach (var v in c.Facts.PlacedViews.Where(v => ScaledViewTypes.Contains(v.ViewType) && v.ScaleDenominator is > 0).OrderBy(v => v.Name))
        {
            if (c.Settings.ScaleExemptKeywords.Any(k => v.Name.Contains(k, StringComparison.OrdinalIgnoreCase))) continue;
            var scale = v.ScaleDenominator!.Value;
            var where = $"{v.Name} ({v.ViewType}, on {Join(v.OnSheets)})";
            if (scale > c.Settings.MaxScaleDenominator)
                f.Add(new(Severity.Fail, $"{where} is at {ScaleLabel(scale)}. Smallest allowed is {ScaleLabel(c.Settings.MaxScaleDenominator)}."));
            else if (scale > c.Settings.PreferredScaleDenominator)
                f.Add(new(Severity.Warn, $"{where} is at {ScaleLabel(scale)}. {ScaleLabel(c.Settings.PreferredScaleDenominator)} reads better in the field."));
        }
        return Make(c, "view-scales", f, $"Every placed plan, elevation and section is {ScaleLabel(c.Settings.PreferredScaleDenominator)} or larger.");
    }

    /// <summary>
    /// An opening is exterior only when its host wall type is Function = Exterior. So a wrong
    /// Function poisons the tags check, elevation tagging and exterior dimensions. This finds it.
    /// </summary>
    private static CheckResult WallFunction(Ctx c)
    {
        if (c.Facts.WallTypes.Count == 0) return Skip("wall-function", "No wall types were read from the model.");
        var f = new List<Finding>();
        var flagged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in c.Facts.WallTypes.Where(t => t.Kind != "Stacked").OrderBy(t => t.Name))
        {
            var typeName = t.Name.Split(": ", 2).Last();
            var interior = HasWord(typeName, c.Settings.InteriorWallKeywords);
            var exterior = HasWord(typeName, c.Settings.ExteriorWallKeywords);
            if (interior == exterior) continue; // neither, or both: the name doesn't say
            var where = $"Wall type \"{t.Name}\" ({Plural(t.InstanceCount, "wall")}, {Math.Round(t.LengthFeet)} ft)";
            if (interior && Same(t.Function, "Exterior"))
            {
                flagged.Add(t.Name);
                f.Add(new(Severity.Fail, $"{where} is named interior but its Function is Exterior. Every door and window in it reads as exterior, so tags, elevation tags and exterior dimensions get it wrong. Set Function to Interior.", Target: t.Name, Suggestion: "Interior"));
            }
            else if (exterior && Same(t.Function, "Interior"))
            {
                flagged.Add(t.Name);
                f.Add(new(Severity.Fail, $"{where} is named exterior but its Function is Interior. Its doors and windows won't get elevation tags or exterior dimensions. Set Function to Exterior.", Target: t.Name, Suggestion: "Exterior"));
            }
        }

        // An exterior opening with a room on both sides: the wall may be misclassified even if its name doesn't say.
        foreach (var g in c.Facts.Openings
                     .Where(o => o.IsExterior && o.FromRoomId is not null && o.ToRoomId is not null && o.FromRoomId != o.ToRoomId
                                 && o.HostWallType is not null && !flagged.Contains(o.HostWallType))
                     .GroupBy(o => o.HostWallType!)
                     .OrderBy(g => g.Key))
        {
            var list = g.OrderBy(o => o.Label).ToList();
            f.Add(new(Severity.Warn,
                $"{Plural(list.Count, "opening")} in \"{g.Key}\" (Function = Exterior) {(list.Count == 1 ? "has" : "have")} a room on both sides: {Join(list.Take(5).Select(o => o.Label))}{(list.Count > 5 ? ", ..." : "")}. If it's an interior wall, set its Function to Interior. A wall to a garage or porch room can be right as is.",
                list[0].ElementId, Target: g.Key));
        }
        return Make(c, "wall-function", f, $"{Plural(c.Facts.WallTypes.Count, "wall type")}. Every Function matches what the name says, and no exterior opening has rooms on both sides.");
    }

    private static bool HasWord(string text, IEnumerable<string> words) =>
        words.Any(w => !string.IsNullOrWhiteSpace(w)
                       && Regex.IsMatch(text, $@"(?<![A-Za-z]){Regex.Escape(w.Trim())}(?![A-Za-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

    private static CheckResult TextHeights(Ctx c)
    {
        var f = new List<Finding>();
        var min = c.Settings.MinTextHeightInches;
        foreach (var u in c.Facts.AnnotationTypes.OrderBy(u => u.Kind).ThenBy(u => u.TypeName))
        {
            var where = $"{Cap(u.Kind)} type \"{u.TypeName}\" ({Plural(u.Count, "use")} on sheets{(u.SampleView is null ? "" : $", e.g. in {u.SampleView}")})";
            if (u.HeightInches is not double h)
                f.Add(new(Severity.Warn, $"{where}: couldn't read its text height."));
            else if (h < min - 1e-6)
                f.Add(new(Severity.Fail, $"{where} is {MeasuredInchesLabel(h, min)} tall. Minimum is {InchesLabel(min)}."));
        }
        return Make(c, "text-heights", f, $"All text and dimension types used on sheets are {InchesLabel(min)} or taller.");
    }

    private static CheckResult Rooms(Ctx c)
    {
        var f = new List<Finding>();
        var unplaced = c.Facts.Rooms.Where(r => r.State == "unplaced").ToList();
        if (unplaced.Count > 0)
            f.Add(new(Severity.Warn, $"{unplaced.Count} unplaced rooms are in the model (not on any plan). They clutter schedules. Delete them or place them."));

        foreach (var r in c.Facts.Rooms.Where(r => r.State == "not enclosed"))
            f.Add(new(Severity.Fail, $"Room {RoomLabel(r)} is not enclosed or is redundant. It has no area.", r.ElementId));

        foreach (var r in c.Facts.Rooms.Where(r => r.State == "placed").OrderBy(r => r.Name))
        {
            if (string.IsNullOrWhiteSpace(r.Name))
                f.Add(new(Severity.Fail, $"Room {RoomLabel(r)} has no name.", r.ElementId));
            if (string.IsNullOrWhiteSpace(r.Number))
                f.Add(new(Severity.Fail, $"Room {RoomLabel(r)} has no number.", r.ElementId));
            else if (Regex.IsMatch(r.Number, "['\"]|\\d\\s*[xX]\\s*\\d"))
                f.Add(new(Severity.Fail, $"Room {r.Name} has number \"{r.Number}\". That's a size or a note, not a room number. Move it to its own parameter or the tag.", r.ElementId));
        }
        var placedCount = c.Facts.Rooms.Count(r => r.State == "placed");
        return Make(c, "rooms", f, $"{placedCount} rooms, all placed, named and numbered.");
    }

    private static CheckResult DoorRooms(Ctx c)
    {
        var f = c.Facts.Openings
            .Where(o => o.Category == "doors" && o.FromRoomId is null && o.ToRoomId is null)
            .OrderBy(o => o.Label)
            .Select(o => new Finding(Severity.Warn, $"{o.Label} isn't next to any room. Room boundaries are missing around it, so schedules can't say where it is.", o.ElementId))
            .ToList();
        return Make(c, "door-rooms", f, "Every door opens to at least one room.");
    }

    private static CheckResult ViewTemplates(Ctx c)
    {
        var f = new List<Finding>();
        var required = c.Settings.TemplateRequiredViewTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var office = c.OfficeTemplates.ToHashSet(StringComparer.Ordinal);
        foreach (var v in c.Facts.PlacedViews.Where(v => required.Contains(v.ViewType)).OrderBy(v => v.Name))
        {
            var where = $"{v.Name} ({v.ViewType}, on {Join(v.OnSheets)})";
            if (v.Template is null)
                f.Add(new(Severity.Fail, $"{where} has no view template."));
            else if (office.Count > 0 && !office.Contains(v.Template))
                f.Add(new(Severity.Fail, $"{where} uses \"{v.Template}\", which isn't in the office standards."));
        }
        var count = c.Facts.PlacedViews.Count(v => required.Contains(v.ViewType));
        return Make(c, "view-templates", f, $"All {count} placed views that need one use an office view template.");
    }

    /// <summary>
    /// On each side of each placed floor plan, the widest string is the overall. Every other string that
    /// spans most of the side (the openings and walls tiers) must start and end where the overall does,
    /// so a builder adding up any tier gets the overall. Short local strings (a porch, a bay) are left
    /// alone. A warning, not a failure: the designer may close a tier to something else on purpose.
    /// </summary>
    private static CheckResult DimensionClosureCheck(Ctx c)
    {
        if (c.Facts.PlanStrings.Count == 0) return Skip("dimension-closure", "No exterior dimension strings were found on placed floor plans.");
        const double tol = 1.0 / 192; // 1/16"
        var f = new List<Finding>();
        foreach (var group in c.Facts.PlanStrings.GroupBy(s => (s.ViewName, s.Side)).OrderBy(g => g.Key.ViewName).ThenBy(g => g.Key.Side))
        {
            var overall = group.MaxBy(s => s.Hi - s.Lo)!;
            var span = overall.Hi - overall.Lo;
            foreach (var s in group.Where(s => s != overall && s.Hi - s.Lo >= 0.6 * span).OrderBy(s => s.Lo))
            {
                var startOff = s.Lo - overall.Lo;
                var endOff = overall.Hi - s.Hi;
                if (Math.Abs(startOff) <= tol && Math.Abs(endOff) <= tol) continue;
                var parts = new List<string>();
                if (Math.Abs(startOff) > tol) parts.Add($"starts {Drafting.DimensionClosure.Feet(Math.Abs(startOff))} {(startOff > 0 ? "inside" : "outside")} the overall");
                if (Math.Abs(endOff) > tol) parts.Add($"ends {Drafting.DimensionClosure.Feet(Math.Abs(endOff))} {(endOff > 0 ? "short of" : "past")} the overall");
                f.Add(new(Severity.Warn, $"{group.Key.ViewName}, {group.Key.Side} side: a {s.Segments}-segment string {string.Join(" and ", parts)} ({Drafting.DimensionClosure.Feet(span)}). It won't add up to the overall.", s.ElementId));
            }
        }
        var views = c.Facts.PlanStrings.Select(s => s.ViewName).Distinct().Count();
        return Make(c, "dimension-closure", f, $"Every exterior tier closes to its overall on {Plural(views, "floor plan")}.");
    }

    private static CheckResult Egress(Ctx c)
    {
        Regex bedroom;
        try { bedroom = new Regex(c.Settings.BedroomPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
        catch (ArgumentException)
        {
            return Skip("egress", $"manual.audit.bedroomPattern \"{c.Settings.BedroomPattern}\" isn't a valid regex.");
        }

        var bedrooms = c.Facts.Rooms.Where(r => r.State == "placed" && bedroom.IsMatch(r.Name)).OrderBy(r => r.Name).ToList();
        if (bedrooms.Count == 0) return Skip("egress", "No placed rooms are named like bedrooms.");

        var f = new List<Finding>();
        var egress = c.Settings.EgressParameter;
        var maxSill = c.Settings.EgressMaxSillInches / 12.0;
        foreach (var room in bedrooms)
        {
            var windows = c.Facts.Openings.Where(o => o.Category == "windows" && o.ServedRoomId == room.ElementId).ToList();
            if (windows.Count == 0)
            {
                f.Add(new(Severity.Fail, $"{RoomLabel(room)} has no windows. A bedroom needs an egress opening.", room.ElementId));
                continue;
            }
            var marked = windows.Where(o => IsYes(o.Value(egress))).ToList();
            if (marked.Count == 0)
            {
                var why = windows.Any(o => o.HasParameter(egress)) ? "" : $" (the {egress} parameter doesn't exist yet)";
                f.Add(new(Severity.Fail,
                    $"{RoomLabel(room)}: no window is marked {egress} = Yes{why}. Windows in the room: {Join(windows.Select(o => o.Label))}.", room.ElementId));
                continue;
            }
            if (marked.All(o => o.SillFeet is double s && s > maxSill + 1e-6))
            {
                var o = marked[0];
                f.Add(new(Severity.Fail,
                    $"{RoomLabel(room)}: egress window {o.Label} has its sill at {SizeCode.FeetInches(o.SillFeet!.Value)}. Max is {InchesLabel(c.Settings.EgressMaxSillInches)} above the floor.", o.ElementId));
            }
        }
        return Make(c, "egress", f, $"{bedrooms.Count} bedrooms, each with a window marked {egress} = Yes and a sill at {InchesLabel(c.Settings.EgressMaxSillInches)} or lower. Net clear opening is not checked (needs manufacturer data).");
    }

    private static CheckResult Tempered(Ctx c)
    {
        var f = new List<Finding>();
        var tempered = c.Settings.TemperedParameter;
        var doors = c.Facts.Openings.Where(o => o.Category == "doors").ToList();
        var rooms = c.Facts.Rooms.ToDictionary(r => r.ElementId);

        foreach (var d in doors.OrderBy(d => d.Label))
        {
            if (IsYes(d.Value(tempered)) || IsDecidedNo(d, c.Settings)) continue;
            if (c.Settings.GlassDoorKeywords.Any(k => d.TypeKey.Contains(k, StringComparison.OrdinalIgnoreCase)))
                f.Add(new(Severity.Fail, $"{d.Label} has glass but isn't marked {tempered} = Yes{NoWithoutReason(d, c.Settings)}. Glazing in doors must be tempered (R308.4.1).", d.ElementId));
        }

        foreach (var w in c.Facts.Openings.Where(o => o.Category == "windows").OrderBy(o => o.Label))
        {
            if (IsYes(w.Value(tempered)) || IsDecidedNo(w, c.Settings) || w.SillFeet is not double sill) continue;
            var reasons = new List<string>();

            if (sill < 5.0 && w.X is double wx && w.Y is double wy)
            {
                var near = doors.FirstOrDefault(d => d.Level == w.Level && d.X is double dx && d.Y is double dy
                    && Math.Sqrt((wx - dx) * (wx - dx) + (wy - dy) * (wy - dy)) - ((w.WidthFeet ?? 0) + (d.WidthFeet ?? 0)) / 2 <= 2.0);
                if (near is not null) reasons.Add($"within 24\" of {near.Label} with its sill under 60\" (R308.4.2)");
            }
            if (sill < 5.0 && w.ServedRoomId is long roomId && rooms.TryGetValue(roomId, out var room) && room.HasWetFixture)
                reasons.Add($"in {RoomLabel(room)}, which has a tub or shower, with its sill under 60\" (R308.4.5)");
            if (w.WidthFeet is double ww && w.HeightFeet is double wh && ww * wh > 9.0 && sill < 1.5 && sill + wh > 3.0)
                reasons.Add($"over 9 sf with its sill under 18\" (R308.4.3)");

            if (reasons.Count > 0)
                f.Add(new(Severity.Warn, IsNo(w.Value(tempered))
                    ? $"{w.Label} may need tempered glass: {string.Join("; ", reasons)}. It's set to {tempered} = No with no reason. Add why to {ReasonParameterLabel(c.Settings)}, or set it to Yes."
                    : $"{w.Label} may need tempered glass: {string.Join("; ", reasons)}. Confirm, then set {tempered} = Yes, or set No and add why to {ReasonParameterLabel(c.Settings)}.", w.ElementId));
        }
        return Make(c, "tempered", f, $"Glass doors are marked {tempered}, and no window sits in a hazardous location unmarked. Stairs are not checked yet.");
    }

    private static readonly Regex TypedReference = new(@"(?<![\d/])(\d{1,2})\s*/\s*([A-Z]{1,2}\d{1,2}(?:\.\d{1,2})?)\b", RegexOptions.CultureInvariant);

    private static CheckResult References(Ctx c)
    {
        var f = new List<Finding>();
        var sheets = c.Facts.Sheets.Select(s => s.Number).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in c.Facts.Texts)
        {
            foreach (Match m in TypedReference.Matches(t.Text.ToUpperInvariant()))
            {
                var reference = $"{m.Groups[1].Value}/{m.Groups[2].Value}";
                if (!seen.Add($"{t.ViewName}|{reference}")) continue;
                var sheet = m.Groups[2].Value;
                f.Add(sheets.Contains(sheet)
                    ? new(Severity.Warn, $"\"{reference}\" in {t.ViewName} is typed by hand. If that detail moves, this won't update. Check it, or use a live callout.")
                    : new(Severity.Fail, $"\"{reference}\" in {t.ViewName} points to sheet {sheet}, which doesn't exist."));
            }
        }
        return Make(c, "references", f, "No hand-typed detail references in text on sheets.");
    }

    private static CheckResult CoverSheet(Ctx c)
    {
        var number = c.Settings.CoverSheetNumber;
        var cover = c.Facts.Sheets.FirstOrDefault(s => Same(s.Number, number));
        if (cover is null)
            return Make(c, "cover-sheet", new() { new(Severity.Fail, $"No cover sheet numbered \"{number}\".") }, "");

        var f = new List<Finding>();
        if (!c.Facts.Schedules.Any(s => s.Category == "sheets" && s.OnSheets.Contains(number, StringComparer.OrdinalIgnoreCase)))
            f.Add(new(Severity.Fail, $"Cover sheet {number} has no sheet index on it."));

        var text = string.Join("\n", c.Facts.Texts.Where(t => t.SheetNumbers.Contains(number, StringComparer.OrdinalIgnoreCase)).Select(t => t.Text));
        var flat = Regex.Replace(text, @"\s+", " ");
        foreach (var phrase in c.Settings.CoverSheetMustContain.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            if (!flat.Contains(Regex.Replace(phrase, @"\s+", " "), StringComparison.OrdinalIgnoreCase))
                f.Add(new(Severity.Fail, $"Cover sheet {number} doesn't say \"{phrase}\" anywhere."));
        }
        return Make(c, "cover-sheet", f, $"Cover sheet {number} has the sheet index and {Join(c.Settings.CoverSheetMustContain.Select(p => $"\"{p}\""))}.");
    }

    // ----------------------------------------------------------------- helpers

    private static CheckResult Make(Ctx c, string id, List<Finding> findings, string passSummary)
    {
        var title = Catalog.First(x => x.Id == id).Title;
        if (findings.Count == 0) return new CheckResult(id, title, CheckStatus.Pass, passSummary, 0, Array.Empty<Finding>());

        var fails = findings.Count(x => x.Severity == Severity.Fail);
        var warns = findings.Count - fails;
        var status = fails > 0 ? CheckStatus.Fail : CheckStatus.Warn;
        var parts = new List<string>();
        if (fails > 0) parts.Add(Plural(fails, "problem"));
        if (warns > 0) parts.Add(Plural(warns, "warning"));
        var max = Math.Max(1, c.Settings.MaxFindingsPerCheck);
        var shown = findings.OrderBy(x => x.Severity == Severity.Fail ? 0 : 1).Take(max).ToList();
        var summary = string.Join(", ", parts) + "." + (findings.Count > shown.Count ? $" Showing the first {shown.Count}." : "");
        return new CheckResult(id, title, status, summary, findings.Count, shown);
    }

    private static CheckResult Skip(string id, string why) =>
        new(id, Catalog.First(x => x.Id == id).Title, CheckStatus.Skip, why, 0, Array.Empty<Finding>());

    /// <summary>A failing Type Mark finding, with the fix attached when there is one.</summary>
    private static Finding WithSuggestion(Ctx c, OpeningTypeFact t, string message)
    {
        c.Marks.TryGetValue((t.Category, t.Key), out var s);
        return new Finding(Severity.Fail, s is null ? message : $"{message} {s.Reason}", Target: t.Key, Suggestion: s?.Mark);
    }

    /// <summary>
    /// The Type Mark each failing door/window type should get: the size from its real
    /// Width/Height, plus an operation code (kept if valid, corrected if close, else read
    /// from the family name). Suggestions that would collide with another type's mark are
    /// withheld, because two types on one mark merge in the schedule.
    /// </summary>
    public static IReadOnlyDictionary<(string Category, string Key), MarkSuggestion> SuggestMarks(IEnumerable<OpeningTypeFact> types, AuditSettings s)
    {
        var result = new Dictionary<(string, string), MarkSuggestion>();
        var list = types.ToList();
        foreach (var t in list)
        {
            if (t.WidthFeet is not double w || t.HeightFeet is not double h) continue;
            var isWindow = t.Category == "windows";
            var allowed = isWindow ? s.WindowOperations : s.DoorOperations;
            var code = SizeCode.Parse(t.TypeMark);
            var sizeOk = code is not null && code.Closest(w, h).OffInches <= s.TypeMarkToleranceInches + 1e-9;
            var opOk = code?.Operation is null
                ? !(isWindow && s.WindowOperationRequired)
                : allowed.Contains(code.Operation, StringComparer.OrdinalIgnoreCase);
            if (code is not null && sizeOk && opOk) continue; // nothing to fix

            var size = $"{CodePart(w)}{CodePart(h)}";
            var real = $"{SizeCode.FeetInches(w)} x {SizeCode.FeetInches(h)}";
            string? op = null;
            if (code?.Operation is { } existing)
                op = allowed.FirstOrDefault(a => string.Equals(a, existing, StringComparison.OrdinalIgnoreCase)) ?? Names.ClosestMatch(existing, allowed);
            op ??= InferOperation(t, isWindow ? s.WindowOperationKeywords : s.DoorOperationKeywords, allowed);

            if (isWindow && s.WindowOperationRequired && op is null)
            {
                result[(t.Category, t.Key)] = new(null,
                    $"The type is {real}: \"{size}\" plus its operation ({string.Join(", ", allowed)}). Couldn't tell the operation from the family name, so ask.");
                continue;
            }
            var mark = op is null ? size : $"{size} {op}";
            result[(t.Category, t.Key)] = new(mark, $"The type is {real}, so it should be \"{mark}\".");
        }

        // Withhold suggestions that would put two types on one mark.
        var finals = list.Select(t => (t, Mark: result.TryGetValue((t.Category, t.Key), out var m) && m.Mark is not null ? m.Mark : t.TypeMark))
            .Where(x => !string.IsNullOrWhiteSpace(x.Mark))
            .GroupBy(x => (x.t.Category, Mark: Normalize(x.Mark!)))
            .Where(g => g.Count() > 1);
        foreach (var group in finals)
        {
            foreach (var (t, mark) in group)
            {
                if (!result.TryGetValue((t.Category, t.Key), out var m) || m.Mark is null) continue;
                var others = string.Join(", ", group.Where(x => x.t.Key != t.Key).Select(x => $"\"{x.t.Key}\""));
                result[(t.Category, t.Key)] = new(null,
                    $"Its size reads \"{mark}\", but that would match {others}, and the schedule would merge them. Pick a different operation code or confirm the size, then propose it.");
            }
        }
        return result;
    }

    private static string? InferOperation(OpeningTypeFact t, IEnumerable<OperationKeyword> keywords, IReadOnlyCollection<string> allowed)
    {
        var name = Regex.Replace(t.Key.ToUpperInvariant().Replace('_', ' ').Replace('-', ' '), @"\s+", " ");
        foreach (var k in keywords)
        {
            if (name.Contains(k.Keyword.ToUpperInvariant(), StringComparison.Ordinal) && allowed.Contains(k.Code, StringComparer.OrdinalIgnoreCase))
                return k.Code;
        }
        return null;
    }

    private static string CodePart(double feet)
    {
        var totalInches = (int)Math.Round(feet * 12);
        return $"{totalInches / 12}{totalInches % 12}";
    }

    public static string ScaleLabel(int denominator)
    {
        if (denominator <= 0) return "unknown scale";
        if (denominator >= 120 && denominator % 12 == 0) return $"1\" = {denominator / 12}'-0\"";
        var sixtyFourths = 12.0 * 64 / denominator;
        if (Math.Abs(sixtyFourths - Math.Round(sixtyFourths)) > 1e-9) return $"1:{denominator}";
        return $"{InchesLabel(sixtyFourths / 64)} = 1'-0\"";
    }

    /// <summary>0.09375 to 3/32", 0.125 to 1/8", 1.5 to 1 1/2".</summary>
    public static string InchesLabel(double inches)
    {
        var n = (int)Math.Round(inches * 64);
        var whole = n / 64;
        var rest = n % 64;
        if (rest == 0) return $"{whole}\"";
        var den = 64;
        while (rest % 2 == 0) { rest /= 2; den /= 2; }
        return whole > 0 ? $"{whole} {rest}/{den}\"" : $"{rest}/{den}\"";
    }

    /// <summary>
    /// A measured height for a message. Exact 64ths read as fractions; anything else shows
    /// the decimal so "1/8\"" never appears for a type that's really 0.120".
    /// </summary>
    public static string MeasuredInchesLabel(double inches, double reference)
    {
        if (Math.Abs(inches * 64 - Math.Round(inches * 64)) < 1e-4) return InchesLabel(inches);
        var dec = inches.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + "\"";
        var near = InchesLabel(reference);
        return InchesLabel(inches) == near
            ? $"{dec} (just {(inches < reference ? "under" : "over")} {near})"
            : dec;
    }

    private static bool IsYes(string? value) =>
        value is not null && value.Trim().ToLowerInvariant() is "yes" or "y" or "true" or "1";

    private static bool IsNo(string? value) =>
        value is not null && value.Trim().ToLowerInvariant() is "no" or "n" or "false" or "0";

    private static string? TemperedReason(OpeningFact o, AuditSettings s) =>
        s.TemperedReasonParameters.Select(o.Value).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>Tempered = No with a reason written down: the designer decided, the audit stops asking.</summary>
    private static bool IsDecidedNo(OpeningFact o, AuditSettings s) =>
        IsNo(o.Value(s.TemperedParameter)) && TemperedReason(o, s) is not null;

    private static string NoWithoutReason(OpeningFact o, AuditSettings s) =>
        IsNo(o.Value(s.TemperedParameter)) ? $" (it's No with no reason in {ReasonParameterLabel(s)})" : "";

    private static string ReasonParameterLabel(AuditSettings s) =>
        s.TemperedReasonParameters.Count == 0 ? "Comments" : string.Join(" or ", s.TemperedReasonParameters);

    private static bool IsExempt(string wallType, IEnumerable<string> exempt) =>
        exempt.Any(e => Same(e, wallType) || Same(e, wallType.Split(": ", 2).Last()));

    private static bool LooksUnfinished(string name) =>
        string.IsNullOrWhiteSpace(name)
        || Regex.IsMatch(name, @"[-–:,]\s*$")
        || Regex.IsMatch(name, @"\b(unnamed|copy \d+|copy)\b", RegexOptions.IgnoreCase);

    private static string RoomLabel(RoomFact r) =>
        string.IsNullOrWhiteSpace(r.Number) ? $"\"{r.Name}\"" : $"\"{r.Name}\" ({r.Number})";

    private static string Normalize(string s) => Regex.Replace(s.Trim().ToUpperInvariant(), @"\s+", " ");
    private static bool Same(string? a, string? b) => a is not null && b is not null && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    private static string Join(IEnumerable<string> items) { var list = items.ToList(); return list.Count == 0 ? "nothing" : string.Join(", ", list); }
    private static string Quote(IEnumerable<string> items) => string.Join(", ", items.Select(i => $"\"{i}\""));
    private static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";
    private static string Kind(string category) => category == "windows" ? "Window" : category == "doors" ? "Door" : Cap(category);
    private static string Singular(string category) => category.EndsWith('s') ? category[..^1] : category;
    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
