#!/usr/bin/env bash
# Pack Gravity.Dsl.MsBuild from the source tree into Playground/local-packages/
# so the playground's PackageReference resolves without hitting nuget.org.
# Re-run this whenever Gravity.Dsl.MsBuild (or any of its ProjectReferences,
# including the bundled reference emitters) changes.
#
# Gravity.Dsl.MsBuild already bundles every reference emitter (C#, JSON Schema,
# PostgreSQL DDL) under its tasks/net9.0/ folder via PrivateAssets="all"
# ProjectReferences, so packing this one project is enough — the playground
# consumer needs no extra <PackageReference>.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PLAYGROUND_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$PLAYGROUND_DIR/.." && pwd)"
LOCAL_FEED="$PLAYGROUND_DIR/local-packages"
CONFIG="${1:-Debug}"
VERSION="${GRAVITY_PLAYGROUND_VERSION:-0.3.0-playground}"

mkdir -p "$LOCAL_FEED"

echo "[pack] packing Gravity.Dsl.MsBuild ($CONFIG, v$VERSION) -> $LOCAL_FEED"
rm -f "$LOCAL_FEED"/Gravity.Dsl.*.nupkg
dotnet pack "$REPO_ROOT/Gravity.Dsl.MsBuild/Gravity.Dsl.MsBuild.csproj" \
  -c "$CONFIG" \
  -p:Version="$VERSION" \
  -o "$LOCAL_FEED" \
  --nologo

echo "[pack] done. Local feed contents:"
ls -1 "$LOCAL_FEED"/*.nupkg
