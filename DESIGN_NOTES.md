# Design notes and deferred decisions

Why the open entries in `TASKS.md` are still open: what each rough edge costs
to leave alone, what shape a fix would have to take, and which alternatives
were considered and rejected. `TASKS.md` names the outcome and the status; this
file holds the reasoning behind it.

Read `AGENTS.md` first — the rules there are what any fix below has to stay
inside. Nothing described here loses data or blocks authoring.

## Instance IDs and document ordering

`src/SceneMaker.Core/PropEditing.cs`, `NextInstanceId` — `ID-01`

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

## The Prop asset bar selects an asset as a side effect

`src/SceneMaker.App/SceneMakerMain.cs`, `BuildPropAssetBar` — `APP-01`

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

## Authoring height

`src/SceneMaker.App/SceneMakerMain.cs`, the `Height` control — `HEIGHT-01`,
`HEIGHT-02`, `GRADE-01`

Setting a cell's height means repainting it with the context bar at the new
value. There is no way to raise a region without also rewriting its Asset, and
no way to read a single cell's height other than by the colour it takes in the
height view.

That is enough for laying out terraces and ramps, which is what the feature was
built for. It gets thin the moment someone wants to lift an existing hillside by
0.2 m without touching what it is made of — a height brush that leaves the Asset
alone, or a numeric readout on the hovered cell.

`default_elevation_meters` is set in the create dialog and never again. It is
the height a newly authored cell takes, so changing it later would not touch
anything already authored — it is a preference for future strokes, and one that
survives reopening the Scene. Leaving it out was scope, not judgement: nobody
has needed it yet. If it is added it belongs in the context bar next to
`Height`, and it is a document edit, so it goes through `EditorController.Apply`
and lands in the undo history like any other.

A separate grade tool may later distribute a start and end elevation across
cells in quantum-sized increments. The height view itself should remain
inspection rather than silently editing the Scene.

## Bridges: a straight span that sets its own posts

`BRIDGE-02`, `BRIDGE-03`

A bridge over a river means one place carries two surfaces: `(water, 0.0)` for
what swims and `(land, 1.125)` for what walks across. Nothing in the export
resolves that and nothing should — the Actor's domain picks the surface and the
height difference decides the step, both in the simulation.

Do not resolve it by baking a deck height into the Terrain cell under it. That
was considered and rejected: it destroys the fact that there is water below,
and a boat has to be able to pass under the bridge.

### The deck is not a new kind of surface

This entry used to answer the question with `asset_profiles[].traversable_surface`
on a Prop Asset. That answer is superseded, and by nothing that was built for
bridges: a Path is already an independently materialized surface with width and
absolute height that a cut never removes, which is exactly what a deck is. A
straight bridge is geometrically a Path with two linear points at one height.

So the deck reuses `RouteSurfaceGeometry` and needs no new geometry at all.

### But it is its own authored record

A bridge is nonetheless not stored as a `route_surfaces` entry. A Path carries
per-segment grade and an additive/subtractive operation; a bridge carries an
anchor Asset and must have exactly two linear points. Sharing one record would
force each kind to hold the other's fields and give the two mutually exclusive
validation rules — the shape this project already rejected once, for water and
routes, and for the same reason.

The authored record is small, because everything derivable is derived:

```jsonc
{
  "bridge_id": "bridge_0001",
  "asset_key": "planks",             // Terrain role, the deck material
  "anchor_asset_key": "bridge",      // Placement role, whose anchor_component is the post
  "start_authoring_px": { "x": 1024, "y": 320 },
  "end_authoring_px":   { "x": 1536, "y": 320 },
  "width_meters": 4.0,
  "elevation_meters": 1.125
}
```

Length is not stored: two ends already fix it, and a second source of the same
truth is one too many. It is a readout in the context bar, like a Path's grade
report. The deck is horizontal — one height for the whole span — until a case
needs otherwise.

### The posts are derived, and that is what makes them owned

