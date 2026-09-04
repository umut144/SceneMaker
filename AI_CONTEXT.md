# SceneMaker context

SceneMaker is a standalone semantic scene authoring tool. A workspace is one
game and contains its own scene/template data and `config.json`.

The Workspace configuration is SceneMaker's closed authoring catalog. Besides
the grid metrics it owns every enabled Asset's stable `asset_key`, display name,
SceneMaker role, editor color and, for Terrain, its surface token and authoring
kind. A surface is an open lower_snake_case token such as `land` or `water`,
held per Asset rather than per cell so that one Terrain Asset cannot contradict
itself. Terrain and curve Assets need no PolyTools package merely to exist.

Each configured Placement may consume a synchronized PolyTools geometry copy
below `imports/polytools/`. The matching root Manifest and the transitive Asset
References its visible shape needs are the whole import: unrelated packages are
neither copied nor loaded, and a Workspace with no Placements needs no PolyTools
import at all. SceneMaker derives the footprint and pivot/anchor from that
transformed geometry and rounds the bounds outward to whole authoring pixels.
The catalog's name and `asset_type` are validated only as source structure; they
never become authoring identity or classification. SceneMaker never discovers
packages by scanning directories and never reads a sibling PolyTools project at
runtime.

## Authoring ownership

SceneMaker's navigation is explicit and stable: it does not grow, shrink or
rename its authoring areas from whichever Assets a PolyTools export happens to
contain. SceneMaker owns its author-facing names, roles, colors, surface tokens
and authoring kinds. That lets the River tool offer `Water`, `Lava` or `Mud`
without borrowing the name or type of a PolyTools package. A PolyTools
`asset_type` is never translated into a SceneMaker role; only a configured
Placement requests geometry with the same `asset_key`.

The editor calls spatial map objects **Placements** because that is what an
author does with them. Internal types such as `PropDocument`, the
`EditorMode.Props` enum member and the persisted `props` field remain unchanged
for now, avoiding a schema and consumer migration for terminology alone.
Transitions are not being inferred from PolyTools Props or reintroduced as a
second identical record. They get their own SceneMaker model only when the
simulation has a concrete transition contract it can consume and test.

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
surface token whose meaning it never reads. It is what an authoring area offers
its Assets by, and it makes two mistakes impossible: a river painted cell by
cell, and a river made of grass.

The editor asks that question of the area rather than of the Asset. Terrain,
River, Path and Hill are separate areas because they author different geometry.
Terrain offers the cell-authored Assets; River offers the curve-authored Assets
in its `Surface` field; Path may present any Terrain Asset on an independent
route band; Hill authors shape and height only and so has no material to offer.
It used to run the other way, with the chosen Asset swapping the tool out from
under the author, which meant choosing a river silently ended a hill contour
being drawn. Now an Asset is only the material: changing it keeps the geometry,
and only leaving the area gives it up - out loud.

Where that material is asked for follows from what the area draws. Painting
cells means switching Assets constantly, so Terrain carries them as a palette
of chips above the canvas. Drawing a curve means choosing one Asset for the
body being pulled out, once, beside its width and its heights - so River
carries it as a `Surface` field in that tool's context bar, and there is no
second navigation row repeating the area's own name. The rule is the authoring
kind rather than a list of areas, which is why a later closed-curve area would
get the field for the same reason. With one offered Asset the field still
stands and cannot be opened: an area whose shape changes with how many Assets a
Workspace happens to enable would be a different tool in every Workspace.

None of this says water. A corridor carries whatever curve-authored Asset the
Workspace offers - water, lava, mud - and what that means is the runtime's
question: SceneMaker never reads the Asset's `surface` token, so the fields say
`Surface` and `Surface level` rather than naming one of them. The stored types
are still `water_bodies` and `water_raster`, which is document and export
format and a separate question from what the editor calls its fields.

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
segments live in `BezierChain`, which knows nothing about rivers; a hill
outline and a route flatten the same way. `OpenChainCorridor` adds only the
horizontal band around such an open chain: unrounded width over arc length,
square caps local to the two outermost segments, bounds and the nearest station
when two stretches reach the same position. It has no material, vertical or
raster meaning. `WaterGeometry` converts its own points into that geometry,
rasters the band at water-grid resolution and supplies the vertical section. A
later inclined route may reuse the band without becoming a sequence of Terrain
steps. Each authored kind keeps its own document record and converts into the
shared geometry - one shared record instead would force every kind to carry the
others' fields and tie schemas together that have no reason to change at the
same time. A closed loop is not an open chain with its ends joined: it has no
first and last point to clamp against, its stations wrap, and it needs an
orientation before anything can be said about its inside, so it gets its own
entry point rather than flowing quietly through code that clamps.

