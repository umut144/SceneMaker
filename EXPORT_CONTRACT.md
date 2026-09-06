# SceneMaker export contract

For consumers of the export — the Bevy runtime in `BevyProjects/world01` above
all. Everything a reader needs in order to load a map and compose it is here;
nothing else in this repository is part of the contract, and the authored
`scenes/`, `templates/` and `config.json` documents are explicitly not.

Current schemas: **export 12**, embedded **scene 13**. A reader must reject any
other version rather than guess. There is no migration path in either
direction; see the schema section of `AGENTS.md` for why.

Export 12 carries bridges: the authored record in `scene.bridges`, and a
`bridge_bakes` array beside the Scene holding each deck's triangles and the
four posts standing at its corners.

Export 11 carried excavating Paths. Every route segment now states its
`operation` and, when it excavates, the `clearance_above_meters` it asks for,
and a new `route_surface_cut_raster` array delivers the cells that excavation
removes from the Terrain. Export 10 refused such a Scene outright rather than
writing it through the additive shape; it is the version that could not say
what a tunnel means, not a version whose meaning changed.

The authored Scene currently has its own schema 16. It is deliberately newer
than the embedded Scene: elevation-region contours are editor source, folded
into the ordinary `terrain_cells` below and omitted from export. Authored route
surfaces remain independent continuous bands and are exported separately from
that folded Terrain.

## What is on disk

`workspaces/<key>/exports/` holds one file per Scene, named
`<scene_id>.scene_export.json`. Exporting from the editor writes all of them at
once; `scripts/export_scene.sh <workspace> <scene-id>` rewrites a single one.

**Every file in that directory carries the same `version`.** The directory is
one set, written by one export, and a consumer reads it as one: a mixed set is
a half-finished export rather than something to be tolerated. Exporting from
the editor writes them all; the single-Scene export exists for a surgical swap
between two runs of the same version, never for straddling two.

There is no index. The directory is the list, and every file says what it is —
so a consumer finds the Templates by reading `exports/` and keeping the files
whose `scene.scene_kind` is `"template"`. An index was considered and rejected:
the single-Scene export exists precisely for a seasonal swap, and it would leave
an index stale, which is worse than none. Copy the directory rather than named
files and completeness follows by construction.

Every file has the same shape. `scene.scene_kind` tells the two kinds apart:

- `"instance"` — an authored map. This is what a session loads.
- `"template"` — a small finished Scene meant to be placed into an Instance at
  a Template Anchor. It carries `scene.template_definition` with its group
  number and insertion anchor.

A reader should index every `"template"` file by
`scene.template_definition.group_number`; several Templates share a group on
purpose.

## Document shape

