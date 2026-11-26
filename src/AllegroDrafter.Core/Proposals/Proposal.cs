namespace AllegroDrafter.Core.Proposals;

public enum ProposalStatus
{
    /// <summary>Waiting on the designer.</summary>
    Pending,
    /// <summary>Approved, being written to the model right now.</summary>
    Applying,
    /// <summary>Committed to the model as one undoable transaction.</summary>
    Applied,
    /// <summary>Designer said no. Nothing was changed.</summary>
    Rejected,
    /// <summary>Approved but Revit refused or the model changed. Nothing was changed. Can be retried.</summary>
    Failed,
    /// <summary>Applied, then undone in Revit (Ctrl+Z). Redo in Revit puts it back to Applied.</summary>
    Undone,
}

public sealed class Proposal
{
    internal Proposal(string id, string documentTitle, ProposalRequest request, DateTime createdUtc, string? finishStep = null)
    {
        Id = id;
        DocumentTitle = documentTitle;
        Title = request.Title;
        Rationale = request.Rationale;
        Changes = request.Changes.ToList();
        CreatedUtc = createdUtc;
        FinishStep = finishStep;
    }

    /// <summary>Rebuilds a proposal saved to disk by <see cref="ProposalStore"/>.</summary>
    internal Proposal(ProposalRecord r)
    {
        Id = r.Id;
        DocumentTitle = r.DocumentTitle;
        Title = r.Title;
        Rationale = r.Rationale;
        Changes = r.Changes.ToList();
        CreatedUtc = r.CreatedUtc;
        Status = r.Status;
        ResultMessage = r.ResultMessage;
        CreatedElementIds = r.CreatedElementIds.ToList();
        ResolvedUtc = r.ResolvedUtc;
        ResolvedBy = r.ResolvedBy;
        Attempts = r.Attempts;
        FinishStep = r.FinishStep;
    }

    internal ProposalRecord ToRecord() => new(
        Id, DocumentTitle, Title, Rationale, Changes.ToList(), CreatedUtc, Status,
        ResultMessage, CreatedElementIds.ToList(), ResolvedUtc, ResolvedBy, Attempts, FinishStep);

    public string Id { get; }
    public string DocumentTitle { get; }
    public string Title { get; }
    public string Rationale { get; }
    public IReadOnlyList<ProposalChange> Changes { get; }
    public DateTime CreatedUtc { get; }

    /// <summary>The Finish Drafting step that queued this batch ("type-marks"), or null for anything else.</summary>
    public string? FinishStep { get; }

    /// <summary>Waiting on the designer, or failed and still retryable.</summary>
    public bool IsOpen => Status is ProposalStatus.Pending or ProposalStatus.Failed;

    public ProposalStatus Status { get; internal set; } = ProposalStatus.Pending;
    public string? ResultMessage { get; internal set; }
    public IReadOnlyList<long> CreatedElementIds { get; internal set; } = Array.Empty<long>();
    public DateTime? ResolvedUtc { get; internal set; }
    public string? ResolvedBy { get; internal set; }
    public int Attempts { get; internal set; }

    /// <summary>The apply message, kept while the proposal is undone so a redo can restore it.</summary>
    internal string? AppliedMessage { get; set; }

    public bool CanApprove => Status is ProposalStatus.Pending or ProposalStatus.Failed;
    public bool CanReject => Status is ProposalStatus.Pending or ProposalStatus.Failed;
    public bool IsResolved => Status is ProposalStatus.Applied or ProposalStatus.Rejected or ProposalStatus.Undone;
}

/// <summary>On-disk shape of a proposal.</summary>
public sealed record ProposalRecord(
    string Id,
    string DocumentTitle,
    string Title,
    string Rationale,
    List<ProposalChange> Changes,
    DateTime CreatedUtc,
    ProposalStatus Status,
    string? ResultMessage,
    List<long> CreatedElementIds,
    DateTime? ResolvedUtc,
    string? ResolvedBy,
    int Attempts,
    string? FinishStep = null);
