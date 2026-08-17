# SceneMaker MVP checklist

This checklist orders small accepted outcomes. Complete and commit one slice at
a time. Do not pull later features into an earlier slice merely because their
future shape is known.

## Slice 0 — Local context

Acceptance outcome: future work can recover the agreed boundaries without
reconstructing the design conversation.

- [x] Add `AGENT.md` with local working and verification rules.
- [x] Add `AI_CONTEXT.md` with the accepted coordinate and ownership model.
- [x] Add this ordered MVP checklist.
- [x] Record that active game contracts remain authoritative until explicitly
  versioned by a focused slice.

## Slice 1 — Standalone foundation and Canvas

Acceptance outcome: SceneMaker starts as an independent Godot/C# application,
creates or loads one small workspace and fixed-size scene, and navigates a
semantic Canvas without authoring content yet.

- [x] Create `tools/SceneMaker/project.godot` and a standalone C# project using
  the repository's accepted Godot/.NET versions.
- [x] Keep engine-neutral document models and validation outside Godot Nodes.
- [x] Define the smallest versioned Workspace and editable Scene documents.
- [x] Give every Scene a stable ID, fixed positive `size_cells`, and fixed
  top-left/Y-down local coordinates in the original version-1 contract.
- [x] Save atomically and reject unknown, malformed, or out-of-bounds data.
- [x] Build the UI shell: Data Bar, left Tool Bar, Canvas, and separate Settings
  button.
- [x] Show the initial `Terrain | Placements | Scene Instances` data tabs and
  mark Scene Instances unavailable rather than pretending to implement it.
- [x] Draw the 16-authoring-pixel-per-cell grid without creating a Node per
  cell or authoring pixel.
- [x] Implement Q/E zoom and WASD pan as view-only operations.
- [x] Prove navigation cannot change serialized scene data.
- [x] Add unit tests for Workspace/Scene round trips and invalid inputs.
- [x] Add a short headless SceneMaker startup test.
- [x] Complete a manual startup, resizing, zoom, and pan acceptance pass.

Explicitly excluded: World Asset schema changes, painting, Fill, Placements,
Scene Instances, Chunk Helper, and runtime export.

## Slice 2 — Terrain Paint

Acceptance outcome: one fixed-size scene can paint Grass and Water on the
existing 0.5 m WorldGrid cells, save, reload, and reproduce the same data.

- [x] Read the validated World Asset catalog rather than defining a SceneMaker
  Asset registry copy.
- [x] Add the smallest closed SceneMaker-owned Terrain display configuration;
  reference only canonical `asset_id` values and duplicate no Asset identity
  or semantics.
- [x] Enable only reviewed Terrain Assets required by this slice.
- [x] Populate the hierarchical Terrain Data Bar from active supported Assets.
- [x] Use the catalog's stable `asset_id` and name; assign any SceneMaker-only
  display color outside the World Asset catalog.
- [x] Implement one Paint tool with one-Cell Terrain semantics.
- [x] Reject painting outside `size_cells`.
- [x] Store one semantic Terrain Asset ID per occupied Cell without Godot
  TileMap persistence as source of truth.
- [x] Canonically order serialized Terrain cells.
- [x] Verify byte-stable save/load/save output.
- [x] Add automated Paint, bounds, catalog, and persistence tests.
- [x] Complete a manual Grass/Water painting feedback pass.
- [x] Persist and automatically restore the last active Workspace and its last
  active/edited Scene as separate UI session state.
- [x] Complete a manual recent-session restore and dialog-start-directory pass.

Explicitly excluded: Fill, placement footprints, and runtime export.

## Slice 3 — First Placement Asset: Tree

Acceptance outcome: after a user-reviewed Tree extent and anchor decision,
SceneMaker places Tree anchors on individual authoring pixels and derives the
conservative footprint from the accepted physical extent.

Decision gate before implementation:

- [x] Inspect all existing Tree size, collision, Debug, and presentation
  evidence without treating any incidental value as authoritative.
- [x] Ask the user to choose/review Tree physical width and height.
- [x] Ask the user to choose/review the Tree Asset anchor.
- [x] Record the accepted values in one versioned World Asset spatial contract.
- [x] Decide and document the common extent/anchor JSON representation and
  units for this first real Asset.
- [x] Version SceneMaker Scenes to bottom-left/+Y-up, migrate version-1/2
  documents deterministically, and keep Godot's Y-down Canvas projection-only.

Implementation and verification:

- [x] Extend strict World Asset schemas and the pure C# loader deliberately.
- [x] Make SceneMaker support explicit through its closed tool-local display
  configuration without adding producer metadata to the World Asset catalog.
- [x] Keep all other incomplete Placement Assets unavailable.
- [x] Derive Tree footprint using `ceil(extent_meters * 32)`; store no manual
  `placement_size_px`.
- [x] Store stable Placement instance IDs, `asset_id`, and integer anchor
  `position_authoring_px` only.
- [x] Reject invalid metadata, out-of-scene footprints, and overlapping
  footprints according to the accepted MVP rule.
- [x] Ensure selection supplies the Asset-derived footprint automatically;
  provide no Brush Size control or fallback.
- [x] Replace the explanatory left panel with a narrow text-free icon toolbar
  for Selector, Eraser, Pencil Draw, and Line Draw.
- [x] Add footprint-spaced Placement Line Draw with a live pointer preview and
  functional footprint selection/erasure for the manual Tree-band pass.
- [x] Refine Pencil and Line into non-modal preview workflows: yellow valid
  candidates, individually red blocked candidates, two-click Line endpoints,
  staged Escape rollback, and Enter-only persistence after endpoint lock.
- [x] Prove save/load preserves non-Cell-aligned odd coordinates such as
  `(69, 27)` exactly while respecting the Tree's in-Scene footprint.
- [x] Prove anchor delta `N` maps to exactly `N / 32 m` in simulation space and
  `N * 4` pixels in the standard game projection.
- [x] Prove no placement path reads sprite, Presentation Region, Debug, or
  Collision bounds to reposition instances.
- [x] Run a manual Tree-band test and iterate with the user on extent, anchor,
  preview validation, and staged Line Draw.
- [x] Mark Tree SceneMaker-ready only after manual acceptance.

Explicitly excluded: automatic jitter, rotation, other Placement Assets, and
runtime consumption.

## Retired intermediate — Scene Source export

The former per-Scene `srt.scene_maker_source` export established early
producer-neutral validation before the Runtime Scene Package existed. It had
no active consumer and duplicated the editable Scene shape, so the action,
contract, loader, and dedicated tests were removed after the complete Runtime
Package path was accepted. Editable `.scene.json` documents remain the
SceneMaker source of truth.

## Slice 5 — First Transition Asset: Portal

Acceptance outcome: SceneMaker authors reviewed Portal Transition instances in
a separate root collection, prevents every spatial overlap, and persists their
exact anchors without claiming runtime startup integration yet.

- [x] Record Portal's reviewed `2 m × 3 m` physical extent and bottom-center
  `(1 m, 0 m)` anchor in its World Asset definition.
- [x] Enable only Portal through a closed SceneMaker Transition display
  configuration without creating a second Asset registry.
- [x] Version SceneMaker scenes to add a canonical `transitions` collection and
  migrate version-3 scenes deterministically to an empty collection.
- [x] Add the `Transitions` perspective between Placements and Scene Instances.
- [x] Reuse Selector, Eraser, Pencil Draw, and staged Line Draw with the same
  yellow/red preview contract.
- [x] Persist stable Transition instance IDs, Asset IDs, and integer anchor
  authoring pixels only.
- [x] Derive Portal's `64 × 96` authoring footprint and `(32, 0)` anchor from
  the World Asset contract.
- [x] Permit touching hulls but reject Transition/Transition and
  Transition/Placement overlap in either authoring direction.
- [x] Version editable Scene documents with a separate exact `transitions`
  collection.
- [x] Keep startup-scene derivation, Portal open/close state, and server-local
  player distribution out of the authoring slice.
- [x] Add automated catalog, bounds, overlap, tools, migration, and round-trip
  coverage.
- [x] Remove the unused Height placeholder and render Terrain, Placements, and
  Transitions together while highlighting and editing only the active tab.
- [x] Keep Terrain outside Placement/Transition footprint collision.

## Slice 6 — Generic Scene-backed simulation World

Acceptance outcome: the game accepts SceneMaker output through a reviewed,
versioned pure-C# contract; startup and authoritative instanced Spaces resolve
arbitrary configured scene IDs without a mandatory Macro Map or injected map
content.

This slice includes the project-wide coordinate migration from the currently
active top-left/+Y-down World contract to the owner-selected bottom-left/+Y-up
logical contract. SceneMaker version 4 already uses the target convention and
must not be silently reinterpreted while the runtime is still transitional.

