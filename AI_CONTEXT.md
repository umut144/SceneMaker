# SceneMaker context

SceneMaker is a standalone semantic scene authoring tool. A workspace is one
game and contains its own scene/template data and `config.json`.

Each Workspace consumes a synchronized copy of the current PolyTools Runtime
Export below `imports/polytools/`. PolyTools Catalog schema 1 is the closed
Asset set; the current Runtime Manifest schema 15 supplies geometry, hierarchy,
Asset References, and pivots. Legacy Runtime Manifest schema 14 remains
supported for compatibility. SceneMaker never discovers packages by scanning
directories and never reads a sibling PolyTools project at runtime.

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
centerline of Bezier points, a width and a surface height; the cells a
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
cell whose centre lies no further than half the body's width from the
centerline, cut off by the two lines perpendicular to the curve at its ends, so
a river starts and ends straight across instead of bulging into a half-circle.
Width belongs to the body, not to its points: a river that widens is authored
as a second river starting where the first one ends.

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

Cuts apply to Terrain and never to fills. That one sentence is what will let a
bridge deck cross the river it spans without the river carving it away.

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