```jsonc
{
  "format": "scene_maker_scene_export",
  "version": 10,
  "workspace_key": "world01",
  "grid": {
    "terrain_cell_meters": 1.0,        // edge length of one Terrain cell
    "authoring_pixels_per_meter": 32,  // unit of every *_authoring_px value
    "game_pixels_per_meter": 192,      // the consumer's own render scale
    "water_cell_meters": 0.5           // edge length of one water cell
  },
  "asset_profiles": [                  // every Asset the Workspace enables
    {
      "asset_key": "grass",
      "surface": "land",               // Terrain only
      "footprint_meters": null,        // null for Terrain Assets
      "anchor_meters": null
    },
    {
      "asset_key": "ankh",
      "surface": null,                 // null for everything that is not Terrain
      "footprint_meters": { "width": 1.0625, "height": 1.71875 },
      "anchor_meters":    { "x": 0.53125, "y": 0.3125 }
    }
  ],
  "water_raster": [                    // derived from scene.water_bodies
    {
      "water_body_id": "river_0001",
      "water_kind": "river",
      "asset_key": "river",
      "cells": [                       // water cells, not Terrain cells
        {
          "x": 64, "y": 20,
          "bed_meters": 1.5,           // floor of the channel
          "surface_meters": 2.0,       // top of the water
          "cut_top_meters": 7.0        // Terrain is removed up to here
        }
      ]
    }
  ],
  "route_surface_bakes": [             // derived from scene.route_surfaces
    {
      "route_surface_id": "route_0001",
      "asset_key": "grass",
      "vertices": [
        { "x_meters": 1.0, "y_meters": 2.0, "elevation_meters": 1.0 },
        { "x_meters": 5.0, "y_meters": 2.0, "elevation_meters": 2.0 },
        { "x_meters": 5.0, "y_meters": 0.0, "elevation_meters": 2.0 },
        { "x_meters": 1.0, "y_meters": 0.0, "elevation_meters": 1.0 }
      ],
      "triangle_indices": [0, 1, 2, 0, 2, 3],
      "boundary_edges": [
        { "start_vertex_index": 0, "end_vertex_index": 1 },
        { "start_vertex_index": 1, "end_vertex_index": 2 },
        { "start_vertex_index": 2, "end_vertex_index": 3 },
        { "start_vertex_index": 3, "end_vertex_index": 0 }
      ],
      "centerline_samples": [
        {
          "x_meters": 1.0,
          "y_meters": 1.0,
          "elevation_meters": 1.0,
          "width_meters": 2.0,
          "station_meters": 0.0,
          "authored_point_index": 0
        },
        {
          "x_meters": 5.0,
          "y_meters": 1.0,
          "elevation_meters": 2.0,
          "width_meters": 2.0,
          "station_meters": 4.0,
          "authored_point_index": 1
        }
      ],
      "segments": [
        {
          "segment_id": "route_0001.segment_0001",
          "grade_percent": 25,
          "operation": "additive",         // or "subtractive"
          "clearance_above_meters": null,  // set only for a subtractive segment
          "start_point_index": 0,
          "end_point_index": 1,
          "start_sample_index": 0,
          "end_sample_index": 1
        }
      ]
    }
  ],
  "route_surface_cut_raster": [        // derived from subtractive segments
    {
      "route_surface_id": "route_0002",
      "cells": [                       // water cells, as water_raster uses
        {
          "x": 64, "y": 20,
          "segment_id": "route_0002.segment_0001",
          "floor_meters": 1.0,         // the Path surface, which survives
          "cut_top_meters": 3.0        // Terrain is removed up to here
        }
      ]
    }
  ],
  "bridge_bakes": [                    // derived from scene.bridges
    {
      "bridge_id": "bridge_0001",
      "asset_key": "planks",           // the deck's Terrain Asset
      "vertices": [
        { "x_meters": 10.0, "y_meters": 12.0, "elevation_meters": 1.125 }
      ],
      "triangle_indices": [0, 1, 2, 0, 2, 3],
      "boundary_edges": [
        { "start_vertex_index": 0, "end_vertex_index": 1 }
      ],
      "posts": [
        {
          "post_id": "bridge_0001.post_start_left",
          "corner": "start_left",      // or start_right, end_left, end_right
          "asset_key": "rope_post",    // the anchor Asset
          "x_meters": 10.0,
          "y_meters": 12.0,
          "elevation_meters": 1.125
        }
      ]
    }
  ],
  "scene": {
    "schema": "srt.scene_maker_scene",
    "version": 11,
    "scene_id": "overworld01",   // names the map, not the Workspace
    "scene_kind": "instance",
    "coordinate_space": "scene_local_bottom_left_y_up",
    "size_cells": { "width": 100, "height": 100 },
    "terrain_cells": [                 // painted Terrain plus folded hills
      { "x": 0, "y": 0, "asset_key": "grass", "elevation_meters": 1.0 }
    ],
    "props": [
      {
        "instance_id": "tree_0001",
        "asset_key": "tree",
        "position_authoring_px": { "x": 1088, "y": 2624 },
        "elevation_meters": 1.0
      }
    ],
    "water_bodies": [                  // the authored curves themselves
      {
        "water_body_id": "river_0001",
        "water_kind": "river",
        "asset_key": "river",
        "points": [
          {
            "position_authoring_px": { "x": 1024, "y": 320 },
            "mode": "aligned",         // or "linear", which zeroes both handles
            "handle_in_authoring_px":  { "x": -64, "y": 0 },
            "handle_out_authoring_px": { "x": 64, "y": 0 },
            "elevation_meters": 2.0,       // water surface, absolute
            "channel_depth_meters": 0.5,   // bed below it
            "clearance_above_meters": 5.0, // headroom required above it
            "width_meters": 8.0            // full corridor width here
          }
        ]
      }
    ],
    "route_surfaces": [
      {
        "route_surface_id": "route_0001",
        "asset_key": "grass",
        "points": [
          {
            "position_authoring_px": { "x": 32, "y": 32 },
            "mode": "linear",
            "handle_in_authoring_px": { "x": 0, "y": 0 },
            "handle_out_authoring_px": { "x": 0, "y": 0 },
            "elevation_meters": 1.0,
            "width_meters": 2.0
          },
          {
            "position_authoring_px": { "x": 160, "y": 32 },
            "mode": "linear",
            "handle_in_authoring_px": { "x": 0, "y": 0 },
            "handle_out_authoring_px": { "x": 0, "y": 0 },
            "elevation_meters": 2.0,
            "width_meters": 2.0
          }
        ],
        "segments": [
          {
            "segment_id": "route_0001.segment_0001",
            "grade_percent": 25,
            "operation": "additive",
            "clearance_above_meters": null
          }
        ]
      }
    ],
    "bridges": [
      {
        "bridge_id": "bridge_0001",
        "asset_key": "planks",         // Terrain Asset, the deck surface
        "anchor_asset_key": "rope_post", // the Placement standing at each corner
        "start_authoring_px": { "x": 320, "y": 320 },
        "end_authoring_px":   { "x": 640, "y": 320 },
        "width_meters": 4.0,
        "elevation_meters": 1.125      // the deck, level for the whole span
      }
    ],
    "template_definition": null,       // set only when scene_kind is "template"
    "template_anchors": [
      {
        "anchor_id": "template_anchor_001",
        "group_number": 1,
        "position_authoring_px": { "x": 1088, "y": 2624 }
      }
    ],
    "default_elevation_meters": 1.0    // what a newly authored cell takes
  }
}
```

