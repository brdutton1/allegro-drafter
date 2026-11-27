using AllegroDrafter.Core.Checks;
using AllegroDrafter.Core.Finishing;

namespace AllegroDrafter.Core.Tests;

/// <summary>Schedule check: rows match the model, doors have rooms, tags print the schedule mark, blanks carry a fix.</summary>
public class ScheduleContentsTests
{
    private static readonly Dictionary<string, string?> DoorDone = new() { ["Door Swing"] = "LH", ["Tempered"] = "No" };

    private static OpeningFact Door(long id, string label, string type, long? from = 1, long? to = 2, Dictionary<string, string?>? p = null) =>
        new(id, "doors", label, type, "MAIN FLOOR", id, 0, 3, 8, null, false, from, to, true, true, p ?? DoorDone);

    private static ScheduleFact DoorSchedule(params long[] rows) =>
        new("DOOR SCHEDULE", "doors", false, true,
            new[] { "Type Mark", "From Room: Name", "To Room: Name", "Door Swing", "Tempered" }.Select(f => new ScheduleFieldFact(f, false)).ToList(),
            new[] { "Type Mark" }, new[] { "CS" }, FirstGroupCounts: true, ElementIds: rows);

    private static SetFacts Facts()
    {
        var facts = new SetFacts
        {
            OpeningTypes = { new OpeningTypeFact("doors", "Supa 300: 3080", "Supa 300", "3080", "3080", 3, 8, 2) },
            Openings = { Door(414, "Door 414", "Supa 300: 3080"), Door(546, "Door 546", "Supa 300: 3080") },
        };
        facts.Schedules.Add(DoorSchedule(414, 546));
        return facts;
    }

    private static CheckResult Check(SetFacts facts, string id = "schedule-contents") =>
        SetAuditor.Run(facts, null, null, new[] { id }).Checks.Single();

    [Fact]
    public void A_clean_schedule_passes()
    {
        var facts = Facts();
        facts.TagTexts.Add(new TagTextFact(9001, 414, "doors", "MAIN FLOOR PLAN", "3080"));
        var r = Check(facts);
        Assert.Equal(CheckStatus.Pass, r.Status);
        Assert.Contains("2 rows", r.Summary);
    }

    [Fact]
    public void A_door_the_schedule_leaves_out_fails()
    {
        var facts = Facts();
        facts.Schedules[0] = DoorSchedule(414); // a filter or phase drops 546
        var f = Assert.Single(Check(facts).Findings);
        Assert.Equal(546, f.ElementId);
        Assert.Contains("Door 546 isn't on \"DOOR SCHEDULE\"", f.Message);
    }

    [Fact]
    public void A_door_with_no_room_either_side_fails_one_side_is_fine()
    {
        var facts = Facts();
        facts.Openings[0] = Door(414, "Door 414", "Supa 300: 3080", from: null, to: null);
        facts.Openings[1] = Door(546, "Door 546", "Supa 300: 3080", from: null, to: 2); // exterior door: outside has no room
        var f = Assert.Single(Check(facts).Findings);
        Assert.Equal(414, f.ElementId);
        Assert.Contains("From Room and To Room are both blank", f.Message);
    }

    [Theory]
    [InlineData("D3", false)]          // typed or a Mark-based tag
    [InlineData("", false)]
    [InlineData("3080", true)]
    [InlineData("3080\nLH", true)]     // extra lines on the tag are fine
    public void Tags_must_print_the_schedule_mark(string printed, bool ok)
    {
        var facts = Facts();
        facts.TagTexts.Add(new TagTextFact(9001, 414, "doors", "MAIN FLOOR PLAN", printed));
        var r = Check(facts);
        Assert.Equal(ok, r.Status == CheckStatus.Pass);
        if (!ok) Assert.Contains("its tag on MAIN FLOOR PLAN prints", Assert.Single(r.Findings).Message);
    }

    [Fact]
    public void An_ext_mark_isnt_satisfied_by_a_tag_that_drops_the_ext()
    {
        var facts = Facts();
        facts.OpeningTypes[0] = facts.OpeningTypes[0] with { TypeMark = "3080 EXT" };
        facts.TagTexts.Add(new TagTextFact(9001, 414, "doors", "RIGHT ELEVATION", "3080"));
        Assert.Contains("schedule row is \"3080 EXT\"", Assert.Single(Check(facts).Findings).Message);
    }

    [Fact]
    public void No_placed_schedule_skips()
    {
        var facts = Facts();
        facts.Schedules[0] = facts.Schedules[0] with { OnSheets = Array.Empty<string>() };
        Assert.Equal(CheckStatus.Skip, Check(facts).Status);
    }

    [Fact]
    public void Unreadable_rows_warn_instead_of_passing_silently()
    {
        var facts = Facts();
        facts.Schedules[0] = facts.Schedules[0] with { ElementIds = null };
        Assert.Contains("couldn't read which elements it lists", Assert.Single(Check(facts).Findings).Message);
    }

    // ---------------------------------------------------------------- fixes on blanks

    [Fact]
    public void Blank_swing_carries_the_swing_read_from_the_door()
    {
        var facts = Facts();
        facts.Openings[0] = Door(414, "Door 414", "Supa 300: 3080", p: new() { ["Door Swing"] = null, ["Tempered"] = "No" });
        facts.DoorGeometry.Add(new DoorGeometry(414, "Door 414", "Supa 300", "3080", 0, 1, -1, 0));
        var expected = DoorHanding.Derive(facts.DoorGeometry[0], new AuditSettings()).Value;
        Assert.NotNull(expected);

        var f = Assert.Single(Check(facts, "opening-parameters").Findings);
        Assert.Equal(("Door Swing", expected), (f.Target, f.Suggestion));
        Assert.Equal(414, f.ElementId);
        Assert.Contains($"Rule says: {expected}", f.Message);
    }

    [Fact]
    public void Blank_tempered_on_a_glass_door_carries_yes_and_others_are_left_to_the_designer()
    {
        var facts = Facts();
        facts.OpeningTypes.Add(new OpeningTypeFact("doors", "Frameless Glass Door: 2680", "Frameless Glass Door", "2680", "2680 SHWR", 2.5, 8, 1));
        facts.Openings.Add(Door(597, "Door 597", "Frameless Glass Door: 2680", p: new() { ["Door Swing"] = "LH", ["Tempered"] = null }));
        facts.Openings[0] = Door(414, "Door 414", "Supa 300: 3080", p: new() { ["Door Swing"] = "LH", ["Tempered"] = null });
        var findings = Check(facts, "opening-parameters").Findings;
        Assert.Equal("Yes", findings.Single(x => x.ElementId == 597).Suggestion);
        Assert.Null(findings.Single(x => x.ElementId == 414).Suggestion);
    }
}
