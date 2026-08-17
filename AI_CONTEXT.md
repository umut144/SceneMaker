# SceneMaker AI context

## Status

SceneMaker Slice 1 is implemented as a standalone Godot/C# project. Its
engine-neutral foundation creates and loads strict Workspace and fixed-size
Scene documents and presents a navigable semantic Canvas without authoring
content. The owner accepted the visible grid, zoom, and pan behavior during the
Slice 1 feedback pass. The resulting Workspace layout was then fixed to one
named directory containing `workspace.json` and its `scenes/` directory.

Slice 2 Terrain Paint is accepted. It resolves active Grass and Water identity
through the producer-neutral World Asset catalog, keeps only their SceneMaker
display colors in a tool-local allowlist, paints canonical WorldGrid cells
without TileMap persistence, and restores the last active Workspace and Scene
as separate UI session state.

Slice 3 Tree Placement is accepted after manual feedback. Tree owns the
reviewed `3.5 m × 6.5 m` spatial extent and bottom-center anchor, SceneMaker
derives its `112 × 208` authoring-pixel hull and `(56, 0)` anchor, and the
Selector, Eraser, Pencil Draw, and staged Line Draw workflows have passed the
manual Tree-band check.

The Portal Transition authoring slice adds a separate Transitions root. Portal
owns a reviewed `2 m × 3 m` spatial extent and bottom-center `(1 m, 0 m)`
anchor, deriving a `64 × 96` authoring-pixel hull and `(32, 0)` anchor.
Transition and Placement hulls may touch but never overlap. Runtime startup
selection and player distribution remain the next simulation integration
slice.

This document captures the accepted working direction for the practical
slices. Active repository contracts remain the source of truth until a focused
slice explicitly versions them.

The immediate goal is a very small, robust standalone Godot/C# tool under
`tools/SceneMaker`, not a complete general-purpose world editor.

## Purpose

SceneMaker authors semantic 2D scene data. It does not create or duplicate game
presentation. Its root data perspectives are:

```text
Terrain | Placements | Transitions | Scene Templates
```

- Terrain contains local terrain surfaces such as Grass and Water. The term
  Biome is reserved for the existing macro-world vocabulary such as
  Grasslands and Ocean.
- Placements contains stable World Asset instances such as Tree and Chest.
- Transitions contains stable transition instances. Portal is the first
  supported Transition and has no target-scene link.
- Scene Templates are saved reusable scenes. A concrete Scene Instance owns
  Template Anchors that select a Template from the matching group. SceneMaker's
  Preview composer selects Templates transiently; the version-3 Runtime Scene
  Package preserves the same semantic data for deterministic server-start
  composition.

The tabs select independent semantic collections that may be edited in any
order. They do not select mutually exclusive Canvas layers: all authored data
is rendered together. Only the active tab's data is highlighted, placed in the
visual foreground, selectable, erasable, or drawable; inactive data remains
visible in a subdued form. Terrain never participates in Placement or
Transition footprint collision.

## Existing contracts

The accepted World contract is:

```text
World extent                    defined independently by each Scene
World origin                    bottom-left
Axes                            +X right, +Y up
WorldGrid cell                  0.5 m x 0.5 m
Asset/game projection density   128 px/m
Game tile artwork               64 px x 64 px per WorldGrid cell
Chunk                           125 x 125 cells = 62.5 m x 62.5 m
```

`src/MMORPG.Simulation/WorldGrid.cs` owns the pure C# World conversions. The
existing World Asset ledger is `world_assets/world_assets.json`; individual
flat definitions such as `world_assets/tree.json` use `srt.world_asset`.
Permanent IDs must continue to use the repository allocator.

WorldAuthoring and its `srt.authored_room` export are retired. SceneMaker is
the sole active scene producer. Its arbitrary authoring-pixel Placement
positions require a separate, producer-neutral runtime export contract; it
must not revive or emulate the retired TileMap-derived format.

The owner has since selected bottom-left origin, positive X right, and positive
Y up as the target logical convention for maps and simulation. The existing
runtime and World data above have not yet been migrated; that is a separate
project-wide contract slice. SceneMaker version 5 retains the target convention
now and identifies it explicitly, so no implicit axis reinterpretation is
allowed at the later runtime boundary.

