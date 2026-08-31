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

## Project layout

Four projects, layered strictly downwards. Nothing ever references upwards.

| Project | Depends on | Holds |
| --- | --- | --- |
| `src/SceneMaker.Core` | nothing | Documents, validation, stores, PolyTools import, editing operations, export |
| `src/SceneMaker.Editor` | Core | Editor state that is not Godot: view projection, tool state machine, previews, undo history |
| `src/SceneMaker.App` | Core, Editor | The Godot application: layout, dialogs, drawing, input plumbing |
| `src/SceneMaker.Cli` | Core | Headless validate-and-export |

`tests/SceneMaker.TestSupport` holds the fixtures both test projects use. There
are no tests against `SceneMaker.App`: a test project cannot reference it without
pulling in `Godot.NET.Sdk`, which is why anything worth testing belongs in Core
or Editor.

Three rules follow from this and are worth stating outright:

- Editing operations are pure `SceneDocument -> SceneDocument` functions. They
  assume a canonical document and produce one; full validation runs at the IO
  boundaries only (`DocumentJson`, `SceneStore`, `SceneExport`). Do not add
  `DocumentValidation.Validate` calls back into per-edit paths.
- Input takes one path. `SceneCanvas` converts Godot events into coordinates and
  hands them to `ToolInteraction`, which answers with exactly one `ToolOutcome`;
  `SceneMakerMain.ExecuteSceneCommand` applies it, records undo and reports it.
  New tools extend `ToolInteraction`; they do not add events to the canvas.
- An open Workspace is one value. `EditorSession` bundles the Workspace, its
  PolyTools catalog, its configuration and both display catalogs; it is built in
  full before it is adopted and swapped as a unit. `SceneMakerMain` keeps one
  `EditorSession?`, never a set of correlated nullable fields, so a failed load
  cannot leave a half-opened Workspace behind.

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
