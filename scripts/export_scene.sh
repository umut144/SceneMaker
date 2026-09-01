#!/usr/bin/env bash
# Validates and exports Scenes out of a Workspace.
#
#   export_scene.sh                        every Scene and Template of world01
#   export_scene.sh <workspace>            every Scene and Template of that Workspace
#   export_scene.sh <workspace> <scene-id> that one Scene or Template
#
# Scene Templates are exported as their own files on purpose: a Template can
# then be replaced between seasons without rewriting the map that uses it.
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
workspace_directory="${1:-$project_root/workspaces/world01}"

dotnet run \
  --project "$project_root/src/SceneMaker.Cli/SceneMaker.Cli.csproj" \
  -- \
  "$workspace_directory" \
  ${2:+"$2"}
