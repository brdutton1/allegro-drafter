using AllegroDrafter.Core.Checks;
using AllegroDrafter.Core.Finishing;
using AllegroDrafter.Core.Proposals;
using AllegroDrafter.Core.Standards;

namespace AllegroDrafter.Core.Tests;

public class FinishPlannerTests
{
    private static readonly Dictionary<string, string?> DoorDone = new() { ["Door Swing"] = "LH", ["Tempered"] = "No" };
    private static readonly Dictionary<string, string?> WindowDone = new() { ["Tempered"] = "No", ["Egress"] = "No" };
    private static readonly Dictionary<string, string?> RoomDone = new() { ["Room Size"] = "", ["Comments"] = null };

    private static OpeningFact Opening(long id, string category, string label, string typeKey, Dictionary<string, string?>? p = null,
        double? sill = 3, bool exterior = false, long? to = null, bool plan = true, bool elevation = true, double? x = null) =>
        new(id, category, label, typeKey, "MAIN FLOOR", x ?? id * 100.0, 0, 3, 5, sill, exterior, null, to, plan, elevation,
            p ?? (category == "doors" ? DoorDone : WindowDone));

    private static RoomFact Room(long id, string name, string number, double x, double y, Dictionary<string, string?>? p = null, string state = "placed") =>
        new(id, number, name, "MAIN FLOOR", 100, state, false, x, y, p ?? RoomDone);

    private static ScheduleFact DoorSchedule(params string[] fields) =>
        new("DOOR SCHEDULE", "doors", false, true, fields.Select(f => new ScheduleFieldFact(f, false)).ToList(),
            new[] { "Type Mark", "From Room: Name" }, new[] { "CS" }, FirstGroupCounts: true);

    private static ScheduleFact WindowSchedule() =>
        new("WINDOW SCHEDULE", "windows", false, false, new[] { "Type Mark", "Count", "Tempered", "Egress" }.Select(f => new ScheduleFieldFact(f, false)).ToList(),
            new[] { "Type Mark" }, new[] { "CS" });

    private static readonly string[] DoorFields = { "Type Mark", "From Room: Name", "To Room: Name", "Door Swing", "Tempered" };

    /// <summary>A small house where every step is already done.</summary>
    private static SetFacts Clean() => new()
    {
        Sheets = { new SheetFact("CS", "COVER", 4), new SheetFact("A1", "MAIN FLOOR PLAN", 1) },
        Schedules = { DoorSchedule(DoorFields), WindowSchedule(), new ScheduleFact("DRAWING INDEX", "sheets", false, true, new List<ScheduleFieldFact>(), Array.Empty<string>(), new[] { "CS" }) },
        OpeningTypes = { new OpeningTypeFact("doors", "Supa 300: 3080", "Supa 300", "3080", "3080", 3, 8, 1), new OpeningTypeFact("windows", "ADC: 3050", "ADC", "3050", "3050 CSMT", 3, 5, 1) },
        Openings = { Opening(10, "doors", "Door 414", "Supa 300: 3080"), Opening(20, "windows", "Window 693", "ADC: 3050") },
        Rooms = { Room(1, "ENTRY", "101", 0, -10), Room(2, "KITCHEN", "102", 0, 10) },
        WallTypes = { new WallTypeFact("Basic Wall: Interior 2x4", "Interior", "Basic", 5, 50) },
        Texts = { new TextFact("SCOPE OF WORK: NEW HOME. DO NOT SCALE DRAWINGS.", "GENERAL NOTES", new[] { "CS" }) },
    };

    private static StandardsProfile Profile() => new()
    {
        ViewTemplates =
        {
            new ViewTemplateInfo("ADC - Arch Floor Plan", "FloorPlan"), new ViewTemplateInfo("ADC - Foundation Plan", "FloorPlan"),
            new ViewTemplateInfo("ADC - Elevations", "Elevation"), new ViewTemplateInfo("Architectural Elevation", "Elevation"),
        },
    };

    private static FinishStep Plan(SetFacts facts, FinishState? state = null, StandardsProfile? profile = null)
    {
        profile ??= Profile();
        var report = SetAuditor.Run(facts, profile.Manual.Audit, profile.ViewTemplates.Select(t => t.Name).ToList());
        return FinishPlanner.Next(facts, report, profile, state ?? new FinishState());
    }

