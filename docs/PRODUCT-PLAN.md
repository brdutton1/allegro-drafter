# Allegro Drafter: product plan

Written 2026-11-22. This is the end state and the path to it. ROADMAP.md tracks the checkboxes; this file says what we're building and why.

## The promise

**The designer designs the house. The drafter produces the construction set. The designer approves every change.**

A team member installs one program, opens Revit, models the house, and clicks **Finish Drafting**. The drafter builds the sheets, places and labels the views, dimensions, tags, schedules, notes, sections, elevations and details to Allegro rules, and asks the designer only what it can't know. What used to be days of drafting becomes an afternoon of approving and answering.

## Who does what

| Designer (team) | Drafter: rules | Bryan + Claude Code | Engineer (Jason, Larry) |
|---|---|---|---|
| Levels, walls, floors, roofs, ceilings | Sheets, view placement, view templates | Writes and tunes the office rules | Structural design and sizing |
| Doors, windows (office families) | Type Marks, schedules, tempered/egress | Reads old issued sets to seed rules | PE stamp |
| Rooms (placed, named) | Room numbers, sizes, finishes from rules | Maps existing section text to type notes (once, in the template) | |
| Stairs, casework, fixtures | Plan tags, elevation tags, exterior dims | One-off requests outside the button | |
| Builds section and elevation views | Section/elevation labels from type notes | | |
| Answers question cards | Electrical layout, circuits, panel schedule | | |
| Approves every batch | Cover sheet, sheet index, notes; audit after every batch | | |

Rule: if a decision can be written as a rule, it's a rule. What a rule can't settle becomes a question card; the designer picks. No AI runs inside the button.

## What "all the drafting" means: the Allegro set, sheet by sheet

| Sheet | Drafter does | Rules from | Status |
|---|---|---|---|
| **CS** Cover | Sheet index, project data, code list, design criteria, scope, area calcs, size-code legend, general notes incl. DO NOT SCALE | `manual.coverSheet`, county profile | Audit checks it. Fixes not built |
| **C1-C2** Site | Place site plan, north arrow, scale bar, setbacks labels | `manual.site` | Not built |
| **F1-F2** Foundation | Foundation plan dims to face of concrete, pier/footing tags, foundation details placed and referenced | `manual.foundation` | Not built |
| **A1** Floor plans | Exterior dims (3 tiers), interior dims, door/window/room tags, room names/numbers/areas, notes | `manual.plans` | Exterior dims + tags built. Interior dims not built |
| **A1.x** RCP / deco beam | Ceiling tags, beam labels | `manual.ceilings` | Not built |
| **A2-A3** Elevations | Opening tags (exterior only, facing view), material/finish labels, plate and floor heights, roof pitch, grade | ADR 0002 | Opening tags built. Labels not built |
| **A4-A5** Sections | Assembly labels from type notes, layer callouts, level heads, section heads referenced | ADR 0002 | Not built |
| **A6** Roof plan | Pitch arrows, ridge/valley labels, overhang dims | `manual.roof` | Not built |
| **A11-A13** Interior elevations | Room-based elevation views, cabinet labels, fixture tags | `manual.interiorElevations` | Not built |
| **Schedules** | Door, window, room, finish, area, sheet index | Office schedules | Built and run |
| **Details** | Place standard details by wall/roof/foundation type in the model, live references, no orphans | Detail library map in `manual.details` | Not built |
| **E** Electrical | Receptacles and switches by NEC rules, smoke/CO per IRC R314/R315, circuits per NEC 220, panel schedule, wiring graphics | `manual.electrical`, county NEC edition | Not built |
| **S1-S4** Structural | Sheet setup and placement only. No sizing | Jason/Larry | Sheet setup built |

## The team product

### Install
- **One signed installer** (MSI) from GitHub Releases. No .NET SDK, no git, no Claude Code. Double-click, next, done.
- Installs for every Revit year on the machine (2025, 2026, 2027 when it ships).
- **Auto-update:** on Revit start, check Releases, download in the background, install on next Revit start. Office can pin a version.
- `install.ps1` stays for development.

### Office standards, shared
- `standards.json` moves from each user's `%APPDATA%` to one **office folder on SharePoint** (synced to every workstation). One profile, one set of rules. Edits by Bryan; everyone else read-only.
- Per-machine override only for development.
- Repo stays public; office standards, keys and job data never go in it.

