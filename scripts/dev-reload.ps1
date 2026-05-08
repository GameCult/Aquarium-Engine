param(
    [switch]$Headless,
    [switch]$NoStop,
    [switch]$BuildOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "src\Aquarium.Engine\Aquarium.Engine.csproj"
$devRoot = Join-Path $repoRoot "artifacts\dev-reload"
$slotRoot = Join-Path $devRoot "slots"
$statePath = Join-Path $devRoot "state.json"
$stdoutLogPath = Join-Path $devRoot "latest.out.log"
$stderrLogPath = Join-Path $devRoot "latest.err.log"

New-Item -ItemType Directory -Force -Path $slotRoot | Out-Null

function Stop-PreviousOwnedProcess {
    if ($NoStop -or -not (Test-Path $statePath)) {
        return
    }

    $state = Get-Content -Raw -Path $statePath | ConvertFrom-Json
    if (-not $state.pid) {
        return
    }

    $process = Get-Process -Id ([int]$state.pid) -ErrorAction SilentlyContinue
    if (-not $process) {
        return
    }

    $recordedPath = [string]$state.slot
    $commandLine = $null
    try {
        $commandLine = (Get-CimInstance Win32_Process -Filter "ProcessId = $($process.Id)").CommandLine
    }
    catch {
        $commandLine = $null
    }

    if (-not $commandLine -or $commandLine -notlike "*$recordedPath*") {
        Write-Host "Previous PID $($state.pid) no longer belongs to the recorded slot; leaving it alone."
        return
    }

    Write-Host "Stopping previous script-owned Aquarium process $($state.pid)."
    Stop-Process -Id ([int]$state.pid) -Force
}

Stop-PreviousOwnedProcess

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$slotName = "$timestamp-$([guid]::NewGuid().ToString("N").Substring(0, 8))"
$slotPath = Join-Path $slotRoot $slotName
New-Item -ItemType Directory -Force -Path $slotPath | Out-Null

Write-Host "Building Aquarium into disposable slot:"
Write-Host "  $slotPath"
dotnet build $projectPath `
    -c Debug `
    -o $slotPath `
    /p:UseAppHost=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}

if ($BuildOnly) {
    @{
        slot = $slotPath
        builtAt = (Get-Date).ToString("o")
        pid = $null
        stdoutLog = $stdoutLogPath
        stderrLog = $stderrLogPath
    } | ConvertTo-Json | Set-Content -Path $statePath
    Write-Host "Build-only slot ready."
    exit 0
}

$dllPath = Join-Path $slotPath "Aquarium.Engine.dll"
$arguments = @($dllPath)
if ($Headless) {
    $arguments += "--headless"
}

$startProcessParameters = @{
    FilePath = "dotnet"
    ArgumentList = $arguments
    WorkingDirectory = $slotPath
    RedirectStandardOutput = $stdoutLogPath
    RedirectStandardError = $stderrLogPath
    PassThru = $true
}

if ($Headless) {
    $startProcessParameters.WindowStyle = "Hidden"
}

$process = Start-Process @startProcessParameters

@{
    slot = $slotPath
    builtAt = (Get-Date).ToString("o")
    pid = $process.Id
    stdoutLog = $stdoutLogPath
    stderrLog = $stderrLogPath
} | ConvertTo-Json | Set-Content -Path $statePath

Write-Host "Aquarium running from disposable slot."
Write-Host "  PID: $($process.Id)"
Write-Host "  Stdout: $stdoutLogPath"
Write-Host "  Stderr: $stderrLogPath"
Write-Host "Run this script again to replace only this script-owned process."
