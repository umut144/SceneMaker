# SceneMaker

[![verify](https://github.com/umut144/SceneMaker/actions/workflows/verify.yml/badge.svg)](https://github.com/umut144/SceneMaker/actions/workflows/verify.yml)

A desktop tool for authoring 2D game maps on a grid: terrain, rivers, paths, hills, bridges and placed objects.
It writes a versioned JSON export that a game reads. Built with Godot 4 and C#.

<!-- TODO: Screenshot of the editor with a map open (e.g. workspaces/world01/scenes/overworld01.scene.json),
     showing ToolBar, ContextMenu, Canvas, Outliner and Inspector in one image. Save as docs/images/editor.png
     and embed it here. This is the most important image in the README. -->

## Why it exists

SceneMaker exists to produce map data. It is where the maps of a separate game project (`world01`, a Bevy game that
is not part of this repository) are authored, and what it hands over is a validated, versioned JSON export that the
game reads. The game needs more than a tile painter gives: rivers that can be reshaped after drawing, branches that
a switch can turn on and off, routes that cut through hills, and a written contract it can rely on. SceneMaker
deliberately does not know the game: there is no game-specific export format and no sibling project is read at
runtime.

## What it does today

- **Workspace**: one directory per game world (`workspaces/<key>/`) with a `config.json` that defines every usable
  Asset (role, colour, grid metrics), plus `scenes/`, `templates/` and `exports/`.
- **Editor areas**: Terrain (Pencil, Line, Fill, Eraser, Selector), River (draw, branch, insert point, re-attach),
  Path, Hill (elevation regions), Bridge, Placements (props) and Scene Templates. Undo history, autosave,
  Outliner and Inspector.
- **Water as curves**: rivers are Bezier centre lines with width and vertical section. The cells a simulation reads
  are derived from the curve at export time, never stored in the Scene. Branches and switches are supported.
- **Validation**: every file read or written is validated strictly; readers reject versions they do not know.
- **Export**: validated, versioned JSON per Scene and Scene Template, written only when the content changed.
- **Headless exporter**: `SceneMaker.Cli` validates and exports without the Godot editor.
- **PolyTools import**: geometry (footprints, pivots, collision bounds) for Placements is read from a synchronised
  copy of the export of PolyTools, a separate asset tool that is not part of this repository. The copy for `world01` is committed.

Not there yet (see [`docs/TASKS.md`](docs/TASKS.md) for the full list): adding Assets through the editor (a
deliberate non-goal, edit `config.json` instead), editing a river point's handles after drawing, renaming a switch,
carrying water or bridges in a Scene Template, a height brush, a 3D preview. There is no migration between schema
versions; an old file is rejected, not converted.

## Workflow

1. **Input**: a Workspace (`config.json`, the Asset catalogue) and, for Placements, the synchronised PolyTools
   geometry under `imports/polytools/`.
2. **Editing**: open the Workspace in the Godot application, open or create a Scene and draw.
3. **Output**: *Export* writes `workspaces/<key>/exports/<scene-id>.scene_export.json`. The game reads only that
   file, never the authored document.

<!-- TODO: Short screen recording (GIF, 10-20 s): draw a river with two points, create a branch, export.
     Save as docs/images/river.gif. Second screenshot: the same Scene next to the top of its export JSON. -->

## Export format

One JSON file per Scene. The full field-by-field contract, including what each field promises, is
[`docs/EXPORT_CONTRACT.md`](docs/EXPORT_CONTRACT.md). A consumer must reject any version it does not know
(currently export version 22). Real excerpt from
[`workspaces/world01/exports/test_template.scene_export.json`](workspaces/world01/exports/test_template.scene_export.json):

```json
{
  "format": "scene_maker_scene_export",
  "version": 22,
  "workspace_key": "world01",
  "grid": {
    "terrain_cell_meters": 1.0,
    "authoring_pixels_per_meter": 32,
    "game_pixels_per_meter": 192,
    "water_cell_meters": 0.5
  },
  "asset_profiles": [
    { "asset_key": "ankh", "surface": null,
      "footprint_meters": { "width": 1.0625, "height": 1.4375 },
      "anchor_meters": { "x": 0.53125, "y": 0.03125 } }
  ],
  "water_raster": [], "bridge_bakes": [],
  "scene": { "scene_id": "...", "terrain_cells": "...", "props": "...", "water_bodies": "..." }
}
```

(Abridged; `...` marks elided values.)

## Technology

- **Godot 4.7 (.NET build)**, C# on .NET 9. Godot is used only for the window, drawing and input. Persisted data
  and all editing logic are engine-neutral.
- Four projects, layered downwards, nothing references upwards:
  `SceneMaker.Core` (documents, validation, editing operations, export; no dependencies) ->
  `SceneMaker.Editor` (view projection, tool state machine, undo; no Godot) ->
  `SceneMaker.App` (the Godot application) and `SceneMaker.Cli` (headless export, depends on Core only).
- Editing operations are pure `SceneDocument -> SceneDocument` functions, which is what makes them testable
  without an engine. Full validation runs at the file boundaries only.
- There is no C++ or GDExtension in this repository.

Reasoning behind individual decisions is written down in [`docs/DESIGN_NOTES.md`](docs/DESIGN_NOTES.md); the
project rules are in [`AGENTS.md`](AGENTS.md).

## Setup

Requirements: the [.NET 9 SDK](https://dotnet.microsoft.com/download) and the .NET ("mono") build of Godot 4.7.
`jq` is needed only for the PolyTools sync script.

```sh
git clone https://github.com/umut144/SceneMaker.git
cd SceneMaker

dotnet build SceneMaker.csproj                          # the Godot application assembly
dotnet build src/SceneMaker.Cli/SceneMaker.Cli.csproj   # the headless exporter

godot --headless --path . --import                      # first start: let Godot import the assets
godot --path . --editor                                 # open the project, press Play (F5)
```

`godot` stands for your Godot .NET binary; on macOS the repository's scripts assume
`/Applications/Godot_mono.app/Contents/MacOS/Godot` (override with `SCENEMAKER_GODOT_BIN`).
The main scene is `src/SceneMaker.App/SceneMakerMain.tscn`.

Headless export of every Scene of a Workspace (this rewrites files under `exports/` where content changed), or
of one Scene:

```sh
dotnet run --project src/SceneMaker.Cli -- workspaces/world01
dotnet run --project src/SceneMaker.Cli -- workspaces/world01 <scene-id>
```

## Project structure

| Path | Contents |
| --- | --- |
| `src/SceneMaker.Core` | Documents, validation, stores, PolyTools import, editing operations, export |
| `src/SceneMaker.Editor` | Editor state without Godot: view projection, tool state machine, undo history |
| `src/SceneMaker.App` | Godot application: layout, dialogs, canvas drawing, input plumbing |
| `src/SceneMaker.Cli` | Headless validate-and-export |
| `tests/` | xUnit projects for Core and Editor, shared fixtures, shell tests for start-up and sync |
| `workspaces/world01` | The shipped example Workspace: config, Scenes, Templates, exports, PolyTools import |
| `docs/` | Export contract, design notes, UI design, task tracker |
| `scripts/` | Check, export and PolyTools sync scripts |
| `assets/` | Application icon and tool icons |
| `.github/workflows` | CI (`verify.yml`) |

## Tests and quality

```sh
dotnet test tests/SceneMaker.Core.Tests/SceneMaker.Core.Tests.csproj
dotnet test tests/SceneMaker.Editor.Tests/SceneMaker.Editor.Tests.csproj
tests/test_sync_polytools_world.sh      # PolyTools sync preflight (needs jq)
tests/test_headless_start.sh            # boots the editor headless; needs the Godot binary
./scripts/check.sh --tests              # builds + all of the above except the editor boot, plus an export of each Workspace
```

All projects build with warnings treated as errors. CI runs the build, both test projects and the sync preflight.
There are no automated tests for the Godot application project itself; logic that needs testing lives in Core or
Editor for that reason. The canvas is checked by hand.

## Status and next steps

Work in progress. All commits are by one author; the first is from 17 August 2026. The editor is used to author the maps in
`workspaces/world01`, and the export is read by the `world01` game. Open items are tracked in
[`docs/TASKS.md`](docs/TASKS.md); the ones marked active are the switch-binding for placeable objects (`SWITCH-01`),
manual acceptance of the editor after the Inspector and Outliner rework (`APP-02`), and picking and emphasising
Paths (`PATH-02`).


## Credits and licence

Copyright © 2026 Umut Coşkun. All rights reserved.

SceneMaker is released under a proprietary no-use licence, not an open-source one. No licence or other right is
granted: without prior explicit written permission you may not use, run, copy, modify, distribute or host the
software, for any purpose (including private, educational, research and testing use). Access to this repository
grants no rights. Third-party components remain under their own licences. The complete terms, in English and German,
are in [`LICENSE`](LICENSE).

Built on [Godot Engine](https://godotengine.org) (MIT) and xUnit. Geometry for the example Workspace comes from
PolyTools (separate project). The application and tool icons in `assets/` were created with Codex (OpenAI).