    private static FinishBatch AssertBatch(FinishStep step, string stepId)
    {
        var b = Assert.IsType<FinishBatch>(step);
        Assert.Equal(stepId, b.StepId);
        Assert.InRange(b.Request.Changes.Count, 1, FinishPlanner.MaxChanges);
        return b;
    }

    // --------------------------------------------------------------- order

    [Fact]
    public void A_finished_house_is_done()
    {
        var step = Plan(Clean());
        if (step is FinishQuestions dq) Assert.Fail(string.Join(" | ", dq.Cards.Select(c => c.Id + ": " + c.Prompt)));
        var done = Assert.IsType<FinishDone>(step);
        Assert.Equal("done", done.StepId);
    }

    [Fact]
    public void Wall_gate_runs_first()
    {
        var facts = Clean();
        facts.WallTypes.Add(new WallTypeFact("Basic Wall: Interior 2x6 Wall", "Exterior", "Basic", 2, 19));
        facts.Rooms[0] = facts.Rooms[0] with { Number = "13' x 8'" }; // rooms also need work, but later
        var b = AssertBatch(Plan(facts), "gate");
        Assert.Equal(1, b.StepNumber);
        Assert.Equal(new SetWallFunctionChange("Basic Wall: Interior 2x6 Wall", "Interior"), b.Request.Changes.Single());
    }

    [Fact]
    public void Rejected_step_is_skipped_for_the_rest_of_the_run()
    {
        var facts = Clean();
        facts.WallTypes.Add(new WallTypeFact("Basic Wall: Interior 2x6 Wall", "Exterior", "Basic", 2, 19));
        var state = new FinishState();
        state.SkippedSteps.Add("gate");
        Assert.IsType<FinishDone>(Plan(facts, state));
    }

    // ----------------------------------------------------------- parameters

    [Fact]
    public void Missing_parameters_are_added_together()
    {
        var facts = Clean();
        facts.Openings[0] = facts.Openings[0] with { Parameters = new Dictionary<string, string?>() };
        facts.Openings[1] = facts.Openings[1] with { Parameters = new Dictionary<string, string?>() };
        facts.Rooms[0] = facts.Rooms[0] with { Parameters = new Dictionary<string, string?>() };
        var b = AssertBatch(Plan(facts), "parameters");
        var added = b.Request.Changes.Cast<CreateSharedParameterChange>().ToList();
        Assert.Contains(added, p => p.Name == "Tempered" && p.Categories.SequenceEqual(new[] { "doors", "windows" }) && p.DataType == "yesno");
        Assert.Contains(added, p => p.Name == "Egress" && p.Categories.SequenceEqual(new[] { "windows" }));
        Assert.Contains(added, p => p.Name == "Door Swing" && p.DataType == "text");
        Assert.Contains(added, p => p.Name == "Room Size" && p.Categories.SequenceEqual(new[] { "rooms" }));
    }

    // ----------------------------------------------------------- type marks

    [Fact]
    public void Type_marks_use_the_audit_suggestion_and_ask_when_there_is_none()
    {
        var facts = Clean();
        facts.OpeningTypes[0] = facts.OpeningTypes[0] with { TypeMark = "376" };
        var b = AssertBatch(Plan(facts), "type-marks");
        Assert.Equal(new SetTypeMarkChange("doors", "Supa 300: 3080", "3080"), b.Request.Changes.Single());

        // 3080 EXT collides with 3080 on size; the audit has no suggestion, so the planner asks.
        var collide = Clean();
        collide.OpeningTypes.Add(new OpeningTypeFact("doors", "Supa 300: 3080 EXT", "Supa 300", "3080 EXT", "46104", 3, 8, 1));
        var q = Assert.IsType<FinishQuestions>(Plan(collide));
        var card = Assert.Single(q.Cards);
        Assert.True(card.AllowText);

        var state = new FinishState();
        state.Answers[card.Id] = "3080 EXT";
        var answered = AssertBatch(Plan(collide, state), "type-marks");
        Assert.Equal("3080 EXT", ((SetTypeMarkChange)answered.Request.Changes.Single()).TypeMark);
    }

