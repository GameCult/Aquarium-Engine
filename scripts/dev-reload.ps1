param(
    [switch]$Headless,
    [switch]$NoStop,
    [switch]$BuildOnly,
    [int]$RetainSlots = 12,
    [int]$StartupTimeoutSeconds = 5
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "src\Aquarium.Engine\Aquarium.Engine.csproj"
$devRoot = Join-Path $repoRoot "artifacts\dev-reload"
$slotRoot = Join-Path $devRoot "slots"
$statePath = Join-Path $devRoot "state.clixml"
$buildStatePath = Join-Path $devRoot "last-build.clixml"
$cultCachePath = Join-Path $devRoot "cultcache\aquarium-client.msgpack"
$shaderSourcePath = Join-Path $repoRoot "src\Aquarium.Engine\Render\Shaders\Aquarium.hlsl"
$stdoutLogPath = Join-Path $devRoot "latest.out.log"
$stderrLogPath = Join-Path $devRoot "latest.err.log"

New-Item -ItemType Directory -Force -Path $slotRoot | Out-Null

function Stop-PreviousOwnedProcess {
    if ($NoStop -or -not (Test-Path $statePath)) {
        return
    }

    $state = Import-Clixml -Path $statePath
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

function Remove-OldSlots {
    if ($RetainSlots -lt 1) {
        return
    }

    $slots = Get-ChildItem -Path $slotRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -Skip $RetainSlots

    foreach ($slot in $slots) {
        try {
            Remove-Item -LiteralPath $slot.FullName -Recurse -Force
        }
        catch {
            Write-Host "Could not remove old slot $($slot.FullName): $($_.Exception.Message)"
        }
    }
}

function Get-StderrTail {
    if (-not (Test-Path $stderrLogPath)) {
        return "<stderr log does not exist>"
    }

    $tail = Get-Content -Path $stderrLogPath -Tail 80 -ErrorAction SilentlyContinue
    if (-not $tail) {
        return "<stderr log is empty>"
    }

    return ($tail -join [Environment]::NewLine)
}

function Wait-ForStartedAquarium {
    param(
        [System.Diagnostics.Process]$Process,
        [bool]$ExpectWindow
    )

    $deadline = (Get-Date).AddSeconds([Math]::Max(1, $StartupTimeoutSeconds))
    while ((Get-Date) -lt $deadline) {
        $Process.Refresh()
        if ($Process.HasExited) {
            throw "Aquarium process exited during startup with code $($Process.ExitCode).`n$(Get-StderrTail)"
        }

        if (-not $ExpectWindow) {
            return
        }

        if ($Process.MainWindowHandle -ne [IntPtr]::Zero) {
            return
        }

        Start-Sleep -Milliseconds 100
    }

    if ($ExpectWindow) {
        try {
            Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
        }
        catch {
            # Best effort cleanup before surfacing the broken reload.
        }

        throw "Aquarium process started but did not open a visible window within $StartupTimeoutSeconds seconds.`n$(Get-StderrTail)"
    }
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$slotName = "$timestamp-$([guid]::NewGuid().ToString("N").Substring(0, 8))"
$slotPath = Join-Path $slotRoot $slotName
New-Item -ItemType Directory -Force -Path $slotPath | Out-Null

Write-Host "Building Aquarium into disposable slot:"
Write-Host "  $slotPath"
dotnet build $projectPath `
    -c Debug `
    -o $slotPath `
    /p:UseAppHost=true
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}

if ($BuildOnly) {
    @{
        slot = $slotPath
        builtAt = (Get-Date).ToString("o")
        stdoutLog = $stdoutLogPath
        stderrLog = $stderrLogPath
    } | Export-Clixml -Path $buildStatePath
    Write-Host "Build-only slot ready."
    Remove-OldSlots
    exit 0
}

Stop-PreviousOwnedProcess

$exePath = Join-Path $slotPath "Aquarium.Engine.exe"
if (-not (Test-Path $exePath)) {
    throw "Expected apphost was not produced: $exePath"
}

$arguments = @("--cache", $cultCachePath, "--shader-source", $shaderSourcePath)
if ($Headless) {
    $arguments += "--headless"
}

$startProcessParameters = @{
    FilePath = $exePath
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
Wait-ForStartedAquarium -Process $process -ExpectWindow:(-not $Headless)

@{
    slot = $slotPath
    builtAt = (Get-Date).ToString("o")
    pid = $process.Id
    stdoutLog = $stdoutLogPath
    stderrLog = $stderrLogPath
} | Export-Clixml -Path $statePath

Write-Host "Aquarium running from disposable slot."
Write-Host "  PID: $($process.Id)"
Write-Host "  CultCache: $cultCachePath"
Write-Host "  Stdout: $stdoutLogPath"
Write-Host "  Stderr: $stderrLogPath"
Write-Host "Run this script again to replace only this script-owned process."

Remove-OldSlots
