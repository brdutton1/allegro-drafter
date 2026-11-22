# Model review: sample job (single-story custom home, mountain county)

Source: `snapshots/` read of the open Revit 2026 model on 2026-11-21, taken with the Phase 3 add-in. Project number, client, address and town left out on purpose (this repo is public).

This is what the add-in can see. It cannot see tag label contents, text notes on sheets, dimension strings, or detail numbers. Items marked **verify** need eyes in Revit.

## The model at a glance

| Item | Count | Note |
|---|---|---|
| Sheets | 19 | CS, C1-C2, F1-F2, A1-A6, A11-A13, S1-S4 |
| Views | 302 | About 200 are drafting details. Most are unplaced library details |
| Levels | 10 | Includes BASEMENT SLAB and UPPER SUBFLOOR. Looks single-story (0 stairs, no basement or upper sheets) |
| Rooms | 142 | **17 placed.** 123 unplaced, 2 not enclosed |
| Doors | 27 | 10 types |
| Windows | 32 | 10 types |
| Walls | 139 | 12 types |
| View templates applied | **0** | Not one view uses a template |

## What breaks the set (fix first)

### 1. Doors don't follow the size-callout convention. Windows mostly do.
The office convention: the tag shows the nominal size code (`3050 CSMT`, `4040 SLDR`) and the schedule reports that same value from **Type Mark**.

- **Windows: good, with one typo.** Type Marks are `3050 CSMT`, `2666 CSMT`, `3866 CSMT`, `2066 CSMT`, `6020 FIX`, `3020 FIX`, `1660 FIX`, `4866 FIX`, `7920 TRANSOM`.
  - `ADC-Window-Casement-Double: 6060` has Type Mark **`6060 CMST`**. That's a typo for CSMT, and it sits on bedroom 3 and 4 egress windows.
  - Operation codes are not a closed list: `CSMT`, `FIX`, `TRANSOM`. A transom is a location, not an operation. It's fixed glass.
- **Doors: not following it at all.** Type Marks are Revit's auto numbers: `466`, `372`, `376`, `461`, `445`, `440`, `523`, `370`, `524`, `530`. The size lives only in the type name (`Supa 300: 3080`), and the big sliders use `12' x 10'` and `126" x 88"`, not size codes.