A Template file differs only in `scene_kind`, an empty `template_anchors`, and:

```jsonc
"template_definition": {
  "group_number": 1,
  "insertion_anchor_authoring_px": { "x": 0, "y": 0 }
}
```

## Surfaces

`surface` is the domain a simulation reasons about: `"land"`, `"water"`, and
whatever a consumer adds later. It is an open lower_snake_case token, not an
enumeration, so a new surface never breaks the schema. SceneMaker checks the
shape of the token and never its meaning.

It sits on the Asset, not on the cell. A consumer already reads `asset_key` for
every Terrain cell, so it joins the surface through that key — one entry instead
of one per cell, and a Terrain Asset that presents land in one place and water
in another is structurally impossible.

`surface` is present and non-null for every Terrain Asset and null for every
other kind. A cell whose Asset has no surface cannot occur: only Terrain Assets
may be painted as Terrain.

Passability is not in the export and is not SceneMaker's business. The Actor
brings its domains, the cell brings its surface, and the simulation intersects
them.

## Water

Water is authored as curves and delivered as both: the curves in
`scene.water_bodies`, the cells they cover in `water_raster`. The raster is what
a simulation reads. The curves are there so that a consumer that would rather
build a smooth band than march the raster can, and so that the two can never
drift apart - SceneMaker derives one from the other on every export.

`water_kind` is `"river"` today. A river is an **open** curve: its first point
is the source, its last is the mouth, and that is the whole of its flow
direction. Nothing else in the export says which way the water runs.

The curve is a chain of cubic Bezier segments. Between two consecutive points
`a` and `b` the four control points are

```
P0 = a.position
P1 = a.position + a.handle_out
P2 = b.position + b.handle_in
P3 = b.position
```

Positions sit on the water grid; handles do not, because a handle is a curve
control rather than a place. A point in `"linear"` mode carries two zero
handles, which makes both of its segments straight.

**The corridor rule.** `width_meters` is authored on every point and interpolated
linearly over the same centerline arc length as the vertical profile. A water
cell projects its centre onto each candidate segment. It belongs to the body
when some candidate projection lies no further than half the width at that
projection's station; among the accepted projections, the nearest supplies the
profile. The two outermost segments carry the end caps: the disc around the
first one is clipped by the line perpendicular to the curve at the source, the
disc around the last one by the line perpendicular at the mouth. Without them a
river would begin and end with a half-circle. Inner bends round off by
construction.

