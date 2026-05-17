param(
    [string]$SynthRepo = "E:\Projects\AquaSynth",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$feed = Join-Path $root "packages"
$coreProject = Join-Path $SynthRepo "src\AquaSynth.Core\AquaSynth.Core.csproj"
$faustProject = Join-Path $SynthRepo "src\AquaSynth.Faust\AquaSynth.Faust.csproj"

if (-not (Test-Path $coreProject)) {
    throw "AquaSynth.Core project not found at $coreProject"
}

if (-not (Test-Path $faustProject)) {
    throw "AquaSynth.Faust project not found at $faustProject"
}

New-Item -ItemType Directory -Force -Path $feed | Out-Null
dotnet pack $coreProject -c $Configuration -o $feed
dotnet pack $faustProject -c $Configuration -o $feed

Write-Host "Packed AquaSynth.Core and AquaSynth.Faust into $feed"
Write-Host "If the package version changed, update src\Aquarium.Engine\Aquarium.Engine.csproj to match."
