# Allegro Drafter

AI drafting assistant for Revit 2025 and 2026, built for Allegro Design Co.

Claude does the grunt drafting: sheet setup, view placement, view templates. Later phases add schedules, details and annotation. **The designer approves every change.** Nothing touches the model until you click Approve. Each approval is one Ctrl+Z.

## How it works

```
Claude Desktop / Claude Code
        │  (MCP, stdio)
        ▼
AllegroDrafter.Bridge.exe  ── answers tool lists even when Revit is closed
        │  (HTTP, localhost only, token from mcp.json)
        ▼
Revit add-in ── reads the model, checks every request, queues a proposal
        │
        ▼
Approval pane in Revit ── you approve or reject ── one transaction, logged
```

Three rules hold the whole thing together:

1. **The AI can't invent names.** Title blocks, view templates and types must match the office standards profile exported from your template. A wrong name is refused before you ever see it, with a "did you mean" hint so Claude fixes it itself.
2. **The AI can't change the model.** It can only propose. Proposals are checked against the live model twice: when proposed, and again right before applying.
3. **Everything is logged.** Every approve, reject and failure goes to `%APPDATA%\AllegroDrafter\audit\YYYY-MM.jsonl`.

## Status

Runs in Revit 2026 on a real job: sheets, schedules, the set audit and its fixes (Type Marks, Tempered/Egress, schedule columns and sort, room cleanup). Tags, dimensions, callouts and notes are built and compile against the Revit 2025 and 2026 APIs but haven't had a full real-job run yet. Next: the Finish Drafting button. See [docs/PRODUCT-PLAN.md](docs/PRODUCT-PLAN.md) and [docs/ROADMAP.md](docs/ROADMAP.md).

| Tool | What it does |
|---|---|
| `get_standards` | Office standards profile: the only names the AI may use |
| `get_project_info`, `list_levels`, `list_views`, `list_sheets`, `list_rooms`, `count_elements` | Read the model |
| `propose_sheet_set` | New sheets plus the views on them, one batch |
| `propose_sheets`, `propose_place_views`, `propose_view_templates` | Smaller batches |
| `list_schedules`, `list_elements`, `read_legend` | Read schedules, doors/windows/rooms with parameters, legend text |
| `audit_set` | Read-only punch list before issue: size codes, schedules, tags, wall Function, wall legend, sheets, scales, text height, rooms, templates, egress, tempered, typed references, cover sheet |
| `propose_schedules` | Door, window, room and finish schedules from office schedules, placed on sheets |
| `propose_schedule_fields` | Add, remove and position schedule columns (e.g. Mark on the door schedule) |
| `propose_schedule_sort` | Sort/group a schedule (Type Mark, then swing) and turn itemizing on or off |
| `propose_type_marks` | Size-code Type Marks on door and window types (`376` -> `3080`), checked against the real size |
| `propose_parameters` | Add Yes/No or text instance parameters (Tempered, Egress) from the Allegro shared parameter file |
| `propose_room_cleanup` | Delete unplaced and unenclosed rooms (placed rooms are refused) |
| `propose_wall_function` | Set wall type Function (Interior/Exterior), which decides what counts as an exterior door or window |
| `propose_parameter_values` | Marks, room names/numbers, finishes (finish codes enforced) |
| `propose_tags` | Tag untagged doors, windows, rooms, walls in a view with office tag types |
| `propose_dimensions` | Exterior plan dimension strings: openings, wall jogs, overall |
| `propose_callouts` | Detail callouts (new enlarged views) and reference callouts to standard details |
| `propose_notes` | Text notes with leaders, office text types only |
| `list_proposals`, `get_proposal` | Check what you approved or rejected |

## Get started

Claude Code is the recommended client. See [docs/SETUP.md](docs/SETUP.md). Short version, on the Windows machine with Revit:

```powershell
git clone https://github.com/brdutton1/allegro-drafter
cd allegro-drafter
.\scripts\install.ps1
```

## Repo layout

- `src/AllegroDrafter.Core`: proposals, validation, standards, audit, MCP protocol. No Revit dependency, fully tested.
- `src/AllegroDrafter.Revit`: the add-in (ribbon, approval pane, model reads, transactional apply, standards export).
- `src/AllegroDrafter.Bridge`: stdio exe Claude launches.
- `tests/AllegroDrafter.Core.Tests`: unit and end-to-end tests (HTTP server + bridge).
- `docs/`: setup, architecture, roadmap.
