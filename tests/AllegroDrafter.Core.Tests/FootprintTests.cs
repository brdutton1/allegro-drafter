using AllegroDrafter.Core.Drafting;
using AllegroDrafter.Core.Mcp;
using AllegroDrafter.Core.Proposals;
using static AllegroDrafter.Core.Tests.Fixtures;

namespace AllegroDrafter.Core.Tests;

/// <summary>Outside comes from the plan's shape, not from how each wall was drawn (25-0083, 2026-11-26).</summary>
public class FootprintTests
{
    // L: main block 40 x 10 along the south, wing 10 wide up the west side to y = 40. Centerlines only.
    private static List<PlanWall> L() => new()
    {
        new("S", 0, 0, 40, 0, 0),
        new("E", 40, 0, 40, 10, 0),
        new("N1", 40, 10, 10, 10, 0),
        new("E2", 10, 10, 10, 40, 0),
        new("N2", 10, 40, 0, 40, 0),
        new("W", 0, 40, 0, 0, 0),
    };

    [Theory]
    [InlineData("S", Side.South)]
    [InlineData("E", Side.East)]
    [InlineData("N1", Side.North)]  // below the midline, still faces north
    [InlineData("E2", Side.East)]
    [InlineData("N2", Side.North)]
    [InlineData("W", Side.West)]
    public void Every_wall_faces_out_by_the_plan_shape(string key, Side expected)
    {
        var walls = L();
        Assert.Equal(expected, new Footprint(walls).FacingOf(walls.Single(w => w.Key == key)));
    }

    [Fact]
    public void Drawing_direction_doesnt_matter()
    {
        // The same L with every wall drawn the other way round (as a flipped wall would be).
        var reversed = L().Select(w => w with { X1 = w.X2, Y1 = w.Y2, X2 = w.X1, Y2 = w.Y1 }).ToList();
        var fp = new Footprint(reversed);
        Assert.Equal(Side.North, fp.FacingOf(reversed.Single(w => w.Key == "N1")));
        Assert.Equal(Side.West, fp.FacingOf(reversed.Single(w => w.Key == "W")));
    }

    [Fact]
    public void Inside_and_outside_points()
    {
        var fp = new Footprint(L());
        Assert.True(fp.IsInside(5, 30));    // in the wing
        Assert.True(fp.IsInside(30, 5));    // in the main block
        Assert.False(fp.IsInside(30, 30));  // the notch, outside
        Assert.False(fp.IsInside(-5, 5));
    }

    [Fact]
    public void A_string_in_the_notch_is_on_the_north_side_of_the_low_wall()
    {
        var fp = new Footprint(L());
        // An east-west string at y = 11.5, x = 30: above the north-facing wall at y = 10.
        Assert.Equal(Side.North, fp.SideOf(30, 11.5, checkNorthSouth: true));
        Assert.Equal(1.5, fp.DistanceTo(30, 11.5, 0, -1)!.Value, 6);
        // A north-south string at x = 11.5 in the notch faces east, off the wing's east wall.
        Assert.Equal(Side.East, fp.SideOf(11.5, 30, checkNorthSouth: false));
        Assert.Null(fp.SideOf(5, 30, checkNorthSouth: true)); // inside: not an exterior string
    }

    [Fact]
    public void A_wall_off_the_loop_has_no_facing()
    {
        var walls = L();
        walls.Add(new PlanWall("deck", 50, 0, 50, 10, 0)); // a lone exterior wall out on the site
        Assert.Null(new Footprint(walls).FacingOf(walls[^1]));
    }

    // -------------------------------------------------------------- spacing

    [Fact]
    public void Default_spacing_is_three_eighths_on_paper()
    {
        var d = Assert.IsType<DimensionViewChange>(Assert.Single(ProposalRequestParser.Parse(ToolNames.ProposeDimensions,
            Obj("""{ "title": "t", "views": [ { "viewName": "MAIN FLOOR PLAN" } ] }""")).Changes));
        Assert.Equal(0.375, d.OffsetPaperInches);
        Assert.Equal(0.375, d.StepPaperInches);
        Assert.Equal(1.5, d.OffsetAt(48), 9);   // 3/8" at 1/4" = 1'-0" is 1'-6" of model
        Assert.Equal(0.75, d.StepAt(24), 9);    // 3/8" at 1/2" = 1'-0" is 9"
        Assert.Contains("3/8\" off the wall and 3/8\" apart on paper", d.Describe());
    }

    [Fact]
    public void Model_feet_still_work_when_asked_for()
    {
        var d = Assert.IsType<DimensionViewChange>(Assert.Single(ProposalRequestParser.Parse(ToolNames.ProposeDimensions,
            Obj("""{ "title": "t", "views": [ { "viewName": "L1", "offsetFeet": 3, "stepPaperInches": 0.5 } ] }""")).Changes));
        Assert.Null(d.OffsetPaperInches);
        Assert.Equal(3, d.OffsetAt(48));
        Assert.Equal(2, d.StepAt(48), 9);
        Assert.Throws<ToolArgumentException>(() => ProposalRequestParser.Parse(ToolNames.ProposeDimensions,
            Obj("""{ "title": "t", "views": [ { "viewName": "L1", "offsetFeet": 3, "offsetPaperInches": 0.375 } ] }""")));
    }

    [Fact]
    public void Proposals_saved_before_paper_spacing_keep_their_feet()
    {
        var old = new DimensionViewChange("L1", new[] { "overall" });
        Assert.Equal(4, old.OffsetAt(48));
        Assert.Equal(2, old.StepAt(48));
    }
}
