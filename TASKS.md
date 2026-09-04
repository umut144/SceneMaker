# Open work

Rough edges and deliberately deferred features. Nothing here is a bug that
loses data or blocks authoring; each entry says what it costs to leave alone,
so a later reader can decide rather than rediscover.

Read `AGENTS.md` first — the rules there are what the fixes below have to stay
inside.

## 1. Instance IDs stop reading in numeric order past 9 999

`src/SceneMaker.Core/PropEditing.cs`, `NextInstanceId`

IDs are `{assetKey}_{index:0000}`, and Props are held in canonical order sorted
ordinally by `InstanceId`. The format pads to four digits, so the ten
thousandth Prop of one asset becomes `grass_10000` and sorts between
`grass_0999` and `grass_2000`.

Nothing breaks: ordinal order is still total and deterministic, the canonical
ordering invariant still holds, and documents stay valid. What breaks is the
reader's expectation that the file reads in the order Props were placed.

Widening the pad changes every existing ID and is therefore a document
migration — see the schema-version section in `AGENTS.md` for why that is not
free. Worth doing only if a Scene ever gets near that many Props of one asset;
`world01` is nowhere close.

The allocation problem that used to sit alongside this one is fixed: naming a
Prop no longer formats a candidate string per attempt.

## 2. Building the Prop asset bar selects an asset as a side effect

`src/SceneMaker.App/SceneMakerMain.cs`, `BuildPropAssetBar`

The loop calls `SelectPropAsset` for the first asset it adds, which writes to
the canvas and sets the status line. Layout construction therefore also decides
editor state, and the status message it writes is immediately overwritten by
whatever called it.

The effect is wanted — after loading a Workspace an asset must be selected —
but it belongs with adopting the session, not with the code that adds buttons.

`BuildTerrainAssetBar` had the same shape and no longer does: entering an area
has to choose an Asset anyway, because River cannot keep the one Terrain was
holding, so the choice moved to `ShowTerrainAssetsForArea` and the bar became
pure. The Prop bar has no areas to enter and therefore no such moment to move
it to, which is why it is still here.

This one is App code, which has no tests, and it changes when an asset gets
selected. It wants a manual pass rather than a quick fix.

## 3. Height is authored only by painting over a cell

`src/SceneMaker.App/SceneMakerMain.cs`, the `Height` control

Setting a cell's height means repainting it with the context bar at the new
value. There is no way to raise a region without also rewriting its Asset, and
no way to read a single cell's height other than by the colour it takes in the
height view.

That is enough for laying out terraces and ramps, which is what the feature was
built for. It gets thin the moment someone wants to lift an existing hillside by
0.2 m without touching what it is made of — a height brush that leaves the Asset
alone, or a numeric readout on the hovered cell.

## 4. A Scene's ground height cannot be changed after it is created

`default_elevation_meters` is set in the create dialog and never again. It is
the height a newly authored cell takes, so changing it later would not touch
anything already authored — it is a preference for future strokes, and one that
survives reopening the Scene.

Leaving it out was scope, not judgement: nobody has needed it yet. If it is
added it belongs in the context bar next to `Height`, and it is a document edit,
so it goes through `EditorController.Apply` and lands in the undo history like
any other.

## 5. Bridges: the second surface at one place

Deferred by agreement with the world01 runtime, with the shape already settled,
so it is written down rather than rediscovered.

A bridge over a river means one place carries two surfaces: `(water, 0.0)` for
what swims and `(land, 1.1)` for what walks across. Nothing in the export
resolves that and nothing should — the Actor's domain picks the surface and the
height difference decides the step, both in the simulation.

What SceneMaker would add when it comes is two additive fields, no schema break
beyond a version bump:

- `asset_profiles[].traversable_surface` — the surface a Prop Asset offers to
  something walking on it. Which Props are crossable is then Asset data, not a
  rule the exporter has to know.
- Prop heights already exist (`props[].elevation_meters`), so the deck height
  needs nothing new.

