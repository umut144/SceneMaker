#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "$0")/.." && pwd)"
source_world_dir="${POLYTOOLS_WORLD_DIR:-$project_root/../PolyTools/worlds/world01}"
workspace_dir="${SCENEMAKER_WORKSPACE_DIR:-$project_root/workspaces/world01}"
source_catalog="$source_world_dir/catalog.json"
config_path="$workspace_dir/config.json"
import_parent="$workspace_dir/imports"
destination_dir="$import_parent/polytools"
current_manifest_schema=19
current_config_version=11

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
  .schema_version == 2
  and (.world_key | type == "string" and length > 0)
  and (.world_name | type == "string")
  and (.assets | type == "array" and length > 0)
  and all(.assets[];
    (.asset_key | type == "string" and length > 0)
    and (.display_name | type == "string" and length > 0)
    and (.asset_type | type == "string" and length > 0)
    and (.asset_category == "single" or .asset_category == "set" or .asset_category == "palette")
    and (.runtime_package == ("PolyToolsRuntimeExports/" + .asset_key + "/manifest.json"))
  )
  and (([.assets[].asset_key] | unique | length) == ([.assets[].asset_key] | length))
' "$source_catalog" >/dev/null; then
  printf 'ERROR: invalid PolyTools world catalog: %s\n' "$source_catalog" >&2
  exit 1
fi

world_key="$(jq -r '.world_key' "$source_catalog")"
if ! jq -e --arg world "$world_key" --argjson version "$current_config_version" '
  .format == "scene_maker_workspace"
  and .version == $version
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
      # A Placement may present a surface and usually does not: a tree is
      # stood beside, not walked on. A bridge deck is walked on and is wood,
      # and has no Terrain underneath it to say so on its behalf. `authoring`
      # stays Terrain-only - it says how cells or curves are painted, and a
      # Placement is neither.
      (.surface == null or (.surface | type == "string" and length > 0))
      and (has("authoring") | not)
    end)
  and (([.assets[].asset_key] | unique | length) == ([.assets[].asset_key] | length))
' "$config_path" >/dev/null; then
  # Naming the version alone reads as a version mismatch even when the
  # version is right, and that is the likelier case: this gate also refuses a
  # bad grid and a bad Asset entry. Say which file and what was checked.
  printf 'ERROR: %s is not a valid version %s SceneMaker workspace catalog for PolyTools world %s.\n' \
    "$config_path" "$current_config_version" "$world_key" >&2
  printf '%s\n' \
    '       Checked: format, version, workspace_key, grid, and every Asset entry -' \
    '       role, color, a surface and authoring on Terrain, and no authoring on a Placement.' >&2
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

# A variant is reached by choosing it for a Palette, never by naming it in a
# map. SceneMaker cannot check that itself: it requests only its configured
# Placements, so a Palette Manifest never reaches it and Terrain Assets carry
# no PolyTools package at all. Only here is the whole source catalog present,
# and the variants lists are the authority.
variant_keys="$(
  jq -r '.assets[] | select(.asset_category == "palette") | .asset_key' "$source_catalog" \
    | while IFS= read -r palette_key; do
        [[ -z "$palette_key" ]] && continue
        palette_manifest="$source_world_dir/PolyToolsRuntimeExports/$palette_key/manifest.json"
        if [[ ! -f "$palette_manifest" ]]; then
          printf 'ERROR: missing PolyTools Palette manifest: %s\n' "$palette_manifest" >&2
          exit 1
        fi
        jq -r '.variants[]?' "$palette_manifest"
      done
)"
if [[ -n "$variant_keys" ]]; then
  while IFS= read -r variant_key; do
    [[ -z "$variant_key" ]] && continue
    if jq -e --arg key "$variant_key" \
        'any(.assets[]; .asset_key == $key)' "$config_path" >/dev/null; then
      printf 'ERROR: SceneMaker configures %s, which is a Palette variant; configure the Palette instead.\n' \
        "$variant_key" >&2
      exit 1
    fi
  done <<< "$variant_keys"
fi

mkdir -p "$import_parent"
staging_dir="$(mktemp -d "$import_parent/.polytools-staging.XXXXXX")"
backup_dir="$(mktemp -d "$import_parent/.polytools-backup.XXXXXX")"
published_import=0

validate_manifest() {
  local asset_key="$1"
  local asset_type="$2"
  local manifest_path="$3"
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
}

contains_required_asset() {
  local candidate="$1"
  local existing
  for existing in "${required_asset_keys[@]}"; do
    [[ "$existing" != "$candidate" ]] || return 0
  done
  return 1
}

required_asset_keys=()
while IFS= read -r asset_key; do
  [[ -z "$asset_key" ]] || required_asset_keys+=("$asset_key")
done < <(jq -r '.assets[] | select(.role == "placement") | .asset_key' "$config_path")

for ((required_index = 0; required_index < ${#required_asset_keys[@]}; required_index++)); do
  asset_key="${required_asset_keys[$required_index]}"
  entry="$(jq -r --arg key "$asset_key" '
    .assets[] | select(.asset_key == $key)
    | [.asset_key, .asset_type, .runtime_package] | @tsv
  ' "$source_catalog")"
  if [[ -z "$entry" ]]; then
    printf 'ERROR: PolyTools catalog has no geometry entry for Placement %s.\n' "$asset_key" >&2
    exit 1
  fi
  IFS=$'\t' read -r _ asset_type runtime_package <<<"$entry"
  manifest_path="$source_world_dir/$runtime_package"
  validate_manifest "$asset_key" "$asset_type" "$manifest_path"
  destination="$staging_dir/$runtime_package"
  mkdir -p "$(dirname "$destination")"
  cp "$manifest_path" "$destination"

  while IFS= read -r source_asset_key; do
    [[ -z "$source_asset_key" ]] && continue
    if ! contains_required_asset "$source_asset_key"; then
      required_asset_keys+=("$source_asset_key")
    fi
  done < <(jq -r '
    .components[]
    | select(.kind == "asset_reference")
    | .source_asset_key // empty
  ' "$manifest_path")
done

if ((${#required_asset_keys[@]} == 0)); then
  required_asset_keys_json='[]'
else
  required_asset_keys_json="$(
    printf '%s\n' "${required_asset_keys[@]}" | jq -R . | jq -s .
  )"
fi
jq --argjson required "$required_asset_keys_json" '
  .assets |= map(select(.asset_key as $key | $required | index($key)))
' "$source_catalog" >"$staging_dir/catalog.json"

if [[ -e "$destination_dir" ]]; then
  mv "$destination_dir" "$backup_dir/polytools"
fi
mv "$staging_dir" "$destination_dir"
published_import=1
