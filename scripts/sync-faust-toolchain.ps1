param(
    [string]$SourceRoot,
    [string]$DestinationRoot
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
    $SourceRoot = if ($env:AQUARIUM_FAUST_HOME) { $env:AQUARIUM_FAUST_HOME } else { "C:\Program Files\Faust" }
}

if ([string]::IsNullOrWhiteSpace($DestinationRoot)) {
    $repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
    $DestinationRoot = Join-Path $repoRoot "artifacts\faust\win-x64"
}

$source = Resolve-Path $SourceRoot
$destination = $DestinationRoot
$sourceLib = Join-Path $source "lib\faust.dll"
$sourceShare = Join-Path $source "share\faust"
$sourceBin = Join-Path $source "bin"

if (-not (Test-Path -LiteralPath $sourceLib)) {
    throw "Faust DLL not found at $sourceLib"
}

if (-not (Test-Path -LiteralPath $sourceShare)) {
    throw "Faust libraries not found at $sourceShare"
}

if (Test-Path -LiteralPath $destination) {
    Remove-Item -LiteralPath $destination -Recurse -Force
}

New-Item -ItemType Directory -Force -Path (Join-Path $destination "lib") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $destination "bin") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $destination "share\faust") | Out-Null

Copy-Item -LiteralPath $sourceLib -Destination (Join-Path $destination "lib\faust.dll") -Force

if (Test-Path -LiteralPath $sourceBin) {
    Get-ChildItem -LiteralPath $sourceBin -File -Filter *.dll |
        Copy-Item -Destination (Join-Path $destination "bin") -Force
}

Get-ChildItem -LiteralPath $sourceShare -File -Filter *.lib |
    Copy-Item -Destination (Join-Path $destination "share\faust") -Force

$version = & (Join-Path $sourceBin "faust.exe") --version 2>$null | Select-Object -First 1
if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($version)) {
    Set-Content -Path (Join-Path $destination "VERSION.txt") -Value $version -Encoding UTF8
}

Write-Host "Faust toolchain synchronized:"
Write-Host "  Source:      $source"
Write-Host "  Destination: $destination"