    // ------------------------------------------------------------ schedules

    [Fact]
    public void Missing_columns_go_in_office_order_then_the_layout()
    {
        var facts = Clean();
        facts.Schedules[0] = DoorSchedule("Type Mark", "Count", "Width", "Door Swing", "Tempered") with { IsItemized = false, FirstGroupCounts = false, SortGroupFields = new[] { "Type Mark" } };
        var b = AssertBatch(Plan(facts), "schedules");
        var fields = (ScheduleFieldsChange)b.Request.Changes.Single();
        Assert.Equal(new[] { new FieldToAdd("From Room: Name", After: "Type Mark"), new FieldToAdd("To Room: Name", After: "From Room: Name") }, fields.Add);

        // Columns in, wrong layout: one line per door with size-code headers.
        facts.Schedules[0] = DoorSchedule("Type Mark", "From Room: Name", "To Room: Name", "Count", "Width", "Door Swing", "Tempered") with { IsItemized = false, FirstGroupCounts = false, SortGroupFields = new[] { "Type Mark" } };
        var layout = AssertBatch(Plan(facts), "schedules");
        Assert.Equal(new ScheduleSortChange("DOOR SCHEDULE", new[] { "Type Mark", "From Room: Name" }, true, true), layout.Request.Changes.Single());
    }

    // ---------------------------------------------------------------- tempered

    [Fact]
    public void Tempered_and_egress_fill_by_rule_and_ask_for_hazard_windows()
    {
        var blank = new Dictionary<string, string?> { ["Tempered"] = null, ["Egress"] = null };
        var facts = Clean();
        facts.Rooms.Add(Room(3, "BEDROOM 2", "103", 10, 0));
        facts.Openings.Clear();
        facts.Openings.Add(Opening(10, "doors", "Door 414", "Supa 300: 3080", new() { ["Door Swing"] = "LH", ["Tempered"] = null }));
        facts.Openings.Add(Opening(11, "doors", "Door 597", "Frameless Glass Door: 2680", new() { ["Door Swing"] = "LH", ["Tempered"] = null }));
        facts.Openings.Add(Opening(20, "windows", "Window 693", "ADC: 3050", blank, to: 3));            // bedroom casement
        facts.Openings.Add(Opening(21, "windows", "Window 694", "ADC: 3050", blank, sill: 8));          // clerestory
        var b = AssertBatch(Plan(facts), "tempered-egress");
        var byId = b.Request.Changes.Cast<SetParametersChange>().ToDictionary(c => c.ElementId);
        Assert.Contains(new ParameterValue("Tempered", "No"), byId[10].Values);
        Assert.Contains(new ParameterValue("Tempered", "Yes"), byId[11].Values);
        Assert.Contains(new ParameterValue("Egress", "Yes"), byId[20].Values);
        Assert.Contains(new ParameterValue("Egress", "No"), byId[21].Values);
    }

    [Fact]
    public void Hazard_window_card_answer_becomes_values()
    {
        var facts = Clean();
        facts.Openings.Clear();
        facts.Openings.Add(Opening(10, "doors", "Door 556", "Pocket: 4080", x: 0));
        facts.Openings.Add(Opening(20, "windows", "Window 670", "ADC: 3050", new() { ["Tempered"] = "No", ["Egress"] = "No", ["Comments"] = null }, sill: 2, x: 4)); // next to the door
        var q = Assert.IsType<FinishQuestions>(Plan(facts));
        var card = Assert.Single(q.Cards);
        Assert.Equal("tempered:20", card.Id);
        Assert.Equal("Yes", card.Recommended);

        var state = new FinishState();
        state.Answers[card.Id] = "No: closet 3' deep or less";
        var b = AssertBatch(Plan(facts, state), "tempered-egress");
        var set = (SetParametersChange)b.Request.Changes.Single();
        Assert.Contains(new ParameterValue("Comments", "closet 3' deep or less"), set.Values);
    }

    // ------------------------------------------------------------------ rooms