The four corner posts are not documents. They are computed from the bridge, the
way water cells are computed from a curve and folded hills from a contour.
Everything the author expects of them then holds by construction rather than by
bookkeeping: changing the width moves them, deleting the bridge deletes them,
moving the whole bridge takes them along. There is no second thing to keep in
step, no back-reference to validate, and no orphan a delete can leave behind.

Storing them as ordinary Props was considered and rejected. It reads simpler
until the first edit: a post the author can drag away from its own bridge is a
bridge with three posts and nothing to notice it. Owning them through a
`props[].owned_by_bridge_id` back-reference was the alternative, and it buys the
same behaviour for the price of a cross-reference the document model has
nowhere else.

What it gives up is deliberate: a single post cannot be nudged. Nobody asked to.

### Where a post comes from

PolyTools exports the post as a Component of the `bridge` Asset, not as an
Asset of its own. A Prop must never name a Component — `asset_key` is the whole
of its identity, and a composition rename in PolyTools would then break authored
maps. The Workspace names it instead, once, on the Asset: config 9's
`anchor_component`, described above under authoring ownership. The bridge points
at that Placement, and the part follows from the Asset rather than from the
document.

For the same reason the bridge takes its editor colour from the `bridge` Asset:
one Asset, one identity, one colour.

### No snapping, and no angle constraint either

Path points are free in plan and hill anchors snap, and the difference is not
taste: a contour is read through a raster that asks about cell centres, while a
route is read as continuous geometry. A deck is the second kind, so it does not
snap — and grid-aligned ends would buy the simulation nothing, because the deck
travels as metre-space triangles either way.

Angle snapping was offered and declined. Arbitrarily angled bridges are a
deliberate property of the authored world, not an accident to be corrected.

Note that `Snap` already means taking a height from the Terrain in the River
context. A second `Snap` meaning a position would be a trap.

### Occupied means the same thing whoever asks

Derived posts take real space, so the Prop overlap rule reads the resolved
Scene rather than the authored list: a post blocks a Placement exactly as a
Placement blocks a post. The draft is `Ready` only when the deck and all four
posts can be placed, checked by the same call the commit makes — the promise
the hill draft already makes.

That rule reads the collision regions rather than the visible footprints, which
was a change to every Placement and landed as its own slice. What a Placement
is drawn as and what it occupies are now two boxes: the first has to lie inside
the Scene, the second has to be free. Two trees may therefore overlap with
their crowns and not with their trunks, which the world is full of and the old
rule refused.

A Placement whose model authored no collision Region at all is refused on load
rather than falling back to its footprint. The fallback was considered: it is
the current behaviour and breaks nothing, but it is exactly the hidden default
this Workspace forbids, and the wrong one - a model whose collision nobody has
drawn yet would quietly claim every pixel it is drawn with.

### The area is Structures

`Landscape` authors terrain. A bridge is built, and it is the first of a family
— fences, walls, stairs, ladders — that all describe a span with attachment
points. It gets its own overview area rather than a fourth seat beside River,
Path and Hill, because the navigation is deliberately stable and renaming it
later costs more than naming it now.

`Draw Bridge` offers `Surface`, `Anchor`, `Width`, `Height` and a `Length`
readout. It deliberately does not offer grade, operation or clearance: a tool
that greys out half its context bar is a second tool.

### What is built

Scene schema 17 persists the record above. `BridgeGeometry` derives the four
corners and hands the deck to `RouteSurfaceGeometry` as the two-point chain it
is, so a bridge has no idea of a band of its own. `BridgeEditing` places,
removes and finds one, and answers the draft and the commit with the same call:
representable numbers, a height on the quantum, two different ends, every
corner inside the Scene, and all four posts free - of Placements and of the
posts of every other bridge. A bridge is authored whole, so it is refused
whole rather than placed with three posts.

The occupancy rule is symmetric in both directions now: `PropEditing` reads
bridge posts too, so a Placement cannot be set into a post that a bridge would
have refused to set into the Placement.

Export 11 refuses a Scene holding a bridge rather than dropping it, the same
way export 10 refused an excavating Path. It costs nothing while no tool can
author one, and it is the one thing that cannot lose a bridge silently.

