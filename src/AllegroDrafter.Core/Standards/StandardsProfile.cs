namespace AllegroDrafter.Core.Standards;

/// <summary>
/// The office DNA. Exported from the office template by the "Export Standards"
/// command. Every name the AI is allowed to use comes from this file.
/// The <see cref="Manual"/> section is hand-edited and survives re-exports.
/// </summary>
public sealed class StandardsProfile
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string SourceDocument { get; set; } = "";
    public DateTime ExportedUtc { get; set; }
    public string? RevitVersion { get; set; }

    public List<string> TitleBlocks { get; set; } = new();
    public List<ViewTemplateInfo> ViewTemplates { get; set; } = new();
    public List<ViewFamilyTypeInfo> ViewFamilyTypes { get; set; } = new();
    public List<TagTypeInfo> TagTypes { get; set; } = new();
    public List<string> TextNoteTypes { get; set; } = new();
    public List<string> DimensionTypes { get; set; } = new();
    public List<string> Schedules { get; set; } = new();

    /// <summary>Schedule name to the category it lists (e.g. "DOOR SCHEDULE" -> "Doors").</summary>
    public Dictionary<string, string> ScheduleCategories { get; set; } = new();
    public List<string> DraftingViews { get; set; } = new();
    public List<string> Legends { get; set; } = new();
    public List<SheetInfo> Sheets { get; set; } = new();

    public List<string> WallTypes { get; set; } = new();
    public List<string> FloorTypes { get; set; } = new();
    public List<string> RoofTypes { get; set; } = new();
    public List<string> CeilingTypes { get; set; } = new();
    public List<string> DoorTypes { get; set; } = new();
    public List<string> WindowTypes { get; set; } = new();

    public string? KeynoteFile { get; set; }

    public ManualSettings Manual { get; set; } = new();
}

public sealed record ViewTemplateInfo(string Name, string ViewType);

public sealed record ViewFamilyTypeInfo(string Name, string ViewFamily);

public sealed record TagTypeInfo(string Category, string Name);

public sealed record SheetInfo(string Number, string Name);

/// <summary>Hand-maintained settings. Never overwritten by an export.</summary>
public sealed class ManualSettings
{
    /// <summary>Space to keep clear inside the title block when auto-arranging views.</summary>
    public SheetInsets DrawingAreaInsetsInches { get; set; } = new();

    /// <summary>
    /// Typical sheet index per project type, e.g. "new-home", "addition", "garage", "adu".
    /// Built in Phase 0 from past issued plan sets.
    /// </summary>
    public Dictionary<string, List<SheetInfo>> SheetIndexByProjectType { get; set; } = new();

    /// <summary>Free-form office rules the AI should follow (numbering, view naming, etc.).</summary>
    public List<string> Rules { get; set; } = new();

    /// <summary>
    /// The finish codes from the office finish legend (e.g. "T1" = "PORCELAIN TILE").
    /// When this list has entries, finish parameters can only be set to these codes.
    /// </summary>
    public List<FinishCode> FinishCodes { get; set; } = new();

    /// <summary>Room parameters that hold finish codes. Add custom shared parameters here.</summary>
    public List<string> FinishParameters { get; set; } = new() { "Floor Finish", "Base Finish", "Wall Finish", "Ceiling Finish" };
}

public sealed record FinishCode(string Code, string Description);

public sealed class SheetInsets
{
    public double Left { get; set; } = 1.0;
    public double Right { get; set; } = 1.0;
    public double Top { get; set; } = 0.5;
    public double Bottom { get; set; } = 0.5;
}
