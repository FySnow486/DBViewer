$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")
dotnet publish DBViewer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o artifacts/windows-x64
if ($LASTEXITCODE -ne 0) { throw "Windows publish failed" }
Compress-Archive -Path artifacts/windows-x64/* -DestinationPath artifacts/DBViewer-windows-x64.zip -Force
