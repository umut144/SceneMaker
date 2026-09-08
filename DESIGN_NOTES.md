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

### But the deck is built, not painted

The quad above is what a simulation walks on. It is not what the deck *is*: a
deck is a row of planks, which is why `plank` sits in PolyTools' `bridge` Set
beside `rope_post`. Reading it as a single material was a mistake worth
recording - the deck named a Terrain Asset for a while, and `grass` stood in
for it.

A plank is therefore an ordinary Placement Asset, named by `plank_asset_key`,
and what makes it a plank is that a bridge repeats it. No property on the Asset
says so, because nothing needs to ask.

The field is named after the part rather than the whole, and briefly was not:
it was `deck_asset_key` pointing at an Asset called `deck`, which read as though
a bridge named its deck and got one. A deck is the row; the Asset is the plank
the row repeats. PolyTools renamed the Asset to `plank` for the same reason.

SceneMaker lays the row out rather than shipping the parameters and letting
world01 do it. Laying it out twice is the one way for the Canvas and the
runtime to disagree, and the author only ever saw one of the two. Both halves
travel: the planks are the authority, the count and gap are the record of what
was asked for.

**Count is authored; depth is derived.** Width-with-offset was tried first and
declined in favour of a count, because a count is what an author actually names
and a deck that ends on a leftover sliver is not one anyone wants to author
around. So the depth of a plank is what the gaps leave over,
`(length - (count - 1) * gap) / count`, and lengthening a bridge thickens its
planks rather than adding one. Gaps sit between planks and never at the ends,
so a deck always starts and finishes on wood.

### A deck is walked on, so it carries a surface

A deck presents `wood`. Nothing underneath it can say so - a bridge over a
river has water below - so the deck Asset says it itself, and `surface` stopped
being Terrain-only.

Refusing a surface on a Placement was the earlier rule, on the reading that a
Placement is a thing standing *on* the world rather than part of it. The deck
is the counter-example, and the rule went rather than the deck.

