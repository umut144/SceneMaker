# SceneMaker guide

SceneMaker is an independent Godot/C# authoring application. Its runtime and
Core must not reference MMORPG, Bevy, WorldAssets, PolyDraw, or sibling project
paths. Developer-time sync adapters may consume a game's current PolyTools
Runtime Export and copy it into a Workspace-local import boundary.

## Data ownership

- A Workspace-local `imports/polytools/catalog.json` is the closed source of
  available Asset identity, type, name, and Runtime package references. It and
  the referenced Manifests are synchronized copies, never live sibling reads.
- A workspace represents one PolyTools World/game. Its `config.json` owns grid
  metrics, enabled assets, and editor colors. The PolyTools catalog decides
  whether an Asset is Terrain or a Prop; PolyTools geometry owns footprints and
  anchors. SceneMaker never overrides an Asset's kind.
- Scenes and templates are workspace data. Their documents use `asset_key`s,
  never numeric IDs.
- No hidden metric defaults, directory-discovered Assets, or game-specific
  export format may be introduced.

## Engineering

Keep persisted models engine-neutral. Godot is UI and Canvas projection only.
Validate catalog and workspace configuration strictly before editing data.
Make one focused slice at a time, preserve unrelated work, and commit each
completed slice without pushing unless explicitly requested.

## Verification

Run for relevant changes:

```sh
dotnet build SceneMaker.csproj
dotnet build src/SceneMaker.Cli/SceneMaker.Cli.csproj
dotnet test tests/SceneMaker.Core.Tests/SceneMaker.Core.Tests.csproj
dotnet test tests/SceneMaker.Editor.Tests/SceneMaker.Editor.Tests.csproj
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . --editor --quit
```

## Project documentation

- `AI_CONTEXT.md` contains domain and architecture context.
- `UI_DESIGN.md` contains UI layout and interaction conventions.

## Version control

After every completed change, create a Git commit automatically. Each commit
must use a concise, descriptive commit message that explains the change. Do
not push commits; pushing is handled separately by the project owner.
