using System.Text.Json.Nodes;

namespace AllegroDrafter.Core.Mcp;

public sealed record ToolDefinition(string Name, string Title, string Description, string InputSchemaJson, bool ReadOnly)
{
    public JsonObject ToJson() => new()
    {
        ["name"] = Name,
        ["title"] = Title,
        ["description"] = Description,
        ["inputSchema"] = JsonNode.Parse(InputSchemaJson),
        ["annotations"] = new JsonObject
        {
            ["readOnlyHint"] = ReadOnly,
            ["destructiveHint"] = false,
            ["openWorldHint"] = false,
        },
    };
}

/// <summary>
/// The single list of tools. Served by the Revit add-in and by the bridge
/// (so Claude still sees the tools when Revit is closed).
/// </summary>
public static class ToolCatalog
{
    public const string ServerInstructions = """
        You are drafting inside a live Revit project for Allegro Design Co. Rules:
        1. Call get_standards before proposing anything. Use title block, view template and type names EXACTLY as listed there. Never invent names. If what you need is missing, say so and stop.
        2. Read the model (get_project_info, list_levels, list_views, list_sheets) before proposing, so sheet numbers don't collide and views exist.
        3. You never change the model directly. propose_* tools queue a proposal that the designer approves or rejects in Revit. Tell the designer what you proposed and that it is waiting in the Allegro Drafter pane.
        4. Keep each proposal to one reviewable batch (max 50 changes), grouped logically (e.g. "Architectural floor plan sheets").
        5. Use get_proposal to check whether the designer approved, and read the result message if it failed.
        6. Follow the office rules in get_standards (section "manual"), including the typical sheet index for the project type.
        7. Schedules: check list_schedules first; only create what's missing, by copying the office schedule. Add or remove columns with propose_schedule_fields. To fill data, read elements with list_elements and use the exact parameter names it reports.
        8. Finishes: use only codes from manual.finishCodes. If that list is empty, read the finish legend with read_legend, show the designer the codes you found, and ask them to add them to standards.json before proposing finishes.
        9. Never guess door or window marks, room names or finishes the designer hasn't given you a rule for. Ask.
        10. Annotation: tag with propose_tags, dimension plans with propose_dimensions (exterior strings), callouts with propose_callouts, notes with propose_notes. Propose one view or one sheet's worth at a time so the designer can check it.
        11. Coordinates are in feet: model X/Y for plan views, view coordinates for drafting views and legends. Use list_elements / list_rooms to find where things are.
        """;

    private const string TitleRationale = """
        "title": { "type": "string", "description": "Short batch name the designer sees, e.g. 'A-series floor plan sheets'." },
        "rationale": { "type": "string", "description": "One or two sentences on why, shown to the designer under the title." }
        """;

    private const string ViewTypeProp = """
        "viewType": { "type": "string", "description": "Optional. Disambiguates views that share a name, e.g. FloorPlan vs CeilingPlan. Use the viewType value from list_views." }
        """;

    private const string PositionProps = """
        "xFeet": { "type": "number", "description": "Optional sheet X of the viewport center, in feet from the sheet origin. Give with yFeet, or omit both to auto-arrange." },
        "yFeet": { "type": "number", "description": "Optional sheet Y of the viewport center, in feet." }
        """;

