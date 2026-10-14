#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

version="${1:-v0.1.0}"
if [[ ! "$version" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Usage: bash scripts/build-release.sh [vX.Y.Z]" >&2
  exit 2
fi

for rid in win-x64 linux-x64 osx-arm64 osx-x64; do
  output="artifacts/release/$rid"
  rm -rf "$output"
  dotnet publish DBViewer.csproj -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:PublishTrimmed=false -p:NuGetAudit=false -o "$output"
done

python3 scripts/package-release.py "$version"
