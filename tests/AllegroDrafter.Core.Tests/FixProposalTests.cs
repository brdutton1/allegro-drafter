using System.Text.Json;
using AllegroDrafter.Core.Mcp;
using AllegroDrafter.Core.Proposals;

namespace AllegroDrafter.Core.Tests;

public class FixProposalTests
{
    private static ProposalRequest Parse(string tool, string json) => ProposalRequestParser.Parse(tool, Fixtures.Obj(json));

    private static List<string> Validate(string tool, string json) => ProposalValidator.Validate(Parse(tool, json), Fixtures.Profile());

    // ------------------------------------------------------------- type marks

    [Fact]
    public void Type_marks_parse_and_normalize()
    {
        var r = Parse(ToolNames.ProposeTypeMarks, """
            { "title": "Door size codes", "types": [
                { "category": "Doors", "typeName": "Supa 300: 3080", "typeMark": " 3080 " },
                { "category": "windows", "typeName": "ADC-Window-Casement-Double: 6060", "typeMark": "6060  csmt" } ] }
            """);
        Assert.Equal(new SetTypeMarkChange("doors", "Supa 300: 3080", "3080"), r.Changes[0]);
        Assert.Equal("6060 CSMT", ((SetTypeMarkChange)r.Changes[1]).TypeMark);
    }

    [Fact]
    public void Type_marks_must_be_office_size_codes()
    {
        var errors = Validate(ToolNames.ProposeTypeMarks, """
            { "title": "t", "types": [
                { "category": "doors", "typeName": "Supa 300: 3080", "typeMark": "376" },
                { "category": "windows", "typeName": "A: 6060", "typeMark": "6060 CMST" },
                { "category": "windows", "typeName": "B: 3050", "typeMark": "3050" },
                { "category": "doors", "typeName": "C: 2680", "typeMark": "2680 PKT" } ] }
            """);
        Assert.Contains(errors, e => e.StartsWith("Change 1: \"376\"") && e.Contains("isn't a size code"));
        Assert.Contains(errors, e => e.StartsWith("Change 2:") && e.Contains("Did you mean CSMT?"));
        Assert.Contains(errors, e => e.StartsWith("Change 3:") && e.Contains("needs an operation code"));
        Assert.DoesNotContain(errors, e => e.StartsWith("Change 4:"));
    }

    [Fact]
    public void Two_types_on_one_mark_in_a_proposal_are_refused()
    {
        var errors = Validate(ToolNames.ProposeTypeMarks, """
            { "title": "t", "types": [
                { "category": "doors", "typeName": "Supa 300: 2680", "typeMark": "2680" },
                { "category": "doors", "typeName": "Frameless Glass Door: 2680", "typeMark": "2680" },
                { "category": "doors", "typeName": "Supa 300: 2680", "typeMark": "2680" } ] }
            """);
        Assert.Contains(errors, e => e.StartsWith("Change 2:") && e.Contains("more than one doors type"));
        Assert.Contains(errors, e => e.StartsWith("Change 3:") && e.Contains("gets a Type Mark more than once"));
    }

    [Fact]
    public void Type_mark_change_describes_what_the_designer_approves()
    {
        var c = new SetTypeMarkChange("doors", "Supa 300: 3080", "3080", CurrentMark: "376", Size: "3'-0\" x 8'-0\"", InstanceCount: 8);
        Assert.Equal("Door type \"Supa 300: 3080\" (8 placed, 3'-0\" x 8'-0\"): Type Mark 376 -> 3080", c.Describe());
        Assert.Equal("Window type \"X: 3050\": Type Mark (blank) -> 3050 CSMT", new SetTypeMarkChange("windows", "X: 3050", "3050 CSMT").Describe());
    }

    [Fact]
    public void Size_override_is_carried_and_shown_to_the_designer()
    {
        var r = Parse(ToolNames.ProposeTypeMarks, """
            { "title": "t", "types": [ { "category": "doors", "typeName": "Milgard 3-Panel: 126\" x 88\"", "typeMark": "10674 SLDR",
                "sizeOverride": "Designer: use the size in the type name (126\" x 88\")." } ] }
            """);
        var c = Assert.IsType<SetTypeMarkChange>(r.Changes.Single());
        Assert.Equal("Designer: use the size in the type name (126\" x 88\").", c.SizeOverride);
        var shown = (c with { CurrentMark = "440", Size = "10'-6\" x 10'-0\"", InstanceCount = 2 }).Describe();
        Assert.Equal("Door type \"Milgard 3-Panel: 126\" x 88\"\" (2 placed, family reads 10'-6\" x 10'-0\"): Type Mark 440 -> 10674 SLDR. SIZE CHECK OVERRIDDEN: Designer: use the size in the type name (126\" x 88\").", shown);

        // The office rules still apply: an override doesn't excuse a bad code.
        var bad = Validate(ToolNames.ProposeTypeMarks, """
            { "title": "t", "types": [ { "category": "doors", "typeName": "X", "typeMark": "10674 SLIDE", "sizeOverride": "name size" } ] }
            """);
        Assert.Contains(bad, e => e.Contains("isn't an office operation code"));

        Assert.Throws<ToolArgumentException>(() => Parse(ToolNames.ProposeTypeMarks,
            """{ "title": "t", "types": [ { "category": "doors", "typeName": "X", "typeMark": "3080", "sizeOverride": "  " } ] }"""));
    }

