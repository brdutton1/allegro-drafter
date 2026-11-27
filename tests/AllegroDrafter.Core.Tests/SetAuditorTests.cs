using System.Text.Json;
using AllegroDrafter.Core.Checks;
using AllegroDrafter.Core.Mcp;
using AllegroDrafter.Core.Proposals;
using AllegroDrafter.Core.Standards;

namespace AllegroDrafter.Core.Tests;

public class SizeCodeTests
{
    [Theory]
    [InlineData("3050 CSMT", 3.0, 5.0, "CSMT")]
    [InlineData("2868", 2 + 8 / 12.0, 6 + 8 / 12.0, null)]
    [InlineData("1660 FIX", 1.5, 6.0, "FIX")]
    [InlineData("4040 sldr", 4.0, 4.0, "SLDR")]
    [InlineData("6060 CMST", 6.0, 6.0, "CMST")]
    public void Parses_four_digit_codes(string mark, double w, double h, string? op)
    {
        var code = SizeCode.Parse(mark)!;
        Assert.NotNull(code);
        Assert.Single(code.Readings());
        Assert.Equal(w, code.Primary.WidthFeet, 6);
        Assert.Equal(h, code.Primary.HeightFeet, 6);
        Assert.Equal(op, code.Operation);
    }

    [Theory]
    [InlineData("466")]
    [InlineData("12' x 10'")]
    [InlineData("126\" x 88\"")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("CSMT 3050")]
    public void Rejects_things_that_are_not_size_codes(string? mark) => Assert.Null(SizeCode.Parse(mark));

    [Fact]
    public void Long_codes_resolve_against_the_geometry()
    {
        var wide = SizeCode.Parse("12080 SLDR")!;
        Assert.Contains((12.0, 8.0), wide.Readings());
        Assert.Equal(0, wide.Closest(12, 8).OffInches, 6);

        var narrow = SizeCode.Parse("21068")!; // 2'-10" x 6'-8" (could also read 21'-0")
        var (w, h, off) = narrow.Closest(2 + 10 / 12.0, 6 + 8 / 12.0);
        Assert.Equal(2 + 10 / 12.0, w, 6);
        Assert.Equal(6 + 8 / 12.0, h, 6);
        Assert.Equal(0, off, 6);

        Assert.Contains((12.0, 10.0), SizeCode.Parse("120100 SLDR")!.Readings());
    }

    [Theory]
    [InlineData(3.0, "3'-0\"")]
    [InlineData(2 + 8 / 12.0, "2'-8\"")]
    [InlineData(10 + 2.75 / 12, "10'-2 3/4\"")]
    public void Formats_feet_inches(double feet, string expected) => Assert.Equal(expected, SizeCode.FeetInches(feet));

    [Theory]
    [InlineData(48, "1/4\" = 1'-0\"")]
    [InlineData(96, "1/8\" = 1'-0\"")]
    [InlineData(64, "3/16\" = 1'-0\"")]
    [InlineData(8, "1 1/2\" = 1'-0\"")]
    [InlineData(480, "1\" = 40'-0\"")]
    [InlineData(100, "1:100")]
    public void Labels_scales(int denominator, string expected) => Assert.Equal(expected, SetAuditor.ScaleLabel(denominator));

    [Theory]
    [InlineData(0.09375, "3/32\"")]
    [InlineData(0.125, "1/8\"")]
    [InlineData(44, "44\"")]
    public void Labels_inches(double inches, string expected) => Assert.Equal(expected, SetAuditor.InchesLabel(inches));
}

public class SetAuditorTests
{
    private static readonly Dictionary<string, string?> DoorOk = new() { ["Door Swing"] = "LH", ["Tempered"] = "No" };
    private static readonly Dictionary<string, string?> WindowOk = new() { ["Tempered"] = "No", ["Egress"] = "No" };

    private static OpeningTypeFact Type(string category, string key, string? mark, double w, double h, int count = 1) =>
        new(category, key, key.Split(": ")[0], key.Split(": ")[1], mark, w, h, count);

    private static OpeningFact Opening(long id, string category, string label, string typeKey,
        double? x = null, double? y = null, double w = 3, double h = 5, double? sill = 3,
        bool exterior = false, long? from = null, long? to = null, bool plan = true, bool elevation = true,
        Dictionary<string, string?>? parameters = null) =>
        new(id, category, label, typeKey, "L1", x, y, w, h, sill, exterior, from, to, plan, elevation,
            parameters ?? (category == "doors" ? DoorOk : WindowOk));

    private static ScheduleFact Schedule(string name, string category, IEnumerable<string> fields, string? sortBy = "Type Mark", bool placed = true, params string[] hidden) =>
        new(name, category, false, false,
            fields.Select(f => new ScheduleFieldFact(f, hidden.Contains(f))).Concat(hidden.Where(h => !fields.Contains(h)).Select(h => new ScheduleFieldFact(h, true))).ToList(),
            sortBy is null ? Array.Empty<string>() : new[] { sortBy },
            placed ? new[] { "CS" } : Array.Empty<string>());

    private static CheckResult Check(SetFacts facts, string id, AuditSettings? settings = null, IReadOnlyCollection<string>? templates = null) =>
        Assert.Single(SetAuditor.Run(facts, settings, templates, new[] { id }).Checks);

    // ---------------------------------------------------------------- type-marks

    [Fact]
    public void Type_mark_matching_the_size_passes()
    {
        var facts = new SetFacts { OpeningTypes = { Type("windows", "ADC-Casement: 3050", "3050 CSMT", 3, 5) } };
        Assert.Equal(CheckStatus.Pass, Check(facts, "type-marks").Status);
    }

    [Fact]
    public void Revit_auto_number_as_type_mark_fails_with_the_right_code_suggested()
    {
        var facts = new SetFacts { OpeningTypes = { Type("doors", "Supa 300: 3080", "376", 3, 8, count: 8) } };
        var r = Check(facts, "type-marks");
        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.Contains("\"376\", which is not a size code", r.Findings[0].Message);
        Assert.Contains("\"3080\"", r.Findings[0].Message);
        Assert.Contains("(8 placed)", r.Findings[0].Message);
    }

