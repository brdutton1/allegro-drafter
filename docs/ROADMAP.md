# Roadmap

End state and phase order: [PRODUCT-PLAN.md](PRODUCT-PLAN.md). Design decisions: [adr/](adr/).

## Phase 0: Standards harvest
- [x] Export Standards command (title blocks, view templates, view types, tags, text, dimensions, schedules, drafting views, legends, sheets, wall/floor/roof/ceiling/door/window types, keynote file)
- [x] Hand-edited `manual` section that survives re-exports (rules, sheet index per project type, title block insets)
- [ ] Run it on the office template and review the output (Bryan)
- [ ] Read 5-10 issued PDF plan sets with Claude and write the typical sheet index per project type into `manual.sheetIndexByProjectType`
- [ ] Diff tool for template drift between exports

## Phase 1: Skeleton + sheet builder (built, not yet run in Revit)
- [x] Ribbon tab, dockable approval pane, status dialog
- [x] ExternalEvent dispatcher with timeouts
- [x] Localhost MCP server with token auth, stdio bridge for Claude Desktop / Code
- [x] Read tools: project info, levels, views, sheets, rooms, element counts
- [x] Proposals: create sheets, place views/legends/schedules (auto-arrange or exact), apply view templates
- [x] Standards and model validation, one-transaction apply, double-click safe, retry on failure
- [x] Audit log
- [x] Pending proposals saved to disk; crash mid-apply comes back as Failed, never re-applied
- [x] Drafting workspace with DRAFTING-LOG.md, resume shortcut, updates without closing Claude Code
- [x] Automated tests on Core (113 after Phase 2); add-in compiles against Revit 2025 and 2026 APIs
- [ ] First run inside Revit on a stripped copy of a finished job
- [ ] Ribbon icons
- [ ] Highlight affected elements before approving (preview)
- [ ] Workshared models: check element ownership before proposing

## Phase 2: Schedules + details (schedules built, not yet run in Revit)
- [x] list_schedules, list_elements (doors/windows/rooms with parameters), read_legend
- [x] Create schedules by copying office schedules, or category + schedule view template, and place them on sheets
- [x] Set door/window/room instance parameters (marks, names, numbers, finishes). Type parameters refused.
- [x] Finish codes from the legend enforced on finish parameters
- [x] Add/remove/position schedule columns
- [ ] First run inside Revit on a copy of a finished job
- [ ] Framing schedules
- [ ] Place drafting-view details based on wall, roof and foundation types in the model
- [ ] Detail callouts and sheet references

## Phase 3: Annotation (built, not yet run in Revit)
- [x] Tag untagged doors / windows / rooms / walls in a view (office tag types or project default)
- [x] Text notes with leaders (office text types). Code notes go through this.
- [x] Detail callouts around a room, elements or a rectangle, with template, scale and sheet placement
- [x] Reference callouts to existing standard detail views
- [x] Exterior plan dimensions, three tiers (openings / wall jogs / overall). Geometry planner tested in Core.
- [ ] First run inside Revit on a copy of a finished job
- [ ] Tag collision avoidance (tags currently sit on the element center)
- [ ] Interior dimensions (room-to-room, wall-to-wall)
- [ ] Callouts in sections and elevations
- [ ] Keynotes
- [ ] Angled and curved walls in dimensions

## Phase 4: Plan set QA (audit runs in Revit 2026; fixes built, not yet run)
Research behind it: [research/buildable-plan-sets.md](research/buildable-plan-sets.md).
- [x] `audit_set`: read-only punch list, 17 checks, rules in `manual.audit`, logic in Core with tests
  - size-code Type Marks vs geometry, operation codes, duplicate marks
  - door/window schedules report from Type Mark with Count, swing, Tempered, Egress
  - tags on placed plans and elevations, wall legend vs modeled walls
  - sheet numbers, names, empty sheets, sheet index, cover sheet contents
  - view scales, 1/8" min text and dimensions, view templates
  - rooms, doors without rooms, bedroom egress (R310), tempered candidates (R308.4)
  - hand-typed detail references
- [x] First run inside Revit on a copy of a finished job (2026-11-21: 1.4 s, findings verified; clerestory false alarm fixed)
- [x] Audit findings carry a ready-to-propose fix (`target` + `suggestion`), operation read from the family name, collisions withheld
- [x] Fix proposals: `propose_type_marks` (size-code checked against geometry, no duplicate marks), `propose_parameters` (Tempered/Egress from an Allegro shared parameter file), `propose_schedule_sort` (sort/group + itemized), `propose_room_cleanup` (unplaced/unenclosed only)
- [x] First run of the fix proposals inside Revit (2026-11-21, 25-0083: parameters, Type Marks, schedule columns/sort, tempered/egress values, room cleanup)
- [x] `wall-function` gate and `propose_wall_function` (interior wall types set to Exterior made interior doors read as exterior)
- [x] Undo awareness: Ctrl+Z on a proposal marks it Undone, redo marks it Applied
- [x] Text heights show the real size when just under 1/8"; Tempered = No with a reason in Comments clears the warning
- [x] Finish Drafting button ([ADR 0001](adr/0001-finish-drafting.md)): rules-only planner (11 steps), run loop, run panel with question cards and Show, one batch visible at a time, delete confirm screen, `get_finish_plan` preview tool. Built, not yet run in Revit
- [ ] First full Finish Drafting run on 25-0083; spot-check door handing and lock the convention in `manual.audit`
- [ ] Door schedule group headers with a count per size code (built; needs the install)
- [ ] Manufacturer data per type (RO, net clear W/H, U-factor, SHGC), then RO in schedules and egress by net clear area
- [ ] Dimension strings close to their overall, no gaps
- [ ] Bearing walls stack between levels
- [ ] Tempered at stairs and landings (R308.4.6)
- [ ] Broken live callouts and orphan details

## Out of scope for now
- Modeling from sketches or PDFs
- Multi-tenant Studio OS version (the MCP design keeps that door open)
- Structural design. AI drafts, Jason and Larry engineer.