`RouteSurfaceGeometry` is the first consumer of that horizontal band besides
water. A route point combines an open-chain point with a width and an absolute
support height. Preparing it interpolates both over centerline arc length and
reports the signed rise per metre of every authored interval. The intermediate
height remains a double and is not snapped back to the Workspace elevation
quantum: the quantum constrains authored support heights, whereas the surface
between them is continuously inclined. Grade is only a geometric measurement;
an Actor profile or generator may compare it with its own limit later.

Authoring schema 13 introduced that source as `route_surfaces`; schema 15 adds
a stable segment ID and exact integer `grade_percent` to every interval, and
schema 16 adds an explicit additive/subtractive operation plus positive
clearance for a subtractive interval. A route carries
an Asset whose role is Terrain, regardless of whether that Asset is normally
authored as cells or as a curve; `authoring` chooses a UI, not eligibility as a
surface. Its points stay inside the Scene and their heights obey the vertical
quantum, but their horizontal positions do not snap to either raster. Equal
neighbour positions are valid when their Bezier handles create real arc length,
which a generated loop may need. Templates may persist routes, while a runtime
whose composition cannot translate them must reject them explicitly. Export
schema 10 carries both authored routes and their runtime bake. `Draw Path` is
exposed beside River and Hill under Landscape;
its context bar carries Surface, point mode, width and the shared absolute
Height. The canvas draws the continuous material-coloured band and its
centerline, while the height view colours that band from its interpolated
surface elevation rather than rasterizing it into steps. The eraser removes
the whole authored route under the pointer.

Subtractive authoring deliberately leads its runtime contract by one focused
slice: export schema 10 rejects such a segment instead of silently serializing
it as an additive Path. Terrain cutting and the matching export version follow
next.

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
does: is its centre inside. A hill's edge and a river's edge therefore
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

An elevation region keeps that closed contour and one absolute top elevation
in the Scene document, and nothing else. It carries no material. Painted
Terrain decides two things an elevation region never does: whether there is a
cell at a coordinate at all, and what its surface is made of. A region decides only how
high that cell's solid column reaches. So a contour drawn around a stretch of
sand and grass lifts that pattern onto the hill unchanged, and Terrain
painted afterwards under an existing contour comes up already raised - the same
one rule read from either side. Where nobody painted, a contour produces no
cell: there is no column to raise and no material to invent one from. Such a
body is valid and stays authored; it simply has no effect yet.

Its raster is derived rather than stored. Painted Terrain and every region
contribution form the same solid column: the highest top wins, so a second
contour at 15 m inside one at 10 m is a hill on a hill without an offset
or parent link. A lower contour never cuts existing Terrain down. Two bodies
that meet at one height are no conflict - a height is a number and both name the
same one. Overlap used to be refused when the two bodies named different Assets,
because geometry supplied no winner for the material; with the material gone
from the body, so is the question.

A hill point's anchor is checked at the IO boundary against the two rules
`Draw Hill` authors by: it lies inside the Scene and on the Terrain grid.
Its handles are not. A handle shapes the curve rather than naming a place, so
snapping it would snap the shape, and the curve it shapes may bulge past the
edge of the map - the raster stops at the Scene, the document does not fail.
How many points a contour needs is not a count either: two anchors whose
handles bow the closing edges apart already enclose an area, so the document
rule stays at two and `ElevationRegionGeometry.RequireContour` decides whether the
flattened ring is usable. That the canvas tool asks for three placed points is
a tool decision about drawing, not a rule about geometry.

Elevation-region contours are editor source and do not enter export schema 10. Export
folds them into the ordinary Terrain cells promised to the runtime. This also
keeps hills in the cuttable Terrain solid: a river cut can make a channel
or tunnel through one, whereas modelling a hill as a fill would incorrectly
make it immune to cuts. Scene Templates cannot carry elevation regions until
composition knows how to translate their source contours.

The `Draw Hill` tool is the authoring entry point for those regions. It
needs no Asset and is therefore always available in the Hill area.
It uses the same `ToolInteraction -> ToolOutcome` path as every other tool:
points snap to the Terrain grid, handles remain unsnapped, Enter closes the
contour as one undoable body, and Escape or draft undo removes one point. Point
modes are per next point and the aligned automatic handles are cyclic, so the
closing Bezier edge has no special endpoint behaviour.

