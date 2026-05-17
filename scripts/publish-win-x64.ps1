param(
    [switch]$Zip,
    [switch]$NoSelfContained,
    [switch]$Clean,
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "src\Aquarium.Engine\Aquarium.Engine.csproj"
$liveProjectPath = Join-Path $repoRoot "src\Aquarium.Epiphany\Aquarium.Epiphany.csproj"
$publishRoot = if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    Join-Path $repoRoot "artifacts\publish"
}
else {
    $OutputRoot
}
$publishPath = Join-Path $publishRoot $Runtime
$zipPath = Join-Path $publishRoot "EpiphanyAquariumEngine-$Runtime.zip"

if ($Clean -and (Test-Path $publishPath)) {
    Remove-Item -LiteralPath $publishPath -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $publishPath | Out-Null

$selfContained = if ($NoSelfContained) { "false" } else { "true" }
$publishArguments = @(
    "publish",
    $projectPath,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", $selfContained,
    "-o", $publishPath,
    "/p:UseAppHost=true"
)

Write-Host "Publishing Aquarium Engine:"
Write-Host "  Project: $projectPath"
Write-Host "  Client:  $liveProjectPath"
Write-Host "  Runtime: $Runtime"
Write-Host "  Output:  $publishPath"
dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

dotnet publish $liveProjectPath -c $Configuration -r $Runtime --self-contained false -o $publishPath
if ($LASTEXITCODE -ne 0) {
    throw "client runtime publish failed with exit code $LASTEXITCODE."
}

& (Join-Path $PSScriptRoot "sync-faust-toolchain.ps1") -DestinationRoot (Join-Path $publishPath "Tools\Faust")

$requiredFiles = @(
    "Aquarium.Engine.exe",
    "Aquarium.Epiphany.dll",
    "Aquarium.Engine.Contracts.dll",
    "AquaSynth.Dsl.dll",
    "Tools\Faust\lib\faust.dll",
    "Tools\Faust\share\faust\stdfaust.lib",
    "Assets\Aquarium-Engine-Icon.ico",
    "Assets\Fonts\Montserrat[wght].ttf",
    "Assets\Fonts\UbuntuSans[wdth,wght].ttf",
    "Render\Shaders\D3D12HeightField.hlsl",
    "Render\Shaders\D3D12SdfCommon.hlsli",
    "Render\Shaders\D3D12SdfProxy.hlsli",
    "Render\Shaders\D3D12SelfBody.hlsl",
    "Render\Shaders\D3D12CursorBody.hlsl"
)

foreach ($relativePath in $requiredFiles) {
    $path = Join-Path $publishPath $relativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Publish output is missing required file: $relativePath"
    }
}

Write-Host "Publish output verified."

if ($Zip) {
    if (Test-Path $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    Compress-Archive -Path (Join-Path $publishPath "*") -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "Zip ready:"
    Write-Host "  $zipPath"
}

Write-Host "Shipping build ready:"
Write-Host "  $publishPath"
