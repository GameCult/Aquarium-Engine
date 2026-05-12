param(
    [switch]$Headless,
    [switch]$NoStop,
    [switch]$BuildOnly,
    [switch]$Reopen,
    [int]$RetainSlots = 12,
    [int]$StartupTimeoutSeconds = 5,
    [int]$HeadlessTimeoutSeconds = 30
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "src\Aquarium.Engine\Aquarium.Engine.csproj"
$liveProjectPath = Join-Path $repoRoot "src\Aquarium.Epiphany\Aquarium.Epiphany.csproj"
$devRoot = Join-Path $repoRoot "artifacts\dev-reload"
$slotRoot = Join-Path $devRoot "slots"
$visibleStatePath = Join-Path $devRoot "state.clixml"
$headlessStatePath = Join-Path $devRoot "headless-state.clixml"
$statePath = if ($Headless) { $headlessStatePath } else { $visibleStatePath }
$buildStatePath = Join-Path $devRoot "last-build.clixml"
$liveReloadPointerPath = Join-Path $devRoot "live-current.txt"
$cultCacheDirectory = if ($Headless) { "headless-cultcache" } else { "cultcache" }
$cultCachePath = Join-Path $devRoot "$cultCacheDirectory\aquarium-client.msgpack"
$shaderSourcePath = Join-Path $repoRoot "src\Aquarium.Engine\Render\Shaders\Aquarium.hlsl"
$logName = if ($Headless) { "headless" } else { "latest" }
$stdoutLogPath = Join-Path $devRoot "$logName.out.log"
$stderrLogPath = Join-Path $devRoot "$logName.err.log"

New-Item -ItemType Directory -Force -Path $slotRoot | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path $cultCachePath) | Out-Null

if ($Reopen -and $Headless) {
    throw "-Reopen is for the visible dev window; do not combine it with -Headless."
}

