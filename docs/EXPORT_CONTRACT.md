# SceneMaker export contract

For consumers of the export — the Bevy runtime in `BevyProjects/world01` above
all. Everything a reader needs in order to load a map and compose it is here;
nothing else in this repository is part of the contract, and the authored
`scenes/`, `templates/` and `config.json` documents are explicitly not.

Current schemas: **export 22**, embedded **scene 17**. A reader must reject any
other version rather than guess. There is no migration path in either
direction; see the schema section of `AGENTS.md` for why.

Export 22 removes `game_key` and with it the Game level entirely. A Workspace
holds one flat set of Scenes: `scenes/`, `templates/` and `exports/` sit
directly under it, and SceneMaker has no grouping level between a Workspace and
its Scenes. Which mechanics a Scene is played under is the consumer's decision,
made from its own design data - `world01` calls that a Realm - and a map editor
has no business knowing what physics applies to what it authors, so the export
says nothing about it. `workspace_key` still says which World a Scene came
from. The embedded Scene is unchanged.

Export 21 had added `game_key` for a consumer holding several Games' exports
together. That consumer now holds one set.

Export 20 guarantees that a Placement's `instance_id` is never reused within a
Scene: once a number has named a Placement, no later Placement in that Scene
will carry it again, including after the first one is deleted. This is a
property of `scene.props[].instance_id`, a field the export already carried -
nothing new appears on the wire - so treat this paragraph, not a diff in the
document shape, as what the version bump ships.

Read literally that guarantee is easy to satisfy and useless: refuse to place
another Prop of an Asset once one has been placed, and no number is ever spent
twice. What it actually promises is the same instance_id space SceneMaker has
always drawn `asset_key_counter` from, kept alive for the Scene's whole life
rather than for as long as a Placement happens to survive: uniqueness among
every `instance_id` this Scene's Props have ever carried, not merely among
the ones alive at export time - which is the uniqueness this format already
had (`scene.props` is sorted and required unique by the embedded scene's own
schema; see `AGENTS.md`). A consumer that keeps a long-lived reference to a
Placement by id outside this Scene - `world01`'s per-map design file, which
names a Totem's owning team by `instance_id` and outlives any individual
Totem - can now depend on that reference resolving to the Placement it was
written for, or to nothing, for as long as the Scene exists. It can no longer
silently resolve to a different Placement that later took the same number.

Export 19 makes a switchable body a switch and a name. `scene.switches` is a
list of `{switch, initially_on}` and `scene.water_bodies[].switch` is one string
or `null`. Named states, `active_in` and `inactive` are gone, and with them the
rule that a `dry_bed` body contributes its cut in every state: **a body that is
off contributes nothing at all**, and the Terrain stands as though it had never
been authored.

Nothing was lost there; it changed hands. Whether a body that exists is carrying
water is weather, and weather is the consumer's. An active body ships
`bed_meters`, `surface_meters` and `cut_top_meters` on every cell, so a dry
channel is that body drawn without its fill - three runtime cases where the
document used to carry two authored fields:

| switch | the consumer's simulation | what stands on the map |
|---|---|---|
| off | — | nothing; the ground is whole |
| on | water | the bed under its fill |
| on | no water | the bed, walkable |

Existence is authored, weather is not, and each now lives on the side that
decides it. The change came from authoring rather than from the format: with
named states an author read two positions of one switch as two switches, and
`dry` as a state name sat beside `dry_bed` as the answer to what is left where
the water is not. Six maps used the old model and not one ever bound a body to
the off position. What it costs is the case of two branches on opposite sides of
one switch, which is now two switches the consumer holds opposite.

Export 18 adds no field and one rule: **a body is active when its own
activation says so and the body it leaves is active.** A branch is fed by the
river it comes off, so a branch of a river that is not there has nothing running
through it, and switching a river off takes everything hanging under it with it.
The tree this runs down is `junctions`, which export 17 already ships - the
alternative was to repeat a parent's states on every child, where the two could
then disagree. The embedded Scene is untouched; the version rises because the
same bytes mean more than they did.

