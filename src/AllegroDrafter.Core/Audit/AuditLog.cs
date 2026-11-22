using System.Text.Json;
using AllegroDrafter.Core.Proposals;

namespace AllegroDrafter.Core.Audit;

public sealed record AuditEntry(
    DateTime TimestampUtc,
    string User,
    string Document,
    string ProposalId,
    string Title,
    string Rationale,
    string Outcome,
    string? Message,
    int Attempt,
    IReadOnlyList<string> Changes,
    IReadOnlyList<long> CreatedElementIds)
{
    public static AuditEntry From(Proposal p, string user) => new(
        p.ResolvedUtc ?? DateTime.UtcNow,
        user,
        p.DocumentTitle,
        p.Id,
        p.Title,
        p.Rationale,
        p.Status.ToString(),
        p.ResultMessage,
        p.Attempts,
        p.Changes.Select(c => c.Describe()).ToList(),
        p.CreatedElementIds);
}

/// <summary>
/// Append-only record of every approve, reject and failure: who, when, what.
/// One JSON object per line, one file per month (audit/2026-11.jsonl).
/// </summary>
public sealed class AuditLog
{
    private readonly AppPaths _paths;
    private readonly object _gate = new();

    public AuditLog(AppPaths paths) => _paths = paths;

    public string FileFor(DateTime utc) => Path.Combine(_paths.AuditDir, $"{utc:yyyy-MM}.jsonl");

    /// <summary>Returns false (never throws) if the log could not be written, so Revit keeps working.</summary>
    public bool Write(AuditEntry entry)
    {
        try
        {
            var line = JsonSerializer.Serialize(entry, Json.Compact) + Environment.NewLine;
            lock (_gate)
            {
                Directory.CreateDirectory(_paths.AuditDir);
                File.AppendAllText(FileFor(entry.TimestampUtc), line);
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = ex.Message;
            return false;
        }
    }

    public string? LastError { get; private set; }
}
