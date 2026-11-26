using System.Text.Json;

namespace AllegroDrafter.Core.Finishing;

/// <summary>
/// What each applied Finish Drafting batch changed, per project, saved after every batch so the
/// change report still covers the whole run if Revit closes partway through. A new run (after one
/// finished) starts a fresh list; Resume keeps adding to the same one.
/// </summary>
public sealed class FinishChangesStore
{
    private sealed record Saved(DateTime StartedUtc, List<ChangeImpact> Impacts, DateTime? FinishedUtc = null);

    private readonly object _gate = new();
    private readonly string? _savePath;
    private readonly Dictionary<string, Saved> _byProject = new(StringComparer.Ordinal);

    public FinishChangesStore(string? savePath = null)
    {
        _savePath = savePath;
        if (_savePath is not null) Load();
    }

    public string? Warning { get; private set; }

    /// <summary>This project's latest run: when it started, what it changed, and when it finished (null while in progress).</summary>
    public (DateTime? StartedUtc, IReadOnlyList<ChangeImpact> Impacts, DateTime? FinishedUtc) For(string project)
    {
        lock (_gate)
            return _byProject.TryGetValue(project, out var s)
                ? (s.StartedUtc, s.Impacts.ToList(), s.FinishedUtc)
                : (null, Array.Empty<ChangeImpact>(), null);
    }

    /// <summary>
    /// The run to record into: the unfinished one if there is one (Resume, or Revit closed mid-run),
    /// otherwise a new, empty one starting now.
    /// </summary>
    public DateTime BeginOrResume(string project, DateTime nowUtc)
    {
        lock (_gate)
        {
            if (_byProject.TryGetValue(project, out var s) && s.FinishedUtc is null) return s.StartedUtc;
            _byProject[project] = new Saved(nowUtc, new List<ChangeImpact>());
        }
        Write();
        return nowUtc;
    }

    /// <summary>The run reached the end. The next Finish Drafting starts a new report.</summary>
    public void MarkFinished(string project, DateTime nowUtc)
    {
        lock (_gate)
        {
            if (!_byProject.TryGetValue(project, out var s)) return;
            _byProject[project] = s with { FinishedUtc = nowUtc };
        }
        Write();
    }

    /// <summary>Records what one batch changed. Replaces anything recorded for the same proposal.</summary>
    public void Record(string project, string proposalId, IEnumerable<ChangeImpact> impacts)
    {
        lock (_gate)
        {
            if (!_byProject.TryGetValue(project, out var s) || s.FinishedUtc is not null)
                _byProject[project] = s = new Saved(DateTime.UtcNow, new List<ChangeImpact>());
            s.Impacts.RemoveAll(i => i.ProposalId == proposalId);
            s.Impacts.AddRange(impacts);
        }
        Write();
    }

    private void Write()
    {
        if (_savePath is null) return;
        try
        {
            lock (_gate)
            {
                var json = JsonSerializer.Serialize(_byProject, Json.Indented);
                Directory.CreateDirectory(Path.GetDirectoryName(_savePath)!);
                File.WriteAllText(_savePath + ".tmp", json);
                File.Move(_savePath + ".tmp", _savePath, overwrite: true);
            }
            Warning = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Warning = $"Couldn't save the change report to {_savePath}: {ex.Message}. It will be short if Revit closes mid-run.";
        }
    }

    private void Load()
    {
        if (!File.Exists(_savePath)) return;
        try
        {
            var saved = JsonSerializer.Deserialize<Dictionary<string, Saved>>(File.ReadAllText(_savePath!), Json.Indented);
            foreach (var (project, s) in saved ?? new())
                if (s?.Impacts is not null) _byProject[project] = s with { Impacts = s.Impacts.Where(i => i is not null).ToList() };
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or IOException)
        {
            var backup = $"{_savePath}.broken-{DateTime.UtcNow:yyyyMMddHHmmss}";
            try { File.Move(_savePath!, backup, overwrite: true); } catch (IOException) { }
            Warning = $"The saved change report couldn't be read ({ex.Message}). Starting a new one; the old file is at {backup}.";
        }
    }
}
