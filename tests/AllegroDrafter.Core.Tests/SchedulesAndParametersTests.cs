using AllegroDrafter.Core.Mcp;
using AllegroDrafter.Core.Proposals;
using AllegroDrafter.Core.Standards;
using static AllegroDrafter.Core.Tests.Fixtures;

namespace AllegroDrafter.Core.Tests;

public class ScheduleParsingTests
{
    [Fact]
    public void Copy_mode_with_sheet_creates_then_places()
    {
        var request = ProposalRequestParser.Parse(ToolNames.ProposeSchedules, Obj("""
            { "title": "Schedules", "schedules": [
              { "name": "DOOR SCHEDULE", "sourceSchedule": "OFFICE DOOR SCHEDULE", "sheetNumber": "A6.1" },
              { "name": "WINDOW SCHEDULE", "sourceSchedule": "OFFICE WINDOW SCHEDULE", "sheetNumber": "A6.1", "xFeet": 1, "yFeet": 1.5 },
              { "name": "ROOM FINISH SCHEDULE", "category": "Rooms", "templateName": "Room Finish" } ] }
            """));

        Assert.Equal(5, request.Changes.Count);
        var door = Assert.IsType<CreateScheduleChange>(request.Changes[0]);
        Assert.True(door.IsCopy);
        var finish = Assert.IsType<CreateScheduleChange>(request.Changes[2]);
        Assert.Equal(("rooms", "Room Finish"), (finish.Category, finish.TemplateName));
        var place = Assert.IsType<PlaceViewChange>(request.Changes[3]);
        Assert.Equal(("A6.1", "DOOR SCHEDULE", "Schedule"), (place.SheetNumber, place.ViewName, place.ViewType));
        Assert.True(Assert.IsType<PlaceViewChange>(request.Changes[4]).HasPosition);
        Assert.Contains("copy of", door.Describe());
        Assert.Contains("rooms schedule", finish.Describe());
    }

    [Theory]
    [InlineData("""{ "name": "S", "sourceSchedule": "A", "category": "doors" }""", "not both")]
    [InlineData("""{ "name": "S" }""", "give sourceSchedule")]
    [InlineData("""{ "name": "S", "category": "doors" }""", "give sourceSchedule")]
    [InlineData("""{ "name": "S", "category": "walls", "templateName": "T" }""", "category must be one of")]
    [InlineData("""{ "name": "S", "sourceSchedule": "A", "sheetNumber": "A1", "xFeet": 1 }""", "both xFeet and yFeet")]
    public void Bad_schedule_input_is_refused(string item, string expected)
    {
        var ex = Assert.Throws<ToolArgumentException>(() =>
            ProposalRequestParser.Parse(ToolNames.ProposeSchedules, Obj($$"""{ "title": "t", "schedules": [ {{item}} ] }""")));
        Assert.Contains(expected, ex.Message);
    }
}

public class ParameterParsingTests
{
    [Fact]
    public void Values_accept_text_numbers_and_booleans()
    {
        var request = ProposalRequestParser.Parse(ToolNames.ProposeParameterValues, Obj("""
            { "title": "Door marks", "elements": [
              { "elementId": 123456, "values": { "Mark": 101, "Comments": " Pocket ", "Fire Rated": true } },
              { "elementId": 7, "label": "Room 104", "values": { "Floor Finish": "" } } ] }
            """));

        var first = Assert.IsType<SetParametersChange>(request.Changes[0]);
        Assert.Equal(123456, first.ElementId);
        Assert.Equal("Element 123456", first.ElementLabel);
        Assert.Equal(new[] { new ParameterValue("Mark", "101"), new ParameterValue("Comments", "Pocket"), new ParameterValue("Fire Rated", "Yes") }, first.Values);
        var second = Assert.IsType<SetParametersChange>(request.Changes[1]);
        Assert.Equal("Room 104: Floor Finish = (blank)", second.Describe());
    }

    [Theory]
    [InlineData("""{ "values": { "Mark": "1" } }""", "elementId is required")]
    [InlineData("""{ "elementId": "12", "values": { "Mark": "1" } }""", "whole number")]
    [InlineData("""{ "elementId": 1.5, "values": { "Mark": "1" } }""", "whole number")]
    [InlineData("""{ "elementId": 1 }""", "values is required")]
    [InlineData("""{ "elementId": 1, "values": {} }""", "at least one")]
    [InlineData("""{ "elementId": 1, "values": { "Mark": [1] } }""", "text, a number")]
    public void Bad_parameter_input_is_refused(string item, string expected)
    {
        var ex = Assert.Throws<ToolArgumentException>(() =>
            ProposalRequestParser.Parse(ToolNames.ProposeParameterValues, Obj($$"""{ "title": "t", "elements": [ {{item}} ] }""")));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Changes_with_same_content_are_equal()
    {
        var a = new SetParametersChange(1, "Door 101", new[] { new ParameterValue("Mark", "101") });
        var b = new SetParametersChange(1, "Door 101", new List<ParameterValue> { new("Mark", "101") });
        Assert.Equal(a, b);
    }
}

public class ScheduleAndFinishValidationTests
{
    private static StandardsProfile ProfileWithFinishes()
    {
        var p = Profile();
        p.Schedules.Add("OFFICE DOOR SCHEDULE");
        p.ViewTemplates.Add(new ViewTemplateInfo("Room Finish", "Schedule"));
        p.Manual.FinishCodes.Add(new FinishCode("T1", "PORCELAIN TILE"));
        p.Manual.FinishCodes.Add(new FinishCode("P1", "PAINT, EGGSHELL"));
        p.Manual.FinishCodes.Add(new FinishCode("CPT1", "CARPET"));
        return p;
    }