    [Fact]
    public void Schedule_fields_can_go_before_a_column()
    {
        var r = Parse(ToolNames.ProposeScheduleFields, """
            { "title": "t", "schedules": [ { "schedule": "DOOR SCHEDULE", "add": [
                { "field": "Type Mark", "before": "Width" }, { "field": "Count", "after": "Type Mark" }, { "field": "Tempered" } ] } ] }
            """);
        var c = Assert.IsType<ScheduleFieldsChange>(r.Changes.Single());
        Assert.Equal(new FieldToAdd("Type Mark", Before: "Width"), c.Add[0]);
        Assert.Equal("Schedule \"DOOR SCHEDULE\": add \"Type Mark\" before \"Width\", \"Count\" after \"Type Mark\", \"Tempered\"", c.Describe());

        var ex = Assert.Throws<ToolArgumentException>(() => Parse(ToolNames.ProposeScheduleFields,
            """{ "title": "t", "schedules": [ { "schedule": "S", "add": [ { "field": "A", "after": "B", "before": "C" } ] } ] }"""));
        Assert.Contains("not both", ex.Message);
    }

    [Fact]
    public void Bad_type_mark_category_is_refused()
    {
        var ex = Assert.Throws<ToolArgumentException>(() => Parse(ToolNames.ProposeTypeMarks,
            """{ "title": "t", "types": [ { "category": "walls", "typeName": "x", "typeMark": "3050" } ] }"""));
        Assert.Contains("doors or windows", ex.Message);
    }

    // ------------------------------------------------------------- parameters

    [Fact]
    public void Parameters_parse_with_yesno_default()
    {
        var r = Parse(ToolNames.ProposeParameters, """
            { "title": "Tempered and egress", "parameters": [
                { "name": "Tempered", "categories": ["Doors", "windows"] },
                { "name": "Egress", "type": "yesno", "categories": ["windows"] } ] }
            """);
        var tempered = Assert.IsType<CreateSharedParameterChange>(r.Changes[0]);
        Assert.Equal("yesno", tempered.DataType);
        Assert.Equal(new[] { "doors", "windows" }, tempered.Categories);
        Assert.Equal("Add Yes/No instance parameter \"Tempered\" to Doors, Windows", tempered.Describe());
        Assert.Empty(ProposalValidator.Validate(r, Fixtures.Profile()));
    }

