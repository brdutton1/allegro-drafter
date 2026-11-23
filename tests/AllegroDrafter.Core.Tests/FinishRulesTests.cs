using AllegroDrafter.Core.Checks;
using AllegroDrafter.Core.Finishing;

namespace AllegroDrafter.Core.Tests;

public class RoomSizeSplitTests
{
    [Theory]
    [InlineData("14'-1\" x 11'-4\"", "14'-1\" x 11'-4\"", null, null)]
    [InlineData("9'-9\"x7'-5\"", "9'-9\" x 7'-5\"", null, null)]
    [InlineData("13' x 8'-3\"", "13' x 8'-3\"", null, null)]
    [InlineData("12'-6 1/2\" X 8'", "12'-6 1/2\" x 8'", null, null)]
    [InlineData("12' FLAT CEILING", null, "12' FLAT CEILING", null)]
    [InlineData("101", null, null, "101")]
    [InlineData("B12", null, null, "B12")]
    [InlineData("", null, null, null)]
    [InlineData(null, null, null, null)]
    public void Sorts_the_number_field(string? number, string? size, string? note, string? real)
    {
        var r = RoomSizeSplit.Split(number);
        Assert.Equal(size, r.Size);
        Assert.Equal(note, r.Note);
        Assert.Equal(real, r.Number);
    }
}

public class RoomNumberingTests
{
    private static RoomFact Room(long id, string name, double x, double y, string level = "MAIN FLOOR", string number = "", string state = "placed") =>
        new(id, number, name, level, 100, state, false, x, y);

    [Fact]
    public void Starts_at_the_entry_and_goes_clockwise()
    {
        // Entry at the south, then clockwise in plan: west, north, east.
        var rooms = new[]
        {
            Room(1, "KITCHEN", 10, 0),     // east
            Room(2, "ENTRY", 0, -10),      // south
            Room(3, "GREAT ROOM", 0, 10),  // north
            Room(4, "BEDROOM 2", -10, 0),  // west
        };
        var r = Assert.Single(RoomNumbering.Number(rooms, new AuditSettings()));
        Assert.False(r.Blocked);
        Assert.Equal(new[] { "ENTRY", "BEDROOM 2", "GREAT ROOM", "KITCHEN" }, r.Assignments.Select(a => a.RoomName));
        Assert.Equal(new[] { "101", "102", "103", "104" }, r.Assignments.Select(a => a.NewNumber));
    }

    [Fact]
    public void Entry_wins_over_foyer_when_both_exist()
    {
        var rooms = new[] { Room(1, "FOYER", 5, -5), Room(2, "ENTRY", 0, -10), Room(3, "KITCHEN", 0, 10) };
        var r = Assert.Single(RoomNumbering.Number(rooms, new AuditSettings()));
        Assert.Equal("ENTRY", r.Assignments[0].RoomName);
    }

    [Fact]
    public void Basement_and_upper_levels_get_their_prefix()
    {
        var rooms = new[]
        {
            Room(1, "ENTRY", 0, -10, "MAIN FLOOR"), Room(2, "KITCHEN", 0, 10, "MAIN FLOOR"),
            Room(3, "FOYER", 0, -10, "BASEMENT FLOOR"), Room(4, "REC ROOM", 0, 10, "BASEMENT FLOOR"),
            Room(5, "ENTRY", 0, -10, "UPPER FLOOR"), Room(6, "BEDROOM 3", 0, 10, "UPPER FLOOR"),
        };
        var results = RoomNumbering.Number(rooms, new AuditSettings()).ToDictionary(r => r.Level);
        Assert.Equal(new[] { "001", "002" }, results["BASEMENT FLOOR"].Assignments.Select(a => a.NewNumber));
        Assert.Equal(new[] { "101", "102" }, results["MAIN FLOOR"].Assignments.Select(a => a.NewNumber));
        Assert.Equal(new[] { "201", "202" }, results["UPPER FLOOR"].Assignments.Select(a => a.NewNumber));
    }

    [Fact]
    public void Unknown_level_or_missing_entry_asks()
    {
        var unknownLevel = Assert.Single(RoomNumbering.Number(new[] { Room(1, "ENTRY", 0, 0, "MAIN FLOOR SUBFLOOR X") with { Level = "LEVEL 7" } }, new AuditSettings()));
        Assert.True(unknownLevel.Blocked);
        Assert.Contains("levelNumberPrefixes", unknownLevel.Question);

        var noEntry = Assert.Single(RoomNumbering.Number(new[] { Room(1, "KITCHEN", 0, 0), Room(2, "PANTRY", 1, 1) }, new AuditSettings()));
        Assert.True(noEntry.Blocked);
        Assert.Contains("Which room is the entry", noEntry.Question);

        var twoEntries = Assert.Single(RoomNumbering.Number(new[] { Room(1, "ENTRY", 0, 0), Room(2, "ENTRY", 5, 5) }, new AuditSettings()));
        Assert.Contains("2 rooms on this level are named \"ENTRY\"", twoEntries.Question);
    }

