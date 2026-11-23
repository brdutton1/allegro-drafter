using System.Text.RegularExpressions;

namespace AllegroDrafter.Core.Proposals;

/// <summary>
/// The Revit transaction name for a proposal. It carries the proposal id, so when the
/// designer undoes or redoes in Revit the add-in can tell which proposal it was.
/// </summary>
public static class UndoNames
{
    public const string Prefix = "Allegro Drafter";

    /// <summary>Revit shows long names badly in the undo list. The id is never cut.</summary>
    public const int MaxLength = 80;

    private static readonly Regex IdPattern = new(@"^Allegro Drafter \[(P\d{3,}-[0-9a-f]{4})\]", RegexOptions.CultureInvariant);

    public static string Format(string proposalId, string title)
    {
        var head = $"{Prefix} [{proposalId}]: ";
        var room = Math.Max(0, MaxLength - head.Length);
        var t = (title ?? "").Trim();
        if (t.Length > room) t = room > 3 ? t[..(room - 3)].TrimEnd() + "..." : t[..room];
        return (head + t).TrimEnd();
    }

    /// <summary>The proposal id in a transaction name, or null if it isn't one of ours.</summary>
    public static string? ParseId(string? transactionName)
    {
        if (string.IsNullOrEmpty(transactionName)) return null;
        var m = IdPattern.Match(transactionName);
        return m.Success ? m.Groups[1].Value : null;
    }
}
