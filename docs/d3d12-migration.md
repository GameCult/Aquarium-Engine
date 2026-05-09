# D3D12 Migration

Aquarium is moving to D3D12 before the transparent stochastic surface pipeline
gets real machinery. D3D11 remains the reference renderer until the D3D12 path
has feature parity.

## Current State

- `--renderer d3d11` is the default renderer and owns the live scene.
- `--renderer d3d12` creates a D3D12 device, command queue, flip-discard
  swapchain, RTV heap, per-backbuffer command allocators, command list,
  fence-backed present loop, shader-visible CBV/SRV/UAV descriptor arena,
  per-frame upload buffers, fullscreen root signature, and a smoke-test PSO.
- The D3D12 path currently draws a diagnostic fullscreen pass. It exists to
  prove backend lifecycle, shader compilation, root signature creation,
  descriptor-table binding, per-frame constant upload, PSO creation, render
  target binding, and draw submission before real scene passes migrate.
- `IAquariumRenderer` is the host boundary. The host should not learn backend
  internals as the D3D12 renderer grows teeth.

## Migration Order

1. Keep D3D11 as the visual reference.
2. Move shared renderer contracts behind explicit backend-neutral types.
3. Replace the smoke-only descriptor/upload code with general ring-buffer and
   resource lifetime helpers.
4. Port the existing passes in visible order: grid height, froxel volume, scene,
   bloom, resolve, overlay/debug.
5. Build the transparent stochastic surface pipe in D3D12 once descriptor and
   candidate-buffer ownership is explicit.

## Invariants

- No hidden D3D11 dependency should leak into shared renderer contracts.
- D3D12 work must be validated by actual headless runs, not just compilation.
- D3D11 output remains the comparison target until D3D12 fully renders the scene.
- Transparent surfaces are still events/candidates, not canonical depth.