## SceneMaker coordinate model

SceneMaker does not refine `WorldGrid`. It overlays a finer discrete coordinate
system only where needed:

| Quantity | Exact value |
| --- | ---: |
| WorldGrid cell | `0.5 m` |
| Logical authoring pixels per cell | `16` |
| Authoring pixels per meter | `32` |
| Meters per authoring pixel | `0.03125 m` |
| Game presentation pixels per meter | `128` |
| Game presentation pixels per authoring pixel | `4` |

Terrain coordinates are integer WorldGrid cells. Placement anchor coordinates
are integer authoring pixels. Simulation consumes meters:

```text
position_meters = position_authoring_px / 32
```

Godot presentation may project the same point as:

```text
position_game_px = position_authoring_px * 4
```

The second equation is a presentation projection, not simulation state.

## Physical extent and conservative footprint

Each SceneMaker-enabled placement Asset eventually owns one reviewed physical
extent in its World Asset contract. The same physical definition is intended
to become the shared basis for game presentation, Debug presentation,
SceneMaker, and relevant PolyDraw authoring requirements.

SceneMaker derives, rather than stores, its conservative rectangular footprint:

```text
footprint_width_px  = ceil(asset_width_m  * 32)
footprint_height_px = ceil(asset_height_m * 32)
```

Example for a hypothetical `1.1 m x 1.4 m` Asset:

```text
physical projection       140.8 x 179.2 game px
SceneMaker footprint      36 x 45 authoring px
quantized footprint       144 x 180 game px
```

The quantized footprint is a conservative placement hull, not a replacement
for the physical metric extent. Its small excess is accepted design tolerance.
Two adjacent 36-pixel footprints retain a deterministic anchor distance of
`1.125 m`; SceneMaker must not collapse that distance to the physical width.

Use decimal-safe or exact rational validation around `ceil` so floating-point
noise cannot accidentally add another authoring pixel.

## Asset anchor

Every supported placement Asset has an individually reviewed anchor independent
from its extent. The stored scene position always denotes this anchor. The
anchor may represent bottom-center for a Tree, another point for a Bridge, or a
different deliberate Asset convention.

Slice 3 finalizes the common producer-neutral representation. A World Asset
definition owns either `spatial: null` while its size decision remains open, or
an object shaped as follows:

```json
"spatial": {
  "coordinate_space": "asset_local_bottom_left_y_up",
  "extent_meters": { "width": 3.5, "height": 6.5 },
  "anchor_meters": { "x": 1.75, "y": 0 }
}
```

The anchor is measured from the lower-left corner of the physical extent.
SceneMaker requires it to map exactly to integer authoring pixels. Regardless
of representation:

- there must be one Asset-owned source of truth;
- the anchor must map losslessly to the integer authoring-pixel grid;
- SceneMaker support must be explicit;
- absent or incomplete values must fail validation;
- values must not be inferred from incidental geometry.

The producer-neutral World Asset catalog never stores SceneMaker support,
colors, icons, grouping, atlas slots, or other tool metadata. SceneMaker may
own a small closed display configuration that references canonical `asset_id`
values, but it must resolve identity, name, type, lifecycle, and definition
only from `world_assets/world_assets.json` plus the referenced flat definition.
Such configuration is an allowlist and UI projection, never a second Asset
registry; it must not duplicate keys, names, types, definition paths, or Asset
semantics.

## Scene model

Every scene has a fixed local coordinate space:

```text
origin = bottom-left (0, 0)
+X = right
+Y = up
```

Scene size is fixed and persistent in WorldGrid cells. A Scene has a separate
placement anchor, expressed on the authoring-pixel grid, without changing its
local origin. When a child scene is later placed at parent anchor position `P`,
a child-local point `L` maps conceptually as:

```text
parent_position = P - child_scene_anchor + L
```

