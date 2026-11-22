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
}

public sealed class Proposal
{
    internal Proposal(string id, string documentTitle, ProposalRequest request, DateTime createdUtc)
    {
        Id = id;
        DocumentTitle = documentTitle;
        Title = request.Title;
        Rationale = request.Rationale;
        Changes = request.Changes.ToList();
        CreatedUtc = createdUtc;
    }

    public string Id { get; }
    public string DocumentTitle { get; }
    public string Title { get; }
    public string Rationale { get; }
    public IReadOnlyList<ProposalChange> Changes { get; }
    public DateTime CreatedUtc { get; }

    public ProposalStatus Status { get; internal set; } = ProposalStatus.Pending;
    public string? ResultMessage { get; internal set; }
    public IReadOnlyList<long> CreatedElementIds { get; internal set; } = Array.Empty<long>();
    public DateTime? ResolvedUtc { get; internal set; }
    public string? ResolvedBy { get; internal set; }
    public int Attempts { get; internal set; }

    public bool CanApprove => Status is ProposalStatus.Pending or ProposalStatus.Failed;
    public bool CanReject => Status is ProposalStatus.Pending or ProposalStatus.Failed;
    public bool IsResolved => Status is ProposalStatus.Applied or ProposalStatus.Rejected;
}
