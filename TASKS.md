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

## 2. Building the asset bars selects an asset as a side effect

`src/SceneMaker.App/SceneMakerMain.cs`, `BuildTerrainAssetBar` and
`BuildPropAssetBar`

Both loops call `SelectTerrainAsset` / `SelectPropAsset` for the first asset
they add, which writes to the canvas and sets the status line. Layout
construction therefore also decides editor state, and the status message it
writes is immediately overwritten by whatever called it.

The effect is wanted — after loading a Workspace an asset must be selected —
but it belongs to `AdoptSession`, not to the code that adds buttons. Building
the bars should be pure; `AdoptSession` should choose the default afterwards
and set the status once.

Watch the ordering when moving it: `_canvas.SelectedTerrainAssetKey is null` is
what currently decides whether to select, so the choice has to happen after the
bars exist but before the caller writes its own status.

This one is App code, which has no tests, and it changes when an asset gets
selected. It is the entry here most likely to be noticed only while using the
editor, so it wants a manual pass rather than a quick fix.

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

## 7. A Prop over a cut is still exported as supported

`src/SceneMaker.Core/SceneExport.cs`, the Prop coverage check

The export guarantees that a Prop's footprint is covered by Terrain cells. Since
a water body can cut a range out of those cells, that guarantee no longer says
what it used to: a tree standing in the middle of a tunnel's cross-section
passes it and floats in the game.

Tightening it to "stands on solid ground at its own height" was considered and
deliberately not done. A bridge is a Prop whose whole purpose is to span a place
where the ground has been taken away, and a rule written now would have to be
unwritten for it. What the support requirement is belongs to the Asset - a tree
needs ground under it, a bridge needs ground at its two ends - so it waits for
the Asset to be able to say so.

Until then the gap is real and undetected. An author who plants something inside
a tunnel gets no warning.

## 8. Height analysis and a first-person Layered-3D preview

The height view is currently a continuous colour ramp stretched automatically
between the lowest and highest Terrain or Prop elevation in the Scene. It is a
useful overview, but it cannot yet inspect authored height bands or answer
whether an Actor can move through the resulting space. Water now participates
through a Surface/Bed/Cut-top selector, on the same scale as Terrain and Props.

The intended next form of the height view has three independent capabilities:

- an optional author-selected low/high range, so one outlying mountain does not
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

The Core persists a level-topped mountain as a closed Bezier contour and folds
nested bodies into Terrain by their absolute top elevation. The runtime sees
only the resulting Terrain cells. `Draw Mountain` now authors, previews, closes,
cancels and erases those bodies through the existing `ToolInteraction` path.
Manual UX acceptance of this contour slice is required before route or helix
work starts.

Do not call adjacent flat cell tops a slope without fixing the mesh rule.
Different cell elevations form terraces and vertical steps. A visually smooth
ramp requires an explicit derived meshing rule or a separately authored ramp;
the `0.125 m` quantum alone does not create inclined geometry.

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

## Done

- One factory per Scene kind, so `CreateInstance` no longer accepts Template
  parameters it would silently drop (`9c017be`).
- The settings menu is a closed enum instead of seven hand-picked integers
  (`a2f5e52`).
