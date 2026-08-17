# SceneMaker guide

SceneMaker is an independent Godot/C# authoring application. It must not
reference MMORPG, Bevy, WorldAssets, PolyDraw, or any external asset catalog.

## Data ownership

- Root `catalog.json` declares only globally available stable `asset_key`s,
  categories, and names.
- A workspace represents a game. Its `config.json` owns grid metrics, enabled
  assets, colors, footprints, and anchors.
- Scenes and templates are workspace data. Their documents use `asset_key`s,
  never numeric IDs.
- No hidden metric defaults, external registry, or game-specific export format
  may be introduced.

## Engineering

Keep persisted models engine-neutral. Godot is UI and Canvas projection only.
Validate catalog and workspace configuration strictly before editing data.
Make one focused slice at a time, preserve unrelated work, and commit each
completed slice without pushing unless explicitly requested.

## Verification

Run for relevant changes:

```sh
dotnet build SceneMaker.csproj
dotnet test tests/SceneMaker.Core.Tests/SceneMaker.Core.Tests.csproj
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . --editor --quit
```
