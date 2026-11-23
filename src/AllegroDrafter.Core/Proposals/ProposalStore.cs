using System.Text.Json;

namespace AllegroDrafter.Core.Proposals;

/// <summary>
/// Thread-safe list of proposals. The status changes are guarded so a
/// double-clicked Approve can only ever apply a proposal once.
/// With a save path, every change is written to disk, so pending proposals
/// survive Revit closing or the laptop shutting down.
/// </summary>
public sealed class ProposalStore
{
    public static readonly TimeSpan KeepResolvedFor = TimeSpan.FromDays(7);
    public const string InterruptedMessage = "Revit closed while this was being applied, so Revit undid it. Check the model, then Retry or Reject.";

    private readonly object _gate = new();
    private readonly object _saveGate = new();
    private readonly List<Proposal> _items = new();
    private readonly Func<DateTime> _clock;
    private readonly string? _savePath;
    private int _counter;

    public ProposalStore(Func<DateTime>? clock = null, string? savePath = null)
    {
        _clock = clock ?? (() => DateTime.UtcNow);
        _savePath = savePath;
        if (_savePath is not null) Load();
    }

    /// <summary>Where proposals are saved, or null if they only live in memory.</summary>
    public string? SavePath => _savePath;

    /// <summary>How many proposals came back from the last session.</summary>
    public int RestoredCount { get; private set; }

    /// <summary>Anything worth telling the designer about loading or saving, or null.</summary>
    public string? PersistenceWarning { get; private set; }

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

    private void RaiseChanged()
    {
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Save()
    {
        if (_savePath is null) return;
        try
        {
            string json;
            lock (_gate) json = JsonSerializer.Serialize(_items.Select(p => p.ToRecord()).ToList(), Json.Indented);
            Directory.CreateDirectory(Path.GetDirectoryName(_savePath)!);
            var temp = _savePath + ".tmp";
            lock (_saveGate)
            {
                File.WriteAllText(temp, json);
                File.Move(temp, _savePath, overwrite: true);
            }
            if (PersistenceWarning?.StartsWith("Couldn't save") == true) PersistenceWarning = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            PersistenceWarning = $"Couldn't save proposals to {_savePath}: {ex.Message}. They'll be lost if Revit closes.";
        }
    }

    private void Load()
    {
        if (!File.Exists(_savePath)) return;
        List<ProposalRecord>? records;
        try
        {
            records = JsonSerializer.Deserialize<List<ProposalRecord>>(File.ReadAllText(_savePath!), Json.Indented);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or IOException)
        {
            var backup = $"{_savePath}.broken-{_clock():yyyyMMddHHmmss}";
            try
            {
                File.Move(_savePath!, backup, overwrite: true);
            }
            catch (IOException)
            {
            }
            PersistenceWarning = $"Saved proposals couldn't be read ({ex.Message}). Started empty; the old file is at {backup}.";
            return;
        }

        var cutoff = _clock() - KeepResolvedFor;
        foreach (var record in records ?? new List<ProposalRecord>())
        {
            if (record is null || record.Changes is null) continue;
            var resolved = record.Status is ProposalStatus.Applied or ProposalStatus.Rejected;
            if (resolved && (record.ResolvedUtc ?? record.CreatedUtc) < cutoff) continue;

            var proposal = new Proposal(record);
            if (proposal.Status == ProposalStatus.Applying)
            {
                // Revit died mid-transaction, which rolls it back. Never re-apply on its own.
                proposal.Status = ProposalStatus.Failed;
                proposal.ResultMessage = InterruptedMessage;
                proposal.ResolvedUtc = _clock();
            }
            _items.Add(proposal);
        }

        _counter = _items.Select(p => ParseNumber(p.Id)).DefaultIfEmpty(0).Max();
        RestoredCount = _items.Count(p => !p.IsResolved);
    }

    private static int ParseNumber(string id)
    {
        var digits = new string(id.Skip(1).TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) ? n : 0;
    }
}
