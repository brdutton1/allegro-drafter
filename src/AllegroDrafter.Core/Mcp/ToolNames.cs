namespace AllegroDrafter.Core.Mcp;

public static class ToolNames
{
    public const string GetStandards = "get_standards";
    public const string GetProjectInfo = "get_project_info";
    public const string ListLevels = "list_levels";
    public const string ListViews = "list_views";
    public const string ListSheets = "list_sheets";
    public const string ListRooms = "list_rooms";
    public const string CountElements = "count_elements";
    public const string ListSchedules = "list_schedules";
    public const string ListElements = "list_elements";
    public const string ReadLegend = "read_legend";
    public const string AuditSet = "audit_set";

    public const string ProposeSheets = "propose_sheets";
    public const string ProposeSheetSet = "propose_sheet_set";
    public const string ProposePlaceViews = "propose_place_views";
    public const string ProposeViewTemplates = "propose_view_templates";
    public const string ProposeSchedules = "propose_schedules";
    public const string ProposeParameterValues = "propose_parameter_values";
    public const string ProposeTags = "propose_tags";
    public const string ProposeCallouts = "propose_callouts";
    public const string ProposeNotes = "propose_notes";
    public const string ProposeDimensions = "propose_dimensions";
    public const string ProposeScheduleFields = "propose_schedule_fields";
    public const string ProposeTypeMarks = "propose_type_marks";
    public const string ProposeParameters = "propose_parameters";
    public const string ProposeScheduleSort = "propose_schedule_sort";
    public const string ProposeRoomCleanup = "propose_room_cleanup";

    public const string ListProposals = "list_proposals";
    public const string GetProposal = "get_proposal";

    /// <summary>Categories Claude may schedule and edit in this phase.</summary>
    public static readonly IReadOnlyList<string> EditableCategories = new[] { "doors", "windows", "rooms" };

    /// <summary>Categories Claude may tag.</summary>
    public static readonly IReadOnlyList<string> TaggableCategories = new[] { "doors", "windows", "rooms", "walls" };

    public static readonly IReadOnlySet<string> ProposalTools = new HashSet<string>
    {
        ProposeSheets, ProposeSheetSet, ProposePlaceViews, ProposeViewTemplates,
        ProposeSchedules, ProposeParameterValues,
        ProposeTags, ProposeCallouts, ProposeNotes, ProposeDimensions, ProposeScheduleFields,
        ProposeTypeMarks, ProposeParameters, ProposeScheduleSort, ProposeRoomCleanup,
    };

    /// <summary>Categories a new shared parameter may be bound to.</summary>
    public static readonly IReadOnlyList<string> ParameterCategories = new[] { "doors", "windows", "rooms" };
}
