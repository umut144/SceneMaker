#!/bin/sh
set -eu

script_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
project_directory=$(dirname -- "$script_directory")
scenemaker_godot_bin=${SCENEMAKER_GODOT_BIN:-/Applications/Godot_mono.app/Contents/MacOS/Godot}

output=$(
  "$scenemaker_godot_bin" \
    --headless \
    --path "$project_directory" \
    --quit-after 2 \
    -- \
    --ignore-recent-session \
    2>&1
)
printf '%s\n' "$output"
printf '%s\n' "$output" | grep -Fq "SceneMaker Slice 5 Portal transition authoring ready"
if printf '%s\n' "$output" | grep -E "WARNING:|ERROR:" >/dev/null; then
  exit 1
fi