The same slice took back a second rule: **a Placement with no collision Region
is no longer refused.** It occupies nothing, which is the honest third answer
next to "its footprint" and "an error". A plank occupies nothing because what
takes space there is the bridge. Falling back to the footprint is still wrong,
and still forbidden.

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
  "plank_asset_key": "plank",        // Placement role, one plank of the deck
  "anchor_asset_key": "rope_post",   // Placement role, the post itself
  "start_authoring_px": { "x": 1024, "y": 320 },
  "end_authoring_px":   { "x": 1536, "y": 320 },
  "width_meters": 4.0,
  "elevation_meters": 1.125,
  "plank_count": 12,
  "plank_gap_meters": 0.1
}
```

Length is not stored, and neither is the depth of a plank: two ends fix the
first and the count and gap fix the second, and a second source of the same
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

### Where the plank and the post come from

Both are ordinary Placement Assets, and neither is chosen by an author. That
was tried: the bridge bar offered every enabled Placement in two fields, which
meant offering a tree as a plank and re-answering per bridge a question that is
settled per world. The rule behind it was sound and the conclusion was not -
the document must record which Placement, and nothing on an Asset marks it as a
plank, but "the document must record it" is not "a human must type it".

PolyTools publishes the answer. `bridge` is a Set, and a Set says which Assets
belong together; each member carries a **role**, which is authored, never
derived, and does not follow a rename. The Workspace names the Set in
`bridge_set` and nothing more: which Asset fills which role belongs to the other
project, and copying the two keys here would be the same fact in two places.

The role is what survives. A reference's `name` follows the Asset it points at -
the place was called `plank` while the Asset was called `deck`, and today it
would have moved with it - and component names are explicitly arbitrary, free to
be `abc` and meaningful only together. So a name can never say what a member is
for.

`role` names the part and the Asset names the execution: a stone post is a
second Asset in the same `post` role, not a second role. That is why the role is
`post` and not `rope_post` - a role named after one execution ages exactly as
badly as `deck` did for a plank.

The resolved keys are still written into the bridge record, because the export
has to name what to build without a consumer resolving a Set.

It was not always so, and the detour is worth keeping because the rule that
came out of it stands: **a Prop must never name a Component of an Asset.**
`asset_key` is the whole of a Placement's identity, so a composition rename in
PolyTools would otherwise break authored maps. While the post was only a part
of a bridge model, the Workspace named that part on the Asset instead — never
on the Prop. When PolyTools made the members Assets of their own, the whole
construction went away rather than being kept for symmetry.

A Set is not offered at all: it publishes which Assets belong together, and
its members lie centered on their own pivot, so the box it would be placed by
is their overlap and means nothing. SceneMaker refuses one on request rather
than filtering it out later — what cannot stand anywhere never enters the
catalog.

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

A Placement whose model authored no collision Region occupies nothing. The
footprint fallback is still refused - it is exactly the hidden default this
Workspace forbids, and the wrong one, since a model whose collision nobody has
drawn would quietly claim every pixel it is drawn with. Refusing the Asset
outright was tried and taken back when the first real case arrived: a plank
takes no space of its own, because what takes space there is the bridge.

### The area is Structures

`Landscape` authors terrain. A bridge is built, and it is the first of a family
— fences, walls, stairs, ladders — that all describe a span with attachment
points. It gets its own overview area rather than a fourth seat beside River,
Path and Hill, because the navigation is deliberately stable and renaming it
later costs more than naming it now.

`Draw Bridge` offers `Deck`, `Planks`, `Gap`, `Anchor`, `Width`, `Height` and a
readout of the length and what the planks came out at. It shows no `Surface`
field at all: a deck is planked with a Placement, so there is no Terrain Asset
to choose. It deliberately does not offer grade, operation or clearance: a tool
that greys out half its context bar is a second tool.

### What is built

Scene schema 19 persists the record above. `BridgeGeometry` derives the four
corners, lays out the planks, and hands the deck quad to `RouteSurfaceGeometry`
as the two-point chain it is, so a bridge has no idea of a band of its own.
Export 14 ships the planks beside that quad and the four posts; export 15 adds
the deck's `centerline_samples` - the bake had them all along - and what lies
under each end, see below. `BridgeEditing` places,
removes and finds one, and answers the draft and the commit with the same call:
representable numbers, a height on the quantum, two different ends, every
corner inside the Scene, and all four posts free - of Placements and of the
posts of every other bridge. A bridge is authored whole, so it is refused
whole rather than placed with three posts.

The occupancy rule is symmetric in both directions now: `PropEditing` reads
bridge posts too, so a Placement cannot be set into a post that a bridge would
have refused to set into the Placement.

**A deck is walked by a Path's rule, and the map says what its ends stand
over.** world01 asked for both, and both were already in the house. The bake
of a deck is a route bake, so its `centerline_samples` were computed and then
dropped; export 15 ships them. They come from the shared flattener and not from
`plank_count`, on world01's own argument, which is also ours: a plank is what a
deck looks like, and if the count decided where a character may stand, a purely
visual edit would move the simulation. A straight span flattens to its two
ends, which is the honest number of samples for a line.

What lies under an end is answered by `LayeredSceneColumns`, the same rule the
Section view shows the author, with the deck itself lifted out of the column
(`LayeredSceneColumn.Without`). SceneMaker ships the answer -
`ground_at_start` / `ground_at_end`: elevation, Asset, source - and does not
judge it. Whether the ground is within a step of the deck is a property of an
Actor, and the settled rule that keeps an Actor's collision radius out of a
corridor's width keeps its step height out of a bridge's end. What SceneMaker
can still do, later, is show the author the same two answers while the bridge
is being drawn (`BRIDGE-05`), so a bridge into the river is seen here first.

Export 11 refuses a Scene holding a bridge rather than dropping it, the same
way export 10 refused an excavating Path. It costs nothing while no tool can
author one, and it is the one thing that cannot lose a bridge silently.

### Not in the first slice

A Scene Template cannot carry a bridge yet, for the reason it cannot carry
water: composition moves Terrain cells and Props and nothing else, so it would
be lost silently. Refuse it explicitly instead.

Selecting and reshaping an existing bridge waited, like `PATH-03` did for
Paths, and is built now: `Select Bridge` picks a deck or an end, drags either
end or the whole span, and lets `Planks`, `Gap`, `Width` and `Height` act on
the selection through the same validation the first click uses.

The Section view sees a deck the way it sees a Path, because that is what a
deck is geometrically: `RouteSurfaceRaster` samples `BridgeGeometry.DeckRoute`
beside the authored Paths, from the same triangles the export bakes, so a plane
above the deck shows the planks' colour and a plane below it shows the river.
The posts are Placements and stay hidden there like every Placement, by the
rule in the Section section rather than by an omission.

## Water is drawn, not only counted

`src/SceneMaker.Core/WaterGeometry.cs`, `SurfaceBand` - `WATER-01`

A river was readable and invisible. `water_raster` said which cells the water
occupies, the column rule said what that means vertically, and world01 could
keep an Actor on the bank and send it over the bridges - all of it correct, and
none of it visible. One walked against nothing. Export 16 adds `water_bakes`:
the water surface as a band, one per body.

### It is the band that already existed

Nothing new was meshed for it. `RouteSurfaceBake` is the one place authored
points become triangles - it was written for Paths, took the bridge deck as the
two-point Path a deck is, and now takes a river's curve as what it is: a plan
with a width and a height at every point. What used to be `Build(route)` split
into a shared `Bake` and the route-specific half around it, and water asks for
the shared half through `BuildBand`.

That was the whole point of the request from world01, and it is ours too: a
consumer that reads a Path's bake and a deck's bake reads this with the same
types, and SceneMaker keeps one answer to what a curve looks like. A river-only
mesher would have been a second one, and the two would have disagreed on a bend
eventually.

### A river carries no segments and no single height

The band is `BakedSurfaceBand` rather than `BakedRouteSurface`, and the export
record drops what a route has that a river does not. A Path's interval holds a
grade, an additive/subtractive operation and a clearance; a river has none of
the three, and giving it a segment array carrying invented values would be the
shape this project already refused for water and routes as documents. It does
not get one as derived data either.

The height needed the same care in the other direction. A bridge is level by
definition, so a deck's bake ships one `elevation_meters` and a consumer may
check every corner and sample against it. A river falls - that is the whole of
its flow direction - so there is no body-level height here at all, and the
contract says outright that carrying that check over from a deck is wrong
rather than merely unnecessary. Every vertex and every sample holds the height
interpolated at its own station.

The bed and the cut stay out of the band. They describe a volume, the raster
already delivers them per cell, and a second place to read a river's depth from
is a second place for it to be wrong.

### The band and the raster are allowed to disagree

They answer different questions and are derived by different rules, so they
differ by fractions of a cell at an inner bend and by rounding everywhere - the
raster to the millimetre because it is written down per cell, the band to six
decimals because every bake is. World01 said so first and it is worth writing
down as ours: the raster is the truth for the game, because it is what the
author checked in the Section view and what the simulation reads; the band is
the truth for the eye. Snapping either to the other would give up what makes it
the answer to its own question. The contract states the divergence as a
guarantee about what is *not* promised, which is the only way a consumer can
rely on it.

Two consequences follow rather than being decided: the band is not clipped to
the Scene, exactly as a Path's bake is not, and two overlapping bodies produce
two overlapping bands, which is what a widening river looks like.

### What this closes and what it leaves open

World01's earlier question - what rule to flatten and join a water curve by, so
it could mesh one itself - is closed by not needing an answer: since branches
are authored rather than generated, SceneMaker ships the geometry and the
consumer recomputes nothing.

What was left open here is now answered by export 17; the section below says
how, and why waiting for world01's case was right.

## A branch is a body, and a state switches which bodies there are

`src/SceneMaker.Core/WaterGeometry.cs`, `SceneExport.cs` - closes `WATER-01`

World01's Phase 2 gave the question a case, and every part of the answer fell
out of what a river already is rather than out of a new idea.

### A branch could not be anything but its own body

A body *is* an open curve, source to mouth. Four separate things rest on that:
the corridor rule projects a cell onto **one** centerline, the flattener turns
**one** Bezier chain into segments, `water_bakes` ships **one** band per body,
and the export refuses a body whose surface climbs between consecutive points of
**one** chain. A branching tree inside a body opens all four at once and answers
none of them. A branch as its own body costs no new geometry at all: it already
has an id, a place in the order, a raster and a band.

Its parent and the station it leaves at are **derived**. The endpoint position
and both curves are authored, so the projection follows from them; storing the
station would mean an edit far upstream, which changes arc length, silently
moving a fork nobody touched. What the drawing tool does author is the parent's
identity, so that a fork pulled apart becomes a refused export rather than a
junction that quietly stops being reported.

The entry carries both stations and no kind field. A branch and a rejoining side
channel are then the same entry read at different stations, which is what let
world01 implement both without telling them apart - the shape did the work an
enum would have made them do twice.

### Why alternatives are bodies and not sections

A river that is wide while a branch is shut and narrow while it flows is two
geometries. They have to be authored, because SceneMaker rasters nothing it was
not given.

Sections inside a body were the obvious shape and are the wrong one twice over.
A Path's interval carries a grade, an additive/subtractive operation and a
clearance; a river has none of the three, and this project already refused to
give water a segment array carrying invented values. And a section with its own
width needs its own band, which contradicts one band per body - the rule that
made a river readable by the code that reads a Path.

So the alternatives are bodies, and the author splits the main river at the
fork. That splitting is worth doing in the phase *before* the narrowing exists,
even though nothing needs it yet: it keeps the ids stable across the change, and
it makes the fork a real authored point of three curves - the shared node an
author pictures, without the document having to know what a node is.

### The list, and the hole it closed

The first draft gave a body one state, and world01 found the hole in the example
that was meant to demonstrate the design: with two branches on one stretch and
states `none | first | both`, the first branch flows in `first` **and** in
`both`. One state per body would have forced it to exist twice with identical
curves - the same multiplication of geometry that ruled out shipping the river
in several versions, one level further down.

`active_in` is therefore a list, and it is called that rather than `states`
because `activation_groups[].states` already means the definition of the states.
Two meanings of one word in neighbouring fields is exactly what went wrong with
`span`, in this same file, in the same week.

Named states rather than a boolean per body for the same reason the list is a
list: the states a Scene is really in are enumerated by the author, not produced
as the cross product of independent switches. Two branches give three states if
only three combinations occur.

### `inactive` says what the world is without that water

Switching the fill and never the cut was one rule too few. A river that falls
dry leaves its bed; a branch that opens by carving does not have one beforehand.
Both are ordinary, neither is readable from the curve, so the field is required
and has no default - a default here would decide silently which of two authored
intentions was meant.

The first draft justified it by what a state change costs a consumer, and world01
corrected that: when water leaves a channel, where an actor may stand changes
either way. The field is about meaning, and the contract says so in those terms.

### Activation runs downhill

`src/SceneMaker.Core/WaterActivation.cs` - export 18

The first cut had a hole the author found by drawing the thing on paper: nothing
said what happens to a branch when the river it comes off is switched off. It
stayed "active" and sat there fed by nothing. The plan had been to refuse that
combination in validation - which would have been a rule saying that a
reasonable map is not allowed, instead of a rule saying what it means.

The answer costs no field. A body is there when its own activation says so and
the body its source sits on is there, and the tree that runs down is the
junctions we already ship. So switching a river off takes everything hanging
under it with it, three levels deep or thirty, and a child never repeats its
parent's states - which is the only way the two cannot disagree.

Two things fell out rather than being decided. Where two bodies feed one, its
source sits on both, so it has water as soon as either does: that is what water
does, and the alternative would have needed a rule nobody could have guessed.
And `inactive` still answers for the body carrying it, so a branch left dry
because its river was shut leaves its bed or no trace by its own statement.

The version rose for a rule and no field. A reader pins a number in order to
know what the bytes mean, and they now mean more than they did; leaving 17 in
place would have made two readers of the same number behave differently, which
is exactly what pinning is for.

### The direction is authored, the stations are derived

World01 found the hole while writing their reader. Export 18 told them to find
what feeds a body by taking the junction whose `own_station_meters` is `0` -
a marker that reads a meaning out of a position, and one that stops working the
moment a body is both a branch and has a branch leaving it at its own station
`0`. It then carries two such entries, one for each, and nothing about the
number tells them apart.

The relation was never only in the raster. `scene.water_bodies[].junctions[]`
carries the authored claim with `"end": "source"` on it, and that is what a
consumer should read: the Scene says which end of which body makes the relation,
the raster adds the two stations, and the two can be checked against each other.
The contract now says so.

Two things worth keeping from this. A marker derived from a value is a rule that
holds until the value repeats, and this one repeated in a shape the recommended
authoring produces - splitting at a fork puts a source exactly where another
body's station `0` is. And I had told world01 the opposite a week earlier: that
the authored parent binding was an authoring aid and not export data. It ships
in the Scene block, has all along, and being wrong about our own file is what
made the bad rule look necessary.

No version moved for it. The bytes always meant this; what was wrong was the
recipe for reading them, and a reader that followed `end` was right the whole
time. A number that rises for a corrected sentence teaches a consumer that a
rise might mean nothing.

### Widths are not coupled, and that is what keeps the tree cheap

The same sketch asked for something else: a branch taken off an 8 m river should
narrow it to 6 m from that station down, drawn as nested bands. The bands are a
good idea and cost nothing - they are a view over the tree, since what each
branch takes is known.

The coupling itself is refused, and the reason is not the arithmetic. 8 − 2 = 6
is a convention rather than physics, and an edit to one body silently moving
another is the shape this project keeps throwing out - but the deciding argument
is combinatorial. If a branch can be switched, a stretch below it has two widths
and therefore two bodies that are alternatives. A second branch makes four, a
third eight, and a tree with nine of them would ask an author to draw five
hundred versions of a trunk. So the width of a river is authored once, the bands
show where its water goes, and a branch being shut changes whether it flows and
not how wide anything is.

`Create Branch` may still offer the subtraction as a starting value. A suggestion
that the author can overrule is a different thing from a rule that holds two
bodies together.

### What is still open

Nothing about the format. What the model cannot express is a body that leaves no
trace while inactive **and** carries a cut in some states but not others; there
is no case for it, and `inactive` would have to become a per-state answer to get
it. World01 offered a list of former group names so a rename could be reported
rather than lost, and declined it themselves in the same paragraph - their sync
already reports an unknown name. `WATER-02` holds that, deliberately.

## Selecting a river, and the gesture that was left over

`src/SceneMaker.Editor/ToolInteraction.cs`, `SelectOrGrabWaterBody` - `WATER-05`,
`WATER-06`, `WATER-07`

Rivers were the one curve area with no selector. A drawn body could only be
redrawn, and a body authored by hand - which is how every branch exists today -
could not be touched at all. `River:Select River` is the bridge selector's
shape against a point list: grab a point of the selected body, grab its
corridor to carry the whole curve, or choose another body.

Two details are worth keeping. Points are tested before corridors, because at a
fork every point worth grabbing lies inside two corridors at once. And a
dragged point snaps to the **water** grid rather than the Terrain grid the other
tools use, because that is the grid a curve point is required to sit on; the
shared `SnapToGrid` helper would have produced a drag that is refused at the IO
boundary instead of on release.

### A broken fork is said, not prevented

Moving a point can pull a branch off its parent. Preventing that would mean a
source that cannot leave its parent's corridor, so dissolving a fork would need
its own separate step, and a river would have one rule the other curves do not.
So the move is taken, and `WaterEditing.BrokenJunctions` is asked after every
change: the status line says it in the same breath, and the selection turns red.
The editor validates at its IO boundaries like everything else here - a document
may be wrong between two edits - but the author must not first read about it in
an export hours later.

### Where a branch starts is not where the pointer was

`Create Branch` is `Draw River` with one point changed. Its first point is put
on the body it is nearest to and takes that body's surface height there, and the
junction is written in the same edit that places the curve.

Both halves of that are load-bearing. Drawing a river whose first point happens
to land in another's corridor already produced the *shape* of a branch - what it
did not produce was the statement that it is one, and that statement is what a
consumer splits its flow at and what the export checks the two heights against.
And writing the junction afterwards would leave an undo step in which a river
stands on another one and says nothing about it.

The snap has an order that matters. A curve point belongs on the water grid, so
the exact nearest point on the centerline is found first, and then the grid
position around it that is nearest to the curve **and** still inside the
corridor is taken. Snapping first and checking afterwards offers anchors on a
narrow river that the export then refuses - the corridor of a one-metre river is
half a cell wider than the grid step it would have been rounded by.

What the author gives up is exactness: the source sits up to half a cell off the
centerline. That is below the resolution the raster answers in, and the station
and the height are read from where the point actually is rather than from where
it was aimed, so nothing downstream inherits the error.

### Every press already meant something

Inserting a point has no gesture left. A press on a point grabs it, a press on
the corridor selects or carries, a press on open ground clears, and the same
three with the eraser remove a point or the body. `ToolInteraction` is handed a
position and nothing else - no modifier keys - and that is deliberate: input
takes one path and answers with exactly one `ToolOutcome`.

The two ways out are a modifier, which would widen that contract for one
feature, or a tool of its own. `WATER-05` holds it, and a tool is the answer
that fits what is already here.

### What the context bar can and cannot say

The four water numbers edit the selected point, exactly as the bridge numbers
edit the selected bridge - the same field meaning the same thing whether
something is selected or not. Two things are deliberately absent. The `Point`
mode field is hidden while selecting, because it decides what the *next* drawn
point does and there is no operation yet for an existing point's handles
(`WATER-06`). And activation offers only the states a Scene already declares,
setting exactly one: a body active in several states stays hand-authored, and
nothing here creates a group. Both want a list of bodies against a list of
states, which is the Outliner's picture and not a dropdown's (`WATER-07`).

## How deep a bed has to be is the World's statement

`src/SceneMaker.Core/WorkspaceConfiguration.cs`, `MinimumChannelDepthMeters`

World01 first decided that a river bed lies deeper than a character can climb -
one metre against a step of half a metre - so that a river is not crossable in
either state, and then, a day later, that a shallow river should be wadeable
after all. Both are ordinary game-design decisions and neither is SceneMaker's.

That is exactly why the number lives in the Workspace and not in Core. It
follows from a step height, and SceneMaker knows nothing of characters or steps;
a constant here would be a game rule hidden in an engine-neutral tool, which is
what the metric rules in `AGENTS.md` exist to prevent. `minimum_channel_depth_meters`
therefore sits beside the other grid metrics, and validation holds an author to
whatever their World wrote down and to nothing else. It is not exported: a
consumer receives the actual depths and has no use for the floor they were held
to.

The reversal is what shows the shape was right. A rule that had been a constant
in Core would have cost a code change, a bump and a release; as a Workspace
value it cost one number in one file, with no code touched and no test moved.
World01 now sets `0.125`, one elevation quantum, which is the floor that is left
once game meaning is taken out of it: a channel shallower than the smallest
height this Workspace can author is not a channel at all.

What the field still does while authoring is the same - the depth input cannot
offer less, and a value carried over from a deeper Workspace is lifted rather
than saved as it was. What it no longer does is decide whether a river can be
crossed. That question moved back to where it was always going to be answered,
in the consumer's column rule.

### A group nothing is in

Refusing an empty activation group would block the author between creating one
and putting the first body in it, and validation runs on the way to disk. So it
is an export warning, in the company of the subtractive Path segment that
removes no Terrain: authored intent that has no effect, said out loud on the way
to a file.

This was not a hypothesis. A branch quietly lost its activation while its group
stayed declared, and the export wrote the file without a word - a switchable
branch that was no longer switchable, in the map a consumer was about to test
its state machine against.

## A flow network in front of the authored curves

`FLOW-01` - nothing built yet

The author drew the river tree on paper and asked whether taking a 2 m branch
off an 8 m river should leave 6 m. The honest answer turned out to be that
8 − 2 = 6 is not the absence of a model but a model with a particular exponent,
and the wrong one for rivers. What follows is the model I would build instead,
and where it has to sit so that nothing already agreed with world01 moves.

### Where the line goes

```
flow network  →  authored bodies  →  raster + bands
      ↑                ↑                    ↑
  the model       the baking          the export
