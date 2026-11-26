using System.Text;
using System.Text.RegularExpressions;
using AllegroDrafter.Core.Proposals;

namespace AllegroDrafter.Core.Finishing;

/// <summary>
/// One thing an applied batch changed, and where it prints. Sheet is null when the change lives
/// only in the model (a wall type's Function, a new parameter) and nothing on a sheet moved.
/// </summary>
/// <param name="Where">The view or schedule on the sheet ("DOOR SCHEDULE", "MAIN FLOOR PLAN").</param>
/// <param name="What">What changed there, without a count ("Door Swing", "room tags", "view template ADC - Elevations").</param>
/// <param name="ElementIds">The elements behind it, for Show. Counted in the report.</param>
/// <param name="Noun">What the ids are, for the count ("door", "room", "tag").</param>
public sealed record ChangeImpact(
    string ProposalId,
    string? SheetNumber,
    string? SheetName,
    long? SheetId,
    string Where,
    string What,
    IReadOnlyList<long> ElementIds,
    string? Noun = null);

/// <summary>One line of the report: a change on one view or schedule, merged across batches.</summary>
public sealed record ChangeLine(string Where, string What, IReadOnlyList<long> ElementIds, string? Noun, IReadOnlyList<string> ProposalIds)
{
    public string Text
    {
        get
        {
            var count = Noun is null || ElementIds.Count == 0 ? "" : $" on {ElementIds.Count} {Noun}{(ElementIds.Count == 1 ? "" : "s")}";
            return string.IsNullOrEmpty(What) ? Where + count : $"{Where}: {What}{count}";
        }
    }
}

public sealed record SheetChanges(string Number, string Name, long? SheetId, IReadOnlyList<ChangeLine> Lines);

/// <summary>
/// The Finish Drafting change report: every sheet the run touched and what changed on it, so the
/// designer checks those sheets and nothing else. Built from what applied batches recorded;
/// a batch that's been undone drops out, because nothing of it is in the model any more.
/// </summary>
public sealed class ChangeReport
{
    public IReadOnlyList<SheetChanges> Sheets { get; private init; } = Array.Empty<SheetChanges>();
    public IReadOnlyList<ChangeLine> ModelOnly { get; private init; } = Array.Empty<ChangeLine>();
    public IReadOnlyList<string> UndoneProposals { get; private init; } = Array.Empty<string>();
    public int AppliedBatches { get; private init; }

    public bool IsEmpty => Sheets.Count == 0 && ModelOnly.Count == 0;

    /// <param name="status">The current status of a proposal, from the store. Unknown ids count as gone.</param>
    public static ChangeReport Build(IEnumerable<ChangeImpact> impacts, Func<string, ProposalStatus?> status)
    {
        var all = impacts.ToList();
        var live = all.Where(i => status(i.ProposalId) == ProposalStatus.Applied).ToList();
        var undone = all.Select(i => i.ProposalId).Distinct()
            .Where(id => status(id) == ProposalStatus.Undone).OrderBy(id => id, StringComparer.Ordinal).ToList();

        var sheets = live.Where(i => i.SheetNumber is not null)
            .GroupBy(i => i.SheetNumber!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SheetChanges(g.First().SheetNumber!, g.First().SheetName ?? "", g.First().SheetId, Merge(g)))
            .OrderBy(s => SortKey(s.Number), StringComparer.Ordinal)
            .ToList();

        return new ChangeReport
        {
            Sheets = sheets,
            ModelOnly = Merge(live.Where(i => i.SheetNumber is null)),
            UndoneProposals = undone,
            AppliedBatches = live.Select(i => i.ProposalId).Distinct().Count(),
        };
    }

    /// <summary>Same view, same change: one line, ids unioned (a door changed twice counts once).</summary>
    private static IReadOnlyList<ChangeLine> Merge(IEnumerable<ChangeImpact> impacts) =>
        impacts.GroupBy(i => (Where: i.Where.ToUpperInvariant(), What: i.What.ToUpperInvariant(), i.Noun))
            .Select(g => new ChangeLine(
                g.First().Where,
                g.First().What,
                g.SelectMany(i => i.ElementIds).Distinct().ToList(),
                g.Key.Noun,
                g.Select(i => i.ProposalId).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToList()))
            .OrderBy(l => l.Where, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.What, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Cover first, then by discipline letter, then numbers in number order (A2 before A10, A1 before A1.1).</summary>
    internal static string SortKey(string number)
    {
        var n = number.Trim().ToUpperInvariant();
        var rank = n.StartsWith("CS") || n.StartsWith("G") || n.StartsWith("T") ? "0" : "1";
        return rank + Regex.Replace(n, @"\d+", m => m.Value.PadLeft(6, '0'));
    }

    public string ToMarkdown(string project, DateTime finishedLocal, IEnumerable<string>? remaining = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Finish Drafting: what changed");
        sb.AppendLine();
        sb.AppendLine($"{project} · {finishedLocal:yyyy-MM-dd h:mm tt} · {AppliedBatches} batch{(AppliedBatches == 1 ? "" : "es")} applied");
        sb.AppendLine();
        if (IsEmpty) sb.AppendLine("Nothing in the model changed.").AppendLine();

        if (Sheets.Count > 0)
        {
            sb.AppendLine($"## Sheets to check ({Sheets.Count})");
            sb.AppendLine();
            foreach (var s in Sheets)
            {
                sb.AppendLine($"### {s.Number} {s.Name}".TrimEnd());
                foreach (var l in s.Lines) sb.AppendLine($"- {l.Text} ({string.Join(", ", l.ProposalIds)})");
                sb.AppendLine();
            }
        }
        if (ModelOnly.Count > 0)
        {
            sb.AppendLine("## In the model only (nothing on a sheet moved)");
            sb.AppendLine();
            foreach (var l in ModelOnly) sb.AppendLine($"- {l.Text} ({string.Join(", ", l.ProposalIds)})");
            sb.AppendLine();
        }
        if (UndoneProposals.Count > 0)
        {
            sb.AppendLine($"Undone with Ctrl+Z, so left out above: {string.Join(", ", UndoneProposals)}.");
            sb.AppendLine();
        }
        var left = remaining?.ToList() ?? new List<string>();
        if (left.Count > 0)
        {
            sb.AppendLine("## Still open");
            sb.AppendLine();
            foreach (var r in left) sb.AppendLine($"- {r}");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
