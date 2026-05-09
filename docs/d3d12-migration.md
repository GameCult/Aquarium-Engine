# D3D12 Migration

Aquarium is moving to D3D12 before the transparent stochastic surface pipeline
gets real machinery. D3D11 remains the reference renderer until the D3D12 path
has feature parity.

## Current State

- `--renderer d3d11` is the default renderer and owns the live scene.
- `--renderer d3d12` creates a D3D12 device, command queue, flip-discard
  swapchain, RTV heap, per-backbuffer command allocators, command list,
  fence-backed present loop, shader-visible CBV/SRV/UAV descriptor arena,
  per-frame upload buffers, renderer-owned Grid height target, offscreen smoke
  target, fullscreen root signature, diagnostic UAV target, and smoke-test PSO.
- The D3D12 path currently draws a diagnostic fullscreen pass. It exists to
  prove backend lifecycle, shader compilation, root signature creation,
  descriptor-table binding, per-frame upload-ring constants, PSO creation,
  render target binding, tracked resource state transitions,
  render-target-to-swapchain copy, UAV binding from a transient descriptor,
  named objects, command-list events, and draw submission before real scene
  passes migrate.
- The first real migrated pass is the Grid height pass. D3D12 uploads Aquarium
  frame constants plus a fixed body brush table, renders a base Grid field, then
  draws one additive up-facing gravity quad per body into a 128x128
  scalar `R16_Float` Grid height target. Render debug mode `11` displays that
  target.
- Resize waits for the GPU, releases swapchain-dependent resources, rebuilds
  static shader/RTV descriptor arenas, then recreates backbuffer views and
  dependent render targets. Descriptor exhaustion on resize is no longer a
  bring-up policy.
- `IAquariumRenderer` is the host boundary. The host should not learn backend
  internals as the D3D12 renderer grows teeth.
- `docs/d3d12-best-practices-audit.md` and `research/d3d12/synthesis.md`
  capture the current D3D12 doctrine and audit.

## Migration Order

1. Keep D3D11 as the visual reference.
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