```

The network produces ordinary water bodies with ordinary per-point widths and
depths. **The export contract learns nothing.** World01 keeps reading geometry
and knows nothing about discharge; export 18 stands. This is authoring, not
simulation, which is also the only reason it belongs in SceneMaker at all.

It also dissolves the objection that killed width coupling two notes ago. Two
hundred variants of a trunk are unauthorable by a person and unremarkable for a
generator, and derived data that says outright that it is derived is not the
silent cross-object edit this project keeps throwing out.

### The model

The network is already in the document: junctions are its edges and bodies its
segments. Nothing new is stored for the shape.

What is added is a scalar carried along each segment, three rules, and two
authored numbers per fork.

**The scalar.** Discharge, normalised so that the root carries 1. Absolute
cubic metres per second would be false precision - nobody knows the discharge of
an invented river - and normalising costs nothing, because the author anchors it
with a width instead.

**At a node, the scalar splits.** For water it is conserved: what arrives leaves,
so the shares of a fork sum to 1. Two rivers running together add. That is the
whole node rule.

**Along a segment it is constant** between nodes. Rain and seepage exist and are
not worth a field until somebody wants a river that grows without tributaries.

**From the scalar to the geometry**, and this is the part worth getting right:

```
width = a · Q^0.5
depth = c · Q^0.4
```

Those exponents are not invented. Downstream hydraulic geometry - Leopold and
Maddock, and every survey since - finds width ∝ Q^0.5, depth ∝ Q^0.4 and
velocity ∝ Q^0.1 across a network at a comparable flow frequency. The three
exponents sum to 1 because they must: Q = width × depth × velocity, so
continuity fixes their sum and only their division is empirical. The
*downstream* relation is the one that applies here, because we are comparing
different channels of one network rather than one channel at different times;
the at-a-station exponents are different numbers answering a different question.

Two consequences fall out that are worth stating because they are exactly what
the sketch got wrong, and they are right.

**Branches do not subtract.** An 8 m river splitting its flow evenly gives two
channels of 8/√2 ≈ 5.7 m, together half again as wide as the trunk. Anyone who
has seen a delta knows this. Subtraction is the exponent-1 special case, which
would say that width is proportional to discharge and therefore that velocity
and depth never change - not a simplification but a different world.

**Depth follows too, and meets the rule world01 just set.** A branch taking a
twentieth of the flow is not merely narrow, it is shallow: `0.05^0.4 ≈ 0.30`, so
about a third of the trunk's depth. Against world01's floor - a bed deeper than
a character can climb - the model can say something a width-only model cannot:
*this branch is too small to exist in this world*, because its bed would be
climbable and their rivers may not be. The flow model and the Workspace minimum
meet without either knowing about the other.

**What the author writes** is then very little: the root's width and depth, which
anchor `a` and `c`, and one share per branch. Everything else is computed. The
tool may of course ask for the share as a width - "make this branch 3 m" - and
invert it; the stored number stays the share, because that is what survives a
change further up.

### It answers the state question by itself

A branch that is shut carries nothing, and its water goes down its siblings -
so shutting it redistributes the shares and every width below follows. The
onion bands the author wanted are then not a drawing convention but a picture of
the model, and the 2^n variants of a trunk are computed rather than drawn. What
`activation` switches stays a set of authored bodies; the generator is simply
what wrote them.

### Cracks were the argument, not the requirement

The case for a model rather than a warning was that the same shape would carry
to crack formation. It examined well - a directed network, a transported scalar,
a node rule and a power law are common to both - but a crack differs in every
rule that hangs off that skeleton: its nodes are lossy where water is conserved,
its segments decay where water holds, and aperture against energy release is not
width against discharge.

The author has since put cracks where they belong, in PolyTexture, and taken
them out of this. That is a simplification and worth taking: the model may now
be **about water**. Its node rule is conservation, its segments are constant,
its exponents are the hydraulic ones. Nothing has to be a parameter in order to
serve a second case that is not coming, and a shape kept general for a caller
that never arrives is the kind of generality that is true only if nobody looks.

The skeleton is written down above; if PolyTexture ever wants it, it is three
paragraphs, not a dependency.

### Without a generator it still earns its place

The generator is deferred, and the model does not depend on it. Its near-term
use is a suggestion: `Create Branch` already knows the parent, the station and
the parent's width, so it can offer the branch and the stretch below the widths
and depths the model gives instead of the context bar's defaults. That is one
authored number - the share - turning into four correct ones, in a tool that
already exists.

### What this costs, and the question it leaves

Storing the network alongside the bodies means the document holds two truths
about one width, and they can disagree the moment somebody drags a point. The
answer is the one every procedural tool arrives at: a body carries a mark saying
which network baked it, editing it by hand **detaches** it, and a detached body
is left alone by the next bake and says so in the Outliner. That mark is the
real cost of this design and the thing to get right first - a bake that silently
overwrites a hand edit is worse than no generator.

The open question is where the network lives. Inside the Scene it travels with
the map and can never be lost; beside it, it stays out of a document that is
otherwise only authored geometry. Inside is probably right, for the same reason
`activation_groups` is inside: a thing that relates several bodies has nowhere
else to be.

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
`LayeredSceneColumns`: Terrain after Hill folding, River cuts and fills, Path
surfaces, and bridge decks as the level two-point Paths they are. `RouteSurfaceRaster` samples the same baked triangles used by
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
removed. This shared rule is the foundation for the tunnel and bridge views,
which now rest on it, and for a later LookDev view; the Godot Canvas must only
project its answer.

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
