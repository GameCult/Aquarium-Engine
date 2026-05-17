param(
    [string]$SynthRepo = "E:\Projects\AquaSynth",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$feed = Join-Path $root "packages"
$project = Join-Path $SynthRepo "src\AquaSynth.Dsl\AquaSynth.Dsl.csproj"

if (-not (Test-Path $project)) {
    throw "AquaSynth.Dsl project not found at $project"
}

New-Item -ItemType Directory -Force -Path $feed | Out-Null
dotnet pack $project -c $Configuration -o $feed

Write-Host "Packed AquaSynth.Dsl into $feed"
Write-Host "If the package version changed, update src\Aquarium.Engine\Aquarium.Engine.csproj to match."
