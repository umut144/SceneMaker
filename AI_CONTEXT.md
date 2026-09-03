# SceneMaker context

SceneMaker is a standalone semantic scene authoring tool. A workspace is one
game and contains its own scene/template data and `config.json`.

Each Workspace consumes a synchronized copy of the current PolyTools Runtime
Export below `imports/polytools/`. PolyTools Catalog schema 1 is the closed
Asset set; Runtime Manifest schema 16 supplies geometry, hierarchy, Asset
References, pivots, and validated gameplay Regions. Authored and
Component-bound Regions remain distinct, do not contribute to SceneMaker's
visible Asset bounds, and older Manifest schemas are rejected. SceneMaker never
discovers packages by scanning directories and never reads a sibling PolyTools
project at runtime.

The workspace configuration supplies terrain-cell size, authoring/game pixel
densities, enabled assets, editor colors, the surface each Terrain Asset
presents to a consumer's simulation, and how each Terrain Asset is authored -
painted as cells or drawn as a curve; whether an Asset is Terrain or a Prop is
PolyTools catalog data and is never overridden. A surface is an open
lower_snake_case token such as `land` or `water`, held per Asset rather than per
cell so that one Terrain Asset cannot contradict itself. Prop footprints and
anchors are derived from transformed PolyTools visible geometry and rounded
outward to whole authoring pixels. Scenes store only semantic terrain, props,
template anchors, and exact PolyTools `asset_key`s.

New workspace configurations explicitly start with 1 m terrain cells at 32
authoring pixels and 192 game pixels per meter. These values remain authored
workspace data rather than an implicit reader or export fallback.

## Water

Terrain water is authored as curves, not as cells. A water body carries a
centerline of Bezier points carrying width and a vertical section; the cells a
simulation reads are derived from it and never stored in the Scene. That is
what keeps a river reshapeable after it has been drawn, and it means the
rasterization exists exactly once, in `WaterGeometry`.

Which Terrain Assets are drawn that way is authored Workspace data
(`authoring: "cells" | "curve"`), not something SceneMaker works out from a
surface token whose meaning it never reads. It is what the tool bar narrows
itself by, and it makes two mistakes impossible: a river painted cell by cell,
and a river made of grass.

The water grid is finer than the Terrain grid - `water_cell_meters` in the
Workspace config, 0.5 m for `world01`, where a Terrain cell is 1 m - because a
bank has to follow a curve and a whole Terrain cell cannot. It has to nest a
whole number of times inside a Terrain cell, so a water cell never straddles
two of them. Terrain under a river stays what it was: the water layer is finer
and answers for its own positions, which is a question of resolution rather
than the two-surfaces-at-one-place problem a bridge poses.

A River is an open curve. Its first point is the source and its last is the
mouth - that is the whole of its flow direction. Its corridor is every water
cell whose centre lies no further than half the interpolated point width from the
centerline, cut off by the two lines perpendicular to the curve at its ends, so
a river starts and ends straight across instead of bulging into a half-circle.
Width belongs to each curve point and is interpolated linearly over centerline
arc length, so one river can widen or narrow without being split into bodies.

The curve arithmetic underneath is not water's own. Flattening a cubic Bezier
chain, measuring arc length along it and projecting a position onto one of its
segments live in `BezierChain`, which knows nothing about rivers; a mountain
outline and a route flatten the same way. What stays in `WaterGeometry` is what
only a river means: the width along the corridor, the two end caps, and the
vertical section. Each authored kind keeps its own document record and converts
into the shared chain - one shared record instead would force every kind to
carry the others' fields and tie schemas together that have no reason to change
at the same time. `BezierChain` flattens open chains only. A closed loop is not
the same thing with its ends joined: it has no first and last point to clamp
against, its stations wrap, and it needs an orientation before anything can be
said about its inside, so it gets its own entry point rather than flowing
quietly through code that clamps.

A flattened loop comes back as a ring whose first point is not repeated at the
end, with the way home carried in its own `TotalLength` rather than in the last
station. Its orientation is derived from the shoelace area and never applied:
SceneMaker authors in `scene_local_bottom_left_y_up`, so a positive area is
counter-clockwise - in a y-down space the same sign would mean the opposite -
and a consumer that needs a particular winding asks and decides for itself
rather than having the authored points turned around behind it.

Whether a ring is usable as a contour is a geometric question with a stated
tolerance, not an algebraic one. Flattened coordinates are ordinary doubles and
the products inside an orientation determinant are rounded before they are
subtracted, so `cross == 0` is not a dependable test for collinear and equality
is not a dependable test for touching. The rule is instead that a contour needs
three distinct points, an area, non-adjacent edges at least
`ClosedChainGeometry.SimplicityToleranceAuthoringPixels` apart, and adjacent
edges that share their common corner and nothing else. A near touch is rejected
for the same reason a touch is: nothing downstream could tell the two apart. The
tolerance is a four-thousandth of an authoring pixel - far above the arithmetic
and far below anything the grid can resolve - and it says only that the test
decides the same way every time. How thin a contour may usefully be is a
different question, and it belongs with the rule that fills one.

