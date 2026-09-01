# SceneMaker export contract

For consumers of the export — the Bevy runtime in `BevyProjects/world01` above
all. Everything a reader needs in order to load a map and compose it is here;
nothing else in this repository is part of the contract, and the authored
`scenes/`, `templates/` and `config.json` documents are explicitly not.

Current schemas: **export 5**, embedded **scene 7**. A reader must reject any
other version rather than guess. There is no migration path in either
direction; see the schema section of `AGENTS.md` for why.

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
  "version": 5,
  "workspace_key": "world01",
  "grid": {
    "terrain_cell_meters": 1.0,        // edge length of one Terrain cell
    "authoring_pixels_per_meter": 32,  // unit of every *_authoring_px value
    "game_pixels_per_meter": 192       // the consumer's own render scale
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
  "scene": {
    "schema": "srt.scene_maker_scene",
    "version": 7,
    "scene_id": "overworld01",   // names the map, not the Workspace
    "scene_kind": "instance",
    "coordinate_space": "scene_local_bottom_left_y_up",
    "size_cells": { "width": 100, "height": 100 },
    "terrain_cells": [
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

## Heights

`elevation_meters` is the height of a cell's walking surface, in metres, in the
same unit as everything else here. Ground level in `world01` is `1.0`, water
`0.0`, a bridge deck `1.1`, a hill `2.0`; ramp cells step between them. The
values are authored, not derived, and SceneMaker never constrains their range or
step - a consumer's simulation decides which step an Actor can take.

It is authored per cell, not per Asset, because one grass Asset covers valley
floor and hill alike. `default_elevation_meters` on the Scene is the height a
newly authored cell takes when the author names none; it is authored intent
about the Scene, like its size, and a consumer has no use for it.

A Prop carries its own `elevation_meters`, usually the height of the Terrain
under it. It is a separate field because a bridge deck sits above the water it
crosses - and that is the case that shows why a place does not have one
surface. Over a river there are two: `(water, 0.0)` for what swims and
`(land, 1.1)` for what walks across. Nothing in the export resolves that. The
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
| Prop position, Anchor position | `*_authoring_px` integers | see below |
| Asset footprint and anchor | meters, in `asset_profiles` | — |

```
authoring_px_per_cell = terrain_cell_meters × authoring_pixels_per_meter
meters                = authoring_px / authoring_pixels_per_meter
game_px               = authoring_px × game_pixels_per_meter / authoring_pixels_per_meter
```

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
- A `scene_id` names one Scene in the whole Workspace — never an Instance and a
  Template at once — and it does not change over the life of that Scene. The
  editor refuses to create a second Scene under an existing id, and an export
  refuses a Workspace whose ids collide rather than overwriting a file. A
  consumer may use it as a stable name across a reconnect.
- Every Terrain cell lies inside `size_cells`; no two cells share a coordinate.
- Every Prop footprint is fully covered by Terrain. A Prop never floats over a
  hole.
- `terrain_cells` is ordered by `y`, then `x`. `props` and `asset_profiles` are
  ordered by their id, ordinal. This ordering is a checked invariant, not a
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
