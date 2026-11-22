using System.Text.Json;

namespace AllegroDrafter.Core.Standards;

/// <summary>
/// Loads the active standards profile and reloads it automatically when the
/// file changes on disk (so hand edits take effect without restarting Revit).
/// </summary>
public sealed class StandardsStore
{
    private readonly AppPaths _paths;
    private readonly object _gate = new();
    private StandardsProfile? _cached;
    private DateTime _cachedStamp;

    public StandardsStore(AppPaths paths) => _paths = paths;

    public string ActivePath => _paths.ActiveStandardsFile;

    /// <summary>Why the last load failed, or null.</summary>
    public string? LoadError { get; private set; }

    public StandardsProfile? Current
    {
        get
        {
            lock (_gate)
            {
                var file = new FileInfo(_paths.ActiveStandardsFile);
                if (!file.Exists)
                {
                    _cached = null;
                    LoadError = null;
                    return null;
                }

                if (_cached is not null && file.LastWriteTimeUtc == _cachedStamp) return _cached;

                try
                {
                    _cached = Load(file.FullName);
                    _cachedStamp = file.LastWriteTimeUtc;
                    LoadError = null;
                }
                catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
                {
                    // Keep the last good profile if a hand edit broke the JSON.
                    LoadError = $"Could not read {file.FullName}: {ex.Message}";
                }
                return _cached;
            }
        }
    }

    public static StandardsProfile Load(string path)
    {
        var json = File.ReadAllText(path);
        var profile = JsonSerializer.Deserialize<StandardsProfile>(json, Json.Indented)
            ?? throw new InvalidDataException("File is empty.");
        if (profile.SchemaVersion > StandardsProfile.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Profile schema v{profile.SchemaVersion} is newer than this add-in supports (v{StandardsProfile.CurrentSchemaVersion}). Update Allegro Drafter.");
        }
        profile.Manual ??= new ManualSettings();
        profile.Manual.DrawingAreaInsetsInches ??= new SheetInsets();
        profile.Manual.Audit ??= new Checks.AuditSettings();
        return profile;
    }

    /// <summary>Writes a timestamped copy of an export. Returns its path.</summary>
    public string SaveExport(StandardsProfile profile)
    {
        _paths.EnsureCreated();
        var name = $"{Names.SafeFileName(profile.SourceDocument)}-{profile.ExportedUtc:yyyyMMdd-HHmmss}.json";
        var path = Path.Combine(_paths.StandardsExportDir, name);
        WriteAtomic(path, profile);
        return path;
    }

    /// <summary>
    /// Makes an export the active profile. The hand-edited Manual section of the
    /// previous active profile is carried over so re-exporting never wipes it.
    /// </summary>
    public void SetActive(StandardsProfile exported)
    {
        _paths.EnsureCreated();
        lock (_gate)
        {
            if (File.Exists(_paths.ActiveStandardsFile))
            {
                try
                {
                    exported.Manual = Load(_paths.ActiveStandardsFile).Manual;
                }
                catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
                {
                    // Previous file is unreadable. Back it up instead of silently losing the manual section.
                    File.Copy(_paths.ActiveStandardsFile, _paths.ActiveStandardsFile + $".broken-{DateTime.UtcNow:yyyyMMddHHmmss}", overwrite: true);
                }
            }
            WriteAtomic(_paths.ActiveStandardsFile, exported);
            _cached = null;
        }
    }

    private static void WriteAtomic(string path, StandardsProfile profile)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(profile, Json.Indented));
        File.Move(temp, path, overwrite: true);
    }
}
