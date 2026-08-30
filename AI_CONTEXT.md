# SceneMaker context

SceneMaker is a standalone semantic scene authoring tool. A workspace is one
game and contains its own scene/template data and `config.json`.

Each Workspace consumes a synchronized copy of the current PolyTools Runtime
Export below `imports/polytools/`. PolyTools Catalog schema 1 is the closed
Asset set; Runtime Manifest schema 14 supplies geometry, hierarchy, Asset
References, and pivots. SceneMaker never discovers packages by scanning
directories and never reads a sibling PolyTools project at runtime.

The workspace configuration supplies terrain-cell size, authoring/game pixel
densities, enabled assets, editor colors, and optional Prop/Transition role
overrides. Prop footprints and anchors are derived from transformed PolyTools
visible geometry and rounded outward to whole authoring pixels. Scenes store
only semantic terrain, placements, transitions, template anchors, and exact
PolyTools `asset_key`s.

New workspace manifests explicitly start with 1 m terrain cells at 32
authoring pixels and 192 game pixels per meter. These values remain authored
workspace data rather than an implicit reader or export fallback.

SceneMaker has no runtime dependency. Later consumers receive a small generic
JSON export; they resolve the same `asset_key`s in their own PolyTools content
boundaries. Each export is a versioned snapshot of the Scene, Workspace grid,
and derived enabled asset profiles; editor colors and raw PolyTools documents
are not exported.

For the included workspace, run `scripts/sync_polytools_world.sh` after a
successful PolyTools Runtime Export. Run `scripts/export_scene.sh` to validate
and export `world01/scenes/world01.scene.json`; the default output is
`world01/exports/world01.scene_export.json`. The current authored scene is a
100 × 100 Grass map with one Tree and one Ankh.