    [Fact]
    public void Type_mark_that_disagrees_with_geometry_fails()
    {
        var facts = new SetFacts { OpeningTypes = { Type("windows", "ADC-Casement: 3060", "3050 CSMT", 3, 6) } };
        var r = Check(facts, "type-marks");
        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.Contains("says 3'-0\" x 5'-0\", but the type doesn't match", r.Findings[0].Message);
        Assert.Contains("The type is 3'-0\" x 6'-0\"", r.Findings[0].Message);
        Assert.Equal("3060 CSMT", r.Findings[0].Suggestion);
    }

    [Fact]
    public void Type_mark_within_tolerance_passes()
    {
        // Milgard picture window 3020 at 35 5/8" wide: 3/8" off is inside the 1/2" default.
        var facts = new SetFacts { OpeningTypes = { Type("windows", "Milgard Picture: 3020", "3020 FIX", 35.625 / 12, 2) } };
        Assert.Equal(CheckStatus.Pass, Check(facts, "type-marks").Status);
    }

    [Fact]
    public void Missing_type_mark_fails()
    {
        var facts = new SetFacts { OpeningTypes = { Type("windows", "ADC-Casement: 3050", null, 3, 5) } };
        Assert.Contains("has no Type Mark", Check(facts, "type-marks").Findings[0].Message);
    }

    // ----------------------------------------------------------- operation-codes

    [Fact]
    public void Typo_in_operation_code_is_caught_with_a_suggestion()
    {
        var facts = new SetFacts { OpeningTypes = { Type("windows", "ADC-Casement-Double: 6060", "6060 CMST", 6, 6) } };
        var r = Check(facts, "operation-codes");
        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.Contains("\"CMST\"", r.Findings[0].Message);
        Assert.Contains("Did you mean CSMT?", r.Findings[0].Message);
    }

    [Fact]
    public void Window_without_operation_fails_but_door_without_is_fine()
    {
        var facts = new SetFacts
        {
            OpeningTypes =
            {
                Type("windows", "Picture: 3020", "3020", 3, 2),
                Type("doors", "Supa 300: 3080", "3080", 3, 8),
            },
        };
        var r = Check(facts, "operation-codes");
        Assert.Single(r.Findings);
        Assert.Contains("Window type", r.Findings[0].Message);
    }

    [Fact]
    public void Transom_is_not_an_operation()
    {
        var facts = new SetFacts { OpeningTypes = { Type("windows", "Picture: 7920", "7920 TRANSOM", 7.75, 2) } };
        Assert.Equal(CheckStatus.Fail, Check(facts, "operation-codes").Status);
    }

    [Fact]
    public void Two_types_sharing_a_type_mark_fail()
    {
        var facts = new SetFacts
        {
            OpeningTypes =
            {
                Type("doors", "Supa 300: 3080", "3080", 3, 8),
                Type("doors", "Six Panel: 3080", "3080", 3, 8),
            },
        };
        var r = Check(facts, "operation-codes");
        Assert.Contains("on 2 different doors types", r.Findings[0].Message);
    }

    [Fact]
    public void Custom_operation_list_from_settings_is_used()
    {
        var settings = new AuditSettings { WindowOperations = { "TRANSOM" } };
        var facts = new SetFacts { OpeningTypes = { Type("windows", "Picture: 7920", "7920 TRANSOM", 7.75, 2) } };
        Assert.Equal(CheckStatus.Pass, Check(facts, "operation-codes", settings).Status);
    }

    // ---------------------------------------------------------------- schedules

    [Fact]
    public void Schedules_without_type_mark_fail()
    {
        var facts = new SetFacts
        {
            Schedules =
            {
                Schedule("DOOR SCHEDULE", "doors", new[] { "Width", "Height", "Type", "Door Swing", "Type Comments" }, sortBy: "Type"),
                Schedule("WINDOW SCHEDULE", "windows", new[] { "Width", "Height", "Type", "Type Comments" }, sortBy: null),
            },
        };
        var r = Check(facts, "schedules");
        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.Contains(r.Findings, x => x.Message.Contains("\"DOOR SCHEDULE\" is missing Type Mark, Count, From Room: Name, To Room: Name, Tempered"));
        Assert.Contains(r.Findings, x => x.Message.Contains("\"WINDOW SCHEDULE\" is missing Type Mark, Count, Tempered, Egress"));
        Assert.Contains(r.Findings, x => x.Message.Contains("sorts by \"Type\" first"));
        Assert.Contains(r.Findings, x => x.Message.Contains("sorts by nothing first"));
    }

    [Fact]
    public void Office_standard_schedules_pass()
    {
        var facts = new SetFacts
        {
            Schedules =
            {
                Schedule("DOOR SCHEDULE", "doors", new[] { "Type Mark", "Count", "From Room: Name", "To Room: Name", "Width", "Height", "Door Swing", "Tempered" }),
                Schedule("WINDOW SCHEDULE", "windows", new[] { "Type Mark", "Count", "Width", "Height", "Tempered", "Egress" }),
            },
        };
        Assert.Equal(CheckStatus.Pass, Check(facts, "schedules").Status);
    }

    [Fact]
    public void Hidden_required_field_fails_and_unplaced_schedule_fails()
    {
        var facts = new SetFacts
        {
            Schedules =
            {
                Schedule("DOOR SCHEDULE", "doors", new[] { "Type Mark", "Count", "Door Swing" }, hidden: "Tempered"),
                Schedule("WINDOW SCHEDULE", "windows", new[] { "Type Mark", "Count", "Tempered", "Egress" }, placed: false),
            },
        };
        var r = Check(facts, "schedules");
        Assert.Contains(r.Findings, x => x.Message.Contains("hides Tempered"));
        Assert.Contains(r.Findings, x => x.Message.Contains("isn't on any sheet"));
    }

    [Fact]
    public void No_schedule_at_all_fails()
    {
        var r = Check(new SetFacts(), "schedules");
        Assert.Equal(2, r.FindingCount);
        Assert.Contains(r.Findings, x => x.Message == "No door schedule in the model.");
    }

