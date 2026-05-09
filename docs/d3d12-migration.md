# D3D12 Migration

Aquarium is moving to D3D12 before the transparent stochastic surface pipeline
gets real machinery. D3D12 is now the default renderer; D3D11 remains only as a
temporary visual reference until the old backend is deleted.

## Current State

- `--renderer d3d12` is the default renderer and owns the live scene.
- `--renderer d3d11` still selects the old reference backend while it exists.
- D3D12 creates a device, command queue, flip-discard
  swapchain, RTV heap, per-backbuffer command allocators, command list,
  fence-backed present loop, static and transient shader-visible descriptor
  arenas, per-frame upload buffers, renderer-owned Grid height, medium, HDR
  scene, and bloom render targets, a fullscreen root signature, named objects,
  command-list events, and explicit tracked resource transitions.
- The first real migrated pass is the Grid height pass. D3D12 uploads Aquarium
  frame constants plus a fixed body brush table, renders a base Grid field, then
  draws one additive up-facing gravity quad per body into a 128x128
  scalar `R16_Float` Grid height target.
- D3D12 now owns the first field-resource upload path: CPU-built froxel
  primitive ids and field instances upload through the per-frame upload ring,
  copy into default-heap structured buffers, and bind as transient SRVs for the
  diagnostic shader. This proves the backend can feed real Aquarium field data
  without reading directly from upload memory.
- The D3D12 medium pass renders a packed frustum froxel atlas into diagnostic
  and transport render targets from the field instance buffer. Render debug mode
  `11` now displays D3D12 froxel density, matching the future renderer path
  rather than the temporary Grid-height bring-up view.
- The D3D12 scene pass now renders Self, planets, medium transport, and the
  first transparent candidate event through the D3D12 frame graph. Grid line
  transparency is no longer baked into the medium froxel atlas; the scene
  traversal intersects the binned Grid height sheet, samples the height texture
  at the hit position, evaluates cartesian lines, height isolines, and
  gradient-angle field lines there, then composites that event without
  terminating the ray.
- The D3D12 scene pass now uses one bounded interval traversal for solids,
  medium, and transparent events. Each camera ray walks the medium slice
  intervals, samples medium transport for that interval, looks up binned solid
  and transparent candidates at conservative interval sample points, integrates
  transport up to transparent events, composites those events without stopping,
  and stops only on the nearest opaque hit after integrating transport up to it.
  The current solid evaluator is still analytic spheres; the traversal contract
  is ready for SDF surface evaluators, bracket/bisect refinements, and richer
  particle event lists.
- Transparent Grid contribution is now represented by a transparent-surface
  table plus froxel-binned surface ids. The scene pass discovers transparent
  candidates through the froxel bin, so future particles and billboard-like
  surfaces can use the same event-intersection class instead of gaining special
  alpha handling. The Grid is binned only into a conservative height slab, and
  the shader brackets the height-sheet crossing inside the active ray interval
  before solving it, so shallow views do not rediscover the same sheet in every
  depth froxel. The next expansion is a richer per-froxel event list for many
  particles/quads.
- D3D12 presentation is HDR-linear until the present pass. The scene renders to
  `R16G16B16A16_Float`, a three-level bloom pyramid performs firefly-safe
  downsample plus separable blur, and final presentation applies exposure,
  bloom/veil, and ACES. Debug mode `7` shows bloom and `8` shows exposed
  luminance.
- The D3D12 scene pass writes the first temporal diagnostic spine: color/travel,
  field id/normal metadata, and temporal control render targets. The present
  shader can inspect current temporal control in debug mode `5` and field
  identity in debug mode `6`. Resolve now writes ping-pong history color,
  metadata, and control targets, with previous-camera reprojection and
  field/travel/normal/color/coverage/medium validation for opaque current scene
  hits. Debug mode `3` shows history age and mode `4` shows history weight.
  Medium-only pixels use a density-weighted ray centroid as their temporal
  anchor and a `FIELD_ID_MEDIUM` identity, with continuity weighted by medium
  opacity and coverage rather than surface normals. Transparent candidate
  events write event support and support-weighted travel directly from the scene
  traversal, so Grid and future particles can share temporal support without
  becoming fake alpha surfaces or opaque depth hits.
- D3D12 debug modes `9` and `10` are direct ray-step medium previews, not
  repainted atlas views. The resolve shader samples the field instance buffer
  for the requested `MediumDebugStep`, so density/transmittance diagnostics can
  be compared against the froxel atlas in mode `11`.
- D3D12 now has the same native debug overlay controls as D3D11. DirectWrite
  and Direct2D are kept for crisp overlay text through the documented D3D11On12
  bridge: D3D12 renders the frame, then the overlay acquires the swapchain
  backbuffer, draws debug UI, releases it to Present, and does not participate
  in scene/medium rendering. Diegetic text remains a future renderer-owned
  MSDF/SDF path.
- D3D12 reports once-per-second CPU timing averages for total frame work,
  command-list recording, and the DirectWrite overlay bridge. These are CPU
  timings, not GPU timestamp queries, but they keep the bridge from hiding in
  folklore while the frame graph is still small.
- Resize waits for the GPU, releases swapchain-dependent resources, rebuilds
  static shader/RTV descriptor arenas, then recreates backbuffer views and
  dependent render targets. Descriptor exhaustion on resize is no longer a
  bring-up policy.
- `IAquariumRenderer` is the host boundary. The host should not learn backend
  internals as the D3D12 renderer grows teeth.
- `docs/d3d12-best-practices-audit.md` and `research/d3d12/synthesis.md`
  capture the current D3D12 doctrine and audit.

## Migration Order

1. Keep D3D11 as the visual reference only until the D3D12 default survives
   visible use.
2. Move shared renderer contracts behind explicit backend-neutral types.
3. Port the Grid height pass after preserving D3D11 as the reference output.
4. Keep capacity diagnostics loud while the backend is still small enough to
   make mistakes obvious.
5. Keep replacing placeholder evaluators inside the unified traversal with real
   field/SDF/particle implementations instead of adding side-channel passes.

## Invariants

- No hidden D3D11 dependency should leak into shared renderer contracts.
- D3D12 work must be validated by actual headless runs, not just compilation.
- D3D11 output remains the comparison target until D3D12 survives visible use
  and the old backend is deleted.
- Transparent surfaces are still events/candidates, not canonical depth.
- DirectWrite overlay text belongs behind the narrow D3D11On12 bridge. Do not
  use it for diegetic/world text or let it leak into the renderer's scene graph.
