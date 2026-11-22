# What makes a residential plan set buildable and easy to read

Research for the Allegro Drafter checks. Written 2026-11-21.

The question: what does a framer, a sub and a plan reviewer need from a set, and which parts can a tool check?

Labels:
- **[STD]** is a published standard or jurisdiction requirement.
- **[PRACTICE]** is common practice.
- **[OPINION]** is a builder or forum view.

Model findings this ties to: [model-review-sample-job.md](model-review-sample-job.md).

The best single source is PPRBD's Residential Plan Review packet (2023 PPRBC / 2021 IRC / 2021 IECC): https://www.pprbd.org/File/ByAlias/ResPRguide. Treat it as the checklist.

---

## 1. The office size-code convention

The convention: tag windows and doors with the nominal size and operation (`4050 CSMT`, `3040 SLDR`, `2868`) on plans and elevations. The schedule reports the same value from Revit **Type Mark**.

**The verdict: keep it.** It's the right call for residential.

- PPRBD treats ft-in x ft-in as "the conventional method for stating dimensions of openings" and asks you to state any variation on the plans [STD]. PPRBD also wants window operation shown on the floor plan [STD]. `4050 CSMT` does both in one tag.
- Framers don't have to flip to a schedule. Commercial-style mark tags (`W1`, `101`) push data into the schedule but add a lookup every time [PRACTICE] (https://archinect.com/forum/thread/33850/window-schedules).
- The classic risk of size tags is the plan and the schedule drifting apart. Reading both from Type Mark removes that, **as long as Type Mark is right.** That's the whole game.

### Where the convention breaks, and the check for each

