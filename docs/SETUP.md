# Setup

About 20 minutes the first time. Do it on the Windows machine that runs Revit.

## 1. Install the tools

- **.NET 8 SDK**: `winget install Microsoft.DotNet.SDK.8`, or download "SDK 8.0, Windows x64" from https://dotnet.microsoft.com/download/dotnet/8.0. Revit only installs the runtime, which can't build. Open a new PowerShell after installing.
- **Git**: https://git-scm.com/download/win
- **Claude Code** (recommended): `irm https://claude.ai/install.ps1 | iex` in PowerShell, then open a new PowerShell and run `claude` once to sign in. Claude Desktop also works if its Developer settings are available to you.

## 2. Build and install the add-in

Close Revit, then in PowerShell (start in your user folder, not System32):

```powershell
cd $HOME
git clone https://github.com/brdutton1/allegro-drafter
cd allegro-drafter
.\scripts\install.ps1
```

It runs the tests, builds for every Revit year it finds (2025, 2026), copies the add-in into `%APPDATA%\Autodesk\Revit\Addins\<year>\`, and prints the Claude config line.

If PowerShell blocks the script: `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`, then run it again.

## 3. First launch

1. Start Revit. Click **Always Load** when it asks about Allegro Drafter.
2. You'll see an **Allegro** tab with **Approvals**, **Export Standards** and **Status**.
3. Click **Status**. It should say `MCP server: Running at http://localhost:48884/mcp`.

## 4. Export the office standards (Phase 0, do this first)

1. Open the **office template** (.rte), or a project created from it.
2. Click **Allegro > Export Standards**, then **Yes** to make it the active profile.
3. Open `%APPDATA%\AllegroDrafter\standards.json` and fill in the `manual` section (see below). This is where your office rules live.

Until a profile exists, every proposal is refused. That's on purpose.

### The `manual` section

Hand-edited. Re-exporting never overwrites it.

```json
"manual": {
  "drawingAreaInsetsInches": { "left": 1.0, "right": 4.5, "top": 0.5, "bottom": 0.5 },
  "rules": [
    "Architectural sheets are A1.0 (site), A2.x (floor plans), A3.x (elevations), A4.x (sections), A5.x (details).",
    "Sheet names are ALL CAPS.",
    "Use the 24x36 title block for permit sets, 11x17 only for client review sets."
  ],
  "sheetIndexByProjectType": {
    "new-home": [
      { "number": "G0.0", "name": "COVER SHEET" },
      { "number": "A1.0", "name": "SITE PLAN" },
      { "number": "A2.1", "name": "MAIN FLOOR PLAN" },
      { "number": "A2.2", "name": "UPPER FLOOR PLAN" },
      { "number": "A3.1", "name": "EXTERIOR ELEVATIONS" },
      { "number": "A4.1", "name": "BUILDING SECTIONS" }
    ]
  }
}
```

`drawingAreaInsetsInches.right` should clear your title block strip, so auto-placed views don't land on it.

### Finish codes

Add your finish legend's codes so Claude can't invent finishes:

```json
"finishCodes": [
  { "code": "T1", "description": "PORCELAIN TILE" },
  { "code": "P1", "description": "PAINT, EGGSHELL" },
  { "code": "CPT1", "description": "CARPET" }
],
"finishParameters": ["Floor Finish", "Base Finish", "Wall Finish", "Ceiling Finish"]
```

Easiest: ask Claude "Read the finish legend with read_legend and draft finishCodes for standards.json. Show me first." Check it, then let it write the file.

Once `finishCodes` has entries, any finish parameter can only take those codes. Combined values like `P1/T1` are fine. If your template uses custom finish parameters (e.g. "FLOOR FIN"), list them in `finishParameters`.

## 5. Connect Claude

**Claude Code (recommended):**

```powershell
.\scripts\connect-claude.ps1 -ClaudeCode
```

That registers Allegro Drafter for every folder (`--scope user`). By hand it's:

```powershell
claude mcp add --scope user allegro-drafter -- "C:\Users\YOU\AppData\Roaming\Autodesk\Revit\Addins\2026\AllegroDrafter\AllegroDrafter.Bridge.exe"
```

Without `--scope user` the tools only show up when Claude Code is started in that one folder.

Check it with `claude mcp list`. Then, with Revit open, run `claude` in PowerShell and start asking.

