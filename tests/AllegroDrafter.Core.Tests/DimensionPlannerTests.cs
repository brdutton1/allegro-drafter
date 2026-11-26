using AllegroDrafter.Core.Drafting;

namespace AllegroDrafter.Core.Tests;

public class DimensionPlannerTests
{
    // 40' x 30' box, 1'-0" walls, centerlines on the grid, exterior faces 6" outside. Strings are spaced off the outside faces.
    private static List<PlanWall> Box() => new()
    {
        new("S", 0, 0, 40, 0, -0.5),
        new("E", 40, 0, 40, 30, 40.5),
        new("N", 40, 30, 0, 30, 30.5),
        new("W", 0, 30, 0, 0, -0.5),
    };

    // L-shape: north-east corner notched out (jog at x=25, y=20).
    private static List<PlanWall> L() => new()
    {
        new("S", 0, 0, 40, 0, -0.5),
        new("E1", 40, 0, 40, 20, 40.5),
        new("N1", 40, 20, 25, 20, 20.5),
        new("E2", 25, 20, 25, 30, 25.5),
        new("N2", 25, 30, 0, 30, 30.5),
        new("W", 0, 30, 0, 0, -0.5),
    };

    private static DimensionString Get(DimensionPlan plan, Side side, string strategy) =>
        plan.Strings.Single(s => s.Side == side && s.Strategy == strategy);

    [Fact]
    public void Overall_gives_one_string_per_side_between_outer_faces()
    {
        var plan = DimensionPlanner.Plan(Box(), Array.Empty<PlanOpening>(), new[] { DimensionPlanner.Overall });
        Assert.Equal(4, plan.Strings.Count);
        var north = Get(plan, Side.North, DimensionPlanner.Overall);
        Assert.Equal(34.5, north.LineCoord);
        Assert.Equal(new[] { ("W", -0.5), ("E", 40.5) }, north.Refs.Select(r => (r.Key, r.Position)));
        Assert.True(north.IsHorizontal);
        Assert.Equal(-4.5, Get(plan, Side.South, DimensionPlanner.Overall).LineCoord);
        var east = Get(plan, Side.East, DimensionPlanner.Overall);
        Assert.Equal(44.5, east.LineCoord);
        Assert.Equal(new[] { -0.5, 30.5 }, east.Refs.Select(r => r.Position));
    }

    [Fact]
    public void Walls_tier_is_skipped_when_it_matches_overall()
    {
        var plan = DimensionPlanner.Plan(Box(), Array.Empty<PlanOpening>(), DimensionPlanner.Strategies.ToList());
        Assert.DoesNotContain(plan.Strings, s => s.Strategy == DimensionPlanner.Walls);
        // Overall moves in to the first tier because nothing else used it.
        Assert.Equal(34.5, Get(plan, Side.North, DimensionPlanner.Overall).LineCoord);
    }

    [Fact]
    public void Walls_tier_alone_is_kept_even_on_a_box()
    {
        var plan = DimensionPlanner.Plan(Box(), Array.Empty<PlanOpening>(), new[] { DimensionPlanner.Walls });
        Assert.Equal(4, plan.Strings.Count);
    }

    [Fact]
    public void Walls_tier_picks_up_jogs_on_the_notched_sides_only()
    {
        var plan = DimensionPlanner.Plan(L(), Array.Empty<PlanOpening>(), new[] { DimensionPlanner.Walls, DimensionPlanner.Overall });

        var northWalls = Get(plan, Side.North, DimensionPlanner.Walls);
        Assert.Equal(new[] { -0.5, 25.5, 40.5 }, northWalls.Refs.Select(r => r.Position));
        Assert.Equal(34.5, northWalls.LineCoord);
        Assert.Equal(36.5, Get(plan, Side.North, DimensionPlanner.Overall).LineCoord);

        var eastWalls = Get(plan, Side.East, DimensionPlanner.Walls);
        Assert.Equal(new[] { -0.5, 20.5, 30.5 }, eastWalls.Refs.Select(r => r.Position));

        // South and west facades are straight: only overall.
        Assert.DoesNotContain(plan.Strings, s => s.Side == Side.South && s.Strategy == DimensionPlanner.Walls);
        Assert.DoesNotContain(plan.Strings, s => s.Side == Side.West && s.Strategy == DimensionPlanner.Walls);
    }