### 2. The schedules don't report from Type Mark
- `DOOR SCHEDULE` fields: Width, Height, Type, Door Swing, Type Comments.
- `WINDOW SCHEDULE` fields: Width, Height, Type, Type Comments.
- Neither one has Type Mark, Mark or Count. The tag on the plan and the schedule row have no shared value to match on. Today the schedule shows the Revit type name, which drops the operation (`3050`, not `3050 CSMT`).
- Missing columns builders and reviewers look for: count, rough opening, tempered, egress, U-factor/SHGC, sill height (windows have sill heights set, the schedule doesn't show them).

### 3. The tags may not be driven by a parameter (verify)
Door tag type is `Door Tag_ALLEGRO_CUSTOM_TEXT`. If "custom text" means the label is typed by hand, tags and the schedule can drift apart. Check which parameter the tag label reads. It should be Type Mark.

### 4. Wall legend doesn't match the walls in the model
`WALL LEGEND` (placed on A1 and A1.1) shows:
- A `Foundation - 10" Concrete`. The model has **`8" Foundation Wall`**.
- C `Interior 2x4 Wall (INSULATION)`. The model has **`Interior 2x6 Wall (INSULATION)`**.
- E `Exterior 2x6 Wall Stucco`. The model has **`Exterior 2x6 Wall w/ Stone`** and **`Exterior 2x6 Wall Board and Batten`**. No stucco anywhere.
- Not in the legend at all: `Interior 2x4 Wall w/ Stone 4" fireplace stacked stone`, `Interior 2x6 Wall (No Insulation, Sheetrock)`, `Glass`.

A framer reads the legend, then the plan, and gets two different answers. Not great.

### 5. Energy and code legend on CS: verify before permit
`PARK CO INSULATION` (placed on CS) states **2018 IRC / 2018 IECC** and these minimums: ceiling R-38, 2x6 wall R-19, floor over unheated R-19, basement wall R-9, slab perimeter R-6.

- Office default is 2021 IRC / 2021 IECC. **Verify** which edition the county enforces today.
- The site is IECC climate zone 7. Those R-values are below the IECC prescriptive table for zone 7 under both 2018 and 2021 (for example, 2021 IECC zone 7 ceiling is R-60, wood-frame wall is R-20+5ci or R-13+10ci, slab is R-10). Unless the county has a local amendment or the job uses REScheck, this legend gets a correction notice. **Verify the county amendment.**
- `PARK CO LOADING` says IRC 2018 edition, ground snow 57 psf (ASCE 7-22), wind 115 mph Vult, Exposure C, frost depth 24". Mixing an ASCE 7-22 snow value with an IRC 2018 header is worth a look.

### 6. Rooms are a mess
- 123 unplaced rooms and 2 not enclosed (`BREEZEWAY`, a second `KITCHEN`). Template and earlier-design leftovers. Some are named `CARPET`, `Hardwood`, `Tile`, so finish zones got drawn as rooms.
- **Room dimensions are stored in the room Number** (`16'-3" x 16'-3"` = KITCHEN). Rooms have no real numbers, so any schedule or door From/To that uses room number shows a dimension string.
- Finish parameters are empty on every room.
- 6 doors have no From/To room (`551`, `571`, `575`, `588`, `597`, `599`). Room boundaries are missing around them.

### 7. No view templates in use
0 of 302 views has a template. Every view is set by hand, so lineweights, scale and visibility can differ sheet to sheet. And since the drafter assigns templates by name, it has nothing to match against.

## Set completeness

| Exists in model | On a sheet? | Question |
|---|---|---|
| MAIN FLOOR ELECTRICAL PLAN + ELECTRICAL LEGEND | No | Should the permit set have an electrical sheet? |
| DRAWING INDEX schedule | No | Cover sheet has no sheet index |
| Revision schedule | No | No revision tracking on the set |
| ROOM SCHEDULE | No | Fine if rooms stay unnumbered. Not fine if finishes go in |
| BASEMENT / UPPER FLOOR PLAN views, rooms on BASEMENT SLAB | No | Template leftovers on a single-story job? |
| A11 Inspiration | Empty sheet | Client sheet or delete |

Cross-references:
- `PIER LEGEND` says `SEE "PIER/PAD DTL. 4/F2"`. **Verify** detail 4 on F2 is the pier/pad detail. Typed references don't update when details move.
- Drafting views carry sheet numbers in their names (`PIER DETAIL 2/F1`, `WINDOW CUT DETAIL 2/F1`, `Top Plate Detail 5/S3`). Those go stale as soon as a detail moves.

## Template clutter the drafter could trip on

- **60 text note types.** Includes `BRYAN SEAFOAM MIST QUESTIONS`, `YELLER`, `RED`, `19-0160 Stephenson - Detail View...`, `JLH Arch`, `WW - ...`, and four versions of "ALLEGRO BODY TEXT". The AI is only allowed names from the standards file, so it could legally pick any of these. Whitelist the 3-5 real ones in `manual`.
- **50 dimension types**, with near-duplicates (`Linear - 2/32" Arch`, `Linear - 2/32" ARCH`, `Linear - 2/32" Arch 2`). Pick one per use: plan, site, centerline, "G.C. VERIFY".
- **About 200 drafting details**, many marked `DNU`, `do not use`, `OLD`, `Copy 1`, or numbered `1` and `2`. Rename or purge them, or the drafter has to filter on names.
- Wall types used as modeling hacks: `Truss`, `water fall`, `POST TRIM`, `Glass`. They'll show up in takeoffs (Materials Takeoff Pro) as walls.
- `Exterior 2x6 Wall w/ Stone`: 64 pieces totaling 106.7 ft, about 1.7 ft each. Probably stone wraps at posts. Check that it's on purpose.
- Electrical legend uses 277V commercial fixture families and a "fluorescent lighting" entry. Residential plan, commercial symbols.
- Keynote file is Autodesk's stock `RevitKeynotes_Imperial_2004.txt`, copied into the project folder.

## What the drafter tool needs from this

1. **A size-code rule in `manual`:** Type Mark = `WWHH OPER`, with OPER from a closed list (draft: `CSMT`, `AWN`, `DH`, `SH`, `SLDR`, `FIX`, `PATIO SLDR`, `PKT`, `BARN`, `BIFOLD`; doors with no operation get size only, `3080`). The validator rejects anything else. That catches `CMST`.
2. **A check that compares Type Mark to the family's actual Width/Height.** `3050` must mean 3'-0" x 5'-0". Flag any mismatch.
3. **Schedule templates that include Type Mark and Count, grouped by Type Mark.** One row per size code, so the schedule reads exactly like the tags.
4. **A legend check:** every wall type in the wall legend must exist in the model, and every wall type used in plan views must be in the legend.
5. **A room cleanup proposal:** delete unplaced rooms (with approval), move dimensions out of Number into a "Room Size" parameter or a tag label.
6. **Whitelists in `manual`** for text, dimension and tag types, so "valid name" also means "the right one".
