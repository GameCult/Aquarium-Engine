# D3D12 Best Practices Audit

## Verdict

The current D3D12 path is acceptable as a bring-up scaffold. It proves the right
classes of machinery: per-frame command allocators, fence-protected reuse,
shader-visible descriptors, persistent mapped constants, explicit render target
state, an offscreen target path, SRV sampling, UAV binding, and resize-safe
descriptor arena rebuild.

It is not yet a production renderer architecture. The next risk is porting real
passes without keeping D3D11 as the comparison target and without promoting the
resource graph beyond smoke-pass scaffolding.

## Good Current Decisions

- `D3D12Renderer` keeps D3D11 intact as the visual reference backend.
- Command allocators are per swapchain frame and waited on before reset.
- Shader-visible descriptor slots are not overwritten while in flight.
- Constant upload memory is persistently mapped, 256-byte aligned, and allocated
  through a per-frame upload ring.
- Offscreen render target state is tracked and transitions are skipped when
  redundant.
- Backbuffer state is tracked through the same transition helper style instead
  of raw assumed barriers.
- D3D12 objects are named, and command-list events mark the frame, smoke pass,
  and copy pass for PIX/graphics diagnostics.
- Shader-visible descriptors are split into static and per-frame transient
  arenas. Transient arenas reset only after the owning frame fence clears.
- D3D12 resources are registered by name, and capacity diagnostics report upload,
  transient descriptor, static descriptor, and RTV usage.
- Render targets may create SRVs, and the smoke path now samples its offscreen
  target in a second fullscreen pass instead of copying it as an opaque resource.
- Renderer calls receive current window dimensions, and the D3D12 backend can
  recreate swapchain buffers plus dependent smoke resources on resize.
- Resize waits for the GPU, disposes swapchain-dependent targets, rebuilds
  static shader and RTV descriptor arenas, and then recreates backbuffer/smoke
  descriptors from a clean ownership state.
- Render targets can create UAV descriptors. The D3D12 smoke pass binds a
  transient per-frame UAV descriptor and writes a diagnostic target from the
  pixel shader, proving CBV and UAV tables can share the active transient heap.
  UAV-capable resource creation is separate from persistent UAV descriptor
  ownership, so diagnostic-only transient UAVs do not consume static heap slots.
- The D3D12 smoke path exercises shader compilation, root signature, PSO,
  descriptor table, RTV/UAV binding, barriers, copy, and present.

## Temporary Acceptable Debt

- Committed resources are used directly. Replace with D3D12MA or placed-resource
  suballocation before the renderer owns many textures/buffers.
- Descriptor arenas are still simple bump allocators. Add free lists or
  descriptor-table paging only when real resource churn demands it.
- Static shader and RTV descriptor arenas rebuild wholesale on resize. This is
  deliberately simple and correct for swapchain-dependent resources; add finer
  free lists only when non-resize churn proves it needs them.
- Upload ring is per-frame and reset after fence wait. It is not yet a global
  transient allocator with suballocation statistics or overflow diagnostics.
- Backbuffer state is assumed. Wrap swapchain buffers in state-tracked frame
  resources before resize and multi-pass presentation.
- The offscreen smoke target copies to the swapchain. Keep this only if the real
  post-process graph needs it; otherwise present from the final render target
  path with no ornamental copy.

## Required Before Real Pass Migration

- Upload ring capacity policy and overflow reporting for real frame data.
- Descriptor-owner diagnostics beyond heap-level capacity summaries.
- A real pass migration that uses the D3D12 resource helpers without cloning
  smoke-pass-local policy.

## Rule

Do not port Aquarium's scene/froxel/history passes into ad hoc D3D12 resource
creation. Build the resource machine first, then move pixels through it.
