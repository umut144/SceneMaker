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
| `APP-02` | Application | Manual acceptance of the editor after the Inspector split and the Outliner: the River tools (`Insert Point`, `Select River` including the red selection when a drag breaks a junction; `Create Branch` is done), the `New group` / `Remove group` controls, the panel headers and rows in every mode, the `Eraser` in its new place, the view toggles as an overlay with the legend below them, and the Outliner - nesting, the visibility ticks, selecting by name, and the divider. App code has no tests; this is the only thing that checks it. [Notes](DESIGN_NOTES.md#the-panel-says-what-the-values-belong-to) | **Ready** |
| `PATH-02` | Paths | Make the red authoring wire pickable and emphasize a selected Path; manual acceptance of an additive-to-tunnel-to-additive Path through a hill. [Notes](DESIGN_NOTES.md#horizontal-sections-and-paths-that-excavate-terrain) | **Ready** |

## Optional Later — Paths and sections

The horizontal Section view, `LayeredSceneColumns` and the per-segment
subtractive operation are in place. What remains beyond the active slices:

| ID | Area | Outcome | Status |
|---|---|---|---|
| `PATH-03` | Paths | Selection and per-point reshaping of an existing Path, including Bezier handles, as editing detail rather than re-authoring. [Notes](DESIGN_NOTES.md#horizontal-sections-and-paths-that-excavate-terrain) | **Optional / Later** |
| `PATH-04` | Paths | Define overlapping-station semantics so a helix stays unambiguous; exactly overlapping Paths must not be disambiguated by an arbitrary semantic choice. [Notes](DESIGN_NOTES.md#horizontal-sections-and-paths-that-excavate-terrain) | **Optional / Later** |
| `PATH-05` | Paths | A ramp down into a river bed, first authored on `fork01`. World01 asked for it there rather than in a map: a ramp puts a Path cut and a water cut in one column, which is the hardest corner of their column rule and the one they would rather meet with nothing else standing next to it. Wanted only once a Path can be drawn into a bed at all. | **Optional / Later** |
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
| `BRIDGE-04` | Structures | Let a Scene Template carry a bridge instead of refusing it: composition moves Terrain cells and Props and nothing else today, so a Template's bridge would be lost at every Anchor. Needs the composition rule for an independent band and its posts, and an export-contract answer for a Template's bridge landing on an Instance. [Notes](DESIGN_NOTES.md#not-in-the-first-slice) | **Optional / Later** |
| `BRIDGE-05` | Structures | Show the author what each end of a selected bridge stands over — the same `ground_at_start` / `ground_at_end` the export ships — so a bridge into the river is visible while it is being drawn, not first in the consumer's error log. [Notes](DESIGN_NOTES.md#what-is-built) | **Optional / Later** |
| `TPL-01` | Templates | Let a Scene Template carry water instead of refusing it; requires an export-contract answer for a Template's water landing on an Instance's. [Notes](DESIGN_NOTES.md#a-scene-template-cannot-carry-water) | **Optional / Later** |
| `WATER-02` | Export contract | Carry a list of a group's former names so a rename is reported as "now called X" rather than as an unknown group; world01 offered it and declined it themselves, because their sync already reports an unknown name loudly. [Notes](DESIGN_NOTES.md#what-is-still-open) | **Deliberately deferred** |
| `ID-01` | Documents | Widen the Instance ID pad so IDs keep reading in placement order past 9 999; a document migration, worth doing only if a Scene approaches that many Props of one asset. [Notes](DESIGN_NOTES.md#instance-ids-and-document-ordering) | **Optional / Later** |

## Optional Later — Authoring a switchable river

`WATER-01` gives the document and the export their shape. Nothing in the editor
yet draws a branch, selects a body, or reads activation at all: the Canvas and
the Section view draw every body whatever its state, so an author sees a map the
game never shows. A switchable river is therefore authorable only by hand.

| ID | Area | Outcome | Status |
|---|---|---|---|
| `FLOW-01` | Water | A stored flow network in front of the authored curves: discharge normalised to the root, conserved at forks, and mapped to width and depth by the downstream hydraulic exponents, baked into ordinary bodies so the export learns nothing. Reusable for anything that branches and thins - cracks - once its node rule, segment rule and exponent are parameters. Wants the detach mark solved first: a bake that silently overwrites a hand edit is worse than no generator. [Notes](DESIGN_NOTES.md#a-flow-network-in-front-of-the-authored-curves) | **Optional / Later** |
| `WATER-10` | Views | Show while drawing whether a stretch of river is a ford: bed at most a step below the bank **and** water at most a wade above the bed, both answerable from the Terrain top, `bed_meters` and `surface_meters` this already derives per water cell. World01 gives 0.5 m and 0.4 m as Workspace values, with two conditions: those are the reference character's numbers and not law - theirs sit per character so one may differ - and this **shows, never validates**. A river nobody can cross is the normal case, so a check that forced a ford would forbid good maps; that is the opposite of the width budget, where a rule has to be enforced because it is a rule. | **Optional / Later** |
| `WATER-09` | Views | Draw a river's corridor as nested bands showing what each branch above takes out of it - the onion the author sketched. Derived from the junction tree and the authored widths, so it is a view and never geometry; widths stay uncoupled, see the note. [Notes](DESIGN_NOTES.md#widths-are-not-coupled-and-that-is-what-keeps-the-tree-cheap) | **Optional / Later** |
| `OUTLINE-02` | Views | A per-group state selector in the Outliner that stands the Scene in one state and draws only the bodies it has there, without editing anything. Today the Canvas draws every body whatever its activation says, so an author sees a map the game never shows. `WaterActivation.ActiveBodies` already answers it; what is missing is the chooser and passing its answer down the draw. [Notes](DESIGN_NOTES.md#a-branch-is-a-body-and-a-state-switches-which-bodies-there-are) | **Ready** |
| `WATER-06` | Water | Author a river point's handles and its `Linear`/`Aligned` mode after it is drawn. The bar's `Point` decides what the next drawn point does and says nothing about a selected one; the Inspector's selected-point row exists for Hills only, because a river point's handles cannot be edited yet. This is the row that is missing. Shares its shape with `PATH-03`. | **Optional / Later** |
| `WATER-11` | Water | Rename a group, add or drop a state, and put a body in several states at once. Declaring and removing a group is done; these three are the rest of `WATER-07`, and they want the Outliner's picture - a list of bodies against a list of states - rather than another field in a bar that describes one body. A rename has to carry every member with it, which is the half that is dangerous by hand. [Notes](DESIGN_NOTES.md#selecting-a-river-and-the-gesture-that-was-left-over) | **Optional / Later** |

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
| `BRIDGE-06` | Export contract | A deck's material is settled: a deck is a row of planks, so it names a Placement Asset carrying `surface: "wood"` and no Terrain Asset is involved. | **Settled** |

## Tracker rules

- Keep at most one task **In progress**.
- Describe an observable outcome, not an implementation diary.
- Add acceptance detail only when needed to decide task completion.
- Keep the reasoning in `DESIGN_NOTES.md`; a row stays one sentence and a link.
- Remove completed rows after the immediate handoff; Git preserves their
  history. A decision that closes a question moves to `DESIGN_NOTES.md` under
  Settled decisions instead of being deleted.
- Add a finding here rather than leaving it undocumented.
