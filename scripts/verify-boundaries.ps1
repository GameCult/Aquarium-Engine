$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$engineRoot = Join-Path $repoRoot "src\Aquarium.Engine"

$engineEpiphanyReferences = rg -n -S -- "Aquarium\.Epiphany" $engineRoot
if ($LASTEXITCODE -eq 0) {
    throw "Aquarium.Engine must not reference Aquarium.Epiphany:`n$engineEpiphanyReferences"
}
elseif ($LASTEXITCODE -ne 1) {
    throw "Boundary search for Aquarium.Epiphany references failed."
}

$epiphanyRoleTerms = "Self|Imagination|hibiscus|FaceAgent|HandsAgent|SoulAgent|LifeAgent|CursorBody|CultNet"
$engineRoleReferences = rg -n -S -- $epiphanyRoleTerms $engineRoot
if ($LASTEXITCODE -eq 0) {
    throw "Aquarium.Engine still contains Epiphany/client policy names:`n$engineRoleReferences"
}
elseif ($LASTEXITCODE -ne 1) {
    throw "Boundary search for Epiphany/client policy names failed."
}

Write-Host "Aquarium boundary checks passed."
