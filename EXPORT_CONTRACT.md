# SceneMaker export contract

For consumers of the export — the Bevy runtime in `BevyProjects/world01` above
all. Everything a reader needs in order to load a map and compose it is here;
nothing else in this repository is part of the contract, and the authored
`scenes/`, `templates/` and `config.json` documents are explicitly not.

Current schemas: **export 9**, embedded **scene 10**. A reader must reject any
other version rather than guess. There is no migration path in either
direction; see the schema section of `AGENTS.md` for why.

Export 9 differs from 8 in what it promises, not in what it contains: the
guarantee that every Prop footprint is covered by Terrain is withdrawn. The JSON
is byte-for-byte the same shape, which is exactly why the version had to move —
a reader that relied on the old promise cannot tell the two apart by looking.

The authored Scene currently has its own schema 11. It is deliberately newer
than the embedded Scene: mountain contours are editor source, folded into the
ordinary `terrain_cells` below and omitted from export. The embedded version
therefore stays 10 and the strict runtime shape does not change merely because
the editor learned a new source representation.

## What is on disk

`workspaces/<key>/exports/` holds one file per Scene, named
`<scene_id>.scene_export.json`. Exporting from the editor writes all of them at
once; `scripts/export_scene.sh <workspace> <scene-id>` rewrites a single one.

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
  "version": 9,
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
  "scene": {
    "schema": "srt.scene_maker_scene",
    "version": 10,
    "scene_id": "overworld01",   // names the map, not the Workspace
    "scene_kind": "instance",
    "coordinate_space": "scene_local_bottom_left_y_up",
    "size_cells": { "width": 100, "height": 100 },
    "terrain_cells": [                 // painted Terrain plus folded mountains
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

Worked example, a river crossing a mountain that reaches 10 m, with water at
2.0 m, a channel 0.5 m deep and 5.0 m of headroom:

```
bed 1.5, cut [1.5, 7.0], water [1.5, 2.0]
column: solid (−∞, 1.5]   floor of the tunnel, walking surface at 1.5
        water [1.5, 2.0]  water surface at 2.0
        air   [2.0, 7.0]  the headroom that was asked for
        solid [7.0, 10.0] the mountain above, walking surface at 10.0
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
actual heights. Depth, clearance and values interpolated between valid authored
points need not align to that quantum.

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

## Heights

`elevation_meters` on a Terrain cell is the top of its solid column, in metres,
in the same unit as everything else here. With no water over it that is also its
walking surface, which is what it always was; under a cut it is not, and the
section above says how to resolve it. Ground level in `world01` is `1.0`, water
`0.0`, a bridge deck `1.125`, a hill `2.0`; ramp cells step between them. The
values are authored, not derived, and their step is the Workspace's authoring
quantum. A consumer's simulation independently decides which difference an
Actor can traverse.

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

## Coordinates and units

The origin is the lower left corner of the Scene, x to the right and y upwards.
That is what `coordinate_space` asserts; a reader that assumes y-down will
mirror every map.

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
- Authored mountain contours never appear in this file. Their covered cells are
  folded into `scene.terrain_cells`: the highest absolute top wins, and its
  Terrain Asset supplies the cell's `asset_key`.
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
- Nothing is promised about Terrain under a Prop. A Prop carries an absolute
  `elevation_meters` and may stand over a hole, over water, or over nothing at
  all; a footprint with no cells beneath it is authored intent, not a defect.
  Whether an Actor can reach or stand on it is the consumer's question, and the
  export carries the heights it needs to answer it.
- `terrain_cells` and every body's `cells` are ordered by `y`, then `x`. `props`,
  `asset_profiles`, `scene.water_bodies` and `water_raster` are ordered by their
  id, ordinal. This ordering is a checked invariant, not a
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