| Risk | What happens in the field | Check |
|---|---|---|
| Type Mark doesn't match the geometry | Someone resizes a type or duplicates it and keeps the old mark. The tag says 3050, the window is 3060 | Parse `WWHH` from Type Mark and compare it to type Width/Height. Flag anything over 1/2" |
| Typos and made-up operation codes | `6060 CMST` (in the sample job today), `TRANSOM` used as an operation | Operation must come from a closed list in `manual` |
| Size code is not the rough opening | Manufacturers disagree. In Andersen 100 Series, 3050 *is* the RO. In other lines, 3050 is the unit and the RO is 1/2" to 1-1/2" bigger. A builder reported "all the window openings were too small by a couple of inches" | Schedule carries an **RO column from manufacturer data**. General note: "Size codes are nominal W x H. Frame to RO in schedule." |
| Same size, different rules | Tempered depends on location. Egress depends on net clear opening. Neither is in the size | Instance Yes/No parameters **Tempered** and **Egress**. The tag appends `T` / `E` (Marvin's legend uses the same letters) |
| Reader doesn't know the code | PPRBD asks for variations from ft-in to be stated | Size-code legend on the cover: "4050 = 4'-0" W x 5'-0" H, width first", plus the operation abbreviations |
| Elevation-only windows | Clerestories and gable windows show up only on elevations and get missed | Every exterior unit is tagged on an elevation too |
| Duplicate Type Marks | Revit warns on them, and the schedule merges rows | The operation suffix keeps 3050 DH and 3050 CSMT unique. Duplicates fail the check |

Sources: hunker.com/13413981, greenbuildingadvisor.com/article/getting-window-rough-opening-dimensions-right, finehomebuilding.com/forum/most-common-errors-made-by-an-archy, marvin.com ADM product performance PDF.

### Proposed operation list (for `manual`)

Windows: `CSMT` casement, `AWN` awning, `DH` double hung, `SH` single hung, `SLDR` slider, `FIX` fixed.
Doors: size only for swing doors (`3080`), plus `PKT` pocket, `BARN` barn, `BIFOLD` bifold, `SLDR` sliding patio, `FRNCH` pair.

**Decided 2026-11-21:**
- Door swing goes on the **schedule**, not the tag. The tag stays the size code.
- **Tempered and Egress are labeled on the schedule** as columns, driven by instance Yes/No parameters.
- The operation list above is the default in `manual.audit` and can be edited there.

### Schedule: what goes in it

Group by Type Mark, so it reads exactly like the tags. Columns:

**Type Mark · Qty · Manufacturer/Series · Unit Size · Rough Opening · Head Height · Header · Tempered · Egress (+ net clear) · U-factor · SHGC · Remarks**

- U-factor and SHGC: missing values are a common rejection. Jeffco requires SHGC for Manual J even when the prescriptive table doesn't [STD] (jeffco.us DocumentCenter 16088).
- Header: size, or "see framing plan".
- **Idea worth building:** a small "Openings on this level" schedule (Type Mark, RO, head height) placed on each floor plan sheet, so framers never leave the sheet [OPINION, GBA].

The sample job schedules today have Width, Height, Type and Type Comments. No Type Mark, no count, no RO, no tempered, egress, U or SHGC.

---

## 2. Egress and tempered: the code checks behind the tags

**Egress, IRC R310 [STD]:**
- net clear opening at least 5.7 sf (5.0 sf at grade floor)
- clear height at least 24" and clear width at least 20"
- sill no more than 44" above finished floor
- window well at least 9 sf with 36" projection and width, and a ladder when it's deeper than 44"

A size code can't prove egress. The tool needs **net clear W/H from manufacturer data** as type parameters. Check: every bedroom and every habitable basement has at least one Egress = Yes unit that passes.

**Tempered, IRC R308.4 [STD]:**
- glazing in doors
- glazing within a 24" arc of a door edge with its bottom edge under 60"
- glazing at tubs and showers under 60" and within 60" horizontally of the water
- glazing at stairs and landings with its bottom edge under 36", within 60" of the bottom tread
- large panes over 9 sf, bottom edge under 18", top edge over 36", walking surface within 36"

The tool can find candidates by geometry: units near doors, in rooms with tubs or showers, near stairs, with low sills. Then it asks the designer.

---

## 3. Sheet set organization

- **NCS format is `A-101`** [STD]. Discipline letter, hyphen, sheet-type digit, two-digit sequence.
  - Sheet-type digits: 0 general, 1 plans, 2 elevations, 3 sections, 4 enlarged, 5 details, 6 schedules, 9 3D.
  - Source: archtoolbox.com/construction-document-sheet-numbers.
- Your sets use `A1`, `S1`, `CS`. That's common small-firm practice and not NCS. NCS doesn't require small firms to comply. The cost of staying: sorting breaks past 9 (`A11` sorts before `A2` in plain text), and "A12 = interior elevations" carries no meaning a sub can read.
- **Discipline order:** NCS puts S before A. Most residential firms run A then S. Either is fine. Pick one and lock it.
- **Every sheet [STD, PPRBD]:** builder, designer and engineer names, addresses and phones, plus the full site address. Jeffco adds the design criteria (IRC Table R301.2(1)) and code edition on at least one stamped sheet.
- **Cover sheet:**
  - address and legal description
  - tax schedule number
  - owner and team
  - code list
  - design criteria table (snow, wind, seismic, frost, winter design temp)
  - square footage by area
  - **scope of work** (PPRBD's #4 rejection is unclear scope)
  - sheet index
  - abbreviations and symbols
  - general notes, including dimension basis and "do not scale"
- **Checks:**
  - the sheet index matches the set exactly
  - no duplicate numbers
  - numbers match the office pattern

**Decided 2026-11-21: keep `A1` numbering.** The drafter sorts sheets by discipline and number itself, not by text.

---

## 4. Dimensions

- **Basis:** face of stud is the residential norm [PRACTICE]. "When you're framing walls there's no drywall on them" (finehomebuilding.com/forum/dimensions-on-drawings; greenbuildingadvisor.com/question/plan-dimensions).
  - The alternatives: face of sheathing (so the wall lines up with the foundation) and openings to centerline. Builders split on those.
  - Everyone agrees on stating the basis in a general note.
- **Three exterior strings, outermost to innermost:** overall, jogs and corners, openings. The drafter's `propose_dimensions` already builds these.
- **Strings must close.** The segments add up to the overall. Builders' top complaint [OPINION, consistent]. This is a hard check.
- **No gaps in a string.** Every interior wall is located from a fixed reference. Islands and half walls are dimensioned both ways.
- **Plans stack.** Upper bearing walls sit over lower walls, beams or the foundation [OPINION, "can cost thousands"].
- **The foundation plan gets its own strings**, to face of concrete, including ledges.
- **Elevations show** plate and floor heights, window head heights, roof pitch and finished grade [STD, PPRBD].

---

## 5. Readability

- **PPRBD [STD]:**
  - plans 1/8" = 1'-0" minimum, 1/4" preferred
  - **lettering 1/8" minimum or the plans can be rejected**
  - electrical and plumbing plans held to the same
- **This hits the office template.** The standard dimension types are 3/32" (`3/32" ALLEGRO ARCH` and the others), and several text types are 3/32". Those are under PPRBD's minimum. Check the actual text heights in the types. Names can lie.
- Scales [PRACTICE, fits PPRBD]:

  | View | Scale |
  |---|---|
  | Plans | 1/4" |
  | Elevations | 1/4" |
  | Building sections | 1/4" to 3/8" |
  | Wall sections | 3/4" to 1" |
  | Details | 1-1/2" to 3" |
  | Site | engineer scale, PPRBD suggests 1" = 20' |

- **Notes:** plain text notes on plans beat keynotes for residential. Contractors want the answer where they're looking [OPINION]. Keep keynotes for dense detail sheets.
- **Cross-references:** every section and detail tag reads view/sheet and resolves to a placed view. No typed references like `SEE 4/F2` (the sample job has one in the pier legend). No orphan details.
- **On every plan:** north arrow and graphic scale bar. Sets get reprinted at random sizes.
- **Every room:** name and area. PPRBD wants the area of each habitable room.

---

## 6. What reviewers reject most

**PPRBD's top 5** [STD] (pprbd.org Plan Review - Residential Construction handout):
1. missing required documents
2. missing details, dimensions, materials and sizes
3. wrong design criteria or code edition (trusses to the wrong code, loads that don't match)
4. unclear scope
5. missing PE stamp on non-prescriptive elements (steel beams, ICF above grade, SIPs, post-and-beam, log)

**Also common:**
- **Energy:**
  - R-values not shown per assembly ("R-19/30" is rejected by Jeffco)
  - U/SHGC missing
  - Manual J doesn't match the energy report
  - the wall section's insulation doesn't match the certificate
- **Egress:** a bedroom without a passing unit. Wells not sized.
- **Life safety:** smoke and CO alarms not located. Colorado requires CO within 15' of each bedroom entrance. Garage separation not detailed.
- **Stairs:** rise, run, headroom, handrail and guard not dimensioned in a section.
- **Structural:**
  - header and beam sizes missing
  - king and trimmer counts missing
  - hangers and holdowns not located
  - braced wall panels not marked (R602.10)
  - load path not shown
- **Trusses:**
  - marks don't match the stamped truss package
  - girder plies missing
  - PPRBD: truss layouts don't replace framing plans
- **Floor plan:**
  - furnace and water heater fuel and BTU
  - attic and crawl access
  - fireplace as a listed appliance
  - floor drain
- **Colorado electric-ready / solar-ready:**
  - conduit path from roof to panel
  - EV-ready space
  - a 2-pole breaker space for each gas appliance

**Code editions vary by jurisdiction.** PPRBD is on 2021 IECC. Jeffco moved to 2024 on 7/1/2024. Don't hard-code one edition. The sample job's county legend says 2018. **Deferred 2026-11-21:** code edition checks are out of scope for now. Pull climate zones per county from IECC Table R301.1: Park, Teller and Archuleta are colder than El Paso.

---

## 7. Buildability checklist (what the drafter should check)

Sorted by what the add-in can check today, from data it already reads.

### Can check now (data is in Revit)
1. Type Mark parses as `WWHH OPER` and matches type Width/Height within 1/2". **(Catches `6060 CMST` today.)**
2. Operation code is in the office list. No duplicate Type Marks.
3. Door and window schedules include Type Mark and Qty, grouped by Type Mark.
4. Every door and window instance is tagged on a plan. Exterior units are also tagged on an elevation.
5. Every legend wall type exists in the model, and every modeled wall type is in the legend. **(Fails today: 3 of 5.)**
6. Sheet index matches the set. No duplicate numbers. Numbers fit the pattern.
7. Every plan, elevation and section view is at 1/4" or bigger. None below 1/8".
8. Every text and dimension type used on sheets is at least 1/8".
9. Every placed room has a name, a real number and an area. No unplaced or redundant rooms. **(Fails today: 123 unplaced.)**
10. Every door has a From or To room (it catches missing room boundaries).
11. Every placed view uses an office view template. **(Fails today: 0 of 302.)**
12. Every bedroom has at least one window with Egress = Yes. Sill is 44" max.
13. Tempered candidates are flagged: glass in doors, near doors, near tubs and showers, near stairs, low sills.
14. Every section and detail on a sheet is referenced by a live tag. No typed `SEE x/Fy` references.
15. Cover sheet has code list, design criteria, scope, area, sheet index and size-code legend.

### Needs new parameters or data
16. RO, net clear W/H, U-factor and SHGC per type, from manufacturer data.
17. Window U-factor is at or below the energy certificate value.
18. Header size per opening, or a reference to the framing plan.

### Needs geometry work (harder)
19. Dimension strings close to their overall within 1/16".
20. No gaps in strings. Every interior wall is located.
21. Bearing walls stack between levels.
22. Opening plus casing fits the wall segment.

### Designer judgment (the tool asks, it doesn't decide)
23. Structural:
    - braced wall panels
    - truss marks
    - beam sizes
    - PE stamp triggers (steel, ICF above grade, over-IRC spans)
24. Stair section, smoke/CO locations, mechanical fuel/BTU, attic access, EV and solar ready.

---

## 8. Recommended build order for the drafter

1. **Office rules into `manual`:**
   - size-code pattern and operation list
   - schedule column standard
   - text and dimension type whitelist (1/8" minimum)
   - sheet pattern
2. **An `audit_set` read-only tool** that runs checks 1-15 and returns pass/fail per item, with element IDs. Nothing changes in the model. It's the fastest win, and it's the "expert drafter" voice: it tells you what's wrong before the reviewer does. **Built 2026-11-21** (16 checks, see SETUP.md "Set audit"). Not yet run inside Revit.
3. **Proposals that fix what the audit finds:**
   - Type Mark corrections
   - schedule rebuild with Type Mark and Qty
   - room cleanup
   - view template assignment

   Each fix goes through the approval pane as usual.
4. **Manufacturer data** (RO, net clear, U, SHGC) as type parameters. Then checks 16-18 and egress by the numbers.
5. Geometry checks 19-22 last.

The designer still decides every fix. The tool finds problems faster than a person can, and the designer decides what to do about them.
