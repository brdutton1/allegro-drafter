using AllegroDrafter.Core.Mcp;
using AllegroDrafter.Core.Proposals;
using AllegroDrafter.Core.Standards;
using static AllegroDrafter.Core.Tests.Fixtures;

namespace AllegroDrafter.Core.Tests;

public class AnnotationParsingTests
{
    private static ProposalRequest Parse(string tool, string json) => ProposalRequestParser.Parse(tool, Obj(json));

    private static void Refused(string tool, string json, string expected)
    {
        var ex = Assert.Throws<ToolArgumentException>(() => Parse(tool, json));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Tags_parse_with_defaults()
    {
        var change = Assert.IsType<TagViewChange>(Assert.Single(Parse(ToolNames.ProposeTags,
            """{ "title": "t", "views": [ { "viewName": "Level 1", "category": "Doors" } ] }""").Changes));
        Assert.Equal(("doors", null, false, true), (change.Category, change.TagType, change.Leader, change.UntaggedOnly));
        Assert.Contains("untagged doors", change.Describe());
        Assert.Contains("Tag 12 untagged", (change with { ExpectedCount = 12 }).Describe());
    }

    [Fact]
    public void Tags_refuse_unknown_category() =>
        Refused(ToolNames.ProposeTags, """{ "title": "t", "views": [ { "viewName": "L1", "category": "furniture" } ] }""", "category must be one of");

    [Fact]
    public void Detail_callout_with_sheet_creates_then_places()
    {
        var changes = Parse(ToolNames.ProposeCallouts, """
            { "title": "t", "callouts": [ { "parentView": "Level 1", "name": "ENLARGED KITCHEN", "viewFamilyType": "Detail View",
              "aroundRoomNumber": "104", "scale": 24, "sheetNumber": "A5.1" } ] }
            """).Changes;
        var callout = Assert.IsType<CalloutChange>(changes[0]);
        Assert.False(callout.IsReference);
        Assert.Null(callout.TemplateName);
        Assert.Equal(("104", 1.0, 24), (callout.Area.RoomNumber, callout.Area.MarginFeet, callout.Scale));
        var place = Assert.IsType<PlaceViewChange>(changes[1]);
        Assert.Equal(("A5.1", "ENLARGED KITCHEN"), (place.SheetNumber, place.ViewName));
        Assert.Contains("around room 104", callout.Describe());
    }

    [Fact]
    public void Callout_with_a_view_template_is_refused() =>
        Refused(ToolNames.ProposeCallouts, """
            { "title": "t", "callouts": [ { "parentView": "Level 1", "name": "E", "viewFamilyType": "Detail View", "aroundRoomNumber": "104", "templateName": "A - Enlarged Plan" } ] }
            """, "doesn't apply view templates");

    [Fact]
    public void Reference_callout_parses()
    {
        var callout = Assert.IsType<CalloutChange>(Assert.Single(Parse(ToolNames.ProposeCallouts, """
            { "title": "t", "callouts": [ { "parentView": "Level 1", "referenceView": "DETAIL - FOOTING", "aroundElementIds": [11, 12], "marginFeet": 0.5 } ] }
            """).Changes));
        Assert.True(callout.IsReference);
        Assert.Equal(new long[] { 11, 12 }, callout.Area.ElementIds);
        Assert.Contains("Reference callout", callout.Describe());
    }

    [Theory]
    [InlineData("""{ "parentView": "L1", "referenceView": "D", "name": "X", "aroundRoomNumber": "1" }""", "takes no name")]
    [InlineData("""{ "parentView": "L1", "name": "X", "aroundRoomNumber": "1" }""", "name + viewFamilyType")]
    [InlineData("""{ "parentView": "L1", "referenceView": "D" }""", "exactly one of")]
    [InlineData("""{ "parentView": "L1", "referenceView": "D", "aroundRoomNumber": "1", "aroundElementIds": [1] }""", "exactly one of")]
    [InlineData("""{ "parentView": "L1", "referenceView": "D", "minX": 0, "minY": 0, "maxX": 0, "maxY": 5 }""", "min < max")]
    [InlineData("""{ "parentView": "L1", "referenceView": "D", "minX": 0, "minY": 0 }""", "must all be given")]
    [InlineData("""{ "parentView": "L1", "referenceView": "D", "aroundRoomNumber": "1", "scale": 24 }""", "only applies to new detail")]
    [InlineData("""{ "parentView": "L1", "referenceView": "D", "aroundRoomNumber": "1", "sheetNumber": "A1" }""", "no new view to place")]
    [InlineData("""{ "parentView": "L1", "name": "X", "viewFamilyType": "V", "aroundRoomNumber": "1", "scale": 2.5 }""", "whole number")]
    [InlineData("""{ "parentView": "L1", "name": "X", "viewFamilyType": "V", "aroundRoomNumber": "1", "marginFeet": -1 }""", "can't be negative")]
    [InlineData("""{ "parentView": "L1", "referenceView": "D", "aroundElementIds": ["a"] }""", "whole number")]
    public void Bad_callouts_are_refused(string item, string expected) =>
        Refused(ToolNames.ProposeCallouts, $$"""{ "title": "t", "callouts": [ {{item}} ] }""", expected);

    [Fact]
    public void Notes_parse_and_require_position()
    {
        var note = Assert.IsType<TextNoteChange>(Assert.Single(Parse(ToolNames.ProposeNotes, """
            { "title": "t", "notes": [ { "viewName": "Level 1", "text": "SMOKE ALARM PER IRC R314\nINTERCONNECTED", "textType": "3/32\" Arial", "xFeet": 10, "yFeet": 5, "leaderToElementId": 99 } ] }
            """).Changes));
        Assert.Equal(99, note.LeaderToElementId);
        Assert.Contains(" / INTERCONNECTED", note.Describe());
        Refused(ToolNames.ProposeNotes, """{ "title": "t", "notes": [ { "viewName": "L1", "text": "x", "textType": "T" } ] }""", "xFeet and yFeet are required");
    }

    [Fact]
    public void Dimensions_default_to_all_three_tiers()
    {
        var dims = Assert.IsType<DimensionViewChange>(Assert.Single(Parse(ToolNames.ProposeDimensions,
            """{ "title": "t", "views": [ { "viewName": "Level 1" } ] }""").Changes));
        Assert.Equal(new[] { "openings", "walls", "overall" }, dims.Strategies);
        Assert.Equal((4.0, 2.0, false), (dims.OffsetFeet, dims.StepFeet, dims.AllowExisting));
        Assert.Contains("Add 8 exterior", (dims with { ExpectedStrings = 8 }).Describe());
    }

    [Theory]
    [InlineData("""{ "viewName": "L1", "strategies": ["radial"] }""", "unknown 'radial'")]
    [InlineData("""{ "viewName": "L1", "strategies": [] }""", "can't be empty")]
    [InlineData("""{ "viewName": "L1", "offsetFeet": 0 }""", "must be positive")]
    public void Bad_dimensions_are_refused(string item, string expected) =>
        Refused(ToolNames.ProposeDimensions, $$"""{ "title": "t", "views": [ {{item}} ] }""", expected);
}

public class AnnotationValidationTests
{
    private static StandardsProfile P()
    {
        var p = Profile();
        p.TagTypes.Add(new TagTypeInfo("Door Tags", "Door Tag: Standard"));
        p.ViewFamilyTypes.Add(new ViewFamilyTypeInfo("Detail View", "Detail"));
        p.TextNoteTypes.Add("3/32\" Arial");
        p.DimensionTypes.Add("Linear: Arch 3/32\"");
        return p;
    }

