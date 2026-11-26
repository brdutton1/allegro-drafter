# ADR 0001: Finish Drafting button

Status: Accepted (2026-11-22). Rules only, no AI in the button.

## Context

Today every drafting step starts in a Claude chat. The designer leaves Revit, types a prompt, comes back, approves. Most of what gets proposed is not a judgment call: the audit already derives the fix (size code from the real family size, Tempered/Egress parameters, schedule columns and sort, unplaced rooms). Paying for an AI round-trip on a rule is slow, costs money, and can drift.

Goal: one button on the Allegro tab that finishes the set. Rules do the work. Anything a rule can't settle becomes a question card the designer answers. The designer approves every batch, same as now.

## Decision

### 1. Ribbon: Allegro > Finish Drafting

Opens the pane in "run" mode with three sections:

- **Gate**: model data that poisons later steps. The run stops here until it's clean.
- **Batches**: rule-based fixes, queued one at a time in office order.
- **Your call**: judgment items as question cards with the answer choices on the card.

### 2. Rules engine in Core, no AI

New `Core/Finishing/FinishPlanner`: takes `SetFacts` + audit results + `manual` rules, returns the next batch. Pure function, fully tested. Order:

| Step | Batch | Source |
|---|---|---|
| 0 | Gate: interior wall types with Function = Exterior, missing shared parameter file, unloaded tag families | `wall-function` audit check (built) |
| 1 | Tempered / Egress parameters | `opening-parameters` |
| 2 | Size-code Type Marks | `type-marks`, `operation-codes` suggestions |
| 3 | Schedule columns, then sort | `schedules` |
| 4 | Tempered = Yes on glass doors, No on the rest; Egress per bedroom rule | `tempered`, `egress` |
| 5 | Room Size param, sizes out of Number, renumber 101+ | `rooms` |
| 6 | Door swing from hand/facing (LH/RH/LHR/RHR, POCKET, SLIDING) | `opening-parameters` |
| 7 | Room cleanup (unplaced only, separate confirm screen) | `rooms` |
| 8 | View templates by view type, then tags | `view-templates`, `tags` |
| 9 | Cover sheet: sheet index, DO NOT SCALE note | `cover-sheet` |

After each approval: re-run the audit, plan the next batch. A rule with an exception it can't resolve turns that item into a "Your call" card instead of guessing.

### 3. Judgment items are question cards, not AI

Every "judgment" item turned out to be a rule plus an exception:

- Door swing: derived from the door's hand and facing. A family that reads backwards is listed once in `manual.audit`. A door with no room on either side becomes a card.
- Room numbering "clockwise from the entry": geometry, once a room is named ENTRY or FOYER. No entry room becomes a card.
- Tempered/egress exemptions: the designer picks No and writes the reason in Comments, which the audit honors.

A card shows the element, the rule, and the answer choices. The designer's click becomes the next batch. Answers are logged.

No AI in the button means no API key on team machines, no spend to manage, and the same result every run. Claude stays available to Bryan through Claude Code (MCP) for building rules and reading old sets. ARCHITECTURE.md's "No Anthropic API key lives in the add-in" stays true.

### 4. Safety, before any of this ships

- **Undo awareness.** Listen for `DocumentChanged` undo of an "Allegro Drafter: ..." transaction, mark the proposal Undone, and replan. Without this the run builds on a step that no longer exists.
- **One batch visible at a time** in run mode. Next batch appears only after the audit re-runs, with the existing 1.2 s Approve lockout.
- **Destructive batches** (deletes) get their own confirm screen with the count, not a second click.
- **Stop any time.** Closing the pane ends the run. Re-clicking the button resumes from the audit, not from memory.

## Consequences

- Most of a run costs nothing and gives the same result every time.
- The designer's time goes to approving batches and answering a handful of cards per job.
- The rules live in `manual` and Core, so the office template rollout and the button share one source of truth.
- If a real judgment gap shows up later, a suggestion source can be added behind the card without changing the run.

## Amendment (2026-11-26): fewer clicks, review by sheet

First full run on 25-0083: 13 approvals and 13 cards in about 5 minutes. Most approvals were on batches with nothing to judge (schedule columns, view templates after the designer picked them). Bryan: approving every batch is too many clicks, and after a run he wants to see which sheets changed without hunting.

- **Office auto-apply rule.** `manual.finishDrafting.autoApplySteps` lists steps that apply without a click. Default: `gate`, `parameters` (model data only, nothing a sheet shows). Whatever the list says, a batch always waits if it deletes, applies view templates, places a view on a sheet or places tags. Question cards always wait.
  - Why so narrow: the first version also auto-applied view templates, cover sheet and tags. Bryan's review of 25-0083 after the run: viewports resized and moved, analytical items and reference lines showed up. Cause: 33 office templates applied to laid-out views (approved by click in that run, but it would have happened with no click at all under the first default). A template overwrites the view's own scale, crop, view range and visibility, and removing it doesn't restore them. On an existing job, templates belong to the office template rollout, not the button. Each auto batch is still its own transaction (Ctrl+Z), and the audit log records it as applied by office rule. The run panel has a per-run "stop at every batch" override.
- **Review moves to the end, by sheet.** After every applied batch the add-in records where the change prints: schedules on sheets that show the changed parameter, tags on placed views when the parameter is one the office tags print (`manual.finishDrafting.tagLabelParameters`), new tags/notes/viewports on their sheet. Everything else is listed as model-only. The run panel shows "What changed" per sheet with Open (sheet) and Show (elements), and the finished run writes a markdown report to `%APPDATA%\AllegroDrafter\reports`. Undone batches drop out. The list survives a restart mid-run.
- **Step order.** Cover sheet, then view templates, then schedules. A schedule template replaces the schedule's columns and sort, and placing the sheet index creates a view that needs a template; in the old order both made steps repeat (3 of the 13 approvals).

This keeps the designer in control: the office decides which steps are mechanical, the designer can override per run, and nothing a rule applies goes unreported.

## Next after this: sections and elevations

See [0002-section-and-elevation-labels.md](0002-section-and-elevation-labels.md).