- [x] Define a producer-neutral runtime Scene Package that represents arbitrary
  integer authoring-pixel Placement anchors losslessly.
- [x] Derive the sole Portal-bearing scene as startup Scene rather than
  referencing a fixed Macro Map or fixture; reject zero or multiple
  Portal-bearing scenes.
- [x] Distribute first entries evenly through a deterministic random-looking
  Portal permutation and server-local round-robin cursor; reset it with every
  new non-persistent server.
- [x] Keep Portal instances in place and expose authoritative open/use/closed
  semantics without giving them target-scene links.
- [x] Let each loaded Scene define its own bounds; remove the universal
  `5,000 m x 5,000 m` requirement without changing the `0.5 m` WorldGrid cell.
- [x] Make the active `SimulationCore`, `WorldCollision`, `WorldRules`, and
  Navigation consume generic resolved Scene Terrain semantics while retaining
  resolved Placements for their own later Asset slices.
- [ ] Represent Character and Monster spawn definitions as authored scene data;
  do not inject built-in start positions.
- [x] Remove built-in River, Bridge, Tree, Chest, Ring, Potion, Monster, and
  other map placements after their required Asset contracts and authoring
  slices exist.
- [x] Remove `WorldMacroMap`, its compiler/tests, and
  `design/world_macro_map/` after no active consumer remains.
- [x] Support authoritative instanced Spaces by referencing the same role-free
  Scene IDs; do not introduce a second map schema.
- [x] Never round package positions to 0.5 m or synthesize fake `source_cells`.
- [x] Keep integer package authoring coordinates as exact provenance.
- [x] Export every canonical Scene in the loaded SceneMaker Workspace as one
  atomic, byte-stable Runtime Scene Package without mutating source Scenes.
- [x] Revalidate the complete Workspace export through the pure simulation
  package loader and keep SceneMaker display data out of the output.
- [ ] If redundant meter positions are exported, recompute and reject any
  disagreement in pure C#.
- [ ] Migrate the Suitcase fixture only through an explicit versioned slice.
- [ ] Prove Mirror, Carpet, Threshold, camera, and gameplay behavior remain
  unchanged.
- [x] Update active architecture and World Asset documentation for the 6A
  parallel package boundary; update them again as later runtime consumers land.
- [x] Add pure simulation consumption and automated regression coverage.
- [ ] Complete a focused manual runtime acceptance pass.

Slice 6A is complete at the parallel contract boundary: package schema,
strict pure-C# loading, Asset/geometry validation, Portal-derived startup
Scene, and seeded fair first-entry distribution have automated coverage.
Slice 6B adds a concrete role-neutral `SceneWorldData` projection plus parallel
Scene bounds, Terrain rules, Collision and Navigation queries. It explicitly
blocks unpainted Cells and injects neither legacy River/Bridge geometry nor
built-in objects. Slice 8 later makes that path active; authored Placement
collision semantics and Monster spawn authoring remain unchecked work above.

Slice 6C adds server-local Portal first-entry authority. Portal instances begin
Closed, become observably Open for their completed use tick, retain their exact
authored location, and close on the following tick. Stable Character ordering,
seeded fair assignment, exact anchor positions, semantic lifecycle events, and
consecutive-tick reuse have automated coverage. Active `SimulationCore`
Character creation, networking, persistence, and Godot presentation remain
future work.

Slice 6D closes the concrete SceneMaker producer chain. One Settings action
exports every Workspace Scene to
`design/runtime_scenes/<workspace_id>.runtime-scenes.json`, with SceneMaker support checks
followed by independent simulation validation. Canonical ordering,
byte-stability, multi-Scene inclusion, exact geometry, Portal startup
derivation, hard invalid-package failures, and source non-mutation have
automated coverage. Scene-backed migration Slice 8 now loads this real package
into the active game.

## Slice 8 — Active Scene-backed startup World

Acceptance outcome: local Godot, the authoritative server, and network clients
load a strict game-owned bootstrap, start in authored `scene02`, and never load
the old map or inject its fixed content.

- [x] Add strict `srt.world_bootstrap` version 1 with package path, explicit
  startup Scene, World revision, and Portal-entry seed only.
- [x] Deploy SceneMaker Runtime Packages atomically to
  `design/runtime_scenes/<workspace_id>.runtime-scenes.json`.
- [x] Configure `scene02` as startup Scene and require it to equal the package's
  sole Portal-bearing Scene.
- [x] Make active `SimulationCore` bounds, Terrain, movement rules, Collision,
  and Navigation consume `SceneWorldData`.