    private static List<string> V(params ProposalChange[] changes) => ProposalValidator.Validate(new ProposalRequest("t", "", changes), P());

    private static CalloutChange Callout(string name, string vft = "Detail View", string? template = null) =>
        new("Level 1", new CalloutArea(RoomNumber: "104"), Name: name, ViewFamilyType: vft, TemplateName: template);

    [Fact]
    public void Office_names_pass()
    {
        Assert.Empty(V(
            new TagViewChange("Level 1", "doors", "Door Tag: Standard"),
            new TagViewChange("Level 1", "windows"),
            Callout("ENLARGED KITCHEN"),
            new TextNoteChange("Level 1", "NOTE", "3/32\" Arial", 0, 0),
            new DimensionViewChange("Level 1", new[] { "overall" }, "Linear: Arch 3/32\"")));
    }

    [Fact]
    public void Invented_names_are_refused()
    {
        Assert.Contains("Tag type", Assert.Single(V(new TagViewChange("Level 1", "doors", "Door Tag: Fancy"))));
        Assert.Contains("View type", Assert.Single(V(Callout("X", vft: "Enlarged"))));
        Assert.Contains("doesn't apply view templates", Assert.Single(V(Callout("X", template: "Nope"))));
        Assert.Contains("Text type", Assert.Single(V(new TextNoteChange("Level 1", "N", "Comic Sans", 0, 0))));
        Assert.Contains("Dimension type", Assert.Single(V(new DimensionViewChange("Level 1", new[] { "overall" }, "Linear: Wild"))));
    }

    [Fact]
    public void Duplicates_are_refused()
    {
        Assert.Single(V(new TagViewChange("Level 1", "doors"), new TagViewChange("Level 1", "doors", "Door Tag: Standard")));
        Assert.Single(V(Callout("KITCHEN"), Callout("kitchen")));
        Assert.Single(V(new DimensionViewChange("Level 1", new[] { "overall" }), new DimensionViewChange("Level 1", new[] { "walls" })));
    }

    [Fact]
    public void Same_view_different_categories_is_fine()
    {
        Assert.Empty(V(new TagViewChange("Level 1", "doors"), new TagViewChange("Level 1", "windows"), new TagViewChange("Level 1", "rooms")));
    }
}
