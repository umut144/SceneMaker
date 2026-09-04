#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "$0")/.." && pwd)"
source_world_dir="${POLYTOOLS_WORLD_DIR:-$project_root/../PolyTools/worlds/world01}"
workspace_dir="${SCENEMAKER_WORKSPACE_DIR:-$project_root/workspaces/world01}"
source_catalog="$source_world_dir/catalog.json"
config_path="$workspace_dir/config.json"
import_parent="$workspace_dir/imports"
destination_dir="$import_parent/polytools"
current_manifest_schema=16

cleanup() {
  local status=$?
  trap - EXIT
  if ((status != 0)) && [[ "${published_import:-0}" == 1 ]]; then
    rm -rf -- "$destination_dir"
    if [[ -e "$backup_dir/polytools" ]]; then
      mv "$backup_dir/polytools" "$destination_dir"
    fi
  fi
  [[ -z "${staging_dir:-}" ]] || rm -rf -- "$staging_dir"
  [[ -z "${backup_dir:-}" ]] || rm -rf -- "$backup_dir"
  if ((status == 0)); then
    printf '%s\n' 'POLYTOOLS -> SCENEMAKER SYNC SUCCESS'
  else
    printf '%s\n' 'POLYTOOLS -> SCENEMAKER SYNC FAILED' >&2
  fi
  exit "$status"
}
trap cleanup EXIT

if ! command -v jq >/dev/null 2>&1; then
  printf '%s\n' 'ERROR: jq is required to validate PolyTools exports.' >&2
  exit 1
fi
if [[ ! -f "$source_catalog" ]]; then
  printf 'ERROR: PolyTools catalog not found: %s\n' "$source_catalog" >&2
  exit 1
fi
if [[ ! -f "$config_path" ]]; then
  printf 'ERROR: SceneMaker workspace config not found: %s\n' "$config_path" >&2
  exit 1
fi

if ! jq -e '
  .schema_version == 1
  and (.world_key | type == "string" and length > 0)
  and (.world_name | type == "string")
  and (.assets | type == "array" and length > 0)
  and all(.assets[];
    (.asset_key | type == "string" and length > 0)
    and (.display_name | type == "string" and length > 0)
    and (.asset_type | type == "string")
    and (.runtime_package == ("PolyToolsRuntimeExports/" + .asset_key + "/manifest.json"))
    and (.asset_type == "character" or .asset_type == "props"
      or .asset_type == "weapons" or .asset_type == "terrain"
      or .asset_type == "icons"
      or .asset_type == "symbols")
  )
  and (([.assets[].asset_key] | unique | length) == ([.assets[].asset_key] | length))
' "$source_catalog" >/dev/null; then
  printf 'ERROR: invalid PolyTools world catalog: %s\n' "$source_catalog" >&2
  exit 1
fi

world_key="$(jq -r '.world_key' "$source_catalog")"
if ! jq -e --arg world "$world_key" '
  .format == "scene_maker_workspace"
  and .version == 8
  and .workspace_key == $world
  and (.grid.terrain_cell_meters | type == "number" and . > 0)
  and (.grid.authoring_pixels_per_meter | type == "number" and . > 0)
  and (.grid.game_pixels_per_meter | type == "number" and . > 0)
  and (.grid.water_cell_meters | type == "number" and . > 0)
  and (.grid.elevation_quantum_meters | type == "number" and . > 0)
  and (.assets | type == "array")
  and all(.assets[];
    (.asset_key | type == "string" and length > 0)
    and (.display_name | type == "string" and length > 0)
    and (.role == "terrain" or .role == "placement")
    and (.color | type == "string" and test("^#[0-9A-Fa-f]{6}$"))
    and if .role == "terrain" then
      (.surface | type == "string" and length > 0)
      and (.authoring == "cells" or .authoring == "curve")
    else
      (has("surface") | not) and (has("authoring") | not)
    end)
  and (([.assets[].asset_key] | unique | length) == ([.assets[].asset_key] | length))