    // ------------------------------------------------------- opening-parameters

    [Fact]
    public void Missing_tempered_and_egress_parameters_fail_once_per_category()
    {
        var facts = new SetFacts
        {
            Openings =
            {
                Opening(1, "windows", "Window 630", "ADC: 6060", parameters: new()),
                Opening(2, "windows", "Window 631", "ADC: 6060", parameters: new()),
                Opening(3, "doors", "Door 360", "Pocket: 2680", parameters: new() { ["Door Swing"] = "" }),
            },
        };
        var r = Check(facts, "opening-parameters");
        Assert.Contains(r.Findings, x => x.Message.StartsWith("2 of 2 windows have no \"Tempered\""));
        Assert.Contains(r.Findings, x => x.Message.StartsWith("2 of 2 windows have no \"Egress\""));
        Assert.Contains(r.Findings, x => x.Message.StartsWith("1 of 1 doors have no \"Tempered\""));
        Assert.Contains(r.Findings, x => x.Message == "Door 360: \"Door Swing\" is not set." && x.Severity == Severity.Fail);
    }

    [Fact]
    public void Complete_parameters_pass()
    {
        var facts = new SetFacts { Openings = { Opening(1, "windows", "W", "T: 3050"), Opening(2, "doors", "D", "T: 3080") } };
        Assert.Equal(CheckStatus.Pass, Check(facts, "opening-parameters").Status);
    }

    // --------------------------------------------------------------------- tags

    [Fact]
    public void Untagged_fails_and_exterior_without_elevation_tag_warns()
    {
        var facts = new SetFacts
        {
            Openings =
            {
                Opening(1, "doors", "Door 1", "T: 3080", plan: false),
                Opening(2, "windows", "Window 2", "T: 3050", exterior: true, elevation: false),
                Opening(3, "windows", "Window 3", "T: 3050", exterior: false, elevation: false),
            },
        };
        var r = Check(facts, "tags");
        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.Equal(2, r.FindingCount);
        Assert.Equal(1, r.Findings[0].ElementId);
        Assert.Equal(Severity.Fail, r.Findings[1].Severity); // office tags on elevations

        var relaxed = Check(facts, "tags", new AuditSettings { RequireElevationTags = false });
        Assert.Equal(Severity.Warn, relaxed.Findings.Single(x => x.ElementId == 2).Severity);
    }

    [Fact]
    public void Clerestory_above_the_plan_cut_needs_an_elevation_tag_not_a_plan_tag()
    {
        var facts = new SetFacts
        {
            Openings =
            {
                // 8'-0" sill, not on any plan, tagged on an elevation: fine.
                Opening(1, "windows", "Window 677", "Picture: 3020", sill: 8, exterior: true, plan: false, elevation: true),
                // 10'-2 3/4" transom tagged nowhere: fails, and says why.
                Opening(2, "windows", "Window 662", "Picture: 7920", sill: 10 + 2.75 / 12, exterior: true, plan: false, elevation: false),
            },
        };
        var r = Check(facts, "tags");
        var only = Assert.Single(r.Findings);
        Assert.Equal(2, only.ElementId);
        Assert.Contains("above the plan cut (sill 10'-2 3/4\")", only.Message);
    }

    [Fact]
    public void Parameter_blank_on_every_opening_is_one_finding_after_the_root_causes()
    {
        var blankSwing = new Dictionary<string, string?> { ["Door Swing"] = null };
        var facts = new SetFacts();
        for (var i = 0; i < 27; i++) facts.Openings.Add(Opening(i, "doors", $"Door {i}", "Supa 300: 3080", parameters: blankSwing));
        var r = Check(facts, "opening-parameters");
        Assert.Equal(2, r.FindingCount);
        Assert.StartsWith("27 of 27 doors have no \"Tempered\"", r.Findings[0].Message);
        Assert.Equal("\"Door Swing\" is blank on all 27 doors.", r.Findings[1].Message);
    }

    [Fact]
    public void Suggestions_read_cleanly()
    {
        var facts = new SetFacts
        {
            OpeningTypes =
            {
                Type("doors", "Supa 300: 3080", "376", 3, 8),
                Type("windows", "ADC-Casement: 3050", "668", 3, 5),
            },
        };
        var r = Check(facts, "type-marks");
        Assert.Contains(r.Findings, x => x.Message.EndsWith("The type is 3'-0\" x 8'-0\", so it should be \"3080\"."));
        Assert.Contains(r.Findings, x => x.Message.EndsWith("The type is 3'-0\" x 5'-0\", so it should be \"3050 CSMT\"."));
        Assert.All(r.Findings, x => Assert.NotNull(x.Target));
    }

    [Fact]
    public void Suggested_marks_for_the_sample_job()
    {
        var types = new[]
        {
            Type("doors", "Supa 300: 3080", "376", 3, 8),
            Type("doors", "Door-Interior-Single-Pocket-2_Panel-Wood: 2680", "466", 2.5, 8),
            Type("doors", "Door-Sliding-Milgard-Tuscany_Series-4_Panel: 12' x 10'", "445", 12, 10),
            Type("doors", "Supa 300: 2680", "372", 2.5, 8),
            Type("doors", "Frameless Glass Door: 2680", "530", 2.5, 8),
            Type("windows", "ADC-Window-Casement-Double: 6060", "6060 CMST", 6, 6),
            Type("windows", "Window-Fixed-Milgard-Tuscany_Series-Picture_Window: 7920", "7920 TRANSOM", 7.75, 2),
            Type("windows", "Mystery Window: 3050", "W1", 3, 5),
            Type("windows", "ADC-Window-Casement-Full: 3050", "3050 CSMT", 3, 5),
        };
        var s = SetAuditor.SuggestMarks(types, new AuditSettings());

        Assert.Equal("3080", s[("doors", "Supa 300: 3080")].Mark);
        Assert.Equal("2680 PKT", s[("doors", "Door-Interior-Single-Pocket-2_Panel-Wood: 2680")].Mark);
        Assert.Equal("120100 SLDR", s[("doors", "Door-Sliding-Milgard-Tuscany_Series-4_Panel: 12' x 10'")].Mark);
        Assert.Equal("6060 CSMT", s[("windows", "ADC-Window-Casement-Double: 6060")].Mark);
        Assert.Equal("7920 FIX", s[("windows", "Window-Fixed-Milgard-Tuscany_Series-Picture_Window: 7920")].Mark);

        // Two 2'-6" x 8'-0" swing doors would both become "2680": withheld, with the reason.
        Assert.Null(s[("doors", "Supa 300: 2680")].Mark);
        Assert.Null(s[("doors", "Frameless Glass Door: 2680")].Mark);
        Assert.Contains("would match \"Frameless Glass Door: 2680\"", s[("doors", "Supa 300: 2680")].Reason);

        // No operation in the name: asks instead of guessing.
        Assert.Null(s[("windows", "Mystery Window: 3050")].Mark);
        Assert.Contains("Couldn't tell the operation", s[("windows", "Mystery Window: 3050")].Reason);

        // Already right: nothing to suggest.
        Assert.False(s.ContainsKey(("windows", "ADC-Window-Casement-Full: 3050")));
    }

