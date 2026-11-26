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

    /// <summary>
    /// Operation codes allowed after the size on doors. Swing doors carry the size only.
    /// EXT marks an exterior swing door that shares its size with an interior one ("3080 EXT"),
    /// so the two get separate schedule rows.
    /// </summary>
    public List<string> DoorOperations { get; set; } = new() { "PKT", "BARN", "BIFOLD", "BYPASS", "SLDR", "FRNCH", "OH", "SHWR", "EXT" };

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

    /// <summary>
    /// Office door schedule: size code, where it goes, swing, tempered. Count can be a column, or
    /// the footer count under each size code when the schedule lists one door per line.
    /// </summary>
    public List<string> DoorScheduleFields { get; set; } = new() { "Type Mark", "Count", "From Room: Name", "To Room: Name", "Door Swing", "Tempered" };
    public List<string> WindowScheduleFields { get; set; } = new() { "Type Mark", "Count", "Tempered", "Egress" };

    /// <summary>Door and window schedules sort or group by this field first, so rows read like the tags.</summary>
    public string ScheduleGroupField { get; set; } = "Type Mark";

    // ---- Instance parameters ----

    public string SwingParameter { get; set; } = "Door Swing";
    public string TemperedParameter { get; set; } = "Tempered";
    public string EgressParameter { get; set; } = "Egress";

    /// <summary>
    /// Where the designer writes why an opening is Tempered = No despite looking like a
    /// hazardous location ("closet 3' deep or less"). First one with text wins. A No
    /// with a reason is a decision and clears the warning; a No without one doesn't.
    /// </summary>
    public List<string> TemperedReasonParameters { get; set; } = new() { "Tempered Reason", "Comments" };

    // ---- Finish Drafting rules ----

    /// <summary>Text parameter the room tag reads for the room's size ("14'-1\" x 11'-4\"").</summary>
    public string RoomSizeParameter { get; set; } = "Room Size";

    /// <summary>Room numbering starts at the first of these it finds on a level, then goes clockwise.</summary>
    public List<string> EntryRoomNames { get; set; } = new() { "ENTRY", "FOYER" };

    /// <summary>
    /// Level name to the room number prefix (basement 0, main 1, upper 2). Levels not listed are
    /// read from their name (BASEMENT/LOWER = 0, MAIN/FIRST = 1, UPPER/SECOND = 2), or asked.
    /// </summary>
    public Dictionary<string, int> LevelNumberPrefixes { get; set; } = new();

    /// <summary>Door swing value for doors that don't swing, by Type Mark operation code.</summary>
    public Dictionary<string, string> SwingValueByOperation { get; set; } = new()
    {
        ["PKT"] = "POCKET", ["SLDR"] = "SLIDING", ["BYPASS"] = "SLIDING", ["BARN"] = "SLIDING",
    };

    /// <summary>Operation codes that are still hinged doors, so handing applies. A size-only Type Mark is a swing door too.</summary>
    public List<string> SwingOperations { get; set; } = new() { "EXT", "SHWR", "FRNCH" };

    /// <summary>How the office's door families read in Revit: the leaf swings toward FacingOrientation.</summary>
    public bool DoorSwingsTowardFacing { get; set; } = true;

    /// <summary>How the office's door families read in Revit: HandOrientation points from the door center to the hinges.</summary>
    public bool DoorHandPointsToHinge { get; set; } = true;

    /// <summary>Door families whose handing reads backwards (found on a spot check). LH and RH swap for these.</summary>
    public List<string> HandingFlipFamilies { get; set; } = new();

    /// <summary>Door schedule lists one line per door under a size-code header with a count (office layout).</summary>
    public bool DoorScheduleOneLinePerDoor { get; set; } = true;

    /// <summary>Window operations that count as an egress opening in a bedroom. Office rule: every bedroom casement.</summary>
    public List<string> EgressOperations { get; set; } = new() { "CSMT" };

    /// <summary>The sheet list placed on the cover as the sheet index.</summary>
    public string SheetIndexSchedule { get; set; } = "DRAWING INDEX";

    /// <summary>Kept so older standards files still load. Unused: the drafter doesn't apply view templates.</summary>
    public List<ViewTemplateRule> ViewTemplateRules { get; set; } = new();

    /// <summary>
    /// Checks the audit skips unless asked for by name. view-templates is off by default: view
    /// templates are the designer's call (office rule, 2026-11-26), so missing ones aren't a failure.
    /// </summary>
    public List<string> DisabledChecks { get; set; } = new() { "view-templates" };

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

    // ---- Wall Function ----

    /// <summary>
    /// Words in a wall type name that mean it's an interior wall. A type named like this
    /// with Function = Exterior makes every door and window in it read as exterior. Whole words.
    /// </summary>
    public List<string> InteriorWallKeywords { get; set; } = new() { "Interior", "Int", "Partition" };

    /// <summary>Words in a wall type name that mean it's an exterior wall.</summary>
    public List<string> ExteriorWallKeywords { get; set; } = new() { "Exterior", "Ext" };

    /// <summary>Wall types that are modeling aids, not real walls, so they don't need a legend entry.</summary>
    public List<string> LegendExemptWallTypes { get; set; } = new();

    // ---- Report ----

    /// <summary>Most findings listed per check. The total is always reported.</summary>
    public int MaxFindingsPerCheck { get; set; } = 25;
}

public sealed record OperationKeyword(string Keyword, string Code);

public sealed record ViewTemplateRule(string ViewType, string? NameContains, string Template);