Scene Templates are referenced by stable scene IDs, never copied into the base
Scene Instance. Their terrain-aware composition is an explicitly derived result
and must never destructively rewrite base Scene data. Workspace Scene Instances
are stored directly under `scenes/`; Scene Templates are stored directly under
`templates/`. Runtime export reads both directories.

## Scene Templates and transient Preview

Editable Scene version 5 distinguishes two closed kinds:

```text
Scene Instance  owns zero or more Template Anchors
Scene Template  owns one group number and an insertion anchor
```

An Instance Anchor is a semantic point rendered as a `32 × 32` authoring-pixel
white square with a grey border and its group number. It and the Template
insertion anchor both align to the 16-authoring-pixel WorldGrid. The square is
an authoring affordance, not a terrain footprint, collision shape, or runtime
placement.

For a preview seed, each group uses a deterministic shuffle of its Templates
ordered by stable Scene ID, then pairs them with Anchors ordered by stable
anchor ID. Selection is without replacement; an insufficient group pool blocks
preview generation. The selected Template maps its local point `L` as:

```text
instance_position = anchor_position - template_insertion_anchor + L
```

Only the translated Template Terrain Cells form the replacement mask. The
Composer starts from an untouched base Instance, replaces Terrain at those
Cells, and removes any accumulated Placement or Transition whose full semantic
footprint intersects even one mask Cell. It then adds the translated Template
content. Transparent Template areas never remove or alter base data. A later
Template Anchor in stable `anchor_id` order wins where Templates overlap.

The resulting Preview Scene is never saved. A new preview begins from the
unchanged base Scene; editing any Scene data clears the transient preview. The
version-3 Runtime Scene Package exports Templates and Template Anchors without
pre-baking a result. The server composes its selected startup Scene once from
the bootstrap's independent `template_generation_seed`.

While a Template Preview is visible, every selected Template's effective
Terrain mask has a white dashed Canvas contour. The contour follows only outer
Cell edges of that mask, never the Template's rectangular Scene bounds or its
transparent Cells. If a later Anchor overwrites shared Terrain, the earlier
Template has no contour at those shared Cells.

An actual Chunk Helper applies only to an Overworld-aligned scene with a known
global origin such as `world_origin_cell`. Local Rooms and Templates report the
helper as not applicable. SceneMaker never owns a separate chunk size.

## Initial persistence direction

Use small engine-neutral documents. The foundation slice defines the minimal
versioned editing documents needed for safe save/load; the later export slice
freezes their canonical producer-facing export boundary. The conceptual core
is:

```text
Workspace
  stable workspace_id
  scene files discoverable within the workspace

Scene
  stable scene_id
  fixed size_cells
  terrain cells
  stable placement instances

TerrainCell
  cell coordinate
  asset_id

PlacementInstance
  stable instance_id
  asset_id
  anchor position_authoring_px
```

Names, colors, physical extents, anchors, footprints, and presentation data are
not copied into placement instances. They resolve from validated World Assets.

The current editable contracts are:

```json
{
  "schema": "srt.scene_maker_workspace",
  "version": 1,
  "workspace_id": "my_workspace"
}
```

```json
{
  "schema": "srt.scene_maker_scene",
  "version": 5,
  "scene_id": "scene.my_room",
  "scene_kind": "instance",
  "coordinate_space": "scene_local_bottom_left_y_up",
  "size_cells": {
    "width": 20,
    "height": 12
  },
  "terrain_cells": [],
  "placements": [],
  "transitions": [],
  "template_definition": null,
  "template_anchors": []
}
```

Creating a Workspace beneath a selected parent directory creates exactly:

```text
<selected-parent>/<workspace_id>/
  workspace.json
  scenes/
  templates/
```

Scene Instances live directly under `scenes/`; Scene Templates live directly
under `templates/`. Both use the exact filename `<scene_id>.scene.json`. The
compound suffix keeps the file ordinary JSON while
making its SceneMaker document role unambiguous to people, searches, and file
dialogs. Loads are closed-schema and saves use atomic sibling temporary files.
Only version-5 bottom-left/+Y-up Scenes are accepted. Current Scenes persist
Terrain in canonical Y/X order:

```json
{
  "schema": "srt.scene_maker_scene",
  "version": 4,
  "scene_id": "scene.my_room",
  "coordinate_space": "scene_local_bottom_left_y_up",
  "size_cells": {
    "width": 20,
    "height": 12
  },
  "terrain_cells": [
    { "x": 2, "y": 1, "asset_id": 1003 },
    { "x": 3, "y": 1, "asset_id": 1006 }
  ],
  "placements": [],
  "transitions": []
}
```

The first reviewed Placement Asset is Tree:

```text
physical extent       3.5 m × 6.5 m
authoring footprint   112 × 208 px
anchor                bottom-center
anchor from bottom-left (56, 0) authoring px = (1.75, 0) m
```

The accepted rectangular Debug composition that produced this hull is also
recorded for its later presentation slice: a centered `2.0 m × 2.5 m` trunk
from local Y `0` to `2.5`, and a centered `3.5 m × 5.0 m` crown from local Y
`1.5` to `6.5`. Their vertical overlap is exactly `1.0 m`. Slice 3 displays
only rectangular authoring footprints; it does not yet replace runtime Debug
presentation or make SceneMaker a presentation authority.

Each coordinate denotes exactly one existing `0.5 m` WorldGrid cell. Repainting
a cell replaces its one semantic Asset ID. SceneMaker rejects out-of-bounds,
duplicate, unordered, unknown, retired, wrong-domain, and non-enabled Terrain
references.

SceneMaker keeps recent UI session state separate from Workspace and Scene
semantics. The atomic, closed `user://recent_session.json` stores the absolute
path of the last Workspace manifest and the Workspace-relative path of its
active Scene or Template. At startup SceneMaker validates and restores both; a missing or stale external
path leaves the tool usable with an explicit status message. The Workspace
load dialog begins in the active Workspace directory (or the local
`res://workspaces` root before one is active), while Scene loading begins at
that Workspace's root so both `scenes/` and `templates/` are immediately visible.

The Runtime Scene Package is producer-neutral and validated by pure C#. It
preserves integer authoring coordinates as exact provenance and may carry
redundant meter positions only when the loader recomputes and verifies them.

The accepted runtime target has no mandatory World Macro Map. Every exported
SceneMaker scene is role-neutral: configuration may choose any validated scene
ID as the startup map, and authoritative instanced Spaces may use any validated
scene ID through the same contract. Start/instance role is consumer
configuration, never a duplicated flag baked into the scene export.

The simulation boundary derives scene bounds, Terrain, collision,
static objects, entrances and spawn definitions from the selected scene plus
resolved World Asset contracts. It must not inject a fixed River, Bridge,
Chest, Ring, Potion, Monster, or spawn position. Monster start positions are
future authored scene semantics. The Runtime Scene Package exports no
macro-map fields, global World role, fixed
`5 km` extent, or built-in content.

The former `srt.scene_maker_source` single-Scene export was an intermediate
producer boundary before the Runtime Scene Package existed. It had no active
consumer and duplicated the editable Scene shape, so its menu action, contract,
loader, and tests were removed. Editable `*.scene.json` files remain under
`scenes/`; the only generated Scene export is now the complete Runtime Scene
Package under `exports/`.

Portal is special runtime semantics, not a Scene role flag. In a loaded World
scene set, exactly one scene may contain Portals; their presence derives that
scene as the startup scene. A new non-persistent server will deterministically
permute stable Portal instance IDs and assign first entries round-robin through
that order. The cursor resets with the server and no player persistence is part
of the current target. Portal carries no target Scene. Stairs, Tunnel, and a
future Wormhole will later model bidirectional Scene links separately.

Slice 6A introduced the separate producer-neutral Runtime Scene Package; its
current `srt.runtime_scene_package` version 3 contract retains mandatory
Terrain coverage and adds explicit Instance/Template data. It is validated only
by `MMORPG.Simulation.Scenes` plus the World Asset catalog. The Core derives
exactly one Portal-bearing startup
Scene and offers a deterministic seeded, round-robin first-entry distributor.
Slice 6B can select any validated Scene ID into a concrete `SceneWorldData`
projection. Scene-derived bounds, painted Terrain Surface rules, Collision,
and Navigation work directly from the selected Scene without a global extent.
Unpainted Terrain is explicitly blocked. Resolved
Placements and Transitions remain available but do not become Collision shapes
until their Asset semantics define that behavior. SceneMaker's editable working
format remains independent from the runtime package contract.