    [Fact]
    public void Operation_code_findings_carry_the_fix()
    {
        var facts = new SetFacts { OpeningTypes = { Type("windows", "ADC-Window-Casement-Double: 6060", "6060 CMST", 6, 6) } };
        var f = Check(facts, "operation-codes").Findings.Single();
        Assert.Equal("ADC-Window-Casement-Double: 6060", f.Target);
        Assert.Equal("6060 CSMT", f.Suggestion);
    }

    [Fact]
    public void Egress_says_when_the_parameter_does_not_exist()
    {
        var facts = new SetFacts
        {
            Rooms = { new RoomFact(1, "101", "BEDROOM 2", "L1", 188, "placed", false) },
            Openings = { Opening(11, "windows", "Window 693", "ADC: 3050", to: 1, parameters: new() { ["Tempered"] = "No" }) },
        };
        Assert.Contains("(the Egress parameter doesn't exist yet)", Check(facts, "egress").Findings[0].Message);
    }

    // -------------------------------------------------------------- wall-legend

    [Fact]
    public void Wall_legend_mismatch_is_reported_both_ways()
    {
        var facts = new SetFacts
        {
            Legends = { new LegendFact("WALL LEGEND", new[] { "A1" }, new[] { "Basic Wall: Foundation - 10\" Concrete", "Basic Wall: Interior 2x4 Wall" }) },
            ModeledWallTypes = { "Basic Wall: 8\" Foundation Wall", "Basic Wall: Interior 2x4 Wall", "Basic Wall: Truss" },
        };
        var settings = new AuditSettings { LegendExemptWallTypes = { "Truss" } };
        var r = Check(facts, "wall-legend", settings);
        Assert.Equal(2, r.FindingCount);
        Assert.Contains(r.Findings, x => x.Message.Contains("shows \"Basic Wall: Foundation - 10\" Concrete\", but no wall in the model uses it"));
        Assert.Contains(r.Findings, x => x.Message.Contains("\"Basic Wall: 8\" Foundation Wall\" is in the model but not in"));
    }

    [Fact]
    public void Missing_wall_legend_warns()
    {
        Assert.Equal(CheckStatus.Warn, Check(new SetFacts(), "wall-legend").Status);
    }

    // ------------------------------------------------------------------- sheets

    [Fact]
    public void Sheet_problems_are_found()
    {
        var facts = new SetFacts
        {
            Sheets =
            {
                new SheetFact("CS", "Cover Sheet", 3),
                new SheetFact("A1.1", "Main Floor Deco Beam Plan -", 2),
                new SheetFact("A11", "Inspiration", 0),
                new SheetFact("S8 (1)", "Unnamed Copy 1", 1),
                new SheetFact("X99", "Placeholder", 0, IsPlaceholder: true),
            },
        };
        var r = Check(facts, "sheets");
        Assert.Contains(r.Findings, x => x.Message.Contains("\"S8 (1)\"") && x.Severity == Severity.Fail);
        Assert.Contains(r.Findings, x => x.Message.Contains("\"Main Floor Deco Beam Plan -\" looks unfinished"));
        Assert.Contains(r.Findings, x => x.Message.Contains("A11 (Inspiration) has nothing on it"));
        Assert.Contains(r.Findings, x => x.Message.Contains("No sheet index"));
        Assert.DoesNotContain(r.Findings, x => x.Message.Contains("X99"));
    }

    [Fact]
    public void Clean_sheets_pass()
    {
        var facts = new SetFacts
        {
            Sheets = { new SheetFact("CS", "Cover Sheet & General Notes", 4), new SheetFact("A1", "Main Floor Plan", 2) },
            Schedules = { Schedule("DRAWING INDEX", "sheets", new[] { "Sheet Number", "Sheet Name" }) },
        };
        Assert.Equal(CheckStatus.Pass, Check(facts, "sheets").Status);
    }

    // -------------------------------------------------------------- view-scales

    [Fact]
    public void Scales_fail_below_eighth_warn_below_quarter_and_skip_site()
    {
        var facts = new SetFacts
        {
            PlacedViews =
            {
                new ViewFact("MAIN FLOOR PLAN", "FloorPlan", 48, null, new[] { "A1" }),
                new ViewFact("HOUSE ROOF PLAN", "EngineeringPlan", 64, null, new[] { "A6" }),
                new ViewFact("Section 13", "Section", 128, null, new[] { "A4" }),
                new ViewFact("SITE PLAN", "FloorPlan", 480, null, new[] { "C1" }),
                new ViewFact("DETAIL", "DraftingView", 500, null, new[] { "F2" }),
            },
        };
        var r = Check(facts, "view-scales");
        Assert.Equal(2, r.FindingCount);
        Assert.Contains(r.Findings, x => x.Severity == Severity.Fail && x.Message.Contains("Section 13") && x.Message.Contains("at 3/32\" = 1'-0\""));
        Assert.Contains(r.Findings, x => x.Severity == Severity.Warn && x.Message.Contains("HOUSE ROOF PLAN") && x.Message.Contains("3/16\""));
    }

