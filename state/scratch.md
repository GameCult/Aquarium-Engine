# Scratch

## Current Slice

Planning and first implementation for the SDF field renderer. The core rule:
Aquarium/Aetheria clouds are local SDF media above, below, around, or enclosing
the camera, never skybox-only decoration. `docs/sdf-field-renderer-plan.md`
captures the architecture. `Aquarium.hlsl` now has a first analytic SDF cloud
prototype integrated along the camera ray before surface composition.

## Hot Context

- Root: `E:\Projects\Aquarium-Engine`
- Upstream: `https://github.com/GameCult/Aquarium-Engine.git`
- Live branch: `main`
- Main verification:
  - `dotnet build Aquarium.Engine.slnx`
  - `.\scripts\dev-reload.ps1 -Headless -RetainSlots 4`
- Prototype histories remain external references. Do not re-import their source
  trees into this engine repo.
- Current cloud prototype is deliberately not the final volume pass. Next
  renderer steps: debug views, field instance contract, then explicit local
  volume/froxel pass.
- First artifact found: near ellipsoid edges can occlude farther clouds if the
  SDF boundary emits a positive-distance absorbing veil. Keep density inside the
  sculpted container, eroded, feathered, and low-extinction.
- Follow-up fix made the march structural: intersect each analytic cloud
  ellipsoid, jump empty space to the next true entry, and sample only inside
  cloud intervals so near containers cannot eat the budget before far clouds.
- Method correction: stop iterating cloud features by agile half-step. Build the
  target renderer as a waterfall machine: field contract, broad phase, explicit
  local volume pass, composition, then acceleration. Current shader cloud code is
  temporary scaffolding only.
- Current scene correction: analytic cloud scaffold is no longer composited.
  Bodies render as solids; the Grid traces separately and blends as a transparent
  schematic overlay so future volumetrics can own actual scene mass.
- Grid transparency correction: do not use alpha blending over raymarched scene
  content. Use Aetheria-style stochastic coverage and only draw the Grid when it
  is nearer than the nearest solid body, so it does not overlay planets.
- Pulled Aetheria's `Assets/Resources/LDR_LLL1_0.png` into Aquarium as source
  PNG plus runtime `R8_UNorm` bytes. The renderer binds it as `t2` with wrapping
  point sampling for stochastic Grid coverage.
- Dev watcher fix: restart fingerprint now includes `src/Aquarium.Engine/Assets`
  so runtime content changes trigger apphost rebuild/restart instead of relying
  on shader or live assembly reload.
- Temporal pass: added clean-room TSR-inspired Gate 1. Scene renders into
  HDR/travel texture, resolve pass reprojects by current/previous camera basis,
  clamps history to current 3x3 neighborhood, rejects by travel delta, writes
  ping-pong history, and tonemaps after resolve.
- Temporal Gate 2A: added scene/history metadata storing material id and normal.
  Resolve now rejects material mismatches and normal discontinuities. Grid
  stochastic surfaces record Grid travel/normal even when dither misses.
- Temporal Gate 2B: field id replaces coarse material id for TAA identity. Each
  planet has a stable field id and analytic previous-center reprojection so
  orbiting body history follows the body instead of camera-only motion.
- Temporal Gate 3A: scene pass now writes a temporal-control target separate
  from field/normal metadata. Current channels are reactive strength, stochastic
  coverage, reserved medium opacity, and reserved spare; the resolve uses the
  first two to reduce history for low-coverage reactive Grid samples.
- Temporal Gate 3B: temporal control now has its own ping-pong history. Resolve
  samples previous control at the reprojected UV and reduces history across
  coverage or reserved medium-opacity discontinuities.
- Temporal Gate 3C: temporal-control history `w` stores accepted history age.
  Validation grows age, failed validation resets it, and history authority ramps
  with age instead of fully trusting fresh samples immediately.
- Temporal Gate 3D: travel validation compares previous history travel against
  the reprojected point's expected distance from the previous camera. Do not
  compare previous travel directly to current travel under camera motion.
