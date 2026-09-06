# SceneMaker guide

SceneMaker is an independent Godot/C# authoring application. Its runtime and
Core must not reference MMORPG, Bevy, WorldAssets, PolyDraw, or sibling project
paths. Developer-time sync adapters may consume a game's current PolyTools
Runtime Export and copy it into a Workspace-local import boundary.

## Data ownership

- A workspace represents one PolyTools World/game. Its `config.json` is the
  closed source of SceneMaker Assets and owns each stable `asset_key`, display
  name, SceneMaker role, editor color and Terrain semantics as well as the grid
  metrics. None of that authoring identity is inferred from PolyTools.
- A Workspace-local `imports/polytools/` boundary may contribute visible bounds
  and pivots/anchors to SceneMaker Assets whose role is `placement`, joined by
  the `polytools_asset_id` the Asset names — the id PolyTools keeps stable, not
  a key either project may rename. It is required on a Placement, which without
  PolyTools geometry has no meaning, and optional on Terrain, which SceneMaker
  may author without PolyTools ever hearing of it. The boundary's catalog and
  referenced Manifests are synchronized copies, never live sibling reads. Only
  configured Placement roots and their transitive geometry references belong
  there; Terrain Assets require no PolyTools package, and a Workspace without
  Placements needs no import.
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

Four rules follow from this and are worth stating outright:

- Editing operations are pure `SceneDocument -> SceneDocument` functions. They
  assume a canonical document and produce one; full validation runs at the IO
  boundaries only (`DocumentJson`, `SceneStore`, `SceneExport`). Do not add
  `DocumentValidation.Validate` calls back into per-edit paths.
- Input takes one path. `SceneCanvas` converts Godot events into coordinates and
  hands them to `ToolInteraction`, which answers with exactly one `ToolOutcome`;
  `SceneMakerMain.ExecuteSceneCommand` applies it, records undo and reports it.
  New tools extend `ToolInteraction`; they do not add events to the canvas.
- An open Workspace is one value. `WorkspaceSession` bundles the Workspace, its
  PolyTools catalog, its configuration and both display catalogs; it is built in
  full before it is adopted and swapped as a unit. `SceneMakerMain` keeps one
  `WorkspaceSession?`, never a set of correlated nullable fields, so a failed
  load cannot leave a half-opened Workspace behind. Opening a Workspace is not
  an editor concern, which is why it sits in Core and the headless exporter
  uses the same path.
- Lifecycle decisions live in `EditorController`, not in the Godot node. It
  owns the open Workspace, the open Scene, the edit history, the Template
  listing and the transient Template Preview. Commands take plain paths, change
  state only when they succeed, and answer with an `EditorReport` instead of
  touching the interface; whether a failure belongs in the status line or in a
  dialog is the caller's decision. `SceneMakerMain` reads the controller and
  never keeps its own copy of any of it - what stays there is drawing, layout,
  dialogs and the autosave timer.

## Schema versions

Six schemas, each versioned on its own. They are deliberately not tied
together: the PolyTools schemas are owned by PolyTools, and the rest change for
unrelated reasons.

| Schema | Constant | Current |
| --- | --- | --- |
| Scene document | `SceneMakerSchemas.SceneVersion` | 19 |
| Workspace config | `WorkspaceConfigurationStore.Version` | 14 |
| Scene export | `SceneExport.Version` | 15 |
| PolyTools catalog | `PolyToolsCatalogImporter.CatalogSchemaVersion` | 3 |
| PolyTools runtime manifest | `PolyToolsCatalogImporter.ManifestSchemaVersion` | 21 |
| Recent session | `RecentSessionStore.Version` | 4 |

There is no migration code and none is planned. Every reader rejects a document
whose version it does not know. Bumping a version therefore means rewriting the
affected files by hand — `workspaces/` is the only authored data — or
re-exporting and synchronizing generated imports, and updating the reader in
`BevyProjects/world01` for the export. If that ever stops being practical, add
migrations at the IO boundary, never inside the documents.

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
tests/test_sync_polytools_world.sh
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . --editor --quit
```

CI (`.github/workflows/verify.yml`) runs everything above except the last line:
the Godot SDK comes from NuGet, so the application assembly compiles on a plain
runner, but starting the editor needs the Godot binary. Booting it headless
stays a manual step, as does looking at the canvas.

`scripts/ci_status.sh` reports the newest run and, when it is red, prints the
failing steps' log lines. It needs `gh` and `jq`.

## Project documentation

- `AI_CONTEXT.md` contains domain and architecture context.
- `UI_DESIGN.md` contains UI layout and interaction conventions.
- `TASKS.md` is the task tracker: one table row per open, deferred or
  deliberately rejected outcome, with an ID, area, one-sentence outcome and
  status. Check it before "fixing" something that looks odd, and add a row
  rather than leaving a finding undocumented. Its tracker rules are at the end
  of the file.
- `DESIGN_NOTES.md` holds the reasoning behind those rows: what a rough edge
  costs to leave alone, what shape a fix has to take, and which alternatives
  were considered and rejected, plus the settled decisions that are closed.
  Every task row links into it, and the prose lives there rather than in the
  tracker.

## Version control

After every completed change, create a Git commit automatically. Each commit
must use a concise, descriptive commit message that explains the change. Do
not push commits; pushing is handled separately by the project owner.
