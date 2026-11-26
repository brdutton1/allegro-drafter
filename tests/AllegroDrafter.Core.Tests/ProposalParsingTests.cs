using AllegroDrafter.Core.Mcp;
using AllegroDrafter.Core.Proposals;
using static AllegroDrafter.Core.Tests.Fixtures;

namespace AllegroDrafter.Core.Tests;

public class ProposalParsingTests
{
    [Fact]
    public void SheetSet_puts_all_sheets_before_placements()
    {
        var args = Obj("""
        { "title": "A sheets", "rationale": "New home CD set",
          "sheets": [
            { "number": "A1.0", "name": "MAIN FLOOR PLAN", "titleBlock": "Allegro 24x36: Standard",
              "views": [ { "viewName": "Level 1", "viewType": "FloorPlan" } ] },
            { "number": "A2.0", "name": "ELEVATIONS", "titleBlock": "Allegro 24x36: Standard",
              "views": [ { "viewName": "North", "xFeet": 1.0, "yFeet": 0.75 }, { "viewName": "South" } ] } ] }
        """);

        var request = ProposalRequestParser.Parse(ToolNames.ProposeSheetSet, args);

        Assert.Equal("A sheets", request.Title);
        Assert.Equal("New home CD set", request.Rationale);
        Assert.Equal(5, request.Changes.Count);
        Assert.IsType<CreateSheetChange>(request.Changes[0]);
        Assert.IsType<CreateSheetChange>(request.Changes[1]);
        var first = Assert.IsType<PlaceViewChange>(request.Changes[2]);
        Assert.Equal(("A1.0", "Level 1", "FloorPlan"), (first.SheetNumber, first.ViewName, first.ViewType));
        Assert.False(first.HasPosition);
        var north = Assert.IsType<PlaceViewChange>(request.Changes[3]);
        Assert.True(north.HasPosition);
        Assert.Equal(1.0, north.XFeet);
    }

    [Fact]
    public void ProposeSheets_refuses_views()
    {
        var args = Obj("""{ "title": "t", "sheets": [ { "number": "A1", "name": "n", "titleBlock": "tb", "views": [] } ] }""");
        var ex = Assert.Throws<ToolArgumentException>(() => ProposalRequestParser.Parse(ToolNames.ProposeSheets, args));
        Assert.Contains("propose_sheet_set", ex.Message);
    }

