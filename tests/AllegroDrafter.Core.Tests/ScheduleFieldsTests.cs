using AllegroDrafter.Core.Mcp;
using AllegroDrafter.Core.Proposals;
using static AllegroDrafter.Core.Tests.Fixtures;

namespace AllegroDrafter.Core.Tests;

public class ScheduleFieldsTests
{
    [Fact]
    public void Parses_add_after_and_remove()
    {
        var change = Assert.IsType<ScheduleFieldsChange>(Assert.Single(ProposalRequestParser.Parse(ToolNames.ProposeScheduleFields, Obj("""
            { "title": "Add marks", "schedules": [ { "schedule": "DOOR SCHEDULE",
              "add": [ { "field": "Mark" }, { "field": "Type Mark", "after": "Mark" } ], "remove": [ "Type Comments" ] } ] }
            """)).Changes));
        Assert.Equal(new[] { new FieldToAdd("Mark"), new FieldToAdd("Type Mark", "Mark") }, change.Add);
        Assert.Equal(new[] { "Type Comments" }, change.Remove);
        Assert.Equal("Schedule \"DOOR SCHEDULE\": add \"Mark\", \"Type Mark\" after \"Mark\"; remove \"Type Comments\"", change.Describe());
    }

    [Theory]
    [InlineData("""{ "schedule": "S" }""", "add and/or remove")]
    [InlineData("""{ "add": [ { "field": "Mark" } ] }""", "schedule is required")]
    [InlineData("""{ "schedule": "S", "add": [ { "after": "Width" } ] }""", "field is required")]
    [InlineData("""{ "schedule": "S", "remove": [ 3 ] }""", "must be a string")]
    public void Bad_input_is_refused(string item, string expected)
    {
        var ex = Assert.Throws<ToolArgumentException>(() =>
            ProposalRequestParser.Parse(ToolNames.ProposeScheduleFields, Obj($$"""{ "title": "t", "schedules": [ {{item}} ] }""")));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Repeated_fields_and_repeated_schedules_are_refused()
    {
        var p = Profile();
        Assert.Single(ProposalValidator.Validate(new ProposalRequest("t", "", new ProposalChange[]
        {
            new ScheduleFieldsChange("DOOR SCHEDULE", new[] { new FieldToAdd("Mark") }, new[] { "mark" }),
        }), p));
        Assert.Single(ProposalValidator.Validate(new ProposalRequest("t", "", new ProposalChange[]
        {
            new ScheduleFieldsChange("DOOR SCHEDULE", new[] { new FieldToAdd("Mark") }, Array.Empty<string>()),
            new ScheduleFieldsChange("DOOR SCHEDULE", new[] { new FieldToAdd("Comments") }, Array.Empty<string>()),
        }), p));
        Assert.Empty(ProposalValidator.Validate(new ProposalRequest("t", "", new ProposalChange[]
        {
            new ScheduleFieldsChange("DOOR SCHEDULE", new[] { new FieldToAdd("Mark") }, Array.Empty<string>()),
            new ScheduleFieldsChange("WINDOW SCHEDULE", new[] { new FieldToAdd("Mark") }, Array.Empty<string>()),
        }), p));
    }
}
