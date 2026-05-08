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
- Dev watcher heartbeat fix: watcher now checks the recorded script-owned PID
  from the visible/headless dev-reload state and relaunches if that process is
  gone. It also fingerprints `scripts/*.ps1` so script changes are treated as
  restart-owned source changes.
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
- Temporal Gate 3E: stochastic Grid history bypasses opaque-surface color clamp
  and color-delta rejection. A current dither miss is not evidence that the
  true Grid color is background.
- Temporal debug keys: F1 cycles, 0 final, 1 raw current scene, 2 reprojected
  history, 3 history age, 4 history weight. Use these to prove whether the
  presented frame is resolved and whether history is alive.
- Temporal Gate 3F: Grid stochastic coverage now uses high-retention history
  without coverage/reactive authority penalties. Coverage remains a continuity
  signal, not a reason to throw away the accumulation low-alpha lines require.
- Temporal Gate 3G: identity/control buffers are point-loaded and Grid history
  color uses nearest identity-aligned sampling. This prevents foreground glow
  from bilinearly bleeding into background Grid history at silhouettes.
- Temporal Gate 3H: Grid temporal coverage is line support only, not broad
  overlay alpha. Current support gates Grid history so old stochastic hits do
  not survive as fine smoke across the surface.
- Temporal Gate 3I: Grid resolve reconstructs analytic overlay color at the
  current world hit and uses premultiplied color as the current estimate. Raw
  scene debug mode may still be noisy; final no longer treats stochastic
  hit/miss color as ground truth.
- Temporal Gate 3J: debug mode 1 is raw scene again. Final Grid pixels use the
  analytic current estimate directly instead of blending previous Grid color
  history; history debug surfaces remain diagnostics.
- Temporal Gate 3L: projection jitter is scaled to zero because Self/planet
  surfaces visibly juddered. Keep jitter disabled until opaque and Grid resolve
  both hide the offset. Removed the abandoned unjittered Grid retrace; Grid
  analytic final now uses the existing current hit while jitter is zero.
- Dev watcher correction: live runtime reload now waits for the host stdout log
  to acknowledge the exact new live DLL path before declaring success. A pointer
  write alone is not proof. `dev-watch.ps1 -ReopenWhenClosed` reopens the last
  visible slot without rebuilding when the window has been closed.
- Dev watcher failure found in practice: `Start-Process -Wait` around
  `dev-reload.ps1` could block after the app launched, leaving the watcher stuck
  at initial launch and never polling. The watcher now invokes dev-reload
  synchronously and leaves long-lived process ownership inside dev-reload.
