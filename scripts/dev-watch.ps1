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
$liveProjectPath = Join-Path $repoRoot "src\Aquarium.Engine.Live\Aquarium.Engine.Live.csproj"
$watchLogPath = Join-Path $repoRoot "artifacts\dev-reload\watch.log"
$liveSlotRoot = Join-Path $repoRoot "artifacts\dev-reload\live-slots"
$liveReloadPointerPath = Join-Path $repoRoot "artifacts\dev-reload\live-current.txt"
$shaderPath = Join-Path $repoRoot "src\Aquarium.Engine\Render\Shaders\Aquarium.hlsl"

New-Item -ItemType Directory -Force -Path (Split-Path $watchLogPath) | Out-Null
New-Item -ItemType Directory -Force -Path $liveSlotRoot | Out-Null

function Write-WatchLog([string]$message) {
    $line = "$(Get-Date -Format o) $message"
    Write-Host $line
    Add-Content -Path $watchLogPath -Value $line
}

function Get-SourceFiles {
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
                $_.FullName -notmatch "\\artifacts\\" -and
                (
                    $_.Extension -in @(".cs", ".csproj", ".json", ".slnx") -or
                    $_.FullName -like (Join-Path $repoRoot "src\Aquarium.Engine\Assets\*")
                )
            }
    }
}

function Get-LiveFiles {
    Get-SourceFiles | Where-Object {
        $_.FullName -like (Join-Path $repoRoot "src\Aquarium.Engine.Live\*")
    }
}

function Get-RestartFiles {
    Get-SourceFiles | Where-Object {
        $_.FullName -notlike (Join-Path $repoRoot "src\Aquarium.Engine.Live\*")
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
        all = Get-SourceFingerprint
        live = Get-LiveFingerprint
        restart = Get-RestartFingerprint
    }
    Start-Sleep -Milliseconds $DebounceMilliseconds
    $second = @{
        all = Get-SourceFingerprint
        live = Get-LiveFingerprint
        restart = Get-RestartFingerprint
    }

    while ($first.all -ne $second.all) {
        $first = $second
        Start-Sleep -Milliseconds $DebounceMilliseconds
        $second = @{
            all = Get-SourceFingerprint
            live = Get-LiveFingerprint
            restart = Get-RestartFingerprint
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

function Invoke-LiveReload {
    param([string]$reason)

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $liveSlot = Join-Path $liveSlotRoot "$timestamp-$([guid]::NewGuid().ToString("N").Substring(0, 8))"
    New-Item -ItemType Directory -Force -Path $liveSlot | Out-Null

    Write-WatchLog "Live reload requested: $reason"
    dotnet build $liveProjectPath -c Debug -o $liveSlot
    if ($LASTEXITCODE -ne 0) {
        throw "live runtime build failed with exit code $LASTEXITCODE."
    }

    $liveAssemblyPath = Join-Path $liveSlot "Aquarium.Engine.Live.dll"
    if (-not (Test-Path $liveAssemblyPath)) {
        throw "Expected live runtime assembly was not produced: $liveAssemblyPath"
    }

    Set-Content -Path $liveReloadPointerPath -Value $liveAssemblyPath -Encoding UTF8
    Write-WatchLog "Live reload pointer updated: $liveAssemblyPath"
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

    if ($currentFingerprint.all -eq $lastGoodFingerprint.all -or $currentFingerprint.all -eq $lastFailedFingerprint) {
        if ($Once) {
            break
        }

        continue
    }

    try {
        if ($currentFingerprint.restart -eq $lastGoodFingerprint.restart -and $currentFingerprint.live -ne $lastGoodFingerprint.live) {
            Invoke-LiveReload "live source change"
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
