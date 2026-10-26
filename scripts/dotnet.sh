#!/usr/bin/env bash
set -euo pipefail
GRID_MAPPER_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_CLI_HOME="$GRID_MAPPER_ROOT/.cache/dotnet-cli"
if [[ -x "$GRID_MAPPER_ROOT/.tools/dotnet/dotnet" ]]; then
  export DOTNET_ROOT="$GRID_MAPPER_ROOT/.tools/dotnet"
  exec "$DOTNET_ROOT/dotnet" "$@"
fi
exec dotnet "$@"