The caps belong to their own segment and to nothing else. A cap applied to the
whole corridor would reach across the map and cut away whatever part of the
river happens to lie behind it, which is a real shape - a river that bends back
near its own mouth - and not an error the author could see coming.

## Height is a stack

A place is not one height. `terrain_cells[].elevation_meters` is **the top of a
cell's solid column**, not necessarily a walking surface: a spatial element can
take a range out of that column and put something else there. A column is
therefore resolved rather than read.

Each curve point carries three absolute heights. From them follow two spans that
share a floor:

```
bed      = elevation_meters − channel_depth_meters
water    = [bed, elevation_meters]                       a fill
cut      = [bed, elevation_meters + clearance_above_meters]   removed from Terrain
```

`water_raster` delivers exactly those, per water cell, as `bed_meters`,
`surface_meters` and `cut_top_meters` — three numbers because the two spans
always share their floor.

**Resolving a column.** For a water cell, take the Terrain cell it lies in
(`water cell ÷ water_cells_per_terrain_cell`) and:

```
solid = (−∞, terrain_top]
solid = solid \ every cut that covers this cell
column = solid together with every fill that covers this cell
```

Every maximal run of `solid` presents a walking surface at its top, carrying the
surface of that Terrain cell's Asset. Every water fill presents a water surface
at its top, with its floor at `bed_meters`.

**Cuts apply to Terrain and never to fills.** A bridge deck over a river is a
fill inside that river's cut, and it has to survive it.

Worked example, a river crossing a hill that reaches 10 m, with water at
2.0 m, a channel 0.5 m deep and 5.0 m of headroom:

```
bed 1.5, cut [1.5, 7.0], water [1.5, 2.0]
column: solid (−∞, 1.5]   floor of the tunnel, walking surface at 1.5
        water [1.5, 2.0]  water surface at 2.0
        air   [2.0, 7.0]  the headroom that was asked for
        solid [7.0, 10.0] the hill above, walking surface at 10.0
```

The same three numbers against flat ground at 1 m leave nothing above the cut,
and the river is simply open: the corridor is carved down to its bed and filled
to its surface. An open river, a cut channel and a tunnel are one rule against
different ground, and the Terrain is never lowered for any of them — a cut is a
statement by the water body, not an edit to the height field.

**Interpolation.** The three values are given at the authored points and are
linear in **arc length** along the flattened centerline between them, clamped to
the outermost point beyond either end. A cell takes the values at the point where
its centre projects onto the centerline; where two stretches both reach a cell,
on the inside of a bend, the nearer one wins. Across the width the values are
constant, because a water surface is level across a river.

Arc length rather than the curve's own parameter: the parameter runs unevenly, so
a gradient would depend on how long the author made the handles that shape the
bend. Linear rather than a spline: a spline can overshoot, and an overshoot in a
water surface is a stretch of river running uphill between two points that both
fall.

**Precision.** The three heights this file carries per water cell are rounded to
the millimetre, because they are written down and a file that says 1.4999999999
one day and 1.5 the next is a file nobody can diff. The **width is not rounded**:
it is never written anywhere, it only decides which cells belong to the body, and
rounding it first would widen the corridor by up to half a millimetre - enough to
pull in a whole row wherever half the width falls exactly on a cell centre's
distance. A reader recomputing the raster has to interpolate the width at full
precision to arrive at the same set.

That millimetre precision belongs to the derived raster, not to authoring.
Directly authored absolute heights align to the Workspace's
`elevation_quantum_meters`, which is not exported; a consumer receives the
actual heights. Depth, clearance, values interpolated between valid authored
points, and later Path anchors derived from grade need not align to that
quantum. Only a Path's starting height is chosen directly on the quantum.

Heights are absolute. Nothing stores a relationship to the ground, so repainting
Terrain under a river never moves the water.

Bodies may overlap - that is how a widening river or, later, a river running
into a lake is authored. **A simulation reads their union.** Cells are not
deduplicated across bodies, and a cell listed twice is water once.

