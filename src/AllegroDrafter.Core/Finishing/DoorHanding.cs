using AllegroDrafter.Core.Checks;

namespace AllegroDrafter.Core.Finishing;

/// <summary>A door's plan geometry, read from Revit. Vectors are plan X/Y.</summary>
/// <param name="FacingX">Door FacingOrientation.</param>
/// <param name="HandX">Door HandOrientation.</param>
/// <param name="ExteriorX">For a door in an exterior wall: the wall's outward normal. Null for interior doors.</param>
public sealed record DoorGeometry(
    long ElementId,
    string Label,
    string Family,
    string? TypeMark,
    double FacingX, double FacingY,
    double HandX, double HandY,
    double? ExteriorX = null, double? ExteriorY = null);

/// <summary>The swing value for a door, or why the rule can't decide.</summary>
public sealed record SwingResult(long ElementId, string Label, string? Value, string? Question);

/// <summary>
/// Office door swing values: LH / RH / LHR / RHR (industry handing), POCKET, SLIDING.
/// Handing is read standing on the side the door swings away from; exterior doors are
/// always read from outside, so an outswing exterior door is a reverse (LHR / RHR).
/// Revit conventions differ by family, so two settings say how to read the vectors,
/// and families that read backwards are listed once in manual.audit.handingFlipFamilies.
/// </summary>
public static class DoorHanding
{
    public static SwingResult Derive(DoorGeometry d, AuditSettings s)
    {
        var op = SizeCode.Parse(d.TypeMark)?.Operation;
        if (op is not null)
        {
            foreach (var (code, fixedValue) in s.SwingValueByOperation)
                if (string.Equals(code, op, StringComparison.OrdinalIgnoreCase)) return new(d.ElementId, d.Label, fixedValue, null);
        }
        if (op is not null && !s.SwingOperations.Contains(op, StringComparer.OrdinalIgnoreCase))
            return new(d.ElementId, d.Label, null, $"{d.Label} is a {op} door. What goes in {s.SwingParameter}?");

        var facing = Unit(d.FacingX, d.FacingY);
        var hand = Unit(d.HandX, d.HandY);
        if (facing is null || hand is null)
            return new(d.ElementId, d.Label, null, $"{d.Label}: Revit didn't give a usable facing or hand direction. Which way does it swing (LH, RH, LHR, RHR)?");

        // Direction the leaf swings toward, and the direction from the door's center to its hinges.
        var swing = s.DoorSwingsTowardFacing ? facing.Value : Neg(facing.Value);
        var hinge = s.DoorHandPointsToHinge ? hand.Value : Neg(hand.Value);

        // Standing on the push side, looking the way the door swings.
        var viewer = swing;
        var reverse = false;
        if (d.ExteriorX is double ex && d.ExteriorY is double ey && Unit(ex, ey) is { } outward)
        {
            // Exterior doors are read from outside, looking in.
            viewer = Neg(outward);
            reverse = Dot(swing, outward) > 0; // swings out, toward the viewer
        }

        var right = (viewer.Y, -viewer.X); // viewer's right hand, plan view
        var side = Dot(hinge, right);
        if (Math.Abs(side) < 0.3)
            return new(d.ElementId, d.Label, null, $"{d.Label}: couldn't tell which side the hinges are on. Which way does it swing (LH, RH, LHR, RHR)?");

        var left = side < 0;
        if (s.HandingFlipFamilies.Any(f => string.Equals(f.Trim(), d.Family.Trim(), StringComparison.OrdinalIgnoreCase))) left = !left;
        var value = (left ? "LH" : "RH") + (reverse ? "R" : "");
        return new(d.ElementId, d.Label, value, null);
    }

    private static (double X, double Y)? Unit(double x, double y)
    {
        var len = Math.Sqrt(x * x + y * y);
        return len < 1e-9 ? null : (x / len, y / len);
    }

    private static (double X, double Y) Neg((double X, double Y) v) => (-v.X, -v.Y);
    private static double Dot((double X, double Y) a, (double X, double Y) b) => a.X * b.X + a.Y * b.Y;
}
