# WorldVoxMaker UI Design

WorldVoxMaker authors a sparse volumetric X/Y/Z grid in a Godot 3D viewport.
All displayed and entered dimensions are metres. One voxel is 1 m³; shape and
curve parameters snap to the Workspace's 0.2 m subgrid.

## Layout

The interface has four horizontal regions:

```text
┌─────────────────────────────────────────────────────────────────────┐
│ Workspace · Scene · Save/Undo/Redo · Export format                 │
├─────────────────────────────────────────────────────────────────────┤
│ Tool [Tile/Hill/Path] · Operation [Add/Subtract] · Material         │
├─────────────────────────────────────────────────────────────────────┤
│ Context parameters for Hill or Path                                │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│                         3D voxel viewport                           │
│                                                                     │
├─────────────────────────────────────────────────────────────────────┤
│ Status / draft instructions                                        │
└─────────────────────────────────────────────────────────────────────┘
```

The scene and Workspace are always visible. The scene label includes the live
filled-voxel count. Controls that need an open Workspace or Scene are disabled
until one exists.

## Viewport

- Right-drag orbits the camera around the Scene centre.
- The mouse wheel changes camera distance.
- The ground plane shows the Workspace's 1 m grid even in an empty Scene.
- Filled voxels use the color of their Workspace Asset material.
- Ray picking returns the hit voxel, exact metric point, and entry-face normal.
  Additive Tile uses the adjacent cell; subtractive Tile uses the hit cell.
- Dragging Tile continues one edit stroke, producing one undo step.

## Smart cross-section

The horizontal cut follows the elevation the active tool will edit under the
pointer. It becomes active only when solid volume remains above that point.
At or above the surface the viewport automatically returns to the complete 3D
view.

In cross-section mode, voxels through the active Y layer remain visible and a
yellow plan contour shows the footprint of all volume above the cut. A tunnel
therefore reads as empty space while the surrounding mountain outline remains
visible.

## Tools

Every tool has the same explicit operation selector:

- `Add` creates filled volume.
- `Subtract` removes the identical derived volume and needs no material.

### VoxelTile

Click or drag to paint individual 1 m³ cells. On an empty map, the visible
ground grid targets Y=0. Clicking a filled face in Add mode targets its adjacent
cell, making stacking direct.

### VoxelHill

Click at least three footprint points and press Enter. Base and Top are absolute
metre elevations. Add mode inherits material per column from the nearest
existing voxel at or below the base; it never invents a hill material. Subtract
mode cuts the same closed volume. Escape removes the most recent point.

### VoxelPath

Click at least two control points and press Enter. Drag directly after placing a
point to pull aligned Bezier handles. Each point carries width, cross-section
height, and absolute elevation. With `Grade per segment`, each new point derives
its elevation from the previous point and the signed rise per horizontal metre.

Add mode can leave the corridor floating or fill its gap down to existing
support/the ground plane. Subtract mode cuts only the corridor, enabling tunnels
and crossing internal routes. Escape removes the most recent control point.

## Persistence and history

Committed actions pass through `VoxelToolInteraction -> ToolOutcome ->
EditorController`. Hill and Path drafts are transient; Enter creates one undoable
edit. Tile dragging collapses into one undo step. Successful document edits
restart the autosave timer. Ctrl/Cmd-S saves, Ctrl/Cmd-Z undoes, and
Ctrl/Cmd-Shift-Z or Ctrl/Cmd-Y redoes.

## Export

The top bar exposes:

- `Heightfield (2.5D)` for compatibility; lossy cave/overhang warnings appear
  in the status line.
- `Surface mesh`, which emits exposed voxel faces and omits internal faces.
- `Compressed voxels`, the default lossless contract.
- `Legacy SceneMaker`, retained while its existing consumers move to one of the
  explicit voxel projections.

Templates and Anchor data remain Scene content. Template composition replaces
whole occupied voxel columns at an Anchor and preserves every vertical address.
