# Setup

About 20 minutes the first time. Do it on the Windows machine that runs Revit.

## 1. Install the tools

- **.NET 8 SDK**: https://dotnet.microsoft.com/download/dotnet/8.0 (SDK, x64)
- **Git**: https://git-scm.com/download/win
- **Claude Desktop** (https://claude.ai/download) or Claude Code

## 2. Build and install the add-in

Close Revit, then in PowerShell:

```powershell
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

## 5. Connect Claude

**Claude Desktop:** Settings > Developer > Edit Config. Inside `"mcpServers"` add the line `install.ps1` printed, for example:

```json
{
  "mcpServers": {
    "allegro-drafter": { "command": "C:\\Users\\YOU\\AppData\\Roaming\\Autodesk\\Revit\\Addins\\2025\\AllegroDrafter\\AllegroDrafter.Bridge.exe" }
  }
}
```

Restart Claude Desktop. The Allegro Drafter tools show up in the tools menu.

**Claude Code:**

```powershell
claude mcp add allegro-drafter -- "C:\Users\YOU\AppData\Roaming\Autodesk\Revit\Addins\2025\AllegroDrafter\AllegroDrafter.Bridge.exe"
```

No API key goes into Revit. Claude handles that.

## 6. First real test

Use a **copy** of a finished job with its sheets deleted. Ask Claude:

> Read the standards and this model. Propose the CD sheet set for a new home using our typical sheet index, with the floor plans, elevations and sections placed.

Then in Revit, open the **Approvals** pane, read the changes, and Approve or Reject. Compare the result to the issued set.

## Troubleshooting

| Symptom | Fix |
|---|---|
| Status says server NOT running, port in use | Another Revit is open with the add-in. Close it, or change `port` in `%APPDATA%\AllegroDrafter\mcp.json` and restart Revit. |
| Status says "Access is denied" on the port | Run once in an admin PowerShell: `netsh http add urlacl url=http://localhost:48884/ user=$env:USERNAME` |
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
