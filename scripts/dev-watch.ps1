param(
    [switch]$Headless,
    [switch]$NoInitialLaunch,
    [switch]$ReopenWhenClosed,
    [switch]$Once,
    [string]$ClientProject = "src\Aquarium.Epiphany\Aquarium.Epiphany.csproj",
    [int]$IntervalMilliseconds = 1000,
    [int]$DebounceMilliseconds = 350,
    [int]$RetainSlots = 12,
    [int]$LiveReloadAckTimeoutSeconds = 6
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$reloadScript = Join-Path $PSScriptRoot "dev-reload.ps1"
$liveProjectPath = if ([System.IO.Path]::IsPathRooted($ClientProject)) { $ClientProject } else { Join-Path $repoRoot $ClientProject }
$liveProjectPath = (Resolve-Path $liveProjectPath).Path
$liveAssemblyName = [System.IO.Path]::GetFileNameWithoutExtension($liveProjectPath)
$clientSourceRoot = Split-Path $liveProjectPath
$watchLogPath = Join-Path $repoRoot "artifacts\dev-reload\watch.log"
$liveSlotRoot = Join-Path $repoRoot "artifacts\dev-reload\live-slots"
$liveReloadPointerPath = Join-Path $repoRoot "artifacts\dev-reload\live-current.txt"
$shaderPath = Join-Path $repoRoot "src\Aquarium.Engine\Render\Shaders\Aquarium.hlsl"
$engineShaderRoot = Join-Path $repoRoot "src\Aquarium.Engine\Render\Shaders"
$clientShaderRoot = Join-Path $clientSourceRoot "Shaders"
$visibleStatePath = Join-Path $repoRoot "artifacts\dev-reload\state.clixml"
$headlessStatePath = Join-Path $repoRoot "artifacts\dev-reload\headless-state.clixml"
$ownedProcessStatePath = if ($Headless) { $headlessStatePath } else { $visibleStatePath }

New-Item -ItemType Directory -Force -Path (Split-Path $watchLogPath) | Out-Null
New-Item -ItemType Directory -Force -Path $liveSlotRoot | Out-Null

if ($Headless -and $ReopenWhenClosed) {
    throw "-ReopenWhenClosed is for the visible dev window; do not combine it with -Headless."
}

function Write-WatchLog([string]$message) {
    $line = "$(Get-Date -Format o) $message"
    Write-Host $line
    Add-Content -Path $watchLogPath -Value $line
}

function Get-SourceFiles {
    $roots = @(
        (Join-Path $repoRoot "src"),
        (Join-Path $repoRoot "scripts"),
        (Join-Path $repoRoot "Aquarium.Engine.sln"),
        (Join-Path $repoRoot "global.json")
    )

    foreach ($root in $roots) {
        if (-not (Test-Path $root)) {
            continue
        }

        $item = Get-Item -LiteralPath $root
        if (-not $item.PSIsContainer) {
            $item
            continue
        }

        Get-ChildItem -LiteralPath $item.FullName -Recurse -File |
            Where-Object {
                $_.FullName -notmatch "\\bin\\" -and
                $_.FullName -notmatch "\\obj\\" -and
                $_.FullName -notmatch "\\artifacts\\" -and
                (
                    $_.Extension -in @(".cs", ".csproj", ".json", ".sln", ".ps1") -or
                    $_.FullName -like (Join-Path $repoRoot "src\Aquarium.Engine\Assets\*")
                )
            }
    }
}

function Get-ShaderFiles {
    foreach ($root in @($engineShaderRoot, $clientShaderRoot)) {
        if (-not (Test-Path $root)) {
            continue
        }

        Get-ChildItem -LiteralPath $root -Recurse -File |
            Where-Object { $_.Extension -in @(".hlsl", ".hlsli") }
    }
}

function Get-LiveFiles {
    Get-SourceFiles | Where-Object {
        $_.FullName -like (Join-Path $clientSourceRoot "*")
    }
}

function Get-RestartFiles {
    Get-SourceFiles | Where-Object {
        $_.FullName -notlike (Join-Path $clientSourceRoot "*")
    }
}

function Get-Fingerprint($files) {
    $fingerprintInput = $files |
        Sort-Object FullName |
        ForEach-Object {
            "$($_.FullName)|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)"
        }

    $text = [string]::Join("`n", $fingerprintInput)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha.ComputeHash($bytes)
        return -join ($hashBytes | ForEach-Object { $_.ToString("x2") })
    }
    finally {
        $sha.Dispose()
    }
}

function Get-Hash([string]$text) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha.ComputeHash($bytes)
        return -join ($hashBytes | ForEach-Object { $_.ToString("x2") })
    }
    finally {
        $sha.Dispose()
    }
}

