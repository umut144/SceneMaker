#!/bin/sh
set -eu

script_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
project_directory=$(dirname -- "$script_directory")
test_root=$(mktemp -d)
trap 'rm -rf -- "$test_root"' EXIT

source_world="$test_root/source/world01"
workspace="$test_root/workspace/world01"
mkdir -p "$source_world/PolyToolsRuntimeExports/tree" "$workspace/imports"

cat >"$source_world/catalog.json" <<'JSON'
{
  "schema_version": 1,
  "world_key": "world01",
  "world_name": "Test World",
  "assets": [
    {
      "asset_key": "tree",
      "display_name": "Tree",
      "asset_type": "terrain",
      "runtime_package": "PolyToolsRuntimeExports/tree/manifest.json"
    }
  ]
}
JSON

cat >"$workspace/config.json" <<'JSON'
{
  "format": "scene_maker_workspace",
  "version": 8,
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
    }
  ]
}
JSON

manifest="$source_world/PolyToolsRuntimeExports/tree/manifest.json"
cat >"$manifest" <<'JSON'
{
  "schema_version": 16,
  "asset_key": "tree",
  "display_name": "Tree",
  "asset_type": "terrain",
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

POLYTOOLS_WORLD_DIR="$source_world" \
SCENEMAKER_WORKSPACE_DIR="$workspace" \
  "$project_directory/scripts/sync_polytools_world.sh"

imported_manifest="$workspace/imports/polytools/PolyToolsRuntimeExports/tree/manifest.json"
jq -e '.schema_version == 16 and (.regions | length == 2)' "$imported_manifest" >/dev/null
jq -e '.assets == [{
  "asset_key": "bog",
  "display_name": "Bog",
  "role": "terrain",
  "color": "#123456",
  "surface": "forest",
  "authoring": "cells"
}]' "$workspace/config.json" >/dev/null
published_checksum=$(cksum "$imported_manifest")

valid_config="$test_root/valid-config.json"
cp "$workspace/config.json" "$valid_config"
jq '.assets[0].role = "placement" | .assets[0] |= del(.surface, .authoring)' \
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

jq '.schema_version = 15 | .regions[1] |= del(.vertices, .indices)' \
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