The raster is clipped to the Scene: a river drawn over the map edge simply
stops being authored there rather than failing to export. Terrain under a river
is untouched and stays whatever it is. Which of the two a position is depends on
which grid is asked - the water grid is finer and answers for its own cells -
and that is a resolution question, not the two-surfaces-at-one-place case a
bridge poses further down.

A Scene Template carries no water. Composition moves Terrain cells and Props
and nothing else, so a Template with a river would lose it at every Anchor;
authoring one is refused instead.

## Paths and route surfaces

A Path is an independent surface and never overwrites or folds into a Terrain
cell. Terrain and one or more Paths may occupy the same X/Y at different
heights. Its `asset_key` selects the material/surface it presents; every point
stores absolute `elevation_meters` and `width_meters`.

`scene.route_surfaces` preserves the authored cubic Bezier chain. Consecutive
points form segments in deterministic array order. Every matching
`segments[]` entry stores a stable `segment_id` and the exact authored integer
`grade_percent`, one of `-50`, `-25`, `0`, `25`, or `50`. A reader must consume
that integer and must not reconstruct it from rounded point heights and
floating-point arc lengths. SceneMaker assigns no passability or speed meaning
to those values.

**Excavating segments.** A segment's `operation` is `"additive"` or
`"subtractive"`, and it is the authored meaning rather than something recovered
from whether Terrain happens to overlap the Path today. An additive segment
materializes its surface and removes nothing, so its `clearance_above_meters`
is null. A subtractive segment carries a positive clearance and, at every
station, removes `[floor, floor + clearance]` from the Terrain solid, where the
floor is the interpolated Path elevation. The Path surface itself survives the
cut, and the cut applies to Terrain alone: it never removes water, another
Path, or any other fill.

A reader must not infer that meaning from geometry, and it must not treat a
subtractive segment as an additive one. Both halves of the export state it: the
authored segment in `scene.route_surfaces` and its baked counterpart in
`route_surface_bakes`.

`route_surface_cut_raster` is the derived half of that excavation, and the
easiest way to consume it. It holds one entry per Path that carries at least
one subtractive segment - a purely additive Path is absent rather than present
and empty - and lists the cells that Path removes, on the same water grid
`water_raster` uses, because that is the finest authored volumetric raster.
Each cell names the segment that asked for it, the `floor_meters` that survives
and the `cut_top_meters` up to which Terrain is gone. Resolving a column is
then the water rule with one more source of cuts.

SceneMaker derives those cells rather than leaving them to a consumer because
of what happens where two neighbouring segments disagree. At an authored
operation transition the existing round-join disc of radius `width_meters / 2`
is the portal neighbourhood, and inside it one bisecting plane gives each
segment only its own side; the plane is closed on both sides, so a subtractive
neighbour opens an exactly centred cell instead of leaving a Terrain plug, and
two subtractive neighbours may apply different clearances on their respective
sides. A consumer reimplementing that from the triangles alone would disagree
with what the author inspected in the Section view. The cells are the answer,
so it cannot.

An empty `cells` array on a Path that does carry a subtractive segment is
valid: the excavation lies outside the Scene, exactly as the water raster is
clipped to it. The export warns in that case rather than refusing.

`route_surface_bakes` is the derived runtime half. It is generated by the same
Core operation used to draw persisted Paths in the Canvas: the shared Bezier
flattening, unrounded width interpolation, square outer caps, one quad per
flattened segment and a 16-sided round join at every interior flattened sample.
Vertices use scene-local metres and carry absolute elevation. Triangle indices
are a flat list of triples. `boundary_edges` explicitly lists each primitive's
edge loop, so a runtime need not infer outline topology from overlapping union
primitives. Centerline samples include every authored point/grade transition;
`authored_point_index` is null only for subdivision samples. Their
`station_meters` is cumulative horizontal arc length.

The authored and baked arrays have identical Path order and identity. Baked
segment records repeat the stable ID, grade, operation and clearance and map
each authored point pair onto an inclusive centerline-sample range. Export refuses missing or duplicate
IDs, unsupported grades, non-positive widths, non-finite values, invalid
indices, and degenerate baked triangles. Output order never comes from a hash
map.