    private static List<string> Validate(StandardsProfile p, params ProposalChange[] changes) =>
        ProposalValidator.Validate(new ProposalRequest("t", "", changes), p);

    private static SetParametersChange Set(long id, params (string, string)[] values) =>
        new(id, $"Room {id}", values.Select(v => new ParameterValue(v.Item1, v.Item2)).ToList());

    [Fact]
    public void Known_schedule_and_template_pass()
    {
        Assert.Empty(Validate(ProfileWithFinishes(),
            new CreateScheduleChange("DOOR SCHEDULE", SourceSchedule: "OFFICE DOOR SCHEDULE"),
            new CreateScheduleChange("FINISHES", Category: "rooms", TemplateName: "Room Finish")));
    }

    [Fact]
    public void Invented_source_schedule_is_refused()
    {
        var error = Assert.Single(Validate(ProfileWithFinishes(), new CreateScheduleChange("X", SourceSchedule: "OFFICE DOOR SCHEDUEL")));
        Assert.Contains("Did you mean 'OFFICE DOOR SCHEDULE'?", error);
    }

    [Fact]
    public void Non_schedule_template_is_refused()
    {
        // "A - Floor Plan" is a real template but a FloorPlan one, not a Schedule one.
        var error = Assert.Single(Validate(ProfileWithFinishes(), new CreateScheduleChange("X", Category: "rooms", TemplateName: "A - Floor Plan 1/4\"")));
        Assert.Contains("Schedule view template", error);
    }

    [Fact]
    public void Duplicate_schedule_names_are_refused()
    {
        Assert.Single(Validate(ProfileWithFinishes(),
            new CreateScheduleChange("DOOR SCHEDULE", SourceSchedule: "OFFICE DOOR SCHEDULE"),
            new CreateScheduleChange("door schedule", SourceSchedule: "OFFICE DOOR SCHEDULE")));
    }

    [Fact]
    public void Valid_finish_codes_pass_including_combined_and_blank()
    {
        Assert.Empty(Validate(ProfileWithFinishes(),
            Set(1, ("Floor Finish", "T1"), ("Wall Finish", "P1/T1"), ("Base Finish", ""), ("Ceiling Finish", "P1"))));
    }

    [Fact]
    public void Invented_finish_code_is_refused_with_suggestion()
    {
        var error = Assert.Single(Validate(ProfileWithFinishes(), Set(1, ("Floor Finish", "CPT-1"))));
        Assert.Contains("Finish code 'CPT-1'", error);
        Assert.Contains("Did you mean 'CPT1'?", error);
    }

    [Fact]
    public void Finish_code_check_is_case_sensitive_and_checks_every_token()
    {
        Assert.Single(Validate(ProfileWithFinishes(), Set(1, ("Floor Finish", "t1"))));
        Assert.Single(Validate(ProfileWithFinishes(), Set(1, ("Wall Finish", "P1/T9"))));
    }

    [Fact]
    public void Finish_parameter_names_match_ignoring_case()
    {
        Assert.Single(Validate(ProfileWithFinishes(), Set(1, ("floor finish", "WOOD"))));
    }

    [Fact]
    public void Non_finish_parameters_are_not_code_checked()
    {
        Assert.Empty(Validate(ProfileWithFinishes(), Set(1, ("Name", "PRIMARY BATH"), ("Comments", "anything"))));
    }

    [Fact]
    public void Custom_finish_parameter_is_checked_when_listed()
    {
        var p = ProfileWithFinishes();
        p.Manual.FinishParameters.Add("FLOOR FIN 2");
        Assert.Single(Validate(p, Set(1, ("FLOOR FIN 2", "WOOD"))));
    }

    [Fact]
    public void Without_finish_codes_any_value_is_allowed()
    {
        Assert.Empty(Validate(Profile(), Set(1, ("Floor Finish", "WOOD"))));
    }

    [Fact]
    public void Same_element_twice_or_same_parameter_twice_is_refused()
    {
        Assert.Single(Validate(ProfileWithFinishes(), Set(1, ("Name", "A")), Set(1, ("Number", "101"))));
        Assert.Single(Validate(ProfileWithFinishes(), Set(1, ("Name", "A"), ("name", "B"))));
    }

    [Fact]
    public void Old_profiles_without_finish_settings_still_load_with_defaults()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "old.json");
        File.WriteAllText(path, """{ "schemaVersion": 1, "titleBlocks": ["A: B"], "manual": { "rules": ["x"] } }""");
        var profile = StandardsStore.Load(path);
        Assert.Empty(profile.Manual.FinishCodes);
        Assert.Equal(4, profile.Manual.FinishParameters.Count);
        Assert.Empty(profile.ScheduleCategories);
    }

    [Theory]
    [InlineData("T1", new[] { "T1" })]
    [InlineData("P1/T2", new[] { "P1", "T2" })]
    [InlineData(" T1 , T2 ;P3+ P4 ", new[] { "T1", "T2", "P3", "P4" })]
    [InlineData("", new string[0])]
    public void Finish_tokens_split(string value, string[] expected) =>
        Assert.Equal(expected, ProposalValidator.FinishTokens(value));
}