    [Fact]
    public void Door_schedule_one_line_per_door_with_group_counts_needs_no_count_column()
    {
        var fields = new[] { "Type Mark", "From Room: Name", "To Room: Name", "Width", "Height", "Door Swing", "Tempered" };
        ScheduleFact Doors(bool itemized, bool counts) =>
            new("DOOR SCHEDULE", "doors", false, itemized, fields.Select(f => new ScheduleFieldFact(f, false)).ToList(),
                new[] { "Type Mark", "From Room: Name" }, new[] { "CS" }, counts);
        var window = Schedule("WINDOW SCHEDULE", "windows", new[] { "Type Mark", "Count", "Tempered", "Egress" });

        var ok = Check(new SetFacts { Schedules = { Doors(itemized: true, counts: true), window } }, "schedules");
        Assert.Equal(CheckStatus.Pass, ok.Status);

        var noFooter = Check(new SetFacts { Schedules = { Doors(itemized: true, counts: false), window } }, "schedules");
        Assert.Contains(noFooter.Findings, f => f.Message.Contains("missing Count"));
    }

    [Fact]
    public void Door_schedule_needs_from_and_to_room()
    {
        var facts = new SetFacts
        {
            Schedules =
            {
                Schedule("DOOR SCHEDULE", "doors", new[] { "Type Mark", "Count", "Door Swing", "Tempered" }),
                Schedule("WINDOW SCHEDULE", "windows", new[] { "Type Mark", "Count", "Tempered", "Egress" }),
            },
        };
        var r = Check(facts, "schedules");
        Assert.Contains(r.Findings, f => f.Message.Contains("missing From Room: Name, To Room: Name"));
    }

    // ------------------------------------------------------------ wall-function

    [Fact]
    public void Interior_wall_types_set_to_exterior_fail_with_a_fix()
    {
        var facts = new SetFacts
        {
            WallTypes =
            {
                new WallTypeFact("Basic Wall: Interior 2x6 Wall (INSULATION)", "Exterior", "Basic", 42, 310.4),
                new WallTypeFact("Basic Wall: Exterior 2x6 Wall w/ Stone", "Exterior", "Basic", 64, 106.7),
                new WallTypeFact("Basic Wall: Ext 2x6 Board and Batten", "Interior", "Basic", 10, 80),
                new WallTypeFact("Basic Wall: 8\" Foundation Wall", "Foundation", "Basic", 12, 200),
                new WallTypeFact("Basic Wall: Glass", "Exterior", "Basic", 2, 10),
                // "Internal" and "Paint" don't count as the keyword "Int".
                new WallTypeFact("Basic Wall: Internal Paint Test", "Exterior", "Basic", 1, 5),
                new WallTypeFact("Stacked Wall: Interior Stack", "Exterior", "Stacked", 1, 5),
            },
        };
        var r = Check(facts, "wall-function");
        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.Equal(2, r.FindingCount);
        var interior = Assert.Single(r.Findings, f => f.Target == "Basic Wall: Interior 2x6 Wall (INSULATION)");
        Assert.Equal("Interior", interior.Suggestion);
        Assert.Contains("42 walls, 310 ft", interior.Message);
        var exterior = Assert.Single(r.Findings, f => f.Target == "Basic Wall: Ext 2x6 Board and Batten");
        Assert.Equal("Exterior", exterior.Suggestion);
    }

    [Fact]
    public void Exterior_openings_with_rooms_on_both_sides_warn()
    {
        var facts = new SetFacts
        {
            WallTypes = { new WallTypeFact("Basic Wall: Party Wall", "Exterior", "Basic", 3, 40) },
            Openings =
            {
                Opening(1, "doors", "Door 360", "Pocket: 2680", exterior: true, from: 10, to: 11) with { HostWallType = "Basic Wall: Party Wall" },
                Opening(2, "doors", "Door 361", "Pocket: 2680", exterior: true, from: 12, to: 13) with { HostWallType = "Basic Wall: Party Wall" },
                // A real exterior door: room on one side only.
                Opening(3, "doors", "Door 414", "Supa 300: 3080", exterior: true, to: 10) with { HostWallType = "Basic Wall: Party Wall" },
            },
        };
        var r = Check(facts, "wall-function");
        var w = Assert.Single(r.Findings);
        Assert.Equal(Severity.Warn, w.Severity);
        Assert.Null(w.Suggestion);
        Assert.Contains("2 openings in \"Basic Wall: Party Wall\"", w.Message);
        Assert.DoesNotContain("Door 414", w.Message);
    }

    [Fact]
    public void Wall_function_skips_without_wall_data_and_passes_when_clean()
    {
        Assert.Equal(CheckStatus.Skip, Check(new SetFacts(), "wall-function").Status);
        var clean = new SetFacts { WallTypes = { new WallTypeFact("Basic Wall: Interior 2x4", "Interior", "Basic", 5, 50) } };
        Assert.Equal(CheckStatus.Pass, Check(clean, "wall-function").Status);
    }

