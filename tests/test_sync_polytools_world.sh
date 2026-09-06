#!/bin/sh
set -eu

script_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
project_directory=$(dirname -- "$script_directory")
test_root=$(mktemp -d)
trap 'rm -rf -- "$test_root"' EXIT

source_world="$test_root/source/world01"
workspace="$test_root/workspace/world01"
mkdir -p \
  "$source_world/PolyToolsRuntimeExports/tree" \
  "$source_world/PolyToolsRuntimeExports/leaf" \
  "$workspace/imports"

cat >"$source_world/catalog.json" <<'JSON'
{
  "schema_version": 2,
  "world_key": "world01",
  "world_name": "Test World",
  "assets": [
    {
      "asset_key": "tree",
      "display_name": "Tree",
      "asset_type": "terrain",
      "asset_category": "single",
      "runtime_package": "PolyToolsRuntimeExports/tree/manifest.json"
    },
    {
      "asset_key": "broken",
      "display_name": "Broken but unused",
      "asset_type": "character",
      "asset_category": "single",
      "runtime_package": "PolyToolsRuntimeExports/broken/manifest.json"
    },
    {
      "asset_key": "leaf",
      "display_name": "Referenced leaf",
      "asset_type": "items",
      "asset_category": "single",
      "runtime_package": "PolyToolsRuntimeExports/leaf/manifest.json"
    }
  ]
}
JSON

cat >"$workspace/config.json" <<'JSON'
{
  "format": "scene_maker_workspace",
  "version": 11,
  "workspace_key": "world01",
  "grid": {
    "terrain_cell_meters": 1.0,
    "authoring_pixels_per_meter": 32.0,
    "game_pixels_per_meter": 192.0,
    "water_cell_meters": 0.5,
    "elevation_quantum_meters": 0.125
  },
  "assets": [
    {
      "asset_key": "bog",
      "display_name": "Bog",
      "role": "terrain",
      "color": "#123456",
      "surface": "forest",
      "authoring": "cells"
    },
    {
      "asset_key": "tree",
      "display_name": "Oak",
      "role": "placement",
      "color": "#2E7D32"
    }
  ]
}
JSON

manifest="$source_world/PolyToolsRuntimeExports/tree/manifest.json"
cat >"$manifest" <<'JSON'
{
  "schema_version": 19,
  "asset_key": "tree",
  "display_name": "Tree",
  "asset_type": "terrain",
  "asset_category": "single",
  "asset_pivot": [0.0, 0.0],
  "components": [
    {
      "component_id": "body",
      "local_transform": {
        "position": [0.0, 0.0],
        "rotation_radians": 0.0,
        "scale": [1.0, 1.0]
      },
      "mesh": {
        "vertices": [[0.0, 0.0], [1.0, 0.0], [0.0, 1.0]],
        "indices": [0, 1, 2]
      }
    },
    {
      "component_id": "leaf_reference",
      "kind": "asset_reference",
      "source_asset_key": "leaf",
      "local_transform": {
        "position": [0.0, 0.0],
        "rotation_radians": 0.0,
        "scale": [1.0, 1.0]
      }
    }
  ],
  "regions": [
    {
      "region_id": "region_authored",
      "name": "hurt_region",
      "role": "hurt",
      "geometry_source": "authored",
      "source_component_id": "body",
      "vertices": [[0.0, 0.0], [0.5, 0.0], [0.0, 0.5]],
      "indices": [0, 1, 2]
    },
    {
      "region_id": "region_component",
      "name": "collision_region",
      "role": "collision",
      "geometry_source": "component",
      "source_component_id": "body"
    }
  ]
}
JSON

cat >"$source_world/PolyToolsRuntimeExports/leaf/manifest.json" <<'JSON'
{
  "schema_version": 19,
  "asset_key": "leaf",
  "display_name": "Referenced leaf",
  "asset_type": "items",
  "asset_category": "single",
  "asset_pivot": [0.0, 0.0],
  "components": [
    {
      "component_id": "body",
      "local_transform": {
        "position": [0.0, 0.0],
        "rotation_radians": 0.0,
        "scale": [1.0, 1.0]
      },
      "mesh": {
        "vertices": [[0.0, 0.0], [0.25, 0.25]],
        "indices": [0, 1, 1]
      }
    }
  ],
  "regions": []
}
JSON

full_config="$test_root/full-config.json"
cp "$workspace/config.json" "$full_config"
jq '.assets |= map(select(.role == "terrain"))' \
  "$workspace/config.json" >"$test_root/terrain-only-config.json"
mv "$test_root/terrain-only-config.json" "$workspace/config.json"
POLYTOOLS_WORLD_DIR="$source_world" \
SCENEMAKER_WORKSPACE_DIR="$workspace" \
  "$project_directory/scripts/sync_polytools_world.sh"