' "$config_path" >/dev/null; then
  printf 'ERROR: SceneMaker config must be a valid version 8 catalog for PolyTools world %s.\n' "$world_key" >&2
  exit 1
fi
if ! jq -e --slurpfile catalog "$source_catalog" '
  . as $config
  | all($config.assets[];
      .role != "placement"
      or (.asset_key as $key
        | any($catalog[0].assets[]; .asset_key == $key)))
' "$config_path" >/dev/null; then
  printf '%s\n' 'ERROR: every SceneMaker Placement needs matching PolyTools geometry.' >&2
  exit 1
fi

mkdir -p "$import_parent"
staging_dir="$(mktemp -d "$import_parent/.polytools-staging.XXXXXX")"
backup_dir="$(mktemp -d "$import_parent/.polytools-backup.XXXXXX")"
published_import=0

while IFS=$'\t' read -r asset_key asset_type runtime_package; do
  [[ -n "$asset_key" ]] || continue
  manifest_path="$source_world_dir/$runtime_package"
  if [[ ! -f "$manifest_path" ]]; then
    printf 'ERROR: missing PolyTools manifest: %s\n' "$manifest_path" >&2
    exit 1
  fi
  if ! jq -e \
    --arg key "$asset_key" \
    --arg type "$asset_type" \
    --argjson current_schema "$current_manifest_schema" '
      . as $manifest
      | .schema_version == $current_schema
      and .asset_key == $key
      and .asset_type == $type
      and (.asset_pivot | type == "array" and length == 2
        and all(.[]; type == "number" and isfinite))
      and (.components | type == "array" and length > 0)
      and all(.components[];
        (.component_id | type == "string" and length > 0)
        and (.local_transform.position | type == "array" and length == 2
          and all(.[]; type == "number" and isfinite))
        and (.local_transform.rotation_radians | type == "number" and isfinite)
        and (.local_transform.scale | type == "array" and length == 2
          and all(.[]; type == "number" and isfinite))
      )
      and (([.components[].component_id] | unique | length)
        == ([.components[].component_id] | length))
      and (.regions | type == "array")
      and (([.regions[].region_id] | unique | length)
        == ([.regions[].region_id] | length))
      and all(.regions[];
        . as $region
        | ($manifest.components
          | map(select(.component_id == $region.source_component_id))) as $sources
        | (.region_id | type == "string" and length > 0)
        and (.name | type == "string" and test("^[a-z][a-z0-9]*(?:_[a-z0-9]+)*$"))
        and (.role == "attack" or .role == "hurt" or .role == "collision")
        and (.geometry_source == "authored" or .geometry_source == "component")
        and (.source_component_id | type == "string" and length > 0)
        and ($sources | length == 1)
        and if .geometry_source == "authored" then
          (.vertices | type == "array" and length > 0
            and all(.[]; type == "array" and length == 2
              and all(.[]; type == "number" and isfinite)))
          and (.indices | type == "array" and length > 0 and length % 3 == 0
            and all(.[]; type == "number" and isfinite and floor == .
              and . >= 0 and . < ($region.vertices | length)))
        else
          (has("vertices") | not)
          and (has("indices") | not)
          and ($sources[0].kind != "asset_reference")
          and (($sources[0].mesh | type == "object")
            or ($sources[0].closed_region_mesh | type == "object"))
        end
      )
    ' "$manifest_path" >/dev/null; then
    printf 'ERROR: invalid PolyTools manifest: %s\n' "$manifest_path" >&2
    exit 1
  fi
  destination="$staging_dir/$runtime_package"
  mkdir -p "$(dirname "$destination")"
  cp "$manifest_path" "$destination"
done < <(jq -r '.assets[] | [.asset_key, .asset_type, .runtime_package] | @tsv' "$source_catalog")

cp "$source_catalog" "$staging_dir/catalog.json"

if [[ -e "$destination_dir" ]]; then
  mv "$destination_dir" "$backup_dir/polytools"
fi
mv "$staging_dir" "$destination_dir"
published_import=1