Authoring schema 15 permits route surfaces in both Instances and Templates so
the format does not silently lose them. A runtime whose Template composition
does not yet define Path translation must explicitly reject such a Template;
world01 does so in its first reader.

Heights stay absolute. Snapping the first point to Terrain is an editor action,
not a stored dependency, so later Terrain or hill edits never move the Path.

## Heights

`elevation_meters` on a Terrain cell is the top of its solid column, in metres,
in the same unit as everything else here. With no water over it that is also its
walking surface, which is what it always was; under a cut it is not, and the
section above says how to resolve it. Ground level in `world01` is `1.0`, water
`0.0`, a bridge deck `1.125`, a hill `2.0`. Authored Terrain cells may step
between such values, but those are terraces with vertical edges, not a smooth
ramp. Their values are authored, not derived, and their step is the Workspace's
authoring quantum. A consumer's simulation independently decides which
difference an Actor can traverse. A separately authored, continuously inclined
route is the independent continuous surface described above.

It is authored per cell, not per Asset, because one grass Asset covers valley
floor and hill alike. `default_elevation_meters` on the Scene is the height a
newly authored cell takes when the author names none; it is authored intent
about the Scene, like its size, and a consumer has no use for it.

A Prop carries its own `elevation_meters`, usually the height of the Terrain
under it. It is a separate field because a bridge deck sits above the water it
crosses - and that is the case that shows why a place does not have one
surface. Over a river there are two: `(water, 0.0)` for what swims and
`(land, 1.125)` for what walks across. Nothing in the export resolves that. The
Actor's domain picks the surface, the height difference decides whether the step
is possible, and both happen in the simulation.

## Bridges

A bridge is a straight level span. Six authored numbers say all of it - the
deck's Terrain Asset, the anchor Asset, two ends, a width and one height - and
everything else follows from them, which is why nothing else is stored.

Its deck is an independent surface exactly like a Path's: it never folds into a
Terrain cell, a cut never removes it, and Terrain and a deck may occupy the same
X/Y at different heights. `bridge_bakes` ships it as the same triangles a Path
does, generated by the same code, so a runtime needs no second way to stand on
something.

**The posts travel worked out.** Left and right mean a quarter turn
counter-clockwise from the start end towards the end end - in this y-up space,
the northern side of an eastward span. Deriving them here rather than in a
consumer is deliberate: a corner computed twice is a corner that can be
computed differently, and the author only ever saw one of the two.

A post is an ordinary Placement Asset at a worked-out position. It is derived
rather than authored, so it carries no instance ID from the Scene and cannot be
edited on its own: the bridge is what decides where it stands.

Post IDs are `{bridge_id}.post_{corner}` and use the dot the same way route
segment IDs do. An authored Placement ID is always `{asset_key}_{0000}` and
never contains one, so the two cannot collide.

`elevation_meters` on a post is the deck height its corner sits at. How far the
model reaches below or above that is the model's business.

**The bake is the authority.** A consumer places what `bridge_bakes` gives it
and derives nothing again from `start_authoring_px` and `width_meters`: those
are the authored source, kept so a later SceneMaker can reshape the bridge, and
recomputing corners from them is the one way to disagree with what the author
saw. The two do agree today - an end divided by `authoring_pixels_per_meter` is
exactly the midpoint of its two corner vertices, and their distance is exactly
`width_meters` - and that agreement is a consequence, not an invitation.

**A bridge deck is exactly one quad.** A bridge is straight and level, so its
bake holds four vertices, two triangles and one edge loop; `boundary_edges` is
therefore the outline of the walking surface and may be read as the edge one
falls from. That is a promise about bridges alone. In `route_surface_bakes` the
same field lists **each primitive's** edge loop - one per flattened segment
quad and one per round join - so a Path's union outline is not the
concatenation of them, and inner edges are in there too.

**A bridge cuts nothing.** It adds a surface at its own elevation over the
Terrain it crosses; the Terrain below is untouched and stays walkable, water
below stays water, and `route_surface_cut_raster` knows nothing about bridges
because there is nothing for it to know. Passing under a bridge is the point:
one place carries two surfaces, and which one an Actor uses is the
simulation's question.