Do not resolve this by baking a Prop's height into the Terrain cell under it.
That was considered and rejected: it destroys the fact that there is water below,
and a boat has to be able to pass under the bridge.

## 6. A Scene Template cannot carry water

`src/SceneMaker.Core/DocumentValidation.cs`, the Scene Template branch

`TemplateComposition.Compose` moves Terrain cells and Props. A Template holding
a water body would therefore lose it at every Anchor it is placed at, silently,
so authoring one is refused rather than dropped.

That is the right refusal for now and the wrong feature forever: a Template with
a pond or a stream through it is an obvious thing to want. Adding it is small in
Core - a water body's points move by `translation_px` exactly like a Prop - and
not small in the export contract, which would have to say what happens when a
Template's water lands on an Instance's. Leave it until a Template needs water.

## 7. Height analysis and a first-person Layered-3D preview

The height view is currently a continuous colour ramp stretched automatically
between the lowest and highest Terrain or Prop elevation in the Scene. It is a
useful overview, but it cannot yet inspect authored height bands or answer
whether an Actor can move through the resulting space. Water now participates
through a Surface/Bed/Cut-top selector, on the same scale as Terrain and Props.

The intended next form of the height view has three independent capabilities:

- an optional author-selected low/high range, so one outlying hill does not
  compress every useful height into one colour;
- discrete elevation bands and an arbitrary multi-selection of elevations, so
  chosen floors can be highlighted while the rest of the Scene is muted;
- a traversal overlay that compares neighbouring floor spans, available
  headroom and surface compatibility for a selected Actor profile.

Authored absolute elevations now snap to the Workspace-owned
`elevation_quantum_meters`; `world01` sets it to `0.125 m`. That is half of its
ordinary `0.25 m` step capability, so two elevation increments make the largest
ordinary step. The quantum is a Workspace metric, not a SceneMaker constant.
Likewise, `0.25 m` is an Actor or simulation capability rather than a property
of the geometry: another Actor may accept a different step. A separate grade
tool may later distribute a start and end elevation across cells in
quantum-sized increments; the height view itself should remain inspection
rather than silently editing the Scene.

The Core persists a level-topped hill as a closed Bezier contour and an
absolute top elevation, and folds nested bodies into Terrain by that top. A body
carries no material: painted Terrain decides whether a cell exists and what it
is made of, the contour decides only how high it reaches. The runtime sees only
the resulting Terrain cells. `Draw Hill` now authors, previews, closes,
cancels and erases those bodies through the existing `ToolInteraction` path.
Manual UX acceptance of this contour slice was completed before route work
began.

Scene schema 14 names that neutral source `elevation_regions` and the Core type
`ElevationRegionDocument`; the author-facing area and tools say `Hill`. Existing
stable IDs deliberately retain their `mountain_` prefix: identity survives a
terminology correction, and no behavior may infer meaning from an ID prefix.

Do not call adjacent flat cell tops a slope without fixing the mesh rule.
Different cell elevations form terraces and vertical steps. A visually smooth
ramp requires an explicit derived meshing rule or a separately authored ramp;
the `0.125 m` quantum alone does not create inclined geometry.

The engine-neutral foundation for that separately authored ramp now exists as
`RouteSurfaceGeometry`: an open Bezier centerline with width and absolute
support height at every point. Width and height interpolate over arc length;
the intermediate height stays continuous rather than being snapped into cell
steps. Its grade report is descriptive geometry, not an Actor capability.
Authoring schema 13 introduced routes with a Terrain-role material; schema 14
keeps them unchanged. The author-facing `Draw Path` tool now authors a free
open Bezier curve with height and width per point, previews the continuous band
in the Canvas and erases the whole route. The Landscape navigation and context
controls are present, and the height view colours its interpolated surface.
Export schema 9 still warns and omits routes. Traversal profiles still belong
in Workspace configuration before a tool or generator can judge whether an
Actor can use a route. Export support, selection/point reshaping and the
overlapping-station semantics needed by helixes remain separate later slices.

