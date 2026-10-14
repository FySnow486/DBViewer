#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet publish DBViewer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o artifacts/windows-x64