    [Fact]
    public void Rooms_get_numbers_and_sizes_move_out()
    {
        var facts = Clean();
        facts.Rooms.Clear();
        facts.Rooms.Add(Room(1, "ENTRY", "13'-0\" x 8'-3\"", 0, -10));
        facts.Rooms.Add(Room(2, "KITCHEN", "16'-3\" x 16'-3\"", 10, 0));
        facts.Rooms.Add(Room(3, "CLST", "12' FLAT CEILING", -10, 0));
        var b = AssertBatch(Plan(facts), "rooms");
        var byId = b.Request.Changes.Cast<SetParametersChange>().ToDictionary(c => c.ElementId);
        Assert.Contains(new ParameterValue("Number", "101"), byId[1].Values);
        Assert.Contains(new ParameterValue("Room Size", "13'-0\" x 8'-3\""), byId[1].Values);
        Assert.Contains(new ParameterValue("Number", "102"), byId[3].Values); // west comes next going clockwise from south
        Assert.Contains(new ParameterValue("Comments", "12' FLAT CEILING"), byId[3].Values);
        Assert.Contains(new ParameterValue("Number", "103"), byId[2].Values);
    }

    [Fact]
    public void No_entry_asks_which_room()
    {
        var facts = Clean();
        facts.Rooms.Clear();
        facts.Rooms.Add(Room(1, "KITCHEN", "", 0, -10));
        facts.Rooms.Add(Room(2, "MUDROOM", "", 0, 10));
        var q = Assert.IsType<FinishQuestions>(Plan(facts));
        var card = Assert.Single(q.Cards);
        Assert.Contains("MUDROOM", card.Options);

        var state = new FinishState();
        state.Answers[card.Id] = "MUDROOM";
        var b = AssertBatch(Plan(facts, state), "rooms");
        Assert.Contains(b.Request.Changes.Cast<SetParametersChange>(), c => c.ElementId == 2 && c.Values.Contains(new ParameterValue("Number", "101")));
    }

    // ------------------------------------------------------------- door swing

    [Fact]
    public void Door_swing_from_geometry_and_cards_for_the_rest()
    {
        var facts = Clean();
        facts.Openings.Clear();
        var blank = new Dictionary<string, string?> { ["Door Swing"] = null, ["Tempered"] = "No" };
        facts.Openings.Add(Opening(10, "doors", "Door 414", "Supa 300: 3080", blank));
        facts.Openings.Add(Opening(11, "doors", "Door 360", "Pocket: 2680", blank));
        facts.Openings.Add(Opening(12, "doors", "Door 900", "Bifold: 6080", blank));
        facts.DoorGeometry.Add(new DoorGeometry(10, "Door 414", "Supa 300", "3080", 0, 1, -1, 0));
        facts.DoorGeometry.Add(new DoorGeometry(11, "Door 360", "Pocket", "2680 PKT", 0, 1, -1, 0));
        facts.DoorGeometry.Add(new DoorGeometry(12, "Door 900", "Bifold", "6080 BIFOLD", 0, 1, -1, 0));
        var b = AssertBatch(Plan(facts), "door-swing");
        var byId = b.Request.Changes.Cast<SetParametersChange>().ToDictionary(c => c.ElementId, c => c.Values.Single().Value);
        Assert.Equal("LH", byId[10]);
        Assert.Equal("POCKET", byId[11]);
        Assert.False(byId.ContainsKey(12));
        Assert.Contains("Spot-check", b.Request.Rationale);

        // Approved: the two are set. Only the bifold is left, as a card.
        facts.Openings[0] = facts.Openings[0] with { Parameters = DoorDone };
        facts.Openings[1] = facts.Openings[1] with { Parameters = DoorDone };
        var q = Assert.IsType<FinishQuestions>(Plan(facts));
        Assert.Equal("swing:12", q.Cards.Single().Id);
    }

    // ---------------------------------------------------------- room cleanup

    [Fact]
    public void Only_unplaced_rooms_are_deleted()
    {
        var facts = Clean();
        facts.Rooms.Add(Room(7, "CARPET", "32", 0, 0, state: "unplaced") with { X = null, Y = null });
        facts.Rooms.Add(Room(8, "CARPET", "33", 0, 0, state: "unplaced") with { X = null, Y = null });
        facts.Rooms.Add(Room(9, "BREEZEWAY", "126", 0, 0, state: "not enclosed"));
        var b = AssertBatch(Plan(facts), "room-cleanup");
        var del = (DeleteRoomsChange)b.Request.Changes.Single();
        Assert.Equal(new long[] { 7, 8 }, del.RoomIds);
        Assert.Equal("CARPET x2", del.Summary);
        Assert.True(del.IsDestructive);
    }