Filling a contour asks the same question of a cell that a river's corridor
does: is its centre inside. A mountain's edge and a river's edge therefore
cannot disagree by half a cell where they meet. A centre within the same
tolerance of the outline counts as inside - the corridor already admits a
centre lying exactly on its edge, and a contour whose own edges may sit no
closer together than that tolerance cannot then be rastered by a rule
pretending to resolve less. Everywhere else a horizontal ray decides by
even-odd parity, which agrees with the winding rule for simple rings and only
for those, which is why only those may be filled - the fill checks and refuses
rather than trusting its caller. Nothing reads the winding, so the same outline
drawn either way round fills identically. Narrowing the ring to the segments a
row can meet is an optimisation and only that: a segment on one side of a row
cannot cross its ray, and one further off than the tolerance cannot be within
it.

A mountain body keeps that closed contour, one cell-authored Terrain Asset and
one absolute top elevation in the Scene document. Its raster is derived rather
than stored. Painted Terrain and every mountain contribution form the same
solid column: the highest top wins, so a second contour at 15 m inside one at
10 m is a mountain on a mountain without an offset or parent link. A lower
contour never cuts existing Terrain down. At the same height a contour owns the
surface over a painted cell; two mountain bodies with different Assets are
rejected where they tie, because geometry supplies no winner.

That tie is a question about the contours rather than about which of them
happens to show, so it is asked of every contribution a cell receives - a tie
buried under a third, higher body is refused exactly like a visible one. Every
contribution is therefore collected before any of them is compared: folding
body after body and testing each against the winner so far would make the
answer depend on the order the bodies were folded in, which is their ID order
and therefore the order they were drawn in. The same document has to be refused
whichever way round it was authored, and with the same message.

A mountain point's anchor is checked at the IO boundary against the two rules
`Draw Mountain` authors by: it lies inside the Scene and on the Terrain grid.
Its handles are not. A handle shapes the curve rather than naming a place, so
snapping it would snap the shape, and the curve it shapes may bulge past the
edge of the map - the raster stops at the Scene, the document does not fail.
How many points a contour needs is not a count either: two anchors whose
handles bow the closing edges apart already enclose an area, so the document
rule stays at two and `MountainGeometry.RequireContour` decides whether the
flattened ring is usable. That the canvas tool asks for three placed points is
a tool decision about drawing, not a rule about geometry.

Mountain contours are editor source and do not enter export schema 9. Export
folds them into the ordinary Terrain cells promised to the runtime. This also
keeps mountains in the cuttable Terrain solid: a river cut can make a channel
or tunnel through one, whereas modelling a mountain as a fill would incorrectly
make it immune to cuts. Scene Templates cannot carry mountain bodies until
composition knows how to translate their source contours.

The `Draw Mountain` Terrain tool is the authoring entry point for those bodies.
It uses the same `ToolInteraction -> ToolOutcome` path as every other tool:
points snap to the Terrain grid, handles remain unsnapped, Enter closes the
contour as one undoable body, and Escape or draft undo removes one point. Point
modes are per next point and the aligned automatic handles are cyclic, so the
closing Bezier edge has no special endpoint behaviour.

The draft has three states and they answer one question - what would Enter do.
Too few points is Incomplete and drawn neutrally, because a contour that is not
finished being asked for is not a contour that was refused. Ready is a promise:
the whole attempt has already been made against this Scene, Asset and height by
`MountainEditing.TryPlace`, which is the same call the commit makes, so a ready
contour cannot then be refused and a refused one cannot slip through. Everything
else is Blocked and carries the reason, including a contour that is perfect
geometry and still ties with an existing body at one elevation under a different
Asset - a preview that only checked the ring would call that one ready and lie.

A finished body also carries a contour on the canvas, derived from its points
and drawn in a colour taken from a small editor palette by a stable rule over
its `mountain_body_id`. Both are derived: neither the contour nor the colour is
in the Scene document or the export, and neither means anything to a consumer.
It exists because folded cells cannot say where one body ends - two mountains of
one Asset are one surface without it - and the height view should stay an
analysis rather than the only way to see a boundary.

The eraser asks about the Terrain cell under the pointer rather than the exact
position in it, because the raster asks about a cell's centre and two different
sample points would let the body the author sees filled and the body the click
removes differ by half a cell near an edge. `MountainEditing.FindAtCell` answers
once for both the hover highlight and the removal, so what lights up is what
disappears: the whole topmost body over that cell, never a single derived cell.

## Height is a stack, not a number

A place is not one height. Terrain says how high its solid column reaches;
spatial elements say which vertical ranges they take out of it and which they
fill. A column is resolved rather than stored: start from the Terrain, remove
every cut, add every fill.

