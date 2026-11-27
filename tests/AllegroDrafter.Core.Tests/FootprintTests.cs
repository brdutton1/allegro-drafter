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

    // -------------------------------------------------------------- columns

    // 64 x 30 house; a porch along the north with four 12x12 posts 8' out, like 25-0083's.
    private static List<PlanWall> House() => new()
    {
        new("S", 0, 0, 64, 0, -0.5, Side.South),
        new("E", 64, 0, 64, 30, 64.5, Side.East),
        new("N", 64, 30, 0, 30, 30.5, Side.North),
        new("W", 0, 30, 0, 0, -0.5, Side.West),
    };

    private static List<PlanColumn> PorchPosts() => new()
    {
        new("c1", 0.5, 38, 0.5), new("c2", 21.9, 38, 0.5), new("c3", 42.1, 38, 0.5), new("c4", 63.5, 38, 0.5),
    };

    [Fact]
    public void Porch_posts_get_their_own_string_nearest_the_posts()
    {
        var plan = DimensionPlanner.Plan(House(), Array.Empty<PlanOpening>(), DimensionPlanner.Strategies.ToList(), 1.5, 1.5, PorchPosts());
        var north = plan.Strings.Where(s => s.Side == Side.North).OrderBy(s => s.LineCoord).ToList();

        var posts = north.First();
        Assert.Equal(DimensionPlanner.Columns, posts.Strategy);
        Assert.Equal(new[] { "W", "c1:x", "c2:x", "c3:x", "c4:x", "E" }, posts.Refs.Select(r => r.Key));
        // Clear of the posts, not the house: 38 + 6" half post + 1'-6".
        Assert.Equal(38 + 0.5 + 1.5, posts.LineCoord, 9);
        // Then the overall, a tier further out. Closure holds: the post string ends on the outside faces.
        Assert.Equal(DimensionPlanner.Overall, north.Last().Strategy);
        Assert.Equal(posts.LineCoord + 1.5, north.Last().LineCoord, 9);
        Assert.Empty(DimensionClosure.Problems(plan.Strings));
    }

    [Fact]
    public void Sides_without_posts_and_columns_inside_get_no_column_string()
    {
        var posts = PorchPosts();
        posts.Add(new PlanColumn("inside", 30, 15, 0.5)); // an interior column
        var plan = DimensionPlanner.Plan(House(), Array.Empty<PlanOpening>(), DimensionPlanner.Strategies.ToList(), 1.5, 1.5, posts);
        Assert.Single(plan.Strings, s => s.Strategy == DimensionPlanner.Columns);
        Assert.DoesNotContain(plan.Strings, s => s.Refs.Any(r => r.Key.StartsWith("inside")));
        // South strings still sit off the house, not off the porch.
        Assert.Equal(-0.5 - 1.5, plan.Strings.Where(s => s.Side == Side.South).Max(s => s.LineCoord), 9);
    }

    [Fact]
    public void A_corner_post_is_located_on_both_sides_it_stands_beyond()
    {
        var plan = DimensionPlanner.Plan(House(), Array.Empty<PlanOpening>(), new[] { DimensionPlanner.Columns, DimensionPlanner.Overall }, 1.5, 1.5,
            new[] { new PlanColumn("corner", 70, 38, 0.5) });
        Assert.Contains(plan.Strings, s => s.Side == Side.North && s.Refs.Any(r => r.Key == "corner:x"));
        Assert.Contains(plan.Strings, s => s.Side == Side.East && s.Refs.Any(r => r.Key == "corner:y"));
    }

    // ------------------------------------------------------ deck/slab edges

    // The porch deck: a floor from x = -2 to 66, y = 30 to 40 (runs 2' past the house each way).
    private static List<PlanEdge> Deck() => new()
    {
        new("deckW", -2, true, 30, 40),   // west end face (locates X)
        new("deckE", 66, true, 30, 40),   // east end face
        new("deckN", 40, false, -2, 66),  // north edge face (locates Y)
        new("deckS", 30, false, -2, 66),  // where it meets the house
        new("slabW", -0.5, true, 0, 30),  // the house slab, flush with the walls: not beyond, ignored
    };

    [Fact]
    public void Deck_edges_close_the_north_strings_and_the_strings_clear_the_deck()
    {
        var plan = DimensionPlanner.Plan(House(), Array.Empty<PlanOpening>(), DimensionPlanner.Strategies.ToList(), 1.5, 1.5, PorchPosts(), Deck());
        var north = plan.Strings.Where(s => s.Side == Side.North).OrderBy(s => s.LineCoord).ToList();

        // Overall runs deck edge to deck edge (64' house + 2' each way), like the office's 25-0083 strings.
        var overall = north.Single(s => s.Strategy == DimensionPlanner.Overall);
        Assert.Equal(new[] { ("deckW", -2.0), ("deckE", 66.0) }, overall.Refs.Select(r => (r.Key, r.Position)));
        // Posts: deck edges, house corners and post centers on one string.
        var posts = north.First();
        Assert.Equal(DimensionPlanner.Columns, posts.Strategy);
        Assert.Equal(new[] { "deckW", "W", "c1:x", "c2:x", "c3:x", "c4:x", "E", "deckE" }, posts.Refs.Select(r => r.Key));
        // Clear of the deck's north edge (y = 40), not the house or the posts.
        Assert.Equal(40 + 1.5, posts.LineCoord, 9);
        // The jog string picks up the deck stepping out past the house.
        Assert.Contains(north, s => s.Strategy == DimensionPlanner.Walls && s.Refs.Any(r => r.Key == "deckW"));
        Assert.Empty(DimensionClosure.Problems(plan.Strings));
    }

    [Fact]
    public void Sides_the_deck_doesnt_reach_ignore_it()
    {
        var plan = DimensionPlanner.Plan(House(), Array.Empty<PlanOpening>(), DimensionPlanner.Strategies.ToList(), 1.5, 1.5, null, Deck());
        var south = plan.Strings.Single(s => s.Side == Side.South && s.Strategy == DimensionPlanner.Overall);
        Assert.Equal(new[] { "W", "E" }, south.Refs.Select(r => r.Key));
        Assert.Equal(-0.5 - 1.5, south.LineCoord, 9);
        // East side: the deck runs 2' past the house face there, so the east strings reach its north edge and clear it.
        var east = plan.Strings.Single(s => s.Side == Side.East && s.Strategy == DimensionPlanner.Overall);
        Assert.Equal("deckN", east.Refs.Last().Key);
        // First east string clears the deck's east end (x = 66); the overall is a tier out past the jog string.
        Assert.Equal(66 + 1.5, plan.Strings.Where(s => s.Side == Side.East).Min(s => s.LineCoord), 9);
        Assert.Equal(66 + 3.0, east.LineCoord, 9);
        Assert.DoesNotContain(plan.Strings, s => s.Refs.Any(r => r.Key == "slabW"));
    }

    // ------------------------------------------------------ closure audit

    private static Checks.CheckResult Closure(params Checks.PlanStringFact[] strings)
    {
        var facts = new Checks.SetFacts();
        facts.PlanStrings.AddRange(strings);
        return Checks.SetAuditor.Run(facts, null, null, new[] { "dimension-closure" }).Checks.Single();
    }

    [Fact]
    public void Tiers_that_close_to_the_overall_pass()
    {
        var r = Closure(
            new("MAIN FLOOR PLAN", "east", 15.2, 112.2, 1, 1),   // overall
            new("MAIN FLOOR PLAN", "east", 15.2, 112.2, 6, 2),   // walls
            new("MAIN FLOOR PLAN", "east", 15.2, 112.2, 14, 3),  // openings
            new("MAIN FLOOR PLAN", "east", 40, 52, 2, 4));       // a short local string: left alone
        Assert.Equal(Checks.CheckStatus.Pass, r.Status);
    }

    [Fact]
    public void A_tier_that_stops_short_is_warned_with_how_far()
    {
        var r = Closure(
            new("MAIN FLOOR PLAN", "north", -7.36, 66.14, 1, 1),
            new("MAIN FLOOR PLAN", "north", -7.36, 63.64, 5, 5182721));
        Assert.Equal(Checks.CheckStatus.Warn, r.Status);
        var f = Assert.Single(r.Findings);
        Assert.Equal(5182721, f.ElementId);
        Assert.Contains("north side: a 5-segment string ends 2'-6\" short of the overall", f.Message);
    }

    [Fact]
    public void No_strings_skips()
    {
        Assert.Equal(Checks.CheckStatus.Skip, Closure().Status);
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
