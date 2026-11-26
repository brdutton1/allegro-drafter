using System.Text.Json;

namespace AllegroDrafter.Core.Finishing;

/// <summary>
/// The designer's answers to Finish Drafting question cards, saved per project so a restart
/// doesn't ask the same questions again. Card ids carry element ids, which are only unique
/// inside one model, so answers are always kept apart by project.
/// Skipped steps are not saved: every run gives them a fresh chance (see FinishRun.Start).
/// </summary>
public sealed class FinishAnswersStore
{
    private readonly object _gate = new();
    private readonly string? _savePath;
    private readonly Dictionary<string, Dictionary<string, string>> _byProject = new(StringComparer.Ordinal);

    public FinishAnswersStore(string? savePath = null)
    {
        _savePath = savePath;
        if (_savePath is not null) Load();
    }

    /// <summary>Anything worth telling the designer about loading or saving, or null.</summary>
    public string? Warning { get; private set; }

    /// <summary>A fresh state holding this project's saved answers.</summary>
    public FinishState StateFor(string documentTitle)
    {
        var state = new FinishState();
        lock (_gate)
        {
            if (_byProject.TryGetValue(documentTitle, out var answers))
                foreach (var (id, answer) in answers) state.Answers[id] = answer;
        }
        return state;
    }

    public void Save(string documentTitle, FinishState state)
    {
        lock (_gate) _byProject[documentTitle] = new Dictionary<string, string>(state.Answers, StringComparer.OrdinalIgnoreCase);
        Write();
    }

    private void Write()
    {
        if (_savePath is null) return;
        try
        {
            string json;
            lock (_gate) json = JsonSerializer.Serialize(_byProject, Json.Indented);
            Directory.CreateDirectory(Path.GetDirectoryName(_savePath)!);
            var temp = _savePath + ".tmp";
            lock (_gate)
            {
                File.WriteAllText(temp, json);
                File.Move(temp, _savePath, overwrite: true);
            }
            Warning = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Warning = $"Couldn't save Finish Drafting answers to {_savePath}: {ex.Message}. You may be asked again after Revit closes.";
        }
    }

    private void Load()
    {
        if (!File.Exists(_savePath)) return;
        try
        {
            var saved = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(_savePath!), Json.Indented);
            foreach (var (project, answers) in saved ?? new())
            {
                if (answers is null) continue;
                _byProject[project] = new Dictionary<string, string>(answers, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or IOException)
        {
            var backup = $"{_savePath}.broken-{DateTime.UtcNow:yyyyMMddHHmmss}";
            try { File.Move(_savePath!, backup, overwrite: true); } catch (IOException) { }
            Warning = $"Saved Finish Drafting answers couldn't be read ({ex.Message}). Starting with none; the old file is at {backup}.";
        }
    }
}
