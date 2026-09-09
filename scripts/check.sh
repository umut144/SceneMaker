#!/bin/sh
set -eu

# Routine .NET validation for the SceneMaker workspace.
#
#   ./scripts/check.sh            compiles the Godot assembly and the exporter
#   ./scripts/check.sh --tests    additionally runs the test projects, the
#                                 PolyTools sync preflight, and an export of
#                                 every shipped Workspace into a temporary copy
#
# The default path stays cheap enough to run after every edit. Tests are opt-in
# because building the test assemblies costs another restore-and-compile pass;
# run them before and after a refactor, not after every keystroke.
#
# Mirrors .github/workflows/verify.yml minus the Godot editor boot, which needs
# the Godot binary rather than only its NuGet SDK and stays a manual step.

run_tests=0
for argument in "$@"; do
  case "$argument" in
    --tests) run_tests=1 ;;
    *)
      echo "usage: $0 [--tests]" >&2
      exit 2
      ;;
  esac
done

script_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
project_directory=$(dirname -- "$script_directory")
cd -- "$project_directory"

DOTNET_CLI_TELEMETRY_OPTOUT=1
DOTNET_NOLOGO=1
export DOTNET_CLI_TELEMETRY_OPTOUT DOTNET_NOLOGO

# TreatWarningsAsErrors is set for every project, so one warning fails these.
# Separate invocations keep the Godot application assembly and the headless
# exporter independent of each other.
dotnet build SceneMaker.csproj
dotnet build src/SceneMaker.Cli/SceneMaker.Cli.csproj

if [ "$run_tests" -eq 1 ]; then
  dotnet test tests/SceneMaker.Core.Tests/SceneMaker.Core.Tests.csproj
  dotnet test tests/SceneMaker.Editor.Tests/SceneMaker.Editor.Tests.csproj

  # Shell-level preflight for the PolyTools import sync; no .NET build involved.
  tests/test_sync_polytools_world.sh

  # The shipped Workspaces are the one catalog and the one set of maps that
  # nothing else here looks at: the tests build their own fixtures, and an
  # authored Scene meets the exporter only when somebody exports it by hand. A
  # Workspace Asset added by hand, or a map that drifted past what the export
  # will accept, is exactly the kind of thing that is found far too late.
  #
  # Exported into a copy. A check that rewrote `exports/` in the tree would
  # hand the author a diff they did not make, and one that stopped on a dirty
  # tree would be useless in the middle of the work it is meant to guard.
  export_root=$(mktemp -d)
  trap 'rm -rf -- "$export_root"' EXIT
  for workspace in workspaces/*/; do
    [ -f "$workspace/config.json" ] || continue
    workspace_key=$(basename -- "$workspace")
    staged="$export_root/$workspace_key"
    mkdir -p "$staged/exports"
    cp -- "$workspace/config.json" "$staged/config.json"
    for authored in scenes templates imports; do
      [ -d "$workspace/$authored" ] && cp -R -- "$workspace/$authored" "$staged/$authored"
    done
    # Warnings stay on stderr where the exporter put them; the written paths
    # are inside a temporary directory and are worth nothing to anyone. Not
    # piped into `wc`: a pipeline carries the last command's status, and the
    # one thing this step exists to notice is the exporter refusing.
    written=$(dotnet run --project src/SceneMaker.Cli/SceneMaker.Cli.csproj -- "$staged")
    printf 'Exported %s Scene(s) and Template(s) of %s.\n' \
      "$(printf '%s' "$written" | grep -c . || true)" "$workspace_key"
  done
fi