Each curve point carries three absolute heights - the water surface, the depth
of the channel below it, and the headroom required above it. From them follow
two spans sharing a floor: water fills `[surface - depth, surface]`, and
`[surface - depth, surface + clearance]` is taken out of the Terrain. One rule,
three shapes: where the ground never reaches the headroom the river is open,
where it does a ceiling remains and the river is a tunnel, and in between it is
a cut channel. The Terrain document is never lowered for any of them.

The heights are absolute and interpolated linearly over arc length between the
authored points - never over the curve's own parameter, which runs unevenly and
would tie a river's gradient to the length of its handles, and never through a
smooth spline, which can overshoot and make a stretch run uphill between two
points that both fall. The editor can snap a point to the Terrain under it while
drawing, but what it writes is the number: no offset is stored, so repainting
the ground later leaves the river where the author put it.

Directly authored absolute heights sit on the open Workspace's
`elevation_quantum_meters`: the Scene default, Terrain and Prop elevations, and
water-point surfaces all share that vertical authoring grid. It does not
quantize widths or vertical extents such as channel depth and clearance, and it
does not turn interpolation into steps. A water cell between two valid points
may therefore have a surface that is not itself a multiple of the quantum.

Cuts apply to Terrain and never to fills. That one sentence is what will let a
bridge deck cross the river it spans without the river carving it away.

A Prop needs no Terrain under it. Its `elevation_meters` is absolute, like every
other authored height, so where it stands is already fully said and the ground
below is a separate fact rather than a precondition. Standing free is therefore
ordinary: a platform over a chasm, a lamp on a bridge deck, a rock ledge with
nothing beneath it. SceneMaker checks what geometry can decide by itself - a
representable footprint, inside the Scene, meeting no other Prop - and stops
there. Support is a question about an Actor and its simulation, which is where
it is answered; asking it here would have to be unasked for the first bridge.
Nothing warns, nothing blocks, and no Asset flag records the difference, because
no consumer needs the distinction today.

`world01` renders water with marching squares over that raster. The raster is
what the simulation reads; the smooth mesh is presentation, and the authored
curve travels in the export so a consumer that wants a smooth band instead of a
marched one has it.

## Template Anchors and groups

The authored Scene Instance is the fixed part of a map. Template Anchors are
the parts that are allowed to vary: an Anchor names a group, and every Scene
Template in that group is an equally acceptable thing to place there. Selection
within a group is therefore deliberately arbitrary. It is not a ranking, and it
is not a bug to be fixed with a priority rule - a Template that does not belong
at a given Anchor belongs in a different group.

This is what makes the result hybrid rather than procedural. The main map is
authored by hand and stays where it is; the Anchors and their groups add
variation on top of it. A Scene Template is a small finished Scene used as
seasoning, not a tile in a generator.

Composition is reproducible rather than fixed. `TemplateComposition.Compose`
selects deterministically from a seed, so the same seed always yields the same
map. The editor's preview draws a fresh seed every time on purpose: seeing a
different arrangement each press is the point, because all of them are valid.
The preview is transient and is never written into a document.

The preview is look development, not a specification. In the game an Anchor is
an event slot a server decides about and swaps during a session, so no consumer
runs this selection and none is expected to reproduce it. What the export
contract fixes is only the geometry of placing a Template at an Anchor; which
Template goes where, and what it is allowed to replace, is the consumer's. An
Anchor with nothing at it is ordinary rather than an authoring error, which is
why fewer Templates than Anchors composes partially instead of failing.

`EXPORT_CONTRACT.md` is the consumer-facing description of that export: field
meanings, units, the coordinate space, what the export guarantees, and the
composition algorithm a runtime has to follow to agree with SceneMaker's own
preview. Hand that file to a consumer rather than this one.

SceneMaker has no runtime dependency on PolyTools or the source game project.
Later consumers receive a small generic JSON export; they resolve the same
`asset_key`s in their own PolyTools content boundaries. Each export is a
versioned snapshot of one Scene, the Workspace grid, and derived enabled asset
profiles; editor colors and raw PolyTools documents are not exported.

Scene Templates are exported as their own files rather than embedded in the map
that uses them, so that a Template can be added, replaced or removed between
seasons without rewriting the map. A consumer collects Templates by the group
number each one carries and places them at Anchors of that group. Because a
missing group would leave an Anchor silently empty, an Instance export also
lists `required_template_groups`: the groups its own Anchors ask for, so the
consumer can check its set on load. Exporting from the editor always writes the
whole Workspace; the CLI can export a single Scene by id for a surgical swap.

For the included workspace, run `scripts/sync_polytools_world.sh` after a
successful PolyTools Runtime Export. Run `scripts/export_scene.sh` to validate
and export every Scene of `world01`; the default output goes to
`world01/exports/`. The Workspace currently holds two Scene Instances -
`overworld01`, a 100 x 100 Grass map, and `cave01` - plus two Scene Templates.

The Workspace key `world01` and the Scene id `overworld01` are different names
for different things: the key is bound to the PolyTools world it imports from
and must equal the directory it lives in, while a Scene id names one map inside
that Workspace.
