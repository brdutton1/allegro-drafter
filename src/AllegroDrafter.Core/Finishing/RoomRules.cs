using System.Text.RegularExpressions;
using AllegroDrafter.Core.Checks;

namespace AllegroDrafter.Core.Finishing;

/// <summary>What was in a room's Number field, sorted into where it belongs.</summary>
/// <param name="Size">"14'-1\" x 11'-4\"": goes to the Room Size parameter, which the room tag shows.</param>
/// <param name="Note">Anything else that isn't a number ("12' FLAT CEILING"): goes to Comments.</param>
/// <param name="Number">A real room number already there ("101"), kept as is.</param>
public sealed record RoomNumberSplit(string? Size, string? Note, string? Number);

public static class RoomSizeSplit
{
    // 14'-1" x 11'-4", 13' x 8'-3", 9'-9"x7'-5", 12'-6 1/2" x 8'
    private const string Length = @"\d+'(?:\s*-?\s*\d+(?:\s+\d+/\d+)?"")?";
    private static readonly Regex SizePattern = new($@"^\s*({Length})\s*[xX]\s*({Length})\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex NumberPattern = new(@"^\s*[A-Z]?\d{1,4}[A-Z]?\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static RoomNumberSplit Split(string? number)
    {
        if (string.IsNullOrWhiteSpace(number)) return new(null, null, null);
        var m = SizePattern.Match(number);
        if (m.Success) return new($"{Tidy(m.Groups[1].Value)} x {Tidy(m.Groups[2].Value)}", null, null);
        if (NumberPattern.IsMatch(number)) return new(null, null, number.Trim());
        return new(null, number.Trim(), null);
    }

    private static string Tidy(string length) => Regex.Replace(length.Trim(), @"\s*-\s*", "-");
}

/// <summary>A room number the rule picked, and why.</summary>
public sealed record RoomNumberAssignment(long RoomId, string RoomName, string? OldNumber, string NewNumber);

/// <summary>Numbering for one level, or the question that blocks it.</summary>
public sealed record RoomNumberingResult(string Level, IReadOnlyList<RoomNumberAssignment> Assignments, string? Question)
{
    public bool Blocked => Question is not null;
}

/// <summary>
/// Office rule: level prefix + sequence (main 101, basement 001, upper 201), starting at the
/// entry and going clockwise around the level. Pure geometry on room points, so it's the
/// same answer every run.
/// </summary>
public static class RoomNumbering
{
    public static IReadOnlyList<RoomNumberingResult> Number(IEnumerable<RoomFact> rooms, AuditSettings settings, IReadOnlyDictionary<string, string>? entryOverrides = null)
    {
        var placed = rooms.Where(r => r.State == "placed" && r.X is not null && r.Y is not null).ToList();
        var results = new List<RoomNumberingResult>();
        foreach (var level in placed.GroupBy(r => r.Level ?? "").OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var list = level.ToList();
            var prefix = LevelPrefix(level.Key, settings);
            if (prefix is null)
            {
                results.Add(new(level.Key, Array.Empty<RoomNumberAssignment>(),
                    $"Which floor is level \"{level.Key}\"? Room numbers start with the floor: basement 0 (001), main 1 (101), upper 2 (201). Add it to manual.audit.levelNumberPrefixes."));
                continue;
            }

            var entry = FindEntry(list, settings, entryOverrides is not null && entryOverrides.TryGetValue(level.Key, out var chosen) ? chosen : null, out var why);
            if (entry is null)
            {
                results.Add(new(level.Key, Array.Empty<RoomNumberAssignment>(), why));
                continue;
            }

            var cx = list.Average(r => r.X!.Value);
            var cy = list.Average(r => r.Y!.Value);
            var start = Math.Atan2(entry.Y!.Value - cy, entry.X!.Value - cx);
            var ordered = list
                .OrderBy(r => r.ElementId == entry.ElementId ? -1 : ClockwiseFrom(start, Math.Atan2(r.Y!.Value - cy, r.X!.Value - cx)))
                .ThenBy(r => Math.Sqrt(Math.Pow(r.X!.Value - cx, 2) + Math.Pow(r.Y!.Value - cy, 2)))
                .ThenBy(r => r.ElementId)
                .ToList();
            var assignments = ordered
                .Select((r, i) => new RoomNumberAssignment(r.ElementId, r.Name, string.IsNullOrWhiteSpace(r.Number) ? null : r.Number, Format(prefix.Value, i + 1)))
                .ToList();
            results.Add(new(level.Key, assignments, null));
        }
        return results;
    }

    /// <summary>101, 102 ... 199, then 1100 if a floor ever has more than 99 rooms. Basement: 001.</summary>
    public static string Format(int prefix, int sequence) => $"{prefix}{sequence:00}";

    /// <summary>Radians turned clockwise from start to angle, in [0, 2pi).</summary>
    private static double ClockwiseFrom(double start, double angle)
    {
        var d = (start - angle) % (2 * Math.PI);
        return d < 0 ? d + 2 * Math.PI : d;
    }

    private static RoomFact? FindEntry(List<RoomFact> rooms, AuditSettings settings, string? chosenName, out string? question)
    {
        question = null;
        var names = chosenName is not null ? new List<string> { chosenName } : settings.EntryRoomNames;
        foreach (var name in names)
        {
            var matches = rooms.Where(r => string.Equals(r.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 1) return matches[0];
            if (matches.Count > 1)
            {
                question = $"{matches.Count} rooms on this level are named \"{name}\". Which one is the entry, where numbering starts?";
                return null;
            }
        }
        question = $"No room on this level is named {string.Join(" or ", names)}. Which room is the entry, where numbering starts?";
        return null;
    }

    public static int? LevelPrefix(string level, AuditSettings settings)
    {
        foreach (var (name, prefix) in settings.LevelNumberPrefixes)
            if (string.Equals(name.Trim(), level.Trim(), StringComparison.OrdinalIgnoreCase)) return prefix;
        var l = level.ToUpperInvariant();
        if (Regex.IsMatch(l, @"\b(BASEMENT|LOWER|CELLAR)\b")) return 0;
        if (Regex.IsMatch(l, @"\b(UPPER|SECOND|2ND|LOFT)\b")) return 2;
        if (Regex.IsMatch(l, @"\b(MAIN|FIRST|1ST|GROUND)\b")) return 1;
        return null;
    }
}