- [x] Create Characters through deterministic authored Portal entries and
  validate that every Portal provides a walkable Character footprint.
- [x] Move the provisional Character footprint out of map data into
  `srt.simulation_tuning` version 12.
- [x] Align that explicit footprint exactly with the lower Debug Body circle:
  radius `0.16875 m` at Character-root offset `(0 m, -0.50625 m)` under +Y-up.
- [x] Inject no legacy River, Bridge, World objects, or Pumpkin in the new
  constructor.
- [x] Remove `design/world_data.json` and isolate its old content under the
  automated test fixtures until Slice 12.
- [x] Switch local, server, and network-client Godot startup to the Bootstrap.
- [x] Cover Bootstrap validation, exact `scene02` content, Portal spawning,
  Scene bounds, and absence of injected legacy content in pure C# tests.
- [ ] Complete the focused manual runtime acceptance pass.

## Slice 9 — Scene-backed Godot Debug projection

Acceptance outcome: local and network Godot clients draw the selected Runtime
Scene from its exact semantic coordinates and show authoritative Portal state;
the retired fixed-map projection is not part of active startup.

- [x] Add one game-owned rectangular Debug projection for validated Scene
  Terrain, Tree Placements, Portal Transitions, and Scene bounds.
- [x] Keep SceneMaker colors and editable documents out of runtime; static
  geometry comes only from `SceneWorldData` and resolved World Assets.
- [x] Draw the accepted Tree composition as a centered `2.0 m × 2.5 m` trunk
  plus `3.5 m × 5.0 m` crown with `1.0 m` overlap.
- [x] Draw every Portal at its exact `2 m × 3 m` footprint and authored
  bottom-center anchor.
- [x] Add authoritative Portal state to Core snapshots and protocol version 20
  so local and network projections distinguish `Closed` from `Open`.
- [x] Reject a local-package/network mismatch in Scene ID, Portal ID, Asset ID,
  count, or exact anchor position instead of visually guessing.
- [x] Make the Godot adapter consistently project bottom-left/+Y-up World
  deltas onto top-left/+Y-down Canvas deltas, including Character, controller,
  World fallback, and Overworld Suitcase presentation.
- [x] Remove active `DebugWorldProjection` creation from local and network
  Scene-backed startup; retain the class only with the isolated legacy path.
- [x] Cover Portal open/close replication, exact anchor positions, wire codec,
  and coordinate conversion with pure automated tests.
- [x] Run local and ENet server/client headless startup checks.
- [x] Complete the focused manual visual acceptance pass.

## Slice 10 — First authored Placement collision: Tree

Acceptance outcome: every Tree Placement in any selected Runtime Scene creates
the same reviewed authoritative trunk obstacle, while its larger authoring and
presentation hull remains unchanged.

- [x] Ask the owner to choose Tree collision geometry rather than inheriting
  the obsolete legacy-fixture values.
- [x] Record a `2.0 m × 2.0 m` rectangle with bottom-center `(1.0 m, 0.0 m)`
  collision anchor in Tree's producer-neutral simulation data.
- [x] Keep the `3.5 m × 6.5 m` spatial hull and the `2.0 m × 2.5 m` visible
  trunk unchanged; the upper `0.5 m` of trunk and the crown do not collide.
- [x] Validate collision shape, positive extent, in-bounds anchor, unknown
  fields, and the presence of Tree's reviewed collision as hard catalog rules.
- [x] Resolve every authored Tree anchor into a `ScenePlacementObstacle`
  without allocating a synthetic runtime Entity ID.
- [x] Feed those obstacles into the existing authoritative movement and
  Navigation collision path for every selected Scene.
- [x] Keep static Placement identity and geometry in the validated Runtime
  Scene Package; do not duplicate collision geometry in network snapshots.
- [x] Prove exact obstacle center/size, lower-square movement blocking,
  non-colliding upper hull, Navigation blocking, and all eleven `scene02`
  obstacles in pure tests.
- [ ] Complete the focused manual Tree-collision acceptance pass.

## Slice 11 — Role-free authoritative Scene Spaces

Acceptance outcome: any validated Runtime Package Scene can back an
authoritative portal-free Space through its stable Scene ID, while startup
Portal behavior remains a separate consumer concern and retired WorldData is no
longer part of the public Core API.

- [x] Add `SceneSimulationSpace` with independent stable Space ID, reusable
  Scene ID, non-zero World revision, and explicit Character entry root supplied
  by the consuming server/transition.
