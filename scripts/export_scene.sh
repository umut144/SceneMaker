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
