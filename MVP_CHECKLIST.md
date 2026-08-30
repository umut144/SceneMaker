# SceneMaker standalone roadmap

- [x] Extract SceneMaker into its own Godot/C# repository.
- [x] Remove direct code dependencies on MMORPG and its asset catalog.
- [x] Replace the legacy root catalog with a Workspace-local synchronized
      PolyTools Catalog and Runtime Manifest import.
- [x] Derive Prop footprints and anchors from current PolyTools geometry.
- [x] Keep only grid, colors, enablement, and role overrides in `config.json`.
- [x] Migrate included `world01` documents from numeric IDs to asset keys.
- [x] Drive grid metrics from workspace configuration and spatial Asset metrics
      from the synchronized PolyTools import.
- [x] Add UI for workspace asset profiles.
- [x] Replace legacy `workspace.json` with `config.json` as the sole workspace manifest.
- [x] Define and implement a generic JSON export.
- [x] Add deterministic PolyTools sync and headless scene-export commands.
- [x] Author and export the first 100 × 100 `world01` Grass/Tree/Ankh map.
- [ ] Complete manual acceptance of the standalone workflow.
