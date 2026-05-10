# Scratch

## Current Slice

D3D12 parity push. The host now defaults to D3D12 unless `--renderer` or
`AQUARIUM_RENDERER` says otherwise. D3D12 owns the visible scene path:
Grid-height brushes, field/froxel uploads, medium diagnostic/transport
atlases, scene HDR, bloom, temporal resolve/history, debug modes, and
DirectWrite debug UI through a narrow D3D11On12 overlay bridge. D3D11 remains
selectable only as temporary reference ballast until visible D3D12 use survives
the cut.

Current renderer correction: the D3D12 Grid has been removed from the
transparent/froxel candidate experiment. The Grid now traces directly in the
scene shader as a non-terminating stochastic color overlay before the nearest
solid, but it no longer writes the visible scene identity/travel/control packet.
That packet belongs to the actual opaque/medium result so fog and temporal
debug views do not get clipped/classified by a transparent UI surface. Solid
spheres still use view-froxel projected bounds for empty-space skipping;
transparent surface candidate buffers, descriptors, root parameters, shader
structs, and event traversal were cut rather than left as dead scaffolding.
D3D12 Grid line width now mirrors the D3D11 reference:
periodic domains use `fwidth()` and pixel-width constants instead of world-space
`ddx/ddy` footprint guesses. The stochastic mask now samples Aetheria's copied
512x512 R8 blue-noise texture in screen space through D3D12 `t15/s1`, point and
wrap sampled. It shifts the blue-noise tile by a low-discrepancy pixel offset
each frame so TAA sees temporal coverage variation without all pixels toggling
together.

Follow-up TAA diagnosis: D3D12 had stable Grid metadata/control but noisy final,
raw, and history because the post resolve used stochastic hit/miss scene color
as current radiance. It now matches the D3D11 reference more closely: the post
pass binds the Grid height target, reconstructs analytic Grid color from current
Grid travel/world position, nearest-loads Grid history, and skips color-delta
history rejection for Grid pixels. Raw debug remains the stochastic stream.
This was later superseded because using Grid as the visible scene packet broke
volumetric ordering. The Grid is currently a direct color overlay only; future
work should add a separate transparent-overlay temporal lane rather than
reusing field identity/travel/control.
- Medium temporal split: D3D12 now emits a separate `sceneMediumPacket` MRT and
  ping-pongs `historyMediumPacket` alongside surface color/metadata/control.
  The medium packet stores medium identity, density-weighted travel, opacity,
  and density even when a solid surface is behind it. Debug field identity can
  show medium where opacity exists instead of pretending the planet behind it
  is the whole truth. This is the pattern Grid/particles need next: a separate
  stochastic-event packet, not reuse of the surface packet.
- Stochastic event temporal split: D3D12 now emits `sceneEventColor` and
  `sceneEventMetadata` MRTs for Grid-style stochastic transparent events and
  ping-pongs matching event history targets. The base scene no longer uses
  binary Grid hits as scene color truth; resolve accumulates the stochastic
  event lane by event travel/coverage/identity and composites it over the
  resolved surface+medium scene. This is the future particle/billboard class:
  sampled event color plus event metadata, not alpha blending and not the solid
  surface packet.
- Event TAA tuning: stochastic event history needs very high retention. A 0.92
  cap still lets every dither miss chew visible chunks out of low-coverage Grid
  lines, leaving grain. Current D3D12 event lane uses 0.985 max retention with a
  stronger fresh-history floor. Debug mode 6 is named Lane Identity because it
  shows the winning diagnostic lane, not only the hard surface field id.

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
- D3D12 migration foothold: D3D11 is frozen as the reference backend, and the
  host now selects `d3d11` or `d3d12` through `--renderer` /
  `AQUARIUM_RENDERER`. The D3D12 backend currently proves lifecycle only:
  device, command queue, flip-discard swapchain, RTV heap, command allocator,
  graphics command list, fence wait, clear, and present. Do not build the
  transparent candidate pipe on D3D11 debt; port explicit D3D12 resource/pass
  ownership first.
- D3D12 pass spine pass: the backend now uses one command allocator per
  swapchain buffer instead of stalling a single allocator each frame. It also
  compiles `D3D12Smoke.hlsl`, creates a fullscreen root signature and graphics
  PSO, binds viewport/scissor/RTV, and draws a fullscreen triangle. This proves
  shader, root signature, PSO, and draw submission before real Aquarium passes
  migrate.
- D3D12 descriptor/upload foothold: the D3D12 path now owns a shader-visible
  CBV/SRV/UAV descriptor arena and per-frame upload constant buffers. The smoke
  pass binds a CBV descriptor table at b0 and uploads frame/debug/HDR-tinted
  constants before drawing. This is still diagnostic, but it proves descriptor
  heap visibility, root descriptor table binding, and per-frame CPU-to-GPU
  upload ownership.
