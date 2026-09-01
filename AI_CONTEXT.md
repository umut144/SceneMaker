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
densities, enabled assets, editor colors, and the surface each Terrain Asset
presents to a consumer's simulation; whether an Asset is Terrain or a Prop is
PolyTools catalog data and is never overridden. A surface is an open
lower_snake_case token such as `land` or `water`, held per Asset rather than per
cell so that one Terrain Asset cannot contradict itself. Prop footprints and
anchors are derived from transformed PolyTools visible geometry and rounded
outward to whole authoring pixels. Scenes store only semantic terrain, props,
template anchors, and exact PolyTools `asset_key`s.

New workspace configurations explicitly start with 1 m terrain cells at 32
authoring pixels and 192 game pixels per meter. These values remain authored
workspace data rather than an implicit reader or export fallback.

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
and export `world01/scenes/world01.scene.json`; the default output is
`world01/exports/world01.scene_export.json`. The current authored scene is a
100 × 100 Grass map with one Tree and one Ankh.
