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

Phase 1 (skeleton + sheet builder) is built and compiles against the real Revit 2025 and 2026 APIs. It has **not been run inside Revit yet**. See [docs/ROADMAP.md](docs/ROADMAP.md).

| Tool | What it does |
|---|---|
| `get_standards` | Office standards profile: the only names the AI may use |
| `get_project_info`, `list_levels`, `list_views`, `list_sheets`, `list_rooms`, `count_elements` | Read the model |
| `propose_sheet_set` | New sheets plus the views on them, one batch |
| `propose_sheets`, `propose_place_views`, `propose_view_templates` | Smaller batches |
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
