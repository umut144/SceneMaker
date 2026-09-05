# SceneMaker Tasks

This file tracks only current, next, blocked, or deliberately deferred outcomes.
Completed implementation history remains available in Git.

Each row links to `DESIGN_NOTES.md`, which holds the reasoning: what the rough
edge costs to leave alone, what shape a fix has to take, and which alternatives
were considered and rejected. Read `AGENTS.md` first — the rules there are what
every fix has to stay inside.

## Active Tasks

| ID | Area | Outcome | Status |
|---|---|---|---|
| `PATH-05` | Export contract | Raise world01's reader to export 11: the new `operation`/`clearance_above_meters` on every route segment and the `route_surface_cut_raster` beside the Scene. Lives in `BevyProjects/world01`, not here. [Notes](DESIGN_NOTES.md#horizontal-sections-and-paths-that-excavate-terrain) | **Ready** |
| `PATH-02` | Paths | Make the red authoring wire pickable and emphasize a selected Path; manual acceptance of an additive-to-tunnel-to-additive Path through a hill. [Notes](DESIGN_NOTES.md#horizontal-sections-and-paths-that-excavate-terrain) | **Ready** |
| `BRIDGE-02` | Structures | A `Structures` area with `Draw Bridge`: a straight horizontal deck authored as its own record, four derived corner posts from the anchor Asset's named Component, and Scene Templates refusing to carry one. [Notes](DESIGN_NOTES.md#bridges-a-straight-span-that-sets-its-own-posts) | **Ready** |

## Optional Later — Paths and sections

The horizontal Section view, `LayeredSceneColumns` and the per-segment
subtractive operation are in place. What remains beyond the active slices:

| ID | Area | Outcome | Status |
|---|---|---|---|
| `PATH-03` | Paths | Selection and per-point reshaping of an existing Path, including Bezier handles, as editing detail rather than re-authoring. [Notes](DESIGN_NOTES.md#horizontal-sections-and-paths-that-excavate-terrain) | **Optional / Later** |
| `PATH-04` | Paths | Define overlapping-station semantics so a helix stays unambiguous; exactly overlapping Paths must not be disambiguated by an arbitrary semantic choice. [Notes](DESIGN_NOTES.md#horizontal-sections-and-paths-that-excavate-terrain) | **Optional / Later** |
| `SECT-01` | Views | Separate profile-section tool projecting a marked region onto X-Z or Y-Z to inspect stacked tunnels, clearance, ramps and water spans. [Notes](DESIGN_NOTES.md#horizontal-sections-and-paths-that-excavate-terrain) | **Optional / Later** |

## Optional Later — Height authoring and analysis

The height view is a continuous auto-stretched ramp; height is authored only by
repainting a cell.

| ID | Area | Outcome | Status |
|---|---|---|---|
| `HEIGHT-01` | Height authoring | Raise or lower an existing region without rewriting its Asset: a height brush that leaves the Asset alone, plus a numeric readout on the hovered cell. [Notes](DESIGN_NOTES.md#authoring-height) | **Optional / Later** |
| `HEIGHT-02` | Height authoring | Change a Scene's `default_elevation_meters` after creation, in the context bar next to `Height`, through `EditorController.Apply` and the undo history. [Notes](DESIGN_NOTES.md#authoring-height) | **Optional / Later** |
| `HEIGHT-03` | Height view | Optional author-selected low/high range so one outlying hill does not compress every useful height into one colour. [Notes](DESIGN_NOTES.md#height-analysis-and-a-first-person-layered-3d-preview) | **Optional / Later** |
| `HEIGHT-04` | Height view | Discrete elevation bands and arbitrary multi-selection of elevations, so chosen floors are highlighted while the rest is muted. [Notes](DESIGN_NOTES.md#height-analysis-and-a-first-person-layered-3d-preview) | **Optional / Later** |
| `HEIGHT-05` | Height view | Traversal overlay comparing neighbouring floor spans, headroom and surface compatibility for a selected Actor profile. [Notes](DESIGN_NOTES.md#height-analysis-and-a-first-person-layered-3d-preview) | **Optional / Later** |
| `GRADE-01` | Height authoring | Grade tool distributing a start and end elevation across cells in quantum-sized increments; the height view stays inspection and never edits silently. [Notes](DESIGN_NOTES.md#authoring-height) | **Optional / Later** |
| `LOOK-01` | Preview | Generated first-person Layered-3D LookDev mode with collision and an Actor profile, exposing headroom, unreachable surfaces and passage under a bridge. [Notes](DESIGN_NOTES.md#height-analysis-and-a-first-person-layered-3d-preview) | **Optional / Later** |

## Optional Later — Documents and export contract

| ID | Area | Outcome | Status |
|---|---|---|---|
| `BRIDGE-03` | Structures | Selection and reshaping of an existing bridge — both ends, width and height — as editing detail rather than re-authoring. [Notes](DESIGN_NOTES.md#not-in-the-first-slice) | **Optional / Later** |
| `TPL-01` | Templates | Let a Scene Template carry water instead of refusing it; requires an export-contract answer for a Template's water landing on an Instance's. [Notes](DESIGN_NOTES.md#a-scene-template-cannot-carry-water) | **Optional / Later** |
| `ID-01` | Documents | Widen the Instance ID pad so IDs keep reading in placement order past 9 999; a document migration, worth doing only if a Scene approaches that many Props of one asset. [Notes](DESIGN_NOTES.md#instance-ids-and-document-ordering) | **Optional / Later** |

## Optional Later — Application code

| ID | Area | Outcome | Status |
|---|---|---|---|
| `APP-01` | Application | Move the initial asset selection out of `BuildPropAssetBar` into adopting the session, so layout construction stops deciding editor state. App code without tests; wants a manual pass. [Notes](DESIGN_NOTES.md#the-prop-asset-bar-selects-an-asset-as-a-side-effect) | **Optional / Later** |

## Deliberate non-goals

Settled decisions, kept so they are not rediscovered as findings. Their full
reasoning is under [Settled decisions](DESIGN_NOTES.md#settled-decisions).

| ID | Area | Outcome | Status |
|---|---|---|---|
| `ASSET-01` | Workspace config | Adding Assets through the application is not planned; an agent edits and validates the Workspace configuration instead. | **Deliberately not planned** |
| `NAME-01` | Documents | The editor says Placements while internal `Prop*` types and the persisted `props` array stay unchanged; renaming is a migration with no runtime benefit. | **Deliberately left alone** |
| `TRANS-01` | Documents | Transitions wait for their own simulation-owned target/region contract with a consumer and tests, rather than a second copy of `PropDocument`. | **Deliberately deferred** |
| `BRIDGE-01` | Export contract | `asset_profiles[].traversable_surface` is not coming: a bridge deck is an independent Path-like surface, so no Prop needs to offer a walkable one. | **Superseded** |

## Tracker rules

- Keep at most one task **In progress**.
- Describe an observable outcome, not an implementation diary.
- Add acceptance detail only when needed to decide task completion.
- Keep the reasoning in `DESIGN_NOTES.md`; a row stays one sentence and a link.
- Remove completed rows after the immediate handoff; Git preserves their
  history. A decision that closes a question moves to `DESIGN_NOTES.md` under
  Settled decisions instead of being deleted.
- Add a finding here rather than leaving it undocumented.
