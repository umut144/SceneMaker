#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
workspace_directory="${1:-$project_root/workspaces/world01}"
scene_id="${2:-world01}"

dotnet run \
  --project "$project_root/src/SceneMaker.Cli/SceneMaker.Cli.csproj" \
  -- \
  "$workspace_directory" \
  "$scene_id"