`Select Hill` edits that authored truth instead of its derived cells. A
click on a body selects the topmost contour covering the visible Terrain cell;
an authored point within a screen-sized hit radius takes precedence and begins
a drag. The point snaps to the Terrain grid while both handle offsets, body ID
and absolute top remain unchanged. A selected point exposes the shared `Point`
field: Linear removes its handles, while Aligned creates cyclic automatic
handles from its two neighbours. Either handle tip can then be dragged without
snapping. The opposite tip turns with it but keeps its own length, which is the
meaning of Aligned in the stored document. During a drag the live contour uses
the same reshape check as pointer release: a valid candidate keeps the body's
palette colour, an invalid one turns red and is not committed. A mode change is
checked before its edit is offered and is likewise refused with its reason.
Point moves, handle moves and mode changes are each one undoable edit.
The selected body's `Height` field edits its absolute top through the same
Workspace elevation quantum used while drawing; it leaves ID and contour
untouched and is one undoable edit. Lowering it below painted Terrain or another
body is allowed and simply makes it ineffective in those cells, because a
hill raises columns and never cuts them down.

The draft has three states and they answer one question - what would Enter do.
Too few points is Incomplete and drawn neutrally, because a contour that is not
finished being asked for is not a contour that was refused. Ready is a promise:
the whole attempt has already been made against this Scene and height by
`ElevationRegionEditing.TryPlace`, which is the same call the commit makes, so a ready
contour cannot then be refused and a refused one cannot slip through. Everything
else is Blocked and carries the reason. Ready is not the same as visible: a
contour over unpainted ground is a valid body and stays Ready, and says in the
same breath that it currently raises no Terrain cells. Refusing it would refuse
a legitimate order of work - contour first, paint second - and staying silent
would let an author take an empty preview for a broken tool.

A finished region also carries a contour on the canvas, derived from its points
and drawn in a colour taken from a small editor palette by a stable rule over
its `elevation_region_id`. Both are derived: neither the contour nor the colour is
in the Scene document or the export, and neither means anything to a consumer.
It exists because folded cells cannot say where one region ends - two hills
over the same painted Asset are one surface without it - and the height view
should stay an analysis rather than the only way to see a boundary.

The eraser asks about the Terrain cell under the pointer rather than the exact
position in it, because the raster asks about a cell's centre and two different
sample points would let the body the author sees filled and the body the click
removes differ by half a cell near an edge. `ElevationRegionEditing.FindAtCell` answers
once for both the hover highlight and the removal, so what lights up is what
disappears: the whole topmost body over that cell, never a single derived cell.
What the highlight fills is the cells that body is actually lifting -
`ElevationRegionGeometry.CellsRaisedBy`, the same definition read backwards - rather
than everything its ring covers, because a cell that another body holds higher,
or that nobody painted, does not drop when this one goes. Its outline is drawn
too, so the body being erased stays recognisable even where it lifts nothing.

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
`elevation_quantum_meters`: the Scene default, Terrain and Prop elevations,
water-point surfaces, and a Path's starting height share that vertical
authoring grid. It does not quantize widths, vertical extents such as channel
depth and clearance, or later Path anchors derived from grade and horizontal
arc length. Nor does it turn interpolation into steps. A water cell or derived
Path point may therefore have a surface that is not itself a multiple of the
quantum.

The ordinary Canvas keeps Asset hue as material identity and multiplies it by
an engine-neutral elevation-lighting answer from the Editor layer. An absolute
surface at `1 m` is the fixed 45% anchor; represented elevations below it fade
toward a 25% floor, while the complete Scene's highest represented surface
above it reaches 100%. Terrain after hill folding, water surfaces, Path
triangles and Props all use that rule. The analytical height view remains a
different projection and replaces Asset colour with its blue ramp. The range
belongs to the complete document rather than a future clipped Section result,
so moving a clipping plane cannot relight everything left below it.

Cuts apply to Terrain and never to fills. That one sentence is what will let a
bridge deck cross the river it spans without the river carving it away.

