# SceneMaker agent guide

## Authority and required reading

This file is local working guidance for `tools/SceneMaker`. The repository-root
`AGENTS.md` and the active game documentation remain authoritative. Before a
SceneMaker product, contract, or architecture change, read:

1. `AGENTS.md`
2. `docs/GAME_DESIGN.md`
3. `docs/ARCHITECTURE.md`
4. `docs/GAMEPLAY_VERTICAL_SLICES.md`
5. `docs/decisions/0001-godot-first-authoring.md`
6. `docs/WORLD_ASSETS.md`

Read `docs/FLECS_FUTURE.md` and
`docs/CHARACTER_GAMEPLAY_CONTRACT.md` when the root guide requires them for the
specific change. Read `AI_CONTEXT.md` and `MVP_CHECKLIST.md` in this directory
before working on SceneMaker.

`AI_CONTEXT.md` records the accepted SceneMaker direction. It does not silently
supersede an active game contract. Where integration requires a contract
change, implement that change as an explicit, versioned slice.

## Product boundary

SceneMaker is a small standalone Godot/C# semantic 2D authoring tool. It may
store terrain identity, stable placement references and placement anchors, and
later references to other authored scenes. It must not own sprites, meshes,
materials, animation, presentation regions, collision regions, target regions,
or other gameplay and presentation definitions.

Godot is the frontend. Persisted SceneMaker documents and their validation
must use small engine-neutral C# models. Do not represent persistent pixels,
terrain cells, or placements as Godot Nodes.

Terrain, Placements, Transitions, and Scene Templates are independent semantic
collections. Do not impose a pipeline order between them. The Canvas renders
all available collections together; the active data tab alone is highlighted
and editable. Scene Templates own reusable authored content; Scene Instances
remain concrete role-neutral maps and may own Template Anchors. SceneMaker may
compose an ephemeral Preview from them; that Preview never mutates persisted
base Scene or Template documents. The version-3 Runtime Scene Package carries
these data to the producer-neutral server composer; Preview remains an
independent transient authoring aid.

Editable scenes may temporarily contain Placements or Transitions whose
footprints are not fully covered by Terrain. Render that incomplete state as a
red dashed warning and allow save/edit operations. Runtime export is the hard
gate: every intersected footprint Cell must contain any Terrain Asset,
regardless of its `walkable` value. Solid red remains reserved for authoring
operations that are blocked by bounds or spatial overlap.

## Coordinate rules

Do not change `WorldGrid` for SceneMaker:

```text
1 WorldGrid cell = 0.5 m
1 WorldGrid cell = 16 logical authoring pixels
1 authoring pixel = 0.03125 m
1 m = 32 authoring pixels
1 m = 128 game presentation pixels
1 authoring pixel = 4 game presentation pixels
```

Terrain remains cell-addressed. Placements are addressed by integer authoring
pixels. Simulation positions remain meters; game pixels are presentation-only.
Zoom and pan are view transforms and must never modify, round, or requantize
semantic coordinates.

Every current SceneMaker scene uses a fixed bottom-left local origin, positive
X right, and positive Y up. Godot's Y-down Canvas is only a presentation
projection and must not leak into persisted semantic coordinates. A scene
placement anchor is independent of that fixed origin.

## World Asset decision gates

A SceneMaker-enabled placement Asset needs a reviewed physical extent and a
reviewed Asset anchor owned by its World Asset contract. SceneMaker derives its
conservative authoring footprint from the physical extent:

```text
width_authoring_px  = ceil(width_meters * 32)
height_authoring_px = ceil(height_meters * 32)
```

Do not add or maintain a second manual `placement_size_px`. Do not infer a
missing physical extent or anchor from collision geometry, presentation
bounds, sprites, Debug drawing, PolyDraw output, or another incidental source.

When the current slice first needs an Asset whose physical extent or anchor is
missing or unresolved:

1. stop that Asset's integration;
2. show the user the relevant existing evidence and concrete choices;
3. obtain and record the user's decision;
4. implement and validate that one Asset contract;
5. support it in SceneMaker only after the decision is accepted.

Other incomplete Assets remain explicitly unavailable to SceneMaker. Missing
or malformed metadata is a validation error. Never provide a hidden default or
magic fallback.

The conservative quantization margin is intentional. It may later be useful as
design tolerance, but the MVP must not invent automatic or random jitter.

## Placement invariant

A placement document stores the integer authoring-pixel position of the
Asset's anchor. Consumers derive:

```text
position_meters = position_authoring_px / 32
```

The relative geometry of authored anchors must survive save/load, export,
simulation loading, and presentation projection exactly. No consumer may
recompute placement positions from visual or collision bounds.

Transitions are a separate root collection, not Placements. Their reviewed
physical extent and anchor use the same producer-neutral World Asset spatial
contract and authoring-pixel derivation. Placement and Transition footprints
may touch but never overlap. Portal is the first supported Transition: it owns
a `2 m × 3 m` extent and bottom-center `(1 m, 0 m)` anchor.

## Scope control

- Prefer explicit models, services, and perspective controllers over a plugin
  framework, reflection registry, generic editor framework, or ECS facade.
- Do not add Template orientation, jitter, or a local chunk size merely to
  anticipate later slices. Runtime Template composition is already a reviewed
  version-3 server concern; do not duplicate it in the tool beyond Preview.
- The real Chunk Helper is unavailable for local Rooms and Templates. It may
  be added only for a scene with an explicit global World origin and must use
  the existing `WorldGrid` contract.
- The retired `srt.authored_room` format must not be revived. Do not round
  SceneMaker data to 0.5 m or synthesize fake source cells. Runtime integration
  uses the reviewed, producer-neutral Runtime Scene Package.
- The reviewed runtime boundary now begins with
  `contracts/scenes/runtime_scene_package.schema.json` and
  `docs/RUNTIME_SCENES.md`. Editable SceneMaker documents remain tool-owned;
  do not make the simulation depend on them or on tool-local display catalogs.
- Keep exported Scene Instances role-neutral. Startup-map and authoritative instance-map
  selection belongs to consumer configuration; both use the same stable Scene
  IDs and schema. Do not add Macro Map fields, a universal World extent, fixed
  map objects, or built-in Character/Monster spawn positions to SceneMaker.
- The active runtime resolves static map content only from Runtime Scene
  Packages and the World Asset catalog. Do not add another map input.
- Slice 11 exposes `SceneSimulationSpace` for selecting any role-neutral Scene
  ID as an authoritative portal-free Space. Do not add a second instance-map
  schema or permanent role to editable/exported Scenes.
- SceneMaker accepts only the current version-5 bottom-left/+Y-up Scene
  document. Do not add compatibility loaders for retired document versions.
- Preserve unrelated worktree changes. Commit each completed SceneMaker slice
  separately and do not push without an explicit request.

## Verification

Test the smallest changed boundary first, then run the repository-required
checks:

```sh
dotnet test tests/MMORPG.Simulation.Tests/MMORPG.Simulation.Tests.csproj
dotnet build MMORPG.csproj
```

Build and test the standalone SceneMaker project once it exists. Use a short
headless SceneMaker start for changes to its Godot integration. Manual
acceptance complements automated coordinate, persistence, and invariant tests;
it does not replace them.