    public static readonly IReadOnlyList<ToolDefinition> All = new[]
    {
        new ToolDefinition(ToolNames.GetStandards, "Get office standards",
            "Returns the office standards profile: the ONLY title blocks, view templates, schedules, details, tags and types you may use, plus office rules and typical sheet indexes per project type. Call this first.",
            """
            { "type": "object", "properties": {
                "section": { "type": "string", "enum": ["all","summary","titleBlocks","viewTemplates","viewFamilyTypes","tagTypes","textNoteTypes","dimensionTypes","schedules","scheduleCategories","draftingViews","legends","sheets","modelTypes","manual"],
                             "description": "Which part to return. 'summary' gives counts and the manual rules. Default 'all'." } },
              "additionalProperties": false }
            """, ReadOnly: true),

        new ToolDefinition(ToolNames.GetProjectInfo, "Get project info",
            "Project name, number, client, address, Revit version, whether it is workshared, and counts of sheets, views, levels and rooms in the open model.",
            """{ "type": "object", "properties": {}, "additionalProperties": false }""", ReadOnly: true),

        new ToolDefinition(ToolNames.ListLevels, "List levels",
            "All levels with elevations in feet, lowest first.",
            """{ "type": "object", "properties": {}, "additionalProperties": false }""", ReadOnly: true),

        new ToolDefinition(ToolNames.ListViews, "List views",
            "Views, schedules and legends in the model (not templates) with type, level, scale, current view template and which sheet they are on.",
            """
            { "type": "object", "properties": {
                "viewType": { "type": "string", "description": "Optional filter, e.g. FloorPlan, CeilingPlan, Elevation, Section, Detail, DraftingView, Legend, Schedule, ThreeD." },
                "unplacedOnly": { "type": "boolean", "description": "Only views not yet on any sheet. Default false." } },
              "additionalProperties": false }
            """, ReadOnly: true),

        new ToolDefinition(ToolNames.ListSheets, "List sheets",
            "Existing sheets with number, name, title block and the views placed on each.",
            """{ "type": "object", "properties": {}, "additionalProperties": false }""", ReadOnly: true),

        new ToolDefinition(ToolNames.ListRooms, "List rooms",
            "Rooms with number, name, level and area in square feet. Flags unplaced and unenclosed rooms.",
            """
            { "type": "object", "properties": {
                "level": { "type": "string", "description": "Optional level name filter." } },
              "additionalProperties": false }
            """, ReadOnly: true),

        new ToolDefinition(ToolNames.CountElements, "Count elements by type",
            "Counts walls, doors, windows, floors, roofs, ceilings, stairs, casework and plumbing fixtures by type. Walls include total length in feet. Use it to decide which schedules and details the set needs.",
            """{ "type": "object", "properties": {}, "additionalProperties": false }""", ReadOnly: true),

        new ToolDefinition(ToolNames.ListSchedules, "List schedules",
            "Schedules in the model: name, category, whether it's a key schedule, its fields in order, view template and which sheets it's on.",
            """{ "type": "object", "properties": {}, "additionalProperties": false }""", ReadOnly: true),

        new ToolDefinition(ToolNames.ListElements, "List doors, windows or rooms",
            "Doors, windows or rooms with their element id, label, type, level and parameter values. Use the ids and exact parameter names from here in propose_parameter_values.",
            """
            { "type": "object", "required": ["category"], "properties": {
                "category": { "type": "string", "enum": ["doors","windows","rooms"] },
                "level": { "type": "string", "description": "Optional level name filter." },
                "parameters": { "type": "array", "items": { "type": "string" }, "description": "Optional parameter names to read. Default: doors/windows Mark, Type Mark, Width, Height, Comments (+ from/to room); rooms Number, Name, Area and the finish parameters." },
                "includeParameterList": { "type": "boolean", "description": "Also return every instance parameter name on this category with storage type and read-only flag. Use it to find custom finish parameters. Default false." } },
              "additionalProperties": false }
            """, ReadOnly: true),

        new ToolDefinition(ToolNames.ReadLegend, "Read a legend",
            "Reads the text, symbols and legend components in a legend or drafting view, top to bottom, so you can pull finish codes and descriptions. Omit name to list the legends.",
            """
            { "type": "object", "properties": {
                "name": { "type": "string", "description": "Exact legend or drafting view name." } },
              "additionalProperties": false }
            """, ReadOnly: true),

        new ToolDefinition(ToolNames.ProposeSchedules, "Propose schedules",
            "Proposes creating schedules, by copying an office schedule (preferred) or by category + schedule view template, and optionally placing each on a sheet. Nothing changes until the designer approves.",
            $$"""
            { "type": "object", "required": ["title","schedules"], "properties": {
                {{TitleRationale}},
                "schedules": { "type": "array", "minItems": 1, "items": { "type": "object", "required": ["name"], "properties": {
                    "name": { "type": "string", "description": "New schedule name. Must not already exist." },
                    "sourceSchedule": { "type": "string", "description": "Office schedule to copy, exactly as in get_standards schedules." },
                    "category": { "type": "string", "enum": ["doors","windows","rooms"], "description": "Use with templateName instead of sourceSchedule. Room finish schedules are 'rooms'." },
                    "templateName": { "type": "string", "description": "Schedule view template from get_standards (viewType Schedule)." },
                    "sheetNumber": { "type": "string", "description": "Optional existing sheet to place it on." },
                    {{PositionProps}} }, "additionalProperties": false } } },
              "additionalProperties": false }
            """, ReadOnly: false),

        new ToolDefinition(ToolNames.ProposeParameterValues, "Propose parameter values",
            "Proposes instance parameter values on doors, windows or rooms: marks, room names and numbers, finishes, comments. One entry per element, max 50 elements per proposal. Finish parameters only accept office finish codes. Nothing changes until the designer approves.",
            $$"""
            { "type": "object", "required": ["title","elements"], "properties": {
                {{TitleRationale}},
                "elements": { "type": "array", "minItems": 1, "items": { "type": "object", "required": ["elementId","values"], "properties": {
                    "elementId": { "type": "integer", "description": "Element id from list_elements." },
                    "label": { "type": "string", "description": "Optional. The add-in shows the real label from the model." },
                    "values": { "type": "object", "description": "Parameter name to new value, e.g. { \"Floor Finish\": \"T1\", \"Wall Finish\": \"P1/T2\" }. Use \"\" to clear.",
                                "additionalProperties": { "type": ["string","number","boolean"] } } },
                  "additionalProperties": false } } },
              "additionalProperties": false }
            """, ReadOnly: false),

        new ToolDefinition(ToolNames.ProposeTags, "Propose tags",
            "Proposes tagging every untagged door, window, room or wall visible in a view, using an office tag type (or the project default). Rooms tag in plan views only. Nothing changes until the designer approves.",
            $$"""
            { "type": "object", "required": ["title","views"], "properties": {
                {{TitleRationale}},
                "views": { "type": "array", "minItems": 1, "items": { "type": "object", "required": ["viewName","category"], "properties": {
                    "viewName": { "type": "string" },
                    {{ViewTypeProp}},
                    "category": { "type": "string", "enum": ["doors","windows","rooms","walls"] },
                    "tagType": { "type": "string", "description": "Exact tag type from get_standards tagTypes ('Family: Type'). Omit for the project default." },
                    "leader": { "type": "boolean", "description": "Default false." },
                    "untaggedOnly": { "type": "boolean", "description": "Skip elements already tagged in this view. Default true." } },
                  "additionalProperties": false } } },
              "additionalProperties": false }
            """, ReadOnly: false),

        new ToolDefinition(ToolNames.ProposeDimensions, "Propose exterior dimensions",
            "Proposes rule-based exterior dimension strings on a floor plan, in tiers from the building out: 'openings' (door/window centerlines), 'walls' (every jog), 'overall'. Straight walls only; angled walls are reported, not guessed. Refuses views that already have dimensions unless allowExisting is true.",
            $$"""
            { "type": "object", "required": ["title","views"], "properties": {
                {{TitleRationale}},
                "views": { "type": "array", "minItems": 1, "items": { "type": "object", "required": ["viewName"], "properties": {
                    "viewName": { "type": "string", "description": "A floor plan view." },
                    {{ViewTypeProp}},
                    "strategies": { "type": "array", "items": { "type": "string", "enum": ["openings","walls","overall"] }, "description": "Default all three." },
                    "dimensionType": { "type": "string", "description": "Exact name from get_standards dimensionTypes, e.g. 'Linear: Arch 3/32\"'. Omit for the project default." },
                    "offsetFeet": { "type": "number", "description": "Model feet from the building face to the first string. Default 4." },
                    "stepFeet": { "type": "number", "description": "Model feet between tiers. Default 2." },
                    "allowExisting": { "type": "boolean", "description": "Add even if the view already has dimensions. Default false." } },
                  "additionalProperties": false } } },
              "additionalProperties": false }
            """, ReadOnly: false),

        new ToolDefinition(ToolNames.ProposeCallouts, "Propose callouts",
            "Proposes callouts in plan views. Detail callout: a new enlarged view (name + viewFamilyType), optional template, scale and sheet. Reference callout: points at an existing view such as a standard detail (referenceView). Place it around a room, around elements, or a plan rectangle.",
            $$"""
            { "type": "object", "required": ["title","callouts"], "properties": {
                {{TitleRationale}},
                "callouts": { "type": "array", "minItems": 1, "items": { "type": "object", "required": ["parentView"], "properties": {
                    "parentView": { "type": "string", "description": "Plan view to draw the callout in." },
                    "parentViewType": { "type": "string" },
                    "name": { "type": "string", "description": "New view name (detail callout)." },
                    "viewFamilyType": { "type": "string", "description": "Exact view type from get_standards viewFamilyTypes (Detail or FloorPlan family)." },
                    "referenceView": { "type": "string", "description": "Existing view to reference instead of creating one." },
                    "aroundRoomNumber": { "type": "string" },
                    "aroundElementIds": { "type": "array", "items": { "type": "integer" } },
                    "minX": { "type": "number" }, "minY": { "type": "number" }, "maxX": { "type": "number" }, "maxY": { "type": "number" },
                    "marginFeet": { "type": "number", "description": "Space around the room/elements. Default 1." },
                    "templateName": { "type": "string" },
                    "scale": { "type": "integer", "description": "e.g. 24 for 1/2\" = 1'-0\"." },
                    "sheetNumber": { "type": "string", "description": "Optional sheet to place the new callout view on." },
                    {{PositionProps}} }, "additionalProperties": false } } },
              "additionalProperties": false }
            """, ReadOnly: false),

        new ToolDefinition(ToolNames.ProposeNotes, "Propose notes",
            "Proposes text notes in plan, drafting or legend views, optionally with a leader to an element. Use office text types only.",
            $$"""
            { "type": "object", "required": ["title","notes"], "properties": {
                {{TitleRationale}},
                "notes": { "type": "array", "minItems": 1, "items": { "type": "object", "required": ["viewName","text","textType","xFeet","yFeet"], "properties": {
                    "viewName": { "type": "string" },
                    {{ViewTypeProp}},
                    "text": { "type": "string", "description": "Use \n for line breaks." },
                    "textType": { "type": "string", "description": "Exact name from get_standards textNoteTypes." },
                    "xFeet": { "type": "number" }, "yFeet": { "type": "number" },
                    "leaderToElementId": { "type": "integer", "description": "Optional element to point the leader at (plan views)." } },
                  "additionalProperties": false } } },
              "additionalProperties": false }
            """, ReadOnly: false),

        new ToolDefinition(ToolNames.ProposeScheduleFields, "Propose schedule columns",
            "Proposes adding or removing columns (fields) on an existing schedule, e.g. add Mark after Type to the door schedule. Field names are the schedulable parameter names (list_elements includeParameterList shows them). Nothing changes until the designer approves.",
            $$"""
            { "type": "object", "required": ["title","schedules"], "properties": {
                {{TitleRationale}},
                "schedules": { "type": "array", "minItems": 1, "items": { "type": "object", "required": ["schedule"], "properties": {
                    "schedule": { "type": "string", "description": "Exact schedule name from list_schedules." },
                    "add": { "type": "array", "items": { "type": "object", "required": ["field"], "properties": {
                        "field": { "type": "string" },
                        "after": { "type": "string", "description": "Existing (or just-added) column to insert after. Omit to add at the end." } },
                      "additionalProperties": false } },
                    "remove": { "type": "array", "items": { "type": "string" } } },
                  "additionalProperties": false } } },
              "additionalProperties": false }
            """, ReadOnly: false),

        new ToolDefinition(ToolNames.ProposeSheetSet, "Propose sheets with views",
            "Proposes new sheets AND the views to place on them, as one batch for the designer to approve. Nothing changes until they approve in Revit. Preferred tool for sheet setup.",
            $$"""
            { "type": "object", "required": ["title","sheets"], "properties": {
                {{TitleRationale}},
                "sheets": { "type": "array", "minItems": 1, "items": { "type": "object", "required": ["number","name","titleBlock"], "properties": {
                    "number": { "type": "string", "description": "Sheet number, e.g. A2.1. Must not already exist." },
                    "name": { "type": "string", "description": "Sheet name, e.g. MAIN FLOOR PLAN." },
                    "titleBlock": { "type": "string", "description": "Exact title block name from get_standards ('Family: Type')." },
                    "views": { "type": "array", "items": { "type": "object", "required": ["viewName"], "properties": {
                        "viewName": { "type": "string", "description": "Exact view, legend or schedule name from list_views." },
                        {{ViewTypeProp}},
                        {{PositionProps}} }, "additionalProperties": false } } },
                  "additionalProperties": false } } },
              "additionalProperties": false }
            """, ReadOnly: false),

        new ToolDefinition(ToolNames.ProposeSheets, "Propose empty sheets",
            "Proposes new empty sheets for the designer to approve. Use propose_sheet_set if views go on them too.",
            $$"""
            { "type": "object", "required": ["title","sheets"], "properties": {
                {{TitleRationale}},
                "sheets": { "type": "array", "minItems": 1, "items": { "type": "object", "required": ["number","name","titleBlock"], "properties": {
                    "number": { "type": "string" },
                    "name": { "type": "string" },
                    "titleBlock": { "type": "string", "description": "Exact title block name from get_standards." } },
                  "additionalProperties": false } } },
              "additionalProperties": false }
            """, ReadOnly: false),

        new ToolDefinition(ToolNames.ProposePlaceViews, "Propose placing views",
            "Proposes placing existing views, legends or schedules on existing sheets. Views (other than legends and schedules) can only be on one sheet.",
            $$"""
            { "type": "object", "required": ["title","placements"], "properties": {
                {{TitleRationale}},
                "placements": { "type": "array", "minItems": 1, "items": { "type": "object", "required": ["sheetNumber","viewName"], "properties": {
                    "sheetNumber": { "type": "string" },
                    "viewName": { "type": "string" },
                    {{ViewTypeProp}},
                    {{PositionProps}} }, "additionalProperties": false } } },
              "additionalProperties": false }
            """, ReadOnly: false),

        new ToolDefinition(ToolNames.ProposeViewTemplates, "Propose view templates",
            "Proposes assigning office view templates to views. The template must fit the view type.",
            $$"""
            { "type": "object", "required": ["title","assignments"], "properties": {
                {{TitleRationale}},
                "assignments": { "type": "array", "minItems": 1, "items": { "type": "object", "required": ["viewName","templateName"], "properties": {
                    "viewName": { "type": "string" },
                    {{ViewTypeProp}},
                    "templateName": { "type": "string", "description": "Exact view template name from get_standards." } },
                  "additionalProperties": false } } },
              "additionalProperties": false }
            """, ReadOnly: false),

        new ToolDefinition(ToolNames.ListProposals, "List proposals",
            "Proposals made this Revit session and their status: pending, applying, applied, rejected or failed.",
            """
            { "type": "object", "properties": {
                "status": { "type": "string", "enum": ["pending","applying","applied","rejected","failed"] } },
              "additionalProperties": false }
            """, ReadOnly: true),

        new ToolDefinition(ToolNames.GetProposal, "Get proposal",
            "One proposal with its changes, status and the result message (why it failed, or what was created).",
            """
            { "type": "object", "required": ["id"], "properties": { "id": { "type": "string" } }, "additionalProperties": false }
            """, ReadOnly: true),
    };

    public static ToolDefinition? Find(string name) => All.FirstOrDefault(t => t.Name == name);
}
