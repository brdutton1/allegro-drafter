using AllegroDrafter.Core.Drafting;

namespace AllegroDrafter.Core.Tests;

/// <summary>Phase 1: exterior strings by facing (L/U plans), interior strings, closure.</summary>
public class DraftingDimensionTests
{
    // ------------------------------------------------------- fixtures (feet)

    // L: main block 40 x 10 along the south, wing 10 wide up the west side to y = 40.
    // The north-facing wall at y = 10 sits BELOW the building's midline (y = 20): the case the
    // midline split got wrong (its windows landed on the south string).
    private static List<PlanWall> LowNotchL(bool facing = true) => new()
    {
        new("S", 0, 0, 40, 0, -0.5, facing ? Side.South : null),
        new("E", 40, 0, 40, 10, 40.5, facing ? Side.East : null),
        new("N1", 40, 10, 10, 10, 10.5, facing ? Side.North : null),
        new("E2", 10, 10, 10, 40, 10.5, facing ? Side.East : null),
        new("N2", 10, 40, 0, 40, 40.5, facing ? Side.North : null),
        new("W", 0, 40, 0, 0, -0.5, facing ? Side.West : null),
    };

    // U open to the north: 40 wide, 30 deep, courtyard 20 wide x 20 deep in the middle.
    private static List<PlanWall> U() => new()
    {
        new("S", 0, 0, 40, 0, -0.5, Side.South),
        new("E", 40, 0, 40, 30, 40.5, Side.East),
        new("NE", 40, 30, 30, 30, 30.5, Side.North),
        new("CE", 30, 30, 30, 10, 29.5, Side.West),   // courtyard east wall faces west into the court
        new("CN", 30, 10, 10, 10, 10.5, Side.North),  // courtyard back wall faces north
        new("CW", 10, 10, 10, 30, 10.5, Side.East),   // courtyard west wall faces east
        new("NW", 10, 30, 0, 30, 30.5, Side.North),
        new("W", 0, 30, 0, 0, -0.5, Side.West),
    };

    private static List<PlanWall> Box() => new()
    {
        new("S", 0, 0, 40, 0, -0.5, Side.South),
        new("E", 40, 0, 40, 30, 40.5, Side.East),
        new("N", 40, 30, 0, 30, 30.5, Side.North),
        new("W", 0, 30, 0, 0, -0.5, Side.West),
    };

    private static DimensionString Get(DimensionPlan plan, Side side, string strategy) =>
        plan.Strings.Single(s => s.Side == side && s.Strategy == strategy);

    // --------------------------------------------------- exterior by facing

    [Fact]
    public void Windows_in_a_north_wall_below_the_midline_go_on_the_north_string()
    {
        var windows = new[] { new PlanOpening("win-n1", "N1", 25), new PlanOpening("win-s", "S", 20) };
        var plan = DimensionPlanner.Plan(LowNotchL(), windows, DimensionPlanner.Strategies.ToList());

        Assert.Contains(Get(plan, Side.North, DimensionPlanner.Openings).Refs, r => r.Key == "win-n1");
        var south = Get(plan, Side.South, DimensionPlanner.Openings);
        Assert.Contains(south.Refs, r => r.Key == "win-s");
        Assert.DoesNotContain(south.Refs, r => r.Key == "win-n1");

        // The midline split (no facing) puts it on the south string: the bug this fixes.
        var old = DimensionPlanner.Plan(LowNotchL(facing: false), windows, DimensionPlanner.Strategies.ToList());
        Assert.Contains(Get(old, Side.South, DimensionPlanner.Openings).Refs, r => r.Key == "win-n1");
    }

    [Fact]
    public void Jogs_come_from_walls_that_join_the_facade()
    {
        var plan = DimensionPlanner.Plan(LowNotchL(), Array.Empty<PlanOpening>(), new[] { DimensionPlanner.Walls, DimensionPlanner.Overall });
        // North facade: N2 (wing top) and N1 (low notch). Jogs at W (-0.5), E2 (10.5), E (40.5).
        Assert.Equal(new[] { -0.5, 10.5, 40.5 }, Get(plan, Side.North, DimensionPlanner.Walls).Refs.Select(r => r.Position));
        // South facade is one straight wall: the jog string would equal the overall, so it's left out.
        Assert.DoesNotContain(plan.Strings, s => s.Side == Side.South && s.Strategy == DimensionPlanner.Walls);
    }