The deck presents the surface of its `asset_key`, read from `asset_profiles`
exactly as a Terrain cell's is. A deck therefore names a Terrain Asset - the
one kind of Asset that carries a `surface` - and never a Placement.

A Scene Template carries no bridge, for the reason it carries no water:
composition moves Terrain cells and Props and nothing else, so authoring one is
refused rather than losing it at every Anchor.

## Coordinates and units

The origin is the lower left corner of the Scene, x to the right and y upwards.
That is what `coordinate_space` asserts; a reader that assumes y-down will
mirror every map.

Numbers are JSON numbers. Whether one is written `1`, `1.0` or `1.125` follows
from how the value was produced and says nothing about its kind: read every
`*_meters` field as a real number. In particular a deck height is not an
integer - it sits on the Workspace `elevation_quantum_meters`, which is
`0.125 m` for `world01`, so `1.125` is as ordinary a deck as `1`.

Three units appear, and only one of them is stored:

| Quantity | Stored as | Convert with |
| --- | --- | --- |
| Terrain cell | integer cell coordinates | — |
| Water cell | integer water cell coordinates | see below |
| Prop position, Anchor position, curve point | `*_authoring_px` integers | see below |
| Asset footprint and anchor | meters, in `asset_profiles` | — |

```
authoring_px_per_cell = terrain_cell_meters × authoring_pixels_per_meter
meters                = authoring_px / authoring_pixels_per_meter
game_px               = authoring_px × game_pixels_per_meter / authoring_pixels_per_meter
authoring_px_per_water_cell = water_cell_meters × authoring_pixels_per_meter
water_cells_per_terrain_cell = terrain_cell_meters ÷ water_cell_meters
```

Both of the last two are guaranteed to be positive whole numbers; the Workspace
refuses a grid where they are not. For `world01` they are 16 and 2. Water cell
`(wx, wy)` therefore covers the authoring pixels
`[wx·16, wx·16+16) × [wy·16, wy·16+16)` and lies in Terrain cell
`(wx ÷ 2, wy ÷ 2)`.

`authoring_px_per_cell` is guaranteed to be a positive whole number; the
Workspace refuses a grid where it is not. For `world01` it is 32.

A Prop's `position_authoring_px` is its PolyTools pivot, not a corner. The
footprint of a Prop in meters is therefore

```
lower_left = position_meters − anchor_meters
extent     = footprint_meters
```

with `anchor_meters` and `footprint_meters` taken from the Prop's entry in
`asset_profiles`. Both are derived from PolyTools geometry and rounded outward
to whole authoring pixels before being converted back to meters, so they are
never smaller than the visible asset.

## What the export guarantees

Validated before the file is written, so a reader may rely on it and should
treat a violation as a corrupt file rather than a case to handle:

- Every `asset_key` appears in `asset_profiles`.
- Every Terrain Asset in `asset_profiles` has a non-null `surface`; every other
  Asset has none.
- Every Terrain cell and every Prop carries `elevation_meters`. There is no
  cell without a height and no Prop without one.
- Authored elevation-region contours never appear in this file. They are folded into
  `scene.terrain_cells` as height alone: a covered cell keeps the `asset_key`
  the author painted, and its `elevation_meters` becomes the highest absolute
  top over it. An elevation region carries no material and creates no cell, so a contour
  over a coordinate with no painted Terrain exports nothing there.
- A `scene_id` names one Scene in the whole Workspace — never an Instance and a
  Template at once — and it does not change over the life of that Scene. The
  editor refuses to create a second Scene under an existing id, and an export
  refuses a Workspace whose ids collide rather than overwriting a file. A
  consumer may use it as a stable name across a reconnect.
- Every Terrain cell lies inside `size_cells`; no two cells share a coordinate.
- Every water cell lies inside `size_cells` measured in water cells; within one
  body no two cells share a coordinate. Across bodies they may.
- Every water body has at least two curve points, a positive `width_meters` on every point, and
  an `asset_key` that appears in `asset_profiles`. Every curve point sits on the
  water grid and inside the Scene, carries a positive `channel_depth_meters` and
  a non-negative `clearance_above_meters`.
