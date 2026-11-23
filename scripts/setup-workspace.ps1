<#
.SYNOPSIS
  Creates (or updates) the drafting workspace: %USERPROFILE%\AllegroDrafting
    CLAUDE.md          rules Claude Code loads automatically when started there
    DRAFTING-LOG.md    running log of decisions and progress (never overwritten)
    start.cmd          double-click to resume the last conversation
  plus an "Allegro Drafter" shortcut on the desktop.
  Safe to run again: it never touches an existing DRAFTING-LOG.md.
#>
param(
    [string] $Workspace = (Join-Path $env:USERPROFILE 'AllegroDrafting')
)

$ErrorActionPreference = 'Stop'
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
New-Item -ItemType Directory -Force -Path $Workspace | Out-Null

$claudeMd = @'
# Allegro Drafter: drafting session rules

You are drafting in Revit for Allegro Design Co. through the allegro-drafter tools.
The designer approves every change in the Revit Approvals pane. You never change the model directly.

## Memory: DRAFTING-LOG.md (this folder)
The laptop can shut down at any time. This log is how work survives it.

1. **At the start of every session**, read DRAFTING-LOG.md before doing anything else. Pick up from "Next up".
   Never ask the designer again for something already decided in "Office decisions" or the project's section.
2. **Write to the log as you go**, not at the end:
   - When the designer makes a decision (numbering rule, per-door vs per-type marks, naming, finish codes): add it under "Office decisions" (applies to every job) or under the project (this job only). Ask which if unclear.
   - When you queue a proposal: one line under the project with the date, proposal id and what it does.
   - When a proposal is approved, rejected or fails: update that line with the outcome (use get_proposal).
   - **At the end of every phase or batch of work: write a checkpoint** under the project: what's done, what's pending in the Approvals pane, and the exact next step. Then update "Next up" at the top.
3. Keep entries short. One line each. Dates as YYYY-MM-DD.
4. Use the Revit project title (from get_project_info) as the project heading.

## Working rules
- Call get_standards and get_project_info first in each session.
- Use only names from the office standards. If something is missing, say so; don't invent it.
- Propose one view, one sheet or one schedule's worth at a time so each approval is easy to check.
- After queuing a proposal, tell the designer it's waiting in the Approvals pane, then check get_proposal when they say it's done.
- If a tool says Revit isn't running or is busy, tell the designer exactly that and wait.
'@
[System.IO.File]::WriteAllText((Join-Path $Workspace 'CLAUDE.md'), $claudeMd, $utf8NoBom)

$log = Join-Path $Workspace 'DRAFTING-LOG.md'
if (-not (Test-Path $log)) {
    $template = @"
# Drafting log

## Next up
- (nothing yet)

## Office decisions
<!-- Apply to every job. e.g. 2026-11-23: Doors numbered per door, 101+ by level. Windows W1+. -->

## Projects
<!-- One section per Revit project title, e.g.
### 25-0083 Javier Cervera
- 2026-11-23: P001 door schedule + Mark column, pending.
- Checkpoint 2026-11-23: schedules done; next: tag Level 1 doors.
-->
"@
    [System.IO.File]::WriteAllText($log, $template, $utf8NoBom)
    Write-Host "Created $log"
} else {
    Write-Host "Kept your existing $log"
}

$startCmd = @"
@echo off
title Allegro Drafter
cd /d "$Workspace"
echo Resuming your last Allegro Drafter conversation...
claude --continue
if errorlevel 1 (
  echo No earlier conversation here. Starting a new one.
  claude
)
"@
[System.IO.File]::WriteAllText((Join-Path $Workspace 'start.cmd'), $startCmd, (New-Object System.Text.ASCIIEncoding))

try {
    $desktop = [Environment]::GetFolderPath('Desktop')
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut((Join-Path $desktop 'Allegro Drafter.lnk'))
    $shortcut.TargetPath = Join-Path $Workspace 'start.cmd'
    $shortcut.WorkingDirectory = $Workspace
    $shortcut.Description = 'Resume the Allegro Drafter conversation in Claude Code'
    $shortcut.Save()
    Write-Host 'Desktop shortcut: Allegro Drafter'
} catch {
    Write-Host "Couldn't create the desktop shortcut ($($_.Exception.Message)). Use $Workspace\start.cmd instead."
}

Write-Host "Workspace ready: $Workspace"