    [Fact]
    public void U_plan_locates_the_courtyard_from_the_north()
    {
        var court = new[] { new PlanOpening("door-cn", "CN", 20) };
        var plan = DimensionPlanner.Plan(U(), court, DimensionPlanner.Strategies.ToList());
        var jogs = Get(plan, Side.North, DimensionPlanner.Walls).Refs.Select(r => r.Position).ToList();
        Assert.Equal(new[] { -0.5, 10.5, 29.5, 40.5 }, jogs);
        Assert.Contains(Get(plan, Side.North, DimensionPlanner.Openings).Refs, r => r.Key == "door-cn");
        Assert.DoesNotContain(plan.Strings, s => s.Side == Side.South && s.Refs.Any(r => r.Key == "door-cn"));
        Assert.Empty(DimensionClosure.Problems(plan.Strings));
    }

    [Fact]
    public void Mixed_facing_data_falls_back_to_the_midline_and_says_so()
    {
        var walls = LowNotchL();
        walls[0] = walls[0] with { Facing = null };
        var plan = DimensionPlanner.Plan(walls, Array.Empty<PlanOpening>(), new[] { DimensionPlanner.Overall });
        Assert.Contains(plan.Notes, n => n.Contains("midline"));
    }

    // ------------------------------------------------------------ interior

    [Fact]
    public void One_partition_gets_one_string_closed_to_the_outside()
    {
        var walls = new[] { new PlanInteriorWall("p1", 15, 0, 15, 30, 0.375) };
        var plan = InteriorDimensionPlanner.Plan(walls, Box());

        var s = Assert.Single(plan.Strings);
        Assert.True(s.IsHorizontal);
        Assert.Equal(InteriorDimensionPlanner.Strategy, s.Strategy);
        Assert.Equal(new[] { "ext:W", "p1", "ext:E" }, s.Refs.Select(r => r.Key));
        Assert.Equal(new[] { -0.5, 15, 40.5 }, s.Refs.Select(r => r.Position));
        Assert.InRange(s.LineCoord, 1.0, 29.0); // inside the wall's length, off its ends
        Assert.Empty(DimensionClosure.Problems(plan.Strings));
    }

    [Fact]
    public void Face_basis_measures_to_one_consistent_face()
    {
        var walls = new[] { new PlanInteriorWall("p1", 15, 0, 15, 30, 0.375) };
        var s = Assert.Single(InteriorDimensionPlanner.Plan(walls, Box(), InteriorBasis.Face).Strings);
        Assert.Equal(15 - 0.1875, s.Refs.Single(r => r.Key == "p1").Position, 6);
    }

    [Fact]
    public void Hallway_with_closets_locates_every_wall_with_few_strings()
    {
        // Hall along y 12..16 across the house. Bedrooms south, closets north between partitions.
        var walls = new List<PlanInteriorWall>
        {
            new("hallS", 0, 12, 40, 12, 0.375),
            new("hallN", 0, 16, 40, 16, 0.375),
            new("bed1", 14, 0, 14, 12, 0.375),     // south partitions (y 0..12)
            new("bed2", 27, 0, 27, 12, 0.375),
            new("clo1", 8, 16, 8, 30, 0.375),      // north partitions (y 16..30)
            new("clo2", 12, 16, 12, 22, 0.375),    // short closet wall
            new("clo3", 30, 16, 30, 30, 0.375),
        };
        var plan = InteriorDimensionPlanner.Plan(walls, Box());

        // Every north-south wall is on an east-west string; every east-west wall on a north-south string.
        foreach (var w in walls)
        {
            var located = plan.Strings.Any(s => s.IsHorizontal == w.IsNorthSouth && s.Refs.Any(r => r.Key == w.Key));
            Assert.True(located, $"{w.Key} isn't located");
        }
        // Fewest strings: south partitions share one, north partitions share one (the short closet wall decides where).
        Assert.Equal(2, plan.Strings.Count(s => s.IsHorizontal));
        var north = plan.Strings.Single(s => s.IsHorizontal && s.Refs.Any(r => r.Key == "clo2"));
        Assert.InRange(north.LineCoord, 16 + 1.5, 22); // inside the short wall, clear of the hall wall
        Assert.Equal(new[] { "ext:W", "clo1", "clo2", "clo3", "ext:E" }, north.Refs.Select(r => r.Key));
        // Both hall walls on one north-south string, closed south and north.
        var vertical = Assert.Single(plan.Strings, s => !s.IsHorizontal);
        Assert.Equal(new[] { "ext:S", "hallS", "hallN", "ext:N" }, vertical.Refs.Select(r => r.Key));
        Assert.Empty(DimensionClosure.Problems(plan.Strings));
    }

