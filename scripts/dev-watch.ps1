param(
    [switch]$Headless,
    [switch]$NoInitialLaunch,
    [switch]$Once,
    [int]$IntervalMilliseconds = 1000,
    [int]$DebounceMilliseconds = 350,
    [int]$RetainSlots = 12
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$reloadScript = Join-Path $PSScriptRoot "dev-reload.ps1"
$watchLogPath = Join-Path $repoRoot "artifacts\dev-reload\watch.log"

New-Item -ItemType Directory -Force -Path (Split-Path $watchLogPath) | Out-Null

function Write-WatchLog([string]$message) {
    $line = "$(Get-Date -Format o) $message"
    Write-Host $line
    Add-Content -Path $watchLogPath -Value $line
}

function Get-WatchedFiles {
    $roots = @(
        (Join-Path $repoRoot "src"),
        (Join-Path $repoRoot "Aquarium.Engine.slnx"),
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
                $_.Extension -in @(".cs", ".csproj", ".hlsl", ".json", ".slnx")
            }
    }
}

function Get-SourceFingerprint {
    $fingerprintInput = Get-WatchedFiles |
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

function Wait-StableFingerprint {
    $first = Get-SourceFingerprint
    Start-Sleep -Milliseconds $DebounceMilliseconds
    $second = Get-SourceFingerprint

    while ($first -ne $second) {
        $first = $second
        Start-Sleep -Milliseconds $DebounceMilliseconds
        $second = Get-SourceFingerprint
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
        "-RetainSlots", "$RetainSlots"
    )

    if ($Headless) {
        $arguments += "-Headless"
    }

    $process = Start-Process `
        -FilePath "powershell" `
        -ArgumentList $arguments `
        -WorkingDirectory $repoRoot `
        -Wait `
        -PassThru `
        -NoNewWindow

    if ($process.ExitCode -ne 0) {
        throw "dev-reload failed with exit code $($process.ExitCode)."
    }
}

$lastGoodFingerprint = Wait-StableFingerprint
$lastFailedFingerprint = $null

if (-not $NoInitialLaunch) {
    try {
        Invoke-Reload "initial launch"
        $lastGoodFingerprint = Wait-StableFingerprint
        $lastFailedFingerprint = $null
    }
    catch {
        $lastFailedFingerprint = $lastGoodFingerprint
        Write-WatchLog "Initial launch failed; keeping any previous good process alive. $($_.Exception.Message)"
    }
}

Write-WatchLog "Watching Aquarium sources. Press Ctrl+C to stop."

do {
    Start-Sleep -Milliseconds $IntervalMilliseconds
    $currentFingerprint = Wait-StableFingerprint

    if ($currentFingerprint -eq $lastGoodFingerprint -or $currentFingerprint -eq $lastFailedFingerprint) {
        if ($Once) {
            break
        }

        continue
    }

    try {
        Invoke-Reload "source change"
        $lastGoodFingerprint = $currentFingerprint
        $lastFailedFingerprint = $null
    }
    catch {
        $lastFailedFingerprint = $currentFingerprint
        Write-WatchLog "Reload failed; previous good process stays alive. $($_.Exception.Message)"
    }

    if ($Once) {
        break
    }
} while ($true)
