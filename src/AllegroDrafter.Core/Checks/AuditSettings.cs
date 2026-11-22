namespace AllegroDrafter.Core.Checks;

/// <summary>
/// Office rules the set audit checks against. Lives in standards.json under
/// manual.audit, so it survives re-exports. Every value has a working default.
/// </summary>
public sealed class AuditSettings
{
    // ---- Size codes (Type Mark = "WWHH OPER", e.g. "3050 CSMT", "2868") ----

    /// <summary>Operation codes allowed after the size on windows.</summary>
    public List<string> WindowOperations { get; set; } = new() { "CSMT", "AWN", "DH", "SH", "SLDR", "FIX" };

    /// <summary>Operation codes allowed after the size on doors. Swing doors carry the size only.</summary>
    public List<string> DoorOperations { get; set; } = new() { "PKT", "BARN", "BIFOLD", "BYPASS", "SLDR", "FRNCH", "OH" };

    /// <summary>Windows must carry an operation code. Doors may leave it off.</summary>
    public bool WindowOperationRequired { get; set; } = true;

    /// <summary>
    /// Words in a window family/type name that give its operation, so the audit can
    /// suggest a full Type Mark ("ADC-Window-Casement-Full: 3050" suggests "3050 CSMT").
    /// First match wins. Underscores and dashes count as spaces.
    /// </summary>
    public List<OperationKeyword> WindowOperationKeywords { get; set; } = new()
    {
        new("CASEMENT", "CSMT"), new("AWNING", "AWN"), new("DOUBLE HUNG", "DH"), new("SINGLE HUNG", "SH"),
        new("SLID", "SLDR"), new("GLIDER", "SLDR"), new("PICTURE", "FIX"), new("FIXED", "FIX"), new("TRANSOM", "FIX"),
    };

    /// <summary>Same for doors. Swing doors match nothing and get the size only.</summary>
    public List<OperationKeyword> DoorOperationKeywords { get; set; } = new()
    {
        new("POCKET", "PKT"), new("BARN", "BARN"), new("BIFOLD", "BIFOLD"), new("BI FOLD", "BIFOLD"), new("BYPASS", "BYPASS"),
        new("SLIDING", "SLDR"), new("PATIO", "SLDR"), new("FRENCH", "FRNCH"), new("GARAGE", "OH"), new("OVERHEAD", "OH"),
    };

    /// <summary>How far type Width/Height may be from the size code before it's flagged.</summary>
    public double TypeMarkToleranceInches { get; set; } = 0.5;

    // ---- Schedules ----

    public List<string> DoorScheduleFields { get; set; } = new() { "Type Mark", "Count", "Door Swing", "Tempered" };
    public List<string> WindowScheduleFields { get; set; } = new() { "Type Mark", "Count", "Tempered", "Egress" };

    /// <summary>Door and window schedules sort or group by this field first, so rows read like the tags.</summary>
    public string ScheduleGroupField { get; set; } = "Type Mark";

    // ---- Instance parameters ----

    public string SwingParameter { get; set; } = "Door Swing";
    public string TemperedParameter { get; set; } = "Tempered";
    public string EgressParameter { get; set; } = "Egress";

    // ---- Egress (IRC R310) ----

    /// <summary>Room names that need an egress window. Case-insensitive regex.</summary>
    public string BedroomPattern { get; set; } = @"\b(BED(ROOM)?|BUNK( ?ROOM)?|SLEEPING)\b";

    public double EgressMaxSillInches { get; set; } = 44;

    // ---- Tempered candidates (IRC R308.4) ----

    /// <summary>Words in a door family or type name that mean it has glass.</summary>
    public List<string> GlassDoorKeywords { get; set; } = new() { "GLASS", "GLAZED", "SLIDING", "SLIDER", "PATIO", "FRENCH", "SIDELITE", "SIDELIGHT", "LITE" };

    /// <summary>Words in a plumbing fixture name that make a room wet.</summary>
    public List<string> WetFixtureKeywords { get; set; } = new() { "TUB", "SHOWER", "BATH", "SPA", "SAUNA" };

    // ---- Sheets ----

    public string SheetNumberPattern { get; set; } = @"^(CS|[A-Z]{1,2}\d{1,2}(\.\d{1,2})?)$";
    public string CoverSheetNumber { get; set; } = "CS";

    /// <summary>Text the cover sheet must contain somewhere (its own notes or views placed on it). Case-insensitive.</summary>
    public List<string> CoverSheetMustContain { get; set; } = new() { "SCOPE OF WORK", "DO NOT SCALE" };

    // ---- Graphics ----

    /// <summary>Smallest allowed text and dimension height at plot. PPRBD rejects lettering under 1/8".</summary>
    public double MinTextHeightInches { get; set; } = 0.125;

    /// <summary>Plans, elevations and sections smaller than this fail (96 = 1/8" = 1'-0").</summary>
    public int MaxScaleDenominator { get; set; } = 96;

    /// <summary>Smaller than this is a warning (48 = 1/4" = 1'-0").</summary>
    public int PreferredScaleDenominator { get; set; } = 48;

    /// <summary>Views whose name contains any of these skip the scale check (site plans use engineer scales).</summary>
    public List<string> ScaleExemptKeywords { get; set; } = new() { "SITE" };

    /// <summary>
    /// Plan cut height. Windows with the sill at or above it (clerestories, transoms)
    /// don't show on plans, so they need an elevation tag instead of a plan tag.
    /// </summary>
    public double PlanCutHeightInches { get; set; } = 48;

    /// <summary>Office tags exterior doors and windows on elevations too. False makes a missing elevation tag a warning.</summary>
    public bool RequireElevationTags { get; set; } = true;

    /// <summary>Placed views of these types must use a view template.</summary>
    public List<string> TemplateRequiredViewTypes { get; set; } = new()
    {
        "FloorPlan", "CeilingPlan", "EngineeringPlan", "AreaPlan", "Elevation", "Section", "ThreeD", "Schedule",
    };

    // ---- Legends ----

    /// <summary>Legends that list the wall types. Only the ones placed on a sheet are checked.</summary>
    public List<string> WallLegendNames { get; set; } = new() { "WALL LEGEND", "WALL LEGEND ICF" };

    /// <summary>Wall types that are modeling aids, not real walls, so they don't need a legend entry.</summary>
    public List<string> LegendExemptWallTypes { get; set; } = new();

    // ---- Report ----

    /// <summary>Most findings listed per check. The total is always reported.</summary>
    public int MaxFindingsPerCheck { get; set; } = 25;
}

public sealed record OperationKeyword(string Keyword, string Code);