The same analysis should lead to a generated first-person LookDev mode. It is
DOOM-like in use - enter the authored map, walk it and inspect stairs, tunnels,
rivers and bridges - but it must not adopt classic DOOM's single-floor/single-
ceiling limitation. SceneMaker's source remains Layered 3D, never a dense 3D
voxel grid: remaining solid-span tops make floors, the undersides of higher
spans make ceilings, span boundaries make walls, water spans make water, and
cuts make open channels or tunnels. Multiple floors may exist at the same X/Y.

The preview is derived and engine-neutral authoring data stays unchanged. Its
first useful version can be untextured Godot debug geometry with collision and
an Actor profile providing step height, body height and compatible surfaces.
It should expose mistakes the top-down view cannot: insufficient headroom,
unsupported or unreachable surfaces, an unintended tunnel roof, and whether a
boat or walker can actually pass beneath a bridge.

The editor now says **Placements**, while internal `Prop*` types and the
persisted `props` array deliberately remain unchanged. Renaming those would be
a Scene/export migration with no present runtime benefit. Likewise, do not add
Transitions yet: they need their own simulation-owned target/region contract,
not a second copy of `PropDocument`, and should arrive only when that contract
has a consumer and tests.

## Done

- Adding Assets through the application is deliberately not planned. For now,
  an agent edits and validates the Workspace configuration when a new Asset is
  needed; a later batch operation may take an explicit list. Placement geometry
  is synchronized separately through the existing narrow PolyTools boundary.

- SceneMaker owns authoring identity. Workspace config schema 8 added each
  Asset's `display_name` and SceneMaker `role`; both display catalogs derive
  their vocabulary and classification from it. Terrain can exist without a
  PolyTools package, while a Placement uses matching PolyTools data only for
  footprint and pivot/anchor. The sync no longer rewrites `config.json` from
  PolyTools, so `Water`, `Lava` or another curve surface remains SceneMaker's
  choice rather than an imported package name.
- PolyTools is a Placement-geometry boundary rather than a second authoring
  catalog. Workspace loading requests only the configured Placement roots and
  their transitive Asset References; unrelated Manifests are not opened, and a
  Workspace without Placements opens without an import. Synchronization copies
  the same closure and writes a filtered catalog, leaving `config.json`
  untouched. The geometry result exposes only stable key and bounds — imported
  display names and `asset_type`s no longer cross into SceneMaker's model.

- A hill carries no material. `ElevationRegionDocument` lost its `asset_key`
  and Scene schema moved 11 to 12; the four authored documents under
  `workspaces/world01` were rewritten by hand, as the no-migration rule
  requires. The fold now raises the top of a painted cell and leaves its Asset
  alone, so a contour over sand and grass lifts that pattern unchanged and a
  contour over unpainted ground produces no cell at all — valid, saved, and
  without effect until Terrain is painted under it. The overlap rule that
  refused two bodies tying at one elevation under different Assets went with
  it: with no material on the body there is nothing left to disagree about. The
  export did not move — the JSON and every promise it makes are unchanged, only
  where a cell's `asset_key` comes from, which was never something the export
  said.
- A Prop needs no Terrain under it. This used to be entry 7 here — the export
  guaranteed footprint coverage, a cut could take that ground away, and the
  guarantee quietly stopped meaning what it said. It is settled now by product
  decision rather than by tightening the rule: a Prop holds an absolute height
  and is allowed to stand free, neither the simulation nor the game wants a
  general support rule, and SceneMaker treats a free-standing Prop as neither an
  error nor a warning. Placement, preview, Template composition and the export
  all stopped asking. No `requires_support` flag was added: nothing needs the
  distinction today, and entry 5 still describes what a bridge would want if
  something ever does. Export schema 8 promised the coverage, so withdrawing the
  promise moved it to 9 even though the JSON did not change.
- One factory per Scene kind, so `CreateInstance` no longer accepts Template
  parameters it would silently drop (`9c017be`).
- The settings menu is a closed enum instead of seven hand-picked integers
  (`a2f5e52`).