**Claude Desktop (if you have Settings > Developer):** quit Claude Desktop, then run `.\scripts\connect-claude.ps1`. It finds the config file, backs it up and adds the entry. By hand: Settings > Developer > Edit Config, and inside `"mcpServers"` add:

```json
"allegro-drafter": { "command": "C:\\Users\\YOU\\AppData\\Roaming\\Autodesk\\Revit\\Addins\\2026\\AllegroDrafter\\AllegroDrafter.Bridge.exe" }
```

Fully quit and reopen Claude Desktop, then use a new chat in the **Chat** tab.

No API key goes into Revit. Claude handles that.

## 6. First real test

Use a **copy** of a finished job with its sheets deleted. Ask Claude:

> Read the standards and this model. Propose the CD sheet set for a new home using our typical sheet index, with the floor plans, elevations and sections placed.

Then in Revit, open the **Approvals** pane, read the changes, and Approve or Reject. Compare the result to the issued set.

## Schedules (Phase 2)

On a Save As copy of a finished job, with Revit open, ask Claude Code:

1. "Read the finish legend and draft finishCodes for standards.json. Show me first."
2. "Check which door, window, room and finish schedules exist. Create any that are missing from our office schedules and place them on the schedule sheet."
3. "Number the doors and windows by level and room" (give it your rule).
4. "Fill room finishes by room type using our finish codes." Up to 50 rooms per proposal.

Approve or reject each batch in the Revit pane.

## Annotation (Phase 3)

Work one view at a time, so each approval is easy to check:

- "Tag all untagged doors and windows on the Level 1 floor plan with our standard tags."
- "Add exterior dimensions to the Level 1 floor plan: openings, wall jogs and overall."
- "Make an enlarged plan callout around room 104 at 1/2\" = 1'-0\" and put it on A5.1."
- "Add a reference callout to our footing detail at the north wall."
- "Add a smoke alarm note with a leader to each bedroom." (give it the wording)

Exterior dimensions need wall types with **Function = Exterior**, and door/window families need a **Center (Left/Right)** reference. Claude tells you when either is missing. Tags land on the element center; drag any that collide.

## Set audit (Phase 4)

Ask Claude: **"Audit the set."** It runs `audit_set` and gives you a punch list: pass, warn or fail per check, with element ids. It changes nothing. Ask for fixes and they come back as proposals.

| Check | What fails |
|---|---|
| `type-marks` | Type Mark isn't a size code (`466`), or disagrees with the type's Width/Height by more than 1/2" |
| `operation-codes` | Operation isn't in the office list (`6060 CMST`), a window has none, or two types share a Type Mark |
| `schedules` | Door/window schedule missing Type Mark, Count, swing, Tempered or Egress, a hidden required field, not sorted by Type Mark first, or not on a sheet |
| `opening-parameters` | Doors missing Door Swing / Tempered, windows missing Tempered / Egress, or swing left blank |
| `tags` | A door or window isn't tagged on a placed plan, or an exterior one isn't tagged on a placed elevation. Windows above the plan cut (clerestories, transoms) need the elevation tag only |
| `wall-legend` | Legend shows a wall type the model doesn't use, or the model uses one the legend doesn't show |
| `sheets` | Number doesn't fit the office pattern, unfinished name, empty sheet, no sheet index placed |
| `view-scales` | Placed plan, elevation or section smaller than 1/8". Smaller than 1/4" warns. Site views skip |
| `text-heights` | A text or dimension type used on sheets is under 1/8" (PPRBD minimum) |
| `rooms` | Not enclosed, no name, no number, or a dimension stored as the number. Unplaced rooms warn |
| `door-rooms` | A door with no room on either side (warns) |
| `view-templates` | A placed view has no template, or one that isn't in the office standards |
| `egress` | A bedroom has no window marked Egress = Yes, or its sill is over 44" |
| `tempered` | Glass door not marked Tempered. Windows near doors, in tub/shower rooms, or big and low are flagged to confirm |
| `references` | Hand-typed `4/F2` style references (warn), or ones that point to a sheet that doesn't exist (fail) |
| `cover-sheet` | No cover sheet, no sheet index on it, or missing "SCOPE OF WORK" / "DO NOT SCALE" |

### Size codes, swing, tempered, egress

