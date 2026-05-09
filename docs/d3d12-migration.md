# D3D12 Migration

Aquarium is moving to D3D12 before the transparent stochastic surface pipeline
gets real machinery. D3D11 remains the reference renderer until the D3D12 path
has feature parity.

## Current State

- `--renderer d3d11` is the default renderer and owns the live scene.
- `--renderer d3d12` creates a D3D12 device, command queue, flip-discard
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
- The D3D12 scene pass now renders Self, planets, and medium transport through
  the D3D12 frame graph. Grid line transparency is injected as a thin medium
  contribution in the froxel atlas instead of being drawn as an alpha surface or
  used as a scene-depth terminator.
- Transparent Grid contribution is now represented by a transparent-surface
  table plus froxel-binned surface ids. The medium pass discovers it through the
  froxel bin, so future particles and billboard-like surfaces can use the same
  class of integration instead of gaining special alpha handling. The binned
  Grid surface now carries cartesian gridlines, height isolines, and
  gradient-angle field lines through that medium path.
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
  opacity and coverage rather than surface normals. The medium pass also writes
  a generic transparent-event summary target for binned stochastic surfaces.
  Scene identity can now use `FIELD_ID_TRANSPARENT_EVENT` with event support and
  support-weighted travel, so Grid and future particles share temporal support
  without becoming fake alpha surfaces or opaque depth hits.
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
- Resize waits for the GPU, releases swapchain-dependent resources, rebuilds
  static shader/RTV descriptor arenas, then recreates backbuffer views and
  dependent render targets. Descriptor exhaustion on resize is no longer a
  bring-up policy.
- `IAquariumRenderer` is the host boundary. The host should not learn backend
  internals as the D3D12 renderer grows teeth.
- `docs/d3d12-best-practices-audit.md` and `research/d3d12/synthesis.md`
  capture the current D3D12 doctrine and audit.

## Migration Order

1. Keep D3D11 as the visual reference only.
2. Move shared renderer contracts behind explicit backend-neutral types.
3. Port the Grid height pass after preserving D3D11 as the reference output.
4. Keep capacity diagnostics loud while the backend is still small enough to
   make mistakes obvious.
5. Port the existing passes in visible order: grid height, froxel volume, scene,
   bloom, resolve, overlay/debug.
6. Build the transparent stochastic surface pipe in D3D12 once descriptor and
   candidate-buffer ownership is explicit.

## Invariants

- No hidden D3D11 dependency should leak into shared renderer contracts.
- D3D12 work must be validated by actual headless runs, not just compilation.
- D3D11 output remains the comparison target until D3D12 fully renders the scene.
- Transparent surfaces are still events/candidates, not canonical depth.
- DirectWrite overlay text belongs behind the narrow D3D11On12 bridge. Do not
  use it for diegetic/world text or let it leak into the renderer's scene graph.