### Not in the first slice

A Scene Template cannot carry a bridge yet, for the reason it cannot carry
water: composition moves Terrain cells and Props and nothing else, so it would
be lost silently. Refuse it explicitly instead. Selecting and reshaping an
existing bridge is editing detail and waits, like `PATH-03` does for Paths.

## A Scene Template cannot carry water

`src/SceneMaker.Core/DocumentValidation.cs`, the Scene Template branch —
`TPL-01`

`TemplateComposition.Compose` moves Terrain cells and Props. A Template holding
a water body would therefore lose it at every Anchor it is placed at, silently,
so authoring one is refused rather than dropped.

That is the right refusal for now and the wrong feature forever: a Template with
a pond or a stream through it is an obvious thing to want. Adding it is small in
Core - a water body's points move by `translation_px` exactly like a Prop - and
not small in the export contract, which would have to say what happens when a
Template's water lands on an Instance's. Leave it until a Template needs water.

## Height analysis and a first-person Layered-3D preview

`HEIGHT-03`, `HEIGHT-04`, `HEIGHT-05`, `LOOK-01`

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
of the geometry: another Actor may accept a different step.

The Core persists a level-topped hill as a closed Bezier contour and an
absolute top elevation, and folds nested bodies into Terrain by that top. A body
carries no material: painted Terrain decides whether a cell exists and what it
is made of, the contour decides only how high it reaches. The runtime sees only
the resulting Terrain cells. `Draw Hill` now authors, previews, closes,
cancels and erases those bodies through the existing `ToolInteraction` path.
Manual UX acceptance of this contour slice was completed before route work
began.

Scene schema 15 names that neutral source `elevation_regions` and the Core type
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
steps. `RouteSurfaceEditing` can derive those stored absolute anchors from a
quantized starting height and the five fixed per-segment grade presets. It uses
horizontal Bezier arc length and accumulates before rounding each stored anchor
to six decimals. Its grade report is descriptive geometry, not an Actor
capability.

Authoring schema 13 introduced routes with a Terrain-role material; schema 15
adds a stable segment ID and exact integer grade to every interval. The
author-facing `Draw Path` tool now authors a free open Bezier curve with an
automatic or manual starting height, per-segment grade and width per point,
previews the continuous band in the Canvas and erases the whole route. The
Landscape navigation and context controls are present, and the height view
colours its interpolated surface. Export schema 11 carries the authored Path,
the runtime bake used by the Canvas, and the cells a subtractive interval
removes. Traversal profiles still belong
to the consuming simulation before a tool or generator can judge whether an
Actor can use a route. Selection/point reshaping and the overlapping-station
semantics needed by helixes remain separate later slices — see the section
below.

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

## Horizontal sections and Paths that excavate Terrain

`PATH-01`, `PATH-02`, `PATH-03`, `PATH-04`, `SECT-01`

Height is now legible in the ordinary Canvas before another analytical view is
added. Asset hue remains the material identity, while visible surface elevation
changes its brightness. A surface at `1 m` is shown at 45% of its Asset colour,
represented elevations below it fade toward a 25% floor, and the complete
Scene's highest represented surface above it reaches full colour. Changing the
horizontal section elevation must not renormalize the colours and make the
whole Canvas jump. The existing height view remains the precise numerical
inspection tool.

The mutually exclusive horizontal **Section** view now exists beside the
ordinary and height views. It is a top-down clipping plane, not a view of only
the material that intersects one infinitesimally thin elevation: remove
everything above the chosen elevation, then look down on the highest remaining
surface or cut face at each X/Y. A hill ending at `10 m` is therefore cut and
coloured at the plane when the plane is at `5 m` or `8 m`, appears whole at
`10 m`, and remains whole above `10 m`. The ordinary view is the same operation
with no finite upper clip. The Section elevation is transient view state, steps
in the open Workspace's `elevation_quantum_meters`, displays enough decimal
places for `0.125 m`, and is never written to a Scene or export.

