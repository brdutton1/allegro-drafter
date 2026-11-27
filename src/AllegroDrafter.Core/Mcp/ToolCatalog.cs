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
        0. Scope (office rule): drafting, tagging and checking schedules, plus the model-data fixes below. Never touch the office template. Never apply, clear or suggest view templates; leave view templates to the designer.
        1. Call get_standards before proposing anything. Use title block and type names EXACTLY as listed there. Never invent names. If what you need is missing, say so and stop.
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
        12. Before issue, or when asked what's wrong with the set, run audit_set. Report failures first, in plain words. Offer fixes as proposals, never fix silently. A size code is the nominal opening (3050 = 3'-0" W x 5'-0" H). Only use a size code the audit derived from the type's real size (the finding's "suggestion"), never a guess.
        13. Fixing a set ("make it permit-ready", "fix the audit"): one batch per step, each waiting for approval before the next, re-running audit_set after each:
            0. propose_wall_function: every wall-function finding that has a suggestion, before tags or dimensions. Warnings without a suggestion need the designer's call; ask.
            a. propose_parameters: Tempered and Egress (yesno) on doors and windows, if opening-parameters says they're missing.
            b. propose_type_marks: every type-marks / operation-codes finding that has a suggestion. Findings without one need the designer's call; ask.
            c. propose_schedule_fields then propose_schedule_sort: required columns, sorted by Type Mark (doors: then Door Swing), itemized off.
            d. propose_parameter_values: Tempered = Yes on glass doors the tempered check failed. Window tempered/egress warnings and door swings need the designer's call; list them and ask.
            e. propose_room_cleanup with allUnplaced, only after telling the designer how many rooms it deletes.
            f. propose_tags and propose_dimensions for what's left.
           Template problems (text heights, wall legend, view templates) are office decisions. Report them, don't patch them per project.
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
            "Returns the office standards profile: the ONLY title blocks, schedules, details, tags and types you may use (view templates are listed for reference only; never apply them), plus office rules and typical sheet indexes per project type. Call this first.",
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

        new ToolDefinition(ToolNames.ReadDimensions, "Read a view's dimensions",
            "Read-only. Every dimension string in a plan view, so you can learn how the designer dimensions: the side of the building it's on (model direction and where it sits on the sheet), its order out from the building and distance from the wall face, its dimension type, its segment values, and for every reference what it measures to (which wall layer face: finish, sheathing, face of stud/core; door or window centerline or edge; grid; level; reference plane). Also the view's rotation and its viewport rotation on the sheet. Changes nothing.",
            $$"""
            { "type": "object", "required": ["viewName"], "properties": {
                "viewName": { "type": "string" },
                {{ViewTypeProp}} },
              "additionalProperties": false }
            """, ReadOnly: true),

        new ToolDefinition(ToolNames.GetFinishPlan, "Preview Finish Drafting",
            "Read-only. What the Finish Drafting button would do next on this model: the step, and the exact batch it would queue or the questions it would ask. Uses the live run's answers if a run is going in Revit. Changes nothing. The designer runs it from Allegro > Finish Drafting.",
            """
            { "type": "object", "properties": {}, "additionalProperties": false }
            """, ReadOnly: true),

        new ToolDefinition(ToolNames.AuditSet, "Audit the plan set",
            "Read-only punch list before issue. Checks size-code Type Marks against door/window sizes, operation codes, door/window schedules (Type Mark, Count, swing, tempered, egress), tags, wall legend vs modeled walls, sheet numbers and index, view scales, text heights (1/8\" min), rooms, view templates, bedroom egress (R310), tempered glazing candidates (R308.4), hand-typed detail references and the cover sheet. Returns pass/warn/fail per check with element ids. Changes nothing. Rules come from get_standards manual.audit.",
            """
            { "type": "object", "properties": {
                "checks": { "type": "array", "items": { "type": "string", "enum": ["type-marks","operation-codes","schedules","opening-parameters","tags","wall-function","wall-legend","sheets","view-scales","text-heights","rooms","door-rooms","view-templates","egress","tempered","references","cover-sheet"] },
                            "description": "Optional. Run only these checks. Default: all." } },
              "additionalProperties": false }
            """, ReadOnly: true),

        new ToolDefinition(ToolNames.ProposeSchedules, "Propose schedules",
            "Proposes creating schedules by copying an office schedule, and optionally placing each on a sheet. Never with a view template (office rule). Nothing changes until the designer approves.",
            $$"""
            { "type": "object", "required": ["title","schedules"], "properties": {
                {{TitleRationale}},
                "schedules": { "type": "array", "minItems": 1, "items": { "type": "object", "required": ["name","sourceSchedule"], "properties": {
                    "name": { "type": "string", "description": "New schedule name. Must not already exist." },
                    "sourceSchedule": { "type": "string", "description": "Office schedule to copy, exactly as in get_standards schedules." },
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

        new ToolDefinition(ToolNames.ProposeDimensions, "Propose plan dimensions",
            "Proposes rule-based dimension strings on a floor plan. Exterior tiers from the building out: 'columns' (porch and exterior column centerlines, only on sides that have them), 'openings' (door/window centerlines), 'walls' (every jog), 'overall'. Walls measure to the outside of finish. 'interior': strings that locate every interior wall, chained to the exterior faces, never running along a wall. Walls are sorted by the way their exterior faces, so L and U plans come out right. Straight walls only; angled walls are reported, not guessed. Refuses views that already have dimensions unless allowExisting is true.",
            $$"""
            { "type": "object", "required": ["title","views"], "properties": {
                {{TitleRationale}},
                "views": { "type": "array", "minItems": 1, "items": { "type": "object", "required": ["viewName"], "properties": {
                    "viewName": { "type": "string", "description": "A floor plan view." },
                    {{ViewTypeProp}},
                    "strategies": { "type": "array", "items": { "type": "string", "enum": ["columns","openings","walls","overall","interior"] }, "description": "Default all five. columns: porch/exterior column centerlines, nearest the columns, on sides that have them." },
                    "interiorBasis": { "type": "string", "enum": ["face","center"], "description": "Interior walls: the face toward the exterior wall the string starts from (office rule, default) or centerlines." },
                    "openingsTo": { "type": "string", "enum": ["centerline","edges"], "description": "Doors and windows: centerlines (office rule, default) or rough opening edges." },
                    "wallsTo": { "type": "string", "enum": ["finishFace","coreFace"], "description": "Exterior walls: outside of finish (office rule, default) or face of stud." },
                    "sides": { "type": "array", "items": { "type": "string", "enum": ["top","bottom","left","right","north","south","east","west"] }, "description": "Only these exterior sides. top/bottom/left/right are as the view sits on the sheet; north/south/east/west are model directions. Leave out for every side. Interior strings are drawn only if 'interior' is in strategies. Use with allowExisting to fill in one side the designer cleared." },
                    "dimensionType": { "type": "string", "description": "Exact name from get_standards dimensionTypes, e.g. 'Linear: Arch 3/32\"'. Omit for the project default." },
                    "offsetPaperInches": { "type": "number", "description": "Paper inches from the outside face to the first string. Office default 3/8 (0.375)." },
                    "stepPaperInches": { "type": "number", "description": "Paper inches between tiers. Office default 3/8 (0.375)." },
                    "offsetFeet": { "type": "number", "description": "Model feet instead of paper inches. Only if the designer asks." },
                    "stepFeet": { "type": "number", "description": "Model feet instead of paper inches. Only if the designer asks." },
                    "allowExisting": { "type": "boolean", "description": "Add even if the view already has dimensions. Default false." } },
                  "additionalProperties": false } } },
              "additionalProperties": false }
            """, ReadOnly: false),

        new ToolDefinition(ToolNames.ProposeCallouts, "Propose callouts",
            "Proposes callouts in plan views. Detail callout: a new enlarged view (name + viewFamilyType), optional scale and sheet; no view template (office rule). Reference callout: points at an existing view such as a standard detail (referenceView). Place it around a room, around elements, or a plan rectangle.",
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
                        "after": { "type": "string", "description": "Existing (or just-added) column to insert after. Omit both after and before to add at the end." },
                        "before": { "type": "string", "description": "Existing (or just-added) column to insert before, e.g. put Type Mark before Width to make it the first column." } },
                      "additionalProperties": false } },
                    "remove": { "type": "array", "items": { "type": "string" } } },
                  "additionalProperties": false } } },
              "additionalProperties": false }
            """, ReadOnly: false),

        new ToolDefinition(ToolNames.ProposeTypeMarks, "Propose Type Marks",
            "Proposes the Type Mark (size code + operation, e.g. \"3050 CSMT\", \"2868\") on door and window types, the value tags and schedules read. Changes every placed instance of the type. Refused unless it's a size code that matches the type's real Width/Height, uses an office operation code, and doesn't duplicate another type's mark. Use the suggestion from audit_set findings. Nothing changes until the designer approves.",
            $$"""
            { "type": "object", "required": ["title","types"], "properties": {
                {{TitleRationale}},
                "types": { "type": "array", "minItems": 1, "items": { "type": "object", "required": ["category","typeName","typeMark"], "properties": {
                    "category": { "type": "string", "enum": ["doors","windows"] },
                    "typeName": { "type": "string", "description": "\"Family: Type\" exactly as audit_set or count_elements reports it." },
                    "typeMark": { "type": "string", "description": "New Type Mark, e.g. \"3080\" or \"6060 CSMT\"." },
                    "sizeOverride": { "type": "string", "description": "ONLY when the designer told you to use a size that doesn't match the family's Width/Height. Their reason, in their words. Skips the size check and is shown in the pane." } },
                  "additionalProperties": false } } },
              "additionalProperties": false }
            """, ReadOnly: false),

        new ToolDefinition(ToolNames.ProposeParameters, "Propose project parameters",
            "Proposes adding instance parameters (e.g. Tempered and Egress, Yes/No) to doors, windows or rooms, from the Allegro Drafter shared parameter file so schedules and tags can use them. Refused if a parameter with that name already exists. Nothing changes until the designer approves.",
            $$"""
            { "type": "object", "required": ["title","parameters"], "properties": {
                {{TitleRationale}},
                "parameters": { "type": "array", "minItems": 1, "items": { "type": "object", "required": ["name","categories"], "properties": {
                    "name": { "type": "string" },
                    "type": { "type": "string", "enum": ["yesno","text"], "description": "Default yesno." },
                    "categories": { "type": "array", "items": { "type": "string", "enum": ["doors","windows","rooms"] } } },
                  "additionalProperties": false } } },
              "additionalProperties": false }
            """, ReadOnly: false),

        new ToolDefinition(ToolNames.ProposeScheduleSort, "Propose schedule sorting",
            "Proposes the sort/group order of a schedule (replaces the current order), itemized on/off, and group headers. Office door schedule: sortBy [\"Type Mark\", \"From Room: Name\"], itemized true, groupHeaders true gives a size-code header with a count and one line per door below it. itemized false gives one row per size code with a Count. Every sortBy field must already be a column; add missing ones first with propose_schedule_fields and wait for approval. Nothing changes until the designer approves.",
            $$"""
            { "type": "object", "required": ["title","schedules"], "properties": {
                {{TitleRationale}},
                "schedules": { "type": "array", "minItems": 1, "items": { "type": "object", "required": ["schedule","sortBy"], "properties": {
                    "schedule": { "type": "string", "description": "Exact schedule name from list_schedules." },
                    "sortBy": { "type": "array", "minItems": 1, "items": { "type": "string" }, "description": "Column names, first sort first." },
                    "itemized": { "type": "boolean", "description": "Optional. false = one row per group with Count; true = one row per element." },
                    "groupHeaders": { "type": "boolean", "description": "Optional. true = a header row and a count footer for each value of the first sortBy field (use with itemized true)." } },
                  "additionalProperties": false } } },
              "additionalProperties": false }
            """, ReadOnly: false),

        new ToolDefinition(ToolNames.ProposeWallFunction, "Propose wall type Function",
            "Proposes the Function (Interior, Exterior, Foundation, Retaining, Soffit, CoreShaft) of wall types. A door or window is exterior only when its host wall type is Exterior, so this fixes interior doors read as exterior (tags check, elevation tags, exterior dimensions). Use the audit's wall-function suggestions. Changes every wall of the type. Nothing changes until the designer approves.",
            $$"""
            { "type": "object", "required": ["title","wallTypes"], "properties": {
                {{TitleRationale}},
                "wallTypes": { "type": "array", "minItems": 1, "items": { "type": "object", "required": ["typeName","function"], "properties": {
                    "typeName": { "type": "string", "description": "\"Basic Wall: Interior 2x6 Wall (INSULATION)\", exactly as audit_set reports it." },
                    "function": { "type": "string", "enum": ["Interior","Exterior","Foundation","Retaining","Soffit","CoreShaft"] } },
                  "additionalProperties": false } } },
              "additionalProperties": false }
            """, ReadOnly: false),

        new ToolDefinition(ToolNames.ProposeRoomCleanup, "Propose room cleanup",
            "Proposes deleting rooms that aren't placed on a plan or aren't enclosed (leftovers that clutter schedules). Placed rooms are always refused. allUnplaced lists every one, and the designer sees the exact list before approving. Nothing changes until the designer approves.",
            $$"""
            { "type": "object", "required": ["title"], "properties": {
                {{TitleRationale}},
                "roomIds": { "type": "array", "items": { "type": "integer" }, "description": "Room element ids from audit_set or list_rooms." },
                "allUnplaced": { "type": "boolean", "description": "Delete every unplaced and unenclosed room instead of a list." } },
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

        new ToolDefinition(ToolNames.ListProposals, "List proposals",
            "Proposals made this Revit session and their status: pending, applying, applied, rejected or failed.",
            """
            { "type": "object", "properties": {
                "status": { "type": "string", "enum": ["pending","applying","applied","rejected","failed","undone"] } },
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
