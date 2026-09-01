# Open work

Known rough edges that were found during the architecture review, judged real
but not worth doing at the time. Nothing here is a bug that loses data or
blocks authoring; each entry says what it costs to leave alone, so a later
reader can decide rather than rediscover.

Read `AGENTS.md` first — the rules there are what the fixes below have to stay
inside.

## 1. `SceneDocument.Create` accepts parameters it then ignores

`src/SceneMaker.Core/Documents.cs`

The factory takes `templateGroupNumber`, `insertionAnchorX` and
`insertionAnchorY` regardless of `sceneKind`. For an Instance it drops them
without a word: `TemplateDefinition` is only built when the kind is `Template`.
A caller that passes a group number for an Instance gets silence, not an
error.

The documents themselves stay legal — `DocumentValidation` rejects a
`TemplateDefinition` on an Instance — so this is an API that invites a mistake
rather than one that produces bad data.

A fix would split the factory into `CreateInstance(sceneId, width, height)` and
`CreateTemplate(sceneId, width, height, groupNumber, insertionAnchor)`, which
also removes the four default arguments. `SceneStore.Create` and
`SceneMakerMain.CreateScene` are the callers; `TestScenes` in
`tests/SceneMaker.TestSupport` builds both kinds and would follow.

## 2. Instance IDs stop reading in numeric order past 9 999

`src/SceneMaker.Core/PropEditing.cs`, `NextInstanceId`

IDs are `{assetKey}_{index:0000}`, and Props are held in canonical order sorted
ordinally by `InstanceId`. The format pads to four digits, so the ten
thousandth Prop of one asset becomes `grass_10000` and sorts between
`grass_0999` and `grass_2000`.

Nothing breaks: ordinal order is still total and deterministic, the canonical
ordering invariant still holds, and documents stay valid. What breaks is the
reader's expectation that the file reads in the order Props were placed.

Widening the pad changes every existing ID and is therefore a document
migration — see the schema-version section in `AGENTS.md` for why that is not
free. Worth doing only if a Scene ever gets near that many Props of one asset;
`world01` is nowhere close.

`NextInstanceId` also rebuilds a `HashSet` of every existing ID per call and
scans from 1 each time. That is invisible for hundreds of Props and would
matter long before the ID format did.

## 3. The settings menu addresses items by hardcoded integers

`src/SceneMaker.App/SceneMakerMain.cs`

`CreateWorkspaceMenuId = 10`, `LoadWorkspaceMenuId = 11`, and so on. The
numbers are grouped by section (10s, 20s, 30s) and are used both when the
items are added and when `UpdateDocumentStatus` enables or disables them.

It works, and the constants at least keep the numbers in one place. It is
still a hand-maintained mapping: adding an item in the middle of a section
means picking a free number, and nothing catches a collision.

An enum with `AddItem(text, (int)MenuItem.CreateWorkspace)` would make the set
closed and let the compiler notice a duplicate. Small, contained, no behaviour
change.

## 4. Building the asset bars selects an asset as a side effect

`src/SceneMaker.App/SceneMakerMain.cs`, `BuildTerrainAssetBar` and
`BuildPropAssetBar`

Both loops call `SelectTerrainAsset` / `SelectPropAsset` for the first asset
they add, which writes to the canvas and sets the status line. Layout
construction therefore also decides editor state, and the status message it
writes is immediately overwritten by whatever called it.

The effect is wanted — after loading a Workspace an asset must be selected —
but it belongs to `AdoptSession`, not to the code that adds buttons. Building
the bars should be pure; `AdoptSession` should choose the default afterwards
and set the status once.

Watch the ordering when moving it: `_canvas.SelectedTerrainAssetKey is null` is
what currently decides whether to select, so the choice has to happen after the
bars exist but before the caller writes its own status.