- **Type Mark** is the size code plus operation: `3050 CSMT`, `4040 SLDR`, `6020 FIX`. Swing doors carry the size only: `3080`. The tag and the schedule both read Type Mark, so they can't disagree.
- **Door swing** goes on the schedule, not the tag. The door schedule sorts by Type Mark, then swing.
- **Tempered** and **Egress** are instance Yes/No parameters on doors and windows, shown as schedule columns. Add them once as project parameters (Manage > Project Parameters, Doors and Windows, Yes/No, instance).

### Tuning the rules

Every rule has a default. Override any of them in `standards.json` under `manual.audit`:

```json
"manual": {
  "audit": {
    "windowOperations": ["CSMT", "AWN", "DH", "SH", "SLDR", "FIX"],
    "doorOperations": ["PKT", "BARN", "BIFOLD", "BYPASS", "SLDR", "FRNCH", "OH"],
    "typeMarkToleranceInches": 0.5,
    "doorScheduleFields": ["Type Mark", "Count", "Door Swing", "Tempered"],
    "windowScheduleFields": ["Type Mark", "Count", "Tempered", "Egress"],
    "swingParameter": "Door Swing",
    "sheetNumberPattern": "^(CS|[A-Z]{1,2}\\d{1,2}(\\.\\d{1,2})?)$",
    "coverSheetMustContain": ["SCOPE OF WORK", "DO NOT SCALE"],
    "minTextHeightInches": 0.125,
    "planCutHeightInches": 48,
    "wallLegendNames": ["WALL LEGEND", "WALL LEGEND ICF"],
    "legendExemptWallTypes": ["Truss", "POST TRIM", "water fall"]
  }
}
```

A list you give replaces the default list. It doesn't add to it.

### Fixing what the audit finds

Ask Claude: **"Fix the audit."** It works in batches. Each one waits in the pane for your approval, then it re-runs the audit before the next:

1. **Parameters:** adds Tempered and Egress (Yes/No) to doors and windows. They come from `%APPDATA%\AllegroDrafter\AllegroDrafterSharedParameters.txt`, so they work in schedules and tags. Keep that file. Other projects reuse it.
2. **Type Marks:** each line shows the old mark, the new one, the type's real size and how many are placed, e.g. `376 -> 3080`. Refused unless the size code matches the type's Width/Height.
   - The audit reads the operation from the family name (Casement → CSMT, Pocket → PKT, Sliding → SLDR, Picture → FIX).
   - If two types would end up on one mark, or the name doesn't say the operation, it asks you instead of guessing.
3. **Schedules:** adds the missing columns, then sorts by Type Mark (doors: then Door Swing) with itemizing off, so each row is one size code with a count.
4. **Tempered:** sets Tempered = Yes on glass doors. Windows flagged near doors or in wet rooms, and every door swing, are your call. Claude lists them and asks.
5. **Rooms:** deletes unplaced and unenclosed rooms. You see the exact count and names first. Placed rooms can't be deleted this way.
6. **Templates and tags** for what's left.

Text heights and the wall legend are office template decisions. The audit reports them, but Claude doesn't patch them per project.

## Troubleshooting

| Symptom | Fix |
|---|---|
| Status says server NOT running, port in use | Another Revit is open with the add-in. Close it, or change `port` in `%APPDATA%\AllegroDrafter\mcp.json` and restart Revit. |
| Status says "Access is denied" on the port | Run once in an admin PowerShell: `netsh http add urlacl url=http://localhost:48884/ user=$env:USERNAME` |
| Claude Desktop has no Developer tab, or never starts allegro-drafter | Use Claude Code instead (step 5). |
| Claude Code only sees the tools in one folder | It was added without `--scope user`. Run `.\scripts\connect-claude.ps1 -ClaudeCode`. |
| Claude says "Revit is not running" | Open Revit with a project. The bridge reconnects on the next call. |
| Claude says "Revit is busy" | A dialog or edit mode is open in Revit. Finish it. |
| Every proposal refused: "No office standards profile" | Do step 4. |
| A proposal failed on approve | Read the red message on the card. Usually the model changed since it was proposed. Fix it and click Retry, or ask Claude to propose again. |

## Files

All in `%APPDATA%\AllegroDrafter\`:

- `standards.json`: the active office standards profile
- `standards-exports\`: every export with a timestamp (diff these to catch template drift)
- `audit\YYYY-MM.jsonl`: every approve, reject and failure
- `mcp.json`: port and access token. Keep it private.