    // ---------------------------------------------------------- view templates

    [Fact]
    public void Views_without_templates_are_left_alone()
    {
        // Office rule (2026-11-26): view templates are the designer's. The run never asks about or applies them.
        var facts = Clean();
        facts.PlacedViews.Add(new ViewFact("MAIN FLOOR PLAN", "FloorPlan", 48, null, new[] { "A1" }));
        facts.PlacedViews.Add(new ViewFact("FRONT ELEVATION", "Elevation", 48, null, new[] { "A2" }));
        var profile = Profile();
        profile.Manual.Audit.ViewTemplateRules.Add(new ViewTemplateRule("Elevation", null, "ADC - Elevations")); // old setting: ignored
        var done = Assert.IsType<FinishDone>(Plan(facts, profile: profile));
        Assert.DoesNotContain(done.Remaining, r => r.Contains("view template", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(FinishPlanner.Steps, s => s.Id.Contains("template"));
    }
    // ------------------------------------------------------------------ tags

    [Fact]
    public void Tags_go_on_floor_plans_and_exterior_elevations_only()
    {
        var facts = Clean();
        facts.PlacedViews.Add(new ViewFact("MAIN FLOOR PLAN", "FloorPlan", 48, "ADC - Arch Floor Plan", new[] { "A1" }));
        facts.PlacedViews.Add(new ViewFact("MAIN FLOOR PLAN DECO BEAM LAYOUT", "FloorPlan", 48, "ADC - Arch Floor Plan", new[] { "A1.1" }));
        facts.PlacedViews.Add(new ViewFact("FRONT ELEVATION", "Elevation", 48, "ADC - Elevations", new[] { "A2" }));
        facts.PlacedViews.Add(new ViewFact("KITCHEN SINK", "Elevation", 24, "ADC - Elevations", new[] { "A13" }));
        facts.Openings[0] = facts.Openings[0] with { TaggedInPlacedPlan = false, IsExterior = true, TaggedInPlacedElevation = false };
        var b = AssertBatch(Plan(facts), "tags");
        var views = b.Request.Changes.Cast<TagViewChange>().Select(t => t.ViewName).Distinct().ToList();
        Assert.Equal(new[] { "FRONT ELEVATION", "MAIN FLOOR PLAN" }, views);
    }

    // ------------------------------------------------------------ cover sheet

    [Fact]
    public void Cover_sheet_places_the_index_and_asks_about_notes()
    {
        var facts = Clean();
        facts.Schedules[2] = facts.Schedules[2] with { OnSheets = Array.Empty<string>() };
        var b = AssertBatch(Plan(facts), "cover-sheet");
        Assert.Equal(new PlaceViewChange("CS", "DRAWING INDEX", "Schedule"), b.Request.Changes.Single());

        facts.Schedules[2] = facts.Schedules[2] with { OnSheets = new[] { "CS" } };
        facts.Texts.Clear();
        var q = Assert.IsType<FinishQuestions>(Plan(facts));
        Assert.Contains(q.Cards, c => c.Id == "cover-note:DO NOT SCALE" && c.Options.Contains(FinishState.DoneByHand));

        var state = new FinishState();
        foreach (var c in q.Cards) state.Answers[c.Id] = FinishState.DoneByHand;
        var done = Assert.IsType<FinishDone>(Plan(facts, state));
        Assert.Contains(done.Remaining, r => r.StartsWith("Cover sheet"));
    }

    // ------------------------------------------------------------------- caps

    [Fact]
    public void Big_steps_split_into_batches_of_fifty()
    {
        var facts = Clean();
        facts.Openings.Clear();
        for (var i = 0; i < 120; i++)
            facts.Openings.Add(Opening(1000 + i, "windows", $"Window {i}", "ADC: 3050", new() { ["Tempered"] = null, ["Egress"] = "No" }, sill: 8));
        var b = AssertBatch(Plan(facts), "tempered-egress");
        Assert.Equal(50, b.Request.Changes.Count);
    }
}