jq -e '.assets == []' "$workspace/imports/polytools/catalog.json" >/dev/null
test ! -e "$workspace/imports/polytools/PolyToolsRuntimeExports/tree/manifest.json"
mv "$full_config" "$workspace/config.json"

POLYTOOLS_WORLD_DIR="$source_world" \
SCENEMAKER_WORKSPACE_DIR="$workspace" \
  "$project_directory/scripts/sync_polytools_world.sh"

imported_manifest="$workspace/imports/polytools/PolyToolsRuntimeExports/tree/manifest.json"
jq -e '.schema_version == 19 and (.regions | length == 2)' "$imported_manifest" >/dev/null
jq -e '.assets == [
  {
    "asset_key": "bog",
    "display_name": "Bog",
    "role": "terrain",
    "color": "#123456",
    "surface": "forest",
    "authoring": "cells"
  },
  {
    "asset_key": "tree",
    "display_name": "Oak",
    "role": "placement",
    "color": "#2E7D32"
  }
]' "$workspace/config.json" >/dev/null
jq -e '[.assets[].asset_key] == ["tree", "leaf"]' \
  "$workspace/imports/polytools/catalog.json" >/dev/null
test -f "$workspace/imports/polytools/PolyToolsRuntimeExports/leaf/manifest.json"
test ! -e "$workspace/imports/polytools/PolyToolsRuntimeExports/broken/manifest.json"
published_checksum=$(cksum "$imported_manifest")

valid_config="$test_root/valid-config.json"
cp "$workspace/config.json" "$valid_config"

# A Placement may present a surface. Most do not, but a bridge deck is walked
# on and is wood, and has no Terrain underneath it to say so on its behalf.
# `authoring` stays Terrain-only: it says how cells or curves are painted.
jq '.assets[1].surface = "wood"' \
  "$workspace/config.json" >"$test_root/placement-surface.json"
mv "$test_root/placement-surface.json" "$workspace/config.json"
POLYTOOLS_WORLD_DIR="$source_world" \
SCENEMAKER_WORKSPACE_DIR="$workspace" \
  "$project_directory/scripts/sync_polytools_world.sh"
cp "$valid_config" "$workspace/config.json"

jq '.assets[1].authoring = "cells"' \
  "$workspace/config.json" >"$test_root/placement-authoring.json"
mv "$test_root/placement-authoring.json" "$workspace/config.json"
if POLYTOOLS_WORLD_DIR="$source_world" \
  SCENEMAKER_WORKSPACE_DIR="$workspace" \
  "$project_directory/scripts/sync_polytools_world.sh"; then
  printf '%s\n' 'Expected a Placement declaring authoring to fail preflight.' >&2
  exit 1
fi
cp "$valid_config" "$workspace/config.json"

jq '.assets[1].asset_key = "missing"' \
  "$workspace/config.json" >"$test_root/missing-geometry.json"
mv "$test_root/missing-geometry.json" "$workspace/config.json"
if POLYTOOLS_WORLD_DIR="$source_world" \
  SCENEMAKER_WORKSPACE_DIR="$workspace" \
  "$project_directory/scripts/sync_polytools_world.sh"; then
  printf '%s\n' 'Expected a Placement without matching PolyTools geometry to fail preflight.' >&2
  exit 1
fi
test "$(cksum "$imported_manifest")" = "$published_checksum"
mv "$valid_config" "$workspace/config.json"

invalid_manifest="$test_root/invalid-manifest.json"
jq '.regions[1].vertices = [[0, 0], [1, 0], [0, 1]] | .regions[1].indices = [0, 1, 2]' \
  "$manifest" >"$invalid_manifest"
mv "$invalid_manifest" "$manifest"
if POLYTOOLS_WORLD_DIR="$source_world" \
  SCENEMAKER_WORKSPACE_DIR="$workspace" \
  "$project_directory/scripts/sync_polytools_world.sh"; then
  printf '%s\n' 'Expected component-bound Region vertex data to fail preflight.' >&2
  exit 1
fi
test "$(cksum "$imported_manifest")" = "$published_checksum"

jq '.schema_version = 18 | .regions[1] |= del(.vertices, .indices)' \
  "$manifest" >"$invalid_manifest"
mv "$invalid_manifest" "$manifest"
if POLYTOOLS_WORLD_DIR="$source_world" \
  SCENEMAKER_WORKSPACE_DIR="$workspace" \
  "$project_directory/scripts/sync_polytools_world.sh"; then
  printf '%s\n' 'Expected schema 15 to fail preflight.' >&2
  exit 1
fi
test "$(cksum "$imported_manifest")" = "$published_checksum"

printf '%s\n' 'SceneMaker PolyTools sync preflight tests passed.'