    [Theory]
    [InlineData("""{ "sheets": [ { "number": "A1", "name": "n", "titleBlock": "tb" } ] }""", "arguments.title")]
    [InlineData("""{ "title": "t", "sheets": [] }""", "at least one")]
    [InlineData("""{ "title": "t" }""", "arguments.sheets is required")]
    [InlineData("""{ "title": "t", "sheets": [ { "name": "n", "titleBlock": "tb" } ] }""", "sheets[0].number")]
    [InlineData("""{ "title": "t", "sheets": [ "A1" ] }""", "sheets[0] must be an object")]
    [InlineData("""{ "title": "t", "sheets": [ { "number": 7, "name": "n", "titleBlock": "tb" } ] }""", "sheets[0].number must be a string")]
    [InlineData("""{ "title": "  ", "sheets": [ { "number": "A1", "name": "n", "titleBlock": "tb" } ] }""", "arguments.title")]
    public void Bad_sheet_input_names_the_exact_field(string json, string expected)
    {
        var ex = Assert.Throws<ToolArgumentException>(() => ProposalRequestParser.Parse(ToolNames.ProposeSheets, Obj(json)));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Placement_needs_both_coordinates_or_neither()
    {
        var args = Obj("""{ "title": "t", "placements": [ { "sheetNumber": "A1", "viewName": "L1", "xFeet": 1 } ] }""");
        var ex = Assert.Throws<ToolArgumentException>(() => ProposalRequestParser.Parse(ToolNames.ProposePlaceViews, args));
        Assert.Contains("both xFeet and yFeet", ex.Message);
    }

    [Fact]
    public void Placement_rejects_non_numeric_coordinates()
    {
        var args = Obj("""{ "title": "t", "placements": [ { "sheetNumber": "A1", "viewName": "L1", "xFeet": "1", "yFeet": 2 } ] }""");
        Assert.Throws<ToolArgumentException>(() => ProposalRequestParser.Parse(ToolNames.ProposePlaceViews, args));
    }

    [Fact]
    public void View_template_tool_is_gone()
    {
        // Office rule (2026-11-26): the drafter never applies view templates.
        var args = Obj("""{ "title": "t", "assignments": [ { "viewName": "Level 1", "templateName": "A - Floor Plan 1/4\"" } ] }""");
        Assert.Throws<ToolArgumentException>(() => ProposalRequestParser.Parse("propose_view_templates", args));
        Assert.DoesNotContain(ToolCatalog.All, t => t.Name.Contains("template", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Never apply, clear or suggest view templates", ToolCatalog.ServerInstructions);
        Assert.DoesNotContain("propose_view_templates", ToolCatalog.ServerInstructions);
    }

    [Fact]
    public void Unknown_tool_is_refused()
    {
        Assert.Throws<ToolArgumentException>(() => ProposalRequestParser.Parse("list_views", Obj("""{ "title": "t" }""")));
    }

    [Fact]
    public void Strings_are_trimmed()
    {
        var args = Obj("""{ "title": " t ", "sheets": [ { "number": " A1 ", "name": "n", "titleBlock": " tb " } ] }""");
        var sheet = (CreateSheetChange)ProposalRequestParser.Parse(ToolNames.ProposeSheets, args).Changes[0];
        Assert.Equal(("A1", "tb"), (sheet.Number, sheet.TitleBlock));
    }
}

public class ProposalValidatorTests
{
    private static ProposalRequest Request(params ProposalChange[] changes) => new("Batch", "why", changes);

    [Fact]
    public void Valid_request_has_no_errors()
    {
        var errors = ProposalValidator.Validate(Request(
            new CreateSheetChange("A1.0", "PLAN", "Allegro 24x36: Standard"),
            new PlaceViewChange("A1.0", "Level 1")), Profile());
        Assert.Empty(errors);
    }

    [Fact]
    public void Missing_profile_blocks_everything()
    {
        var errors = ProposalValidator.Validate(Request(new CreateSheetChange("A1", "n", "x")), null);
        Assert.Contains("Export Standards", Assert.Single(errors));
    }

    [Fact]
    public void Invented_title_block_is_rejected_with_suggestion()
    {
        var errors = ProposalValidator.Validate(Request(new CreateSheetChange("A1", "n", "Allegro 24x36 Standard")), Profile());
        var error = Assert.Single(errors);
        Assert.StartsWith("Change 1: ", error);
        Assert.Contains("Did you mean 'Allegro 24x36: Standard'?", error);
    }

    [Fact]
    public void Title_block_match_is_exact_case()
    {
        var errors = ProposalValidator.Validate(Request(new CreateSheetChange("A1", "n", "allegro 24x36: standard")), Profile());
        Assert.Single(errors);
    }

    [Fact]
    public void Any_view_template_is_refused_even_an_office_one()
    {
        // Even an exact office template name: the designer sets templates, never the drafter.
        var errors = ProposalValidator.Validate(Request(new ApplyViewTemplateChange("Level 1", "A - Floor Plan 1/4\"")), Profile());
        Assert.Equal("Change 1: " + ProposalValidator.NoTemplates, Assert.Single(errors));
        var callout = ProposalValidator.Validate(Request(new CalloutChange("Level 1", new CalloutArea(RoomNumber: "104"), Name: "ENLARGED", ViewFamilyType: "Detail View", TemplateName: "A - Floor Plan 1/4\"")), Profile());
        Assert.Contains(callout, e => e.Contains(ProposalValidator.NoTemplates));
    }

    [Fact]
    public void Duplicate_sheet_numbers_are_rejected_ignoring_case()
    {
        var errors = ProposalValidator.Validate(Request(
            new CreateSheetChange("A1.0", "n", "Allegro 24x36: Standard"),
            new CreateSheetChange("a1.0", "n2", "Allegro 24x36: Standard")), Profile());
        Assert.Contains("more than once", Assert.Single(errors));
    }

    [Fact]
    public void Same_view_on_same_sheet_twice_is_rejected()
    {
        var errors = ProposalValidator.Validate(Request(
            new PlaceViewChange("A1", "Level 1"),
            new PlaceViewChange("A1", "Level 1")), Profile());
        Assert.Single(errors);
    }

    [Fact]
    public void Oversized_batch_is_rejected()
    {
        var changes = Enumerable.Range(1, ProposalValidator.MaxChangesPerProposal + 1)
            .Select(i => (ProposalChange)new CreateSheetChange($"A{i}", "n", "Allegro 24x36: Standard"))
            .ToArray();
        var errors = ProposalValidator.Validate(Request(changes), Profile());
        Assert.Contains("limit is 50", Assert.Single(errors));
    }

    [Fact]
    public void Exactly_max_changes_is_allowed()
    {
        var changes = Enumerable.Range(1, ProposalValidator.MaxChangesPerProposal)
            .Select(i => (ProposalChange)new CreateSheetChange($"A{i}", "n", "Allegro 24x36: Standard"))
            .ToArray();
        Assert.Empty(ProposalValidator.Validate(Request(changes), Profile()));
    }

    [Fact]
    public void Empty_proposal_is_rejected()
    {
        Assert.NotEmpty(ProposalValidator.Validate(Request(), Profile()));
    }
}