    [Fact]
    public void Audit_set_schema_lists_every_check()
    {
        var schema = System.Text.Json.Nodes.JsonNode.Parse(AllegroDrafter.Core.Mcp.ToolCatalog.Find(AllegroDrafter.Core.Mcp.ToolNames.AuditSet)!.InputSchemaJson)!;
        var ids = schema["properties"]!["checks"]!["items"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        Assert.Equal(SetAuditor.CheckIds, ids);
    }

    // ------------------------------------------------------------- text-heights

    [Fact]
    public void Text_under_an_eighth_inch_fails()
    {
        var facts = new SetFacts
        {
            AnnotationTypes =
            {
                new AnnotationTypeUsage("dimension", "3/32\" ALLEGRO ARCH", 0.09375, 120, "MAIN FLOOR PLAN"),
                new AnnotationTypeUsage("text", "ALLEGRO BODY TEXT", 0.125, 40, "sheet CS"),
            },
        };
        var r = Check(facts, "text-heights");
        Assert.Single(r.Findings);
        Assert.Contains("\"3/32\" ALLEGRO ARCH\" (120 uses on sheets, e.g. in MAIN FLOOR PLAN) is 3/32\" tall. Minimum is 1/8\".", r.Findings[0].Message);
    }

    [Fact]
    public void Text_just_under_an_eighth_shows_the_real_size()
    {
        var facts = new SetFacts { AnnotationTypes = { new AnnotationTypeUsage("text", "1/8\" ARIAL TEXT", 0.12, 25, "sheet CS") } };
        var r = Check(facts, "text-heights");
        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.Contains("is 0.120\" (just under 1/8\") tall. Minimum is 1/8\".", r.Findings[0].Message);
    }

    [Fact]
    public void Text_at_exactly_an_eighth_passes()
    {
        var facts = new SetFacts { AnnotationTypes = { new AnnotationTypeUsage("text", "1/8\" ARIAL TEXT", 0.125, 25, "sheet CS") } };
        Assert.Equal(CheckStatus.Pass, Check(facts, "text-heights").Status);
    }

    [Theory]
    [InlineData(0.09375, 0.125, "3/32\"")]
    [InlineData(0.12, 0.125, "0.120\" (just under 1/8\")")]
    [InlineData(0.1, 0.125, "0.100\"")]
    public void Labels_measured_inches(double inches, double reference, string expected) =>
        Assert.Equal(expected, SetAuditor.MeasuredInchesLabel(inches, reference));

    // -------------------------------------------------------------------- rooms

    [Fact]
    public void Room_problems_are_found()
    {
        var facts = new SetFacts
        {
            Rooms =
            {
                new RoomFact(1, "16'-3\" x 16'-3\"", "KITCHEN", "L1", 364, "placed", false),
                new RoomFact(2, "", "PANTRY", "L1", 72, "placed", false),
                new RoomFact(3, "126", "BREEZEWAY", "L1", 0, "not enclosed", false),
                new RoomFact(4, "32", "CARPET", "L0", 0, "unplaced", false),
                new RoomFact(5, "33", "Room", "L0", 0, "unplaced", false),
                new RoomFact(6, "101", "GREAT ROOM", "L1", 492, "placed", false),
            },
        };
        var r = Check(facts, "rooms");
        Assert.Contains(r.Findings, x => x.Message.StartsWith("2 unplaced rooms"));
        Assert.Contains(r.Findings, x => x.Message.Contains("not a room number") && x.ElementId == 1);
        Assert.Contains(r.Findings, x => x.Message.Contains("\"PANTRY\" has no number"));
        Assert.Contains(r.Findings, x => x.Message.Contains("not enclosed") && x.ElementId == 3);
        Assert.DoesNotContain(r.Findings, x => x.ElementId == 6);
    }

    [Fact]
    public void Door_with_no_rooms_warns()
    {
        var facts = new SetFacts { Openings = { Opening(9, "doors", "Door 551", "T: 2680"), Opening(10, "doors", "Door 360", "T: 2680", from: 1, to: 2) } };
        var r = Check(facts, "door-rooms");
        Assert.Single(r.Findings);
        Assert.Equal(9, r.Findings[0].ElementId);
    }

    // ----------------------------------------------------------- view-templates

    [Fact]
    public void Placed_views_need_office_templates()
    {
        var facts = new SetFacts
        {
            PlacedViews =
            {
                new ViewFact("MAIN FLOOR PLAN", "FloorPlan", 48, null, new[] { "A1" }),
                new ViewFact("FRONT ELEVATION", "Elevation", 48, "Architectural Elevation Copy", new[] { "A2" }),
                new ViewFact("FOUNDATION PLAN", "FloorPlan", 48, "ADC - Foundation Plan", new[] { "F1" }),
                new ViewFact("WALL LEGEND", "Legend", 48, null, new[] { "A1" }),
            },
        };
        var r = Check(facts, "view-templates", templates: new[] { "ADC - Foundation Plan", "ADC - Elevations" });
        Assert.Equal(2, r.FindingCount);
        Assert.Contains(r.Findings, x => x.Message.Contains("MAIN FLOOR PLAN") && x.Message.Contains("no view template"));
        Assert.Contains(r.Findings, x => x.Message.Contains("isn't in the office standards"));
    }

    // ------------------------------------------------------------------- egress

    [Fact]
    public void Bedroom_egress_rules()
    {
        var yes = new Dictionary<string, string?> { ["Tempered"] = "No", ["Egress"] = "Yes" };
        var facts = new SetFacts
        {
            Rooms =
            {
                new RoomFact(1, "101", "BEDROOM 2", "L1", 188, "placed", false),
                new RoomFact(2, "102", "MASTER BEDROOM", "L1", 302, "placed", false),
                new RoomFact(3, "103", "BEDROOM 4", "L1", 202, "placed", false),
                new RoomFact(4, "104", "BUNK ROOM", "L1", 150, "placed", false),
                new RoomFact(5, "105", "KITCHEN", "L1", 364, "placed", false),
            },
            Openings =
            {
                Opening(11, "windows", "Window 693", "ADC: 3050", to: 1, sill: 3, parameters: yes),
                Opening(12, "windows", "Window 668", "ADC: 3050", to: 2, sill: 3),
                Opening(13, "windows", "Window 670", "ADC: 3050", to: 3, sill: 4, parameters: yes),
            },
        };
        var r = Check(facts, "egress");
        Assert.Equal(3, r.FindingCount);
        Assert.Contains(r.Findings, x => x.Message.Contains("\"MASTER BEDROOM\"") && x.Message.Contains("no window is marked Egress = Yes"));
        Assert.Contains(r.Findings, x => x.Message.Contains("\"BEDROOM 4\"") && x.Message.Contains("sill at 4'-0\". Max is 44\""));
        Assert.Contains(r.Findings, x => x.Message.Contains("\"BUNK ROOM\"") && x.Message.Contains("has no windows"));
        Assert.DoesNotContain(r.Findings, x => x.Message.Contains("KITCHEN") || x.Message.Contains("BEDROOM 2\""));
    }

    [Fact]
    public void No_bedrooms_skips_egress()
    {
        var facts = new SetFacts { Rooms = { new RoomFact(1, "1", "GARAGE", "L1", 600, "placed", false) } };
        Assert.Equal(CheckStatus.Skip, Check(facts, "egress").Status);
    }

    // ----------------------------------------------------------------- tempered

    [Fact]
    public void Tempered_candidates()
    {
        var temperedYes = new Dictionary<string, string?> { ["Tempered"] = "Yes", ["Egress"] = "No" };
        var facts = new SetFacts
        {
            Rooms = { new RoomFact(50, "110", "BATHROOM", "L1", 48, "placed", HasWetFixture: true) },
            Openings =
            {
                Opening(1, "doors", "Door 568", "Door-Sliding-Milgard-Tuscany_Series-4_Panel: 12' x 10'", x: 0, y: 0, w: 12, h: 10),
                Opening(2, "doors", "Door 414", "Supa 300: 3080", x: 50, y: 0, w: 3, h: 8),
                Opening(3, "doors", "Door 597", "Frameless Glass Door: 2680", x: 100, y: 100, parameters: new() { ["Door Swing"] = "LH", ["Tempered"] = "Yes" }),
                // 1'-6" past the edge of door 414, sill 2'-0": within 24".
                Opening(10, "windows", "Window 700", "Casement: 3050", x: 50 + 1.5 + 1.5 + 1.5, y: 0, w: 3, h: 5, sill: 2),
                // In the bathroom, sill 3'-0".
                Opening(11, "windows", "Window 701", "Casement: 2040", x: 200, y: 200, w: 2, h: 4, sill: 3, to: 50),
                // 4' x 6' fixed, sill 1'-0": big, low pane.
                Opening(12, "windows", "Window 702", "Fixed: 4060", x: 300, y: 300, w: 4, h: 6, sill: 1),
                // Same as 12 but already marked.
                Opening(13, "windows", "Window 703", "Fixed: 4060", x: 400, y: 400, w: 4, h: 6, sill: 1, parameters: temperedYes),
                // Clerestory far from everything.
                Opening(14, "windows", "Window 704", "Fixed: 3020", x: 500, y: 500, w: 3, h: 2, sill: 8),
            },
        };
        var r = Check(facts, "tempered");
        Assert.Contains(r.Findings, x => x.ElementId == 1 && x.Severity == Severity.Fail);
        Assert.DoesNotContain(r.Findings, x => x.ElementId is 2 or 3 or 13 or 14);
        Assert.Contains(r.Findings, x => x.ElementId == 10 && x.Message.Contains("within 24\" of Door 414"));
        Assert.Contains(r.Findings, x => x.ElementId == 11 && x.Message.Contains("R308.4.5"));
        Assert.Contains(r.Findings, x => x.ElementId == 12 && x.Message.Contains("R308.4.3"));
        Assert.Equal(CheckStatus.Fail, r.Status);
    }

    [Fact]
    public void Tempered_no_with_a_reason_is_a_decision()
    {
        Dictionary<string, string?> P(string? tempered, string? comments = null) =>
            new() { ["Tempered"] = tempered, ["Egress"] = "No", ["Comments"] = comments };
        var facts = new SetFacts
        {
            Openings =
            {
                Opening(2, "doors", "Door 556", "Pocket: 4080", x: 0, y: 0, w: 4, h: 8),
                // All three sit next to door 556 with a low sill.
                Opening(20, "windows", "Window 670", "Casement: 3050", x: 4, y: 0, sill: 2, parameters: P("No", "Closet 3' deep or less, R308.4.2 exception")),
                Opening(21, "windows", "Window 671", "Casement: 3050", x: -4, y: 0, sill: 2, parameters: P("No")),
                Opening(22, "windows", "Window 672", "Casement: 3050", x: 0, y: 4, sill: 2, parameters: P(null)),
                // Glass door the designer exempted with a reason, and one with a bare No.
                Opening(30, "doors", "Door 600", "Frameless Glass Door: 2680", x: 900, y: 900,
                    parameters: new() { ["Door Swing"] = "LH", ["Tempered"] = "No", ["Comments"] = "Decorative glass, R308.4.1 exception" }),
                Opening(31, "doors", "Door 601", "Frameless Glass Door: 2680", x: 950, y: 950),
            },
        };
        var r = Check(facts, "tempered");
        Assert.DoesNotContain(r.Findings, x => x.ElementId is 20 or 30);
        Assert.Contains(r.Findings, x => x.ElementId == 21 && x.Message.Contains("set to Tempered = No with no reason. Add why to Tempered Reason or Comments"));
        Assert.Contains(r.Findings, x => x.ElementId == 22 && x.Message.Contains("Confirm, then set Tempered = Yes"));
        Assert.Contains(r.Findings, x => x.ElementId == 31 && x.Severity == Severity.Fail && x.Message.Contains("No with no reason"));
    }

    [Fact]
    public void Tempered_reason_can_come_from_a_custom_parameter()
    {
        var settings = new AuditSettings { TemperedReasonParameters = new() { "Tempered Reason" } };
        var facts = new SetFacts
        {
            Openings =
            {
                Opening(2, "doors", "Door 556", "Pocket: 4080", x: 0, y: 0, w: 4, h: 8),
                Opening(20, "windows", "Window 670", "Casement: 3050", x: 4, y: 0, sill: 2,
                    parameters: new() { ["Tempered"] = "No", ["Comments"] = "unrelated note", ["Tempered Reason"] = "Closet" }),
                Opening(21, "windows", "Window 671", "Casement: 3050", x: -4, y: 0, sill: 2,
                    parameters: new() { ["Tempered"] = "No", ["Comments"] = "unrelated note" }),
            },
        };
        var r = Check(facts, "tempered", settings);
        Assert.DoesNotContain(r.Findings, x => x.ElementId == 20);
        Assert.Contains(r.Findings, x => x.ElementId == 21);
    }

    // --------------------------------------------------------------- references

    [Fact]
    public void Typed_references_warn_and_broken_ones_fail()
    {
        var facts = new SetFacts
        {
            Sheets = { new SheetFact("F2", "Foundation Details", 8) },
            Texts =
            {
                new TextFact("OR\r14\" SQUARE FTG.\rSEE \"PIER/PAD DTL. 4/F2\"", "PIER LEGEND", new[] { "F1" }),
                new TextFact("SEE 3/S9 FOR BEAM", "MAIN FLOOR PLAN", new[] { "A1" }),
                new TextFact("1/4\" = 1'-0\"  2x6 @ 16\" O.C. R-38 W/ STONE", "WALL LEGEND", new[] { "A1" }),
            },
        };
        var r = Check(facts, "references");
        Assert.Equal(2, r.FindingCount);
        Assert.Contains(r.Findings, x => x.Severity == Severity.Warn && x.Message.Contains("\"4/F2\" in PIER LEGEND"));
        Assert.Contains(r.Findings, x => x.Severity == Severity.Fail && x.Message.Contains("sheet S9, which doesn't exist"));
    }

    // -------------------------------------------------------------- cover-sheet

    [Fact]
    public void Cover_sheet_needs_index_and_phrases()
    {
        var facts = new SetFacts
        {
            Sheets = { new SheetFact("CS", "Cover Sheet & General Notes", 5) },
            Texts = { new TextFact("ALL DIMENSIONS TO FACE OF STUD.\rDO NOT\r SCALE DRAWINGS.", "sheet CS", new[] { "CS" }) },
        };
        var r = Check(facts, "cover-sheet");
        Assert.Equal(2, r.FindingCount);
        Assert.Contains(r.Findings, x => x.Message.Contains("no sheet index"));
        Assert.Contains(r.Findings, x => x.Message.Contains("\"SCOPE OF WORK\""));

        facts.Texts.Add(new TextFact("SCOPE OF WORK: NEW SINGLE FAMILY RESIDENCE", "GENERAL NOTES", new[] { "CS" }));
        facts.Schedules.Add(Schedule("DRAWING INDEX", "sheets", new[] { "Sheet Number" }));
        Assert.Equal(CheckStatus.Pass, Check(facts, "cover-sheet").Status);
    }

    [Fact]
    public void Missing_cover_sheet_fails()
    {
        Assert.Equal(CheckStatus.Fail, Check(new SetFacts(), "cover-sheet").Status);
    }

    // -------------------------------------------------------------- the report

    [Fact]
    public void Full_run_counts_and_verdict()
    {
        var report = SetAuditor.Run(new SetFacts(), null);
        // Every check but the ones off by default (view-templates: the designer's call, office rule).
        Assert.Equal(SetAuditor.CheckIds.Count - 1, report.Checks.Count);
        Assert.DoesNotContain(report.Checks, c => c.Id == "view-templates");
        Assert.Contains(SetAuditor.Run(new SetFacts(), null, null, new[] { "view-templates" }).Checks, c => c.Id == "view-templates"); // still runs when asked by name
        Assert.Equal(SetAuditor.CheckIds.Count, SetAuditor.Run(new SetFacts(), new AuditSettings { DisabledChecks = new() }).Checks.Count);
        Assert.Equal(report.Checks.Count, report.Summary.Pass + report.Summary.Warn + report.Summary.Fail + report.Summary.Skip);
        Assert.StartsWith("Not ready to issue.", report.Summary.Verdict);
        Assert.Contains(report.Notes, n => n.Contains("exterior plan strings close to their overall"));
    }

    [Fact]
    public void Unknown_check_name_is_refused()
    {
        var ex = Assert.Throws<ToolArgumentException>(() => SetAuditor.Run(new SetFacts(), null, null, new[] { "tags", "spelling" }));
        Assert.Contains("'spelling'", ex.Message);
        Assert.Contains("type-marks", ex.Message);
    }

    [Fact]
    public void Findings_are_capped_but_counted()
    {
        var facts = new SetFacts();
        for (var i = 0; i < 40; i++) facts.Openings.Add(Opening(i, "doors", $"Door {i}", "T: 3080", plan: false));
        var r = Check(facts, "tags", new AuditSettings { MaxFindingsPerCheck = 10 });
        Assert.Equal(40, r.FindingCount);
        Assert.Equal(10, r.Findings.Count);
        Assert.Contains("Showing the first 10", r.Summary);
    }

    [Fact]
    public void Report_serializes_with_camel_case_and_string_enums()
    {
        var json = JsonSerializer.Serialize(SetAuditor.Run(new SetFacts(), null, null, new[] { "cover-sheet" }), Json.Indented);
        Assert.Contains("\"status\": \"fail\"", json);
        Assert.Contains("\"severity\": \"fail\"", json);
        Assert.Contains("\"verdict\":", json);
    }

    [Fact]
    public void Audit_settings_round_trip_and_survive_a_null()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "standards.json");
        var profile = Fixtures.Profile();
        profile.Manual.Audit.WindowOperations.Add("TRANSOM");
        profile.Manual.Audit.MinTextHeightInches = 0.1;
        File.WriteAllText(path, JsonSerializer.Serialize(profile, Json.Indented));

        var loaded = StandardsStore.Load(path);
        Assert.Contains("TRANSOM", loaded.Manual.Audit.WindowOperations);
        Assert.Equal(0.1, loaded.Manual.Audit.MinTextHeightInches);
        Assert.Equal(7, loaded.Manual.Audit.WindowOperations.Count); // replaced, not appended to the defaults

        File.WriteAllText(path, File.ReadAllText(path).Replace("\"audit\": {", "\"audit\": null, \"x\": {"));
        Assert.NotNull(StandardsStore.Load(path).Manual.Audit);
    }

    [Fact]
    public void Tool_is_in_the_catalog_and_read_only()
    {
        var tool = ToolCatalog.Find(ToolNames.AuditSet);
        Assert.NotNull(tool);
        Assert.True(tool!.ReadOnly);
        Assert.DoesNotContain(ToolNames.AuditSet, ToolNames.ProposalTools);
        foreach (var id in SetAuditor.CheckIds) Assert.Contains($"\"{id}\"", tool.InputSchemaJson);
    }
}
