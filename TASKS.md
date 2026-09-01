# Open work

Known rough edges that were found during the architecture review, judged real
but not worth doing at the time. Nothing here is a bug that loses data or
blocks authoring; each entry says what it costs to leave alone, so a later
reader can decide rather than rediscover.

Read `AGENTS.md` first — the rules there are what the fixes below have to stay
inside.

## 1. Instance IDs stop reading in numeric order past 9 999

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

The allocation problem that used to sit alongside this one is fixed: naming a
Prop no longer formats a candidate string per attempt.

## 2. Building the asset bars selects an asset as a side effect

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

This one is App code, which has no tests, and it changes when an asset gets
selected. It is the entry here most likely to be noticed only while using the
editor, so it wants a manual pass rather than a quick fix.

## Done

- One factory per Scene kind, so `CreateInstance` no longer accepts Template
  parameters it would silently drop (`9c017be`).
- The settings menu is a closed enum instead of seven hand-picked integers
  (`a2f5e52`).
