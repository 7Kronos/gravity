#!/usr/bin/env bash
# Pack every Gravity.Dsl.* NuGet the playground consumes into
# Playground/local-packages/ so the playground's <PackageReference>s resolve
# without hitting nuget.org. Re-run this whenever any of the packed projects
# (or their ProjectReferences) change.
#
# All three emitter NuGets are packed at the same version so the playground
# can pin one literal value in Playground/Directory.Packages.props.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PLAYGROUND_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$PLAYGROUND_DIR/.." && pwd)"
LOCAL_FEED="$PLAYGROUND_DIR/local-packages"
CONFIG="${1:-Debug}"
VERSION="${GRAVITY_PLAYGROUND_VERSION:-0.2.0-playground}"

#  Gravity.Dsl.MsBuild bundles its own transitive deps via PrivateAssets="all".
#  The pluggable emitter packages do not — they declare their ProjectReferences
#  to Gravity.Dsl.{Ast,Emitter} as ordinary refs, which NuGet promotes to
#  <PackageReference> dependencies in the produced .nupkg. Combined with the
#  NuGet.config's packageSourceMapping (Gravity.Dsl.* restricted to local-packages),
#  those transitive packages MUST also live in the local feed or restore fails
#  with NU1101. So we pack them all here.
PACKAGES=(
  "Gravity.Dsl.Ast"
  "Gravity.Dsl.Compiler"
  "Gravity.Dsl.Emitter"
  "Gravity.Dsl.MsBuild"
  "Gravity.Dsl.Emitter.JsonSchema"
  "Gravity.Dsl.Emitter.PostgresDdl"
)

mkdir -p "$LOCAL_FEED"

for pkg in "${PACKAGES[@]}"; do
  csproj="$REPO_ROOT/$pkg/$pkg.csproj"
  if [ ! -f "$csproj" ]; then
    echo "[pack] ERROR: csproj not found at $csproj" >&2
    exit 1
  fi
  echo "[pack] packing $pkg ($CONFIG, v$VERSION) -> $LOCAL_FEED"
  rm -f "$LOCAL_FEED"/"$pkg".*.nupkg
  dotnet pack "$csproj" \
    -c "$CONFIG" \
    -p:Version="$VERSION" \
    -o "$LOCAL_FEED" \
    --nologo
done

echo "[pack] done. Local feed contents:"
ls -1 "$LOCAL_FEED"/*.nupkg