`LayeredSceneColumns.Prepare` is the engine-neutral implementation of that
vertical rule for Terrain and authored water. It folds Elevation Regions once,
rasters every water corridor once, merges overlapping cut intervals without
depending on body order, subtracts them from the Terrain solid and retains
every fill independently. A column can then answer the top surface in the
unlimited ordinary view or after a finite horizontal clip: when the plane lies
inside a span the plane is its section face, and when it lies in a void the
highest remaining boundary below it is visible. Exact Terrain/fill ties choose
the fill. Spans are closed at both ends, matching the export contract. The clip
removes only geometry strictly above its plane: exactly at a fill bed the fill
wins its tie with the floor, and exactly at a cut top the Terrain roof boundary
remains as a section face. This is also why clipping exactly at a hill top keeps
the hill visible. The current lookup quantizes an arbitrary authoring position
to the Workspace water grid because that is the finest authored volumetric
raster; the authoring-position entry keeps the query engine-neutral for later
continuous Path cuts.

The Canvas now exposes that answer as a third, transient presentation beside
the ordinary Asset view and the blue height map. `CanvasViewState` owns the
mutually exclusive choice and the Section elevation; the App only snaps the
number to the open Workspace quantum and projects `VisibleAt` at water-raster
resolution. Moving the plane never changes the complete-document lighting
range. Prepared columns memoize each resolved cell because a redraw asks the
same immutable Scene repeatedly.

The interface labels that transient elevation `Cut at`, not `Height`: `Height`
remains the authoring elevation and is hidden while the Section view is active.
The height map's water-boundary selector is contextual too; `Surface`, `Bed`
and `Cut top` appear in the ContextMenu only while the height map is active,
not as a third permanent control in the right-hand ToolOptionsBar.

Section has two transient inspection shapes. `Cut at` is the original upper
plane. `Cut between` stores a start and a positive offset and inspects the
inclusive band `[start, start + offset]`; moving start therefore translates the
whole band without editing a second absolute endpoint. After clipping at the
upper boundary, a resolved surface below start is omitted. Both controls step
on the Workspace elevation quantum, but neither enters a document or export.

The horizontal Section draws only what the shared column model resolves:
painted Terrain after Elevation Regions, water cuts and fills, and additive
Path surfaces. `RouteSurfaceRaster` samples the existing runtime bake at
water-cell centres, so Section uses the same triangles, joins, caps and
continuous elevation that the normal Canvas and export already carry. A Path
is a zero-thickness independent surface: it wins an exact tie with Terrain,
remains separate from fills, can be stacked at one X/Y, and stays occluded by a
higher solid. Props are still omitted rather than drawn over roofs with invented
occlusion. Technical authoring overlays and active previews stay visible so
choosing the view never disables a tool.

A Path is an independently materialized open route rather than a sequence of
raised Terrain cells. It can describe a level way, a ramp or a descent. Its
first point carries a directly authored absolute surface height. Each later
draft point chooses the grade of the segment arriving there from exactly
`-50%`, `-25%`, `0%`, `+25%` or `+50%`; Core derives its absolute height from
that grade and the segment's horizontal Bezier arc length. The document stores
those resulting absolute heights, not the grade. Stored derived anchors are
rounded to six decimal places only after an unrounded running height has been
accumulated, so adding points cannot accumulate file-precision drift. Every
point also carries a full width; height and width are interpolated continuously
over Bezier arc length.
The point positions are deliberately free in plan instead of snapping to the
Terrain grid. `RouteSurfaceEditing` authors and removes the body, while
`ToolInteraction` owns the unfinished open curve and makes the preview and
Enter use the same prepared geometry. Ascending and descending routes are the
same operation with opposite incoming grades. The first click normally copies
the effective Terrain top under it, after hills have been folded in. That is a
one-time snap: the Path keeps the number and never follows later Terrain edits.
Where no Terrain exists, or when the author deliberately wants another level,
a session-only manual start override supplies it instead. Changing grade or
width affects the next point; changing the general Height field does not.

The initial width is 2 m because the first target Actor has a 1 m collision
radius. That is a convenient authoring default, not a statement that the route
is traversable: tight inner curves and a future Actor profile still need their
own clearance test. The editor therefore validates representable geometry and
the Workspace height quantum for the start only. Its five grades are authoring
choices, not Actor capabilities: SceneMaker derives their geometry but does not
reject a valid Path because a particular Actor could not climb it. Export
schema 10 exports route surfaces without assigning traversal policy.

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
Later consumers receive a small generic JSON export and resolve its stable
`asset_key`s in their own content boundaries. Each export is a versioned
snapshot of one Scene, the Workspace grid, and derived enabled Asset profiles;
editor colors, author-facing display names and raw PolyTools documents are not
exported.

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
