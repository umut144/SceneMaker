#!/usr/bin/env bash
# Validates and exports Scenes out of one Game in a Workspace.
#
#   export_scene.sh                                  every Scene and Template of world01/sandbox
#   export_scene.sh <workspace> <game>               every Scene and Template of that Game
#   export_scene.sh <workspace> <game> <scene-id>    that one Scene or Template
#
# Scene Templates are exported as their own files on purpose: a Template can
# then be replaced between seasons without rewriting the map that uses it.
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
workspace_directory="${1:-$project_root/workspaces/world01}"
game_key="${2:-sandbox}"

dotnet run \
  --project "$project_root/src/SceneMaker.Cli/SceneMaker.Cli.csproj" \
  -- \
  "$workspace_directory" \
  "$game_key" \
  ${3:+"$3"}

# world01 refuses to load an export SceneMaker hasn't synced into its own
# asset tree. sync_scenemaker_world.sh is specific to that one Workspace, and
# a Workspace directory's name is already required to equal its own key, so
# that's also the exact check for whether it applies here.
if [ "$(basename "$workspace_directory")" = world01 ]; then
  world01_root="${WORLD01_REPO_DIR:-$project_root/../../BevyProjects/world01}"
  world01_sync="$world01_root/scripts/sync_scenemaker_world.sh"
  if [ -f "$world01_sync" ]; then
    SCENEMAKER_WORKSPACE="$workspace_directory" bash "$world01_sync"
  else
    printf 'world01 sync skipped: %s not found.\n' "$world01_sync" >&2
  fi
fi
