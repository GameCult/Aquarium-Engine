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
  history, 3 history age, 4 history weight, 5 current temporal control, 6
  current field identity. Use these to prove whether the presented frame is
  resolved and whether history is alive.
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
- Temporal Gate 3M: final presentation now cuts temporal color history for all
  surfaces while jitter is disabled. History/color/control ping-pongs remain
  diagnostics and future volumetric scaffolding, not visible authority.
- Temporal debug correction: mode 5 exposes current temporal control. The first
  look showed the entire Grid red because stale Grid logic marked low support as
  reactive. Grid reactive is now zero; support/coverage remains the green
  signal. Empty background coverage is now zero; opaque hits write coverage 1
  and Grid hits write support coverage.
- Temporal Gate 3L: projection jitter is scaled to zero because Self/planet
  surfaces visibly juddered. Keep jitter disabled until opaque and Grid resolve
  both hide the offset. Removed the abandoned unjittered Grid retrace; Grid
  analytic final now uses the existing current hit while jitter is zero.
- HDR/PBR research pass: `research/rendering/hdr-pbr-presentation.md` says the
  next renderer work before the SDF field renderer is explicit exposure,
  pre-tonemap bloom/veil, and display-transform/luminance debug views. Avoid
  threshold glow; bloom spreads energy already present in scene-linear HDR.
- HDR implementation pass: renderer now has fixed manual exposure, half/quarter/
  eighth bloom targets, firefly-safe downsample, separable horizontal/vertical
  blur per level, pre-tonemap bloom/veil composite, mode 7 bloom contribution,
  and mode 8 exposed luminance.
- Debug UI pass: Aquarium now has a native Direct2D/DirectWrite debug panel
  toggled with F2. It uses a CultLib-inspired code-first control API with
  retained bound sliders/toggles/buttons instead of Unity prefabs or ImGui
  chrome. First live controls are render debug mode, exposure, bloom intensity,
  and bloom veil; the HDR controls feed shader constants. Important UI lessons:
  Vortice `Rect` is x/y/width/height, so use `RectFromEdges` for edge math;
  rows/buttons/tooltips are flat Direct2D overlay UI; sliders need
  element-specific base/hover/active states where track/fill and handle brighten
  independently without geometry changes. To brighten saturated orange, lerp
  toward white instead of trying to increase HSV value.
- Global graphics settings pass: render debug mode, exposure, bloom intensity,
  and bloom veil now live in the typed `epiphany.aquarium.graphics_settings`
  CultCache document. `GraphicsSettings` is a contracts DTO shared by host,
  live runtime, and renderer. The UI still binds to renderer properties, the
  host syncs changed renderer settings back to the live runtime, and the runtime
  flushes dirty settings on the normal state interval, shutdown, and before live
  assembly reload.
- Fog-land Gate 1 foothold: renderer now packs a fixed field instance structured
  buffer for Self, planets, and local medium ellipsoids. HLSL consumes the buffer
  for registered medium density/transmittance diagnostics. Debug mode 9 shows
  medium density, mode 10 shows transmittance. Final scene composition is still
  untouched until the explicit low-res volume pass exists.
- Fog-land Gate 3 foothold: registered medium integration moved out of the scene
  shader into a half-resolution medium pass. The texture stores density,
  transmittance, and source diagnostics. Mode 11 shows medium source. Final scene
  color remains unchanged; composition is still the next machine part.
- Fog composition gate: the medium pass now writes separate diagnostic and
  transport targets. Resolve can composite `surface * transmittance +
  in-scattering`, controlled by persisted `MediumCompositeIntensity`, which
  defaults to zero so final remains unchanged until deliberately enabled/tuned.
- Medium density correction: the medium pass now stores a 4x4 frustum slice
  atlas instead of a finished screen-space ray integral. Each slice texel
  reconstructs a world point and evaluates field-local density there; resolve
  integrates the stored cells front-to-back. Removed the time term from erosion
  noise so the texture itself is stationary under inspection.
- Follow-up correction: mode 9 is no longer the frustum atlas mean or a single
  `z = 0` slice. It is a world-anchored density column over the Grid plane, so
  current above/below medium ellipsoids are actually visible. Modes 10/11 remain
  transport diagnostics; source debug is boosted separately from physical
  transport so black-on-black patches do not pass as information.
- Debug UI control grammar now includes named option rows. Render Debug uses an
  option/dropdown selector instead of a numeric slider so the expanding debug
  mode list remains legible.
- Dev watcher correction: live runtime reload now waits for the host stdout log
  to acknowledge the exact new live DLL path before declaring success. A pointer
  write alone is not proof. `dev-watch.ps1 -ReopenWhenClosed` reopens the last
  visible slot without rebuilding when the window has been closed.
- Dev watcher failure found in practice: `Start-Process -Wait` around
  `dev-reload.ps1` could block after the app launched, leaving the watcher stuck
  at initial launch and never polling. The watcher now invokes dev-reload
  synchronously and leaves long-lived process ownership inside dev-reload.
- CultCache startup failure found: dev cache
  `artifacts/dev-reload/cultcache/aquarium-client.msgpack` was zero bytes, so
  MessagePack threw `EndOfStreamException` before schema guardrails could run.
  Aquarium now quarantines unreadable snapshots with a `.corrupt-<timestamp>`
  suffix and boots fresh state; CultLib single-file stores now write through a
  temp file and atomic replace to avoid future zero-byte live snapshots.
- Headless dev-reload correction: `-Headless` now creates a hidden Win32 window,
  waits for the two-frame run to exit, kills and fails on timeout, and logs
  renderer startup checkpoints to stdout. This exposed the real shader compile
  failure: the medium density-column debug loop must stay rolled because forcing
  an unroll expands the registered medium density stack beyond D3DCompiler's
  tolerance.
- Medium debug correction: "Medium Source" was a misleading view. In volume
  rendering source should mean injected/in-scattered radiance from a real light
  field, but Aquarium does not have that injection stage yet. Debug modes now
  separate direct camera-ray density, direct camera-ray transmittance, and the
  low-resolution atlas density diagnostic.
- Medium ray preview UI: debug controls can now be conditionally visible.
  `GraphicsSettings.MediumDebugStep` persists a ray sample index. The Ray Step
  slider appears only in Medium Ray Density / Medium Ray Transmittance modes;
  density mode shows density at that selected sample, and transmittance mode
  shows accumulated survival to that sample.
- Medium ray preview binding fix: the resolve shader uses
  `registeredMediumDensity` for ray preview modes, so resolve must bind the
  field instance structured buffer at `t12`. The medium atlas pass already had
  the buffer, which is why atlas density could show blobs while ray density and
  ray transmittance were empty at every step.
- Medium composite correction: final composition must not use the diagnostic
  4x4 frustum atlas. The atlas intentionally reveals the current low-res pass
  artifact; composite now uses the same direct registered-medium ray integration
  as ray debug, clamped to the visible surface travel. Atlas density remains a
  diagnostic view only.
- First real froxel volume pass: the failed 4x4 MAD path has been replaced by a
  packed 160x90x32-at-720p frustum froxel volume stored as an 8x4 D3D11 atlas.
  The medium pass writes per-froxel density/transmittance/in-scattering, resolve
  integrates the froxel transport for final composite, and direct ray debug
  remains the reference for checking shape and continuity.
- Grid/fog composition correction: Grid travel is still written for temporal
  stability, but the Grid is not a solid medium terminator. Froxel composition
  stops at Self/planet solids only; Grid pixels integrate medium to the visible
  far distance so the schematic overlay cannot cut holes through fog or occlude
  itself.