- D3D12 render-target foothold: the smoke pass now renders into a renderer-owned
  default-heap offscreen render target with explicit RTV descriptor ownership and
  tracked state transitions, then copies that texture into the swapchain
  backbuffer. This proves render-target resource lifetime and copy barriers
  before Aquarium scene/history/froxel targets are ported.
- D3D12 research pass: `research/d3d12/synthesis.md` and
  `docs/d3d12-best-practices-audit.md` distill Microsoft/NVIDIA/AMD guidance.
  Current D3D12 path is valid bring-up scaffolding, but do not port real passes
  until descriptor allocation is split into static/transient ranges, upload data
  uses a fence-reclaimed ring, resource ownership is named/tracked, and debug
  names/PIX markers exist.
- D3D12 full-ass foundation pass: D3D12 objects now have debug names, the frame
  and smoke/copy passes emit command-list events for PIX-style inspection,
  swapchain backbuffers are state-tracked resources, and per-frame constants are
  allocated from a persistent mapped upload ring after the frame fence wait.
- D3D12 descriptor/registry pass: shader-visible descriptors are split into
  static and per-frame transient arenas. Transient descriptors reset after the
  frame fence wait, named resources live in `D3D12ResourceRegistry`, and
  one-second capacity diagnostics report upload ring, transient/static shader
  descriptor, and RTV usage.
- D3D12 SRV/resize pass: render targets can now own SRV descriptors, the smoke
  target is sampled by a second fullscreen pass instead of copied as a raw
  resource, and the renderer interface passes current window dimensions so D3D12
  can recreate swapchain buffers and dependent smoke resources on resize. Resize
  still consumes fixed bring-up descriptors and fails loudly before exhaustion;
  production descriptor reclamation/rebuild policy remains before serious pass
  migration.
- D3D12 UAV/resize-rebuild pass: render targets can own UAV descriptors, the
  D3D12 smoke pass binds a transient per-frame UAV descriptor and writes a
  diagnostic target from the pixel shader. Pixel shader UAVs share output
  register namespace, so the diagnostic UAV binds at `u1` rather than `u0`.
  UAV resource capability is now independent from persistent UAV descriptor
  ownership, so transient diagnostic UAVs do not burn static descriptors.
  D3D12 ignores the D3D11 `--shader-source` override and compiles its own copied
  `D3D12Smoke.hlsl`. Resize now waits for the GPU, releases swapchain-dependent
  resources, rebuilds static shader/RTV descriptor arenas, and recreates
  backbuffer/smoke/diagnostic resources from a clean descriptor state.
- D3D12 Grid height migration correction: the first version still did a
  fullscreen per-texel body loop. That was the wrong architecture. The D3D12
  Grid height pass now behaves like Aetheria's gravity layer: a base Grid field
  draw, then one additive up-facing quad per body, driven by a small uploaded
  brush table. The target is scalar `R16_Float`: height is one value, and
  additive blending works on that format where `R32G32B32A32_Float` was the
  wrong bet. Render debug mode `11` displays the height target. Temporal
  previous camera/Grid/time state is renderer-level
  state, not swapchain-frame-resource state; swapchain image index is not frame
  history. Next D3D12 migration step is the froxel/field resource upload path.
- D3D12 froxel/field upload pass: D3D12 now builds the froxel primitive id table
  and field instance table, uploads them through the per-frame upload ring,
  copies them into default-heap structured buffers, and binds transient SRVs to
  the diagnostic shader. This proves the real field data path without forcing
  shaders to read directly from upload heap memory. The table-building code can
  duplicate D3D11 during migration because D3D11 is reference-only and will be
  removed after the D3D12 path owns the renderer.
- D3D12 medium froxel pass: D3D12 now renders diagnostic and transport froxel
  atlases from the D3D12 field instance buffer. Render debug mode `11` displays
  D3D12 froxel density; the temporary Grid-height debug view was cut rather
  than left as stale bring-up scaffolding.
- D3D12 transparent candidate scene pass: Grid line transparency is no longer
  injected into the medium froxel atlas. The scene traversal binds the
  transparent surface table, discovers the binned Grid candidate through froxel
  ids, intersects the Grid height sheet, evaluates cartesian gridlines, height
  isolines, and gradient-angle field lines at the actual ray hit, composites the
  transparent event without terminating, and continues marching toward medium
  and opaque SDF/solid hits.
- D3D12 transparent-surface bins: Grid line transparency now flows through a
  transparent surface table plus froxel-binned transparent surface ids. The
  scene shader discovers the Grid layer through the froxel bin, not through a
  hardcoded alpha/composite side path or a medium-atlas summary. This is the
  first implementation of the particle/billboard transparency contract.