The view offers both `Cut at` and `Cut between`. The finite form stores `Start`
plus a positive `Offset`, not two absolute endpoints, and displays only the
inclusive band `[Start, Start + Offset]`; moving Start moves the whole band.
Both values are transient and follow the Workspace elevation quantum.

Its Canvas projection is limited to the contents resolved by
`LayeredSceneColumns`: Terrain after Hill folding, River cuts and fills, and
Path surfaces. `RouteSurfaceRaster` samples the same baked triangles used by
Canvas and export at water-cell centres, preserving continuous height, stacked
surfaces and Terrain occlusion. Subtractive intervals contribute cuts from
their own baked primitives. The existing round-join footprint at an authored
transition is the exact portal neighbourhood: a disc with radius
`width_meters / 2`. Within that disc, a single bisecting plane applies to every
Bezier primitive and gives each segment only its own side. Outside it, a curve
that returns behind the plane is real Path geometry rather than portal
overreach. A cell centre exactly on the plane belongs to both sides, so a
subtractive neighbour opens it and cannot leave a Terrain plug. Two
subtractive neighbours may keep different clearances on their respective
sides. These cuts affect Terrain alone and retain every Path floor. Placements
stay hidden rather than painted over a clipped roof with a false draw-order
answer until they acquire an actual layered-surface or occlusion rule.

The engine-neutral column-resolution foundation now exists instead of a second
Canvas-only interpretation. `LayeredSceneColumns` prepares painted Terrain
folded through Elevation Regions and the existing water cuts and fills. For a
finite clip, the answer at one X/Y is the highest remaining boundary at or
below the plane; overlapping cuts are merged independently of body order and
fills survive them. Every span is closed: clipping exactly at a fill bed shows
the fill over the floor, while clipping exactly at a cut top retains the roof
boundary as a section face, because only geometry strictly above the plane was
removed. This shared rule is the foundation for later tunnel, bridge and
LookDev views; the Godot Canvas must only project its answer.

A Path tunnel is not a new body kind. It is a run of **subtractive Path
segments** inside the same independently materialized Path that already
supports level, ascending and descending additive segments. The context choice
applies to the next segment, just like grade, so one Path can be additive in
the open, become subtractive at a portal, and become additive again at its
exit. Persist the semantic operation per segment rather than infer it from
whether Terrain currently happens to overlap the Path.

Scene schema 16 now persists that per-segment operation and clearance, and the
Draw Path context applies both to the next point pair. Export schema 11 carries
both, plus a `route_surface_cut_raster` of the cells each subtractive interval
removes. Deriving those cells here rather than in a consumer is deliberate: the
portal rule at an operation transition would otherwise be reimplemented against
the raw triangles and could disagree with what the author inspected in the
Section view. The column cut and the retained floor are implemented; the red
authoring wire remains a separate slice.

Every subtractive segment carries a positive `clearance_above_meters`. At each
station its floor is the existing interpolated Path elevation and its Terrain
cut is `[floor, floor + clearance]`. Cuts affect Terrain solids and never water
or another independent fill or surface. The Path remains the materialized
floor after the cut. A Path keeps exactly one interpolated `width_meters`: for
an additive segment it is the surface width, and for a subtractive segment it
is both the surface and excavation width. SceneMaker does not reject a narrow
corridor for an Actor; a consumer applies collision radius and traversal
profile. A second excavation width is deferred until a real design needs a
narrow surface inside a deliberately wider void.

In the ordinary top-down view, a subtractive segment hidden below a Terrain
roof is an authoring wire rather than a falsely visible material surface: solid
red corridor edges with a dashed red centreline. The wire is pickable, a
selected Path is emphasized, and its points and Bezier handles are editing
detail. In a Section plane passing through the excavated span, the roof has
been clipped away and the real Path floor becomes visible. Exactly overlapping
Paths remain ambiguous from above; do not hide that with an arbitrary semantic
choice.

Implement the remaining work as focused slices: cut bake/export with a
corresponding consumer-version bump; then wire picking and manual acceptance of
an additive-to-tunnel-to-additive Path through a hill. Export must never
silently omit excavation.

