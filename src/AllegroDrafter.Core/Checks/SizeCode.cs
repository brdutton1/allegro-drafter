using System.Text.RegularExpressions;

namespace AllegroDrafter.Core.Checks;

/// <summary>
/// Nominal size codes as framers read them: width then height, feet then inches.
/// "3050" = 3'-0" x 5'-0". "2868" = 2'-8" x 6'-8". "12080" = 12'-0" x 8'-0".
/// Five and six digit codes are ambiguous ("21068" could be 2'-10" or 21'-0"),
/// so <see cref="Readings"/> returns every legal reading and the caller picks
/// the one that matches the geometry.
/// </summary>
public sealed record SizeCode(string Digits, string? Operation)
{
    private static readonly Regex Pattern = new(@"^\s*(\d{4,6})(?:\s+([A-Za-z][A-Za-z0-9 ]*?))?\s*$", RegexOptions.CultureInvariant);

    /// <summary>Null when the text isn't a size code at all (e.g. Revit's auto number "466", or "12' x 10'").</summary>
    public static SizeCode? Parse(string? typeMark)
    {
        if (string.IsNullOrWhiteSpace(typeMark)) return null;
        var m = Pattern.Match(typeMark);
        if (!m.Success) return null;
        var op = m.Groups[2].Success ? Regex.Replace(m.Groups[2].Value.Trim().ToUpperInvariant(), @"\s+", " ") : null;
        var code = new SizeCode(m.Groups[1].Value, string.IsNullOrEmpty(op) ? null : op);
        return code.Readings().Count == 0 ? null : code;
    }

    /// <summary>Every legal (width, height) reading in feet. Four-digit codes have exactly one.</summary>
    public IReadOnlyList<(double WidthFeet, double HeightFeet)> Readings()
    {
        var results = new List<(double, double)>();
        for (var split = 2; split <= Digits.Length - 2; split++)
        {
            foreach (var w in Dimension(Digits[..split]))
            foreach (var h in Dimension(Digits[split..]))
            {
                if (!results.Contains((w, h))) results.Add((w, h));
            }
        }
        return results;
    }

    /// <summary>The reading closest to the given geometry, and how far off it is (inches, worst of the two sides).</summary>
    public (double WidthFeet, double HeightFeet, double OffInches) Closest(double widthFeet, double heightFeet)
    {
        var best = (W: 0.0, H: 0.0, Off: double.MaxValue);
        foreach (var (w, h) in Readings())
        {
            var off = Math.Max(Math.Abs(w - widthFeet), Math.Abs(h - heightFeet)) * 12;
            if (off < best.Off - 1e-9) best = (w, h, off);
        }
        return best;
    }

    public (double WidthFeet, double HeightFeet) Primary => Readings()[0];

    public static string FeetInches(double feet)
    {
        var totalInches = Math.Round(feet * 12 * 8) / 8; // nearest 1/8"
        var ft = (int)Math.Floor(totalInches / 12);
        var inches = totalInches - ft * 12;
        var whole = (int)Math.Floor(inches);
        var eighths = (int)Math.Round((inches - whole) * 8);
        var fraction = eighths switch
        {
            0 => "",
            _ => " " + Reduce(eighths),
        };
        return $"{ft}'-{whole}{fraction}\"";
    }

    private static string Reduce(int eighths)
    {
        var num = eighths;
        var den = 8;
        while (num % 2 == 0 && den > 1) { num /= 2; den /= 2; }
        return $"{num}/{den}";
    }

    /// <summary>One side: "30" = 3'-0", "120" = 12'-0" or 1'-20" (illegal), "210" = 2'-10" or 21'-0".</summary>
    private static IEnumerable<double> Dimension(string part)
    {
        // Feet digits then inch digits. Inches must be 0-11. Feet can't be 0 or start with 0.
        if (part[0] == '0') yield break;
        for (var feetDigits = 1; feetDigits < part.Length; feetDigits++)
        {
            var inchDigits = part.Length - feetDigits;
            if (inchDigits > 2 || feetDigits > 2) continue;
            var feet = int.Parse(part[..feetDigits]);
            var inches = int.Parse(part[feetDigits..]);
            if (feet == 0 || inches > 11) continue;
            if (inchDigits == 2 && inches < 10) continue; // "2068" is 2'-0", never "2'-06"
            yield return feet + inches / 12.0;
        }
    }
}