Export 17 lets a river be switched. An authored branch that a game trigger
opens was not expressible: a body was simply there, and the only way to have one
appear was a second map. `scene.activation_groups` names the states a Scene can
be in and `scene.water_bodies[].activation` says which of them a body exists in,
so a fork is authored once and the runtime chooses. Alternatives are bodies
rather than variants inside one - the band is per body, so a stretch with two
widths is two bodies that are never active together, and neither the band nor
the raster changed shape to carry it. `inactive` says what the world looks like
without that water, a cut channel or no trace at all, because those are two
different authored intentions and neither is guessable from the geometry. Two
derived additions come with it: `junctions` says where a body's ends meet
another body, and `station_meters` on every water cell says where along its
river that cell lies. Both were already computed and thrown away. The embedded
Scene moves to 16 for the two authored fields; everything else is derived as
before.

Export 16 draws the water. A river was readable and invisible: `water_raster`
said which cells it occupies and nothing said where its banks are, so a map
could be walked into a river nobody could see. `water_bakes` ships one band per
water body - the same `vertices`, `triangle_indices`, `boundary_edges` and
`centerline_samples` `route_surface_bakes` and `bridge_bakes` carry, from the
same flattener and the same mesher - so a consumer needs no third way of
turning authored data into geometry. Its height is on every vertex and every
sample and never on the body, because a river falls; a deck's one level height
has no counterpart here. The raster is unchanged and stays what a simulation
reads. The embedded Scene is unchanged: both halves are derived.

Export 15 gives a bridge deck the two things a consumer needs to walk it rather
than merely stand on it. `bridge_bakes` now carries `centerline_samples` in the
shape and at the place `route_surface_bakes` has them, so a deck is walked by
the rule a Path is walked by; and `ground_at_start` / `ground_at_end` say what
lies under each end of the deck, so a bridge that reaches the bank can be told
from one that ends over the river. The embedded Scene is unchanged: both are
derived, and nothing an author writes changed.

Export 14 rebuilds the bridge deck. A deck is no longer a material laid over
the span: it is a row of planks, and a plank is a Placement Asset. The authored
record names that Asset in `plank_asset_key` and says how many planks fill the
span and what gap sits between two of them; `bridge_bakes` ships the planks
already laid out, beside the quad they add up to and the four corner posts.

The field is named after the part, not the whole: a deck is the row, and the
Asset is the one plank it repeats. Export 13 called it `deck_asset_key` and was
never read by a consumer, so 14 is the only name there has been in practice.

Export 14 also widens `surface` on an `asset_profiles` entry. It is no longer
Terrain-only: a Placement that is walked on says so there, because a bridge
deck is wood and has no Terrain underneath it to say so for it. Most Placements
still carry `null`.

Export 12 carried bridges for the first time: the authored record in
`scene.bridges`, and a `bridge_bakes` array beside the Scene holding each
deck's triangles and the four posts standing at its corners.

Export 11 carried excavating Paths. Every route segment now states its
`operation` and, when it excavates, the `clearance_above_meters` it asks for,
and a new `route_surface_cut_raster` array delivers the cells that excavation
removes from the Terrain. Export 10 refused such a Scene outright rather than
writing it through the additive shape; it is the version that could not say
what a tunnel means, not a version whose meaning changed.

The authored Scene currently has its own schema 22. It is deliberately newer
than the embedded Scene: elevation-region contours are editor source, folded
into the ordinary `terrain_cells` below and omitted from export. Authored route
surfaces remain independent continuous bands and are exported separately from
that folded Terrain.

## What is on disk

A Workspace holds its `scenes/`, `templates/` and `exports/` directories
directly: `workspaces/<workspace>/exports/` holds one file per Scene, named
`<scene_id>.scene_export.json`. A `scene_id` is unique across the Workspace.
Exporting from the editor writes every Scene at once;
`scripts/export_scene.sh <workspace> <scene-id>` rewrites a single one.

