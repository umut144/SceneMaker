# SceneMaker guide

SceneMaker is an independent Godot/C# authoring application. Its runtime and
Core must not reference MMORPG, Bevy, WorldAssets, PolyDraw, or sibling project
paths. Developer-time sync adapters may consume a game's current PolyTools
Runtime Export and copy it into a Workspace-local import boundary.

## Data ownership

- A Workspace represents one PolyTools World. Its `config.json` is the closed
  source of SceneMaker Assets and owns each stable `asset_key`, display name,
  SceneMaker role, editor color and Terrain semantics as well as the grid
  metrics. None of that authoring identity is inferred from PolyTools. A
  Workspace's directory is named after its `workspace_key`.
- A Game is a named subdirectory of a Workspace - a plain directory with no
  schema or file of its own, holding its own `scenes/` and `templates/`
  (`GameStore`). It is the sub-unit a set of maps built from the World's
  shared Assets and PolyTools import belongs to: `workspaces/world01/sandbox`
  and `workspaces/world01/moba` are two Games sharing the one `world01`
  World. A Scene's Game membership is derived from its file path and is
  never stored in the document - see `docs/DESIGN_NOTES.md` under "A
  Workspace becomes a World; a Game sits between it and Scenes" for why.
- A Workspace-local `imports/polytools/` boundary, shared by every Game in the
  Workspace, may contribute visible bounds and pivots/anchors to SceneMaker
  Assets whose role is `placement`, joined by the `polytools_asset_id` the
  Asset names — the id PolyTools keeps stable, not a key either project may
  rename. It is required on a Placement, which without PolyTools geometry has
  no meaning, and optional on Terrain, which SceneMaker may author without
  PolyTools ever hearing of it. The boundary's catalog and referenced
  Manifests are synchronized copies, never live sibling reads. Only configured
  Placement roots and their transitive geometry references belong there;
  Terrain Assets require no PolyTools package, and a Workspace without
  Placements needs no import.
- Scenes and templates are Game data, one level under the Workspace they
  belong to. Their documents use `asset_key`s, never numeric IDs.
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
  uses the same path. A Workspace carries no Game by itself: opening a Game
  (`LoadedGame`, `GameStore`) is a separate, later step, and creating a
  Workspace does not create one.
- Lifecycle decisions live in `EditorController`, not in the Godot node. It
  owns the open Workspace, the open Game, the open Scene, the edit history, the
  Template listing and the transient Template Preview. Commands take plain
  paths, change state only when they succeed, and answer with an
  `EditorReport` instead of touching the interface; whether a failure belongs
  in the status line or in a dialog is the caller's decision. `SceneMakerMain`
  reads the controller and never keeps its own copy of any of it - what stays
  there is drawing, layout, dialogs and the autosave timer.

## Schema versions

Six schemas, each versioned on its own. They are deliberately not tied
together: the PolyTools schemas are owned by PolyTools, and the rest change for
unrelated reasons.

| Schema | Constant | Current |
| --- | --- | --- |
| Scene document | `SceneMakerSchemas.SceneVersion` | 22 |
| Workspace config | `WorkspaceConfigurationStore.Version` | 17 |
| Scene export | `SceneExport.Version` | 20 |
| PolyTools catalog | `PolyToolsCatalogImporter.CatalogSchemaVersion` | 3 |
| PolyTools runtime manifest | `PolyToolsCatalogImporter.ManifestSchemaVersion` | 23 |
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

Everything below lives in `docs/`. Only this file and `CLAUDE.md` stay at the
repository root, because they are what an agent is pointed at first.

- `docs/AI_CONTEXT.md` contains domain and architecture context.
- `docs/UI_DESIGN.md` contains UI layout and interaction conventions.
- `docs/EXPORT_CONTRACT.md` is what the consumer reads: every field the export
  ships and what it promises. It is the one document with a reader outside this
  repository, so it changes by agreement and not by edit.
- `docs/TASKS.md` is the task tracker: one table row per open, deferred or
  deliberately rejected outcome, with an ID, area, one-sentence outcome and
  status. Check it before "fixing" something that looks odd, and add a row
  rather than leaving a finding undocumented. Its tracker rules are at the end
  of the file.
- `docs/DESIGN_NOTES.md` holds the reasoning behind those rows: what a rough
  edge costs to leave alone, what shape a fix has to take, and which
  alternatives were considered and rejected, plus the settled decisions that
  are closed. Every task row links into it, and the prose lives there rather
  than in the tracker.

## Telling the consumer about a map

What we say to `world01` about a Scene comes from
`workspaces/<workspace>/exports/<scene>.scene_export.json`, never from the
authored document under `scenes/` or `templates/`. They hold the export; the
document can be hours ahead of it and carry work that was never exported. This
has cost us two corrections already - both times we described the file nearest
to us instead of the file they have.

Reading the right file is not enough. Counts about a map count **cells**, not
raster entries: a cell three bodies claim is one cell and three entries, and
their assurances are about cells. The check is that the arithmetic closes -
distinct cells, plus one per two-body cell, plus two per three-body cell, equals
the entry total. That was the third number we got wrong in a week, and the first
one whose source was already right.

And a count over water cells is a claim about one **position of every switch**,
so say which. "These cells overlap" is unconditional; "these cells meet flowing
water" is not, and the difference is invisible until somebody flips a switch. A
table of switch positions beside a table of counts is two answers to two
questions unless each count names its row.

## Version control

After every completed change, create a Git commit automatically. Each commit
must use a concise, descriptive commit message that explains the change. Do
not push commits; pushing is handled separately by the project owner.