### No AI on team machines
- The button is rules plus question cards. No API keys to issue, no spend to cap, same result every run.
- Claude Code (MCP) stays on Bryan's machine for building rules and one-off work.

### Talking to the drafter inside Revit
- **Finish Drafting** button for the full run.
- **Per-sheet buttons** for partial runs: "Draft this plan", "Label this section", "Tag this elevation".

### Shared log
- Every approve, reject, answer and failure goes to an office log on SharePoint, per job.
- Weekly: what got rejected most. A rejected rule is a rule to fix. That's how the drafter gets better without anyone retraining it.

### Real-world model conditions
- **Workshared models:** check element ownership before proposing; a batch that touches borrowed elements says who has them.
- **Undo:** proposals know when they've been undone (ADR 0001).
- **Old jobs:** the gate catches template junk before the run (wall Function, junk types, unplaced rooms).
- **Speed:** audit under 2 s on a typical house; a full run should be dominated by the designer's review time, not the tool.

## Phases

Each phase ends with a real job run start to finish, and a time measurement against how long it took by hand.

| Phase | Scope | Done when |
|---|---|---|
| **A. Foundation** | Undo awareness, `wall-function` gate, audit bug fixes (1/8" rounding, honoring Tempered = No), read wall types/Function, roadmap and docs current | 25-0083 audit shows only real problems |
| **B. Finish Drafting v1** | Ribbon button, run mode in pane, FinishPlanner (steps 0-9), question cards, room renumbering and door swing rules | 25-0083 goes from today's audit to clean in one sitting |
| **C. Template rollout** | Office template cleaned: wall Function, text/dim at 1/8", wall legend, Type Marks on every loaded family, shared params, type notes, tag families | New job from template passes the gate with zero findings |
| **D. Sections and elevations** | ADR 0002: type notes, material callouts, label placement, audit checks | A section and an elevation labeled to match Bryan's sample PDF |
| **E. Plans complete** | Interior dims, strings close, roof plan, RCP, foundation plan dims | A1 and F1 need no hand dimensioning |
| **F. Electrical** | NEC receptacle/switch layout, smoke/CO, circuits (NEC 220), panel schedule, wiring graphics. Residential families with connectors come from Phase C | E1 drafts itself; panel schedule matches the circuits |
| **G. Details and references** | Detail library map, place by assembly, live callouts, no orphans | Detail sheets assemble themselves from the model's types |
| **H. Team release** | MSI, signing, auto-update, SharePoint standards and log, per-sheet buttons, setup doc for staff | A team member installs it without help and drafts a job |
| **I. Cover, site, interior elevations** | Remaining sheets | Whole set from model to permit-ready |
| **J. Estimator export** | Clean quantities to Materials Takeoff Pro: wall/floor/roof by type and layer, openings with RO, finishes by room, devices by circuit | A job's takeoff in Materials Takeoff Pro needs no hand counting |

Order reasoning: A and B save time on every job right away. C makes every later phase cheaper, because a clean template means fewer gate findings and fewer rules fighting junk data. H comes after the drafter is good enough that handing it out saves time instead of creating support calls.

Estimate-ready from day one: every gate check also protects the takeoff. No walls used as modeling aids (`Truss`, `POST TRIM`, `water fall`), real wall types with correct layers, sizes from real family geometry. Drafted right means counted right.

## What breaks, and the guard for each

| Risk | Guard |
|---|---|
| Approval fatigue: people click Approve without reading | One batch at a time, lockout, destructive confirm screen, batch summary in plain words, rejected-rule review |
| A rule is wrong and runs on every job | Rules live in one shared `manual`; the log shows the rejection pattern; fix once, every job gets it |
| Model data lies (wall Function, families with wrong sizes) | Gate step stops the run; audit derives from geometry, not names |
| Revit API changes in a new year | Build and test per year in CI; release only after a real job run on that year |
| Team edits standards and breaks rules | SharePoint folder read-only except Bryan |
| Workshared conflicts | Ownership check before proposing |
| Drafter output looks "off" vs the office | Labels and notes use office text/tag types only; every new output type is checked against an issued Allegro set before it ships |

## Out of scope (oceans)

- Modeling the house from sketches or PDFs. The designer models.
- Structural design or sizing. AI drafts; Jason and Larry engineer.
- Multi-tenant Studio OS. The design keeps that door open (standards as data, no Allegro names in code), but it's not this project.
- Pricing. Materials Takeoff Pro prices; the drafter only exports quantities.
