# D3D12 Best Practices Audit

## Verdict

The current D3D12 path is acceptable as a bring-up scaffold. It proves the right
classes of machinery: per-frame command allocators, fence-protected reuse,
shader-visible descriptors, persistent mapped constants, explicit render target
state, and an offscreen target path.

It is not yet a production renderer architecture. The next risk is letting
smoke-pass helpers become permanent resource policy by accident.

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
- The D3D12 smoke path exercises shader compilation, root signature, PSO,
  descriptor table, RTV binding, barriers, copy, and present.

## Temporary Acceptable Debt

- Committed resources are used directly. Replace with D3D12MA or placed-resource
  suballocation before the renderer owns many textures/buffers.
- Descriptor arenas are still simple bump allocators. Add free lists or
  descriptor-table paging only when real resource churn demands it.
- Upload ring is per-frame and reset after fence wait. It is not yet a global
  transient allocator with suballocation statistics or overflow diagnostics.
- Backbuffer state is assumed. Wrap swapchain buffers in state-tracked frame
  resources before resize and multi-pass presentation.
- The offscreen smoke target copies to the swapchain. Keep this only if the real
  post-process graph needs it; otherwise present from the final render target
  path with no ornamental copy.

## Required Before Real Pass Migration

- Upload ring capacity policy and overflow reporting for real frame data.
- Debug names for D3D12 resources, descriptors by owner, and command lists.
- Backbuffer state tracking and resize-safe recreation.

## Rule

Do not port Aquarium's scene/froxel/history passes into ad hoc D3D12 resource
creation. Build the resource machine first, then move pixels through it.