A later, separate profile-section tool may project a marked region onto X-Z or
Y-Z. Its purpose is to inspect stacked tunnels, floors, ceilings, clearance,
ramps and water spans that a top-down section cannot disambiguate. It is not
part of the first horizontal Section slice.

## Settled decisions

Decisions that are closed rather than pending. They are kept because a later
reader would otherwise rediscover the question, not because anything is left to
do.

- **Assets are not added through the application.** For now, an agent edits and
  validates the Workspace configuration when a new Asset is needed; a later
  batch operation may take an explicit list. Placement geometry is
  synchronized separately through the existing narrow PolyTools boundary.

- **SceneMaker owns authoring identity.** Workspace config schema 8 added each
  Asset's `display_name` and SceneMaker `role`; both display catalogs derive
  their vocabulary and classification from it. Terrain can exist without a
  PolyTools package, while a Placement uses matching PolyTools data only for
  footprint and pivot/anchor. The sync no longer rewrites `config.json` from
  PolyTools, so `Water`, `Lava` or another curve surface remains SceneMaker's
  choice rather than an imported package name.

- **PolyTools is a Placement-geometry boundary**, not a second authoring
  catalog. Workspace loading requests only the configured Placement roots and
  their transitive Asset References; unrelated Manifests are not opened, and a
  Workspace without Placements opens without an import. Synchronization copies
  the same closure and writes a filtered catalog, leaving `config.json`
  untouched. The geometry result exposes only stable key and bounds — imported
  display names and `asset_type`s no longer cross into SceneMaker's model.

- **A hill carries no material.** `ElevationRegionDocument` lost its
  `asset_key` and Scene schema moved 11 to 12; the four authored documents
  under `workspaces/world01` were rewritten by hand, as the no-migration rule
  requires. The fold now raises the top of a painted cell and leaves its Asset
  alone, so a contour over sand and grass lifts that pattern unchanged and a
  contour over unpainted ground produces no cell at all — valid, saved, and
  without effect until Terrain is painted under it. The overlap rule that
  refused two bodies tying at one elevation under different Assets went with
  it: with no material on the body there is nothing left to disagree about. The
  export did not move — the JSON and every promise it makes are unchanged, only
  where a cell's `asset_key` comes from, which was never something the export
  said.

- **A Prop needs no Terrain under it.** This used to be an open entry — the
  export guaranteed footprint coverage, a cut could take that ground away, and
  the guarantee quietly stopped meaning what it said. It is settled now by
  product decision rather than by tightening the rule: a Prop holds an absolute
  height and is allowed to stand free, neither the simulation nor the game
  wants a general support rule, and SceneMaker treats a free-standing Prop as
  neither an error nor a warning. Placement, preview, Template composition and
  the export all stopped asking. No `requires_support` flag was added: nothing
  needs the distinction today, and the bridge section above still describes
  what a bridge would want if something ever does. Export schema 8 promised the
  coverage, so withdrawing the promise moved it to 9 even though the JSON did
  not change.

- **The editor says Placements**, while internal `Prop*` types and the
  persisted `props` array deliberately remain unchanged. Renaming those would
  be a Scene/export migration with no present runtime benefit.

- **Transitions are not added yet.** They need their own simulation-owned
  target/region contract, not a second copy of `PropDocument`, and should
  arrive only when that contract has a consumer and tests.

- **A Prop will not offer a walkable surface** (`BRIDGE-01`). The field
  `asset_profiles[].traversable_surface` was the settled answer while a deck
  could only be a Prop. An independently materialized route surface arrived for
  unrelated reasons and answers it better: a deck is a surface, not a Prop with
  a flag. The two facts that made the old entry worth keeping survive in the
  bridge section above — one place carries two surfaces, and a deck height is
  never baked into the Terrain cell below it.

- One factory per Scene kind, so `CreateInstance` no longer accepts Template
  parameters it would silently drop (`9c017be`).

- The settings menu is a closed enum instead of seven hand-picked integers
  (`a2f5e52`).