    [Theory]
    [InlineData("""{ "title": "t", "parameters": [ { "name": "Tempered", "categories": ["walls"] } ] }""", "not allowed")]
    [InlineData("""{ "title": "t", "parameters": [ { "name": "Tempered" } ] }""", "categories is required")]
    [InlineData("""{ "title": "t", "parameters": [ { "name": "Tempered", "type": "number", "categories": ["doors"] } ] }""", "yesno or text")]
    [InlineData("""{ "title": "t", "parameters": [ { "name": "  ", "categories": ["doors"] } ] }""", "parameters[0].name")]
    public void Bad_parameters_are_refused(string json, string expected)
    {
        var ex = Assert.Throws<ToolArgumentException>(() => Parse(ToolNames.ProposeParameters, json));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Same_parameter_twice_is_refused()
    {
        var errors = Validate(ToolNames.ProposeParameters, """
            { "title": "t", "parameters": [
                { "name": "Tempered", "categories": ["doors"] },
                { "name": "Tempered", "categories": ["windows"] } ] }
            """);
        Assert.Contains(errors, e => e.Contains("added more than once"));
    }

    // ------------------------------------------------------------ schedule sort

    [Fact]
    public void Schedule_sort_parses()
    {
        var r = Parse(ToolNames.ProposeScheduleSort, """
            { "title": "Sort by size code", "schedules": [
                { "schedule": "DOOR SCHEDULE", "sortBy": ["Type Mark", "Door Swing"], "itemized": false },
                { "schedule": "WINDOW SCHEDULE", "sortBy": ["Type Mark"] } ] }
            """);
        Assert.Equal(new ScheduleSortChange("DOOR SCHEDULE", new[] { "Type Mark", "Door Swing" }, false), r.Changes[0]);
        Assert.Null(((ScheduleSortChange)r.Changes[1]).Itemized);
        Assert.Equal("Schedule \"DOOR SCHEDULE\": sort by \"Type Mark\", then \"Door Swing\"; one row per group, with Count", r.Changes[0].Describe());
    }

    [Fact]
    public void Schedule_sort_needs_distinct_fields()
    {
        Assert.Throws<ToolArgumentException>(() => Parse(ToolNames.ProposeScheduleSort,
            """{ "title": "t", "schedules": [ { "schedule": "S", "sortBy": [] } ] }"""));
        var ex = Assert.Throws<ToolArgumentException>(() => Parse(ToolNames.ProposeScheduleSort,
            """{ "title": "t", "schedules": [ { "schedule": "S", "sortBy": ["Type Mark", "type mark"] } ] }"""));
        Assert.Contains("more than once", ex.Message);
        var errors = Validate(ToolNames.ProposeScheduleSort,
            """{ "title": "t", "schedules": [ { "schedule": "S", "sortBy": ["A"] }, { "schedule": "S", "sortBy": ["B"] } ] }""");
        Assert.Contains(errors, e => e.Contains("sorted more than once"));
    }

    // ------------------------------------------------------------- room cleanup

    [Fact]
    public void Room_cleanup_takes_ids_or_all_unplaced()
    {
        var byIds = (DeleteRoomsChange)Parse(ToolNames.ProposeRoomCleanup, """{ "title": "t", "roomIds": [5, 3, 5] }""").Changes.Single();
        Assert.Equal(new long[] { 5, 3 }, byIds.RoomIds);
        Assert.False(byIds.AllUnplaced);

        var all = (DeleteRoomsChange)Parse(ToolNames.ProposeRoomCleanup, """{ "title": "t", "allUnplaced": true }""").Changes.Single();
        Assert.True(all.AllUnplaced);
        Assert.Equal("Delete every unplaced and unenclosed room", all.Describe());
        Assert.Equal("Delete 123 unplaced or unenclosed room(s): CARPET x6, Room x15",
            (all with { RoomIds = Enumerable.Range(1, 123).Select(i => (long)i).ToList(), Summary = "CARPET x6, Room x15" }).Describe());

        Assert.Throws<ToolArgumentException>(() => Parse(ToolNames.ProposeRoomCleanup, """{ "title": "t" }"""));
        Assert.Throws<ToolArgumentException>(() => Parse(ToolNames.ProposeRoomCleanup, """{ "title": "t", "roomIds": [1], "allUnplaced": true }"""));
    }

    // --------------------------------------------------------------- plumbing

    [Fact]
    public void New_changes_survive_the_proposal_store_round_trip()
    {
        var changes = new List<ProposalChange>
        {
            new SetTypeMarkChange("doors", "Supa 300: 3080", "3080", "376", "3'-0\" x 8'-0\"", 8),
            new CreateSharedParameterChange("Tempered", "yesno", new[] { "doors", "windows" }),
            new ScheduleSortChange("DOOR SCHEDULE", new[] { "Type Mark", "Door Swing" }, false),
            new DeleteRoomsChange(new long[] { 1, 2 }, true, "CARPET x2"),
        };
        var json = JsonSerializer.Serialize<IReadOnlyList<ProposalChange>>(changes, Json.Indented);
        Assert.Contains("\"kind\": \"setTypeMark\"", json);
        Assert.Contains("\"kind\": \"deleteRooms\"", json);
        var back = JsonSerializer.Deserialize<List<ProposalChange>>(json, Json.Indented)!;
        Assert.Equal(changes, back);
    }

    [Fact]
    public void Fix_tools_are_proposal_tools_in_the_catalog()
    {
        foreach (var name in new[] { ToolNames.ProposeTypeMarks, ToolNames.ProposeParameters, ToolNames.ProposeScheduleSort, ToolNames.ProposeRoomCleanup })
        {
            var tool = ToolCatalog.Find(name);
            Assert.NotNull(tool);
            Assert.False(tool!.ReadOnly);
            Assert.Contains(name, ToolNames.ProposalTools);
            System.Text.Json.Nodes.JsonNode.Parse(tool.InputSchemaJson); // schema is valid JSON
        }
        Assert.Contains("propose_type_marks", ToolCatalog.ServerInstructions);
    }
}