Slice 6C adds the parallel authoritative `PortalEntrySimulation`. Every Portal
starts Closed and remains at its authored location. First entry opens the
assigned Portal for an observable completed simulation tick, records the
Character at the exact anchor-derived meter position, and closes the Portal on
the following tick. Same-tick requests are Character-ID ordered before seeded
round-robin assignment. This state is server-local and non-persistent; active
`SimulationCore`, networking, and Godot presentation are still unchanged.

Slice 6D makes SceneMaker the concrete Runtime Scene Package producer.
Settings → Export Runtime Scene Package reads all `*.scene.json` files directly
under the loaded Workspace's `scenes/` and `templates/` directories and atomically deploys
`design/runtime_scenes/<workspace_id>.runtime-scenes.json`. It validates every Scene against
the tool's closed support allowlists and then delegates complete package
validation and canonical serialization to the pure simulation contract. The
result is byte-stable and source Scenes are never rewritten. Workspace identity,
colors, and other tool state do not enter the package.

Scene-backed migration Slice 8 makes that output active. The strict game-owned
`design/world_bootstrap.json` selects `world01.runtime-scenes.json`, startup
Scene `scene02`, World revision `3`, and the deterministic Portal-entry seed.
The active local, server, and network-client paths derive their `50 m × 50 m`
bounds and Terrain from the package and distribute Character creation through
the three authored Portals and inject no fixed objects or Monster.

Slice 9 makes that Scene-backed data visible through a game-owned rectangular
Godot Debug fallback. Terrain and exact Tree/Portal geometry are read from the
validated `SceneWorldData`; no SceneMaker display color or editable file enters
the game. Tree uses the previously accepted rectangular trunk/crown
composition. Portal `Closed`/`Open` state now leaves the Core in snapshots and
crosses `srt.mmorpg` protocol version 20, while static Portal identity and
geometry remain in the locally validated Runtime Scene Package. The projection
verifies Scene ID, instance ID, Asset ID, count, and exact anchor before using
network state. `WorldCanvasCoordinates` is the sole Y reflection between the
bottom-left/+Y-up World and Godot's top-left/+Y-down Canvas; semantic World data
is never rewritten for presentation.

Slice 10 adds the first reviewed Placement simulation collision without
changing Tree's authoring hull. Tree owns a `2.0 m × 2.0 m` rectangle in its
factory data with bottom-center collision anchor `(1.0 m, 0.0 m)`. Matching
that anchor to the Placement anchor makes the collision bottom edge exactly
flush with the visible trunk bottom. The visible trunk remains
`2.0 m × 2.5 m`, so its upper `0.5 m` and the complete crown are intentionally
non-colliding. `SceneWorldData` resolves one obstacle per Tree instance from
the exact package anchor; active movement and Navigation consume those
obstacles. Static collision data remains local validated Scene/Asset input and
is not redundantly replicated or assigned invented Entity IDs.

Slice 11 adds the public `SceneSimulationSpace` Core boundary. It selects any
validated role-neutral Scene ID with an explicit World revision and Character
entry root, including Scenes with no Portals. Each running Space has its own
stable `space_id`, so multiple instances may reuse one Scene ID. It reuses
`SceneWorldData`, `WorldRules`, `WorldCollision`, and Navigation and creates no
Portal runtime
state. General Core and network tests use a generated portal-free Runtime Scene
fixture. SceneMaker accepts only the current version-5 bottom-left/+Y-up Scene
document contract; no compatibility loader rewrites older Scene versions.

The Terrain Fill and surface-coverage slice adds a classic iterative four-way
Fill tool. The clicked Cell's Terrain identity, including empty, defines the
connected source region; other Terrain types and Scene bounds stop the fill.
Terrain Eraser may deliberately create empty Cells.