- D3D12 HDR presentation pass: final D3D12 scene output is now scene-linear
  `R16G16B16A16_Float`. A three-level half/quarter/eighth bloom pyramid uses
  firefly-safe downsample plus separable horizontal/vertical blur before final
  exposure, bloom/veil composite, and ACES presentation. Debug mode 7 shows
  bloom contribution; mode 8 shows exposed luminance; mode 11 still bypasses
  post to inspect the medium density atlas.
- D3D12 binned Grid linework pass: the transparent surface scene path now
  evaluates Grid height gradients and contributes cartesian gridlines, height
  isolines, and gradient-angle field lines from the same froxel-binned
  transparent surface entry. Keep future particles/billboards in this shared
  transparent-surface integration class; do not resurrect alpha surfaces for
  them.
- D3D12 temporal diagnostic spine pass: the scene pass now writes MRTs for
  color/travel, field-id/normal metadata, and temporal control. Present debug
  mode 5 shows current control and mode 6 shows field identity. History
  ping-pong/reprojection is still pending; do not claim TAA parity until those
  buffers are written and validated.
- D3D12 temporal history pass: resolve now writes ping-pong color, metadata,
  and control history targets and presents through the resolve shader. Opaque
  current scene hits reproject through previous camera/Grid state, remap planet
  hits to previous centers, and validate history by field id, travel, normal,
  neighborhood color, coverage, and medium opacity. Debug mode 3 shows history
  age and mode 4 shows history weight. Grid/transparent-specific temporal
  reconstruction remains pending.
- D3D12 medium temporal identity pass: medium-only pixels now store
  `FIELD_ID_MEDIUM`, coverage from medium opacity/density, and travel from a
  density-weighted ray centroid. Resolve treats this as volumetric history:
  normal validation is skipped, color rejection is relaxed, and continuity is
  driven by coverage plus medium opacity. This lets binned Grid/fog contribution
  accumulate without reintroducing alpha surfaces.
- D3D12 medium ray debug pass: resolve now binds the field instance buffer and
  implements direct ray-step registered-medium previews for modes 9 and 10.
  These are correctness probes independent of the froxel atlas; mode 11 remains
  the atlas density view.
- D3D12 transparent event traversal pass: the scene pass now derives
  transparent-event support and support-weighted travel from intersected
  candidates instead of a froxel summary. This fixes the Grid blur/undersampling
  failure caused by sampling linework once per low-resolution medium atlas
  slice. The follow-up grazing-view fix bins the Grid only into a conservative
  height slab and brackets the height-sheet crossing inside the active ray
  interval before solving it; otherwise shallow rays rediscover the same sheet
  in multiple depth froxels and composite soup. Particles/billboards need the
  same pipe with richer per-froxel event lists and quad intersection evaluators.
- D3D12 overlay parity pass: DirectWrite/Direct2D debug UI remains native hinted
  overlay text through the documented D3D11On12 bridge. D3D12 renders the frame
  and leaves the backbuffer in render-target state; the bridge acquires the
  current swapchain image, draws the shared `DebugUi`, releases it to Present,
  and marks the tracked resource state accordingly. Keep this bridge narrow and
  overlay-only. Diegetic/world text still belongs to future MSDF/SDF renderer
  billboards.
- D3D12 parity default pass: host renderer selection now defaults to `d3d12`
  unless `--renderer` or `AQUARIUM_RENDERER` selects otherwise. D3D12 reports
  once-per-second CPU timing averages for whole frame, command-list recording,
  and DirectWrite overlay bridge cost. These are CPU timings, not GPU timestamp
  query timings.
- D3D12 async shader pipeline pass: initial D3D12 shader/PSO construction is no
  longer on the startup/main-thread path. The renderer starts a background
  pipeline build from the editable shader source directory, but does not present
  the swapchain until the first pipeline set is ready. The host repaints the
  original Win32 splash with "Compiling renderer pipelines" while waiting. This
  currently flickers a bit, likely because GDI splash repaint is fighting normal
  window invalidation, but it is better than handing the user a black render
  target or stale DirectWrite loading text written into a live backbuffer.
  Future polish: throttle/repaint only when invalidated or build a proper
  pre-renderer splash state/window. D3D12 shader files are polled directly for
  hot reload; replacements compile on a background task, swap on the render
  thread after a GPU wait, and failures preserve the previous pipeline set.
  Headless smoke now waits for a ready rendered frame and uses a separate
  headless CultCache so it cannot fight the visible dev window's state file.
- D3D12 unified traversal foothold: `D3D12Scene.hlsl` now uses one bounded ray
  interval traversal for solid candidates, medium transport, and transparent
  events. Each interval samples transport, checks conservative start/mid/end
  froxel primitive bins for solid candidates, integrates partial transport up
  to a hit, then stops on the nearest opaque surface. Current solid evaluators
  are analytic spheres; future SDF surfaces should replace that evaluator
  inside this traversal rather than creating a second solid pass.
