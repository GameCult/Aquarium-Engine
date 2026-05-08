$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$statePath = Join-Path $repoRoot "artifacts\dev-reload\state.clixml"

if (-not (Test-Path $statePath)) {
    Write-Host "No dev-reload state file exists."
    exit 0
}

$state = Import-Clixml -Path $statePath
if (-not $state.pid) {
    Write-Host "No script-owned Aquarium PID is recorded."
    exit 0
}

$process = Get-Process -Id ([int]$state.pid) -ErrorAction SilentlyContinue
if (-not $process) {
    Write-Host "Recorded Aquarium process $($state.pid) is not running."
    exit 0
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
    Write-Host "PID $($state.pid) no longer belongs to the recorded slot; leaving it alone."
    exit 0
}

Write-Host "Stopping script-owned Aquarium process $($state.pid)."
Stop-Process -Id ([int]$state.pid) -Force
