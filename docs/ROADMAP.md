# Roadmap

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
- [x] 82 automated tests on Core; add-in compiles against Revit 2025 and 2026 APIs
- [ ] First run inside Revit on a stripped copy of a finished job
- [ ] Ribbon icons
- [ ] Highlight affected elements before approving (preview)
- [ ] Workshared models: check element ownership before proposing

## Phase 2: Schedules + details
- [ ] Create door / window / room / framing schedules by duplicating template schedules
- [ ] Place drafting-view details based on wall, roof and foundation types in the model
- [ ] Detail callouts and sheet references

## Phase 3: Annotation
- [ ] Tag all doors / windows / rooms / walls in a view (tag types from standards)
- [ ] Keynotes
- [ ] General notes and code notes (2021 IRC + PPRBD) as text or legends
- [ ] Dimensions: exterior wall strings, openings, overalls. Rule-based first, AI picks the strategy per view.

## Phase 4: Plan set QA
- [ ] Punch list before issue: untagged elements, missing schedules, sheet index vs actual sheets, empty views, broken callouts, notes that contradict the model

## Out of scope for now
- Modeling from sketches or PDFs
- Multi-tenant Studio OS version (the MCP design keeps that door open)
- Structural design. AI drafts, Jason and Larry engineer.
