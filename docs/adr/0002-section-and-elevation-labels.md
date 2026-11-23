# ADR 0002: Section and elevation labels as tags, not text

Status: Proposed (2026-11-22). Needs Bryan's label rules (see Open questions).

## Context

Today sections and elevations are labeled with text notes: "2x6 STUDS @ 16" O.C.", "R-21 BATT", "BOARD & BATTEN SIDING", "STANDING SEAM METAL ROOF". Text notes:

- don't know what they point at, so they go stale when a wall type or material changes
- can't be checked by the audit (it can't tell what a note is labeling)
- have to be typed per view, per job

The designer builds the sections. The tool labels them per office rules, and tags the elevations.

## Decision: the words live on the type or material, a tag shows them

The label text stays exactly what the office writes today. It just moves from the note into a parameter, and a tag displays it.

| What's labeled | Where the words live | Tag that shows them |
|---|---|---|
| Wall assembly (sections) | Wall type, shared text param `Allegro Assembly Note` | Wall tag `ALLEGRO ASSEMBLY` |
| Roof / floor / ceiling assembly | Same param on roof, floor, ceiling types | Roof / floor / ceiling tags `ALLEGRO ASSEMBLY` |
| Single layer (insulation, sheathing, gyp) | Material, shared text param `Allegro Callout` (fallback: Description) | Material tag `ALLEGRO MATERIAL` |
| Elevation finishes (siding, stone, roofing, trim) | Material `Allegro Callout` of the exterior finish layer | Material tag `ALLEGRO MATERIAL` |
| Elevation doors/windows | Type Mark (already done) | Existing door/window tags |
| Plate / bearing heights in sections | Level name + elevation | Level heads (no new tag) |

Why this is the best way:

- **Change once, every view updates.** Edit the wall type note and every section on every sheet reads the new text.
- **The audit can check it.** "Wall type in section A has no assembly note" becomes a real finding. So does "tag text disagrees with the energy legend R-value."
- **It's deterministic.** Which tag goes where is a rule: one assembly tag per wall/roof/floor type cut in the section, one material tag per exterior finish visible in the elevation.
- **It looks the same.** Office text type and arrowhead in the tag family, so the sheet reads like it does now.
- **Templates carry it.** Notes live on types in the office template, so every new job starts labeled. That's real time saved, not just on this job.

Keynotes were considered and rejected: the office uses Autodesk's stock keynote file, and keynote numbers plus a legend is a different look than what the office issues. Keynote tags can show keynote text, but then the words live in a separate text file that has to be maintained. Type and material parameters are simpler.

## Placement rules (deterministic)

- **Sections:** tags stack in a note column outside the building outline, on the side the designer picks per view (default: right). Sorted top to bottom by the height of the element they point at, fixed spacing from the office text height, straight or one-bend leaders. No overlaps by construction.
- **Elevations:** one material tag per finish per facade, leader to the largest visible face of that material, tag above the eave or beside the wall per the office rule. Doors/windows keep the current exterior-only, facing-the-view filter.
- **Crowded views:** if the column can't fit (too many items for the height), the batch says so and offers groupings from the office rules (e.g. combine sheathing + WRB into the assembly note) as a question card. Designer picks.

## New tools and steps

1. `propose_parameters`: add `Allegro Assembly Note` (types) and `Allegro Callout` (materials) from the Allegro shared parameter file.
2. `propose_type_notes`: fill the notes, seeded from the office's existing text notes, matched to the type each one points at. The designer approves the mapping once in the template.
3. `propose_section_labels` / `propose_elevation_labels`: tags per the placement rules, one view per batch.
4. Audit checks: `section-labels` (every cut type labeled), `elevation-labels` (every exterior finish labeled), `label-drift` (leftover text notes that duplicate a tag).
5. Finish Drafting runs these as steps after tags (ADR 0001, step 8).

Mapping the office's existing section text notes to type notes is a one-time template job Bryan runs with Claude Code. The button itself stays rules only.

## Verify in Revit before building

- Multi-line text parameters in tag labels in Revit 2025/2026 (line breaks in the note, label wrapping).
- Material tags placed by API on a specific layer in a section, and on wall faces in elevations.
- Tag leaders landing on cut faces in sections.

## Open questions for Bryan

1. Send one finished, issued section and one elevation sheet (PDF) that's labeled the way you want. That's the rulebook.
2. Sections: what has to be labeled every time? (Assemblies, insulation R-values, plate heights, bearing, headers, flashing?)
3. Elevations: finishes only, or also plate heights, roof pitch, grade?
4. "Label per my rules" for section views themselves: naming (`BUILDING SECTION 1` vs `SECTION A`), and section head numbering on sheets. Same rules for every job?
