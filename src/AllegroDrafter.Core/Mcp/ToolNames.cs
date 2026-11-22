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

    public const string ProposeSheets = "propose_sheets";
    public const string ProposeSheetSet = "propose_sheet_set";
    public const string ProposePlaceViews = "propose_place_views";
    public const string ProposeViewTemplates = "propose_view_templates";

    public const string ListProposals = "list_proposals";
    public const string GetProposal = "get_proposal";

    public static readonly IReadOnlySet<string> ProposalTools = new HashSet<string>
    {
        ProposeSheets, ProposeSheetSet, ProposePlaceViews, ProposeViewTemplates,
    };
}