**Every file in a Workspace's `exports/` directory carries the same
`version`.**
That directory is one set, written by one export, and a consumer reads it as
one: a mixed set is a half-finished export rather than something to be
tolerated. Exporting from the editor writes them all; the single-Scene export
exists for a surgical swap between two runs of the same version, never for
straddling two.

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
  "version": 22,
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
      "surface": "land",               // every Terrain Asset has one
      "footprint_meters": null,        // null for Terrain Assets
      "anchor_meters": null
    },
    {
      "asset_key": "ankh",
      "surface": null,                 // most Placements present none
      "footprint_meters": { "width": 1.0625, "height": 1.71875 },
      "anchor_meters":    { "x": 0.53125, "y": 0.3125 }
    },
    {
      "asset_key": "plank",
      "surface": "wood",               // a Placement that is walked on
      "footprint_meters": { "width": 4.0, "height": 0.75 },
      "anchor_meters":    { "x": 2.0, "y": 0.375 }
    }
  ],
  "water_raster": [                    // derived from scene.water_bodies
    {
      "water_body_id": "river_0001",
      "water_kind": "river",
      "asset_key": "river",
      "junctions": [                   // where this body's ends meet another
        {
          "water_body_id": "river_0004",
          "own_station_meters": 42.5,  // station on THIS body
          "station_meters": 0.0        // station on the named body
        }
      ],
      "cells": [                       // water cells, not Terrain cells
        {
          "x": 64, "y": 20,
          "bed_meters": 1.5,           // floor of the channel
          "surface_meters": 2.0,       // top of the water
          "cut_top_meters": 7.0,       // Terrain is removed up to here
          "station_meters": 42.5       // where along the body this cell lies
        }
      ]
    }
  ],
  "water_bakes": [                     // also derived from scene.water_bodies
    {
      "water_body_id": "river_0001",   // the same body, in the same order
      "asset_key": "river",
      "vertices": [                    // one quad per flattened segment
        { "x_meters": 32.0, "y_meters": 14.0, "elevation_meters": 2.0 },
        { "x_meters": 40.0, "y_meters": 14.0, "elevation_meters": 1.5 },
        { "x_meters": 40.0, "y_meters": 6.0,  "elevation_meters": 1.5 },
        { "x_meters": 32.0, "y_meters": 6.0,  "elevation_meters": 2.0 }
      ],
      "triangle_indices": [0, 1, 2, 0, 2, 3],
      "boundary_edges": [              // this primitive's loop, not the outline
        { "start_vertex_index": 0, "end_vertex_index": 1 },
        { "start_vertex_index": 1, "end_vertex_index": 2 },
        { "start_vertex_index": 2, "end_vertex_index": 3 },
        { "start_vertex_index": 3, "end_vertex_index": 0 }
      ],
      "centerline_samples": [          // same shape as a route bake's
        {
          "x_meters": 32.0,            // the source
          "y_meters": 10.0,
          "elevation_meters": 2.0,     // per sample: a river is not level
          "width_meters": 8.0,
          "station_meters": 0.0,
          "authored_point_index": 0
        },
        {
          "x_meters": 40.0,
          "y_meters": 10.0,
          "elevation_meters": 1.5,     // further down, so lower
          "width_meters": 8.0,
          "station_meters": 8.0,
          "authored_point_index": 1
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
      "plank_asset_key": "plank",      // the Placement one plank is
      "length_meters": 10.0,           // the span, end to end
      "heading_degrees": 0.0,          // counter-clockwise from +X
      "plank_count": 12,               // as authored
      "plank_gap_meters": 0.1,         // as authored
      "plank_depth_meters": 0.741667,  // what that came out as
      "planks": [
        {
          "plank_id": "bridge_0001.plank_0000",
          "asset_key": "plank",
          "x_meters": 10.370833,       // the plank's centre
          "y_meters": 10.0,
          "elevation_meters": 1.125,
          "depth_meters": 0.741667,    // along the span
          "width_meters": 4.0          // across it, always the full deck
        }
      ],
      "vertices": [
        { "x_meters": 10.0, "y_meters": 12.0, "elevation_meters": 1.125 }
      ],
      "triangle_indices": [0, 1, 2, 0, 2, 3],
      "boundary_edges": [
        { "start_vertex_index": 0, "end_vertex_index": 1 }
      ],
      "centerline_samples": [          // same shape as a route bake's
        {
          "x_meters": 10.0,            // the start end
          "y_meters": 10.0,
          "elevation_meters": 1.125,
          "width_meters": 4.0,
          "station_meters": 0.0,
          "authored_point_index": 0
        },
        {
          "x_meters": 20.0,            // the end end; station = length_meters
          "y_meters": 10.0,
          "elevation_meters": 1.125,
          "width_meters": 4.0,
          "station_meters": 10.0,
          "authored_point_index": 1
        }
      ],
      "ground_at_start": {             // what lies under the start end
        "elevation_meters": 1.0,
        "asset_key": "grass",
        "source_id": null              // Terrain owns no id
      },
      "ground_at_end": {               // or null where nothing lies there
        "elevation_meters": 0.5,
        "asset_key": "river",
        "source_id": "river_0001"
      },
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
    "version": 15,
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
    "switches": [                      // what this Scene can switch
      {
        "switch": "fork_at_mill",      // author-given, unique in the Scene
        "initially_on": false          // where it stands before anything flips it
      }
    ],
    "water_bodies": [                  // the authored curves themselves
      {
        "water_body_id": "river_0001",
        "water_kind": "river",
        "asset_key": "river",
        "switch": null,                // null or absent: always there
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
        "plank_asset_key": "plank",    // Placement Asset, one plank of the deck
        "anchor_asset_key": "rope_post", // the Placement standing at each corner
        "start_authoring_px": { "x": 320, "y": 320 },
        "end_authoring_px":   { "x": 640, "y": 320 },
        "width_meters": 4.0,
        "elevation_meters": 1.125,     // the deck, level for the whole span
        "plank_count": 12,
        "plank_gap_meters": 0.1
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

`surface` is present and non-null for every Terrain Asset. A cell whose Asset
has no surface cannot occur: only Terrain Assets may be painted as Terrain.

A Placement may carry one too, and most do not - a tree is stood beside, not
walked on. The case that needs it is a bridge deck: it is walked on, it is
wood, and a bridge over a river has no Terrain underneath it that could say so
on its behalf. Read a Placement's `surface` as what that Placement presents to
walk on, and its absence as "nothing to say", never as "not walkable" - what an
Actor can do with a surface is the simulation's question, here as everywhere.

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

### The band beside the raster

`water_bakes` is the third delivery of the same river and the one to draw:
the water surface as a band, in the shape `route_surface_bakes` and
`bridge_bakes` use. `vertices` are scene-local metres carrying absolute
elevation, `triangle_indices` is a flat list of triples, `boundary_edges` lists
**each primitive's** edge loop - one quad per flattened segment and one
16-sided round join at every interior sample, as on a Path and unlike a
bridge's single quad - and `centerline_samples` runs from the source at station
`0` to the mouth. It is produced by the same code that bakes a Path, from the
same flattened centerline the raster is built on.

**A river is not level.** This is the one thing a reader must not carry over
from a deck. A bridge has one `elevation_meters` and every corner and sample
sits at it; a water body has no such field at all, and a check that every
vertex shares one height is wrong here rather than merely unnecessary. The
height is on every vertex and every sample, interpolated over arc length
between the authored points by the rule the section above states. Read it per
sample.

**Density is the flattener's, not the raster's.** A Bezier is subdivided until
it is straight within tolerance; a straight stretch stays one segment, and
`plank_count` has no counterpart here to argue with. `station_meters` is
cumulative horizontal arc length from the source, which is what a consumer
cutting the band into chunks hangs its cuts on. `authored_point_index` names
the authored curve point a sample falls on and is null for a subdivision
sample, exactly as on a Path.

**The band carries the surface and nothing else.** The bed, the cut and the
headroom stay in `water_raster`, where a volume belongs. One band per body,
never one per stretch of it - which is also why a river that must be two widths
is two bodies rather than one body cut into pieces.

A note on one word, because it is used for two things here and a consumer once
read the wrong one. A **height span** is an interval on the height axis at one
cell: `[bed_meters, surface_meters]` is a fill, `[bed_meters, cut_top_meters]` a
cut. A **stretch** is a length of river along its stations. A bridge's *span* is
neither; it is that structure's length, end to end.

**The raster and the band may disagree by a fraction of a cell, and that is
correct.** They answer two questions. The raster decides which cells the water
occupies - it is what a simulation reads and what the author checked in the
Section view - and it decides them by asking each cell centre a yes-or-no
question at half-metre resolution. The band follows the curve continuously, so
its bank sits where the curve is rather than where the grid rounded it to, and
at an inner bend the union of quads and joins reaches a hair differently than
the nearest-projection rule does. Neither is a correction of the other and
neither should be snapped to the other. Heights differ in the same small way
and for the same reason: the raster rounds to the millimetre because it is
written down per cell, the band to six decimal places because every bake does.

Two further differences follow from what a band is. It is **not clipped to the
Scene** - a river drawn over the map edge keeps its geometry here and simply
stops being rastered there, exactly as a Path's bake is not clipped. And where
two bodies overlap, their **bands overlap too**: a simulation reads the union
of the rasters, and a renderer draws two bands over one another, which is what
a widening river or a river running into a lake looks like.

### Where along the river a cell is

Every water cell carries `station_meters`: the station of the projection the
corridor rule already chose in order to give that cell its profile. It makes the
raster orderable along the flow, which is what a consumer cutting a river into
chunks, advancing or retreating a waterline, or asking how far a cell lies from
the source hangs its work on. Flow direction needs nothing else - a river runs
from station `0` to its last - and the value is rounded to the millimetre, like
the three heights and for the same reason.

Nothing follows from it about time. How fast a river fills, whether it recedes
from the mouth or falls in depth, and whether the walkable edge moves with the
picture are the consumer's decisions; the export says where water can be and
which way it runs.

### Where bodies meet

`junctions` lists every place one of a body's **ends** meets another body. It is
derived: the endpoint position and both curves are authored, so the projection
and both stations follow from them.

```jsonc
{ "water_body_id": "river_0001", "own_station_meters": 0.0, "station_meters": 42.5 }
```

`station_meters` is the station on the **named** body; `own_station_meters` is
the station on the body carrying the entry. Both use the unit
`centerline_samples[].station_meters` uses. An `own_station_meters` of `0` is
this body's source, its last station is its mouth, and anything between is
another body leaving this one's course.

There is no kind field, and that is deliberate: a branch and a rejoining side
channel are the same entry read at different stations, so a consumer that reads
it generally handles both without having to tell them apart. Every junction
appears on **both** bodies with the stations swapped, which is a checked
invariant, so a consumer holding one body never scans the file to find where its
water goes. Only ends produce a junction - a river merely crossing another
produces none.

### Water that switches

A water body can be switched on and off by the game. `scene.switches` names the
switches a Scene has and where each one starts; `scene.water_bodies[].switch`
names the one that decides whether a body exists. A body with `switch: null`
is always there.

```jsonc
"switches": [
  { "switch": "fork_at_mill", "initially_on": false }
]

"switch": "fork_at_mill"
```

A switch is one bit. `initially_on` says where it stands before anything has
flipped it, and it belongs to the **switch**, not to a body, so two bodies on
one switch cannot disagree about where a Scene starts.

**Alternatives are bodies.** A stretch that is wide while a branch is shut and
narrow while it flows is two bodies on two switches the consumer holds opposite.
A body carries no sections and no per-state widths: the band is per body, so a
second width is a second body, and nothing in `water_bakes` or `water_raster`
had to change to express it.

**A body that is off contributes nothing.** No fill, no cut; the Terrain stands
as though it had never been authored. There is no authored field for what is
left behind, because there is nothing to leave behind.

**A dry bed is yours, not ours.** Whether a body that exists is carrying water
is weather - a drought, a season, a gate half shut - and none of that is a fact
about the map. An active body ships `bed_meters`, `surface_meters` and
`cut_top_meters` on every cell, so a channel drawn without its fill is a dry bed
that an actor can walk into, decided per body and per moment on your side.

| switch | your simulation | what stands on the map |
|---|---|---|
| off | — | nothing; the ground is whole |
| on | water | the bed under its fill |
| on | no water | the bed, walkable |

**Switching runs downhill.** A body's own switch is not the whole answer: it is
there when that switch is on **and** the body its source sits on is there. What
feeds a body is stated in the Scene block, on the body itself:
`scene.water_bodies[].junctions[]` with `"end": "source"` names it. Ask the same
question of that body. A body with no source junction answers for itself, which
is what a river's uppermost stretch is.

Read the direction there and nowhere else. The `junctions` beside the raster
carry the same relations with their stations worked out, which is what they are
for, but they say nothing about which end of which body made them: a body that
is itself a branch **and** has a branch leaving it at its own station `0` then
carries two entries with `own_station_meters` `0`, one for each, and no rule
over that number can tell them apart. `end` is never ambiguous.

Where two bodies feed one, its source sits on both, and it has water as soon as
**either** of them does. A ring of bodies feeding one another is refused rather
than exported, so following this always ends.

Nothing about the chain is authored twice: a child does not repeat its parent's
switch, so the two cannot disagree. A main river may carry a switch like any
other body - switch it off and the whole tree under it is off, whatever the
branches say about themselves.

**Everything is exported in every state.** A body's raster and its band are in
the file whatever its switch says. Nothing is omitted, nothing is fetched later,
and there is no second file: what a switch decides is which of them a consumer
applies.

**What flips a switch is not here.** SceneMaker says that a body is switchable
and what the switch is called; when it flips is the consumer's decision, in the
way an Anchor's Template is. The two are not one mechanism: an Anchor swaps a
whole Template and belongs to the server, a switch changes authored elements
inside one Scene.

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
actual heights. A channel is likewise never shallower than the Workspace's
`minimum_channel_depth_meters`, also not exported: how deep a bed has to be is
a fact about a World - in `world01` it follows from how high a character can
climb, so that a river is not crossable dry or flowing - and SceneMaker only
holds an author to the number that World wrote down. Depth, clearance, values interpolated between valid authored
points, and later Path anchors derived from grade need not align to that
quantum. Only a Path's starting height is chosen directly on the quantum.

Heights are absolute. Nothing stores a relationship to the ground, so repainting
Terrain under a river never moves the water.

### Two bodies at one cell

Bodies may overlap, and at a branch they always do. A widening river, a fork and
later a river running into a lake are all authored that way, so an overlap is
never an error and **a consumer must not keep one water value per cell**. Which
body was read last decides nothing.

**A simulation reads the union.** Cells are not deduplicated across bodies, and
a cell listed twice is water once. Resolve such a cell by the column rule above:
the fills of every body covering it stack rather than compete.

- Two fills whose height spans overlap or touch are one water span,
  `[min bed_meters, max surface_meters]`.
- Two fills whose spans are disjoint are two water surfaces at that cell - an
  aqueduct over a river, the shape a bridge over one already has.
- Cuts unite the same way, `[min bed_meters, max cut_top_meters]`, and apply to
  Terrain alone.

Only an active body contributes anything at all - neither fill nor cut when its
switch is off; see [Water that switches](#water-that-switches). Whether an
active body's fill is drawn is the consumer's, and taking it away leaves the bed
walkable without changing a cut.

Where a branch leaves its parent the two agree by construction: SceneMaker
refuses an export whose branch source does not sit on its parent's surface at
that station, so the union there is the same water twice rather than two
answers that differ.

The raster is clipped to the Scene: a river drawn over the map edge simply
stops being authored there rather than failing to export. Terrain under a river
is untouched and stays whatever it is. Which of the two a position is depends on
which grid is asked - the water grid is finer and answers for its own cells -
and that is a resolution question, not the two-surfaces-at-one-place case a
bridge poses further down.

A Scene Template carries no water, so `water_bakes` is empty in every Template
file. Composition moves Terrain cells and Props and nothing else, so a Template
with a river would lose it at every Anchor; authoring one is refused instead.

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

A bridge is a straight level span. Eight authored numbers say all of it - the
deck's plank Asset, the anchor Asset, two ends, a width, one height, a
plank count and a plank gap - and everything else follows from them, which is
why nothing else is stored.

Its deck is an independent surface exactly like a Path's: it never folds into a
Terrain cell, a cut never removes it, and Terrain and a deck may occupy the same
X/Y at different heights. `bridge_bakes` ships it as the same triangles a Path
does, generated by the same code, so a runtime needs no second way to stand on
something.

**A deck is built, not painted.** It is a row of planks, and a plank is an
ordinary Placement Asset repeated along the span. What makes something a plank
is that a bridge repeats it, not a property the Asset carries, so any enabled
Placement can be a deck.

**Count is authored; depth is derived.** `plank_count` is what an author names
and `plank_gap_meters` is the empty run between two neighbours; the depth of
one plank is what is left once the gaps are taken off, so lengthening a bridge
thickens its planks rather than adding one. Gaps sit *between* planks and never
at the ends, so a deck always starts and finishes on wood:

    plank_depth = (length - (plank_count - 1) * plank_gap) / plank_count

That number is rounded to six decimal places once, in SceneMaker, and shipped
as `plank_depth_meters`; a deck whose gaps leave nothing over is refused while
it is being authored and can never reach an export.

Each plank ships as a box rather than a mesh: a centre, a `depth_meters` along
the span and a `width_meters` across it, which is always the full deck width.
Turn a unit plank by `heading_degrees` and stretch it to those two numbers. A
plank is derived, so like a post it carries no instance ID from the Scene.

**The planks are the authority; the parameters are the record.** Build what
`planks` gives you. `plank_count` and `plank_gap_meters` travel beside them so
a consumer can say what it was asked for, not so it can lay the deck out a
second time - laying it out twice is the one way to disagree with what the
author saw.

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
and derives nothing again from `start_authoring_px`, `width_meters` or the
plank parameters: those
are the authored source, kept so a later SceneMaker can reshape the bridge, and
recomputing corners from them is the one way to disagree with what the author
saw. The two do agree today - an end divided by `authoring_pixels_per_meter` is
exactly the midpoint of its two corner vertices, and their distance is exactly
`width_meters` - and that agreement is a consequence, not an invitation.

**A bridge deck is exactly one quad.** A bridge is straight and level, so its
bake holds four vertices, two triangles and one edge loop; `boundary_edges` is
therefore the outline of the walking surface and may be read as the edge one
falls from. The gaps between planks are not holes in it: what a simulation
walks on is the deck, and the planks are what it looks like. That is a promise
about bridges alone. In `route_surface_bakes` the
same field lists **each primitive's** edge loop - one per flattened segment
quad and one per round join - so a Path's union outline is not the
concatenation of them, and inner edges are in there too.

**A deck has the centerline a Path has.** `centerline_samples` is the same
record in the same place as in `route_surface_bakes`, produced by the same
flattener from `start_authoring_px` to `end_authoring_px`, with
`station_meters` running from `0` at the start end to `length_meters` at the
end end. Its density is that flattener's rule and nothing else: a Bezier is
subdivided until it is straight within tolerance, and a straight span is
already straight, so a deck flattens to exactly its two ends with nothing
between. There will never be fewer than those two. `plank_count` has no say in
it and never will - the planks are what a deck looks like, not where one may
stand, and a consumer that hangs its graph on the centerline is not moved when
an author makes twelve planks fourteen. Interpolate between samples the way you
would on a Path; on a bridge every interpolated value is constant.

**Level, and one width, by definition.** A bridge is a straight level span:
`elevation_meters` is one number, every corner and every sample sits at it, and
there is no grade. `width_meters` is one number too, and every sample carries
it. Read either from one sample or from the record and check the rest against
it if you like; they cannot differ. Should a bridge ever learn to slope or to
widen, that is a new export version, and a reader pinned to this one will
refuse it rather than misread it - which is the point of the pin.

**Under each end, what is there - not whether it is reachable.** `ground_at_start`
and `ground_at_end` say what a top-down look at that end of the deck finds once
the deck itself is lifted away: the highest of the painted Terrain, water, a
Path surface or another bridge's deck at that X/Y, resolved by the same column
rule the Section view shows the author, at the water-cell resolution that rule
uses. Each carries the surface's `elevation_meters`, the `asset_key` it
presents - read walkability off `asset_profiles` exactly as for any surface -
and the `source_id` of the authored thing that put it there, null for Terrain.
The whole field is `null` where nothing lies there at all: no Terrain painted,
no water, no band.

SceneMaker does **not** guarantee that an end meets walkable ground within a
step, and will not: a step is a property of an Actor, and the same rule that
keeps a corridor's width out of SceneMaker's judgement (a consumer applies
collision radius and traversal profile) keeps a step height out of it. An
author who wants a bridge to be walked onto puts its end over the bank; the
export says whether they did. Comparing that ground to the deck against an
Actor's step, and treating a bridge into nothing as the error it is, belongs to
the consumer, and now has a source to point at when someone asks why a
character stands where it stands.

**A bridge cuts nothing.** It adds a surface at its own elevation over the
Terrain it crosses; the Terrain below is untouched and stays walkable, water
below stays water, and `route_surface_cut_raster` knows nothing about bridges
because there is nothing for it to know. Passing under a bridge is the point:
one place carries two surfaces, and which one an Actor uses is the
simulation's question.

The deck presents the surface of its `plank_asset_key`, read from
`asset_profiles` exactly as a Terrain cell's is. That is why `surface` is no
longer Terrain-only: the deck Asset is a Placement, and a bridge over a river
has no Terrain underneath it that could carry `wood` on its behalf.

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
- In every exported water cell, `bed_meters ≤ surface_meters ≤ cut_top_meters`,
  and `station_meters` lies between `0` and that body's last centerline station.
- Every entry of `scene.switches` has a `switch` unique in the Scene and an
  `initially_on`.
- Every `switch` on a body names one that `scene.switches` declares. A body
  whose `switch` is absent or null is always there. Only a water body may carry
  one - a Placement, a bridge or a Path with a `switch` is refused rather than
  ignored.
- No body is fed, around the junctions, by itself: following `"end": "source"`
  junctions from any body never returns to it.
- Every junction in `scene.water_bodies[].junctions` has a matching pair beside
  the raster and the other way round. The Scene states the relation and which
  end of this body makes it; the raster adds the two stations. A reader may
  check one against the other and should.
- Every junction appears on both bodies, with `water_body_id` and the two
  stations exchanged, and names a body that exists. Each station lies between
  `0` and its own body's last centerline station. A junction is produced only by
  an end of a body.
- The source of a body that meets another carries that other body's surface
  height at the junction station, within the Workspace's elevation quantum.
- `water_raster` lists the same bodies as `scene.water_bodies`, in the same
  order, and each body's `cells` are what the corridor rule above produces from
  its curve. A reader may recompute them and must get the same set.
- `water_bakes` lists those same bodies, once each, in that same order, with
  matching `water_body_id` and `asset_key`. Position and id are the whole of
  the join between the three arrays.
- Every water band has at least two centerline samples, its first at station
  `0` and carrying `authored_point_index` `0`, its last carrying the index of
  the mouth. Stations do not decrease. Every baked value is finite, every index
  is in range, and no emitted triangle is degenerate at export precision - the
  same promise `route_surface_bakes` makes.
- Nothing is promised about a water band agreeing with its raster cell for
  cell. The two are derived by different rules on purpose; see the section
  above.
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
  `asset_profiles`, `scene.water_bodies`, `water_raster`, `water_bakes`,
  `scene.route_surfaces` and `route_surface_bakes` are ordered by their id,
  ordinal; `scene.switches` by `switch`. A `junctions` array is
  ordered by `water_body_id` and then by `own_station_meters`, because one body
  may meet another at both of its ends. Segment and sample arrays retain their
  deterministic chain order.
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
