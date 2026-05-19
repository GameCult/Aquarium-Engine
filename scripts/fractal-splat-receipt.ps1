param(
    [int]$Splats = 2000000,
    [int]$Warmup = 30,
    [int]$Frames = 120,
    [int]$Depth = 8,
    [int]$Candidates = 2,
    [int]$ReservoirUpdates = 50000,
    [int]$ReadbackSplats = 64
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
dotnet run --project (Join-Path $repoRoot "tools\Aquarium.Fractal.Receipt\Aquarium.Fractal.Receipt.csproj") -c Release -- `
    --splats $Splats `
    --warmup $Warmup `
    --frames $Frames `
    --depth $Depth `
    --candidates $Candidates `
    --reservoir-updates $ReservoirUpdates `
    --readback-splats $ReadbackSplats
