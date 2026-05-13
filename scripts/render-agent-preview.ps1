param(
    [string]$Agent = "Soul",
    [string]$OutputDirectory,
    [int]$Width = 768,
    [int]$Height = 768,
    [int]$FramesPerView = 18,
    [float]$TimeSeconds = 18.0
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "src\Aquarium.Epiphany.AgentPreview\Aquarium.Epiphany.AgentPreview.csproj"
$previewRoot = Join-Path $repoRoot "artifacts\agent-preview"
$slotPath = Join-Path $previewRoot ("slot-" + (Get-Date -Format "yyyyMMdd-HHmmss") + "-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot ("artifacts\agent-previews\$Agent\" + (Get-Date -Format "yyyyMMdd-HHmmss"))
}

New-Item -ItemType Directory -Force -Path $slotPath | Out-Null
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

Write-Host "Building isolated preview renderer:"
Write-Host "  $slotPath"
dotnet build $projectPath -c Debug -o $slotPath /p:UseAppHost=true
if ($LASTEXITCODE -ne 0) {
    throw "preview build failed with exit code $LASTEXITCODE."
}

$exePath = Join-Path $slotPath "Aquarium.Epiphany.AgentPreview.exe"
if (-not (Test-Path $exePath)) {
    throw "Expected preview executable was not produced: $exePath"
}

& $exePath `
    --agent $Agent `
    --output $OutputDirectory `
    --width $Width `
    --height $Height `
    --frames $FramesPerView `
    --time $TimeSeconds
if ($LASTEXITCODE -ne 0) {
    throw "preview render failed with exit code $LASTEXITCODE."
}

Write-Host "Preview frames written:"
Write-Host "  $OutputDirectory"