function Get-AquariumFrameContractText {
    if (-not (Test-Path $shaderPath)) {
        return ""
    }

    $text = Get-Content -Raw -LiteralPath $shaderPath
    $match = [regex]::Match(
        $text,
        "cbuffer\s+AquariumFrame\s*:\s*register\(b0\)\s*\{(?<body>.*?)\};",
        [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $match.Success) {
        return ""
    }

    return $match.Value
}

function Get-SourceFingerprint {
    $fileFingerprint = Get-Fingerprint (Get-SourceFiles)
    $contractFingerprint = Get-Hash (Get-AquariumFrameContractText)
    Get-Hash "$fileFingerprint`nAquariumFrame:$contractFingerprint"
}

function Get-ShaderFingerprint {
    Get-Fingerprint (Get-ShaderFiles)
}

function Get-AllFingerprint {
    $sourceFingerprint = Get-SourceFingerprint
    $shaderFingerprint = Get-ShaderFingerprint
    Get-Hash "$sourceFingerprint`nShaders:$shaderFingerprint"
}

function Get-LiveFingerprint {
    Get-Fingerprint (Get-LiveFiles)
}

function Get-RestartFingerprint {
    $fileFingerprint = Get-Fingerprint (Get-RestartFiles)
    $contractFingerprint = Get-Hash (Get-AquariumFrameContractText)
    Get-Hash "$fileFingerprint`nAquariumFrame:$contractFingerprint"
}

function Wait-StableFingerprints {
    $first = @{
        all = Get-AllFingerprint
        live = Get-LiveFingerprint
        restart = Get-RestartFingerprint
        shaders = Get-ShaderFingerprint
    }
    Start-Sleep -Milliseconds $DebounceMilliseconds
    $second = @{
        all = Get-AllFingerprint
        live = Get-LiveFingerprint
        restart = Get-RestartFingerprint
        shaders = Get-ShaderFingerprint
    }

    while ($first.all -ne $second.all) {
        $first = $second
        Start-Sleep -Milliseconds $DebounceMilliseconds
        $second = @{
            all = Get-AllFingerprint
            live = Get-LiveFingerprint
            restart = Get-RestartFingerprint
            shaders = Get-ShaderFingerprint
        }
    }

    return $second
}

function Invoke-Reload {
    param([string]$reason)

    Write-WatchLog "Reload requested: $reason"

    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $reloadScript,
        "-ClientProject", $liveProjectPath,
        "-RetainSlots", "$RetainSlots"
    )

    if ($Headless) {
        $arguments += "-Headless"
    }

    Push-Location $repoRoot
    try {
        & powershell @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "dev-reload failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

function Get-OwnedProcessState {
    if (-not (Test-Path $ownedProcessStatePath)) {
        return $null
    }

    try {
        return Import-Clixml -Path $ownedProcessStatePath
    }
    catch {
        Write-WatchLog "Could not read script-owned process state. $($_.Exception.Message)"
        return $null
    }
}

function Test-RecordedProcessRunning {
    $state = Get-OwnedProcessState
    if (-not $state -or -not $state.pid -or -not $state.slot) {
        return $false
    }

    $process = Get-Process -Id ([int]$state.pid) -ErrorAction SilentlyContinue
    if (-not $process) {
        return $false
    }

    $recordedPath = [string]$state.slot
    $commandLine = $null
    try {
        $commandLine = (Get-CimInstance Win32_Process -Filter "ProcessId = $($process.Id)").CommandLine
    }
    catch {
        $commandLine = $null
    }

    return [bool]($commandLine -and $commandLine -like "*$recordedPath*")
}

function Test-OwnedProcessRunning {
    if ($NoInitialLaunch) {
        return $true
    }

    return Test-RecordedProcessRunning
}

function Invoke-Reopen {
    param([string]$reason)

    if ($Headless) {
        Invoke-Reload $reason
        return
    }

    Write-WatchLog "Reopen requested: $reason"

    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $reloadScript,
        "-Reopen",
        "-ClientProject", $liveProjectPath,
        "-RetainSlots", "$RetainSlots"
    )

    Push-Location $repoRoot
    try {
        & powershell @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "dev-reload -Reopen failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

function Invoke-LiveReload {
    param([string]$reason)

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $liveSlot = Join-Path $liveSlotRoot "$timestamp-$([guid]::NewGuid().ToString("N").Substring(0, 8))"
    New-Item -ItemType Directory -Force -Path $liveSlot | Out-Null

    Write-WatchLog "Live reload requested: $reason"
    dotnet build $liveProjectPath -c Debug -o $liveSlot
    if ($LASTEXITCODE -ne 0) {
        throw "client runtime build failed with exit code $LASTEXITCODE."
    }

    $liveAssemblyPath = Join-Path $liveSlot "$liveAssemblyName.dll"
    if (-not (Test-Path $liveAssemblyPath)) {
        throw "Expected client runtime assembly was not produced: $liveAssemblyPath"
    }

    Set-Content -Path $liveReloadPointerPath -Value $liveAssemblyPath -Encoding UTF8
    Write-WatchLog "Live reload pointer updated: $liveAssemblyPath"
    Wait-LiveReloadAcknowledged $liveAssemblyPath
}

function Copy-ShaderTree {
    param(
        [string]$SourceRoot,
        [string]$DestinationRoot
    )

    if (-not (Test-Path $SourceRoot)) {
        return
    }

    Get-ChildItem -LiteralPath $SourceRoot -Recurse -File |
        Where-Object { $_.Extension -in @(".hlsl", ".hlsli") } |
        ForEach-Object {
            $relativePath = [System.IO.Path]::GetRelativePath($SourceRoot, $_.FullName)
            $destinationPath = Join-Path $DestinationRoot $relativePath
            $destinationDirectory = Split-Path $destinationPath
            New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
            Copy-Item -LiteralPath $_.FullName -Destination $destinationPath -Force
        }
}

function Read-LogTextAfterLength {
    param(
        [string]$Path,
        [long]$StartLength
    )

    if (-not (Test-Path $Path)) {
        return ""
    }

    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
        if ($StartLength -gt 0 -and $StartLength -lt $stream.Length) {
            $stream.Seek($StartLength, [System.IO.SeekOrigin]::Begin) | Out-Null
        }
        elseif ($StartLength -ge $stream.Length) {
            return ""
        }

        $reader = [System.IO.StreamReader]::new($stream)
        try {
            return $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Wait-ShaderHotReloadAcknowledged {
    param(
        [string]$StdoutLog,
        [string]$StderrLog,
        [long]$StdoutStartLength,
        [long]$StderrStartLength
    )

    $deadline = (Get-Date).AddSeconds([Math]::Max(1, $LiveReloadAckTimeoutSeconds))
    while ((Get-Date) -lt $deadline) {
        $stdoutText = Read-LogTextAfterLength -Path $StdoutLog -StartLength $StdoutStartLength
        if ($stdoutText -match "D3D12 shader hot reload applied") {
            Write-WatchLog "Shader hot reload acknowledged by renderer."
            return
        }

        $stderrText = Read-LogTextAfterLength -Path $StderrLog -StartLength $StderrStartLength
        if ($stderrText -match "D3D12 shader hot reload failed") {
            throw "renderer reported shader hot reload failure. See $StderrLog"
        }

        Start-Sleep -Milliseconds 250
    }

    throw "shader files were copied, but renderer did not acknowledge hot reload within $LiveReloadAckTimeoutSeconds seconds. Check $StdoutLog and $StderrLog."
}

function Invoke-ShaderHotReload {
    param([string]$reason)

    Write-WatchLog "Shader hot reload requested: $reason"

    $state = Get-OwnedProcessState
    if (-not $state -or -not $state.slot) {
        throw "no script-owned process slot is recorded for shader hot reload."
    }

    if (-not (Test-RecordedProcessRunning)) {
        throw "no script-owned process is running to receive shader hot reload."
    }

    $slotPath = [string]$state.slot
    $shaderDestinationRoot = Join-Path $slotPath "Render\Shaders"
    New-Item -ItemType Directory -Force -Path $shaderDestinationRoot | Out-Null

    $stdoutLog = if ($state.PSObject.Properties.Name -contains "stdoutLog" -and $state.stdoutLog) { [string]$state.stdoutLog } else { $stdoutLogPath }
    $stderrLog = if ($state.PSObject.Properties.Name -contains "stderrLog" -and $state.stderrLog) { [string]$state.stderrLog } else { $stderrLogPath }
    $stdoutStartLength = if (Test-Path $stdoutLog) { (Get-Item -LiteralPath $stdoutLog).Length } else { 0 }
    $stderrStartLength = if (Test-Path $stderrLog) { (Get-Item -LiteralPath $stderrLog).Length } else { 0 }

    Copy-ShaderTree -SourceRoot $engineShaderRoot -DestinationRoot $shaderDestinationRoot
    Copy-ShaderTree -SourceRoot $clientShaderRoot -DestinationRoot $shaderDestinationRoot
    Write-WatchLog "Shader files copied into running slot: $shaderDestinationRoot"

    Wait-ShaderHotReloadAcknowledged -StdoutLog $stdoutLog -StderrLog $stderrLog -StdoutStartLength $stdoutStartLength -StderrStartLength $stderrStartLength
}

function Wait-LiveReloadAcknowledged {
    param([string]$liveAssemblyPath)

    if (-not (Test-RecordedProcessRunning)) {
        throw "live reload pointer was updated, but no script-owned process is running to load it."
    }

    $state = Get-OwnedProcessState
    if (-not $state -or -not $state.stdoutLog) {
        Write-WatchLog "Live reload acknowledgement skipped: no running process log is recorded."
        return
    }

    $stdoutLog = [string]$state.stdoutLog
    $stderrLog = if ($state.stderrLog) { [string]$state.stderrLog } else { $null }
    $timeoutSeconds = [Math]::Max(1, $LiveReloadAckTimeoutSeconds)
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    $appliedLine = "Client runtime reload applied: $liveAssemblyPath"

    while ((Get-Date) -lt $deadline) {
        if (Test-Path $stdoutLog) {
            $stdoutTail = (Get-Content -Path $stdoutLog -Tail 120 -ErrorAction SilentlyContinue) -join "`n"
            if ($stdoutTail.Contains($appliedLine)) {
                Write-WatchLog "Live reload acknowledged by host: $liveAssemblyPath"
                return
            }
        }

        if ($stderrLog -and (Test-Path $stderrLog)) {
            $stderrTail = (Get-Content -Path $stderrLog -Tail 120 -ErrorAction SilentlyContinue) -join "`n"
            if ($stderrTail -match "Client runtime reload failed") {
                throw "host reported client runtime reload failure. See $stderrLog"
            }
        }

        Start-Sleep -Milliseconds 250
    }

    throw "live reload pointer was updated, but host did not acknowledge loading $liveAssemblyPath within $timeoutSeconds seconds. Check $stdoutLog and $stderrLog."
}

$lastGoodFingerprint = Wait-StableFingerprints
$lastFailedFingerprint = $null

if (-not $NoInitialLaunch) {
    try {
        Invoke-Reload "initial launch"
        $lastGoodFingerprint = Wait-StableFingerprints
        $lastFailedFingerprint = $null
    }
    catch {
        $lastFailedFingerprint = $lastGoodFingerprint.all
        Write-WatchLog "Initial launch failed; keeping any previous good process alive. $($_.Exception.Message)"
    }
}

Write-WatchLog "Watching Aquarium sources. Press Ctrl+C to stop."

do {
    Start-Sleep -Milliseconds $IntervalMilliseconds
    $currentFingerprint = Wait-StableFingerprints

    if (-not (Test-OwnedProcessRunning)) {
        try {
            if ($ReopenWhenClosed) {
                Invoke-Reopen "script-owned process is not running"
            }
            else {
                Invoke-Reload "script-owned process is not running"
            }

            $lastGoodFingerprint = Wait-StableFingerprints
            $lastFailedFingerprint = $null
            $currentFingerprint = $lastGoodFingerprint
        }
        catch {
            $lastFailedFingerprint = $currentFingerprint.all
            Write-WatchLog "Relaunch failed; watcher remains alive. $($_.Exception.Message)"
        }

        if ($Once) {
            break
        }
    }

    if ($currentFingerprint.all -eq $lastGoodFingerprint.all -or $currentFingerprint.all -eq $lastFailedFingerprint) {
        if ($Once) {
            break
        }

        continue
    }

    try {
        if ($currentFingerprint.restart -eq $lastGoodFingerprint.restart -and
            $currentFingerprint.live -eq $lastGoodFingerprint.live -and
            $currentFingerprint.shaders -ne $lastGoodFingerprint.shaders) {
            Invoke-ShaderHotReload "shader source change"
        }
        elseif ($currentFingerprint.restart -eq $lastGoodFingerprint.restart -and $currentFingerprint.live -ne $lastGoodFingerprint.live) {
            Invoke-LiveReload "live source change"
            if ($currentFingerprint.shaders -ne $lastGoodFingerprint.shaders) {
                Invoke-ShaderHotReload "shader source change accompanying live reload"
            }
        }
        else {
            Invoke-Reload "host/core/content source change"
        }

        $lastGoodFingerprint = $currentFingerprint
        $lastFailedFingerprint = $null
    }
    catch {
        $lastFailedFingerprint = $currentFingerprint.all
        Write-WatchLog "Reload failed; previous good process stays alive. $($_.Exception.Message)"
    }

    if ($Once) {
        break
    }
} while ($true)