    [Fact]
    public void Strings_never_run_along_a_wall()
    {
        // Partition 10..20 in Y; an east-west wall sits at y = 19.5, right where the string wants to go.
        var walls = new[]
        {
            new PlanInteriorWall("p", 20, 10, 20, 20, 0.375),
            new PlanInteriorWall("ew", 20, 19.5, 40, 19.5, 0.375),
        };
        var s = InteriorDimensionPlanner.Plan(walls, Box(), clearanceFeet: 1.5).Strings.Single(x => x.IsHorizontal);
        Assert.True(Math.Abs(s.LineCoord - 19.5) >= 1.5 - 1e-9, $"string at {s.LineCoord} runs along the wall at 19.5");
        Assert.InRange(s.LineCoord, 10, 20);
    }

    [Fact]
    public void Offset_bearing_wall_on_an_L_closes_to_the_right_exterior_faces()
    {
        // In the low-notch L, a partition at x = 5 in the wing (y 15..35): its string must close
        // between W (-0.5) and E2 (10.5), the walls the line actually crosses, not the far east wall.
        var s = Assert.Single(InteriorDimensionPlanner.Plan(new[] { new PlanInteriorWall("p", 5, 15, 5, 35, 0.5) }, LowNotchL()).Strings);
        Assert.Equal(new[] { ("ext:W", -0.5), ("p", 5.0), ("ext:E2", 10.5) }, s.Refs.Select(r => (r.Key, r.Position)));
    }

    [Fact]
    public void Angled_interior_walls_are_skipped_and_reported()
    {
        var plan = InteriorDimensionPlanner.Plan(new[] { new PlanInteriorWall("a", 0, 0, 10, 10, 0.375) }, Box());
        Assert.Empty(plan.Strings);
        Assert.Contains(plan.Notes, n => n.Contains("angled"));
    }

    [Fact]
    public void A_string_that_cant_close_says_so()
    {
        // No exterior walls given at all.
        var plan = InteriorDimensionPlanner.Plan(new[] { new PlanInteriorWall("p", 15, 0, 15, 30, 0.375), new PlanInteriorWall("q", 25, 0, 25, 30, 0.375) }, Array.Empty<PlanWall>());
        Assert.Contains(plan.Notes, n => n.Contains("doesn't close"));
        Assert.Contains(DimensionClosure.Problems(plan.Strings), p => p.Contains("doesn't end on exterior faces"));
    }

    // ------------------------------------------------- one entry point

    [Fact]
    public void Plan_dimensioning_draws_exterior_then_interior_and_checks_closure()
    {
        var interior = new[] { new PlanInteriorWall("p1", 15, 0, 15, 30, 0.375) };
        var plan = PlanDimensioning.Plan(Box(), Array.Empty<PlanOpening>(), interior, PlanDimensioning.AllStrategies.ToList());
        Assert.Equal(4, plan.Strings.Count(s => s.Strategy == DimensionPlanner.Overall));
        Assert.Single(plan.Strings, s => s.Strategy == PlanDimensioning.Interior);
        Assert.DoesNotContain(plan.Notes, n => n.StartsWith("Check:"));

        var interiorOnly = PlanDimensioning.Plan(Box(), Array.Empty<PlanOpening>(), interior, new[] { PlanDimensioning.Interior });
        Assert.All(interiorOnly.Strings, s => Assert.Equal(PlanDimensioning.Interior, s.Strategy));
        Assert.Throws<ArgumentException>(() => PlanDimensioning.Plan(Box(), Array.Empty<PlanOpening>(), interior, new[] { "radial" }));
        Assert.Equal(InteriorBasis.Center, PlanDimensioning.ParseBasis("centerline"));
        Assert.Equal(InteriorBasis.Face, PlanDimensioning.ParseBasis(null));
    }

    // ------------------------------------------------------------- closure

    [Fact]
    public void Exterior_tiers_close_to_the_overall()
    {
        var plan = DimensionPlanner.Plan(LowNotchL(), new[] { new PlanOpening("w", "N1", 25) }, DimensionPlanner.Strategies.ToList());
        Assert.Empty(DimensionClosure.Problems(plan.Strings));

        // A tier that stops short of the overall doesn't add up.
        var overall = Get(plan, Side.North, DimensionPlanner.Overall);
        var broken = new DimensionString(Side.North, DimensionPlanner.Openings, 50, new[] { overall.Refs[0], new DimensionRef("w", 25) });
        var problems = DimensionClosure.Problems(new[] { overall, broken });
        Assert.Contains("North openings string doesn't start and end on the overall (41'-0\")", Assert.Single(problems));
    }

    [Theory]
    [InlineData(40.5, "40'-6\"")]
    [InlineData(10.0 + 1.0 / 24, "10'-0 1/2\"")]
    [InlineData(0.1875, "0'-2 1/4\"")]
    [InlineData(3.0 + 3.0 / 192, "3'-0 3/16\"")]
    public void Feet_and_inches_read_like_a_dimension(double feet, string expected) =>
        Assert.Equal(expected, DimensionClosure.Feet(feet));
}
