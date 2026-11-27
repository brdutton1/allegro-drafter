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
- [x] Proposals: create sheets, place views/legends/schedules (auto-arrange or exact). View templates removed 2026-11-26 (office rule: the designer's call)
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
- [x] Create schedules by copying office schedules and place them on sheets
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
- [x] Finish Drafting survives Stop/Resume and restarts: one open batch per project (a new batch rejects the leftover, so the same work can't be approved twice), leftovers hidden during a run, card answers saved per project
- [x] First full Finish Drafting run on 25-0083 (2026-11-26: about 5 min, 13 approvals, 13 cards)
- [x] Door schedule group headers with a count per size code
- [x] Office auto-apply rule (`manual.finishDrafting.autoApplySteps`; deletes always wait), per-run "stop at every batch" override
- [x] "What changed" report by sheet: in the run panel with Open/Show, markdown file per run, survives restarts, undone batches drop out
- [x] Step order: cover sheet, view templates, then schedules (no repeated steps); a view type with no office template gets one card instead of silence
- [x] Auto-apply narrowed to model-only steps; view templates, sheet placement and tags always wait (25-0083 review: templates moved viewports and showed analytical/reference lines). Detail sections asked apart from building sections
- [x] Scope (2026-11-26): no template work, nothing with view templates. Finish Drafting step, `propose_view_templates`, schedule-from-template and callout templates removed; validator refuses them; `view-templates` audit check off by default (`manual.audit.disabledChecks`)
- [ ] Cover sheet: place the sheet index in empty space, not the drawing area's top-left

## Drafting engine (plan approved 2026-11-26)
The drafter does the drafting on the job as it is, with the job's own types. Only adds; never moves the designer's annotation.
- [ ] Spikes in Revit: core-face references for dims, real tag bounding boxes, API-created elevations/sections get no default template, dimensioning to levels
- [x] Phase 1 rules built (not yet proven in Revit): outside read from the plan's outline (flipped walls), Bryan's rules from `read_dimensions` (outside of finish, opening centerlines, interior to the face toward the starting exterior wall, 3/8" paper spacing, order columns/openings/walls/overall), porch columns, deck/floor/slab edges, interior strings clear of swings and room tags, `manual.drafting` settings, one side only, foundation walls, `dimension-closure` audit check
- [ ] PAUSED by Bryan (2026-11-27): "This is very complicated. I want to pause on this and get tags and the other things working." Last state: flood-fill outline (cb03731) built, not yet checked on 25-0083. Resume with read_dimensions on MAIN FLOOR PLAN.
- [ ] Phase 1 proven on 25-0083: reader puts Bryan's strings on the right sides; one cleared side redrawn and approved
- [ ] Face of stud references (spike 1) if the office ever wants it
- [ ] Phase 2: tags that don't collide (measured tag sizes, greedy layout, leaders as fallback)
- [ ] Phase 3: `schedule-contents` check with a fix per finding
- [ ] Phase 4: exterior elevations created (FRONT/REAR/LEFT/RIGHT), tagged, height strings, roof pitch, placed on A2/A3
- [ ] Phase 5: building sections created by rule, labeled, heights, placed on A4/A5
- [ ] Phase 6: Finish Drafting runs the drafting steps, one view per batch
- [ ] Second run with the new build on a fresh job copy: count clicks, check the report against the sheets
- [ ] Spot-check door handing (573 Therma-Tru "Inswing" read as LHR) and lock the convention in `manual.audit`
- [ ] Manufacturer data per type (RO, net clear W/H, U-factor, SHGC), then RO in schedules and egress by net clear area
- [ ] Dimension strings close to their overall, no gaps
- [ ] Bearing walls stack between levels
- [ ] Tempered at stairs and landings (R308.4.6)
- [ ] Broken live callouts and orphan details

## Out of scope for now
- Modeling from sketches or PDFs
- Multi-tenant Studio OS version (the MCP design keeps that door open)
- Structural design. AI drafts, Jason and Larry engineer.