function Stop-PreviousOwnedProcess {
    if ($NoStop -or -not (Test-Path $statePath)) {
        return
    }

    $state = Import-Clixml -Path $statePath
    if (-not ($state.PSObject.Properties.Name -contains "pid")) {
        return
    }

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

function Wait-ForHeadlessAquarium {
    param(
        [System.Diagnostics.Process]$Process
    )

    $deadline = (Get-Date).AddSeconds([Math]::Max(1, $HeadlessTimeoutSeconds))
    while ((Get-Date) -lt $deadline) {
        $Process.Refresh()
        if ($Process.HasExited) {
            $Process.WaitForExit()
            $exitCode = [int]$Process.ExitCode
            if ($exitCode -ne 0) {
                throw "Headless Aquarium exited with code $exitCode.`n$(Get-StderrTail)"
            }

            return
        }

        Start-Sleep -Milliseconds 100
    }

    try {
        Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
    }
    catch {
        # Best effort cleanup before surfacing the broken smoke test.
    }

    throw "Headless Aquarium did not exit within $HeadlessTimeoutSeconds seconds; killed PID $($Process.Id).`n$(Get-StderrTail)"
}

function Start-AquariumProcess {
    param(
        [string]$SlotPath,
        [string]$LiveAssemblyPath,
        [bool]$RunHeadless
    )

    $exePath = Join-Path $SlotPath "Aquarium.Engine.exe"
    if (-not (Test-Path $exePath)) {
        throw "Expected apphost does not exist: $exePath"
    }

    if (-not (Test-Path $LiveAssemblyPath)) {
        throw "Expected client runtime assembly does not exist: $LiveAssemblyPath"
    }

    Set-Content -Path $liveReloadPointerPath -Value $LiveAssemblyPath -Encoding UTF8

    $arguments = @(
        "--cache", $cultCachePath,
        "--shader-source", $shaderSourcePath,
        "--client-assembly", $LiveAssemblyPath,
        "--client-reload-pointer", $liveReloadPointerPath
    )
    if ($RunHeadless) {
        $arguments += "--headless"
    }

    $startProcessParameters = @{
        FilePath = $exePath
        ArgumentList = $arguments
        WorkingDirectory = $SlotPath
        RedirectStandardOutput = $stdoutLogPath
        RedirectStandardError = $stderrLogPath
        PassThru = $true
    }

    if ($RunHeadless) {
        $startProcessParameters.WindowStyle = "Hidden"
    }

    return Start-Process @startProcessParameters
}

function Reopen-VisibleProcess {
    if (-not (Test-Path $visibleStatePath)) {
        throw "No visible dev-reload state file exists. Run scripts\dev-reload.ps1 once to create a launch slot."
    }

    $state = Import-Clixml -Path $visibleStatePath
    if (-not $state.slot) {
        throw "Visible dev-reload state does not record a slot."
    }

    if ($state.PSObject.Properties.Name -contains "pid" -and $state.pid) {
        $existing = Get-Process -Id ([int]$state.pid) -ErrorAction SilentlyContinue
        if ($existing) {
            Write-Host "Visible Aquarium process $($state.pid) is already running."
            return
        }
    }

    $slotPath = [string]$state.slot
    $liveAssemblyPath = Join-Path $slotPath "Aquarium.Epiphany.dll"
    if ($state.PSObject.Properties.Name -contains "liveAssembly" -and $state.liveAssembly) {
        $liveAssemblyPath = [string]$state.liveAssembly
    }

    $process = Start-AquariumProcess -SlotPath $slotPath -LiveAssemblyPath $liveAssemblyPath -RunHeadless:$false
    Wait-ForStartedAquarium -Process $process -ExpectWindow:$true

    @{
        slot = $slotPath
        builtAt = $state.builtAt
        reopenedAt = (Get-Date).ToString("o")
        pid = $process.Id
        liveAssembly = $liveAssemblyPath
        headless = $false
        stdoutLog = $stdoutLogPath
        stderrLog = $stderrLogPath
    } | Export-Clixml -Path $visibleStatePath

    Write-Host "Aquarium reopened from last visible slot."
    Write-Host "  PID: $($process.Id)"
    Write-Host "  Slot: $slotPath"
    return
}

if ($Reopen) {
    Reopen-VisibleProcess
    exit 0
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

dotnet build $liveProjectPath `
    -c Debug `
    -o $slotPath
if ($LASTEXITCODE -ne 0) {
    throw "client runtime build failed with exit code $LASTEXITCODE."
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
$liveAssemblyPath = Join-Path $slotPath "Aquarium.Epiphany.dll"
if (-not (Test-Path $exePath)) {
    throw "Expected apphost was not produced: $exePath"
}

if (-not (Test-Path $liveAssemblyPath)) {
    throw "Expected client runtime assembly was not produced: $liveAssemblyPath"
}

$process = Start-AquariumProcess -SlotPath $slotPath -LiveAssemblyPath $liveAssemblyPath -RunHeadless:$Headless
if ($Headless) {
    Wait-ForHeadlessAquarium -Process $process

    @{
        slot = $slotPath
        builtAt = (Get-Date).ToString("o")
        liveAssembly = $liveAssemblyPath
        headless = $true
        stdoutLog = $stdoutLogPath
        stderrLog = $stderrLogPath
    } | Export-Clixml -Path $statePath

    Write-Host "Headless Aquarium completed from disposable slot."
    Write-Host "  Slot: $slotPath"
    Write-Host "  CultCache: $cultCachePath"
    Write-Host "  Stdout: $stdoutLogPath"
    Write-Host "  Stderr: $stderrLogPath"
    Remove-OldSlots
    exit 0
}

Wait-ForStartedAquarium -Process $process -ExpectWindow:$true

@{
    slot = $slotPath
    builtAt = (Get-Date).ToString("o")
    pid = $process.Id
    liveAssembly = $liveAssemblyPath
    headless = [bool]$Headless
    stdoutLog = $stdoutLogPath
    stderrLog = $stderrLogPath
} | Export-Clixml -Path $statePath

Write-Host "Aquarium running from disposable slot."
Write-Host "  PID: $($process.Id)"
Write-Host "  CultCache: $cultCachePath"
Write-Host "  Stdout: $stdoutLogPath"
Write-Host "  Stderr: $stderrLogPath"
Write-Host "Run this script again to replace only this script-owned process."
if (-not $Headless) {
    Write-Host "Run with -Reopen to reopen this visible slot without rebuilding."
}

Remove-OldSlots
