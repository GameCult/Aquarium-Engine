# D3D12 Best Practices Audit

## Verdict

The current D3D12 path is acceptable as a bring-up scaffold. It proves the right
classes of machinery: per-frame command allocators, fence-protected reuse,
shader-visible descriptors, persistent mapped constants, explicit render target
state, a migrated Grid height pass, an offscreen target path, SRV sampling, UAV
binding, and resize-safe descriptor arena rebuild.

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
- D3D12 renders the Grid height target as a proper brush pass: frame constants
  plus a fixed body brush table feed a base field draw followed by one additive
  up-facing gravity quad per body. The target is scalar `R16_Float`: height is
  one value, and the format still supports the additive render-target blending
  the brush pass needs.
- D3D12 uploads the live froxel primitive table and field instance table into
  default-heap structured buffers each frame, using the upload ring only as the
  copy source. Their SRVs are created in the transient descriptor heap used by
  the consuming pass, avoiding impossible static+transient heap co-binding.
- D3D12 renders the first medium froxel atlas from the field instance buffer
  into diagnostic and transport render targets. Mode `11` exposes froxel
  density, so the debug path now follows the renderer being built rather than a
  Grid-height bring-up convenience.
- D3D12 final mode now draws a real scene pass instead of the smoke diagnostic:
  Self, planets, and medium transport flow through D3D12. Transparent Grid line
  contribution is sampled into the medium atlas as a thin participating layer,
  so it is integrated with volumetrics rather than alpha-blended or used to
  occlude the ray.
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
- The offscreen smoke target copies to the swapchain. Keep this only if the real
  post-process graph needs it; otherwise present from the final render target
  path with no ornamental copy.
- The D3D12 scene pass is still a first parity slice: no full temporal resolve,
  bloom pyramid, full terrain line shader, or transparent-surface bin table yet.
  The transparency contract is correct; the implementation still needs the
  general binned transparent surface representation.
- D3D11 and D3D12 field table builders are temporarily duplicated by design.
  D3D11 is reference-only and will be removed after migration; do not pay DRY
  tax merely to preserve it.

## Required Before Real Pass Migration

- Upload ring capacity policy and overflow reporting for real frame data.
- Descriptor-owner diagnostics beyond heap-level capacity summaries.
- Continue pass migration without cloning smoke-pass-local policy into each
  resource.

## Rule

Do not port Aquarium's scene/froxel/history passes into ad hoc D3D12 resource
creation. Build the resource machine first, then move pixels through it.
