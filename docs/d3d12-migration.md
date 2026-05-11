# D3D12 Renderer

D3D12 is the live renderer. The old D3D11 scene backend has been removed; the
only remaining D3D11 use is the narrow D3D11On12 bridge required for native
DirectWrite/Direct2D overlay text.

## Current State

- D3D12 owns the device, command queue, flip-discard swapchain, RTV heap,
  per-backbuffer command allocators, command list, fence-backed present loop,
  static and transient shader-visible descriptor arenas, upload buffers,
  renderer-owned Grid height, HDR scene, bloom, temporal diagnostic targets,
  named objects, command-list events, and explicit tracked transitions.
- The Grid height pass uploads Aquarium frame constants plus a fixed body brush
  table, renders a base Grid field, then draws one additive up-facing gravity
  quad per body into a 128x128 scalar `R16_Float` target.
- The scene pass traces Self, planets, the cursor locator, and a separate Grid
  event lane. Grid line transparency is direct-traced against the height sheet
  before the nearest solid and emitted as premultiplied event radiance.
- Presentation is scene-linear until the post pass. The scene renders to
  `R16G16B16A16_Float`, a three-level bloom pyramid performs firefly-safe
  downsample plus separable blur, and final presentation applies exposure,
  bloom/veil, and ACES.
- The scene pass writes color/travel, field id/normal metadata, temporal
  control, and event targets. The present shader writes ping-pong history and
  exposes debug views for raw scene, history, control, identity, bloom, and
  exposed luminance.
- DirectWrite and Direct2D remain for crisp overlay text through D3D11On12:
  D3D12 renders the frame, then the overlay acquires the swapchain backbuffer,
  draws debug UI, releases it to Present, and never participates in world
  rendering.
- Shader and PSO creation runs off the main thread. Startup holds the splash
  until the first pipeline set is ready. Runtime shader edits to
  `D3D12Grid.hlsl`, `D3D12Scene.hlsl`, or `D3D12Post.hlsl` trigger a background
  rebuild; successful builds swap in after a GPU wait and failures keep the
  previous pipeline set.
- Resize waits for the GPU, releases swapchain-dependent resources, rebuilds
  descriptor arenas, and recreates backbuffer views plus dependent render
  targets.

## Invariants

- `IAquariumRenderer` is the host boundary. The host should not learn renderer
  internals as D3D12 grows teeth.
- D3D12 work must be validated by actual headless runs, not just compilation.
- Transparent-looking Grid output is an event lane, not canonical opaque depth.
- DirectWrite overlay text belongs behind the narrow D3D11On12 bridge. Do not
  use it for diegetic/world text or let it leak into the scene graph.
