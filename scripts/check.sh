#!/bin/sh
set -eu

# Routine .NET validation for the SceneMaker workspace.
#
#   ./scripts/check.sh            compiles the Godot assembly and the exporter
#   ./scripts/check.sh --tests    additionally runs the test projects
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
fi
