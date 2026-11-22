namespace AllegroDrafter.Core.Proposals;

/// <summary>
/// Thread-safe list of proposals for this Revit session. The status changes are
/// guarded so a double-clicked Approve can only ever apply a proposal once.
/// </summary>
public sealed class ProposalStore
{
    private readonly object _gate = new();
    private readonly List<Proposal> _items = new();
    private readonly Func<DateTime> _clock;
    private int _counter;

    public ProposalStore(Func<DateTime>? clock = null) => _clock = clock ?? (() => DateTime.UtcNow);

    /// <summary>Raised after any change. May fire on any thread.</summary>
    public event EventHandler? Changed;

    public Proposal Add(string documentTitle, ProposalRequest request)
    {
        Proposal proposal;
        lock (_gate)
        {
            _counter++;
            var id = $"P{_counter:000}-{Guid.NewGuid().ToString("N")[..4]}";
            proposal = new Proposal(id, documentTitle, request, _clock());
            _items.Add(proposal);
        }
        RaiseChanged();
        return proposal;
    }

    public IReadOnlyList<Proposal> List()
    {
        lock (_gate) return _items.ToList();
    }

    public Proposal? Get(string id)
    {
        lock (_gate) return _items.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public int PendingCount
    {
        get { lock (_gate) return _items.Count(p => p.Status == ProposalStatus.Pending); }
    }

    /// <summary>
    /// Moves a proposal to Applying. Returns false if it was already applying,
    /// applied or rejected, so a second click does nothing.
    /// </summary>
    public bool TryBeginApply(string id)
    {
        lock (_gate)
        {
            var p = Find(id);
            if (p is null || !p.CanApprove) return false;
            p.Status = ProposalStatus.Applying;
            p.Attempts++;
        }
        RaiseChanged();
        return true;
    }

    public void CompleteApply(string id, bool success, string message, IEnumerable<long> createdElementIds, string user)
    {
        lock (_gate)
        {
            var p = Find(id) ?? throw new InvalidOperationException($"Unknown proposal {id}.");
            if (p.Status != ProposalStatus.Applying) throw new InvalidOperationException($"Proposal {id} is {p.Status}, not Applying.");
            p.Status = success ? ProposalStatus.Applied : ProposalStatus.Failed;
            p.ResultMessage = message;
            p.CreatedElementIds = success ? createdElementIds.ToList() : Array.Empty<long>();
            p.ResolvedUtc = _clock();
            p.ResolvedBy = user;
        }
        RaiseChanged();
    }

    public bool Reject(string id, string user)
    {
        lock (_gate)
        {
            var p = Find(id);
            if (p is null || !p.CanReject) return false;
            p.Status = ProposalStatus.Rejected;
            p.ResultMessage = "Rejected by designer. Nothing was changed.";
            p.ResolvedUtc = _clock();
            p.ResolvedBy = user;
        }
        RaiseChanged();
        return true;
    }

    /// <summary>Removes applied and rejected proposals from the list (the audit log keeps them).</summary>
    public int ClearResolved()
    {
        int removed;
        lock (_gate) removed = _items.RemoveAll(p => p.IsResolved);
        if (removed > 0) RaiseChanged();
        return removed;
    }

    private Proposal? Find(string id) =>
        _items.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