    [Fact]
    public void Openings_tier_locates_centerlines_on_its_own_side_closest_to_building()
    {
        var openings = new[]
        {
            new PlanOpening("win-n", "N", 10),
            new PlanOpening("win-n2", "N", 30),
            new PlanOpening("door-s", "S", 20),
            new PlanOpening("win-e", "E", 15),
        };
        var plan = DimensionPlanner.Plan(Box(), openings, DimensionPlanner.Strategies.ToList());

        var north = Get(plan, Side.North, DimensionPlanner.Openings);
        Assert.Equal(new[] { -0.5, 10, 30, 40.5 }, north.Refs.Select(r => r.Position));
        Assert.Equal(34.5, north.LineCoord);
        Assert.Equal(36.5, Get(plan, Side.North, DimensionPlanner.Overall).LineCoord);

        Assert.Equal(new[] { -0.5, 20, 40.5 }, Get(plan, Side.South, DimensionPlanner.Openings).Refs.Select(r => r.Position));
        Assert.Equal(new[] { -0.5, 15, 30.5 }, Get(plan, Side.East, DimensionPlanner.Openings).Refs.Select(r => r.Position));
        // West wall has no openings: no openings string, overall takes the first tier.
        Assert.DoesNotContain(plan.Strings, s => s.Side == Side.West && s.Strategy == DimensionPlanner.Openings);
        Assert.Equal(-4.5, Get(plan, Side.West, DimensionPlanner.Overall).LineCoord);
    }

    [Fact]
    public void Coincident_references_are_merged()
    {
        var walls = Box();
        walls.Add(new PlanWall("W-dup", 0, 0, 0, 10, -0.5)); // second wall segment, same face
        var plan = DimensionPlanner.Plan(walls, Array.Empty<PlanOpening>(), new[] { DimensionPlanner.Walls });
        Assert.Equal(2, Get(plan, Side.North, DimensionPlanner.Walls).Refs.Count);
    }

    [Fact]
    public void Angled_walls_are_skipped_and_reported()
    {
        var walls = Box();
        walls.Add(new PlanWall("bay", 10, 0, 12, -2, -2.5));
        var plan = DimensionPlanner.Plan(walls, Array.Empty<PlanOpening>(), new[] { DimensionPlanner.Overall });
        Assert.Contains(plan.Notes, n => n.Contains("1 angled"));
        Assert.Equal(4, plan.Strings.Count);
    }

    [Fact]
    public void Nothing_usable_gives_notes_not_strings()
    {
        var plan = DimensionPlanner.Plan(Array.Empty<PlanWall>(), Array.Empty<PlanOpening>(), new[] { DimensionPlanner.Overall });
        Assert.Empty(plan.Strings);
        Assert.NotEmpty(plan.Notes);

        var oneWall = DimensionPlanner.Plan(new[] { new PlanWall("S", 0, 0, 40, 0, -0.5) }, Array.Empty<PlanOpening>(), new[] { DimensionPlanner.Overall });
        Assert.Empty(oneWall.Strings);
        Assert.Contains(oneWall.Notes, n => n.Contains("fewer than two"));
    }

    [Fact]
    public void Custom_offsets_are_used()
    {
        var plan = DimensionPlanner.Plan(L(), Array.Empty<PlanOpening>(), new[] { DimensionPlanner.Walls, DimensionPlanner.Overall }, offsetFeet: 6, stepFeet: 3);
        Assert.Equal(36.5, Get(plan, Side.North, DimensionPlanner.Walls).LineCoord);
        Assert.Equal(39.5, Get(plan, Side.North, DimensionPlanner.Overall).LineCoord);
    }

    [Fact]
    public void Bad_input_throws()
    {
        Assert.Throws<ArgumentException>(() => DimensionPlanner.Plan(Box(), Array.Empty<PlanOpening>(), new[] { "radial" }));
        Assert.Throws<ArgumentException>(() => DimensionPlanner.Plan(Box(), Array.Empty<PlanOpening>(), new[] { "overall" }, offsetFeet: 0));
    }
}