    [Fact]
    public void Designer_can_pick_the_entry_and_map_a_level()
    {
        var settings = new AuditSettings { LevelNumberPrefixes = new() { ["LEVEL 7"] = 3 } };
        var rooms = new[] { Room(1, "KITCHEN", 0, 0, "LEVEL 7"), Room(2, "MUDROOM", 0, -10, "LEVEL 7") };
        var r = Assert.Single(RoomNumbering.Number(rooms, settings, new Dictionary<string, string> { ["LEVEL 7"] = "MUDROOM" }));
        Assert.Equal(new[] { ("MUDROOM", "301"), ("KITCHEN", "302") }, r.Assignments.Select(a => (a.RoomName, a.NewNumber)));
    }

    [Fact]
    public void Unplaced_rooms_are_left_out_and_old_numbers_kept_for_the_pane()
    {
        var rooms = new[] { Room(1, "ENTRY", 0, -10, number: "13'-0\" x 8'-3\""), Room(2, "CARPET", 0, 0, state: "unplaced") };
        var r = Assert.Single(RoomNumbering.Number(rooms, new AuditSettings()));
        var a = Assert.Single(r.Assignments);
        Assert.Equal("13'-0\" x 8'-3\"", a.OldNumber);
    }

    [Theory]
    [InlineData("MAIN FLOOR SUBFLOOR", 1)]
    [InlineData("BASEMENT SLAB", 0)]
    [InlineData("UPPER SUBFLOOR", 2)]
    [InlineData("Level 1", null)]
    public void Reads_the_floor_from_the_level_name(string level, int? prefix) =>
        Assert.Equal(prefix, RoomNumbering.LevelPrefix(level, new AuditSettings()));
}

public class DoorHandingTests
{
    private static readonly AuditSettings S = new();

    // Door in an east-west wall. Leaf swings north (+Y) toward facing.
    private static DoorGeometry Door(double hx, double hy, string mark = "3080", double fx = 0, double fy = 1, double? ex = null, double? ey = null, string family = "Supa 300") =>
        new(1, "Door 1", family, mark, fx, fy, hx, hy, ex, ey);

    [Fact]
    public void Interior_left_and_right_hand()
    {
        // Viewer stands south, looking north (the way it swings). Hinges west = viewer's left.
        Assert.Equal("LH", DoorHanding.Derive(Door(hx: -1, hy: 0), S).Value);
        Assert.Equal("RH", DoorHanding.Derive(Door(hx: 1, hy: 0), S).Value);
    }

    [Fact]
    public void Exterior_doors_are_read_from_outside()
    {
        // Outside is south (-Y). Inswing (north): read from outside, hinges west = left: LH.
        Assert.Equal("LH", DoorHanding.Derive(Door(hx: -1, hy: 0, ex: 0, ey: -1), S).Value);
        // Outside is north (+Y), door swings north = out toward the viewer: reverse.
        // Viewer stands north looking south; their right is west, so west hinges = RHR.
        Assert.Equal("RHR", DoorHanding.Derive(Door(hx: -1, hy: 0, ex: 0, ey: 1), S).Value);
        Assert.Equal("LHR", DoorHanding.Derive(Door(hx: 1, hy: 0, ex: 0, ey: 1), S).Value);
    }

    [Fact]
    public void Families_that_read_backwards_flip()
    {
        var s = new AuditSettings { HandingFlipFamilies = new() { "Supa 300" } };
        Assert.Equal("RH", DoorHanding.Derive(Door(hx: -1, hy: 0), s).Value);
    }

    [Fact]
    public void Revit_conventions_are_settings()
    {
        var s = new AuditSettings { DoorHandPointsToHinge = false };
        Assert.Equal("RH", DoorHanding.Derive(Door(hx: -1, hy: 0), s).Value);
        var t = new AuditSettings { DoorSwingsTowardFacing = false };
        Assert.Equal("RH", DoorHanding.Derive(Door(hx: -1, hy: 0), t).Value);
    }

    [Theory]
    [InlineData("2680 PKT", "POCKET")]
    [InlineData("120100 SLDR", "SLIDING")]
    [InlineData("3680 BARN", "SLIDING")]
    public void Non_swing_doors_get_their_value(string mark, string expected) =>
        Assert.Equal(expected, DoorHanding.Derive(Door(hx: -1, hy: 0, mark: mark), S).Value);

    [Fact]
    public void Ext_and_shower_doors_are_still_handed()
    {
        Assert.Equal("LH", DoorHanding.Derive(Door(hx: -1, hy: 0, mark: "3080 EXT"), S).Value);
        Assert.Equal("LH", DoorHanding.Derive(Door(hx: -1, hy: 0, mark: "2680 SHWR"), S).Value);
    }

    [Fact]
    public void Unclear_doors_ask()
    {
        Assert.NotNull(DoorHanding.Derive(Door(hx: -1, hy: 0, mark: "6080 BIFOLD"), S).Question);
        Assert.NotNull(DoorHanding.Derive(Door(hx: 0, hy: 0), S).Question);
        // Hand vector along the swing direction: no side.
        Assert.NotNull(DoorHanding.Derive(Door(hx: 0, hy: 1), S).Question);
    }
}
