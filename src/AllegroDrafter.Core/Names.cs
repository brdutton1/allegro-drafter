namespace AllegroDrafter.Core;

public static class Names
{
    /// <summary>The one naming format used everywhere for family types: "Family: Type".</summary>
    public static string FamilyType(string family, string type) => $"{family}: {type}";

    /// <summary>
    /// Closest option to a name the AI asked for, so the error it gets back can
    /// point it at the real one ("Did you mean ...?"). Null if nothing is close.
    /// </summary>
    public static string? ClosestMatch(string value, IEnumerable<string> options)
    {
        string? best = null;
        var bestDistance = int.MaxValue;
        var needle = value.Trim().ToLowerInvariant();
        foreach (var option in options)
        {
            var distance = Levenshtein(needle, option.ToLowerInvariant());
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = option;
            }
        }

        if (best is null) return null;
        var allowed = Math.Max(3, needle.Length / 3);
        return bestDistance <= allowed ? best : null;
    }

    public static int Levenshtein(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }

    /// <summary>"Unknown X 'y'. Did you mean 'z'?" plus a short list of valid options.</summary>
    public static string UnknownNameMessage(string kind, string value, IReadOnlyCollection<string> options, int listLimit = 12)
    {
        var message = $"{kind} '{value}' is not in the office standards profile.";
        var closest = ClosestMatch(value, options);
        if (closest is not null) message += $" Did you mean '{closest}'?";
        if (options.Count == 0)
        {
            message += $" The profile has no {kind.ToLowerInvariant()} entries; re-export standards from the office template.";
        }
        else
        {
            var shown = options.Take(listLimit).Select(o => $"'{o}'");
            message += $" Valid options: {string.Join(", ", shown)}";
            if (options.Count > listLimit) message += $" (+{options.Count - listLimit} more, see get_standards)";
            message += ".";
        }
        return message;
    }

    public static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':' }).ToHashSet();
        var cleaned = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "untitled" : cleaned;
    }
}