- In every exported water cell, `bed_meters ≤ surface_meters ≤ cut_top_meters`.
- `water_raster` lists the same bodies as `scene.water_bodies`, in the same
  order, and each body's `cells` are what the corridor rule above produces from
  its curve. A reader may recompute them and must get the same set.
- `route_surface_bakes` lists exactly the same Paths as
  `scene.route_surfaces`, in the same order, with matching Path IDs, Asset keys,
  segment IDs and exact grades. Every Path has at least one segment.
- Path widths are finite and positive. Every grade is exactly one of
  `-50`, `-25`, `0`, `25`, or `50`. Every baked value is finite, every index is
  in range, and no emitted triangle is degenerate at export precision.
- Nothing is promised about Terrain under a Prop. A Prop carries an absolute
  `elevation_meters` and may stand over a hole, over water, or over nothing at
  all; a footprint with no cells beneath it is authored intent, not a defect.
  Whether an Actor can reach or stand on it is the consumer's question, and the
  export carries the heights it needs to answer it.
- `terrain_cells` and every body's `cells` are ordered by `y`, then `x`. `props`,
  `asset_profiles`, `scene.water_bodies`, `water_raster`,
  `scene.route_surfaces` and `route_surface_bakes` are ordered by their id,
  ordinal. Segment and sample arrays retain their deterministic chain order.
  This ordering is a checked invariant, not a
  coincidence, so a reader may binary-search it.
- Editor-only data is absent: authoring colours, PolyTools geometry, and the
  Workspace's own documents are never exported.

`asset_key` values are resolved by the consumer in its own PolyTools content
boundary. SceneMaker exports the key and nothing about how it looks.

## Placing a Template at an Anchor

An Anchor is a slot a Template can be placed into. **Which** Template goes into
which Anchor is not described here and is not SceneMaker's business: in the game
an Anchor is an event slot the server decides about and swaps during a session —
a dragon attack leaves lava, a dungeon entrance appears, a chest is gone. That is
a decision, not a draw.

SceneMaker's own preview does make a choice, seeded so it is reproducible, but
that is look development for the editor: it shows how a map *can* look. It is not
a specification, a consumer is not expected to reproduce it, and a map composed
differently is not a divergence.

What this contract does specify is the **geometry**: given an Anchor and a
Template, where exactly the Template lands.

```
translation_px    = anchor.position_authoring_px
                  − template.template_definition.insertion_anchor_authoring_px

translation_cells = translation_px / authoring_px_per_cell
```

The division is exact: both positions are validated to sit on the cell grid, so
their difference is always a whole number of cells.

From that:

- The Template's Terrain cells, each moved by `translation_cells`, are the cells
  the Template covers — its **mask**. Every masked cell must lie inside the
  Instance; a Template that would hang over the edge at that Anchor does not fit
  there.
- The Template's Props are moved by `translation_px`.
- Cell heights and Prop heights travel unchanged. An Anchor places a Template in
  x and y only; it never raises or lowers what it places, and it carries no
  height of its own.

**What happens to what was already there is policy, and policy belongs to the
consumer.** SceneMaker's preview lets Template Terrain overwrite the Instance's
and drops Instance Props whose footprint meets the mask, because for a preview
that is the simplest thing that looks right. A consumer is free to restrict this
further — world01 ranks what is placeable and keeps the higher rank, so a
Template tree does not replace a dungeon entrance and Template Terrain does not
erase a river. Such a rule is game meaning; the preview does not model it, and
the difference between the two is expected rather than a bug.

An Anchor with nothing placed at it is the ordinary case, not an authoring
error. The preview leaves it empty and composes the rest.

## Seasons

Templates are separate files so that one can be added, replaced or removed
without rewriting the map that uses it. Removing the last Template of a group
leaves that group's Anchors unfilled, which is a state the game is expected to
handle rather than a failure.

A Template's `scene_id` is how a consumer names it — world01 replicates an
occupancy as "Template X at Anchor Y" and that name has to survive a reconnect.
Reusing an id for different content therefore changes what an existing name
means. Add a Template under a new id instead of repurposing one.
