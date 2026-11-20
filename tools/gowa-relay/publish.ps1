# Publishes the webhook relay as one self-contained exe (no .NET needed on the server PC) to tools\gowa-relay\publish\.
# install-gowa-server.ps1 copies it from there. scripts\build_and_publish.bat / publish.ps1 call this too.
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'MochiDock.GowaRelay.csproj'
$output = Join-Path $PSScriptRoot 'publish'
& dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $output --source https://api.nuget.org/v3/index.json --verbosity minimal --nologo
if ($LASTEXITCODE -ne 0) { throw "Relay publish failed with exit code $LASTEXITCODE." }
$exe = Join-Path $output 'MochiDock.GowaRelay.exe'
if (-not (Test-Path $exe) -or (Get-Item $exe).Length -eq 0) { throw "Relay exe missing after publish: $exe" }
Write-Host "Relay published: $exe"
