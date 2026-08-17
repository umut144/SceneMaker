# SceneMaker context

SceneMaker is a standalone semantic scene authoring tool. A workspace is one
game and contains its own scene/template data and `config.json`.

The root `catalog.json` lists what may exist, using readable stable keys such
as `terrain.grass`, `prop.tree`, and `transition.portal`. It deliberately owns
no geometry, anchors, colors, collision, presentation, or gameplay metadata.

The workspace configuration supplies spatial meaning: terrain-cell size,
authoring/game pixel densities, enabled assets, colors, footprints, and
anchors. Scenes store only semantic terrain, placements, transitions, template
anchors, and their `asset_key`s.

SceneMaker has no runtime dependency. Later consumers receive a small generic
JSON export; they resolve the same `asset_key`s in their own catalogs.