Editable Scenes may contain Placements and Transitions without complete
Terrain underneath. Every WorldGrid Cell intersected by the complete
conservative footprint must nevertheless contain any Terrain before runtime
export. `walkable` may be true or false and does not change this structural
surface rule. Existing incomplete instances remain authorable and are drawn
with red dashed outlines across all perspectives; solid red still means a
candidate is geometrically blocked. Runtime Scene Package validation enforces
the invariant independently. SceneMaker's exporter collects all affected
spatial instances, reports their missing Cell ranges, and never overwrites an
earlier valid package when validation fails.

## UI direction

The target layout has one dynamic top Navigation Bar, a left Tool Bar extending
to the bottom, a large Canvas, and a separate rectangular Settings button at
the upper right of the Navigation overview. The overview shows all root tabs in
one horizontal row. Selecting a root tab replaces that row's contents in place
with a left Return button and the selected tab's former contextual controls;
Return restores the root-tab overview. There is no stacked second navigation
row.

`Map` is a Scene-level context, not another semantic data tab. Its first
reviewed operations extend the rectangular Scene bounds north or east by an
explicit WorldGrid-Cell count. They preserve every existing Terrain Cell,
Placement/Transition anchor, and Template Anchor coordinate exactly. The
effective natural map shape remains the painted Terrain inside those rectangular
bounds; unpainted Cells have no surface. South/west growth, which would require
a controlled translation of all local coordinates, is deliberately deferred.
There is no Brush Settings button and no user-configurable brush size.

The Canvas should be one viewport-drawn control with bounded visible-data
iteration, not one Node per pixel or cell. Q/E zoom and WASD pan affect only
the view transform. WASD uses a constant speed per axis while held and stops
abruptly after release; simultaneous directions retain their full components,
so diagonal panning is faster. Perspective-specific behavior should use a few explicit
controllers or interfaces, without a generic plugin framework.

The top Data Bar has no Height placeholder. Terrain, Placements, Transitions,
and Scene Templates share one composite Canvas. Changing tabs changes
visual emphasis and tool scope, never data visibility.

The left Canvas toolbar is deliberately narrow and text-free. It exposes five
large square icon buttons: Selector, Eraser, Pencil Draw, Line Draw, and
Terrain Fill. Fill is enabled only for the Terrain perspective. The
former explanatory perspective text and Canvas-navigation legend are removed;
tool names remain available as hover tooltips. Pencil Draw shows a yellow
footprint preview under the pointer; a conflicting or out-of-bounds preview is
red and clicking it is silently blocked with a footer notice rather than a
modal dialog.

Placement Line Draw has explicit transient states. Before its first click it
previews one possible start footprint. The first click fixes the start; pointer
movement then previews the complete footprint-spaced line. The second click
fixes the end while keeping every candidate as preview-only. `Enter` persists
the line only when every candidate is valid. `Escape` first releases a fixed
end and a second press releases the start. Every invalid candidate is red and
blocks confirmation; valid candidates remain yellow. The repeated anchors use
the selected Asset's derived footprint, so axis-aligned Tree bands advance by
exactly `112` authoring pixels horizontally or `208` vertically. Selector
highlights a Placement without changing it, and Eraser removes the Placement
whose half-open footprint contains the pointer. Tool actions and blocked
operations are reported in the non-modal footer status line.

## Deliberately deferred

- terrain algorithms beyond accepted Paint and four-way Fill behavior
- orientation and rotated footprints
- automatic or random jitter
- real Chunk Helper for scenes without a global origin
- deletion workflows unless needed by an accepted slice
- Runtime export and the first producer-neutral SceneMaker integration
- wholesale completion of every World Asset contract

The retired WorldAuthoring pipeline, its TileSets, atlases, palette metadata,
and `srt.authored_room` documents are not SceneMaker inputs or compatibility
targets. The current Suitcase room is a Core-owned fixture until a later slice
rebuilds and verifies it through SceneMaker and a new runtime contract.

Assets are reviewed and enabled one at a time. When a slice reaches a missing
physical extent or anchor, the user chooses it through a manual feedback loop;
then the contract and consumers are advanced deliberately.