- [x] Reuse `SceneWorldData`, Terrain rules, Placement collision, and Navigation
  without adding `start_map`/`instance_map` Scene roles or another schema.
- [x] Allow a Scene with no Portals to back a Core and emit no Portal state or
  Portal lifecycle events.
- [x] Reject non-finite or non-walkable Character entry footprints at Space
  construction.
- [x] Remove the `WorldData` constructor from the public `SimulationCore` API.
- [x] Move general Character, status, Ability, load, camera, Core, and network
  tests onto a generated role-neutral Scene fixture.
- [x] Remove the pre-Slice-8 fixture and migrate or retire all dependent tests.
- [x] Record Slice 12 as complete deletion of all remaining legacy code and
  data, not as a compatibility-retention slice.
- [x] Run the complete automated and headless runtime verification.

## Slice 12 — Complete legacy deletion

Acceptance outcome: the repository contains no retired World/Macro Map code,
data, compiler, presentation, tests, documentation claims, or compatibility
entry points. Required still-relevant behavior is proven through Scene-backed
fixtures or dedicated current product slices before deletion.

- [x] Replace or retire every regression using the retired fixture.
- [x] Delete the old World input types, tuning/geometry branches,
  and the internal legacy `SimulationCore` constructor.
- [x] Delete the retired fixture data/tests and all references from project files.
- [x] Delete the Macro Map design directory, compiler/tests, and old Godot
  Macro Map preview/streaming probe code.
- [x] Remove obsolete sections/files from active documentation and
  verify repository-wide that no legacy code or data remains.

## Slice 7 — Terrain Fill and spatial surface coverage

Acceptance outcome: Terrain can be filled as ordinary bounded regions, scenes
may remain incomplete while being edited, and no runtime package can contain a
Placement or Transition without Terrain under its complete footprint.

- [x] Add a text-free Terrain Fill tool using four-way connectivity.
- [x] Treat the clicked Terrain identity, including empty, as the source region
  and stop at other Terrain identities and Scene bounds.
- [x] Implement Fill iteratively and prove large regions do not depend on call
  stack depth.
- [x] Let Terrain Eraser create explicit unpainted Cells.
- [x] Compute every WorldGrid Cell intersected by the exact half-open
  conservative Placement or Transition footprint.
- [x] Allow incomplete spatial instances to be authored, saved, and repaired.
- [x] Draw incomplete instances with red dashed outlines in every perspective;
  keep solid red for blocked bounds/overlap candidates.
- [x] Require any Terrain under every intersected footprint Cell regardless of
  its `walkable` value.
- [x] Version the Runtime Scene Package to version 2 and enforce coverage again
  in the producer-neutral simulation loader.
- [x] Aggregate export coverage issues with exact Cell ranges and preserve the
  previous valid output on failure.
- [x] Add focused Fill, coverage, export, and runtime-loader regression tests.

## Later slices

Each item requires its own acceptance outcome and should be promoted into a
detailed slice only when it becomes current.

- [ ] Add further Placement Assets one at a time through the same extent,
  anchor, automated-test, and manual-feedback gate as Tree.
- [ ] Add rectangular orientation and rotated-anchor rules.
- [x] Add Scene Instance/Scene Template kinds, template insertion anchors, and
  grouped Template Anchors without runtime composition.
- [x] Compose a deterministic SceneMaker Preview from a grouped pool without
  destructively changing the base Scene Instance; Template Terrain is the sole
  replacement mask.
- [x] Version the Runtime Scene Package for Template definitions and server
  initialization composition with an independent deterministic bootstrap seed.
- [ ] Add Overworld-aligned scenes with explicit `world_origin_cell`.
- [ ] Visualize actual Chunk boundaries from `WorldGrid` only for those aligned
  scenes; keep the helper unavailable elsewhere.
- [ ] Add deterministic tolerance/jitter only through a separate explicit
  design contract.

## Required verification for every completed slice

- [ ] Run focused SceneMaker tests.
- [ ] Run `dotnet test tests/MMORPG.Simulation.Tests/MMORPG.Simulation.Tests.csproj`.
- [ ] Run `dotnet build MMORPG.csproj`.
- [ ] Run a short headless Godot start when Godot integration changes.
- [ ] Preserve unrelated worktree changes.
- [ ] Update relevant active documentation when a game-owned contract changes.
- [ ] Commit only the completed slice with a focused message.
- [ ] Do not push without an explicit user request.
